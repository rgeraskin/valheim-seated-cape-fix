using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SeatedCapeFix;
using Xunit;

/// <summary>
/// Catches a game update that breaks a Harmony patch. The compiler checks only that a patched method's name exists
/// (nameof against the publicized game); Harmony finds the target and binds the patch's parameters by name only when
/// the game loads, and one failure there stops the plugin's patching. Each patch is resolved here the way Harmony does
/// it (class and method attributes merged, a method declared on that type, <c>TargetMethod</c>, <c>Prepare</c>) against
/// the game the tests build with (CI: the newest dedicated server, fetched weekly), and its injected parameters are
/// checked against the target. Plain reflection: HarmonyX's AccessTools fails its type initializer on .NET 10.
/// </summary>
public class PatchTargetTests
{
    const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static readonly string[] KindNames = { "Prefix", "Postfix", "Transpiler", "Finalizer" };

    // Harmony's injected parameters that don't name a parameter of the original
    static readonly HashSet<string> Special = new()
    {
        "__instance", "__state", "__result", "__resultRef", "__originalMethod", "__args", "__runOriginal", "__exception",
    };

    /// <summary>Prefix/Postfix/Transpiler/Finalizer by attribute or by name; null for a helper.</summary>
    static string Kind(MethodInfo m)
    {
        if (m.GetCustomAttribute<HarmonyPrefix>() != null) return "Prefix";
        if (m.GetCustomAttribute<HarmonyPostfix>() != null) return "Postfix";
        if (m.GetCustomAttribute<HarmonyTranspiler>() != null) return "Transpiler";
        if (m.GetCustomAttribute<HarmonyFinalizer>() != null) return "Finalizer";
        return KindNames.Contains(m.Name) ? m.Name : null;
    }

    static IEnumerable<(Type type, MethodInfo method)> PatchMethods() =>
        from type in typeof(Plugin).Assembly.GetTypes()
        where type.GetCustomAttributes<HarmonyPatch>(false).Any()
        from m in type.GetMethods(AllDeclared)
        where m.IsStatic && Kind(m) != null
        select (type, m);

    public static IEnumerable<object[]> Patches() => PatchMethods().Select(p => new object[] { $"{p.type.FullName}.{p.method.Name}" });

    [Fact]
    public void AllPatchesAreFound() => Assert.True(PatchMethods().Count() >= 1, "patches found: " + PatchMethods().Count());

    [Theory]
    [MemberData(nameof(Patches))]
    public void PatchBindsToTheGame(string patch)
    {
        var (type, method) = PatchMethods().Single(p => $"{p.type.FullName}.{p.method.Name}" == patch);
        var errors = new List<string>();

        var prepare = type.GetMethod("Prepare", AllDeclared);
        if (prepare != null && prepare.GetParameters().Length == 0 && prepare.ReturnType == typeof(bool) && !(bool)prepare.Invoke(null, null))
            errors.Add("Prepare() returns false: the patch would be skipped");

        var original = Original(type, method);
        string kind = Kind(method);
        errors.AddRange(CheckParameters(method, kind, original));
        if (kind == "Prefix" && method.ReturnType != typeof(void) && method.ReturnType != typeof(bool))
            errors.Add($"Prefix returns {method.ReturnType.Name} (void or bool)");
        Assert.True(errors.Count == 0, $"{patch} → {original.DeclaringType.Name}.{original.Name}: {string.Join("; ", errors)}");
    }

    /// <summary>The patch's target as Harmony finds it: the class's attributes, then the method's, later ones filling in; a declared method.</summary>
    static MethodBase Original(Type type, MethodInfo patch)
    {
        var target = type.GetMethod("TargetMethod", AllDeclared);
        if (target != null)
            return (MethodBase)target.Invoke(null, null) ?? throw new Exception($"{type.Name}.TargetMethod() returns null");

        Type declaringType = null;
        string methodName = null;
        Type[] argumentTypes = null;
        MethodType? methodType = null;
        foreach (var info in type.GetCustomAttributes<HarmonyPatch>(false).Concat(patch.GetCustomAttributes<HarmonyPatch>(false)).Select(a => a.info))
        {
            declaringType = info.declaringType ?? declaringType;
            methodName = info.methodName ?? methodName;
            argumentTypes = info.argumentTypes ?? argumentTypes;
            methodType = info.methodType ?? methodType;
        }
        string name = $"{type.Name}.{patch.Name}";
        Assert.True(declaringType != null && methodName != null, $"{name}: no type or method name in its [HarmonyPatch]");
        Assert.True(methodType == null || methodType == MethodType.Normal, $"{name}: method type {methodType} isn't checked here: extend the test");
        MethodInfo method;
        try
        {
            method = argumentTypes == null
                ? declaringType.GetMethod(methodName, AllDeclared)
                : declaringType.GetMethod(methodName, AllDeclared, null, argumentTypes, null);
        }
        catch (AmbiguousMatchException)
        {
            throw new Exception($"{name}: {declaringType.Name}.{methodName} has overloads now: name the argument types in [HarmonyPatch]");
        }
        return method ?? throw new Exception($"{name}: {declaringType.Name}.{methodName}"
            + (argumentTypes == null ? "" : $"({string.Join(", ", argumentTypes.Select(a => a.Name))})")
            + " isn't declared there (gone in a game update, or moved to a base class)");
    }

    static IEnumerable<string> CheckParameters(MethodInfo patch, string kind, MethodBase original)
    {
        if (kind == "Transpiler") yield break;
        var originalParams = original.GetParameters();
        var returnType = (original as MethodInfo)?.ReturnType ?? typeof(void);
        foreach (var p in patch.GetParameters())
        {
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
            if (p.Name == "__instance")
            {
                if (original.IsStatic) yield return "__instance on a static method";
                else if (!type.IsAssignableFrom(original.DeclaringType)) yield return $"__instance is {type.Name}, the method is on {original.DeclaringType.Name}";
            }
            else if (p.Name == "__result")
            {
                if (returnType == typeof(void)) yield return "__result on a void method";
                else if (!type.IsAssignableFrom(returnType)) yield return $"__result is {type.Name}, the method returns {returnType.Name}";
            }
            else if (p.Name.StartsWith("___"))
            {
                if (FieldUp(original.DeclaringType, p.Name.Substring(3)) == null) yield return $"field {p.Name.Substring(3)} is gone from {original.DeclaringType.Name}";
            }
            else if (Special.Contains(p.Name))
            {
            }
            else if (p.Name.StartsWith("__") && int.TryParse(p.Name.Substring(2), out int index))
            {
                if (index >= originalParams.Length) yield return $"{p.Name}: the method has {originalParams.Length} parameters";
            }
            else
            {
                var match = originalParams.FirstOrDefault(o => o.Name == p.Name);
                if (match == null)
                {
                    yield return $"parameter {p.Name} is gone (the method has: {string.Join(", ", originalParams.Select(o => o.Name))})";
                    continue;
                }
                var originalType = match.ParameterType.IsByRef ? match.ParameterType.GetElementType() : match.ParameterType;
                if (!type.IsAssignableFrom(originalType)) yield return $"parameter {p.Name} is {originalType.Name} now, the patch takes {type.Name}";
            }
        }
    }

    // Harmony's ___field injection looks up the class hierarchy
    static FieldInfo FieldUp(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, AllFields | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
}
