using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using MagicaCloth2;
using UnityEngine;

namespace SeatedCapeFix;

/// <summary>
/// On every player (local and remote). Cloth on a character collides only with the colliders it was given (the body's),
/// never with the world, so a sitting player's cape falls straight through the seat. While the player sits, this gives
/// their cloth a support shaped like what is under and behind them, found by raycasts in the player's own space: rows of
/// side to side capsules over the seat or floor, each as wide as the surface there, ending where the seat ends (the cape
/// falls over the edge) or at a wall (a plane keeps the cape in front of it). It fits any seat (chairs, benches, ship
/// helms, carts, sleds) without knowing it.
/// </summary>
public class CapeSeat : MonoBehaviour
{
    /// <summary>How the surface behind the hips ends.</summary>
    public enum Back
    {
        /// <summary>The seat ends (a bench, a stool, a chair): the cape falls over its back edge.</summary>
        Drop,
        /// <summary>A wall rises (a sled's or a cart's back, a ship's stern): the cape gathers in front of it.</summary>
        Wall,
        /// <summary>The floor goes on (a deck): the cape lies on it.</summary>
        Floor,
        /// <summary>
        /// A backrest right behind the hips (a chair, a throne): the cape goes behind it as in the vanilla game, which hides
        /// it best; squeezed in front of it the cape has no room and pokes out under the seat.
        /// </summary>
        Backrest,
        /// <summary>No seat under the hips (a saddle on a creature, a seat without colliders): a short edge just behind the hips.</summary>
        Guess,
    }

    // tunables (the dev harness changes them live)
    /// <summary>The seat is the first surface this far under the hips bone.</summary>
    internal static float SeatSearch = 0.5f;
    /// <summary>From the hips bone down to the bottom of the seated body, when no seat is found.</summary>
    internal static float ButtDepth = 0.12f;
    /// <summary>How far behind the hips the surface is followed.</summary>
    internal static float MaxBack = 0.8f;
    /// <summary>Distance between rows.</summary>
    internal static float RowStep = 0.1f;
    /// <summary>Distance between surface samples (back and to the sides).</summary>
    internal static float SideStep = 0.05f;
    /// <summary>Half the widest row.</summary>
    internal static float HalfWidth = 0.45f;
    /// <summary>A surface further than this below or above the last one ends the support.</summary>
    internal static float Bump = 0.06f;
    /// <summary>Radius of the row capsules: large enough that neighbouring rows make an almost flat top.</summary>
    internal static float Radius = 0.15f;
    /// <summary>The back edge when no seat is found.</summary>
    internal static float GuessEdge = 0.25f;
    /// <summary>Surface steps closer than this to the hips are the seat's own shape (a backrest's foot): passed over.</summary>
    internal static float MinWall = 0.2f;
    /// <summary>A wall closer than this to the hips is a backrest: no support (the cape goes behind it).</summary>
    internal static float Backrest = 0.3f;
    /// <summary>Walls are looked for this high above the surface (a lower lip doesn't stop the cape).</summary>
    internal static float WallHeight = 0.12f;
    /// <summary>
    /// A floor narrower than this behind the hips ends like a seat's edge, unless walls rise on both sides (a hull
    /// narrowing to the stern): then they hold the cape in.
    /// </summary>
    internal static float MinFloorWidth = 0.4f;
    /// <summary>Hull walls are looked for at WallHeight and this high above the floor (two heights give their slope).</summary>
    internal static float HullWallHeight = 0.45f;
    /// <summary>How far to the side hull walls are looked for.</summary>
    internal static float HullReach = 0.8f;
    /// <summary>Surface rays start this high above the level they look for: walls up to this height are seen.</summary>
    internal static float WallProbe = 1.0f;
    /// <summary>Cloth rests this far above the surfaces (its thickness).</summary>
    internal static float Lift = 0.01f;

    /// <summary>Seconds between probes while the player sits.</summary>
    internal static float Interval = 0.5f;

    const int MaxRows = 12;
    static int s_mask;
    /// <summary>The frame of the last scheduled probe: one per frame for all players, so sitting players never add up in one frame.</summary>
    static int s_probeFrame = -1;

    Player m_player;
    VisEquipment m_vis;
    Transform m_hips;
    GameObject m_root;
    readonly List<MagicaCapsuleCollider> m_rows = new List<MagicaCapsuleCollider>();
    MagicaPlaneCollider m_wall, m_floor, m_left, m_right;
    ColliderComponent[] m_all = new ColliderComponent[0];
    readonly List<ColliderComponent> m_use = new List<ColliderComponent>();
    bool m_on, m_wasVisible;
    float m_nextProbe;

    // kept between probes, so a probe allocates nothing
    readonly List<float> m_rowsD = new List<float>(), m_rowsY = new List<float>(), m_rowsL = new List<float>(), m_rowsR = new List<float>();
    readonly List<Vector3> m_wallL = new List<Vector3>(), m_wallR = new List<Vector3>();
    bool m_hull;
    readonly List<MagicaCloth> m_cloths = new List<MagicaCloth>();
    // what Apply last gave the cloth: the same again changes nothing
    readonly List<MagicaCloth> m_appliedCloths = new List<MagicaCloth>();
    readonly List<ColliderComponent> m_applied = new List<ColliderComponent>();
    readonly HashSet<List<ColliderComponent>> m_changed = new HashSet<List<ColliderComponent>>();
    float m_hipsY, m_widthMin, m_widthMax;
    int m_rowsUsed;
    bool m_noCloth;

    /// <summary>The last probe, for diagnostics (formatted only when asked for).</summary>
    public string Info => !m_on ? "off"
        : m_noCloth ? "no cloth"
        : Kind == Back.Guess ? string.Format(CultureInfo.InvariantCulture, "Guess seat at {0:0.000} (hips {1:0.000}) back {2:0.00}", SeatHeight, m_hipsY, BackDistance)
        : string.Format(CultureInfo.InvariantCulture, "{0} seat at {1:0.000} (hips {2:0.000}) back {3:0.00} rows {4} width {5:0.00}..{6:0.00}",
            Kind, SeatHeight, m_hipsY, BackDistance, m_rowsUsed, m_widthMin, m_widthMax).Replace(" width 0.00..0.00", "") + (m_hull ? " hull" : "");
    public bool On => m_on;
    public bool SeatFound => Kind != Back.Guess;
    public Back Kind { get; private set; }
    /// <summary>In the player's space.</summary>
    public float SeatHeight { get; private set; }
    /// <summary>From the hips back to the edge, the wall or the end of the probe.</summary>
    public float BackDistance { get; private set; }

    void Awake()
    {
        m_player = GetComponent<Player>();
        m_vis = GetComponent<VisEquipment>();
    }

    void LateUpdate()
    {
        // the animator's "sitting" tag: chairs, helms, emote sit; true for other players too (their animator is synced)
        bool want = Plugin.Enabled.Value && m_player && m_player.m_nview && m_player.m_nview.IsValid() && m_player.IsSitting() && FindHips();
        if (want != m_on)
        {
            // sitting down probes at once, whatever else: the cape must not fall through the seat meanwhile
            if (want) Begin();
            else End();
            return;
        }
        if (!m_on) return;
        // the later probes only follow changes, which matter only where someone sees the cape: off screen the
        // colliders stay as they are (they move with the player), and the player coming into view is probed at once
        bool visible = Visible();
        if (visible && !m_wasVisible) m_nextProbe = 0f;
        m_wasVisible = visible;
        if (!visible || Time.time < m_nextProbe || s_probeFrame == Time.frameCount) return;
        s_probeFrame = Time.frameCount;
        m_nextProbe = Time.time + Interval;
        Refresh();
    }

    void OnDestroy()
    {
        if (m_on) Sync(null);
    }

    bool FindHips()
    {
        if (m_hips) return true;
        return m_vis && m_vis.m_boneMap.TryGetValue("Hips", out m_hips) && m_hips;
    }

    /// <summary>The body or a cloth is drawn by some camera this frame (shadows count).</summary>
    bool Visible()
    {
        if (m_vis && m_vis.m_bodyModel && m_vis.m_bodyModel.isVisible) return true;
        foreach (var cloth in m_cloths)
        {
            if (!cloth) continue;
            foreach (var r in cloth.SerializeData.sourceRenderers)
                if (r && r.isVisible) return true;
        }
        return false;
    }

    void Begin()
    {
        m_on = true;
        if (!m_root) Build();
        Refresh();
        m_wasVisible = Visible();
        m_nextProbe = Time.time + Interval;
        if (Plugin.DebugLog.Value) Plugin.Log.LogInfo($"{m_player.GetPlayerName()} sits: {Info}");
    }

    void End()
    {
        m_on = false;
        long t0 = Stopwatch.GetTimestamp();
        Sync(null);
        Perf.AddEnd(Stopwatch.GetTimestamp() - t0);
        if (Plugin.DebugLog.Value) Plugin.Log.LogInfo($"{m_player.GetPlayerName()} stands up");
    }

    /// <summary>Probes and gives the cloth its colliders; a player without cloth isn't probed at all.</summary>
    void Refresh()
    {
        long t0 = Stopwatch.GetTimestamp();
        Collect();
        m_noCloth = m_cloths.Count == 0;
        if (m_noCloth) m_use.Clear();
        else Probe();
        long t1 = Stopwatch.GetTimestamp();
        Apply(m_use);
        Perf.Add(t1 - t0, Stopwatch.GetTimestamp() - t1);
    }

    /// <summary>The colliders, made once per player and kept: MagicaCloth drops a collider only while it still exists.</summary>
    void Build()
    {
        m_root = new GameObject("SeatedCapeFix");
        m_root.transform.SetParent(transform, false);
        // MagicaCloth scales collider sizes by lossyScale.x
        var s = transform.lossyScale.x;
        if (Mathf.Abs(s) > 1e-4f) m_root.transform.localScale = Vector3.one / s;
        for (int i = 0; i < MaxRows; i++)
        {
            var row = Child("SeatRow" + i).AddComponent<MagicaCapsuleCollider>();
            row.direction = MagicaCapsuleCollider.Direction.X;
            row.alignedOnCenter = true;
            m_rows.Add(row);
        }
        // a plane pushes cloth to its up side
        m_floor = Child("SeatFloor").AddComponent<MagicaPlaneCollider>();
        m_wall = Child("SeatWall").AddComponent<MagicaPlaneCollider>();
        m_wall.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // faces forward
        m_left = Child("SeatHullLeft").AddComponent<MagicaPlaneCollider>();
        m_right = Child("SeatHullRight").AddComponent<MagicaPlaneCollider>();
        var all = new List<ColliderComponent>(m_rows) { m_floor, m_wall, m_left, m_right };
        m_all = all.ToArray();
    }

    GameObject Child(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(m_root.transform, false);
        return go;
    }

    static int Mask => s_mask != 0 ? s_mask
        : s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

    /// <summary>
    /// Finds the seat under the hips, then walks back row by row while the surface stays level (within Bump of the row
    /// before), measuring each row's width; stops where the surface drops away, rises into a wall, or MaxBack. All in the
    /// player's own space: the seat's attach point places and turns the player, so up is the seat's up even on a rolling
    /// ship or a tilted sled.
    /// </summary>
    void Probe()
    {
        var hips = transform.InverseTransformPoint(m_hips.position);
        m_hipsY = hips.y;
        m_use.Clear();
        int used = 0;
        m_rowsUsed = 0;
        m_widthMin = m_widthMax = 0f;
        m_hull = false;
        if (!Down(new Vector3(hips.x, hips.y + 0.05f, hips.z), 0.05f + SeatSearch, out float seat))
        {
            // a short edge just behind the hips at the bottom of the body
            Kind = Back.Guess;
            SeatHeight = hips.y - ButtDepth;
            BackDistance = GuessEdge;
            float r = GuessEdge * 0.5f;
            Row(used++, hips.x, SeatHeight - r, hips.z - (GuessEdge - r), r, 0.5f);
            m_rowsUsed = used;
            return;
        }

        var kind = Back.Floor;
        float back = MaxBack;
        // a thin wall (a sled's or a cart's back board) can sit between two samples: look for it straight back too
        float wall = Horizontal(new Vector3(hips.x, seat + WallHeight, hips.z - MinWall), Vector3.back, MaxBack - MinWall) + MinWall;
        float last = seat;
        var rowsD = m_rowsD;
        var rowsY = m_rowsY;
        var rowsL = m_rowsL;
        var rowsR = m_rowsR;
        rowsD.Clear();
        rowsY.Clear();
        rowsL.Clear();
        rowsR.Clear();
        int n = 0;
        for (float d = 0f; d <= MaxBack + 1e-3f; d += SideStep, n++)
        {
            if (d >= wall)
            {
                kind = Back.Wall;
                back = wall;
                break;
            }
            float z = hips.z - d;
            bool found = Surface(hips.x, z, last, out float y);
            if (!found || y < last - Bump)
            {
                kind = Back.Drop;
                back = Mathf.Max(d - SideStep * 0.5f, 0.05f);
                break;
            }
            if (y > last + Bump)
            {
                // the seat's own backrest: go on behind it at the seat's level
                if (d < MinWall) continue;
                kind = Back.Wall;
                back = d - SideStep * 0.5f;
                break;
            }
            last = y;
            if (n % Mathf.Max(1, Mathf.RoundToInt(RowStep / SideStep)) != 0) continue;
            // a floor that narrows to less than a cape ends there (the cape falls over it), unless walls rise on both
            // sides: a hull narrowing to the stern, where a fall would take the cape through the hull
            if (!m_hull && d >= MinWall && Narrow(hips.x, z, y))
            {
                if (SideWalls(hips.x, z, y)) m_hull = true;
                else
                {
                    kind = Back.Drop;
                    back = d - RowStep * 0.5f;
                    break;
                }
            }
            rowsD.Add(d);
            rowsY.Add(y);
        }
        SeatHeight = seat;
        BackDistance = back;

        if (kind == Back.Wall && back < Backrest)
            kind = Back.Backrest;
        Kind = kind;
        if (kind == Back.Drop)
        {
            // a seat that ends: rows of capsules over it, the last one's back at the edge, so the cape lies on the seat
            // and falls over the edge (a plane would hold it out level in the air behind the seat)
            float rad = Radius;
            for (int i = 0; i < rowsD.Count && used < MaxRows; i++)
            {
                // the row's full width, measured only for the rows that are made
                float z = hips.z - rowsD[i];
                rowsL.Add(Side(hips.x, z, rowsY[i], -1f));
                rowsR.Add(Side(hips.x, z, rowsY[i], 1f));
                float d = rowsD[i];
                if (d > back - rad) d = back - rad;
                Row(used++, hips.x + (rowsR[i] - rowsL[i]) * 0.5f, rowsY[i] - rad, hips.z - d, rad, rowsL[i] + rowsR[i]);
                if (d >= back - rad) break;
            }
        }
        else if (kind != Back.Backrest)
        {
            // a floor: planes (cloth is pushed to their front side, so even cloth that fell through the floor while the
            // player sat down comes back up): the floor and the wall behind
            // tilted to the floor's slope behind (sitting on a hillside)
            float slope = 0f;
            int k = rowsD.Count - 1;
            if (k > 0 && rowsD[k] > rowsD[0]) slope = (rowsY[k] - rowsY[0]) / (rowsD[k] - rowsD[0]);
            Place(m_floor.transform, new Vector3(0f, seat + Lift, hips.z), Quaternion.FromToRotation(Vector3.up, new Vector3(0f, 1f, slope).normalized));
            m_use.Add(m_floor);
            if (kind == Back.Wall)
            {
                Place(m_wall.transform, new Vector3(0f, 0f, hips.z - back + Lift), m_wall.transform.localRotation);
                m_use.Add(m_wall);
            }
            // walls on both sides (a hull, a sled's or a cart's sides): planes too, so the cape lies in the trough they
            // make with the floor instead of hanging out through them
            if (!m_hull && rowsD.Count > 0 && SideWalls(hips.x, hips.z - rowsD[0], rowsY[0])) m_hull = true;
            if (m_hull && HullWalls(hips.x, rowsD, rowsY, hips.z) && Clear(m_left.transform, hips) && Clear(m_right.transform, hips))
            {
                m_use.Add(m_left);
                m_use.Add(m_right);
            }
            else m_hull = false;
        }
        else m_hull = false;
        m_rowsUsed = used;
        m_widthMin = rowsL.Count > 0 ? Min(rowsL, rowsR) : 0f;
        m_widthMax = rowsL.Count > 0 ? Max(rowsL, rowsR) : 0f;
    }

    /// <summary>Moves a collider only when it moved: an unchanged transform costs the cloth nothing.</summary>
    static void Place(Transform t, Vector3 pos, Quaternion rot)
    {
        if (t.localPosition != pos) t.localPosition = pos;
        if (t.localRotation != rot) t.localRotation = rot;
    }

    static float Min(List<float> l, List<float> r)
    {
        float m = float.MaxValue;
        for (int i = 0; i < l.Count; i++) m = Mathf.Min(m, l[i] + r[i]);
        return m;
    }

    static float Max(List<float> l, List<float> r)
    {
        float m = 0f;
        for (int i = 0; i < l.Count; i++) m = Mathf.Max(m, l[i] + r[i]);
        return m;
    }

    /// <summary>A side to side capsule whose top is at <paramref name="centerY"/> + r (+ Lift).</summary>
    void Row(int i, float x, float centerY, float z, float r, float width)
    {
        var c = m_rows[i];
        Place(c.transform, new Vector3(x, centerY + Lift, z), Quaternion.identity);
        var size = new Vector3(r, r, Mathf.Max(width, 0.1f));
        if (c.GetSize() != size)
        {
            c.SetSize(r, r, size.z);
            c.UpdateParameters();
        }
        m_use.Add(c);
    }

    /// <summary>
    /// The top surface at (x, z) near <paramref name="level"/>: rays from well above, so a wall's top is found too (a ray
    /// that starts inside a collider doesn't hit it).
    /// </summary>
    bool Surface(float x, float z, float level, out float y) =>
        Down(new Vector3(x, level + WallProbe, z), WallProbe + Bump * 2f, out y);

    /// <summary>
    /// How far to one side the surface stays at the row's level, short of a side wall; stops early once
    /// <paramref name="other"/> + the width so far reaches <paramref name="enough"/> (then it returns that partial width).
    /// </summary>
    float Side(float x, float z, float level, float dir, float other = 0f, float enough = float.MaxValue)
    {
        float limit = Horizontal(new Vector3(x, level + WallHeight, z), Vector3.right * dir, HalfWidth);
        float w = 0f;
        for (float s = SideStep; s <= HalfWidth + 1e-3f && s < limit; s += SideStep)
        {
            if (!Surface(x + dir * s, z, level, out float y) || Mathf.Abs(y - level) > Bump) break;
            w = s;
            if (other + w >= enough) break;
        }
        return w;
    }

    /// <summary>Walls rise on both sides of the row: level rays at two heights hit something on each side.</summary>
    bool SideWalls(float x, float z, float level)
    {
        for (float dir = -1f; dir <= 1f; dir += 2f)
        {
            if (Horizontal(new Vector3(x, level + WallHeight, z), Vector3.right * dir, HullReach) == float.MaxValue) return false;
            if (Horizontal(new Vector3(x, level + HullWallHeight, z), Vector3.right * dir, HullReach) == float.MaxValue) return false;
        }
        return true;
    }

    /// <summary>
    /// Fits a plane to each hull wall through the points level rays at two heights hit on every row, and places the side
    /// planes facing in. False when a side has too few points or isn't wall-like.
    /// </summary>
    bool HullWalls(float x, List<float> rowsD, List<float> rowsY, float hipsZ)
    {
        m_wallL.Clear();
        m_wallR.Clear();
        for (int i = 0; i < rowsD.Count; i++)
        {
            float z = hipsZ - rowsD[i];
            for (int k = 0; k < 2; k++)
            {
                float y = rowsY[i] + (k == 0 ? WallHeight : HullWallHeight);
                float l = Horizontal(new Vector3(x, y, z), Vector3.left, HullReach);
                float r = Horizontal(new Vector3(x, y, z), Vector3.right, HullReach);
                if (l != float.MaxValue) m_wallL.Add(new Vector3(x - l, y, z));
                if (r != float.MaxValue) m_wallR.Add(new Vector3(x + r, y, z));
            }
        }
        return FitWall(m_wallL, 1f, m_left.transform) && FitWall(m_wallR, -1f, m_right.transform);
    }

    /// <summary>A side plane leaves room for the body: the hips (at seat height) are well on its inner side.</summary>
    bool Clear(Transform plane, Vector3 hips)
    {
        var p = new Vector3(hips.x, SeatHeight + 0.1f, hips.z);
        return Vector3.Dot(p - plane.localPosition, plane.localRotation * Vector3.up) > 0.15f;
    }

    /// <summary>
    /// Least squares x = a·y + b·z + c through the points; the plane faces <paramref name="inward"/> (+1: towards +x).
    /// </summary>
    static bool FitWall(List<Vector3> pts, float inward, Transform plane)
    {
        if (pts.Count < 2) return false;
        // normal equations over (y, z, 1)
        double syy = 0, syz = 0, sy = 0, szz = 0, sz = 0, n = pts.Count, sxy = 0, sxz = 0, sx = 0;
        foreach (var p in pts)
        {
            syy += p.y * p.y; syz += p.y * p.z; sy += p.y; szz += p.z * p.z; sz += p.z;
            sxy += p.x * p.y; sxz += p.x * p.z; sx += p.x;
        }
        double a, b, c;
        double det = syy * (szz * n - sz * sz) - syz * (syz * n - sz * sy) + sy * (syz * sz - szz * sy);
        if (System.Math.Abs(det) > 1e-9)
        {
            a = (sxy * (szz * n - sz * sz) - syz * (sxz * n - sz * sx) + sy * (sxz * sz - szz * sx)) / det;
            b = (syy * (sxz * n - sx * sz) - sxy * (syz * n - sz * sy) + sy * (syz * sx - sxz * sy)) / det;
            c = (syy * (szz * sx - sz * sxz) - syz * (syz * sx - sy * sxz) + sxy * (syz * sz - szz * sy)) / det;
        }
        else
        {
            // one row: no slope along z
            double d2 = syy * n - sy * sy;
            if (System.Math.Abs(d2) < 1e-9) return false;
            a = (sxy * n - sy * sx) / d2;
            b = 0;
            c = (sx - a * sy) / n;
        }
        var normal = new Vector3(inward, -(float)a * inward, -(float)b * inward).normalized;
        // a wall, not a floor or a ceiling
        if (Mathf.Abs(normal.y) > 0.8f) return false;
        float yc = (float)(sy / n), zc = (float)(sz / n);
        var on = new Vector3((float)(a * yc + b * zc + c), yc, zc);
        Place(plane, on + normal * Lift, Quaternion.FromToRotation(Vector3.up, normal));
        return true;
    }

    /// <summary>
    /// The row is narrower than MinFloorWidth (left + right width). Measures only until the answer is known: a width only
    /// grows as the sides are walked further, so once it reaches MinFloorWidth the full widths would too (same floats).
    /// </summary>
    bool Narrow(float x, float z, float level)
    {
        float l = Side(x, z, level, -1f, 0f, MinFloorWidth);
        if (l >= MinFloorWidth) return false;
        float r = Side(x, z, level, 1f, l, MinFloorWidth);
        return l + r < MinFloorWidth;
    }

    /// <summary>A level ray in the player's space: the distance to the first hit, or float.MaxValue.</summary>
    float Horizontal(Vector3 localOrigin, Vector3 localDir, float dist)
    {
        var t = transform;
        return Physics.Raycast(t.TransformPoint(localOrigin), t.TransformDirection(localDir), out var hit, dist, Mask, QueryTriggerInteraction.Ignore)
            ? hit.distance : float.MaxValue;
    }

    /// <summary>A downward ray in the player's space; the hit's height in that space.</summary>
    bool Down(Vector3 localOrigin, float dist, out float localY)
    {
        localY = 0f;
        var t = transform;
        if (!Physics.Raycast(t.TransformPoint(localOrigin), -t.up, out var hit, dist, Mask, QueryTriggerInteraction.Ignore))
            return false;
        localY = t.InverseTransformPoint(hit.point).y;
        return true;
    }

    /// <summary>The player's cloth now (it can be equipped or taken off while sitting).</summary>
    void Collect() => GetComponentsInChildren(true, m_cloths);

    /// <summary>
    /// Gives every cloth on the player the colliders in use and takes the others away; null takes all of them away.
    /// </summary>
    void Sync(List<ColliderComponent> use)
    {
        Collect();
        Apply(use);
    }

    void Apply(List<ColliderComponent> use)
    {
        // the same cloth with the same colliders: nothing to do (the colliders' moves reach the cloth by themselves)
        int n = use?.Count ?? 0;
        if (Same(m_cloths, m_appliedCloths) && n == m_applied.Count)
        {
            bool same = true;
            for (int i = 0; i < n && same; i++) same = use[i] == m_applied[i];
            if (same) return;
        }
        // a cloth whose own list was empty shares the player's list (VisEquipment.m_clothColliders)
        m_changed.Clear();
        foreach (var cloth in m_cloths)
        {
            if (!cloth) continue;
            var list = cloth.SerializeData.colliderCollisionConstraint.colliderList;
            foreach (var c in m_all)
            {
                if (!c) continue;
                bool want = use != null && use.Contains(c);
                if (want && !list.Contains(c)) { list.Add(c); m_changed.Add(list); }
                else if (!want && list.Remove(c)) m_changed.Add(list);
            }
        }
        foreach (var cloth in m_cloths)
            if (cloth && m_changed.Contains(cloth.SerializeData.colliderCollisionConstraint.colliderList)) cloth.SetParameterChange();
        m_appliedCloths.Clear();
        m_appliedCloths.AddRange(m_cloths);
        m_applied.Clear();
        if (use != null) m_applied.AddRange(use);
    }

    static bool Same(List<MagicaCloth> a, List<MagicaCloth> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}

/// <summary>What the probes cost in play (read by the dev harness): count, total and worst time.</summary>
static class Perf
{
    internal static int Count, Ends;
    internal static long Ticks, MaxTicks, MaxProbe, MaxApply, MaxEnd, First = -1;

    internal static void Add(long probe, long apply)
    {
        long ticks = probe + apply;
        if (First < 0) First = ticks;
        Count++;
        Ticks += ticks;
        if (ticks > MaxTicks) MaxTicks = ticks;
        if (probe > MaxProbe) MaxProbe = probe;
        if (apply > MaxApply) MaxApply = apply;
    }

    internal static void AddEnd(long ticks)
    {
        Ends++;
        if (ticks > MaxEnd) MaxEnd = ticks;
    }

    internal static void Reset()
    {
        Count = Ends = 0;
        Ticks = MaxTicks = MaxProbe = MaxApply = MaxEnd = 0;
    }

    internal static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
