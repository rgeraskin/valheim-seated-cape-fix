using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace SeatedCapeFix;

/// <summary>
/// Keeps capes (and other cloth) out of seats: while a player sits, their cloth also collides with the seat under them,
/// so it lies on the seat and falls over its back edge instead of hanging through it.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "rgeraskin.SeatedCapeFix";
    public const string Name = "SeatedCapeFix";
    public const string Version = "0.1.0";

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> DebugLog;

    void Awake()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Cloth collides with the seat while sitting.");
        DebugLog = Config.Bind("Debug", "Log", false, "Log the seat each sitting player's cloth is given.");
        Log = Logger;
        new Harmony(Guid).PatchAll();
        PreJit(typeof(CapeSeat));
        PreJit(typeof(Perf));
    }

    /// <summary>
    /// Compiles the fix's methods now, while the game loads: compiled on first use instead, the first time anyone sits
    /// down in a session took ~10 ms more (a dropped frame).
    /// </summary>
    static void PreJit(Type type)
    {
        foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (m.IsAbstract || m.ContainsGenericParameters) continue;
            try { m.MethodHandle.GetFunctionPointer(); }
            catch (Exception e) { Log.LogDebug($"pre-JIT {m.Name}: {e.Message}"); }
        }
    }

    internal static BepInEx.Logging.ManualLogSource Log;
}

[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
static class PlayerAwakePatch
{
    static void Postfix(Player __instance) => __instance.gameObject.AddComponent<CapeSeat>();
}
