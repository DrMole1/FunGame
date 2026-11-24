using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Chapitre 11.3 : structure Château. V0.7.14 : le Château n'est plus une zone entière mais une structure posée dans une zone
// d'Échiquier (parcelles bleues d'un gabarit, WorldBuilder.Structure) :
// - délimitée par ses remparts de 20 m de haut, sur lesquels on ne peut ni monter ni sauter (l'enceinte de la structure monte
//   jusqu'à son plafond gris, à 60 m), visibles malgré la distance et l'éclairage (briques lumineuses), surmontés de créneaux. Un
//   rempart face à chaque partie de l'Échiquier (côté entrée, côté Neutral Room), chacun avec 3 entrées espacées d'au moins 30 m :
//   une herse ouverte sous une arche et un pont qui survole la douve ;
// - devant chaque rempart, une douve de 2 m de profondeur accolée à lui, large de 3 m (5 m par endroits, où un escalier dans le
//   sens de la longueur permet d'en sortir), et un rebord de 2 m le long de l'Échiquier ;
// - murs en briques noires et blanches, sols en damier, plafonds gris (béton) ;
// - jusqu'à 6 Tours (30 m, 15 à 20 m de diamètre, créneaux, au moins 15 meurtrières, escalier en colimaçon de 3 m de large jusqu'au
//   dernier étage, sans accès au toit ; étages à 10 et 20 m une fois sur deux) et 1 Donjon (une Tour 50 % plus haute et plus large),
//   au centre de la zone de préférence ;
// - entre eux, des salles de 8 à 12 m de haut (côtés de 5 à 20 m), aux toits ceints de créneaux et reliés entre eux et au sol par
//   des escaliers, et des corridors (15 % pour 85 % de salles ; 30 % des corridors ont un renfoncement en biseau) : cachots, salles
//   d'armes, salles de banquet, chapelles. Chaque entrée a son arche ; éclairage par des lustres ; aucun objet décoratif ne flotte.
//   V0.7.14 (Note 01) : plus de torches.
public static partial class WorldBuilder
{
    public const float ChateauHeight = 60f;
    // V0.7.14 : remparts de 20 m (une cellule d'épaisseur), douves de 3 m (5 m par endroits), rebord de 2 m.
    const float RampartHeight = 20f, MoatDepth = 2f, LedgeWidth = 2f, MoatEnd = 5f, GateWidth = 4f, GateHeight = 5f;
    const float TowerHeight = 30f;
    const int MaxTowers = 6;
    const float WideMoatChance = 0.3f;

    // Tour ronde posée dans la structure : centre, rayon, hauteur.
    internal class RoundPlan { public Vector3 center; public float radius, height; public int kind; }   // kind : 0 Tour, 1 Donjon

    internal class ChateauPlan
    {
        public StructureSite site;
        public System.Random rnd;
        public readonly HashSet<Vector2Int> ring1 = new HashSet<Vector2Int>(), ring2 = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> courts = new HashSet<Vector2Int>();      // cours intérieures (cellules vides)
        public readonly HashSet<Vector2Int> wide = new HashSet<Vector2Int>();        // douve de 5 m
        public readonly Dictionary<Vector2Int, Vector2Int> wideStairs = new Dictionary<Vector2Int, Vector2Int>();   // vers la voisine
        public readonly Dictionary<Vector2Int, Vector2Int> outward = new Dictionary<Vector2Int, Vector2Int>();     // vers l'Échiquier
        public readonly List<RoundPlan> rounds = new List<RoundPlan>();
        public readonly Dictionary<Room, int> natures = new Dictionary<Room, int>();
    }

    // Avant l'Échiquier : structure, remparts, entrées, Tours et Donjon, puis les salles et corridors du Château.
    static ChateauPlan PlanChateau(Maze m, int seed, int zone)
    {
        var plan = new ChateauPlan { rnd = new System.Random(seed * 37 + zone * 6151) };
        var rnd = plan.rnd;
        var site = plan.site = MarkStructure(m, TerrainKind.Chateau, Terrains.TemplateOf(seed, zone));
        int n = m.N;
        m.voidCell = new bool[n, n];
        m.voidCeil = ChateauHeight;
        m.structureBevelChance = 0.3f;
        m.archChance = 1f;
        m.archDoorways = true;
        m.structureCeilingSlab = RoofSlab;
        m.structureCorridorCeil = 8f;
        m.brickWalls = true;
        m.greyCeilings = true;

        // Douve et rebord (à 1 cellule de l'Échiquier), rempart (à 2 cellules).
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!m.InStructure(i, j) || m.Fixed(i, j)) continue;
                var c = new Vector2Int(i, j);
                if (site.dist[i, j] == 1) plan.ring1.Add(c);
                else if (site.dist[i, j] == 2) plan.ring2.Add(c);
                else continue;
                m.voidCell[i, j] = true;
            }
        // Entrées : une ligne droite à travers la douve et le rempart, jusqu'à une cour intérieure.
        PickCrossings(m, site, 3, c => true, rnd);
        foreach (var c in site.crossings)
        {
            var landing = c.At(3);
            m.voidCell[landing.x, landing.y] = true;
            plan.courts.Add(landing);
        }
        // Douve de 5 m par endroits (bord droit, loin des entrées), avec un escalier vers une voisine au bord droit.
        foreach (var c in plan.ring1)
        {
            int toE = -1, count = 0;
            for (int d = 0; d < 4; d++)
            {
                var q = c + new Vector2Int(DI[d], DJ[d]);
                if (m.InGrid(q.x, q.y) && !m.InStructure(q.x, q.y)) { toE = d; count++; }
            }
            if (count != 1) continue;
            plan.outward[c] = new Vector2Int(DI[toE], DJ[toE]);
        }
        var ringList = new List<Vector2Int>(plan.ring1);
        ringList.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in ringList)
        {
            if (!plan.outward.TryGetValue(c, out var o) || rnd.NextDouble() >= WideMoatChance) continue;
            if (site.crossings.Exists(g => Mathf.Abs(g.At(1).x - c.x) + Mathf.Abs(g.At(1).y - c.y) <= 2)) continue;
            if (System.Linq.Enumerable.Any(plan.wide, w => Mathf.Abs(w.x - c.x) + Mathf.Abs(w.y - c.y) <= 3)) continue;
            var side = new Vector2Int(o.y, o.x);
            var options = new List<Vector2Int>();
            foreach (var s in new[] { side, -side })
            {
                var nb = c + s;
                if (plan.outward.TryGetValue(nb, out var no) && no == o && !plan.wide.Contains(nb)) options.Add(s);
            }
            if (options.Count == 0) continue;
            plan.wide.Add(c);
            plan.wideStairs[c] = options[rnd.Next(options.Count)];
        }

        // Tours et Donjon : sur des cours dégagées, loin des remparts, du Hub et de la Neutral Room.
        bool NearKeep(int i, int j) => Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 2 || m.neutralRoom.Contains(new Vector2Int(i, j))
            || Mathf.Abs(i - m.doorCol) <= 2 && j >= m.N - 5;
        bool PlotFree(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            int i0 = Mathf.FloorToInt((center.x - plot - o.x) / Cell), i1 = Mathf.FloorToInt((center.x + plot - o.x) / Cell);
            int j0 = Mathf.FloorToInt((center.z - plot - o.z) / Cell), j1 = Mathf.FloorToInt((center.z + plot - o.z) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!m.InStructure(i, j) || m.Fixed(i, j) || site.dist[i, j] < 3 || NearKeep(i, j) || plan.courts.Contains(new Vector2Int(i, j))) return false;
                    if (site.crossings.Exists(g => g.At(3) == new Vector2Int(i, j) || g.At(4) == new Vector2Int(i, j))) return false;
                }
            foreach (var other in plan.rounds)
                if (Flat(other.center - center) < other.radius + plot + 3f) return false;
            return true;
        }
        void MarkPlot(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            for (int i = Mathf.FloorToInt((center.x - plot - o.x) / Cell); i <= Mathf.FloorToInt((center.x + plot - o.x) / Cell); i++)
                for (int j = Mathf.FloorToInt((center.z - plot - o.z) / Cell); j <= Mathf.FloorToInt((center.z + plot - o.z) / Cell); j++)
                {
                    if (!m.InStructure(i, j)) continue;
                    m.voidCell[i, j] = true;
                    plan.courts.Add(new Vector2Int(i, j));
                }
        }
        // Cellules libres de la structure (pour tirer un centre).
        var inner = new List<Vector2Int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (m.InStructure(i, j) && site.dist[i, j] >= 3 && !m.Fixed(i, j)) inner.Add(new Vector2Int(i, j));
        bool TryRound(float radius, float height, int kind, Rect? area, int attempts)
        {
            if (inner.Count == 0) return false;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var cell = inner[rnd.Next(inner.Count)];
                Vector3 c = m.Center(cell.x, cell.y) + new Vector3(Rand(rnd, -2.5f, 2.5f), 0f, Rand(rnd, -2.5f, 2.5f));
                if (area.HasValue && !area.Value.Contains(new Vector2(c.x, c.z))) continue;
                if (!PlotFree(c, radius + 2f)) continue;
                MarkPlot(c, radius + 2f);
                plan.rounds.Add(new RoundPlan { center = c, radius = radius, height = height, kind = kind });
                return true;
            }
            return false;
        }
        // Le Donjon d'abord, au centre de la zone (les 4 parcelles du milieu) si la structure y passe ; sinon ailleurs, plus petit s'il
        // le faut.
        {
            float radius = Rand(rnd, 15f, 20f) * 1.5f * 0.5f;
            Vector2 lo = GridPoint(zone, new Vector2(1f, 1f)), hi = GridPoint(zone, new Vector2(3f, 3f));
            var center = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            if (!TryRound(radius, TowerHeight * 1.5f, 1, center, 300) && !TryRound(radius * 0.8f, TowerHeight * 1.5f, 1, center, 200)
                && !TryRound(radius * 0.8f, TowerHeight * 1.5f, 1, null, 300))
                TryRound(radius * 0.6f, TowerHeight * 1.5f, 1, null, 300);
        }
        // Jusqu'à 6 Tours.
        int towers = 3 + rnd.Next(MaxTowers - 2);
        for (int k = 0; k < towers; k++) TryRound(Rand(rnd, 7.5f, 10f), TowerHeight, 0, null, 120);

        // Salles et corridors dans le reste de la structure.
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 4, roomCoverage = 0.85f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = 12f, floors = new[] { 0f }, ceilings = new[] { 8f, 9f, 10f, 11f, 12f },
            chaoticCeilings = new[] { 8f, 10f, 12f }, pitCeilings = new[] { 8f }, fixedRooms = true,
        };
        Generate(m, rnd, style, (i, j) => m.InStructure(i, j));
        foreach (Room room in m.rooms)
            if (!room.plain) { room.plain = true; plan.natures[room] = rnd.Next(RoomNatures); }
        return plan;
    }

    // Point d'une grille de gabarit (4 x 4) en coordonnées monde (x, z).
    static Vector2 GridPoint(int zone, Vector2 g) => new Vector2(OriginX + g.x * ZoneSize / 4f, ZoneSouth(zone) + g.y * ZoneSize / 4f);

    // Après l'Échiquier : passages des entrées, cours reliées aux salles.
    static void FinishChateauPlan(Maze m, ChateauPlan plan)
    {
        foreach (var c in plan.site.crossings)
            for (int k = 0; k < 3; k++)
            {
                var q = c.At(k);
                SetWall(m, q.x, q.y, c.d, false);
            }
        // Le rebord se longe librement ; les cours voisines communiquent.
        foreach (var set in new[] { plan.ring1, plan.courts })
            foreach (var c in set)
                foreach (int d in new[] { 0, 2 })
                    if (set.Contains(c + new Vector2Int(DI[d], DJ[d]))) SetWall(m, c.x, c.y, d, false);
        OpenCourtyards(m, plan.courts, plan.rnd);
    }

    // Les cours s'ouvrent sur les salles et corridors voisins : tout ce qui se touche finit relié (chaque cour et chaque ensemble de
    // salles), puis environ une arête sur douze de plus.
    static void OpenCourtyards(Maze m, HashSet<Vector2Int> courts, System.Random rnd)
    {
        bool Town(int i, int j) => m.InStructure(i, j) && !m.Void(i, j) && !m.Reserved(i, j);
        var node = new Dictionary<Vector2Int, int>();
        int nodes = 0;
        // Ensembles de salles (cellules reliées sans mur).
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var c = new Vector2Int(i, j);
                if (!Town(i, j) || node.ContainsKey(c)) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(c);
                node[c] = nodes;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        var nb = q + new Vector2Int(DI[d], DJ[d]);
                        if (!Town(nb.x, nb.y) || node.ContainsKey(nb) || HasWall(m, q.x, q.y, d)) continue;
                        node[nb] = nodes;
                        queue.Enqueue(nb);
                    }
                }
                nodes++;
            }
        // Cours (cellules vides voisines).
        foreach (var c in courts)
        {
            if (node.ContainsKey(c)) continue;
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(c);
            node[c] = nodes;
            while (queue.Count > 0)
            {
                var q = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var nb = q + new Vector2Int(DI[d], DJ[d]);
                    if (!courts.Contains(nb) || node.ContainsKey(nb)) continue;
                    node[nb] = nodes;
                    queue.Enqueue(nb);
                }
            }
            nodes++;
        }
        var parent = new int[nodes];
        for (int k = 0; k < nodes; k++) parent[k] = k;
        int Find(int k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
        var edges = new List<(Vector2Int town, int dir)>();
        foreach (var c in courts)
            for (int d = 0; d < 4; d++)
            {
                var t = c + new Vector2Int(DI[d], DJ[d]);
                if (Town(t.x, t.y) && !m.Fixed(t.x, t.y)) edges.Add((t, d ^ 1));
            }
        edges.Sort((a, b) => a.town.x != b.town.x ? a.town.x.CompareTo(b.town.x) : a.town.y != b.town.y ? a.town.y.CompareTo(b.town.y) : a.dir.CompareTo(b.dir));
        Shuffle(edges, rnd);
        foreach (var (town, dir) in edges)
        {
            var court = town + new Vector2Int(DI[dir], DJ[dir]);
            int a = Find(node[town]), b = Find(node[court]);
            if (a == b && rnd.NextDouble() > 0.08) continue;
            parent[a] = b;
            SetWall(m, town.x, town.y, dir, false);
        }
    }

    // Après la géométrie de l'Échiquier : enceinte, plafond, douves, remparts, entrées, toits, Tours et Donjon.
    static IEnumerator BuildChateauSteps(Maze m, ChateauPlan plan, ZoneRefs refs, Transform root, Transform colliders, int zone)
    {
        var rnd = plan.rnd;
        var site = plan.site;
        var shell = new Kit(root, colliders, "Z" + zone + "_Chateau");
        var walls = new Kit(root, colliders, "Z" + zone + "_Remparts");

        // Sol des cours intérieures.
        {
            var mask = new bool[m.N, m.N];
            foreach (var c in plan.courts) mask[c.x, c.y] = true;
            foreach (Rect r in MaskRects(mask, m.origin.x, m.origin.z, Cell)) shell.Floor(r, 0f);
        }
        BuildMoatsAndRamparts(m, plan, shell, walls);
        yield return null;
        // Entrées : passage sous le rempart (herse ouverte, arche de chaque côté) et pont au-dessus de la douve.
        foreach (var c in site.crossings) BuildCastleEntrance(m, c, root, shell, walls);
        if (site.crossings.Count > 0)
        {
            var first = site.crossings[0];
            refs.structureEntry = m.Center(first.e.x, first.e.y);
        }
        StructureShell(m, shell, ChateauHeight);
        // Toits des salles (escaliers depuis les cours, sauf sur les douves, les remparts et les abords des Tours).
        var busy = new HashSet<Vector2Int>(plan.ring1);
        busy.UnionWith(plan.ring2);
        foreach (var c in site.crossings) busy.Add(c.At(3));
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                Vector3 cc = m.Center(i, j);
                if (plan.rounds.Exists(r => Flat(r.center - cc) < r.radius + 2.6f)) busy.Add(new Vector2Int(i, j));
            }
        BuildRoofs(m, shell, rnd, busy);
        shell.Flush(GameArt.BrickWall);
        walls.Flush(RampartBrick);
        yield return null;

        // Tours et Donjon.
        foreach (var round in plan.rounds)
        {
            var kit = new Kit(root, colliders, "Z" + zone + (round.kind == 1 ? "_Donjon" : "_Tour"));
            var (topFloor, roofAbove) = BuildTower(kit, round, rnd, root);
            // Cloche Décorative au dernier étage (chapitre 7.6), suspendue sous le toit ; c'est l'endroit le plus haut atteignable.
            Vector3 aside = Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f) * Vector3.forward * Mathf.Min(2.5f, round.radius * 0.3f);
            refs.bellSpots.Insert(0, HangingBell(topFloor + aside, roofAbove));
            m.perches.Add((topFloor + Vector3.right * 2f, 0f, 3f));
            kit.Flush(GameArt.BrickWall);
            yield return null;
        }
        int bells = GameRules.HangingBellsInZone(zone);
        if (refs.bellSpots.Count > bells) refs.bellSpots.RemoveRange(bells, refs.bellSpots.Count - bells);

        // Dehors (luminosité ambiante relevée) : cours, douves, rebords et toits ; pas dans les Tours ni le Donjon.
        refs.outdoors = p =>
        {
            if (!m.CellAt(p, out int oi, out int oj) || !m.InStructure(oi, oj)) return false;
            foreach (var r in plan.rounds)
                if (Flat(p - r.center) < r.radius + 0.3f && p.y < r.height + 0.5f) return false;
            return m.Void(oi, oj) || p.y > m.Ceil(oi, oj) + 0.1f;
        };
    }

    // Douves, rebords et remparts, dessinés sur une grille fine de 1 m d'après la distance à l'Échiquier : rebord jusqu'à 2 m, douve
    // jusqu'à 5 m (toute la largeur aux endroits élargis), rempart de 5 à 10 m.
    static void BuildMoatsAndRamparts(Maze m, ChateauPlan plan, Kit shell, Kit walls)
    {
        const int sub = 5;   // cases de 1 m par cellule
        int n = m.N * sub;
        var dist = new int[n, n];
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (m.InStructure(x / sub, z / sub)) dist[x, z] = int.MaxValue;
                else queue.Enqueue(new Vector2Int(x, z));
            }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = c.x + dx, nz = c.y + dz;
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || dist[nx, nz] <= dist[c.x, c.y] + 1) continue;
                    dist[nx, nz] = dist[c.x, c.y] + 1;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
        }
        var gateCells = new HashSet<Vector2Int>();
        foreach (var c in plan.site.crossings) gateCells.Add(c.At(2));
        var ledge = new bool[n, n];
        var moat = new bool[n, n];
        var rampart = new bool[n, n];     // dessus du rempart (créneaux), passages compris
        var solid = new bool[n, n];       // blocs pleins du rempart (sans les passages)
        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                var cell = new Vector2Int(x / sub, z / sub);
                bool r1 = plan.ring1.Contains(cell), r2 = plan.ring2.Contains(cell);
                if (!r1 && !r2) continue;
                int k = dist[x, z];
                if (k <= LedgeWidth && r1 && !plan.wide.Contains(cell)) ledge[x, z] = true;
                else if (k <= MoatEnd && r1) moat[x, z] = true;
                else
                {
                    rampart[x, z] = true;
                    if (!gateCells.Contains(cell)) solid[x, z] = true;
                }
            }
        float x0 = m.origin.x, z0 = m.origin.z;
        foreach (Rect r in MaskRects(ledge, x0, z0, 1f)) shell.Floor(r, 0f);
        foreach (Rect r in MaskRects(moat, x0, z0, 1f)) shell.Floor(r, -MoatDepth);
        foreach (Rect r in MaskRects(solid, x0, z0, 1f))
            walls.Box(new Vector3(r.center.x, (RampartHeight - MoatDepth) * 0.5f, r.center.y), new Vector3(r.width, RampartHeight + MoatDepth, r.height));
        // Parois des douves (sauf contre le rempart, qui descend jusqu'au fond).
        bool Inside(int x, int z) => x >= 0 && z >= 0 && x < n && z < n;
        foreach (var (a, b, inward) in MaskEdges(moat, x0, z0, 1f, (x, z) => !Inside(x, z) || !moat[x, z] && !solid[x, z]))
        {
            bool alongX = inward.x == 0f;
            float length = alongX ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
            Vector2 mid = (a + b) * 0.5f + inward * 0.1f;
            float h = MoatDepth - 0.01f;
            shell.Box(new Vector3(mid.x, -MoatDepth + h * 0.5f, mid.y), alongX ? new Vector3(length, h, 0.2f) : new Vector3(0.2f, h, length));
        }
        // Créneaux au sommet du rempart, sur ses deux bords.
        foreach (var (a, b, inward) in MaskEdges(rampart, x0, z0, 1f, (x, z) => !Inside(x, z) || !rampart[x, z] && m.InStructure(x / sub, z / sub)))
            KitMerlons(walls, new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y), RampartHeight, 0.3f, new Vector3(inward.x, 0f, inward.y), null, null, false);
        // Escaliers des endroits élargis : contre le bord extérieur de la douve, dans le sens de la longueur, jusqu'au rebord voisin.
        float run = StairRun(MoatDepth);
        foreach (var kv in plan.wideStairs)
        {
            Vector2Int c = kv.Key, along = kv.Value, o = plan.outward[c];
            Vector3 center = m.Center(c.x, c.y), dir = new Vector3(along.x, 0f, along.y), toE = new Vector3(o.x, 0f, o.y);
            Vector3 lane = center + toE * (Cell * 0.5f - LedgeWidth * 0.5f);
            Vector3 head = lane + dir * (Cell * 0.5f);
            KitStairs(shell, head - dir * run, dir, LedgeWidth, -MoatDepth, 0f);
        }
    }

    // Entrée d'un rempart : pont au-dessus de la douve (avec ses parapets), passage de 4 x 5 m sous le rempart, herse ouverte au
    // milieu, arche sur chaque face.
    static void BuildCastleEntrance(Maze m, Crossing c, Transform root, Kit shell, Kit walls)
    {
        Vector3 dir = new Vector3(c.Step.x, 0f, c.Step.y), side = new Vector3(c.Step.y, 0f, c.Step.x);
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        Vector3 boundary = m.Center(c.e.x, c.e.y) + dir * (Cell * 0.5f);
        Vector3 Along(float t, float s, float y) => boundary + dir * t + side * s + Vector3.up * y;
        Vector3 Size(float lengthAlong, float acrossSide, float h) => alongX ? new Vector3(lengthAlong, h, acrossSide) : new Vector3(acrossSide, h, lengthAlong);
        Rect Area(float t0, float t1, float s0, float s1)
        {
            Vector3 a = Along(t0, s0, 0f), b = Along(t1, s1, 0f);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }
        // Pont (de 2 à 5 m du bord), dalle de 0,4 m et parapets.
        shell.Floor(Area(LedgeWidth - 0.05f, MoatEnd + 0.05f, -GateWidth * 0.5f, GateWidth * 0.5f), 0f, 0.4f);
        shell.Box(Along((LedgeWidth + MoatEnd) * 0.5f, 0f, -0.21f), Size(MoatEnd - LedgeWidth, GateWidth, 0.4f), false);
        foreach (float s in new[] { -1f, 1f })
            shell.Box(Along((LedgeWidth + MoatEnd) * 0.5f, s * (GateWidth * 0.5f - 0.12f), 0.45f), Size(MoatEnd - LedgeWidth, 0.24f, 0.9f));
        // Passage sous le rempart (de 5 à 10 m du bord).
        float t0 = Cell, t1 = Cell * 2f, mid = (t0 + t1) * 0.5f, sideWall = (Cell - GateWidth) * 0.5f;
        shell.Floor(Area(t0, t1, -GateWidth * 0.5f, GateWidth * 0.5f), 0f, MoatDepth);
        foreach (float s in new[] { -1f, 1f })
            walls.Box(Along(mid, s * (GateWidth * 0.5f + sideWall * 0.5f), (RampartHeight - MoatDepth) * 0.5f), Size(Cell, sideWall, RampartHeight + MoatDepth));
        walls.Box(Along(mid, 0f, (GateHeight + RampartHeight) * 0.5f), Size(Cell, GateWidth, RampartHeight - GateHeight));
        Quaternion rot = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        foreach (float t in new[] { t0, t1 }) DoorArch(walls.walls, Along(t, 0f, 0f), rot, GateWidth, GateHeight, 0.6f);
        var gate = CastleGate.Create(root, Along(mid, 0f, 0f), rot, GateWidth, GateHeight);
        gate.SetOpen(true, true);
    }

    // Briques noires et blanches des remparts, lumineuses : on les voit malgré la distance et l'éclairage.
    static Material rampartBrick;
    static Material RampartBrick
    {
        get
        {
            if (rampartBrick != null) return rampartBrick;
            Material brick = GameArt.BrickWall;
            var mat = new Material(GameArt.Emissive("RempartLumineux", Color.white, Color.white)) { name = "RempartLumineux" };
            mat.mainTexture = brick.mainTexture;
            mat.color = brick.color;
            mat.SetTexture("_EmissionMap", brick.mainTexture);
            mat.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return rampartBrick = mat;
        }
    }

    // Toits des salles et corridors (visibles depuis les Tours), ceints de créneaux au-dessus des cours.
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
        bool Building(int i, int j) => m.InGrid(i, j) && m.InStructure(i, j) && !m.Void(i, j) && !m.Reserved(i, j) && !m.Fixed(i, j);
        bool Court(int i, int j) => m.InGrid(i, j) && m.InStructure(i, j) && m.Void(i, j);
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
        var parent = new int[regions + 1];
        for (int r = 0; r <= regions; r++) parent[r] = r;
        int Find(int r) { while (parent[r] != r) r = parent[r] = parent[parent[r]]; return r; }
        var skip = new HashSet<(int, int, int)>();
        var used = new HashSet<Vector2Int>(busy);
        int ground = regions;   // nœud "sol" de l'union
        int placed = 0;
        bool SameBase(Vector2Int c, Vector2Int from) => from == c || (Court(from.x, from.y)
            ? Court(c.x, c.y) && !busy.Contains(c)
            : Building(c.x, c.y) && region[c.x, c.y] == region[from.x, from.y]);
        // Escalier vers le bord du toit de la cellule 'top', depuis la cellule voisine 'from' (dans la direction 'd' depuis 'top') : une
        // cour (depuis le sol) ou un toit plus bas. Il monte droit vers le toit (perpendiculaire), ou le long de son bord (parallèle),
        // et n'occupe que des cellules de même niveau que 'from' ; dans une cour, il ne cache aucune porte.
        bool TryStair(Vector2Int top, int d, int mode)
        {
            var from = top + new Vector2Int(DI[d], DJ[d]);
            bool fromGround = Court(from.x, from.y);
            float baseY = fromGround ? 0f : m.Ceil(from.x, from.y) + RoofSlab, h = m.Ceil(top.x, top.y) + RoofSlab;
            if (h - baseY < 0.5f) return false;
            float run = StairRun(h - baseY);
            int k = Mathf.CeilToInt((run + 0.6f) / Cell);
            Vector3 toTop = -new Vector3(DI[d], 0f, DJ[d]);
            var step = mode == 0 ? new Vector2Int(DI[d], DJ[d]) : (d < 2 ? new Vector2Int(0, 1) : new Vector2Int(1, 0)) * (mode == 1 ? 1 : -1);
            var cells = new List<Vector2Int>();
            for (int t = 0; t < k; t++)
            {
                var c = from + step * t;
                if (!SameBase(c, from) || used.Contains(c) || !fromGround && busy.Contains(c)) return false;
                if (fromGround)
                {
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
                        bool groundSide = Court(ni, nj) && !busy.Contains(new Vector2Int(ni, nj));
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
                int a = Find(region[top.x, top.y]), b = Find(Court(from.x, from.y) ? ground : region[from.x, from.y]);
                if (a == b) continue;
                if (TryStair(top, d, mode)) progress = true;
            }
        }
        // Au moins 8 depuis le sol.
        foreach (var (top, d, mode) in candidates)
        {
            if (placed >= RoofStairsFromGround) break;
            var from = top + new Vector2Int(DI[d], DJ[d]);
            if (Court(from.x, from.y)) TryStair(top, d, mode);
        }
        int components = 0, reached = 0;
        for (int r = 0; r < regions; r++)
            if (Find(r) == r) components++;
        for (int r = 0; r < regions; r++) if (Find(r) == Find(ground)) reached++;
        RoofReport = placed + " escaliers depuis le sol, " + reached + "/" + regions + " terrasses reliées au sol, " + components + " ensembles isolés";

        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!m.InStructure(i, j) || m.Void(i, j) || m.Reserved(i, j)) continue;
                float ceil = m.Ceil(i, j), roof = ceil + RoofSlab;
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f + Wall * 0.5f;
                kit.floors.WorldQuad(new Vector3(c.x - e, roof, c.z - e), new Vector3(c.x + e, roof, c.z - e),
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

    // ---------- Tours et Donjon ----------

    // Tour ronde (Tour ou Donjon) : mur percé d'une porte (sous une arche) et de meurtrières, créneaux, escalier en colimaçon de 3 m
    // de large jusqu'au dernier étage, étages intermédiaires une fois sur deux. V0.7.14 : le toit est fermé (on ne peut ni y monter ni
    // sortir de la Tour par le haut) ; le dernier étage est 4 m dessous. Retourne le centre du dernier étage et sa hauteur sous toit.
    const float TowerTopRoom = 4f;

    static (Vector3 topFloor, float headroom) BuildTower(Kit kit, RoundPlan plan, System.Random rnd, Transform root)
    {
        float R = plan.radius, H = plan.height, top = H - TowerTopRoom;
        Vector3 c = plan.center;
        const int sides = 24;
        float doorAngle = Rand(rnd, 0f, 360f);
        var slits = new List<(int, float)>();
        int slitCount = 15 + rnd.Next(8);
        for (int k = 0; k < slitCount; k++) slits.Add((rnd.Next(sides), Rand(rnd, 4f, H - 3f)));
        KitRoundWall(kit, c, R, 0f, H, sides, new List<float> { doorAngle }, 2.6f, 3.2f, slits);
        RoundDoorArch(kit, c, R, doorAngle, 2.6f, 3.2f);
        // Escalier contre le mur, de la porte jusqu'au dernier étage.
        float rOut = R - 1.05f, rIn = rOut - TowerStairWidth;
        float end = KitSpiral(kit, c, rIn, rOut, 0f, top, doorAngle + 25f);
        // Étages (à 10 et 20 m pour une Tour, 15 et 30 m pour le Donjon), une fois sur deux, au centre (l'escalier passe autour).
        float step = H / 3f, lowestCeiling = top;
        foreach (float level in new[] { step, step * 2f })
            if (level < top - 2.5f && rnd.NextDouble() < 0.5)
            {
                KitDisc(kit, c, rIn - 0.05f, level, 0.35f, sides);
                lowestCeiling = Mathf.Min(lowestCeiling, level - 0.35f);
            }
        // Dernier étage : dalle pleine, ouverte au-dessus de l'arrivée de l'escalier ; puis le toit fermé et les créneaux autour.
        float arrivalSpan = 2.2f / SpiralRise * (SpiralTread / ((rIn + rOut) * 0.5f)) * Mathf.Rad2Deg;
        KitDisc(kit, c, R - 1f, top, 0.5f, sides, end - arrivalSpan, end + 2f, 0f);
        KitDisc(kit, c, R - 0.5f, H + 0.5f, 0.5f, sides);
        KitRoundMerlons(kit, c, R, H + 0.5f, sides);
        // Lustres : sous le premier plafond, et sous le toit (aucun décor ne flotte).
        AddChandelier(root, c + Vector3.up * lowestCeiling, 16f, 1.4f, rnd);
        if (lowestCeiling < top - 0.1f) AddChandelier(root, c + Vector3.up * H, 10f, 1.1f, rnd);
        return (c + Vector3.up * top, H - top);
    }

    const float TowerStairWidth = 3f;

    // Arche autour d'une porte percée dans un mur rond (épaisseur 1 m), face à l'angle 'angle'.
    static void RoundDoorArch(Kit kit, Vector3 center, float radius, float angle, float width, float height)
    {
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        DoorArch(kit.walls, center + rot * Vector3.forward * (radius - 0.5f), rot, width, height, 1.3f);
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

    // V0.7.14 (Note 01) : plus de torches dans le Château.
    static void FurnishCastle(Maze m, Transform root, Dictionary<Room, int> natures, System.Random rnd)
    {
        var props = new GameObject("Mobilier_Z" + m.zone).transform;
        props.SetParent(root, false);
        foreach (var kv in natures)
        {
            Room room = kv.Key;
            if (room.cells.Count == 0) continue;
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
                    ? PlaceOnFloor(m, room, go, cell, 0f, Rand(rnd, 0f, 360f), placed, rnd) && FitInCell(m, room, go, cell)
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

    // V0.7.13 (Note 24) : l'objet posé au sol reste dans sa cellule, sans entrer dans les murs (ni boucher une porte) : il est
    // repoussé à l'intérieur ; s'il est trop grand pour la cellule, il n'est pas posé.
    static bool FitInCell(Maze m, Room room, GameObject go, Vector2Int cell)
    {
        Bounds wb = WorldBounds(go.transform);
        Vector3 c = m.Center(cell.x, cell.y), shift = Vector3.zero;
        float lim = Cell * 0.5f - Wall * 0.5f - 0.08f;
        bool Closed(int d)
        {
            int ni = cell.x + DI[d], nj = cell.y + DJ[d];
            return !m.InGrid(ni, nj) || m.RoomAt(ni, nj) != room || HasWall(m, cell.x, cell.y, d);
        }
        if (Closed(0) && Closed(1) && wb.size.x > lim * 2f || Closed(2) && Closed(3) && wb.size.z > lim * 2f) return false;
        if (Closed(0) && wb.max.x > c.x + lim) shift.x -= wb.max.x - (c.x + lim);
        if (Closed(1) && wb.min.x + shift.x < c.x - lim) shift.x += (c.x - lim) - (wb.min.x + shift.x);
        if (Closed(2) && wb.max.z > c.z + lim) shift.z -= wb.max.z - (c.z + lim);
        if (Closed(3) && wb.min.z + shift.z < c.z - lim) shift.z += (c.z - lim) - (wb.min.z + shift.z);
        go.transform.position += shift;
        return true;
    }

    // Boîte englobante (monde) des rendus d'un objet.
    static Bounds WorldBounds(Transform t)
    {
        var b = new Bounds(t.position, Vector3.zero);
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
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

    // Emplacements de monticules dans les cours intérieures (au sol).
    static void CourtyardSpots(Maze m, ZoneRefs refs, HashSet<Vector2Int> courts, System.Random rnd)
    {
        var sorted = new List<Vector2Int>(courts);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in sorted)
        {
            if (rnd.NextDouble() > 0.35) continue;
            Vector3 p = m.Center(c.x, c.y) + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f));
            if (!Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.95f || !hit.transform.IsChildOf(refs.structure)) continue;
            refs.itemSpots.Add(hit.point);
        }
    }
}
