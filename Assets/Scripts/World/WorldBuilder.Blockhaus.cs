using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Chapitre 11.4 : structure Blockhaus. V0.7.14 : le Blockhaus n'est plus une zone entière mais une structure posée dans une zone
// d'Échiquier (parcelles bleues d'un gabarit, WorldBuilder.Structure) :
// - 20 m de hauteur et 10 m de profondeur (30 m praticables) : 3 étages de 10 m (de -10 à +20 m), appartements et couloirs (20 % de
//   couloirs pour 80 % d'appartements) ; murs et sols en damier noir et blanc, tous les plafonds gris (béton) ;
// - entre un couloir et un appartement : une ouverture, garnie d'une arche 40 % du temps ; encadrements des portes et des fenêtres
//   gris (béton), en relief de 0,12 m ; plinthes grises de 0,3 m (relief 0,12 m), continues ;
// - environ 5 % des appartements fermés par une porte sous cadenas (la Clé l'ouvre), qui contiennent un Super Monticule, une Cloche
//   Décorative ou deux monticules simples ; en Zone 3, l'un d'eux abrite le cadavre de Tête-de-Cloche ;
// - au moins 2 escaliers en colimaçon (3 m de large) par structure ; 20 % des appartements font 10 m de plus en hauteur ou en
//   profondeur ; plus de bifurcations dans les couloirs ; 30 % des couloirs ont un renfoncement en biseau ;
// - délimité par un ravin de 15 m de large (de -80 à +80 m) qui le sépare de l'Échiquier ; 3 ponts (ceux de l'Échiquier) par ravin,
//   espacés d'au moins 30 m, d'une fenêtre à la fenêtre d'en face. V0.7.15 (Note 02) : la façade est une grille régulière, une
//   ouverture par cellule à chaque étage (néon au-dessus), et plus haut comme plus bas, jusqu'en haut de la façade et jusqu'au fond
//   du ravin, des fenêtres factices (le modèle de l'Échiquier) de la même taille, au même pas ; la façade se voit même de loin et
//   sans éclairage. Y tomber tue (GameSession). Plinthes autour des bouts de murs et des tableaux des portes (Note 01).
//   V0.7.16 (Note 01) : la façade du Blockhaus (sa face sur le ravin) est grise (béton), ses autres murs gardent le damier ; la paroi
//   d'en face, côté Échiquier, n'a plus de fenêtre (ni factice, ni percée dans son mur) : seuls les départs des ponts l'ouvrent.
public static partial class WorldBuilder
{
    // V0.7.14 : 10 m de profondeur (le plus bas étage), 20 m de hauteur ; ravin de 15 m (3 cellules).
    public const float BlockhausDepth = 10f;
    const float BlockhausTop = 20f;
    const float StoreyHeight = 10f, RavineReach = 80f;
    const int RavineCells = 3;
    static readonly float[] StoreyLevels = { -10f, 0f, 10f };
    const int HubStorey = 1;
    const float BlockArchChance = 0.4f, LockedRoomChance = 0.05f, ExtraCorridorLink = 0.35f;
    // V0.7.14 : plinthes de 0,3 m en relief de 0,12 m, grises (béton) ; encadrements des portes et fenêtres de 0,12 m de relief.
    const float PlinthHeight = 0.3f, PlinthRelief = 0.12f, FrameWidth = 0.16f, FrameRelief = 0.12f, WindowLow = 1f, WindowHigh = 3.8f;
    // V0.7.14 : néons des façades sur le ravin, 80 % de volume en plus (chaque dimension x 1,8^(1/3)).
    static readonly float RavineNeonScale = Mathf.Pow(1.8f, 1f / 3f);

    // Appartement fermé sous cadenas : son étage, la pièce, l'arête de sa porte.
    internal class LockedRoom { public int storey; public Room room; public (int, int, bool) edge; }

    internal class BlockhausPlan
    {
        public StructureSite site;
        public System.Random rnd;
        public readonly HashSet<Vector2Int> ravine = new HashSet<Vector2Int>(), building = new HashSet<Vector2Int>();
        public readonly List<Vector2Int> wells = new List<Vector2Int>();
        public readonly HashSet<Vector2Int> wellCells = new HashSet<Vector2Int>();
        public Maze[] storeys;
        public readonly List<LockedRoom> locked = new List<LockedRoom>();
        public readonly List<(Vector3 a, Vector3 b)> bridges = new List<(Vector3, Vector3)>();
    }

    // Avant l'Échiquier : structure, ravin, cages d'escalier, puis les 3 étages (celui du Hub dans la grille de la zone), les
    // appartements fermés et les ponts.
    static BlockhausPlan PlanBlockhaus(Maze main, int seed, int zone)
    {
        var plan = new BlockhausPlan { rnd = new System.Random(seed * 43 + zone * 5381) };
        var rnd = plan.rnd;
        var site = plan.site = MarkStructure(main, TerrainKind.Blockhaus, Terrains.TemplateOf(seed, zone));
        int n = main.N;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!main.InStructure(i, j)) continue;
                var c = new Vector2Int(i, j);
                bool nearRoom = main.neutralRoom.Contains(c) || Mathf.Abs(i - main.doorCol) <= 2 && j >= n - 5;
                if (site.dist[i, j] <= RavineCells && !nearRoom) plan.ravine.Add(c);
                else plan.building.Add(c);
            }
        // Cages d'escalier (2 x 2 cellules) dans le bâtiment, au moins 2.
        int wanted = Mathf.Clamp(2 + plan.building.Count / 150, 2, 5);
        var buildingList = plan.building.OrderBy(c => c.x).ThenBy(c => c.y).ToList();
        for (int k = 0; k < wanted; k++)
            for (int attempt = 0; attempt < 400 && buildingList.Count > 0; attempt++)
            {
                var c = buildingList[rnd.Next(buildingList.Count)];
                bool ok = true;
                for (int i = c.x - 1; i <= c.x + 2 && ok; i++)
                    for (int j = c.y - 1; j <= c.y + 2 && ok; j++)
                    {
                        var q = new Vector2Int(i, j);
                        if (!plan.building.Contains(q) || main.Fixed(i, j) || main.neutralRoom.Contains(q) || Mathf.Abs(i - main.doorCol) <= 2 && j >= n - 5
                            || plan.wells.Exists(w => Mathf.Abs(w.x - i) <= 3 && Mathf.Abs(w.y - j) <= 3)) ok = false;
                    }
                if (!ok) continue;
                plan.wells.Add(c);
                break;
            }
        foreach (var w in plan.wells) for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) plan.wellCells.Add(new Vector2Int(w.x + a, w.y + b));

        // ---------- Les 3 étages ----------
        plan.storeys = new Maze[StoreyLevels.Length];
        bool[,] doubleBelow = new bool[n, n];
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 3, roomCoverage = 0.8f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = StoreyHeight, floors = new[] { 0f }, ceilings = new[] { StoreyHeight },
            chaoticCeilings = new[] { StoreyHeight }, pitCeilings = new[] { StoreyHeight }, fixedRooms = true,
        };
        bool Inside(int i, int j) => plan.building.Contains(new Vector2Int(i, j));
        var bridgeCells = new HashSet<Vector2Int>();
        for (int s = 0; s < StoreyLevels.Length; s++)
        {
            Maze m = main;
            if (s != HubStorey)
            {
                // Hors de l'étage du Hub : rien hors de la structure ; la Neutral Room (si elle est dans la structure) est murée.
                m = new Maze(zone, n, ZoneOrigin(zone), false, main.start);
                m.structure = main.structure;
                m.structureKind = TerrainKind.Blockhaus;
                if (main.neutralRoom.xMin >= 0 && plan.building.Contains(new Vector2Int(main.doorCol, n - 2)))
                {
                    for (int i = main.neutralRoom.xMin; i < main.neutralRoom.xMax; i++)
                        for (int j = main.neutralRoom.yMin; j < main.neutralRoom.yMax; j++) m.fixedCell[i, j] = true;
                    AddFixedRoom(m, main.neutralRoom, StoreyHeight);
                }
            }
            m.voidWallFull = true;
            m.voidCeil = StoreyHeight;
            m.greyCeilings = true;
            m.structureBevelChance = 0.3f;
            // Le sol de l'étage du dessus sert de plafond (pas de collision de plafond qui dépasserait chez lui).
            m.structureNoCeilingColliders = s < StoreyLevels.Length - 1;
            m.voidCell ??= new bool[n, n];
            m.silentVoid ??= new bool[n, n];
            foreach (var c in plan.ravine) m.voidCell[c.x, c.y] = true;
            foreach (var c in plan.wellCells) m.voidCell[c.x, c.y] = true;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (s != HubStorey && !m.InStructure(i, j)) { m.voidCell[i, j] = true; m.silentVoid[i, j] = true; }
                    if (doubleBelow[i, j]) { m.voidCell[i, j] = true; m.silentVoid[i, j] = true; }
                }
            Generate(m, rnd, style, Inside);
            foreach (Room room in m.rooms)
                if (room.cells.Count > 0 && Inside(room.cells[0].x, room.cells[0].y)) room.plain = true;
            // V0.7.12 : plus de bifurcations, les couloirs voisins s'ouvrent souvent les uns sur les autres.
            foreach (var c in plan.building)
                foreach (int d in new[] { 0, 2 })
                {
                    int ni = c.x + DI[d], nj = c.y + DJ[d];
                    if (!Corridor(m, c.x, c.y) || !Corridor(m, ni, nj) || !Inside(ni, nj) || !HasWall(m, c.x, c.y, d) || rnd.NextDouble() >= ExtraCorridorLink) continue;
                    SetWall(m, c.x, c.y, d, false);
                }
            // Appartements de double hauteur (rognant l'étage du dessus), sauf au dernier étage et sous la Neutral Room.
            doubleBelow = new bool[n, n];
            if (s < StoreyLevels.Length - 1)
                foreach (Room room in m.rooms)
                {
                    if (room.cells.Count == 0 || !Inside(room.cells[0].x, room.cells[0].y) || m.Fixed(room.cells[0].x, room.cells[0].y) || rnd.NextDouble() >= 0.2) continue;
                    if (room.cells.Exists(c => main.neutralRoom.Contains(c) || Mathf.Abs(c.x - main.doorCol) <= 2 && c.y >= n - 5)) continue;
                    room.ceil = StoreyHeight * 2f;
                    foreach (var c in room.cells) doubleBelow[c.x, c.y] = true;
                }
            // Portes : entre un couloir et un appartement, une par paire voisine ; fenêtres sur le ravin.
            foreach (var c in plan.building)
            {
                int i = c.x, j = c.y;
                if (m.Void(i, j) || m.Fixed(i, j)) continue;
                foreach (int d in new[] { 0, 2 })
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (!Inside(ni, nj) || m.Void(ni, nj) || m.Fixed(ni, nj) || HasWall(m, i, j, d)) continue;
                    if ((m.RoomAt(i, j) == null) != (m.RoomAt(ni, nj) == null)) m.doorEdges.Add((i, j, d == 2));
                }
                // V0.7.15 (Note 02) : une fenêtre sur le ravin dans chaque cellule de la façade, à chaque étage.
                for (int d = 0; d < 4; d++)
                {
                    if (!plan.ravine.Contains(new Vector2Int(i + DI[d], j + DJ[d]))) continue;
                    m.wallHoles[EdgeKey(i, j, d)] = (WindowLow, WindowHigh);
                }
            }
            // Cages d'escalier : une porte vers un couloir ou un appartement voisin à chaque étage.
            foreach (var w in plan.wells)
            {
                var sides = new List<(Vector2Int cell, int dir)>();
                foreach (var c in new[] { w, w + Vector2Int.right, w + Vector2Int.up, w + Vector2Int.one })
                    for (int d = 0; d < 4; d++)
                    {
                        var nb = c + new Vector2Int(DI[d], DJ[d]);
                        if (plan.wellCells.Contains(nb) || !Inside(nb.x, nb.y) || m.Void(nb.x, nb.y) || m.Fixed(nb.x, nb.y)) continue;
                        sides.Add((nb, d ^ 1));
                    }
                if (sides.Count == 0) continue;
                var (cell, dir) = sides[rnd.Next(sides.Count)];
                SetWall(m, cell.x, cell.y, dir, false);
            }
            // Ponts du ravin (à l'étage du Hub) : choisis avant les appartements fermés, qui ne doivent pas les boucher.
            if (s == HubStorey)
            {
                PickCrossings(main, site, RavineCells + 1, c =>
                {
                    var b = c.At(RavineCells + 1);
                    return Inside(b.x, b.y) && !main.Void(b.x, b.y) && !main.Fixed(b.x, b.y) && !plan.wellCells.Contains(b);
                }, rnd);
                foreach (var c in site.crossings) bridgeCells.Add(c.At(RavineCells + 1));
            }
            // V0.7.12 : appartements fermés sous cadenas (leur porte est leur seule entrée).
            LockRooms(m, s, plan.wellCells, plan.locked, rnd, plan.building, s == HubStorey ? bridgeCells : null);
            // Les autres portes : 40 % d'arches (sans battant).
            foreach (var e in m.doorEdges)
                if (!plan.locked.Exists(l => l.storey == s && l.edge.Equals(e)) && rnd.NextDouble() < BlockArchChance) m.archEdges.Add(e);
            plan.storeys[s] = m;
        }
        return plan;
    }

    // Après l'Échiquier : fenêtres du pont de chaque côté du ravin, et quelques fenêtres de l'Échiquier sur le ravin.
    static void FinishBlockhausPlan(Maze m, BlockhausPlan plan)
    {
        foreach (var c in plan.site.crossings)
        {
            var b = c.At(RavineCells + 1);
            m.wallHoles[EdgeKey(c.e.x, c.e.y, c.d)] = (0f, WindowHigh);
            m.wallHoles[EdgeKey(b.x, b.y, c.d ^ 1)] = (0f, WindowHigh);
            // Le long du pont, d'une cellule du ravin à l'autre (chemins : Parfum Odorant).
            for (int k = 1; k < RavineCells; k++) SetWall(m, c.At(k).x, c.At(k).y, c.d, false);
            Vector3 dir = new Vector3(DI[c.d], 0f, DJ[c.d]), lift = Vector3.up * -0.02f;
            Vector3 a = m.Center(c.e.x, c.e.y) + dir * (Cell * 0.5f + Wall * 0.5f) + lift;
            Vector3 bb = m.Center(b.x, b.y) - dir * (Cell * 0.5f + Wall * 0.5f) + lift;
            plan.bridges.Add((a, bb));
        }
        // V0.7.16 (Note 01) : plus de fenêtre de l'Échiquier sur un ravin (Blockhaus, douve du Château).
    }

    // Après la géométrie de l'Échiquier (l'étage du Hub) : les deux autres étages, le ravin, les cages d'escalier, les portes, le décor.
    static IEnumerator BuildBlockhausSteps(Maze main, BlockhausPlan plan, ZoneRefs refs, Transform root, Transform colliders, int seed, int zone)
    {
        var rnd = plan.rnd;
        var storeys = plan.storeys;
        for (int s = 0; s < storeys.Length; s++)
        {
            if (s == HubStorey) continue;
            float y = StoreyLevels[s];
            var storeyRoot = new GameObject("Etage" + s).transform;
            storeyRoot.SetParent(root, false);
            storeyRoot.localPosition = Vector3.up * y;
            var storeyColliders = new GameObject("Colliders_Etage" + s);
            storeyColliders.transform.SetParent(colliders, false);
            storeyColliders.transform.localPosition = Vector3.up * y;
            var geometry = BuildGeometry(storeys[s], storeyRoot, storeyColliders, new System.Random(seed * 97 + zone * 173 + s));
            while (geometry.MoveNext()) yield return null;
            BuildBevels(storeys[s], storeyRoot, new ZoneRefs(), new System.Random(seed * 53 + zone * 929 + s));
            yield return null;
        }

        // ---------- Ravin, cages d'escalier ----------
        var shell = new Kit(root, colliders, "Z" + zone + "_Blockhaus");
        BuildRavine(main, plan, shell, rnd, root);
        foreach (var w in plan.wells) BuildStairwell(main, storeys, w, shell, rnd);
        shell.Flush();
        yield return null;
        if (plan.site.crossings.Count > 0)
        {
            var first = plan.site.crossings[0];
            refs.structureEntry = main.Center(first.e.x, first.e.y);
        }

        // ---------- Portes, éclairage, mobilier, emplacements des monticules ----------
        var props = new GameObject("Decor_Blockhaus_Z" + zone).transform;
        props.SetParent(root, false);
        int doorIds = 0;
        for (int s = 0; s < storeys.Length; s++)
        {
            Maze m = storeys[s];
            float y = StoreyLevels[s];
            // Seuls les appartements fermés ont une porte (sous cadenas) ; les autres passages restent ouverts.
            foreach (var (i, j, north) in m.doorEdges)
            {
                if (!plan.locked.Exists(l => l.storey == s && l.edge.Equals((i, j, north)))) continue;
                Vector3 edge = m.Center(i, j) + (north ? Vector3.forward : Vector3.right) * (Cell * 0.5f) + Vector3.up * y;
                var door = ApartmentDoor.Create(props, zone * 100000 + doorIds++, edge, Quaternion.Euler(0f, north ? 0f : 90f, 0f), DoorWidth - 0.1f, DoorHeight - 0.05f, true);
                refs.lockedDoors.Add(door);
            }
            var finish = new Kit(props, colliders, "Z" + zone + "_Finitions" + s);
            DecorateStorey(m, plan, y, props, finish, refs, rnd, s == HubStorey, plan.locked.FindAll(l => l.storey == s));
            finish.Flush();
            yield return null;
        }

        foreach (var (a, b) in plan.bridges) refs.bridges.Add(SuspendedBridge.Create(props, zone * 100 + 60 + refs.bridges.Count, a, b));
        // Dehors (luminosité ambiante relevée) : dans le ravin ; y tomber tue.
        refs.outdoors = p => main.CellAt(p, out int oi, out int oj) && plan.ravine.Contains(new Vector2Int(oi, oj));
        refs.ravineAt = refs.outdoors;

        // Cloches Décoratives : au dernier étage, suspendues au plafond, s'il en manque encore à la zone.
        Maze top = storeys[storeys.Length - 1];
        var topRooms = top.rooms.FindAll(r => r.cells.Count >= 2 && plan.building.Contains(r.cells[0]) && !top.Fixed(r.cells[0].x, r.cells[0].y) && !plan.locked.Exists(l => l.room == r));
        Shuffle(topRooms, rnd);
        foreach (Room room in topRooms)
        {
            Vector3 c = RoomFloorCenter(top, room, StoreyLevels[storeys.Length - 1]);
            // 17.3 : l'endroit le plus haut atteignable.
            main.perches.Add((new Vector3(c.x + 2f, StoreyLevels[storeys.Length - 1], c.z), 0f, 3f));
            if (refs.bellSpots.Count >= GameRules.HangingBellsInZone(zone)) break;
            refs.bellSpots.Insert(0, HangingBell(c, room.ceil));
        }
        int bells = GameRules.HangingBellsInZone(zone);
        if (refs.bellSpots.Count > bells) refs.bellSpots.RemoveRange(bells, refs.bellSpots.Count - bells);

        // V0.7.12 : contenu des appartements fermés (Super Monticule, Cloche Décorative ou deux monticules simples) ; en Zone 3, le
        // cadavre de Tête-de-Cloche dans l'un d'eux (sous une Cloche, rien au sol).
        Physics.SyncTransforms();
        foreach (var l in plan.locked)
        {
            Maze m = storeys[l.storey];
            Vector3 c = RoomFloorCenter(m, l.room, StoreyLevels[l.storey]);
            if (zone == GameRules.SecretCorpseZone && !refs.secretCorpsePlaced)
            {
                SecretCorpse.Build(root, c, 0f, 2f);
                refs.secretCorpsePlaced = true;
                refs.bellSpots.Add(HangingBell(c, l.room.ceil));
                continue;
            }
            int content = rnd.Next(3);
            if (content == 0) refs.forcedSupers.Add(c);
            else if (content == 1) refs.bellSpots.Add(HangingBell(c, l.room.ceil));
            else
            {
                // Deux monticules, dans deux cellules de l'appartement s'il en a plusieurs.
                Vector3 lift = Vector3.up * StoreyLevels[l.storey];
                bool two = l.room.cells.Count > 1;
                refs.forcedMounds.Add(two ? m.Center(l.room.cells[0].x, l.room.cells[0].y) + lift : c - Vector3.right * 1.6f);
                refs.forcedMounds.Add(two ? m.Center(l.room.cells[1].x, l.room.cells[1].y) + lift : c + Vector3.right * 1.6f);
            }
        }
    }

    // V0.7.13 : une Cloche Décorative pend à 2,3 m au-dessus du sol (jamais plus de 3,5 m) : on l'attrape en sautant. Sa chaîne
    // monte jusqu'au plafond.
    const float BellAboveFloor = 2.3f;
    static (Vector3, float) HangingBell(Vector3 floorCenter, float ceil) =>
        (floorCenter + Vector3.up * BellAboveFloor, Mathf.Max(0.3f, ceil - BellAboveFloor - GameRules.BellHeight - 0.05f));

    // La cellule de la pièce la plus proche de son centre (une pièce en L n'a pas son centre chez elle), au niveau 'y'.
    static Vector3 RoomFloorCenter(Maze m, Room room, float y)
    {
        Vector3 c = Vector3.zero;
        foreach (var cell in room.cells) c += m.Center(cell.x, cell.y);
        c /= room.cells.Count;
        Vector3 best = m.Center(room.cells[0].x, room.cells[0].y);
        foreach (var cell in room.cells)
        {
            Vector3 p = m.Center(cell.x, cell.y);
            if (Flat(p - c) < Flat(best - c)) best = p;
        }
        best.y = y;
        return best;
    }

    static bool Corridor(Maze m, int i, int j) => m.InGrid(i, j) && !m.Void(i, j) && !m.Reserved(i, j) && !m.Fixed(i, j) && m.RoomAt(i, j) == null;

    // Clé d'une arête (i, j, nord ?) depuis la cellule (i, j) et une direction.
    static (int, int, bool) EdgeKey(int i, int j, int d) => d == 0 ? (i, j, false) : d == 1 ? (i - 1, j, false) : d == 2 ? (i, j, true) : (i, j - 1, true);

    // Environ 5 % des appartements (hors Neutral Room, appartements de double hauteur, voisins des cages d'escalier et des ponts)
    // n'ont plus qu'une porte, sous cadenas : toutes leurs autres ouvertures (portes, passages vers les appartements voisins, fenêtres)
    // sont murées. Un appartement par lequel il faut passer pour atteindre d'autres pièces n'est jamais fermé.
    static void LockRooms(Maze m, int storey, HashSet<Vector2Int> wellCells, List<LockedRoom> locked, System.Random rnd, HashSet<Vector2Int> building, HashSet<Vector2Int> keep)
    {
        var rooms = m.rooms.FindAll(r => r.cells.Count > 0 && building.Contains(r.cells[0]));
        Shuffle(rooms, rnd);
        foreach (Room room in rooms)
        {
            if (rnd.NextDouble() >= LockedRoomChance || room.ceil > StoreyHeight + 0.01f) continue;
            if (room.cells.Exists(c => m.Fixed(c.x, c.y) || m.Reserved(c.x, c.y) || m.keepClear.Contains(c) || keep != null && keep.Contains(c))) continue;
            var cellSet = new HashSet<Vector2Int>(room.cells);
            bool nearWell = false;
            foreach (var c in room.cells)
                for (int d = 0; d < 4; d++) if (wellCells.Contains(c + new Vector2Int(DI[d], DJ[d]))) nearWell = true;
            if (nearWell) continue;
            var doors = new List<(int, int, bool)>();
            foreach (var e in m.doorEdges)
            {
                var (i, j, north) = e;
                var a = new Vector2Int(i, j);
                var b = north ? new Vector2Int(i, j + 1) : new Vector2Int(i + 1, j);
                if (cellSet.Contains(a) != cellSet.Contains(b)) doors.Add(e);
            }
            if (doors.Count == 0) continue;
            if (Components(m, cellSet, building) != Components(m, null, building)) continue;
            var door = doors[rnd.Next(doors.Count)];
            foreach (var c in room.cells)
                for (int d = 0; d < 4; d++)
                {
                    var nb = c + new Vector2Int(DI[d], DJ[d]);
                    if (cellSet.Contains(nb) || !m.InGrid(nb.x, nb.y)) continue;
                    var key = EdgeKey(c.x, c.y, d);
                    if (key.Equals(door)) continue;
                    m.wallHoles.Remove(key);
                    m.doorEdges.Remove(key);
                    SetWall(m, c.x, c.y, d, true);
                }
            locked.Add(new LockedRoom { storey = storey, room = room, edge = door });
        }
    }

    // Nombre d'ensembles de cellules du bâtiment reliées (sans mur entre elles) à cet étage, sans compter 'without'.
    static int Components(Maze m, HashSet<Vector2Int> without, HashSet<Vector2Int> building)
    {
        var seen = new bool[m.N, m.N];
        int count = 0;
        bool Open(int i, int j) => m.InGrid(i, j) && !seen[i, j] && !m.Void(i, j) && building.Contains(new Vector2Int(i, j)) && (without == null || !without.Contains(new Vector2Int(i, j)));
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!Open(i, j)) continue;
                count++;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(i, j));
                seen[i, j] = true;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = q.x + DI[d], nj = q.y + DJ[d];
                        if (!Open(ni, nj) || HasWall(m, q.x, q.y, d)) continue;
                        seen[ni, nj] = true;
                        queue.Enqueue(new Vector2Int(ni, nj));
                    }
                }
            }
        return count;
    }

    // Fond du ravin (-80 m), plafond (+80 m) ; ses parois au-dessus et au-dessous de ce que l'Échiquier et les étages construisent ; la
    // façade visible des étages (béton, V0.7.16) et du mur de l'Échiquier d'en face (damier, sans fenêtre).
    static void BuildRavine(Maze m, BlockhausPlan plan, Kit kit, System.Random rnd, Transform root)
    {
        var cells = plan.ravine;
        MeshBuilder visible = new MeshBuilder(), concrete = new MeshBuilder();
        var fake = new List<(Vector3 wall, Vector3 along, Vector3 normal, float y)>();
        var sorted = cells.OrderBy(c => c.x).ThenBy(c => c.y).ToList();
        foreach (var c in sorted)
        {
            Vector3 center = m.Center(c.x, c.y);
            var rect = Rect.MinMaxRect(center.x - Cell * 0.5f, center.z - Cell * 0.5f, center.x + Cell * 0.5f, center.z + Cell * 0.5f);
            kit.Floor(rect, -RavineReach, 1f);
            kit.walls.WorldQuad(new Vector3(rect.xMin, RavineReach, rect.yMin), new Vector3(rect.xMin, RavineReach, rect.yMax), new Vector3(rect.xMax, RavineReach, rect.yMax), new Vector3(rect.xMax, RavineReach, rect.yMin), Vector3.down, 0.5f);
            kit.Collider(new Vector3(center.x, RavineReach + 0.5f, center.z), new Vector3(Cell, 1f, Cell));
            for (int d = 0; d < 4; d++)
            {
                var nb = new Vector2Int(c.x + DI[d], c.y + DJ[d]);
                if (cells.Contains(nb)) continue;
                bool boundary = !m.InGrid(nb.x, nb.y);
                bool echiquier = !boundary && !m.InStructure(nb.x, nb.y);
                Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                Vector3 edge = center + dir * (Cell * 0.5f);
                // Ce que l'Échiquier (son mur, de 0 à son plafond) ou les étages (de -10 à +20 m) construisent déjà.
                float low = echiquier ? 0f : -BlockhausDepth, high = echiquier ? m.Ceil(nb.x, nb.y) : BlockhausTop;
                if (boundary && d == 3 && m.southHeights != null) high = Mathf.Max(high, m.southHeights[c.x]);
                // V0.7.16 (Note 04) : la paroi du dessous dépasse de 1 cm le sol qu'elle borde (étage du Blockhaus, Échiquier) : leurs
                // faces ne sont plus dans le même plan (scintillement au seuil des ouvertures et des ponts).
                foreach (var (y0, y1) in new[] { (-RavineReach, low + SeamLift), (high, RavineReach) })
                {
                    if (y1 - y0 < 0.05f) continue;
                    Vector3 wc = edge + Vector3.up * ((y0 + y1) * 0.5f);
                    // V0.7.16 (Note 01) : façade du Blockhaus en béton gris ; côté Échiquier, le damier, sans fenêtre factice.
                    kit.Box(wc, d < 2 ? new Vector3(Wall, y1 - y0, Cell + Wall) : new Vector3(Cell + Wall, y1 - y0, Wall), true, boundary || echiquier ? null : kit.grey);
                }
                if (boundary) continue;
                // V0.7.15 (Note 02) : façade du Blockhaus en grille régulière : à chaque étage une ouverture (où le joueur passe), et
                // au-dessus comme au-dessous, jusqu'en haut de la façade et jusqu'au fond du ravin, une fenêtre factice de la même taille,
                // au même pas (une par cellule, une tous les 10 m). Là où un étage n'a pas d'ouverture (appartement fermé, double
                // hauteur), une fenêtre factice la remplace.
                if (!echiquier)
                {
                    Vector3 wall = center + dir * (Cell * 0.5f - Wall * 0.5f);
                    for (float level = -RavineReach; level + WindowHigh <= RavineReach + 0.01f; level += StoreyHeight)
                    {
                        int s = System.Array.FindIndex(StoreyLevels, v => Mathf.Abs(v - level) < 0.01f);
                        if (s >= 0 && plan.storeys[s].wallHoles.ContainsKey(EdgeKey(c.x, c.y, d))) continue;
                        fake.Add((wall, side, -dir, level + WindowLow));
                    }
                }
                // Façade praticable, visible de loin et sans éclairage (damier lumineux côté Échiquier, béton lumineux côté Blockhaus),
                // percée de ses fenêtres (côté Échiquier : les départs des ponts seulement).
                var holes = new List<Rect>();
                if (echiquier)
                {
                    if (m.wallHoles.TryGetValue(EdgeKey(c.x, c.y, d), out var hole)) holes.Add(Rect.MinMaxRect(-AerialHoleWidth * 0.5f, hole.low, AerialHoleWidth * 0.5f, hole.high));
                }
                else
                    for (int s = 0; s < plan.storeys.Length; s++)
                    {
                        Maze sm = plan.storeys[s];
                        float y = StoreyLevels[s];
                        if (sm.wallHoles.TryGetValue(EdgeKey(c.x, c.y, d), out var hole)) holes.Add(Rect.MinMaxRect(-AerialHoleWidth * 0.5f, y + hole.low, AerialHoleWidth * 0.5f, y + hole.high));
                    }
                // V0.7.16 : sur toute la hauteur du ravin (du fond à son plafond), pas seulement là où sont les étages ou l'Échiquier :
                // toute la façade du Blockhaus est grise, et le mur de l'Échiquier est éclairé de manière uniforme.
                Vector3 face = edge - dir * (Wall * 0.5f + 0.015f);
                foreach (Rect p in Subtract(new List<Rect> { Rect.MinMaxRect(-Cell * 0.5f - Wall * 0.5f, -RavineReach, Cell * 0.5f + Wall * 0.5f, RavineReach) }, holes))
                    (echiquier ? visible : concrete).WorldQuad(face + side * p.xMin + Vector3.up * p.yMin, face + side * p.xMax + Vector3.up * p.yMin,
                        face + side * p.xMax + Vector3.up * p.yMax, face + side * p.xMin + Vector3.up * p.yMax, -dir, 0.5f);
            }
        }
        if (!visible.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_FacadesRavin", root, visible.Build("FacadesRavin"), VisibleChecker);
        if (!concrete.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_FacadesBlockhaus", root, concrete.Build("FacadesBlockhaus"), VisibleConcrete);
        // Fenêtres factices, en plusieurs maillages (pierre, vitres noires, vitres allumées).
        const int perMesh = 300;
        for (int k0 = 0; k0 < fake.Count; k0 += perMesh)
        {
            MeshBuilder stone = new MeshBuilder(), dark = new MeshBuilder(), lit = new MeshBuilder();
            for (int k = k0; k < Mathf.Min(fake.Count, k0 + perMesh); k++)
            {
                var w = fake[k];
                MeshBuilder glass = rnd.NextDouble() < GameRules.LitWindowChance ? lit : dark;
                AddWindow(stone, glass, w.wall, w.along, w.normal, w.y, AerialHoleWidth, WindowHigh - WindowLow);
            }
            string name = "Z" + m.zone + "_FenetresRavin" + k0 / perMesh;
            GameArt.MeshObject(name + "_Pierre", root, stone.Build(name), GameArt.WindowStone);
            if (!dark.IsEmpty) GameArt.MeshObject(name + "_Vitres", root, dark.Build(name), GameArt.WindowDark);
            if (!lit.IsEmpty) GameArt.MeshObject(name + "_Allumees", root, lit.Build(name), GameArt.WindowLit);
        }
    }

    // Damier lumineux des façades du ravin (on les voit de loin, même sans éclairage).
    static Material visibleChecker;
    static Material VisibleChecker
    {
        get
        {
            if (visibleChecker != null) return visibleChecker;
            Material checker = GameArt.CheckerWall;
            var mat = new Material(GameArt.Emissive("DamierLumineux", Color.white, Color.white)) { name = "DamierLumineux" };
            mat.mainTexture = checker.mainTexture;
            mat.color = checker.color;
            mat.SetTexture("_EmissionMap", checker.mainTexture);
            mat.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return visibleChecker = mat;
        }
    }

    // V0.7.16 (Note 01) : béton lumineux de la façade du Blockhaus sur le ravin (gris comme ses plafonds, visible de loin, même sans
    // éclairage).
    static Material visibleConcrete;
    static Material VisibleConcrete
    {
        get
        {
            if (visibleConcrete != null) return visibleConcrete;
            Material grey = GreyCeiling;
            var mat = new Material(GameArt.Emissive("BetonLumineux", Color.white, Color.white)) { name = "BetonLumineux" };
            mat.mainTexture = grey.mainTexture;
            mat.color = grey.color;
            mat.SetTexture("_EmissionMap", grey.mainTexture);
            mat.SetColor("_EmissionColor", new Color(0.3f, 0.3f, 0.3f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return visibleConcrete = mat;
        }
    }

    // Cage d'escalier (2 x 2 cellules) : un colimaçon de 3 m de large (V0.7.14) de -10 m à +20 m autour d'un pilier, des paliers vers
    // les portes de chaque étage. V0.7.13 : pas de rambarde ; des piliers comblent les coins de la cage.
    static void BuildStairwell(Maze main, Maze[] storeys, Vector2Int w, Kit kit, System.Random rnd)
    {
        Vector3 center = (main.Center(w.x, w.y) + main.Center(w.x + 1, w.y + 1)) * 0.5f;
        float half = Cell, bottom = StoreyLevels[0], height = BlockhausTop - bottom;
        kit.Floor(Rect.MinMaxRect(center.x - half, center.z - half, center.x + half, center.z + half), bottom);
        float rOut = half - Wall * 0.5f - 0.05f, rIn = rOut - TowerStairWidth;
        kit.Box(center + Vector3.up * (bottom + height * 0.5f), new Vector3(rIn * 1.4f, height, rIn * 1.4f));
        float start = Rand(rnd, 0f, 360f);
        KitSpiral(kit, center, rIn, rOut, bottom, BlockhausTop, start);
        foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
            kit.Box(center + corner * (half - StairwellPillar * 0.5f) + Vector3.up * (bottom + height * 0.5f), new Vector3(StairwellPillar, height, StairwellPillar));
        // Paliers : à chaque étage, une dalle entre l'escalier et le mur de la cage (le joueur passe de l'un à l'autre).
        for (int s = 0; s < storeys.Length; s++)
        {
            float y = StoreyLevels[s];
            foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
            {
                Vector3 c = center + corner * (half - 0.9f);
                kit.Box(new Vector3(c.x, y - 0.15f, c.z), new Vector3(1.8f, 0.3f, 1.8f));
            }
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                Vector3 c = center + new Vector3(dx, 0f, dz) * (half - 0.4f);
                kit.Box(new Vector3(c.x, y - 0.15f, c.z), dx != 0 ? new Vector3(0.8f, 0.3f, half * 2f) : new Vector3(half * 2f, 0.3f, 0.8f));
            }
            AddLamp(kit.root, new Vector3(center.x, y + StoreyHeight - 0.1f, center.z), 9f, 1.2f, rnd);
        }
        kit.grey.WorldQuad(new Vector3(center.x - half, BlockhausTop, center.z - half), new Vector3(center.x - half, BlockhausTop, center.z + half),
            new Vector3(center.x + half, BlockhausTop, center.z + half), new Vector3(center.x + half, BlockhausTop, center.z - half), Vector3.down, 0.5f);
        kit.Collider(new Vector3(center.x, BlockhausTop + 0.5f, center.z), new Vector3(half * 2f, 1f, half * 2f));
    }

    const float StairwellPillar = 1.2f;

    static readonly string[] BlockhausFurniture = { "Lavabo", "Douche", "Armoire", "Urinoir", "Baignoire" };

    // V0.7.13 (Note 23) : baignoires 50 % plus grandes (on peut y entrer) ; V0.7.14 (Note 06) : encore 30 % plus grandes.
    static float BlockhausModelHeight(string model) =>
        model == "Armoire" ? 2f : model == "Baignoire" ? 1.7f * 1.5f * 1.3f : model == "Douche" ? 1f : model == "Urinoir" ? 0.8f : model == "BoiteLettres" ? 0.55f : 0.7f;

    // Hauteur du milieu de l'objet au-dessus du sol (contre le mur) ; 0 : posé au sol.
    static float BlockhausMountHeight(string model)
    {
        switch (model)
        {
            case "Lavabo": return 0.8f;                 // à hauteur des mains
            case "Douche": return 2.15f;                // au-dessus de la tête
            case "Urinoir": return 0.75f;               // à hauteur du bassin
            default: return 0f;
        }
    }

    // V0.7.13 : urinoirs côte à côte (2 : 20 %, 3 : 10 %, 4 : 5 %).
    static int UrinalRow(System.Random rnd)
    {
        double roll = rnd.NextDouble();
        return roll < 0.05 ? 4 : roll < 0.15 ? 3 : roll < 0.35 ? 2 : 1;
    }

    const float PipeChance = 0.2f, MailboxChance = 0.1f, PanelChance = 0.035f;

    // Étage d'un Blockhaus : néons des couloirs, appartements éclairés trois fois sur quatre, plinthes et encadrements gris, néons
    // au-dessus des fenêtres du ravin, mobilier soviétique contre les murs (modèles Meshy) ; dans les couloirs, boîtes aux lettres,
    // tuyaux et tableaux électriques (ceux de la Control Room) ; quelques yeux, emplacements des monticules.
    static void DecorateStorey(Maze m, BlockhausPlan plan, float y, Transform props, Kit finish, ZoneRefs refs, System.Random rnd, bool hubStorey, List<LockedRoom> lockedHere)
    {
        var placed = new List<Vector3>();
        // Faces de mur déjà garnies (boîte aux lettres, tuyau, tableau) : une seule chose par face.
        var usedFaces = new HashSet<(int, int, int)>();
        bool FreeFace(int i, int j, int d) => HasWall(m, i, j, d) && !m.HasWallHole(i, j, d) && !usedFaces.Contains((i, j, d));
        ControlDecor.BeginPipes(props);
        foreach (var cell in plan.building.OrderBy(c => c.x).ThenBy(c => c.y))
        {
            int i = cell.x, j = cell.y;
            if (m.Void(i, j) || m.Fixed(i, j)) continue;
            Vector3 c = m.Center(i, j) + Vector3.up * y;
            Room room = m.RoomAt(i, j);
            bool clear = hubStorey && m.keepClear.Contains(cell);
            if (room == null && (i + j) % 2 == 0) AddLamp(props, c + Vector3.up * m.Ceil(i, j), 7f, 1.1f, rnd);
            if (!clear && rnd.NextDouble() < 0.55)
            {
                Vector3 p = c + new Vector3(Rand(rnd, -1.6f, 1.6f), 0f, Rand(rnd, -1.6f, 1.6f));
                if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > 0.95f && Mathf.Abs(hit.point.y - y) < 0.3f) refs.itemSpots.Add(hit.point);
            }
            if (rnd.NextDouble() < 0.015)
            {
                int d = rnd.Next(4);
                if (HasWall(m, i, j, d))
                {
                    Vector3 nn = -new Vector3(DI[d], 0f, DJ[d]);
                    AddEye(props, c - nn * (Cell * 0.5f - Wall * 0.5f) + Vector3.up * Rand(rnd, 1.5f, 3f), nn, Rand(rnd, 0.6f, 1f));
                }
            }
            for (int d = 0; d < 4; d++)
            {
                Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                Vector3 face = c + dir * (Cell * 0.5f - Wall * 0.5f);
                int ni = i + DI[d], nj = j + DJ[d];
                var key = EdgeKey(i, j, d);
                bool doorway = !HasWall(m, i, j, d) && (m.doorEdges.Contains(key) || m.Void(ni, nj) && !m.Silent(ni, nj));
                bool hasHole = m.HasWallHole(i, j, d);
                bool fullHole = hasHole && m.wallHoles[key].low < 0.01f;
                // V0.7.14 (Note 02) : plinthes continues au pied des murs (jusque dans les coins et d'une cellule à l'autre), de part
                // et d'autre des portes ; pas sous une fenêtre ouverte jusqu'au sol.
                if (HasWall(m, i, j, d) && !fullHole) Plinth(finish, face, dir, Cell, y);
                else if (doorway || fullHole)
                {
                    float opening = fullHole ? AerialHoleWidth : DoorWidth;
                    float part = (Cell - opening) * 0.5f;
                    foreach (float sgn in new[] { -1f, 1f }) Plinth(finish, face + side * (sgn * (opening * 0.5f + part * 0.5f)), dir, part, y);
                }
                // V0.7.15 (Note 01) : la plinthe fait le tour des tableaux des portes et des ouvertures jusqu'au sol (le pied des
                // arches compris), jusqu'au milieu du mur, sauf sous la porte d'un appartement fermé.
                if ((doorway || fullHole) && !lockedHere.Exists(l => l.edge.Equals(key)))
                {
                    float opening = fullHole ? AerialHoleWidth : DoorWidth, across = Wall * 0.5f + PlinthRelief;
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        Vector3 cap = c + dir * (Cell * 0.5f - across * 0.5f) + side * (sgn * (opening * 0.5f - PlinthRelief * 0.5f)) + Vector3.up * (PlinthHeight * 0.5f);
                        finish.grey.Box(cap, Mathf.Abs(dir.x) > 0.5f ? new Vector3(across, PlinthHeight, PlinthRelief) : new Vector3(PlinthRelief, PlinthHeight, across), 1f);
                    }
                }
                // V0.7.14 : encadrements gris des portes et des fenêtres (des deux côtés du mur s'il donne sur le ravin).
                if (doorway && !m.archEdges.Contains(key)) Frame(finish, face, dir, side, DoorWidth, 0f, DoorHeight, y);
                if (hasHole)
                {
                    var hole = m.wallHoles[key];
                    Frame(finish, face, dir, side, AerialHoleWidth, hole.low, hole.high, y);
                    if (plan.ravine.Contains(new Vector2Int(ni, nj))) Frame(finish, face + dir * (Wall + 0.001f), -dir, side, AerialHoleWidth, hole.low, hole.high, y);
                }
                // Néon au-dessus des vraies fenêtres, côté ravin.
                if (hasHole && plan.ravine.Contains(new Vector2Int(ni, nj)))
                    AddWallNeon(props, c + dir * (Cell * 0.5f + Wall * 0.5f + 0.06f) + Vector3.up * (WindowHigh + 0.45f), dir, 8f, 1f, rnd, RavineNeonScale);
            }
            if (room != null || clear) continue;
            // Boîtes aux lettres (couloirs uniquement), en colonnes contre un mur plein (à toutes les hauteurs).
            if (rnd.NextDouble() < MailboxChance)
            {
                int d = rnd.Next(4);
                var box = FreeFace(i, j, d) ? GameArt.SpawnModelFloor("BoiteLettres", props, BlockhausModelHeight("BoiteLettres")) : null;
                if (box != null)
                {
                    float h = GameArt.LocalBounds(box.transform).size.y;
                    float bottom = Rand(rnd, PlinthHeight + 0.05f, 1f);
                    if (!PlaceAgainstWall(m, box, cell, d, bottom, 0.02f, placed, rnd, y)) Object.Destroy(box);
                    else
                    {
                        usedFaces.Add((i, j, d));
                        for (float above = bottom + h + 0.08f; above + h < 2.9f && rnd.NextDouble() < 0.7; above += h + 0.08f)
                        {
                            var more = Object.Instantiate(box, props);
                            more.transform.SetPositionAndRotation(box.transform.position + Vector3.up * (above - bottom), box.transform.rotation);
                        }
                    }
                }
            }
            // V0.7.13 : tuyaux de tailles différentes le long des murs (à la verticale, ou à l'horizontale au-dessus des portes).
            if (rnd.NextDouble() < PipeChance)
            {
                int d = rnd.Next(4);
                if (FreeFace(i, j, d))
                {
                    usedFaces.Add((i, j, d));
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                    Vector3 face = c + dir * (Cell * 0.5f - Wall * 0.5f);
                    int count = 1 + rnd.Next(3);
                    bool vertical = rnd.NextDouble() < 0.5;
                    float ceil = m.Ceil(i, j), u = Rand(rnd, -1.6f, 1.2f), level = Rand(rnd, DoorHeight + 0.5f, Mathf.Min(ceil - 0.6f, 6f));
                    for (int k = 0; k < count; k++)
                    {
                        float r = Mathf.Min(ControlDecor.RandomPipeRadius(rnd), 0.22f);
                        bool dark = rnd.NextDouble() < 0.5;
                        if (vertical)
                        {
                            // Du sol au plafond, au-delà du relief des plinthes.
                            Vector3 at = face - dir * (PlinthRelief + r + 0.03f) + side * u;
                            ControlDecor.Pipe(r, dark, new Vector3(at.x, y, at.z), new Vector3(at.x, y + ceil, at.z));
                            u += r * 2f + 0.12f + Rand(rnd, 0f, 0.2f);
                        }
                        else
                        {
                            // D'un bout à l'autre du mur, sortant du mur et y rentrant par deux coudes.
                            float half = Cell * 0.5f - Wall * 0.5f - 0.3f, off = r + 0.03f;
                            Vector3 a = face + side * -half + Vector3.up * level, b = face + side * half + Vector3.up * level;
                            ControlDecor.Pipe(r, dark, a + dir * 0.1f, a - dir * off, b - dir * off, b + dir * 0.1f);
                            level += r * 2f + 0.1f + Rand(rnd, 0f, 0.25f);
                            if (level > ceil - 0.4f) break;
                        }
                    }
                }
            }
            // V0.7.13 : tableaux électriques (le bloc mural de la Control Room), plus rares que les boîtes aux lettres.
            if (rnd.NextDouble() < PanelChance)
            {
                int d = rnd.Next(4);
                if (FreeFace(i, j, d))
                {
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                    Vector3 at = c + dir * (Cell * 0.5f - Wall * 0.5f) + side * Rand(rnd, -1.4f, 1.4f) + Vector3.up * Rand(rnd, 1.4f, 1.9f);
                    if (WallBox(props, "Decor/Ventilation", at, -dir, 0.9f)) usedFaces.Add((i, j, d));
                }
            }
        }
        ControlDecor.EndPipes();
        PlinthCorners(m, plan, y, finish);
        foreach (Room room in m.rooms)
        {
            if (room.cells.Count == 0 || !plan.building.Contains(room.cells[0]) || m.Fixed(room.cells[0].x, room.cells[0].y)) continue;
            // 25 % des appartements ne sont pas éclairés (V0.7.14 : tous les plafonds sont gris, comme le reste de la structure).
            if (rnd.NextDouble() >= 0.25) AddLamp(props, RoomFloorCenter(m, room, y) + Vector3.up * room.ceil, 10f, 1.3f, rnd);
            // L'appartement fermé garde le sol libre pour son contenu.
            if (lockedHere.Exists(l => l.room == room)) continue;
            int count = 1 + rnd.Next(room.cells.Count + 1);
            for (int k = 0; k < count; k++)
            {
                string model = BlockhausFurniture[rnd.Next(BlockhausFurniture.Length)];
                var cell = room.cells[rnd.Next(room.cells.Count)];
                int d = rnd.Next(4);
                var go = GameArt.SpawnModelFloor(model, props, BlockhausModelHeight(model));
                if (go == null) continue;
                float mount = BlockhausMountHeight(model);
                float bottom = mount > 0f ? mount - GameArt.LocalBounds(go.transform).size.y * 0.5f : 0f;
                // Au-dessus des plinthes, ou devant elles (rien n'entre dans le mur).
                float gap = bottom < PlinthHeight + 0.02f ? PlinthRelief + 0.04f : 0.02f;
                if (model == "Baignoire") gap = Mathf.Max(gap, 0.25f);
                var row = PlaceRowAgainstWall(m, go, cell, d, bottom, gap, placed, rnd, y, model == "Urinoir" ? UrinalRow(rnd) : 1, null);
                // V0.7.14 (Note 06) : une baignoire ne rentre dans aucun mur (ni dans les cellules voisines hors de la pièce).
                if (row != null && model == "Baignoire" && !FitInRoom(m, room, go))
                {
                    placed.RemoveAt(placed.Count - 1);
                    row = null;
                }
                if (row == null)
                {
                    Object.Destroy(go);
                    continue;
                }
                foreach (var obj in row)
                {
                    if (model == "Baignoire") BathCollider(obj);
                    else
                    {
                        Bounds b = GameArt.LocalBounds(obj.transform);
                        var col = obj.AddComponent<BoxCollider>();
                        col.center = b.center;
                        col.size = b.size;
                    }
                }
                // V0.7.13 : une baignoire contre un mur a son pommeau de douche sur ce mur, au-dessus d'un de ses bouts.
                if (model == "Baignoire")
                {
                    var shower = GameArt.SpawnModelFloor("Douche", props, BlockhausModelHeight("Douche"));
                    if (shower == null) continue;
                    Vector3 c = m.Center(cell.x, cell.y), side = new Vector3(DJ[d], 0f, DI[d]);
                    Bounds wb = WorldBounds(go.transform);
                    float end = Vector3.Dot(wb.center - c, side) + (rnd.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Abs(Vector3.Dot(wb.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z)))) * 0.6f;
                    float showerBottom = BlockhausMountHeight("Douche") + 0.3f - GameArt.LocalBounds(shower.transform).size.y * 0.5f;
                    var put = PlaceRowAgainstWall(m, shower, cell, d, showerBottom, 0.02f, placed, rnd, y, 1, end);
                    if (put == null) Object.Destroy(shower);
                }
            }
        }
    }

    // Tout l'objet tient dans la pièce : chaque coin de sa boîte englobante est dans une cellule de la pièce, à l'écart des murs.
    static bool FitInRoom(Maze m, Room room, GameObject go)
    {
        Bounds b = WorldBounds(go.transform);
        foreach (float x in new[] { b.min.x, b.max.x })
            foreach (float z in new[] { b.min.z, b.max.z })
            {
                var p = new Vector3(x, 0f, z);
                if (!m.CellAt(p, out int i, out int j) || m.RoomAt(i, j) != room) return false;
                Vector3 c = m.Center(i, j);
                float lim = Cell * 0.5f - Wall * 0.5f - 0.03f;
                for (int d = 0; d < 4; d++)
                {
                    if (!HasWall(m, i, j, d)) continue;
                    float off = d < 2 ? (p.x - c.x) * DI[d] : (p.z - c.z) * DJ[d];
                    if (off > lim) return false;
                }
            }
        return true;
    }

    // Objet (et ses copies, côte à côte) contre le mur 'd' de la cellule : son dos à 'gap' de la face du mur, son bas à 'y' au-dessus du
    // sol de l'étage ('floorY'), tourné vers la pièce ; toute la rangée tient dans la cellule. 'along' : position imposée le long du mur
    // (sinon tirée au hasard, loin des objets déjà posés). Retourne les objets posés, ou null.
    static List<GameObject> PlaceRowAgainstWall(Maze m, GameObject go, Vector2Int cell, int d, float y, float gap, List<Vector3> placed, System.Random rnd, float floorY, int copies, float? along)
    {
        if (!HasWall(m, cell.x, cell.y, d) || m.HasWallHole(cell.x, cell.y, d)) return null;
        Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
        Quaternion rot = Quaternion.Euler(0f, d == 0 ? -90f : d == 1 ? 90f : d == 2 ? 180f : 0f, 0f);
        go.transform.rotation = rot;
        Bounds b = GameArt.LocalBounds(go.transform);
        Vector3 ln = Quaternion.Inverse(rot) * n, ls = Quaternion.Inverse(rot) * side;
        float depth = Mathf.Abs(ln.x) * b.extents.x + Mathf.Abs(ln.z) * b.extents.z, width = Mathf.Abs(ls.x) * b.extents.x + Mathf.Abs(ls.z) * b.extents.z;
        float centerN = Vector3.Dot(rot * b.center, n), centerS = Vector3.Dot(rot * b.center, side);
        float spacing = width * 2f + 0.08f, rowHalf = spacing * (copies - 1) * 0.5f;
        float reach = Cell * 0.5f - Wall * 0.5f - 0.15f - width - rowHalf;
        if (reach < 0f) return null;
        float at = along.HasValue ? Mathf.Clamp(along.Value, -reach, reach) : Rand(rnd, -reach, reach);
        Vector3 c = m.Center(cell.x, cell.y);
        float wallPlane = Cell * 0.5f - Wall * 0.5f;
        var points = new List<Vector3>();
        for (int k = 0; k < copies; k++)
        {
            Vector3 p = c + n * (wallPlane - gap - depth - centerN) + side * (at - rowHalf + k * spacing - centerS) + Vector3.up * (floorY + y);
            if (!along.HasValue && placed.Exists(q => Flat(q - p) < Mathf.Max(1.2f, width * 2f)))
            {
                if (k == 0) return null;
                break;
            }
            points.Add(p);
        }
        var result = new List<GameObject>();
        for (int k = 0; k < points.Count; k++)
        {
            var obj = k == 0 ? go : Object.Instantiate(go, go.transform.parent);
            obj.transform.SetPositionAndRotation(points[k], rot);
            result.Add(obj);
            placed.Add(points[k]);
        }
        return result;
    }

    // Collision d'une baignoire. V0.7.14 : la forme exacte du modèle (collision de maillage, non convexe) : on ne traverse plus ses
    // parois ni son rebord, mais on peut entrer dedans. Sans maillage lisible : un fond et quatre bords.
    static void BathCollider(GameObject bath)
    {
        bool meshed = false;
        foreach (var mf in bath.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            meshed = true;
        }
        if (meshed) return;
        Bounds b = GameArt.LocalBounds(bath.transform);
        const float t = 0.12f;
        void Part(Vector3 center, Vector3 size)
        {
            var col = bath.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;
        }
        Part(new Vector3(b.center.x, b.min.y + t * 0.5f, b.center.z), new Vector3(b.size.x, t, b.size.z));
        float wy = b.center.y, h = b.size.y;
        Part(new Vector3(b.min.x + t * 0.5f, wy, b.center.z), new Vector3(t, h, b.size.z));
        Part(new Vector3(b.max.x - t * 0.5f, wy, b.center.z), new Vector3(t, h, b.size.z));
        Part(new Vector3(b.center.x, wy, b.min.z + t * 0.5f), new Vector3(b.size.x, h, t));
        Part(new Vector3(b.center.x, wy, b.max.z - t * 0.5f), new Vector3(b.size.x, h, t));
    }

    // Bloc mural (modèle de la Control Room) plaqué contre un mur, centré sur 'wallPoint', tourné vers 'inward', avec sa collision.
    static bool WallBox(Transform parent, string model, Vector3 wallPoint, Vector3 inward, float height)
    {
        var pivot = new GameObject(model.Substring(model.LastIndexOf('/') + 1)).transform;
        pivot.SetParent(parent, false);
        pivot.SetPositionAndRotation(wallPoint, Quaternion.LookRotation(inward));
        var go = GameArt.SpawnModel(model, pivot, height);
        if (go == null) { Object.Destroy(pivot.gameObject); return false; }
        Bounds b = GameArt.LocalBounds(go.transform);
        go.transform.localPosition = new Vector3(0f, -height * 0.5f, -b.min.z + 0.01f);
        Bounds wb = GameArt.LocalBounds(pivot);
        var col = pivot.gameObject.AddComponent<BoxCollider>();
        col.center = wb.center;
        col.size = wb.size;
        return true;
    }

    // V0.7.15 (Note 01) : aux coins des murs (bouts de mur, angles saillants, jonctions), la plinthe fait le tour du poteau, du côté
    // de chaque cellule praticable qui le touche (pas côté ravin ni cage d'escalier). Un peu en retrait des plinthes des murs, pour ne
    // pas scintiller avec elles là où elles se recouvrent.
    static void PlinthCorners(Maze m, BlockhausPlan plan, float y, Kit finish)
    {
        bool Walkable(int i, int j) => m.InGrid(i, j) && plan.building.Contains(new Vector2Int(i, j)) && !m.Void(i, j) && !m.Fixed(i, j);
        // Arête (de la cellule (i, j) vers +X si 'east', sinon vers +Z) avec un mur au niveau du sol (plein, ou de part et d'autre
        // d'une porte).
        bool FloorWall(int i, int j, bool east)
        {
            int ni = east ? i + 1 : i, nj = east ? j : j + 1;
            bool aIn = m.InGrid(i, j), bIn = m.InGrid(ni, nj);
            if (!aIn && !bIn) return false;
            if (!aIn || !bIn) return !m.Silent(aIn ? i : ni, aIn ? j : nj);
            bool av = m.Void(i, j), bv = m.Void(ni, nj);
            if (av && bv || m.Silent(i, j) || m.Silent(ni, nj)) return false;
            return (east ? m.eastWall[i, j] : m.northWall[i, j]) || m.doorEdges.Contains((i, j, !east)) || av != bv;
        }
        float e = Wall * 0.5f + PlinthRelief - 0.004f;
        for (int a = 0; a <= m.N; a++)
            for (int b = 0; b <= m.N; b++)
            {
                if (!FloorWall(a - 1, b, true) && !FloorWall(a - 1, b - 1, true) && !FloorWall(a, b - 1, false) && !FloorWall(a - 1, b - 1, false)) continue;
                Vector3 corner = m.origin + new Vector3(a * Cell, y + PlinthHeight * 0.5f, b * Cell);
                for (int sx = 0; sx < 2; sx++)
                    for (int sz = 0; sz < 2; sz++)
                        if (Walkable(a - 1 + sx, b - 1 + sz))
                            finish.grey.Box(corner + new Vector3((sx - 0.5f) * e, 0f, (sz - 0.5f) * e), new Vector3(e, PlinthHeight, e), 1f);
            }
    }

    // Plinthe grise (béton) de 0,3 m, en relief de 0,12 m, au pied d'un mur ('face' : milieu de la face du mur, 'dir' : vers le mur),
    // sur 'length'.
    static void Plinth(Kit kit, Vector3 face, Vector3 dir, float length, float floorY)
    {
        if (length < 0.05f) return;
        Vector3 c = new Vector3(face.x, floorY + PlinthHeight * 0.5f, face.z) - dir * (PlinthRelief * 0.5f - 0.01f);
        Vector3 size = Mathf.Abs(dir.x) > 0.5f ? new Vector3(PlinthRelief, PlinthHeight, length) : new Vector3(length, PlinthHeight, PlinthRelief);
        kit.grey.Box(c, size, 1f);
    }

    // Encadrement gris (béton) d'une ouverture de 'width', de 'low' à 'high' au-dessus du sol de l'étage, sur la face 'face' d'un mur
    // ('dir' : vers le mur) : deux montants, un linteau, et un appui sous une fenêtre.
    static void Frame(Kit kit, Vector3 face, Vector3 dir, Vector3 side, float width, float low, float high, float floorY)
    {
        Vector3 Size(float along, float h) => Mathf.Abs(side.x) > 0.5f ? new Vector3(along, h, FrameRelief) : new Vector3(FrameRelief, h, along);
        Vector3 at = face - dir * (FrameRelief * 0.5f - 0.005f) + Vector3.up * floorY;
        float bottom = low < 0.01f ? 0f : low - FrameWidth;
        foreach (float s in new[] { -1f, 1f })
            kit.grey.Box(at + side * (s * (width * 0.5f + FrameWidth * 0.5f)) + Vector3.up * ((bottom + high + FrameWidth) * 0.5f), Size(FrameWidth, high + FrameWidth - bottom), 1f);
        kit.grey.Box(at + Vector3.up * (high + FrameWidth * 0.5f), Size(width, FrameWidth), 1f);
        if (low > 0.01f) kit.grey.Box(at + Vector3.up * (low - FrameWidth * 0.5f), Size(width, FrameWidth), 1f);
    }
}
