using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using MagicaCloth2;
using SeatedCapeFix;
using UnityEngine;
using ValheimDevHarness;

namespace SeatedCapeFixHarness;

/// <summary>
/// Test commands for SeatedCapeFix: the cloth's colliders, a clipping measure of the rendered cape (vertices under the
/// seat, inside the legs), sitting on things (chairs, a ship's helm, emotes), and the fix's tunables.
/// </summary>
[BepInPlugin("rgeraskin.SeatedCapeFixHarness", "SeatedCapeFix DevHarness", "0.0.1")]
[BepInDependency(Harness.Guid)]
[BepInDependency(Plugin.Guid)]
public class SeatedCapeFixHarness : BaseUnityPlugin
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    void Awake()
    {
        Harness.Register("cape", "cape: the player's cloth (type, collision mode, colliders) and the seat the fix found", _ => Dump());
        Harness.Register("capeclip", "capeclip [label]: rendered cloth vertices under the seat / inside the legs / forward under the hips", a => Harness.Log(Clip(a.Length > 0 ? a[0] : "")));
        Harness.Register("sit", "sit [name] [radius=6] [seat]: sit on the nearest chair/bench/throne (Chair component; name filter on the object, seat filter on the Chair's own name)", Sit);
        Harness.Register("helm", "helm [radius=15]: take the nearest ship's helm the way the game does (attach to its control point)", Helm);
        Harness.Register("use", "use <TypeName> [radius=6]: E on the nearest interactable component of that type (SledSeat: a Winter Sled or cart seat)", a =>
        {
            float radius = a.Length > 1 ? Harness.F(a[1]) : 6f;
            var p = Player.m_localPlayer;
            var it = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(m => m is Interactable && m.GetType().Name.Equals(a[0], StringComparison.OrdinalIgnoreCase) && Vector3.Distance(m.transform.position, p.transform.position) < radius)
                .OrderBy(m => Vector3.Distance(m.transform.position, p.transform.position)).FirstOrDefault();
            if (!it) { Harness.Log("! no " + a[0]); return; }
            Harness.Log($"use {it.transform.root.name}/{it.name}: {((Interactable)it).Interact(p, false, false)}");
        });
        Harness.Register("clearinv", "clearinv: remove every item that isn't equipped (equip gives a new one each time)", _ =>
        {
            var inv = Player.m_localPlayer.GetInventory();
            foreach (var item in inv.GetAllItems().Where(i => !i.m_equipped).ToList()) inv.RemoveItem(item);
            Harness.Log($"inventory: {inv.GetAllItems().Count} items left");
        });
        Harness.Register("stand", "stand: get up (AttachStop) and stop an emote", _ =>
        {
            var p = Player.m_localPlayer;
            p.AttachStop();
            p.StopEmote();
        });
        Harness.Register("emote", "emote <name>: start a looping emote (sit, ...)", a => Player.m_localPlayer.StartEmote(a[0], false));
        Harness.Register("seat", "seat [<field> <value>]: the fix's tunables (Mode Edge|Plane, ButtDepth, MaxEdge, Width, ...); no args lists them", SeatTune);
        Harness.Register("capeshots", "capeshots <prefix> [dist=2.2]: back, side (hips level), back three-quarter low, front three-quarter low shots of the player", Shots);
        Harness.Register("capev", "capev: bounds of the cape's vertices (player space) read four ways, to pick the right one", _ => VertBounds());
        Harness.Register("probeback", "probeback [step=0.1] [n=8]: every collider under points behind the hips (player space): name, layer, type, height, rendered", ProbeBack);
        Harness.Register("target", "target [local | remote [name]]: the player cape/capeclip/capev/bones/capeshots measure (remote: the nearest other player)", Target);
        Harness.Register("probetime", "probetime [n=300]: time the fix's probe + collider sync on the target player n times (after a warm-up): avg/median/p99/max µs and bytes allocated per call", ProbeTime);
        Harness.Register("frametimes", "frametimes [seconds=10] [label]: frame times over that long: avg, median, p99, max ms and frames over 2x the median", FrameTimes);
        Harness.Register("perf", "perf [reset]: the probes the fix ran in play so far (all players): count, average and worst ms", PerfStats);
        Harness.Register("hide", "hide [off]: no camera draws the target player (forceRenderingOff on every renderer), as if off screen", a =>
        {
            bool off = a.Length > 0 && a[0] == "off";
            foreach (var r in Who.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !off;
            Harness.Log(off ? "shown" : "hidden");
        });
        Harness.Register("poke", "poke: cloth vertices on the far side of a collider from the hips (through a hull or a seat): count, centre and extent in player space, the colliders crossed", _ => Poke());
        Harness.Register("pokeshot", "pokeshot <name> [yaw=180] [pitch=20] [dist=1] [fov]: a screenshot aimed at the last poke's centre (player space, kept from a poke that found any), yaw from the player's front", PokeShot);
        Harness.Register("hidebody", "hidebody [off]: hide everything of the target player but its cloth (a cut-away view of the cape)", a =>
        {
            bool off = a.Length > 0 && a[0] == "off";
            var cloth = new HashSet<Renderer>(Who.GetComponentsInChildren<MagicaCloth>(true).SelectMany(c => c.SerializeData.sourceRenderers).Where(r => r));
            foreach (var r in Who.GetComponentsInChildren<Renderer>(true)) if (!cloth.Contains(r)) r.forceRenderingOff = !off;
            Harness.Log(off ? "body shown" : $"body hidden ({cloth.Count} cloth renderers kept)");
        });
        Harness.Register("fshot", "fshot <name> [yaw=180] [pitch=20] [dist=2] [height=0.6] [fov] [back=0]: like camshot (focus moved back by back m), but the camera follows the target every frame (a drifting ship), with an optional field of view (zoom)", FollowShot);
        Harness.Register("scanback", "scanback [xstep=0.15] [zstep=0.1] [n=12]: every collider under a grid behind the hips (player space): per point the hits top to bottom as name@height", ScanBack);
        Harness.Register("mountcols", "mountcols: the colliders of the nearest saddled creature (player space) and the target's cape vertices inside each", MountCols);
        Harness.Register("seats", "seats [filter]: every prefab with a Chair, a ship helm or a saddle (mods' too), with its attach animation", Seats);
        Harness.Register("seattest", "seattest <prefab> [shot]: spawn a seat prefab (ships on the water), sit on each kind of its seats (or ride it), measure the cape (shot: also off/on screenshots), remove it", SeatTest);
        Harness.Register("rideon", "rideon [name] [radius=30]: tame and saddle the nearest saddle-able creature (Lox, Asksvin, modded mounts) and mount it", Ride);
        Harness.Register("bones","bones: hips/legs bone heights and offsets in the player's space", _ => Bones());
        Harness.AddState(() =>
        {
            var p = Who;
            var cs = p ? p.GetComponent<CapeSeat>() : null;
            return cs ? $"capeseat {(p == Player.m_localPlayer ? "local" : "remote " + p.GetPlayerName())} sitting={p.IsSitting()} attached={p.IsAttached()} {cs.Info}" : "capeseat -";
        });
    }

    static Player s_target;
    /// <summary>The player the measuring commands look at: the local one, or another player chosen with `target`.</summary>
    static Player Who => s_target ? s_target : Player.m_localPlayer;

    static void Target(string[] a)
    {
        s_target = null;
        if (a.Length > 0 && a[0] != "local")
        {
            var me = Player.m_localPlayer;
            s_target = Player.GetAllPlayers().Where(o => o != me && (a.Length < 2 || o.GetPlayerName().Equals(a[1], StringComparison.OrdinalIgnoreCase)))
                .OrderBy(o => Vector3.Distance(o.transform.position, me.transform.position)).FirstOrDefault();
            if (!s_target) { Harness.Log("! no other player"); return; }
        }
        Harness.Log($"target {Who.GetPlayerName()} (local={Who == Player.m_localPlayer}) at {Vector3.Distance(Who.transform.position, Player.m_localPlayer.transform.position).ToString("0.0", Inv)} m");
    }

    static string V(Vector3 v) => string.Format(Inv, "({0:0.000} {1:0.000} {2:0.000})", v.x, v.y, v.z);

    static void Dump()
    {
        var p = Who;
        var t = p.transform;
        Harness.Log($"sitting={p.IsSitting()} attached={p.IsAttached()} anim={p.m_attachAnimation} root scale={t.lossyScale.x:0.###}");
        var cs = p.GetComponent<CapeSeat>();
        Harness.Log("fix: " + (cs ? cs.Info : "no component"));
        foreach (var cloth in p.GetComponentsInChildren<MagicaCloth>(true))
        {
            var sd = cloth.SerializeData;
            var cc = sd.colliderCollisionConstraint;
            Harness.Log($"cloth {Actions.PathOf(cloth.transform).Replace(Actions.PathOf(t) + "/", "")} type={sd.clothType} active={cloth.isActiveAndEnabled} collision={cc.mode} friction={cc.friction:0.##} gravity={sd.gravity:0.##} renderers={string.Join(",", sd.sourceRenderers.Where(r => r).Select(r => r.name + ":" + r.GetType().Name))}");
            foreach (var c in cc.colliderList)
            {
                if (!c) { Harness.Log("  (destroyed collider)"); continue; }
                var size = c.GetSize() * c.GetScale();
                string extra = c is MagicaCapsuleCollider cap ? $" dir={cap.direction} centered={cap.alignedOnCenter}" : "";
                Harness.Log($"  {c.GetType().Name.Replace("Magica", "")} on {c.transform.parent?.name}/{c.name} at {V(t.InverseTransformPoint(c.transform.TransformPoint(c.center)))} size(world)={V(size)}{extra}");
            }
        }
    }

    static void VertBounds()
    {
        var p = Who;
        var t = p.transform;
        var mesh = new Mesh();
        foreach (var cloth in p.GetComponentsInChildren<MagicaCloth>())
            foreach (var r in cloth.SerializeData.sourceRenderers)
            {
                if (!(r is SkinnedMeshRenderer smr)) continue;
                Harness.Log($"smr {smr.name} scale={V(smr.transform.lossyScale)} root={smr.rootBone?.name} bones={smr.bones.Length} mesh={smr.sharedMesh?.name} verts={smr.sharedMesh?.vertexCount} enabled={smr.enabled} visible={smr.isVisible}");
                void B(string how, Vector3[] verts, Matrix4x4 m)
                {
                    var mn = Vector3.one * 1e9f; var mx = -mn;
                    foreach (var v in verts) { var l = t.InverseTransformPoint(m.MultiplyPoint3x4(v)); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); }
                    Harness.Log($"  {how}: min {V(mn)} max {V(mx)}");
                }
                smr.BakeMesh(mesh, true);
                B("bake+scale, TRS", mesh.vertices, Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one));
                smr.BakeMesh(mesh, false);
                B("bake, localToWorld", mesh.vertices, smr.transform.localToWorldMatrix);
                B("bake, TRS", mesh.vertices, Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one));
                B("shared, localToWorld", smr.sharedMesh.vertices, smr.transform.localToWorldMatrix);
            }
        UnityEngine.Object.Destroy(mesh);
    }

    static void ProbeBack(string[] a)
    {
        float step = a.Length > 0 ? Harness.F(a[0]) : 0.1f;
        int count = a.Length > 1 ? Harness.I(a[1]) : 8;
        var p = Player.m_localPlayer;
        var t = p.transform;
        var hips = t.InverseTransformPoint(p.GetComponent<VisEquipment>().m_boneMap["Hips"].position);
        for (int i = 0; i < count; i++)
        {
            var o = t.TransformPoint(new Vector3(hips.x, hips.y + 0.6f, hips.z - i * step));
            var hits = Physics.RaycastAll(o, -t.up, 2f, Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            Harness.Log(string.Format(Inv, "d={0:0.00}: ", i * step) + string.Join(" | ", hits.Select(h =>
            {
                var c = h.collider;
                bool rendered = c.GetComponent<Renderer>() || c.GetComponentInChildren<MeshRenderer>();
                return string.Format(Inv, "{0} y={1:0.000} {2} {3}{4}{5}", Actions.PathOf(c.transform).Replace(Actions.PathOf(c.transform.root) + "/", c.transform.root.name.Replace("(Clone)", "") + ":"),
                    t.InverseTransformPoint(h.point).y, LayerMask.LayerToName(c.gameObject.layer), c.GetType().Name.Replace("Collider", ""),
                    c is MeshCollider mc && !mc.convex ? "" : "", rendered ? " R" : "");
            })));
        }
    }

    static void Bones()
    {
        var p = Who;
        var t = p.transform;
        var map = p.GetComponent<VisEquipment>().m_boneMap;
        foreach (var n in new[] { "Hips", "Spine", "Spine1", "Spine2", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" })
            if (map.TryGetValue(n, out var b))
                Harness.Log($"{n} {V(t.InverseTransformPoint(b.position))} scale={b.lossyScale.x:0.###}");
            else Harness.Log($"{n} missing");
    }

    /// <summary>
    /// Bakes every cloth renderer and measures in the player's space: vertices below the seat surface where the seat is
    /// (cast down from just above the seat), vertices inside the thighs/shins, vertices forward of the hips and below the
    /// seat (the cape swinging between the legs). Seat height from the fix's probe, or its own probe with the fix off.
    /// </summary>
    static string Clip(string label)
    {
        var p = Who;
        var t = p.transform;
        var map = p.GetComponent<VisEquipment>().m_boneMap;
        Vector3 B(string n) => t.InverseTransformPoint(map[n].position);
        var hips = B("Hips");
        // thigh, shin, thigh, shin: from, to, radius
        var legA = new[] { B("LeftUpLeg"), B("LeftLeg"), B("RightUpLeg"), B("RightLeg") };
        var legB = new[] { B("LeftLeg"), B("LeftFoot"), B("RightLeg"), B("RightFoot") };
        var legR = new[] { 0.08f, 0.06f, 0.08f, 0.06f };
        // the seat: same probe as the fix (its numbers when it's on)
        var cs = p.GetComponent<CapeSeat>();
        bool seatFound;
        float seat;
        if (cs && cs.On) { seatFound = cs.SeatFound; seat = cs.SeatHeight; }
        else seatFound = DownLocal(t, new Vector3(hips.x, hips.y + 0.05f, hips.z), 0.45f, out seat);

        int n = 0, under = 0, inLeg = 0, fwd = 0;
        float minY = float.MaxValue, maxBack = 0f, maxDepth = 0f;
        var mesh = new Mesh();
        foreach (var cloth in p.GetComponentsInChildren<MagicaCloth>())
        {
            foreach (var r in cloth.SerializeData.sourceRenderers)
            {
                if (!r || !r.isVisible && !r.enabled) continue;
                Vector3[] verts;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr)
                {
                    // the bones carry a scale of ~95 that the unscaled bake already holds: no scale on top
                    smr.BakeMesh(mesh, false);
                    verts = mesh.vertices;
                    m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    verts = mf.sharedMesh.vertices;
                    m = r.transform.localToWorldMatrix;
                }
                foreach (var v in verts)
                {
                    var w = m.MultiplyPoint3x4(v);
                    var l = t.InverseTransformPoint(w);
                    n++;
                    minY = Mathf.Min(minY, l.y);
                    maxBack = Mathf.Max(maxBack, hips.z - l.z);
                    // under the seat: below its surface where a ray from just above finds that surface (its flat part, not a bevelled edge)
                    if (seatFound && l.y < seat - 0.04f && DownLocal(t, new Vector3(l.x, seat + 0.1f, l.z), 0.18f, out var top) && Mathf.Abs(top - seat) < 0.03f)
                    {
                        under++;
                        maxDepth = Mathf.Max(maxDepth, seat - l.y);
                    }
                    for (int i = 0; i < legA.Length; i++)
                        if (SegDist(l, legA[i], legB[i]) < legR[i]) { inLeg++; break; }
                    if (l.z > hips.z + 0.05f && l.y < (seatFound ? seat : hips.y - 0.12f)) fwd++;
                }
            }
        }
        UnityEngine.Object.Destroy(mesh);
        return string.Format(Inv, "capeclip {0} verts={1} underSeat={2} (deepest {10:0.00}) inLegs={3} fwdUnder={4} seat={5}{6:0.000} lowest={7:0.000} back={8:0.00} | {9}",
            label, n, under, inLeg, fwd, seatFound ? "" : "~", seat, minY, maxBack, cs ? cs.Info : "-", maxDepth);
    }

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(p, a + ab * k);
    }

    static int s_mask;
    static int Mask => s_mask != 0 ? s_mask : s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

    static bool DownLocal(Transform t, Vector3 o, float dist, out float y)
    {
        y = 0f;
        if (!Physics.Raycast(t.TransformPoint(o), -t.up, out var hit, dist, Mask, QueryTriggerInteraction.Ignore)) return false;
        y = t.InverseTransformPoint(hit.point).y;
        return true;
    }

    static void Sit(string[] a)
    {
        string name = a.Length > 0 ? a[0] : "";
        float radius = a.Length > 1 ? Harness.F(a[1]) : 6f;
        var p = Player.m_localPlayer;
        var chair = FindObjectsByType<Chair>(FindObjectsSortMode.None)
            .Where(c => c.transform.root.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && Vector3.Distance(c.transform.position, p.transform.position) < radius
                && (a.Length < 3 || c.name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
        if (!chair) { Harness.Log("! no chair " + name); return; }
        p.AttachStart(chair.m_attachPoint, null, false, false, chair.m_inShip, chair.m_attachAnimation, chair.m_detachOffset);
        Harness.Log($"sitting on {chair.transform.root.name}/{chair.name} ({chair.m_attachAnimation})");
    }

    /// <summary>
    /// Spawns a seat prefab in front of the player (ships on the water), sits on each kind of seat it has (one per
    /// component type and attach animation; saddle-able creatures are ridden), measures the cape and optionally takes a
    /// following screenshot, then removes it.
    /// </summary>
    static IEnumerator SeatTest(string[] a)
    {
        var prefab = ZNetScene.instance.GetPrefab(a[0]);
        if (!prefab) { Harness.Log("! no prefab " + a[0]); yield break; }
        bool shots = a.Length > 1 && a[1] == "shot";
        var p = Player.m_localPlayer;
        var fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized;
        bool ship = prefab.GetComponent<Ship>();
        var pos = p.transform.position + fwd * (ship ? 6f : 2.5f);
        float ground = ZoneSystem.instance.GetGroundHeight(pos);
        pos.y = ship ? Mathf.Max(ground, ZoneSystem.instance.m_waterLevel) + 0.3f : ground + 0.05f;
        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(fwd));
        yield return new WaitForSecondsRealtime(3f);
        var seats = new List<KeyValuePair<string, Action>>();
        foreach (var c in go.GetComponentsInChildren<Chair>())
        {
            var chair = c;
            seats.Add(new KeyValuePair<string, Action>("chair " + chair.name + " " + chair.m_attachAnimation, () =>
                p.AttachStart(chair.m_attachPoint, null, false, false, chair.m_inShip, chair.m_attachAnimation, chair.m_detachOffset)));
        }
        foreach (var c in go.GetComponentsInChildren<ShipControlls>())
        {
            var sc = c;
            seats.Add(new KeyValuePair<string, Action>("helm " + sc.m_attachAnimation, () =>
            {
                sc.m_nview.GetZDO().Set(ZDOVars.s_user, p.GetPlayerID());
                p.StartDoodadControl(sc);
                p.AttachStart(sc.m_attachPoint, null, false, false, true, sc.m_attachAnimation, sc.m_detachOffset);
            }));
        }
        var seen = new HashSet<string>();
        if (go.GetComponent<Tameable>() && go.GetComponent<Tameable>().m_saddle)
        {
            yield return Ride(new[] { prefab.name, "10" });
            yield return Measure(prefab.name, "saddle", shots);
            p.AttachStop();
        }
        foreach (var kv in seats)
        {
            // one seat per kind: the rest are copies (a ship's benches)
            string kind = System.Text.RegularExpressions.Regex.Replace(kv.Key, @"^chair .* (\S+)$", "chair $1");
            if (!seen.Add(kind)) continue;
            kv.Value();
            yield return Measure(prefab.name, kv.Key, shots);
            p.AttachStop();
            p.StopEmote();
            yield return new WaitForSecondsRealtime(1f);
        }
        ZNetScene.instance.Destroy(go);
        yield return new WaitForSecondsRealtime(1f);
    }

    static IEnumerator Measure(string prefab, string seat, bool shots)
    {
        yield return new WaitForSecondsRealtime(5f);
        var p = Player.m_localPlayer;
        var cs = p.GetComponent<CapeSeat>();
        string label = (prefab + "_" + seat.Split(' ')[0]).Replace(' ', '_');
        Harness.Log($"seattest {prefab} | {seat} | sitting={p.IsSitting()} anim={p.m_attachAnimation} | {cs?.Info}");
        Harness.Log(Clip(label));
        Poke();
        if (shots)
        {
            yield return FollowShot(new[] { "seat_on_" + label, "150", "15", "2.5", "0.4" });
            Plugin.Enabled.Value = false;
            yield return new WaitForSecondsRealtime(5f);
            Harness.Log(Clip(label + "_off"));
            Poke();
            yield return FollowShot(new[] { "seat_off_" + label, "150", "15", "2.5", "0.4" });
            Plugin.Enabled.Value = true;
            yield return new WaitForSecondsRealtime(3f);
        }
    }

    static void Helm(string[] a)
    {
        float radius = a.Length > 0 ? Harness.F(a[0]) : 15f;
        var p = Player.m_localPlayer;
        var sc = FindObjectsByType<ShipControlls>(FindObjectsSortMode.None)
            .Where(c => Vector3.Distance(c.transform.position, p.transform.position) < radius)
            .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
        if (!sc) { Harness.Log("! no ship helm"); return; }
        sc.m_nview.GetZDO().Set(ZDOVars.s_user, p.GetPlayerID());
        p.StartDoodadControl(sc);
        p.AttachStart(sc.m_attachPoint, null, false, false, true, sc.m_attachAnimation, sc.m_detachOffset);
        Harness.Log($"at the helm of {sc.GetComponentInParent<Ship>()?.name} ({sc.m_attachAnimation})");
    }

    /// <summary>Every prefab a player can sit on or ride: chairs, helms, saddle-able creatures (mods' prefabs included).</summary>
    static void Seats(string[] a)
    {
        string filter = a.Length > 0 ? a[0] : "";
        int n = 0;
        foreach (var go in ZNetScene.instance.m_prefabs)
        {
            if (!go || go.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var kinds = new List<string>();
            int chairs = go.GetComponentsInChildren<Chair>(true).Length;
            if (chairs > 0) kinds.Add("chair" + (chairs > 1 ? "x" + chairs : "") + " " + string.Join(",", go.GetComponentsInChildren<Chair>(true).Select(c => c.m_attachAnimation).Distinct()));
            var sc = go.GetComponentsInChildren<ShipControlls>(true);
            if (sc.Length > 0) kinds.Add("helm " + string.Join(",", sc.Select(c => c.m_attachAnimation).Distinct()));
            var t = go.GetComponent<Tameable>();
            if (t && t.m_saddle) kinds.Add("saddle " + t.m_saddle.m_attachAnimation);
            if (kinds.Count == 0) continue;
            n++;
            Harness.Log($"  {go.name}: {string.Join("; ", kinds)}");
        }
        Harness.Log($"{n} seat prefabs");
    }

    /// <summary>Tames and saddles the nearest saddle-able creature, then mounts it the way Sadle's granted request does.</summary>
    static IEnumerator Ride(string[] a)
    {
        string name = a.Length > 0 ? a[0] : "";
        float radius = a.Length > 1 ? Harness.F(a[1]) : 30f;
        var p = Player.m_localPlayer;
        var t = FindObjectsByType<Tameable>(FindObjectsSortMode.None)
            .Where(x => x.m_saddle && x.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && Vector3.Distance(x.transform.position, p.transform.position) < radius)
            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
        if (!t) { Harness.Log("! no saddle-able creature " + name); yield break; }
        var c = t.GetComponent<Character>();
        if (!c.IsTamed()) { c.SetTamed(true); yield return new WaitForSecondsRealtime(0.5f); }
        if (!t.HaveSaddle()) { t.m_nview.GetZDO().Set(ZDOVars.s_haveSaddleHash, true); t.SetSaddle(true); yield return new WaitForSecondsRealtime(0.5f); }
        var s = t.m_saddle;
        s.m_nview.GetZDO().Set(ZDOVars.s_user, p.GetPlayerID());
        p.StartDoodadControl(s);
        p.AttachStart(s.m_attachPoint, c.gameObject, false, false, false, s.m_attachAnimation, s.m_detachOffset);
        Harness.Log($"riding {t.name} ({s.m_attachAnimation})");
    }

    static void SeatTune(string[] a)
    {
        var fields = typeof(CapeSeat).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Where(f => !f.IsLiteral && f.Name != "s_mask").ToArray();
        if (a.Length >= 2)
        {
            var f = fields.FirstOrDefault(x => x.Name.Equals(a[0], StringComparison.OrdinalIgnoreCase));
            if (f == null) { Harness.Log("! no tunable " + a[0]); return; }
            f.SetValue(null, f.FieldType.IsEnum ? Enum.Parse(f.FieldType, a[1], true) : Convert.ChangeType(a[1], f.FieldType, Inv));
        }
        Harness.Log(string.Join(" ", fields.Select(f => f.Name + "=" + Convert.ToString(f.GetValue(null), Inv))));
    }

    static void ProbeTime(string[] a)
    {
        int n = a.Length > 0 ? int.Parse(a[0], Inv) : 300;
        var cs = Who.GetComponent<CapeSeat>();
        if (!cs || !cs.On) { Harness.Log("! the target isn't sitting with the fix on"); return; }
        for (int i = 0; i < 5; i++) { cs.Probe(); cs.Sync(cs.m_use); }
        var us = new double[n];
        long mem0 = GC.GetTotalMemory(false);
        for (int i = 0; i < n; i++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            cs.Probe();
            cs.Sync(cs.m_use);
            us[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
        }
        long bytes = GC.GetTotalMemory(false) - mem0;
        Array.Sort(us);
        Harness.Log(string.Format(Inv, "probetime n={0} avg={1:0.0}us median={2:0.0}us p99={3:0.0}us max={4:0.0}us alloc={5}B/call | {6}",
            n, us.Average(), us[n / 2], us[(int)(n * 0.99)], us[n - 1], bytes < 0 ? "? (a GC ran)" : (bytes / n).ToString(Inv), cs.Info));
    }

    // only in builds with SeatedCapeFix.Perf: a separate method, so the other commands work with an older SeatedCapeFix
    static void PerfStats(string[] a)
    {
        Harness.Log(string.Format(Inv, "perf probes={0} avg={1:0.000}ms max={2:0.000}ms (probe {3:0.000} apply {4:0.000}) first={5:0.000}ms | stand-ups={6} max={7:0.000}ms",
            Perf.Count, Perf.Count > 0 ? Perf.Ms(Perf.Ticks) / Perf.Count : 0, Perf.Ms(Perf.MaxTicks), Perf.Ms(Perf.MaxProbe), Perf.Ms(Perf.MaxApply), Perf.Ms(Perf.First), Perf.Ends, Perf.Ms(Perf.MaxEnd)));
        if (a.Length > 0 && a[0] == "reset") Perf.Reset();
    }

    static Vector3 s_pokeCentre = new Vector3(0f, 0.3f, -0.3f);
    static readonly List<Vector3> s_pokeVerts = new List<Vector3>();

    static IEnumerator FollowShot(string[] a)
    {
        var t = Who.transform;
        float yaw = a.Length > 1 ? Harness.F(a[1]) : 180f, pitch = a.Length > 2 ? Harness.F(a[2]) : 20f;
        float dist = a.Length > 3 ? Harness.F(a[3]) : 2f, height = a.Length > 4 ? Harness.F(a[4]) : 0.6f;
        float back = a.Length > 6 ? Harness.F(a[6]) : 0f;
        var gc = GameCamera.instance;
        var cam = gc.m_camera;
        float fov = cam.fieldOfView;
        gc.enabled = false;
        try
        {
            if (a.Length > 5) cam.fieldOfView = Harness.F(a[5]);
            void Place()
            {
                var center = t.position + t.up * height - t.forward * back;
                var dir = Quaternion.AngleAxis(t.eulerAngles.y + 180f + yaw, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                cam.transform.position = center - dir * dist;
                cam.transform.rotation = Quaternion.LookRotation(dir);
            }
            for (int i = 0; i < 30; i++)
            {
                Place();
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            Place();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var path = System.IO.Path.Combine(Harness.Dir, "shots", a[0] + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Harness.Log("shot " + path);
        }
        finally
        {
            cam.fieldOfView = fov;
            gc.enabled = true;
        }
    }

    static IEnumerator PokeShot(string[] a)
    {
        var t = Who.transform;
        var focus = new GameObject("pokeshot focus");
        focus.transform.SetPositionAndRotation(t.TransformPoint(s_pokeCentre), t.rotation);
        var cam = GameCamera.instance.m_camera;
        float fov = cam.fieldOfView;
        if (a.Length > 4) cam.fieldOfView = Harness.F(a[4]);
        try
        {
            yield return Actions.CameraShot(a[0], focus.transform, a.Length > 1 ? Harness.F(a[1]) : 180f, a.Length > 2 ? Harness.F(a[2]) : 20f, a.Length > 3 ? Harness.F(a[3]) : 1f, 0f);
        }
        finally
        {
            cam.fieldOfView = fov;
            UnityEngine.Object.Destroy(focus);
        }
    }

    static void ScanBack(string[] a)
    {
        float xs = a.Length > 0 ? Harness.F(a[0]) : 0.15f, zs = a.Length > 1 ? Harness.F(a[1]) : 0.1f;
        int n = a.Length > 2 ? int.Parse(a[2], Inv) : 12;
        var p = Who;
        var t = p.transform;
        var hips = t.InverseTransformPoint(p.GetComponent<VisEquipment>().m_boneMap["Hips"].position);
        for (int i = 0; i < n; i++)
        {
            float z = hips.z - i * zs;
            var line = new List<string>();
            for (float x = -3 * xs; x <= 3 * xs + 1e-3f; x += xs)
            {
                var o = t.TransformPoint(new Vector3(hips.x + x, hips.y + 1f, z));
                var hits = Physics.RaycastAll(o, -t.up, 3f, Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
                    .Where(h => !h.collider.transform.IsChildOf(t)).Take(3)
                    .Select(h => h.collider.name.Replace("boat.", "b") + "@" + t.InverseTransformPoint(h.point).y.ToString("0.00", Inv));
                line.Add(string.Join("/", hits.DefaultIfEmpty("-")));
            }
            Harness.Log(string.Format(Inv, "back {0:0.00}: ", hips.z - z) + string.Join("  ", line));
        }
    }

    static void Poke()
    {
        var p = Who;
        var t = p.transform;
        var hipsW = p.GetComponent<VisEquipment>().m_boneMap["Hips"].position;
        var mesh = new Mesh();
        int n = 0, outN = 0;
        Vector3 sum = Vector3.zero, min = Vector3.one * 99f, max = -Vector3.one * 99f;
        var names = new Dictionary<string, int>();
        s_pokeVerts.Clear();
        foreach (var cloth in p.GetComponentsInChildren<MagicaCloth>())
            foreach (var r in cloth.SerializeData.sourceRenderers)
            {
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in mesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    n++;
                    if (!Physics.Linecast(hipsW, w, out var hit, Mask, QueryTriggerInteraction.Ignore)) continue;
                    outN++;
                    var l = t.InverseTransformPoint(w);
                    s_pokeVerts.Add(l);
                    sum += l;
                    min = Vector3.Min(min, l);
                    max = Vector3.Max(max, l);
                    string key = hit.collider.transform.root.name + "/" + hit.collider.name;
                    names[key] = names.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
        UnityEngine.Object.Destroy(mesh);
        if (outN > 0) s_pokeCentre = sum / outN;
        Harness.Log(outN == 0 ? $"poke 0/{n}" : $"poke {outN}/{n} centre {V(sum / outN)} min {V(min)} max {V(max)} crossed {string.Join(", ", names.Select(kv => kv.Key + ":" + kv.Value))}");
    }

    /// <summary>The colliders of the creature the target rides (or the nearest one with a saddle) and the cape vertices inside each.</summary>
    static void MountCols(string[] a)
    {
        var p = Who;
        var t = p.transform;
        var c = FindObjectsByType<Tameable>(FindObjectsSortMode.None).Where(x => x.m_saddle)
            .OrderBy(x => Vector3.Distance(x.transform.position, t.position)).FirstOrDefault();
        if (!c) { Harness.Log("! no saddled creature"); return; }
        var cols = c.GetComponentsInChildren<Collider>().Where(x => x.enabled && !x.isTrigger).ToArray();
        var inside = new int[cols.Length];
        var mesh = new Mesh();
        int n = 0, any = 0;
        float low = 99f;
        foreach (var cloth in p.GetComponentsInChildren<MagicaCloth>())
            foreach (var r in cloth.SerializeData.sourceRenderers)
            {
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (var v in mesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    n++;
                    bool hit = false;
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (cols[i] is MeshCollider mc && !mc.convex) continue;
                        if ((cols[i].ClosestPoint(w) - w).sqrMagnitude < 1e-8f) { inside[i]++; hit = true; }
                    }
                    if (hit) { any++; low = Mathf.Min(low, t.InverseTransformPoint(w).y); }
                }
            }
        UnityEngine.Object.Destroy(mesh);
        Harness.Log($"mount {c.name}: {any}/{n} cape vertices inside its colliders" + (any > 0 ? $", lowest inside {low.ToString("F2", Inv)}" : ""));
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            string shape = col is CapsuleCollider cc ? $"capsule r={cc.radius.ToString("F2", Inv)} h={cc.height.ToString("F2", Inv)} dir={cc.direction} centre={V(t.InverseTransformPoint(col.transform.TransformPoint(cc.center)))}"
                : col is SphereCollider sc ? $"sphere r={sc.radius.ToString("F2", Inv)} centre={V(t.InverseTransformPoint(col.transform.TransformPoint(sc.center)))}"
                : col is BoxCollider bc ? $"box size={V(bc.size)} centre={V(t.InverseTransformPoint(col.transform.TransformPoint(bc.center)))}"
                : col is MeshCollider mc ? $"mesh convex={mc.convex}" : col.GetType().Name;
            Harness.Log($"  {col.transform.name} layer={LayerMask.LayerToName(col.gameObject.layer)} scale={V(col.transform.lossyScale)} {shape} inside={inside[i]}");
        }
    }

    static IEnumerator FrameTimes(string[] a)
    {
        float secs = a.Length > 0 ? Harness.F(a[0]) : 10f;
        string label = a.Length > 1 ? a[1] : "";
        var ms = new List<float>();
        float end = Time.realtimeSinceStartup + secs;
        yield return null;
        while (Time.realtimeSinceStartup < end)
        {
            ms.Add(Time.unscaledDeltaTime * 1000f);
            yield return null;
        }
        ms.Sort();
        float med = ms[ms.Count / 2];
        Harness.Log(string.Format(Inv, "frametimes {0} frames={1} avg={2:0.00}ms median={3:0.00}ms p99={4:0.00}ms max={5:0.00}ms over2x={6}",
            label, ms.Count, ms.Average(), med, ms[(int)(ms.Count * 0.99)], ms[ms.Count - 1], ms.Count(x => x > 2 * med)));
    }

    static IEnumerator Shots(string[] a)
    {
        var p = Who.transform;
        string prefix = a[0];
        // yaw from the player's front: 180 = behind
        float dist = a.Length > 1 ? Harness.F(a[1]) : 2.2f;
        yield return Actions.CameraShot(prefix + "_back", p, 180f, 12f, dist, 0.8f);
        yield return Actions.CameraShot(prefix + "_side", p, 90f, 3f, dist, 0.55f);
        yield return Actions.CameraShot(prefix + "_back3q", p, 140f, 2f, dist, 0.4f);
        yield return Actions.CameraShot(prefix + "_front3q", p, 35f, 0f, dist, 0.35f);
    }
}
