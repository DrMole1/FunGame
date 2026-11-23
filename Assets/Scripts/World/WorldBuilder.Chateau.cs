using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (chapitre 11.3) : environnement Château. Une zone de 60 m de haut, damier noir et blanc partout, plafond supérieur gris.
// - Rempart extérieur tiré parmi 12 gabarits (ligne brisée sur la grille de 4 x 4 de la zone). V0.7.12 : 10 m de haut, 6 m
//   d'épaisseur, creux (rez-de-chaussée, et chemin de ronde à l'air libre entre les créneaux, relié par un escalier intérieur), néons
//   sous les créneaux. Une herse fermée de chaque côté ; un levier sur le chemin de ronde, relié aux deux herses par des câbles noirs,
//   les ouvre pour de bon. Douve de 2 m de profondeur accolée au rempart (côté entrée), large de 6 m et doublée par endroits, où des
//   escaliers (dans le sens de la longueur) permettent d'en sortir ; une chaussée mène à la herse. 3 à 5 escaliers derrière (côté
//   Neutral Room) montent sur le rempart. On ne le franchit autrement qu'en Canon.
// - À partir de la Zone 4, un rempart intérieur autour de la Neutral Room (3 gabarits), aux mêmes propriétés.
// - 6 Tours (30 m, 15 à 20 m de diamètre, créneaux, au moins 15 meurtrières, escalier en colimaçon de 3 m de large jusqu'au sommet,
//   étages à 10 et 20 m une fois sur deux), 1 Donjon au centre (une Tour 50 % plus haute et plus large), 10 Maisons-Fou (15 m, 8 m de
//   diamètre, un lustre au plafond).
// - Entre eux, des salles de 8 à 12 m de haut (côtés de 5 à 20 m), aux toits ceints de créneaux, reliés entre eux et au sol par des
//   escaliers (au moins 8 depuis le sol), et des corridors (15 % de corridors pour 85 % de salles ; 30 % des corridors ont un
//   renfoncement en biseau) : cachots, salles d'armes, salles de banquet, chapelles (V0.7.12 : plus de halls). Chaque entrée a son
//   arche ; aucun objet décoratif ne flotte. Éclairage par des lustres. Les cours (remparts, douves, abords des Tours) sont les
//   endroits sans bâtiment.
public static partial class WorldBuilder
{
    public const float ChateauHeight = 60f;
    const float RampartHeight = 10f, RampartThick = 6f, MoatDepth = 2f, MoatWidth = 6f, GateWidth = 4f, GateHeight = 5f;
    const float TowerHeight = 30f, MaisonHeight = 15f, MaisonDiameter = 8f;

    // Gabarits (coordonnées sur la grille de 4 x 4 de la zone : x d'ouest en est, y du sud (entrée) au nord (Neutral Room)).
    static readonly Vector2[][] OuterRamparts =
    {
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
    };

    static readonly Vector2[][] InnerRamparts =
    {
        new[] { new Vector2(1, 4), new Vector2(1, 3), new Vector2(3, 3), new Vector2(3, 4) },
        new[] { new Vector2(0, 3), new Vector2(3, 3), new Vector2(3, 4) },
        new[] { new Vector2(1, 4), new Vector2(1, 3), new Vector2(4, 3) },
    };

    // Point d'une grille de gabarit (4 x 4) en coordonnées monde (x, z).
    static Vector2 GridPoint(int zone, Vector2 g) => new Vector2(OriginX + g.x * ZoneSize / 4f, ZoneSouth(zone) + g.y * ZoneSize / 4f);

    internal class RampartPlan
    {
        public readonly List<Rect> rects = new List<Rect>();          // emprise de chaque segment (6 m d'épaisseur)
        public readonly List<bool> horizontal = new List<bool>();
        public readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>(), moat = new HashSet<Vector2Int>(), front = new HashSet<Vector2Int>(), back = new HashSet<Vector2Int>();
        public HashSet<Vector2Int> nrSide;                              // cellules du côté de la Neutral Room
        public int gateSegment = -1;
        public Vector2 gate;                                            // milieu du passage (x, z)
        public bool gateAlongX;
        public float gateAt;
        public readonly List<Rect> moatRects = new List<Rect>();       // V0.7.12 : emprise des douves (x, z), accolées au rempart
        public readonly List<(Vector3 foot, Vector3 dir)> moatStairs = new List<(Vector3, Vector3)>();
    }

    static RampartPlan PlanRampart(Maze m, Vector2[] template, System.Random rnd)
    {
        var plan = new RampartPlan();
        float half = RampartThick * 0.5f, west = OriginX, east = OriginX + ZoneSize, south = ZoneSouth(m.zone), north = south + ZoneSize;
        for (int k = 0; k + 1 < template.Length; k++)
        {
            Vector2 a = GridPoint(m.zone, template[k]), b = GridPoint(m.zone, template[k + 1]);
            bool horizontal = Mathf.Abs(a.y - b.y) < 0.01f;
            Rect r = horizontal
                ? Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, a.y - half, Mathf.Max(a.x, b.x) + half, a.y + half)
                : Rect.MinMaxRect(a.x - half, Mathf.Min(a.y, b.y) - half, a.x + half, Mathf.Max(a.y, b.y) + half);
            // Les bouts qui touchent le bord de la zone s'arrêtent au mur d'enceinte.
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, west), Mathf.Max(r.yMin, south), Mathf.Min(r.xMax, east), Mathf.Min(r.yMax, north));
            plan.rects.Add(r);
            plan.horizontal.Add(horizontal);
        }
        // Cellules couvertes par le rempart.
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                Vector3 c = m.Center(i, j);
                var cell = Rect.MinMaxRect(c.x - Cell * 0.5f + 0.05f, c.z - Cell * 0.5f + 0.05f, c.x + Cell * 0.5f - 0.05f, c.z + Cell * 0.5f - 0.05f);
                if (plan.rects.Exists(r => r.Overlaps(cell))) plan.cells.Add(new Vector2Int(i, j));
            }
        // Côté de la Neutral Room : tout ce qu'on atteint depuis elle sans traverser le rempart.
        plan.nrSide = Flood(m, new Vector2Int(m.doorCol, m.N - 2), plan.cells);

        // Passage (herses) : sur le plus long segment horizontal, loin de ses extrémités.
        int best = -1;
        for (int k = 0; k < plan.rects.Count; k++)
            if (plan.horizontal[k] && (best < 0 || plan.rects[k].width > plan.rects[best].width)) best = k;
        if (best < 0) best = 0;
        plan.gateSegment = best;
        Rect gr = plan.rects[best];
        plan.gateAlongX = plan.horizontal[best];
        plan.gateAt = plan.gateAlongX ? Rand(rnd, gr.xMin + 14f, gr.xMax - 14f) : Rand(rnd, gr.yMin + 14f, gr.yMax - 14f);
        plan.gate = plan.gateAlongX ? new Vector2(plan.gateAt, gr.center.y) : new Vector2(gr.center.x, plan.gateAt);

        // V0.7.12 : douves accolées au rempart, du côté de l'entrée de la zone : 6 m de large, doublées par endroits (où descendent
        // les escaliers, dans le sens de la longueur) ; elles épousent les coins ; une chaussée mène à la herse.
        var moat = new List<Rect>();
        var holes = new List<Rect>(plan.rects);
        float run = StairRun(MoatDepth);
        foreach (var (a, b, inward) in OutlineEdges(plan.rects, west, east, south, north))
        {
            Vector2 outward = -inward, mid = (a + b) * 0.5f;
            Vector2 probe = mid + outward * (MoatWidth + 1f);
            if (!m.CellAt(new Vector3(probe.x, 0f, probe.y), out int pi, out int pj) || plan.nrSide.Contains(new Vector2Int(pi, pj))) continue;
            bool alongX = inward.x == 0f;
            float lo = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y), hi = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
            float line = alongX ? a.y : a.x, sign = alongX ? outward.y : outward.x;
            moat.Add(Band(alongX, lo - MoatWidth, hi + MoatWidth, line, sign, MoatWidth));
            // Face du passage : la chaussée, et un endroit doublé de chaque côté de la herse.
            bool gateFace = alongX == plan.gateAlongX && plan.gateAt > lo && plan.gateAt < hi
                && Mathf.Abs(line - (plan.gateAlongX ? (sign > 0f ? gr.yMax : gr.yMin) : (sign > 0f ? gr.xMax : gr.xMin))) < 0.01f;
            var spans = new List<Vector2>();
            if (gateFace)
            {
                holes.Add(Band(alongX, plan.gateAt - GateWidth * 0.5f - 0.6f, plan.gateAt + GateWidth * 0.5f + 0.6f, line, sign, MoatWidth * 2f + 0.5f));
                spans.Add(new Vector2(lo, plan.gateAt - GateWidth * 0.5f - 1.5f));
                spans.Add(new Vector2(plan.gateAt + GateWidth * 0.5f + 1.5f, hi));
            }
            else if (hi - lo > 20f && rnd.NextDouble() < 0.75) spans.Add(new Vector2(lo, hi));
            foreach (var span in spans)
            {
                float room = span.y - span.x - 2f;
                if (room < run + 5f) continue;
                float length = Mathf.Min(room, Rand(rnd, run + 6f, run + 11f));
                float s0 = Rand(rnd, span.x + 1f, span.y - 1f - length), s1 = s0 + length;
                moat.Add(Band(alongX, s0, s1, line, sign, MoatWidth * 2f));
                // Escalier contre le bord extérieur de la partie doublée, le long de la douve : il arrive au sol au bout de cette partie
                // (là où la douve redevient étroite) ; son pied garde de l'espace avant le mur d'en face.
                float across = line + sign * (MoatWidth * 2f - 1.35f);
                bool up = rnd.NextDouble() < 0.5;
                float footAt = up ? s1 - run : s0 + run;
                Vector3 foot = alongX ? new Vector3(footAt, 0f, across) : new Vector3(across, 0f, footAt);
                Vector3 dir = (alongX ? Vector3.right : Vector3.forward) * (up ? 1f : -1f);
                plan.moatStairs.Add((foot, dir));
            }
        }
        // Ni sur le Hub, la Neutral Room et l'entrée de la zone, ni hors de la zone.
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
                if (m.Reserved(i, j) || m.Fixed(i, j) || Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 1)
                {
                    Vector3 c = m.Center(i, j);
                    holes.Add(Rect.MinMaxRect(c.x - Cell * 0.5f, c.z - Cell * 0.5f, c.x + Cell * 0.5f, c.z + Cell * 0.5f));
                }
        var pieces = new List<Rect>();
        foreach (Rect r in moat)
        {
            Rect clipped = Rect.MinMaxRect(Mathf.Max(r.xMin, west), Mathf.Max(r.yMin, south), Mathf.Min(r.xMax, east), Mathf.Min(r.yMax, north));
            if (clipped.width > 0.05f && clipped.height > 0.05f) pieces.Add(clipped);
        }
        // Morceaux disjoints (sols sans recouvrement).
        foreach (Rect r in pieces)
            foreach (Rect p in Subtract(Subtract(new List<Rect> { r }, holes), plan.moatRects))
                if (p.width > 0.3f && p.height > 0.3f) plan.moatRects.Add(p);
        // Un escalier dont la partie doublée a été rognée (Hub, chaussée) n'est gardé que s'il est tout entier dans la douve.
        plan.moatStairs.RemoveAll(st => !InRects(plan.moatRects, st.foot - st.dir * 0.5f) || !InRects(plan.moatRects, st.foot + st.dir * (run - 0.2f)));

        foreach (var c in plan.cells)
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || plan.cells.Contains(n) || m.Reserved(n.x, n.y) || m.Fixed(n.x, n.y)) continue;
                if (plan.nrSide.Contains(n)) plan.back.Add(n);
            }
        // Cellules des douves, et une bande de sol devant elles.
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var c = new Vector2Int(i, j);
                if (plan.cells.Contains(c) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                Vector3 cc = m.Center(i, j);
                var cell = Rect.MinMaxRect(cc.x - Cell * 0.5f + 0.05f, cc.z - Cell * 0.5f + 0.05f, cc.x + Cell * 0.5f - 0.05f, cc.z + Cell * 0.5f - 0.05f);
                if (plan.moatRects.Exists(r => r.Overlaps(cell))) plan.moat.Add(c);
            }
        foreach (var c in plan.moat)
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || plan.cells.Contains(n) || plan.moat.Contains(n) || plan.nrSide.Contains(n) || m.Reserved(n.x, n.y) || m.Fixed(n.x, n.y)) continue;
                plan.front.Add(n);
            }
        plan.moat.Remove(m.start);
        plan.front.Remove(m.start);
        return plan;
    }

    // Bande le long d'une arête (alongX : arête parallèle à X, à z = line), de 'lo' à 'hi', large de 'width' du côté 'sign'.
    static Rect Band(bool alongX, float lo, float hi, float line, float sign, float width)
    {
        float a = line, b = line + sign * width;
        return alongX ? Rect.MinMaxRect(lo, Mathf.Min(a, b), hi, Mathf.Max(a, b)) : Rect.MinMaxRect(Mathf.Min(a, b), lo, Mathf.Max(a, b), hi);
    }

    static bool InRects(List<Rect> rects, Vector3 p) => rects.Exists(r => r.Contains(new Vector2(p.x, p.z)));

    // Contour de la réunion de rectangles disjoints (bords de la zone compris) : (début, fin, direction vers l'intérieur).
    static List<(Vector2 a, Vector2 b, Vector2 inward)> UnionOutline(List<Rect> rects)
    {
        var result = new List<(Vector2, Vector2, Vector2)>();
        foreach (Rect r in rects)
        {
            var edges = new[]
            {
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(0f, 1f)),
                (new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(0f, -1f)),
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(1f, 0f)),
                (new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(-1f, 0f)),
            };
            foreach (var (a, b, inward) in edges)
            {
                bool alongX = inward.x == 0f;
                float line = alongX ? a.y : a.x, outside = alongX ? -inward.y : -inward.x;
                var spans = new List<Vector2> { alongX ? new Vector2(a.x, b.x) : new Vector2(a.y, b.y) };
                // Retire ce qu'un autre rectangle couvre juste de l'autre côté de l'arête.
                float probe = line + outside * 0.02f;
                foreach (Rect o in rects)
                {
                    if (o == r) continue;
                    float cLo = alongX ? o.yMin : o.xMin, cHi = alongX ? o.yMax : o.xMax;
                    if (probe <= cLo || probe >= cHi) continue;
                    float oLo = alongX ? o.xMin : o.yMin, oHi = alongX ? o.xMax : o.yMax;
                    var next = new List<Vector2>();
                    foreach (var s in spans)
                    {
                        if (s.y <= oLo || s.x >= oHi) { next.Add(s); continue; }
                        if (s.x < oLo) next.Add(new Vector2(s.x, oLo));
                        if (s.y > oHi) next.Add(new Vector2(oHi, s.y));
                    }
                    spans = next;
                }
                foreach (var s in spans)
                    if (s.y - s.x > 0.05f)
                        result.Add(alongX ? (new Vector2(s.x, line), new Vector2(s.y, line), inward) : (new Vector2(line, s.x), new Vector2(line, s.y), inward));
            }
        }
        return result;
    }

    // Cellules atteintes depuis 'from' sans passer par 'blocked' (ni par le Hub).
    static HashSet<Vector2Int> Flood(Maze m, Vector2Int from, HashSet<Vector2Int> blocked)
    {
        var seen = new HashSet<Vector2Int>();
        if (!m.InGrid(from.x, from.y)) return seen;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        seen.Add(from);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || seen.Contains(n) || blocked.Contains(n) || m.Reserved(n.x, n.y)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    // Tour ronde posée dans la zone : centre, rayon, hauteur (Tours, Donjon, Maisons-Fou).
    internal class RoundPlan { public Vector3 center; public float radius, height; public int kind; }   // kind : 0 Tour, 1 Donjon, 2 Maison-Fou

    static IEnumerator BuildChateauSteps(ZoneBuild build, Transform parent, int seed, int zone, ZoneRefs previous)
    {
        var rnd = new System.Random(seed * 37 + zone * 6151);
        Maze m = CreateMaze(seed, zone);
        m.terrain = TerrainKind.Chateau;
        m.voidCell = new bool[m.N, m.N];
        m.voidCeil = ChateauHeight;
        m.bevelChance = 0.3f;
        // V0.7.12 : toutes les entrées (salles, corridors, portes des cours) ont leur arche.
        m.archChance = 1f;
        m.archDoorways = true;
        m.ceilingSlab = RoofSlab;
        m.corridorCeil = 8f;
        if (previous != null) m.southHeights = previous.maze.northHeights;

        // ---------- Plan : remparts, Tours, Donjon, Maisons-Fou ----------
        var ramparts = new List<RampartPlan> { PlanRampart(m, OuterRamparts[rnd.Next(OuterRamparts.Length)], rnd) };
        if (zone >= 4) ramparts.Add(PlanRampart(m, InnerRamparts[rnd.Next(InnerRamparts.Length)], rnd));
        void MarkVoid(Vector2Int c) { if (m.InGrid(c.x, c.y) && !m.Reserved(c.x, c.y) && !m.Fixed(c.x, c.y) && c != m.start) m.voidCell[c.x, c.y] = true; }
        foreach (var rp in ramparts)
        {
            foreach (var c in rp.cells) MarkVoid(c);
            foreach (var c in rp.moat) MarkVoid(c);
            foreach (var c in rp.front) MarkVoid(c);
            foreach (var c in rp.back) MarkVoid(c);
        }
        // Une marge de sol libre devant le Hub et devant la Neutral Room.
        bool NearKeep(int i, int j) => Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 2 || m.neutralRoom.Contains(new Vector2Int(i, j))
            || Mathf.Abs(i - m.doorCol) <= 2 && j >= m.N - 5;

        var rounds = new List<RoundPlan>();
        // Emprise libre : ni rempart, ni douve, ni autre bâtiment rond (3 m d'écart), ni les abords du Hub et de la Neutral Room ; les
        // cours au pied des remparts sont permises.
        var rampartArea = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) { rampartArea.UnionWith(rp.cells); rampartArea.UnionWith(rp.moat); }
        bool PlotFree(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            int i0 = Mathf.FloorToInt((center.x - plot - o.x) / Cell), i1 = Mathf.FloorToInt((center.x + plot - o.x) / Cell);
            int j0 = Mathf.FloorToInt((center.z - plot - o.z) / Cell), j1 = Mathf.FloorToInt((center.z + plot - o.z) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (!m.InGrid(i, j) || m.Reserved(i, j) || m.Fixed(i, j) || rampartArea.Contains(new Vector2Int(i, j)) || NearKeep(i, j)) return false;
            foreach (var other in rounds)
                if (Flat(other.center - center) < other.radius + plot + 3f) return false;
            return true;
        }
        void MarkPlot(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            for (int i = Mathf.FloorToInt((center.x - plot - o.x) / Cell); i <= Mathf.FloorToInt((center.x + plot - o.x) / Cell); i++)
                for (int j = Mathf.FloorToInt((center.z - plot - o.z) / Cell); j <= Mathf.FloorToInt((center.z + plot - o.z) / Cell); j++)
                    MarkVoid(new Vector2Int(i, j));
        }
        // Donjon d'abord, au centre de la zone (les 4 parts centrales de la grille de 4 x 4).
        {
            float radius = Rand(rnd, 15f, 20f) * 1.5f * 0.5f;
            Vector2 lo = GridPoint(zone, new Vector2(1f, 1f)), hi = GridPoint(zone, new Vector2(3f, 3f));
            for (int attempt = 0; attempt < 400; attempt++)
            {
                float r = attempt < 300 ? radius : radius * 0.8f;
                var c = new Vector3(Rand(rnd, lo.x + r, hi.x - r), 0f, Rand(rnd, lo.y + r, hi.y - r));
                if (!PlotFree(c, r + 3f)) continue;
                MarkPlot(c, r + 3f);
                rounds.Add(new RoundPlan { center = c, radius = r, height = TowerHeight * 1.5f, kind = 1 });
                break;
            }
        }
        void PlaceRounds(int count, int kind, float minR, float maxR, float height)
        {
            for (int k = 0; k < count; k++)
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    float r = Rand(rnd, minR, maxR);
                    var c = new Vector3(Rand(rnd, OriginX + r + 6f, OriginX + ZoneSize - r - 6f), 0f, Rand(rnd, ZoneSouth(zone) + r + 6f, ZoneSouth(zone) + ZoneSize - r - 6f));
                    if (!PlotFree(c, r + 2f)) continue;
                    MarkPlot(c, r + 2f);
                    rounds.Add(new RoundPlan { center = c, radius = r, height = height, kind = kind });
                    break;
                }
        }
        PlaceRounds(6, 0, 7.5f, 10f, TowerHeight);
        PlaceRounds(10, 2, MaisonDiameter * 0.5f, MaisonDiameter * 0.5f, MaisonHeight);
        yield return null;

        // ---------- Salles et corridors (labyrinthe dans les cellules restantes) ----------
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 4, roomCoverage = 0.85f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = 12f, floors = new[] { 0f }, ceilings = new[] { 8f, 9f, 10f, 11f, 12f },
            chaoticCeilings = new[] { 8f, 10f, 12f }, pitCeilings = new[] { 8f }, fixedRooms = true,
        };
        Generate(m, rnd, style);
        StampFixedRooms(m);
        var natures = new Dictionary<Room, int>();
        foreach (Room room in m.rooms)
            if (!room.plain) { room.plain = true; natures[room] = rnd.Next(RoomNatures); }
        OpenCourtyards(m, rnd);
        // Chemins (Parfum, câbles) : les cours se traversent librement, pas les remparts (sauf par leur passage).
        var rampartCells = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) rampartCells.UnionWith(rp.cells);
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
                foreach (int d in new[] { 0, 2 })
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (!m.Void(i, j) || !m.Void(ni, nj)) continue;
                    if (rampartCells.Contains(new Vector2Int(i, j)) || rampartCells.Contains(new Vector2Int(ni, nj))) continue;
                    SetWall(m, i, j, d, false);
                }
        yield return null;

        var refs = new ZoneRefs { index = zone, maze = m, root = new GameObject("Zone " + zone), terrain = TerrainKind.Chateau };
        build.Refs = refs;
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;
        refs.entrance = m.Center(m.start.x, m.start.y);
        var outer = ramparts[0];
        refs.sideOf = p => m.CellAt(p, out int ci, out int cj) && outer.nrSide.Contains(new Vector2Int(ci, cj)) ? 1 : 0;

        var geometry = BuildGeometry(m, root, colliders, new System.Random(seed * 97 + zone * 173));
        while (geometry.MoveNext()) yield return null;

        // ---------- Cours, douves, toits, enceinte de 60 m ----------
        var shell = new Kit(root, colliders.transform, "Z" + zone + "_Chateau");
        BuildCourtyards(m, ramparts, shell);
        // Cours où les escaliers des toits ne vont pas : remparts, douves, abords des Tours, du Hub et de la Neutral Room.
        var busy = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) { busy.UnionWith(rp.cells); busy.UnionWith(rp.moat); busy.UnionWith(rp.back); }
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                Vector3 cc = m.Center(i, j);
                if (NearKeep(i, j) || rounds.Exists(r => Flat(r.center - cc) < r.radius + 2.6f)) busy.Add(new Vector2Int(i, j));
            }
        BuildRoofs(m, shell, rnd, busy);
        BuildEnclosure(m, shell, ChateauHeight, previous);
        shell.Flush();
        yield return null;

        // ---------- Remparts ----------
        for (int k = 0; k < ramparts.Count; k++)
        {
            BuildRampart(m, refs, ramparts[k], root, colliders.transform, zone * 10 + k, new System.Random(seed * 41 + zone * 313 + k * 71));
            yield return null;
        }

        // ---------- Tours, Donjon, Maisons-Fou ----------
        float highest = float.MinValue;
        foreach (var plan in rounds)
        {
            var kit = new Kit(root, colliders.transform, "Z" + zone + (plan.kind == 2 ? "_MaisonFou" : plan.kind == 1 ? "_Donjon" : "_Tour"));
            if (plan.kind == 2) BuildMaisonFou(kit, plan, rnd);
            else
            {
                Vector3 summit = BuildTower(kit, plan, rnd, root);
                // Cloches Décoratives tout en haut des Tours et du Donjon (chapitre 7.6).
                refs.bellSpots.Add((summit + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f)), 0f));
                if (summit.y > highest) highest = summit.y;
                m.perches.Add((summit + Vector3.right * 2f, 0f, 3f));
            }
            kit.Flush();
            yield return null;
        }
        // Le plus haut d'abord : les Cloches de la zone y apparaissent en priorité.
        refs.bellSpots.Sort((a, b) => b.pos.y.CompareTo(a.pos.y));
        int bells = GameRules.HangingBellsInZone(zone);
        if (refs.bellSpots.Count > bells) refs.bellSpots.RemoveRange(bells, refs.bellSpots.Count - bells);

        BuildNeutralRoom(m, root, refs, new System.Random(seed * 59 + zone * 613));
        BuildBevels(m, root, refs, new System.Random(seed * 53 + zone * 929));
        yield return null;

        // Décor des salles (lustres, emplacements des monticules), puis le mobilier selon la nature de chaque salle.
        Physics.SyncTransforms();
        floorEyes = new List<Vector3>();
        statueLog = refs.statues;
        var decor = Decorate(m, root, colliders.transform, rnd, refs.itemSpots);
        while (decor.MoveNext()) yield return null;
        statueLog = null;
        FurnishCastle(m, root, natures, rnd);
        CourtyardSpots(m, refs, rnd);
        yield return null;
        var supers = FindSuperMoundSpots(m, refs, new System.Random(seed * 67 + zone * 389));
        while (supers.MoveNext()) yield return null;

        refs.lowestFloor = -MoatDepth - 1f;
        refs.nav = new MazeNav();
        refs.nav.AddZone(m.origin, m.N, Cell, (i, j) => !m.Reserved(i, j), (i, j) => TowerHeight * 1.5f + 2f, refs.lowestFloor);
        refs.nav.Prepare(colliders.transform, refs.door != null ? refs.door.Blocker : null);
        AddReflectionProbes(m, root);
        if (zone == GameRules.SecretCorpseZone) PlaceSecretCorpse(m, root);
        // Le mur nord de la zone monte jusqu'au plafond gris : la zone suivante n'a rien à compléter.
        for (int i = 0; i < m.N; i++) m.northHeights[i] = ChateauHeight;
    }

    // Les cours (cellules vides) s'ouvrent sur les salles et corridors voisins : chaque région de salles est reliée à au moins une
    // cour (sauf celle de la Neutral Room et celle du Hub, déjà reliées au reste), et chaque cour à plusieurs portes.
    static void OpenCourtyards(Maze m, System.Random rnd)
    {
        var edges = new List<(Vector2Int town, int dir)>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (m.Void(ni, nj)) edges.Add((new Vector2Int(i, j), d));
                }
            }
        Shuffle(edges, rnd);
        // Régions de salles (cellules reliées sans mur) : chacune reçoit une porte vers une cour, puis une sur cinq de plus.
        var region = new Dictionary<Vector2Int, int>();
        int regions = 0;
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var c = new Vector2Int(i, j);
                if (m.Void(i, j) || m.Reserved(i, j) || region.ContainsKey(c)) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(c);
                region[c] = regions;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        var n = q + new Vector2Int(DI[d], DJ[d]);
                        if (!m.InGrid(n.x, n.y) || m.Void(n.x, n.y) || m.Reserved(n.x, n.y) || region.ContainsKey(n) || HasWall(m, q.x, q.y, d)) continue;
                        region[n] = regions;
                        queue.Enqueue(n);
                    }
                }
                regions++;
            }
        var served = new HashSet<int>();
        foreach (var (town, dir) in edges)
        {
            int reg = region.TryGetValue(town, out int r) ? r : -1;
            if (m.Fixed(town.x, town.y)) continue;
            // Une porte au moins par région, puis environ une arête sur douze.
            if (served.Contains(reg) && rnd.NextDouble() > 0.08) continue;
            served.Add(reg);
            SetWall(m, town.x, town.y, dir, false);
        }
    }

    // Sol des cours (au niveau du Hub) ; V0.7.12 : douves de 2 m, accolées au rempart (morceaux disjoints), leurs parois et leurs
    // escaliers (dans les endroits doublés).
    static void BuildCourtyards(Maze m, List<RampartPlan> ramparts, Kit kit)
    {
        var moat = new List<Rect>();
        foreach (var rp in ramparts)
            foreach (Rect r in rp.moatRects)
                foreach (Rect p in Subtract(new List<Rect> { r }, moat)) moat.Add(p);
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.Void(i, j)) continue;
                Vector3 c = m.Center(i, j);
                var cell = Rect.MinMaxRect(c.x - Cell * 0.5f, c.z - Cell * 0.5f, c.x + Cell * 0.5f, c.z + Cell * 0.5f);
                foreach (Rect p in Subtract(new List<Rect> { cell }, moat)) kit.Floor(p, 0f);
            }
        foreach (Rect r in moat) kit.Floor(r, -MoatDepth);
        // Parois : tout le contour de la douve (le long du rempart aussi, sous son pied).
        foreach (var (a, b, inward) in UnionOutline(moat))
        {
            bool alongX = inward.x == 0f;
            float length = alongX ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
            Vector2 mid = (a + b) * 0.5f - inward * 0.15f;
            float h = MoatDepth - 0.01f;
            kit.Box(new Vector3(mid.x, -MoatDepth + h * 0.5f, mid.y), alongX ? new Vector3(length + 0.3f, h, 0.3f) : new Vector3(0.3f, h, length + 0.3f));
        }
        foreach (var rp in ramparts)
            foreach (var (foot, dir) in rp.moatStairs) KitStairs(kit, foot, dir, 2.4f, -MoatDepth, 0f);
    }

    // Toits des salles et corridors (visibles depuis les remparts et les Tours), ceints de créneaux au-dessus des cours.
    // V0.7.12 : les toits sont reliés entre eux par des escaliers (du toit le plus bas vers le plus haut), et au sol par au moins 8
    // escaliers de 2 m de large, posés dans les cours le long d'un mur plein ; les créneaux s'ouvrent là où ils arrivent.
    const int RoofStairsFromGround = 8;
    // Bilan du dernier Château construit (vérification).
    internal static string RoofReport = "";
    // Dessus des toits : la collision du plafond (0,3 m) ; on y marche.
    const float RoofStairWidth = 2f, RoofSlab = 0.3f;

    static void BuildRoofs(Maze m, Kit kit, System.Random rnd, HashSet<Vector2Int> busy)
    {
        int n = m.N;
        bool Building(int i, int j) => m.InGrid(i, j) && !m.Void(i, j) && !m.Reserved(i, j) && !m.Fixed(i, j);
        // Terrasses : toits voisins de même hauteur.
        var region = new int[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) region[i, j] = -1;
        int regions = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!Building(i, j) || region[i, j] >= 0) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(i, j));
                region[i, j] = regions;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = q.x + DI[d], nj = q.y + DJ[d];
                        if (!Building(ni, nj) || region[ni, nj] >= 0 || Mathf.Abs(m.Ceil(ni, nj) - m.Ceil(q.x, q.y)) > 0.01f) continue;
                        region[ni, nj] = regions;
                        queue.Enqueue(new Vector2Int(ni, nj));
                    }
                }
                regions++;
            }
        var parent = new int[regions];
        for (int r = 0; r < regions; r++) parent[r] = r;
        int Find(int r) { while (parent[r] != r) r = parent[r] = parent[parent[r]]; return r; }
        var skip = new HashSet<(int, int, int)>();
        var used = new HashSet<Vector2Int>(busy);

        // Escalier vers le bord du toit de la cellule 'top', depuis la cellule voisine 'from' (dans la direction 'd' depuis 'top') : une
        // cour (depuis le sol) ou un toit plus bas. Il monte droit vers le toit (perpendiculaire), ou le long de son bord (parallèle,
        // sens 'side'), et n'occupe que des cellules de même niveau que 'from' ; dans une cour, il ne cache aucune porte.
        int ground = regions;   // nœud "sol" de l'union
        System.Array.Resize(ref parent, regions + 1);
        parent[ground] = ground;
        var grounded = new HashSet<int>();
        int placed = 0;
        bool SameBase(Vector2Int c, Vector2Int from) => from == c || (m.Void(from.x, from.y)
            ? m.InGrid(c.x, c.y) && m.Void(c.x, c.y) && !busy.Contains(c)
            : Building(c.x, c.y) && region[c.x, c.y] == region[from.x, from.y]);
        bool TryStair(Vector2Int top, int d, int mode)
        {
            var from = top + new Vector2Int(DI[d], DJ[d]);
            bool fromGround = m.Void(from.x, from.y);
            float baseY = fromGround ? 0f : m.Ceil(from.x, from.y) + RoofSlab, h = m.Ceil(top.x, top.y) + RoofSlab;
            if (h - baseY < 0.5f) return false;
            float run = StairRun(h - baseY);
            int k = Mathf.CeilToInt((run + 0.6f) / Cell);
            Vector3 toTop = -new Vector3(DI[d], 0f, DJ[d]);
            // mode 0 : perpendiculaire ; 1 et 2 : le long du bord, dans un sens ou dans l'autre.
            var step = mode == 0 ? new Vector2Int(DI[d], DJ[d]) : (d < 2 ? new Vector2Int(0, 1) : new Vector2Int(1, 0)) * (mode == 1 ? 1 : -1);
            var cells = new List<Vector2Int>();
            for (int t = 0; t < k; t++)
            {
                var c = from + step * t;
                if (!SameBase(c, from) || used.Contains(c) || !fromGround && busy.Contains(c)) return false;
                if (fromGround)
                {
                    // Le mur qui borde l'escalier (côté toit) est plein : l'escalier ne bouche aucune porte.
                    var wallCell = mode == 0 ? (t == 0 ? top : new Vector2Int(-1, -1)) : c - new Vector2Int(DI[d], DJ[d]);
                    if (wallCell.x >= 0 && Building(wallCell.x, wallCell.y) && (!HasWall(m, wallCell.x, wallCell.y, d) || m.HasWallHole(wallCell.x, wallCell.y, d))) return false;
                }
                cells.Add(c);
            }
            Vector3 edge = m.Center(top.x, top.y) - toTop * (Cell * 0.5f + Wall * 0.5f + 0.02f);
            Vector3 foot, dir;
            if (mode == 0)
            {
                dir = toTop;
                foot = edge - dir * run;
            }
            else
            {
                dir = -new Vector3(step.x, 0f, step.y);
                Vector3 lane = edge - toTop * (RoofStairWidth * 0.5f + 0.05f);
                Vector3 head = lane + dir * (Cell * 0.5f - 0.3f);
                foot = head - dir * run;
            }
            KitStairs(kit, foot, dir, RoofStairWidth, baseY, h);
            foreach (var c in cells) used.Add(c);
            skip.Add((top.x, top.y, d));
            parent[Find(region[top.x, top.y])] = Find(fromGround ? ground : region[from.x, from.y]);
            if (fromGround) placed++;
            return true;
        }

        var candidates = new List<(Vector2Int top, int d, int mode)>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (Building(i, j))
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!m.InGrid(ni, nj)) continue;
                        bool groundSide = m.Void(ni, nj) && !busy.Contains(new Vector2Int(ni, nj));
                        bool roofSide = Building(ni, nj) && region[ni, nj] != region[i, j] && m.Ceil(ni, nj) < m.Ceil(i, j) - 0.01f;
                        if (!groundSide && !roofSide) continue;
                        for (int mode = 0; mode < 3; mode++) candidates.Add((new Vector2Int(i, j), d, mode));
                    }
        Shuffle(candidates, rnd);
        // Chaque escalier relie deux ensembles encore séparés (le sol compte pour un ensemble), jusqu'à ce que plus rien ne se relie.
        for (bool progress = true; progress;)
        {
            progress = false;
            foreach (var (top, d, mode) in candidates)
            {
                var from = top + new Vector2Int(DI[d], DJ[d]);
                int a = Find(region[top.x, top.y]), b = Find(m.Void(from.x, from.y) ? ground : region[from.x, from.y]);
                if (a == b) continue;
                if (TryStair(top, d, mode)) progress = true;
            }
        }
        // Au moins 8 depuis le sol.
        foreach (var (top, d, mode) in candidates)
        {
            if (placed >= RoofStairsFromGround) break;
            var from = top + new Vector2Int(DI[d], DJ[d]);
            if (m.Void(from.x, from.y)) TryStair(top, d, mode);
        }
        int components = 0, reached = 0;
        for (int r = 0; r < regions; r++)
            if (Find(r) == r) components++;
        for (int r = 0; r < regions; r++) if (Find(r) == Find(ground)) reached++;
        RoofReport = placed + " escaliers depuis le sol, " + reached + "/" + regions + " terrasses reliées au sol, " + components + " ensembles isolés";

        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j)) continue;
                float ceil = m.Ceil(i, j), roof = ceil + RoofSlab;
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f + Wall * 0.5f;
                kit.walls.WorldQuad(new Vector3(c.x - e, roof, c.z - e), new Vector3(c.x + e, roof, c.z - e),
                    new Vector3(c.x + e, roof, c.z + e), new Vector3(c.x - e, roof, c.z + e), Vector3.up, 0.5f);
                // Tranche du toit au-dessus des murs, là où le voisin est plus bas (ou une cour).
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (m.InGrid(ni, nj) && !m.Void(ni, nj) && !m.Reserved(ni, nj) && m.Ceil(ni, nj) >= ceil - 0.01f) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]);
                    Vector3 fc = c + dir * (Cell * 0.5f) + Vector3.up * (ceil + RoofSlab * 0.5f);
                    kit.walls.Box(fc, d < 2 ? new Vector3(Wall, RoofSlab, Cell + Wall) : new Vector3(Cell + Wall, RoofSlab, Wall), 0.5f);
                }
                Room room = m.RoomAt(i, j);
                if (room == null) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    bool edge = !m.InGrid(ni, nj) || m.Void(ni, nj) || m.RoomAt(ni, nj) != room && m.Ceil(ni, nj) < ceil;
                    if (!edge || !m.InGrid(ni, nj) || skip.Contains((i, j, d))) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                    Vector3 mid = c + dir * (Cell * 0.5f);
                    KitMerlons(kit, mid - side * (Cell * 0.5f), mid + side * (Cell * 0.5f), roof, -0.35f, dir, null, kit.walls, false);
                }
            }
    }

    // Enceinte de la zone jusqu'au plafond gris (au-dessus des salles), et le plafond lui-même.
    static void BuildEnclosure(Maze m, Kit kit, float height, ZoneRefs previous)
    {
        float x0 = m.origin.x, z0 = m.origin.z, size = m.N * Cell;
        for (int k = 0; k < m.N; k++)
        {
            // Ouest, est, nord : au-dessus des salles de bord (les cours ont déjà leur mur jusqu'en haut).
            foreach (var (i, j, d) in new[] { (0, k, 1), (m.N - 1, k, 0), (k, m.N - 1, 2), (k, 0, 3) })
            {
                if (m.Void(i, j)) continue;
                float from = m.Reserved(i, j) ? WallHeight : m.Ceil(i, j);
                if (d == 3 && m.southHeights != null) from = Mathf.Max(from, m.southHeights[i]);
                if (height - from < 0.05f) continue;
                Vector3 c = m.Center(i, j) + new Vector3(DI[d], 0f, DJ[d]) * (Cell * 0.5f) + Vector3.up * ((from + height) * 0.5f);
                kit.Box(c, d < 2 ? new Vector3(Wall, height - from, Cell + Wall) : new Vector3(Cell + Wall, height - from, Wall));
            }
        }
        // Plafond gris (et sa collision : rien ne passe au-dessus, pas même un boulet de canon).
        kit.grey.WorldQuad(new Vector3(x0, height, z0), new Vector3(x0, height, z0 + size), new Vector3(x0 + size, height, z0 + size), new Vector3(x0 + size, height, z0), Vector3.down, 0.1f);
        kit.Collider(new Vector3(x0 + size * 0.5f, height + 0.5f, z0 + size * 0.5f), new Vector3(size, 1f, size));
    }

    // ---------- Rempart ----------

    static void BuildRampart(Maze m, ZoneRefs refs, RampartPlan plan, Transform root, Transform colliders, int leverId, System.Random rnd)
    {
        var kit = new Kit(root, colliders, "Z" + m.zone + "_Rempart" + leverId);
        float west = OriginX, east = OriginX + ZoneSize, south = ZoneSouth(m.zone), north = south + ZoneSize;
        // Passage (herses), choisi avec le plan (la douve s'y interrompt).
        Rect gr = plan.rects[plan.gateSegment];
        bool gateAlongX = plan.gateAlongX;
        float gateAt = plan.gateAt;
        var outline = OutlineEdges(plan.rects, west, east, south, north);

        // Faces : les bords de la réunion des emprises (sans le mur d'enceinte), 1 m d'épaisseur, du sol à 10 m.
        foreach (var (a, b, inward) in outline)
        {
            bool alongX = Mathf.Abs(a.y - b.y) < 0.01f;
            float lo = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y), hi = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
            // Ouverture des herses sur les deux faces du segment du passage.
            var pieces = new List<Rect> { Rect.MinMaxRect(lo, 0f, hi, RampartHeight) };
            bool gateFace = alongX == gateAlongX && (alongX ? Mathf.Abs(a.y - gr.yMin) < 0.01f || Mathf.Abs(a.y - gr.yMax) < 0.01f : Mathf.Abs(a.x - gr.xMin) < 0.01f || Mathf.Abs(a.x - gr.xMax) < 0.01f)
                && gateAt > lo && gateAt < hi;
            if (gateFace) pieces = Subtract(pieces, new[] { Rect.MinMaxRect(gateAt - GateWidth * 0.5f, -1f, gateAt + GateWidth * 0.5f, GateHeight) });
            foreach (Rect p in pieces)
            {
                Vector3 c = alongX ? new Vector3(p.center.x, p.center.y, a.y + inward.y * 0.5f) : new Vector3(a.x + inward.x * 0.5f, p.center.y, p.center.x);
                kit.Box(c, alongX ? new Vector3(p.width, p.height, 1f) : new Vector3(1f, p.height, p.width));
            }
            // V0.7.12 : arche autour du passage, sur chaque face.
            if (gateFace)
                DoorArch(kit.walls, alongX ? new Vector3(gateAt, 0f, a.y + inward.y * 0.5f) : new Vector3(a.x + inward.x * 0.5f, 0f, gateAt),
                    alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f), GateWidth, GateHeight, 1.3f);
            // Meurtrières (fond noir) tous les 4 m, au rez-de-chaussée.
            Vector3 outward = new Vector3(-inward.x, 0f, -inward.y);
            Vector3 side = alongX ? Vector3.right : Vector3.forward;
            for (float t = lo + 3f; t < hi - 2f; t += 4f)
            {
                if (gateFace && Mathf.Abs(t - gateAt) < GateWidth) continue;
                Vector3 p = (alongX ? new Vector3(t, 2.5f, a.y) : new Vector3(a.x, 2.5f, t)) + outward * 0.02f;
                kit.dark.WorldQuad(p - side * 0.15f - Vector3.up * 0.9f, p + side * 0.15f - Vector3.up * 0.9f, p + side * 0.15f + Vector3.up * 0.9f, p - side * 0.15f + Vector3.up * 0.9f, outward, 1f);
            }
            // V0.7.12 : néons à intervalles réguliers, sous les créneaux (visibles depuis le sol).
            for (float t = lo + 4f; t < hi - 3f; t += RampartNeonSpacing)
            {
                if (gateFace && Mathf.Abs(t - gateAt) < GateWidth) continue;
                Vector3 p = (alongX ? new Vector3(t, RampartHeight - 0.8f, a.y) : new Vector3(a.x, RampartHeight - 0.8f, t)) + outward * 0.06f;
                AddWallNeon(root, p, outward, 9f, 1.1f, rnd);
            }
        }

        // Escalier intérieur, du rez-de-chaussée au chemin de ronde, le long du segment du passage.
        float run = StairRun(RampartHeight);
        Vector3 foot, dir;
        if (gateAlongX)
        {
            float z = gr.yMin + 1f + 1.3f;
            float xa = gateAt + GateWidth * 0.5f + 1.5f, xb = gateAt - GateWidth * 0.5f - 1.5f;
            if (xa + run < gr.xMax - 1f) { foot = new Vector3(xa, 0f, z); dir = Vector3.right; }
            else { foot = new Vector3(xb, 0f, z); dir = Vector3.left; }
        }
        else
        {
            float x = gr.xMin + 1f + 1.3f;
            float za = gateAt + GateWidth * 0.5f + 1.5f, zb = gateAt - GateWidth * 0.5f - 1.5f;
            if (za + run < gr.yMax - 1f) { foot = new Vector3(x, 0f, za); dir = Vector3.forward; }
            else { foot = new Vector3(x, 0f, zb); dir = Vector3.back; }
        }
        KitStairs(kit, foot, dir, 2.4f, 0f, RampartHeight, true);
        Vector3 head = foot + dir * run;
        Vector3 lateral = new Vector3(Mathf.Abs(dir.z), 0f, Mathf.Abs(dir.x)) * 1.3f;
        var holes = new List<Rect> { MinMax(foot - lateral, head + lateral) };

        // Dalle du chemin de ronde (sans recouvrement entre segments), percée au-dessus de l'escalier.
        var slabs = new List<Rect>();
        for (int k = 0; k < plan.rects.Count; k++)
        {
            var own = new List<Rect> { plan.rects[k] };
            for (int j = 0; j < k; j++) own = Subtract(own, new[] { plan.rects[j] });
            slabs.AddRange(own);
        }
        foreach (Rect p in Subtract(slabs, holes)) kit.Slab(p, RampartHeight, 0.6f);

        // Escaliers extérieurs, derrière le rempart (côté Neutral Room), vers le chemin de ronde : 3 à 5.
        int wanted = 3 + rnd.Next(3), placedBack = 0;
        var gapsByEdge = new List<(Vector3 a, Vector3 b, float from, float to)>();
        var candidates = new List<(Vector3 a, Vector3 b, Vector2 inward)>();
        foreach (var e in outline)
        {
            Vector2 mid = (e.a + e.b) * 0.5f;
            Vector3 probe = new Vector3(mid.x - e.inward.x * 3f, 0f, mid.y - e.inward.y * 3f);
            if (m.CellAt(probe, out int pi, out int pj) && plan.nrSide.Contains(new Vector2Int(pi, pj))) candidates.Add((new Vector3(e.a.x, 0f, e.a.y), new Vector3(e.b.x, 0f, e.b.y), e.inward));
        }
        float backRun = StairRun(RampartHeight);
        for (int attempt = 0; attempt < 40 && placedBack < wanted && candidates.Count > 0; attempt++)
        {
            var (a, b, inward) = candidates[rnd.Next(candidates.Count)];
            float length = Vector3.Distance(a, b);
            if (length < backRun + 4f) continue;
            Vector3 along = (b - a).normalized;
            float start = Rand(rnd, 2f, length - backRun - 2f);
            float end = start + backRun;
            if (gapsByEdge.Exists(g => Vector3.Distance(g.a, a) < 0.1f && start < g.to + 3f && end > g.from - 3f)) continue;
            Vector3 outward = new Vector3(-inward.x, 0f, -inward.y);
            KitStairs(kit, a + along * start + outward * 1.35f, along, 2.4f, 0f, RampartHeight);
            gapsByEdge.Add((a, b, end - 3f, end + 0.5f));
            placedBack++;
        }

        // Créneaux du chemin de ronde, sur les deux bords (ouverts là où arrivent les escaliers extérieurs).
        foreach (var (a2, b2, inward) in outline)
        {
            Vector3 a = new Vector3(a2.x, 0f, a2.y), b = new Vector3(b2.x, 0f, b2.y);
            var gaps = new List<(float, float)>();
            foreach (var g in gapsByEdge) if (Vector3.Distance(g.a, a) < 0.1f && Vector3.Distance(g.b, b) < 0.1f) gaps.Add((g.from, g.to));
            KitMerlons(kit, a, b, RampartHeight, 0.5f, new Vector3(inward.x, 0f, inward.y), gaps);
        }

        // Herses (de chaque côté du passage) et levier sur le chemin de ronde (V0.7.12), du côté opposé à l'escalier intérieur,
        // reliés par des câbles noirs.
        var gates = new List<CastleGate>();
        Quaternion gateRot = gateAlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 at = gateAlongX ? new Vector3(gateAt, 0f, gr.center.y + s * (RampartThick * 0.5f - 0.5f)) : new Vector3(gr.center.x + s * (RampartThick * 0.5f - 0.5f), 0f, gateAt);
            gates.Add(CastleGate.Create(root, at, gateRot, GateWidth, GateHeight));
        }
        refs.leverGates[leverId] = gates;
        float away = -(gateAlongX ? dir.x : dir.z) * Rand(rnd, 6f, 10f);
        Vector3 leverPos = gateAlongX ? new Vector3(gateAt + away, RampartHeight, gr.center.y) : new Vector3(gr.center.x, RampartHeight, gateAt + away);
        if (gateAlongX) leverPos.x = Mathf.Clamp(leverPos.x, gr.xMin + 3f, gr.xMax - 3f);
        else leverPos.z = Mathf.Clamp(leverPos.z, gr.yMin + 3f, gr.yMax - 3f);
        var lever = CastleLever.Create(root, leverId, leverPos, gateAlongX ? 0f : 90f);
        refs.levers.Add(lever);
        foreach (var g in gates)
        {
            // Le câble descend le long de la face intérieure, puis court sur le sol jusqu'au pied de la herse.
            Vector3 gp = g.transform.position;
            Vector3 wallSide = gateAlongX ? new Vector3(leverPos.x, 0f, gr.yMax - 1.05f) : new Vector3(gr.xMax - 1.05f, 0f, leverPos.z);
            var points = new List<Vector3>
            {
                leverPos + Vector3.up * 0.05f,
                new Vector3(wallSide.x, leverPos.y + 0.03f, wallSide.z),
                new Vector3(wallSide.x, 0.03f, wallSide.z),
                new Vector3(gateAlongX ? gp.x : wallSide.x, 0.03f, gateAlongX ? wallSide.z : gp.z),
                gp + Vector3.up * 0.03f,
            };
            lever.AddCable(KitRibbon(root, points, 0.06f));
        }

        // Tourelles décoratives (modèle Meshy de tour) aux angles du chemin de ronde, et lustres sous le chemin de ronde.
        foreach (Rect r in plan.rects)
        {
            foreach (var corner in new[] { new Vector3(r.xMin + 1.5f, RampartHeight, r.yMin + 1.5f), new Vector3(r.xMax - 1.5f, RampartHeight, r.yMax - 1.5f) })
            {
                if (corner.x < west + 2f || corner.x > east - 2f || rnd.NextDouble() < 0.5) continue;
                if (holes.Exists(h => h.Contains(new Vector2(corner.x, corner.z)))) continue;
                var turret = GameArt.SpawnModel("TourChateau", root, 4.5f);
                if (turret != null) turret.transform.position = corner;
            }
            Vector3 lamp = new Vector3(r.center.x, RampartHeight - 0.6f, r.center.y);
            if (!holes.Exists(h => h.Contains(new Vector2(lamp.x, lamp.z)))) AddChandelier(root, lamp, 14f, 1.3f, rnd);
        }
        kit.Flush();
    }

    const float RampartNeonSpacing = 10f;

    // Bords de la réunion des rectangles (sans ceux posés sur le bord de la zone) : (début, fin, direction vers l'intérieur).
    static List<(Vector2 a, Vector2 b, Vector2 inward)> OutlineEdges(List<Rect> rects, float west, float east, float south, float north)
    {
        var result = new List<(Vector2, Vector2, Vector2)>();
        foreach (Rect r in rects)
        {
            var edges = new[]
            {
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(0f, 1f)),
                (new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(0f, -1f)),
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(1f, 0f)),
                (new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(-1f, 0f)),
            };
            foreach (var (a, b, inward) in edges)
            {
                bool alongX = inward.x == 0f;
                if (alongX && (Mathf.Abs(a.y - south) < 0.01f || Mathf.Abs(a.y - north) < 0.01f)) continue;
                if (!alongX && (Mathf.Abs(a.x - west) < 0.01f || Mathf.Abs(a.x - east) < 0.01f)) continue;
                // Retire les parties de l'arête à l'intérieur d'un autre rectangle.
                float lo = alongX ? a.x : a.y, hi = alongX ? b.x : b.y, line = alongX ? a.y : a.x;
                var spans = new List<Vector2> { new Vector2(lo, hi) };
                foreach (Rect o in rects)
                {
                    if (o == r) continue;
                    float oLo = alongX ? o.xMin : o.yMin, oHi = alongX ? o.xMax : o.yMax, cLo = alongX ? o.yMin : o.xMin, cHi = alongX ? o.yMax : o.xMax;
                    if (line <= cLo + 0.01f || line >= cHi - 0.01f) continue;
                    var next = new List<Vector2>();
                    foreach (var s in spans)
                    {
                        if (s.y <= oLo || s.x >= oHi) { next.Add(s); continue; }
                        if (s.x < oLo) next.Add(new Vector2(s.x, oLo));
                        if (s.y > oHi) next.Add(new Vector2(oHi, s.y));
                    }
                    spans = next;
                }
                foreach (var s in spans)
                    if (s.y - s.x > 0.3f)
                        result.Add(alongX ? (new Vector2(s.x, line), new Vector2(s.y, line), inward) : (new Vector2(line, s.x), new Vector2(line, s.y), inward));
            }
        }
        return result;
    }

    // ---------- Tours et Donjon ----------

    // Tour ronde (Tour ou Donjon) : mur percé d'une porte (sous une arche) et de meurtrières, créneaux, escalier en colimaçon de 3 m
    // de large jusqu'au sommet (V0.7.12), étages intermédiaires une fois sur deux. Retourne le centre du sommet.
    static Vector3 BuildTower(Kit kit, RoundPlan plan, System.Random rnd, Transform root)
    {
        float R = plan.radius, H = plan.height;
        Vector3 c = plan.center;
        const int sides = 24;
        float doorAngle = Rand(rnd, 0f, 360f);
        var slits = new List<(int, float)>();
        int slitCount = 15 + rnd.Next(8);
        for (int k = 0; k < slitCount; k++) slits.Add((rnd.Next(sides), Rand(rnd, 4f, H - 3f)));
        KitRoundWall(kit, c, R, 0f, H, sides, new List<float> { doorAngle }, 2.6f, 3.2f, slits);
        RoundDoorArch(kit, c, R, doorAngle, 2.6f, 3.2f);
        // Escalier contre le mur, de la porte jusqu'au sommet.
        float rOut = R - 1.05f, rIn = rOut - TowerStairWidth;
        float end = KitSpiral(kit, c, rIn, rOut, 0f, H, doorAngle + 25f);
        // Étages (à 10 et 20 m pour une Tour, 15 et 30 m pour le Donjon), une fois sur deux, au centre (l'escalier passe autour).
        float step = H / 3f, lowestCeiling = H - 0.5f;
        foreach (float level in new[] { step, step * 2f })
            if (rnd.NextDouble() < 0.5)
            {
                KitDisc(kit, c, rIn - 0.05f, level, 0.35f, sides);
                lowestCeiling = Mathf.Min(lowestCeiling, level - 0.35f);
            }
        // Sommet : dalle pleine, ouverte au-dessus de l'arrivée de l'escalier, créneaux tout autour.
        float arrivalSpan = 2.2f / SpiralRise * (SpiralTread / ((rIn + rOut) * 0.5f)) * Mathf.Rad2Deg;
        KitDisc(kit, c, R - 1f, H, 0.5f, sides, end - arrivalSpan, end + 2f, 0f);
        KitRoundMerlons(kit, c, R, H, sides);
        // Lustre accroché sous le premier plafond (aucun décor ne flotte).
        AddChandelier(root, c + Vector3.up * lowestCeiling, 16f, 1.4f, rnd);
        return c + Vector3.up * H;
    }

    const float TowerStairWidth = 3f;

    // Arche autour d'une porte percée dans un mur rond (épaisseur 1 m), face à l'angle 'angle'.
    static void RoundDoorArch(Kit kit, Vector3 center, float radius, float angle, float width, float height)
    {
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        DoorArch(kit.walls, center + rot * Vector3.forward * (radius - 0.5f), rot, width, height, 1.3f);
    }

    // Maison-Fou : bâtiment en forme de fou (pièce d'échecs) de 15 m, 8 m de diamètre ; on y entre par deux portes (une salle au sol,
    // sous un plafond à 8 m, éclairée par un lustre) ; au-dessus, le corps effilé, le col et la mitre.
    static void BuildMaisonFou(Kit kit, RoundPlan plan, System.Random rnd)
    {
        Vector3 c = plan.center;
        float R = plan.radius;
        const int sides = 16;
        float door = Rand(rnd, 0f, 360f);
        KitRoundWall(kit, c, R, 0f, 8f, sides, new List<float> { door, door + 180f }, 2.4f, 3f, null, 0.8f);
        RoundDoorArch(kit, c, R + 0.1f, door, 2.4f, 3f);
        RoundDoorArch(kit, c, R + 0.1f, door + 180f, 2.4f, 3f);
        KitDisc(kit, c, R - 0.4f, 8.2f, 0.4f, sides);
        // V0.7.12 : un lustre suspendu au plafond de la salle.
        AddChandelier(kit.root, c + Vector3.up * 7.8f, 10f, 1.1f, rnd);
        // Corps effilé (anneaux de plus en plus étroits), col, mitre (pas de collision au-dessus de la salle).
        float y = 8.2f;
        foreach (var (radius, height) in new[] { (R * 0.85f, 1.6f), (R * 0.7f, 1.4f), (R * 0.95f, 0.4f), (R * 0.75f, 1.2f), (R * 0.6f, 1.2f), (R * 0.42f, 1f), (R * 0.22f, 0.8f) })
        {
            for (int k = 0; k < sides; k++)
            {
                float a = (k + 0.5f) * 360f / sides;
                float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.06f;
                Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 0.3f) + Vector3.up * (y + height * 0.5f);
                kit.Turned(p, new Vector3(chord, height, 0.6f), a, false);
            }
            y += height;
        }
        // Bouton au sommet de la mitre.
        var top = GameArt.Prim(PrimitiveType.Sphere, kit.root, c + Vector3.up * (y + 0.4f), Vector3.one * 0.9f, GameArt.CheckerWall);
        foreach (var col in top.GetComponentsInChildren<Collider>()) Object.Destroy(col);
    }

    // ---------- Mobilier des salles (modèles Meshy) ----------

    // V0.7.12 : cachots, salles d'armes, salles de banquet (avec les tableaux des anciens halls), chapelles. Où se pose chaque objet :
    // Wall : contre un mur (au sol) ; Near : au sol, près d'un mur ; Hung : accroché à un mur ; Floor : au sol, dans la salle.
    enum Spot { Wall, Near, Hung, Floor }

    static readonly string[][] Furniture =
    {
        new[] { "Grille", "Menottes", "Latrines" },                  // cachot
        new[] { "Epee", "Masse", "Cible", "Mannequin" },              // salle d'armes
        new[] { "Table", "Chaise", "Chaise", "Chaise", "Cadre" },     // salle de banquet
        new[] { "Banc", "Banc", "Croix", "Autel" },                   // chapelle
    };
    const int RoomNatures = 4;

    static Spot SpotOf(string model)
    {
        switch (model)
        {
            case "Grille": case "Latrines": case "Epee": case "Masse": return Spot.Wall;
            case "Menottes": case "Autel": return Spot.Near;
            case "Cadre": return Spot.Hung;
            default: return Spot.Floor;
        }
    }

    static float ModelHeight(string model)
    {
        switch (model)
        {
            case "Grille": return 2.8f;
            case "Menottes": return 0.8f;
            case "Latrines": return 1.4f;
            case "Epee": return 1.1f;
            case "Masse": return 0.9f;
            case "Cible": return 1.6f;
            case "Mannequin": return 1.8f;
            case "Table": return 3.2f;
            case "Chaise": return 1.2f;
            case "Cadre": return 1.4f;
            case "Banc": return 2.6f;
            case "Croix": return 2.6f;
            case "Autel": return 2f;
            default: return 1f;
        }
    }

    static void FurnishCastle(Maze m, Transform root, Dictionary<Room, int> natures, System.Random rnd)
    {
        var props = new GameObject("Mobilier_Z" + m.zone).transform;
        props.SetParent(root, false);
        foreach (var kv in natures)
        {
            Room room = kv.Key;
            string[] set = Furniture[kv.Value];
            var placed = new List<Vector3>();
            int count = 2 + room.cells.Count;
            for (int k = 0; k < count; k++)
            {
                string model = set[rnd.Next(set.Length)];
                Spot spot = SpotOf(model);
                var cell = room.cells[rnd.Next(room.cells.Count)];
                if (m.keepClear.Contains(cell) || m.archCells.Contains(cell)) continue;
                var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
                if (go == null) continue;
                bool ok = spot == Spot.Floor
                    ? PlaceOnFloor(m, room, go, cell, 0f, Rand(rnd, 0f, 360f), placed, rnd)
                    : PlaceAgainstWall(m, go, cell, rnd.Next(4), spot == Spot.Hung ? Rand(rnd, 1.5f, 2.1f) : 0f, spot == Spot.Near ? 0.25f : 0.02f, placed, rnd);
                if (!ok) { Object.Destroy(go); continue; }
                // Portrait d'un chien (skin) dans les tableaux.
                if (model == "Cadre") AddPortrait(go.transform, rnd);
                if (spot != Spot.Hung)
                {
                    Bounds b = GameArt.LocalBounds(go.transform);
                    var col = go.AddComponent<BoxCollider>();
                    col.center = b.center;
                    col.size = b.size;
                }
            }
        }
    }

    // Objet posé au sol, quelque part dans la cellule (assez loin des murs et des autres objets).
    static bool PlaceOnFloor(Maze m, Room room, GameObject go, Vector2Int cell, float y, float yaw, List<Vector3> placed, System.Random rnd)
    {
        Vector3 c = m.Center(cell.x, cell.y);
        Vector3 p = new Vector3(c.x + Rand(rnd, -1f, 1f), y, c.z + Rand(rnd, -1f, 1f));
        if (placed.Exists(q => Flat(q - p) < 1.6f)) return false;
        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        placed.Add(p);
        return true;
    }

    // Objet contre le mur 'd' de la cellule (mur plein, sans ouverture) : son dos touche la face du mur ('gap' près), à la hauteur 'y'
    // au-dessus du sol de la cellule, tourné vers l'intérieur.
    static bool PlaceAgainstWall(Maze m, GameObject go, Vector2Int cell, int d, float y, float gap, List<Vector3> placed, System.Random rnd, float floorY = 0f)
    {
        if (!HasWall(m, cell.x, cell.y, d) || m.HasWallHole(cell.x, cell.y, d)) return false;
        Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
        Quaternion rot = Quaternion.Euler(0f, d == 0 ? -90f : d == 1 ? 90f : d == 2 ? 180f : 0f, 0f);
        go.transform.rotation = rot;
        Bounds b = GameArt.LocalBounds(go.transform);
        // Étendue de l'objet le long de la normale du mur et le long du mur.
        Vector3 ln = Quaternion.Inverse(rot) * n, ls = Quaternion.Inverse(rot) * side;
        float depth = Mathf.Abs(ln.x) * b.extents.x + Mathf.Abs(ln.z) * b.extents.z, width = Mathf.Abs(ls.x) * b.extents.x + Mathf.Abs(ls.z) * b.extents.z;
        float centerN = Vector3.Dot(rot * b.center, n), centerS = Vector3.Dot(rot * b.center, side);
        float reach = Cell * 0.5f - Wall * 0.5f - 0.15f - width;
        if (reach < 0f) return false;
        Vector3 c = m.Center(cell.x, cell.y);
        float wallPlane = Cell * 0.5f - Wall * 0.5f;
        float along = Rand(rnd, -reach, reach);
        Vector3 p = c + n * (wallPlane - gap - depth - centerN) + side * (along - centerS) + Vector3.up * (floorY + y);
        if (placed.Exists(q => Flat(q - p) < Mathf.Max(1.2f, width * 2f))) return false;
        go.transform.position = p;
        placed.Add(p);
        return true;
    }

    // Image d'un skin de chien dans un cadre de hall (texture Resources/Art/Portraits si elle existe, sinon une toile sombre).
    static void AddPortrait(Transform frame, System.Random rnd)
    {
        Bounds b = GameArt.LocalBounds(frame);
        var tex = Resources.Load<Texture2D>("Art/Portraits/Portrait" + (rnd.Next(8) + 1));
        Material mat = tex != null ? GameArt.Lit("Portrait" + tex.name, Color.white, tex, 0.2f) : GameArt.Lit("PortraitSombre", new Color(0.12f, 0.1f, 0.1f), null, 0.2f);
        var quad = GameArt.Prim(PrimitiveType.Quad, frame, new Vector3(b.center.x, b.center.y, b.max.z - 0.02f), new Vector3(b.size.x * 0.7f, b.size.y * 0.7f, 1f), mat);
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var c in quad.GetComponentsInChildren<Collider>()) Object.Destroy(c);
    }

    // Emplacements de monticules dans les cours (au sol, hors des douves et des escaliers).
    static void CourtyardSpots(Maze m, ZoneRefs refs, System.Random rnd)
    {
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.Void(i, j) || rnd.NextDouble() > 0.35) continue;
                Vector3 p = m.Center(i, j) + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f));
                if (!Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.95f || !hit.transform.IsChildOf(refs.structure)) continue;
                refs.itemSpots.Add(hit.point);
            }
    }
}
