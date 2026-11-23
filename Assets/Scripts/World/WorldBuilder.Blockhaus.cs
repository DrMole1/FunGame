using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (chapitre 11.4) : environnement Blockhaus. Interconnexion d'appartements et de couloirs sur 4 étages de 10 m (de -20 m à
// +20 m : 20 m de hauteur et 20 m de profondeur), damier noir et blanc. 20 % de couloirs pour 80 % de pièces d'appartements.
// - Entre un couloir et un appartement : une ouverture, garnie d'une arche 40 % du temps (V0.7.12 : plus de portes poussées).
// - V0.7.12 : environ 5 % des appartements sont fermés par une porte sous cadenas, leur seule entrée : la Clé sélectionnée rend la
//   porte interactive, l'ouvrir enlève le cadenas. Ils contiennent un Super Monticule, une Cloche Décorative ou deux monticules
//   simples ; en Zone 3, l'un d'eux abrite le cadavre de Tête-de-Cloche.
// - Des cages d'escalier (escaliers en colimaçon, 70 % avec une rambarde) relient les étages ; joueurs et entités les empruntent.
//   V0.7.12 : plus de cages d'escalier, et plus de bifurcations dans les couloirs.
// - 20 % des appartements font 10 m de plus en hauteur (ils rognent l'étage supérieur). Plus de plateformes chaotiques (V0.7.12).
// - Couloirs éclairés par des néons ; 25 % des appartements ne sont pas éclairés, 40 % ont un plafond gris. Plinthes noires de 0,8 m
//   au pied de tous les murs. 30 % des couloirs ont un renfoncement en biseau.
// - Mobilier soviétique en marbre blanc (modèles Meshy) contre les murs : lavabos à hauteur des mains, pommeaux de douche au-dessus
//   de la tête, armoires, urinoirs à hauteur du bassin, baignoires au sol ; boîtes aux lettres à toutes les hauteurs. Rien ne flotte.
// - Un ravin de 20 m de large (V0.7.12 ; 12 gabarits) coupe la zone en deux, de -80 m à +80 m ; des fenêtres s'ouvrent sur lui à
//   chaque étage, un néon au-dessus de chacune (on le franchit en Canon, en visant une fenêtre d'en face) ; plus haut et plus bas, des
//   fenêtres factices. Y tomber tue (GameSession).
public static partial class WorldBuilder
{
    // V0.7.12 : ravin de 20 m de large (avant : 40 m).
    public const float BlockhausDepth = 20f;
    const float StoreyHeight = 10f, RavineHalfWidth = 10f, RavineReach = 80f;
    static readonly float[] StoreyLevels = { -20f, -10f, 0f, 10f };
    const int HubStorey = 2;
    const float BlockArchChance = 0.4f, LockedRoomChance = 0.05f, ExtraCorridorLink = 0.35f, GreyCeilingChance = 0.4f, RailingChance = 0.7f;
    const float PlinthHeight = 0.8f;

    static readonly Vector2[][] Ravines =
    {
        new[] { new Vector2(0, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 3), new Vector2(1, 3), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 3), new Vector2(4, 3) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 3), new Vector2(2, 3), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 3), new Vector2(4, 3) },
        new[] { new Vector2(0, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(4, 1) },
        new[] { new Vector2(1, 4), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 4) },
        new[] { new Vector2(1, 4), new Vector2(1, 2), new Vector2(3, 2), new Vector2(3, 4) },
    };

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // Appartement fermé sous cadenas : son étage, la pièce, l'arête de sa porte.
    class LockedRoom { public int storey; public Room room; public (int, int, bool) edge; }

    static IEnumerator BuildBlockhausSteps(ZoneBuild build, Transform parent, int seed, int zone, ZoneRefs previous)
    {
        var rnd = new System.Random(seed * 43 + zone * 5381);
        int n = ZoneCells;
        Maze main = CreateMaze(seed, zone);

        // ---------- Plan commun aux 4 étages : ravin, cages d'escalier ----------
        Vector2[] ravine = Ravines[rnd.Next(Ravines.Length)];
        var ravineCells = new HashSet<Vector2Int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                // Le ravin ne mord ni sur le Hub et son entrée, ni sur la Neutral Room.
                if (j <= HubRows + 1 || main.neutralRoom.Contains(new Vector2Int(i, j)) || Mathf.Abs(i - main.doorCol) <= 2 && j >= n - 5) continue;
                Vector3 c = main.Center(i, j);
                var p = new Vector2(c.x, c.z);
                for (int k = 0; k + 1 < ravine.Length; k++)
                    if (DistanceToSegment(p, GridPoint(zone, ravine[k]), GridPoint(zone, ravine[k + 1])) <= RavineHalfWidth) { ravineCells.Add(new Vector2Int(i, j)); break; }
            }
        var nrSide = Flood(main, new Vector2Int(main.doorCol, n - 2), ravineCells);
        // Cages d'escalier (2 x 2 cellules) de part et d'autre du ravin (V0.7.12 : 4 de chaque côté).
        var wells = new List<Vector2Int>();
        foreach (bool northSide in new[] { false, true, false, true, false, true, false, true })
            for (int attempt = 0; attempt < 300; attempt++)
            {
                var c = new Vector2Int(rnd.Next(1, n - 2), rnd.Next(HubRows + 1, n - 4));
                bool ok = true;
                for (int i = c.x - 1; i <= c.x + 2 && ok; i++)
                    for (int j = c.y - 1; j <= c.y + 2 && ok; j++)
                    {
                        var q = new Vector2Int(i, j);
                        if (!main.InGrid(i, j) || ravineCells.Contains(q) || main.Reserved(i, j) || main.Fixed(i, j) || q == main.start
                            || nrSide.Contains(q) != northSide || wells.Exists(w => Mathf.Abs(w.x - i) <= 3 && Mathf.Abs(w.y - j) <= 3)) ok = false;
                    }
                if (!ok) continue;
                wells.Add(c);
                break;
            }
        var wellCells = new HashSet<Vector2Int>();
        foreach (var w in wells) for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) wellCells.Add(new Vector2Int(w.x + a, w.y + b));

        var refs = new ZoneRefs { index = zone, root = new GameObject("Zone " + zone), terrain = TerrainKind.Blockhaus };
        build.Refs = refs;
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;
        refs.sideOf = p => main.CellAt(p, out int ci, out int cj) && nrSide.Contains(new Vector2Int(ci, cj)) ? 1 : 0;
        refs.lowestFloor = -RavineReach;

        // ---------- Les 4 étages ----------
        var storeys = new Maze[StoreyLevels.Length];
        var locked = new List<LockedRoom>();
        bool[,] doubleBelow = new bool[n, n];
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 3, roomCoverage = 0.8f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = StoreyHeight, floors = new[] { 0f }, ceilings = new[] { StoreyHeight },
            chaoticCeilings = new[] { StoreyHeight }, pitCeilings = new[] { StoreyHeight }, fixedRooms = true,
        };
        int doorIds = 0;
        for (int s = 0; s < StoreyLevels.Length; s++)
        {
            float y = StoreyLevels[s];
            Maze m = s == HubStorey ? main : CreateMaze(seed, zone);
            if (s != HubStorey)
            {
                // Hors de l'étage du Hub : le Hub et la Neutral Room sont murés (des blocs pleins), sans porte.
                m = new Maze(zone, n, ZoneOrigin(zone), false, new Vector2Int(HubCol, HubRows));
                var sealedHub = new RectInt(HubMin, 0, HubMax - HubMin + 1, HubRows);
                foreach (RectInt block in new[] { sealedHub, main.neutralRoom })
                {
                    for (int i = block.xMin; i < block.xMax; i++)
                        for (int j = block.yMin; j < block.yMax; j++) m.fixedCell[i, j] = true;
                    AddFixedRoom(m, block, StoreyHeight);
                }
            }
            m.terrain = TerrainKind.Blockhaus;
            m.bevelChance = 0.3f;
            m.voidWallFull = true;
            m.voidCeil = StoreyHeight;
            // Le sol de l'étage du dessus sert de plafond (pas de collision de plafond qui dépasserait chez lui).
            m.noCeilingColliders = s < StoreyLevels.Length - 1;
            m.voidCell = new bool[n, n];
            m.silentVoid = new bool[n, n];
            foreach (var c in ravineCells) m.voidCell[c.x, c.y] = true;
            foreach (var c in wellCells) m.voidCell[c.x, c.y] = true;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (doubleBelow[i, j]) { m.voidCell[i, j] = true; m.silentVoid[i, j] = true; }
            // Mur sud : la zone précédente l'a construit jusqu'à sa hauteur (au-dessus du niveau du Hub seulement).
            if (previous != null && y >= 0f)
            {
                m.southHeights = new float[n];
                for (int i = 0; i < n; i++) m.southHeights[i] = Mathf.Max(0f, previous.maze.northHeights[i] - y);
            }
            Generate(m, rnd, style);
            if (s == HubStorey) StampFixedRooms(m);
            foreach (Room room in m.rooms) room.plain = true;
            // V0.7.12 : plus de bifurcations, les couloirs voisins s'ouvrent souvent les uns sur les autres.
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    foreach (int d in new[] { 0, 2 })
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!Corridor(m, i, j) || !Corridor(m, ni, nj) || !HasWall(m, i, j, d) || rnd.NextDouble() >= ExtraCorridorLink) continue;
                        SetWall(m, i, j, d, false);
                    }
            // Appartements de double hauteur (rognant l'étage du dessus), sauf au dernier étage et sous la Neutral Room / le Hub.
            doubleBelow = new bool[n, n];
            if (s < StoreyLevels.Length - 1)
                foreach (Room room in m.rooms)
                {
                    if (room.cells.Count == 0 || m.Fixed(room.cells[0].x, room.cells[0].y) || rnd.NextDouble() >= 0.2) continue;
                    if (room.cells.Exists(c => main.neutralRoom.Contains(c) || c.x >= HubMin - 1 && c.x <= HubMax + 1 && c.y <= HubRows + 1)) continue;
                    room.ceil = StoreyHeight * 2f;
                    foreach (var c in room.cells) doubleBelow[c.x, c.y] = true;
                }
            // Portes : entre un couloir et un appartement, une par paire voisine ; fenêtres sur le ravin.
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                    foreach (int d in new[] { 0, 2 })
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!m.InGrid(ni, nj) || m.Void(ni, nj) || m.Reserved(ni, nj) || m.Fixed(ni, nj) || HasWall(m, i, j, d)) continue;
                        if ((m.RoomAt(i, j) == null) != (m.RoomAt(ni, nj) == null)) m.doorEdges.Add((i, j, d == 2));
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!ravineCells.Contains(new Vector2Int(ni, nj)) || rnd.NextDouble() > 0.6) continue;
                        m.wallHoles[EdgeKey(i, j, d)] = (1f, 2.9f);
                    }
                }
            // Cages d'escalier : une porte vers un couloir ou un appartement voisin à chaque étage.
            foreach (var w in wells)
            {
                var sides = new List<(Vector2Int cell, int dir)>();
                foreach (var c in new[] { w, w + Vector2Int.right, w + Vector2Int.up, w + Vector2Int.one })
                    for (int d = 0; d < 4; d++)
                    {
                        var nb = c + new Vector2Int(DI[d], DJ[d]);
                        if (wellCells.Contains(nb) || !m.InGrid(nb.x, nb.y) || m.Void(nb.x, nb.y) || m.Reserved(nb.x, nb.y) || m.Fixed(nb.x, nb.y)) continue;
                        sides.Add((nb, d ^ 1));
                    }
                if (sides.Count == 0) continue;
                var (cell, dir) = sides[rnd.Next(sides.Count)];
                SetWall(m, cell.x, cell.y, dir, false);
            }
            // V0.7.12 : appartements fermés sous cadenas (leur porte est leur seule entrée).
            LockRooms(m, s, wellCells, locked, rnd);
            // Les autres portes : 40 % d'arches (sans battant).
            foreach (var e in m.doorEdges)
                if (!locked.Exists(l => l.storey == s && l.edge.Equals(e)) && rnd.NextDouble() < BlockArchChance) m.archEdges.Add(e);
            storeys[s] = m;
        }
        yield return null;

        // ---------- Géométrie de chaque étage ----------
        for (int s = 0; s < storeys.Length; s++)
        {
            float y = StoreyLevels[s];
            var storeyRoot = new GameObject("Etage" + s).transform;
            storeyRoot.SetParent(root, false);
            storeyRoot.localPosition = Vector3.up * y;
            var storeyColliders = new GameObject("Colliders_Etage" + s);
            storeyColliders.transform.SetParent(colliders.transform, false);
            storeyColliders.transform.localPosition = Vector3.up * y;
            var geometry = BuildGeometry(storeys[s], storeyRoot, storeyColliders, new System.Random(seed * 97 + zone * 173 + s));
            while (geometry.MoveNext()) yield return null;
            BuildBevels(storeys[s], storeyRoot, new ZoneRefs(), new System.Random(seed * 53 + zone * 929 + s));
            yield return null;
        }
        refs.maze = main;
        refs.entrance = main.Center(main.start.x, main.start.y);

        // ---------- Ravin, cages d'escalier ----------
        var shell = new Kit(root, colliders.transform, "Z" + zone + "_Blockhaus");
        BuildRavine(main, ravineCells, shell, rnd, previous);
        foreach (var w in wells) BuildStairwell(main, storeys, w, shell, rnd);
        shell.Flush();
        yield return null;

        // ---------- Portes, éclairage, mobilier, emplacements des monticules ----------
        var props = new GameObject("Decor_Z" + zone).transform;
        props.SetParent(root, false);
        for (int s = 0; s < storeys.Length; s++)
        {
            Maze m = storeys[s];
            float y = StoreyLevels[s];
            // Seuls les appartements fermés ont une porte (sous cadenas) ; les autres passages restent ouverts.
            foreach (var (i, j, north) in m.doorEdges)
            {
                bool isLocked = locked.Exists(l => l.storey == s && l.edge.Equals((i, j, north)));
                if (!isLocked) continue;
                Vector3 edge = m.Center(i, j) + (north ? Vector3.forward : Vector3.right) * (Cell * 0.5f) + Vector3.up * y;
                var door = ApartmentDoor.Create(props, zone * 100000 + doorIds++, edge, Quaternion.Euler(0f, north ? 0f : 90f, 0f), DoorWidth - 0.1f, DoorHeight - 0.05f, isLocked);
                if (isLocked) refs.lockedDoors.Add(door);
            }
            var finish = new Kit(props, colliders.transform, "Z" + zone + "_Finitions" + s);
            DecorateStorey(m, y, props, finish, refs, rnd, s == HubStorey, locked.FindAll(l => l.storey == s));
            finish.Flush();
            yield return null;
        }

        // Cloches Décoratives : à l'étage le plus haut, suspendues au plafond.
        Maze top = storeys[storeys.Length - 1];
        var topRooms = top.rooms.FindAll(r => !top.Fixed(r.cells[0].x, r.cells[0].y) && r.cells.Count >= 2 && !locked.Exists(l => l.room == r));
        Shuffle(topRooms, rnd);
        foreach (Room room in topRooms)
        {
            if (refs.bellSpots.Count >= GameRules.HangingBellsInZone(zone)) break;
            Vector3 c = RoomFloorCenter(top, room, StoreyLevels[storeys.Length - 1]);
            refs.bellSpots.Add((HangingBellSpot(c, room.ceil), BellChain));
            // 17.3 : l'endroit le plus haut atteignable.
            main.perches.Add((new Vector3(c.x + 2f, StoreyLevels[storeys.Length - 1], c.z), 0f, 3f));
        }

        // V0.7.12 : contenu des appartements fermés (Super Monticule, Cloche Décorative ou deux monticules simples) ; en Zone 3, le
        // cadavre de Tête-de-Cloche dans l'un d'eux (sous une Cloche, rien au sol).
        bool corpsePlaced = false;
        Physics.SyncTransforms();
        foreach (var l in locked)
        {
            Maze m = storeys[l.storey];
            Vector3 c = RoomFloorCenter(m, l.room, StoreyLevels[l.storey]);
            if (zone == GameRules.SecretCorpseZone && !corpsePlaced)
            {
                SecretCorpse.Build(root, c, 0f, 2f);
                corpsePlaced = true;
                refs.bellSpots.Add((HangingBellSpot(c, l.room.ceil), BellChain));
                continue;
            }
            int content = rnd.Next(3);
            if (content == 0) refs.forcedSupers.Add(c);
            else if (content == 1) refs.bellSpots.Add((HangingBellSpot(c, l.room.ceil), BellChain));
            else
            {
                // Deux monticules, dans deux cellules de l'appartement s'il en a plusieurs.
                Vector3 lift = Vector3.up * StoreyLevels[l.storey];
                bool two = l.room.cells.Count > 1;
                refs.forcedMounds.Add(two ? m.Center(l.room.cells[0].x, l.room.cells[0].y) + lift : c - Vector3.right * 1.6f);
                refs.forcedMounds.Add(two ? m.Center(l.room.cells[1].x, l.room.cells[1].y) + lift : c + Vector3.right * 1.6f);
            }
        }

        BuildNeutralRoom(main, root, refs, new System.Random(seed * 59 + zone * 613));
        yield return null;
        Physics.SyncTransforms();
        var supers = FindSuperMoundSpots(main, refs, new System.Random(seed * 67 + zone * 389));
        while (supers.MoveNext()) yield return null;

        refs.nav = new MazeNav();
        refs.nav.AddZone(main.origin, n, Cell, (i, j) => !main.Reserved(i, j), (i, j) => BlockhausDepth + 2f, refs.lowestFloor);
        refs.nav.Prepare(colliders.transform, refs.door != null ? refs.door.Blocker : null);
        AddReflectionProbes(main, root);
        if (zone == GameRules.SecretCorpseZone && !corpsePlaced) PlaceSecretCorpse(main, root);
        // Hauteur du mur nord (la zone suivante complète au-dessus) : 20 m, 80 m au droit du ravin.
        for (int i = 0; i < n; i++) main.northHeights[i] = ravineCells.Contains(new Vector2Int(i, n - 1)) ? RavineReach : BlockhausDepth;
    }

    const float BellChain = 1.6f;

    static Vector3 HangingBellSpot(Vector3 floorCenter, float ceil) => floorCenter + Vector3.up * (ceil - BellChain - GameRules.BellHeight);

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

    // Environ 5 % des appartements (hors Hub et Neutral Room, hors appartements de double hauteur et voisins des cages d'escalier)
    // n'ont plus qu'une porte, sous cadenas : toutes leurs autres ouvertures (portes, passages vers les appartements voisins, fenêtres)
    // sont murées. Un appartement par lequel il faut passer pour atteindre d'autres pièces n'est jamais fermé.
    static void LockRooms(Maze m, int storey, HashSet<Vector2Int> wellCells, List<LockedRoom> locked, System.Random rnd)
    {
        var rooms = new List<Room>(m.rooms);
        Shuffle(rooms, rnd);
        foreach (Room room in rooms)
        {
            if (room.cells.Count == 0 || rnd.NextDouble() >= LockedRoomChance || room.ceil > StoreyHeight + 0.01f) continue;
            if (room.cells.Exists(c => m.Fixed(c.x, c.y) || m.Reserved(c.x, c.y) || m.keepClear.Contains(c))) continue;
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
            if (Components(m, cellSet) != Components(m, null)) continue;
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

    // Nombre d'ensembles de cellules reliées (sans mur entre elles) à cet étage, sans compter 'without'.
    static int Components(Maze m, HashSet<Vector2Int> without)
    {
        var seen = new bool[m.N, m.N];
        int count = 0;
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (seen[i, j] || m.Void(i, j) || without != null && without.Contains(new Vector2Int(i, j))) continue;
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
                        if (!m.InGrid(ni, nj) || seen[ni, nj] || m.Void(ni, nj) || without != null && without.Contains(new Vector2Int(ni, nj))) continue;
                        if (HasWall(m, q.x, q.y, d)) continue;
                        seen[ni, nj] = true;
                        queue.Enqueue(new Vector2Int(ni, nj));
                    }
                }
            }
        return count;
    }

    // Fond du ravin (-80 m), plafond (+80 m), et les façades au-dessus et au-dessous des étages, percées de fenêtres factices.
    static void BuildRavine(Maze m, HashSet<Vector2Int> cells, Kit kit, System.Random rnd, ZoneRefs previous)
    {
        foreach (var c in cells)
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
                Vector3 dir = new Vector3(DI[d], 0f, DJ[d]);
                Vector3 edge = center + dir * (Cell * 0.5f);
                // Au-dessus et au-dessous des étages (sur le bord de la zone, toute la hauteur au-dessus de ce qui existe déjà).
                float from = BlockhausDepth;
                if (boundary && d == 3 && previous != null) from = Mathf.Max(from, previous.maze.northHeights[c.x]);
                foreach (var (y0, y1) in new[] { (-RavineReach, -BlockhausDepth), (from, RavineReach) })
                {
                    if (y1 - y0 < 0.05f) continue;
                    Vector3 wc = edge + dir * (Wall * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
                    kit.Box(wc, d < 2 ? new Vector3(Wall, y1 - y0, Cell + Wall) : new Vector3(Cell + Wall, y1 - y0, Wall));
                    if (boundary) continue;
                    // Fenêtres factices de barre soviétique (noires, 10 % éclairées).
                    Vector3 side = new Vector3(DJ[d], 0f, DI[d]);
                    for (float wy = y0 + 2f; wy < y1 - 1.5f; wy += 3.4f)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            Vector3 p = edge - dir * 0.02f + side * (k * 1.2f) + Vector3.up * wy;
                            MeshBuilder mb = rnd.NextDouble() < 0.1 ? kit.grey : kit.dark;
                            mb.WorldQuad(p - side * 0.7f, p + side * 0.7f, p + side * 0.7f + Vector3.up * 1.4f, p - side * 0.7f + Vector3.up * 1.4f, -dir, 1f);
                        }
                }
            }
        }
    }

    // Cage d'escalier (2 x 2 cellules) : un colimaçon de -20 m à +20 m autour d'un pilier, des paliers vers les portes de chaque étage ;
    // V0.7.12 : 70 % ont une rambarde du côté du vide (ouverte à hauteur des paliers).
    static void BuildStairwell(Maze main, Maze[] storeys, Vector2Int w, Kit kit, System.Random rnd)
    {
        Vector3 center = (main.Center(w.x, w.y) + main.Center(w.x + 1, w.y + 1)) * 0.5f;
        float half = Cell;
        kit.Floor(Rect.MinMaxRect(center.x - half, center.z - half, center.x + half, center.z + half), -BlockhausDepth);
        kit.Box(center + Vector3.up * 0f, new Vector3(1.6f, BlockhausDepth * 2f, 1.6f));
        float start = Rand(rnd, 0f, 360f), rIn = 1f, rOut = half - 0.6f;
        KitSpiral(kit, center, rIn, rOut, -BlockhausDepth, BlockhausDepth, start);
        if (rnd.NextDouble() < RailingChance) KitSpiralRailing(kit, center, rIn, rOut, -BlockhausDepth, BlockhausDepth, start);
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
        kit.walls.WorldQuad(new Vector3(center.x - half, BlockhausDepth, center.z - half), new Vector3(center.x - half, BlockhausDepth, center.z + half),
            new Vector3(center.x + half, BlockhausDepth, center.z + half), new Vector3(center.x + half, BlockhausDepth, center.z - half), Vector3.down, 0.5f);
    }

    // Rambarde noire au bord extérieur d'un colimaçon (montants toutes les 4 marches, main courante), avec sa collision ; elle s'ouvre
    // autour de chaque étage, là où l'on passe de l'escalier au palier.
    static void KitSpiralRailing(Kit kit, Vector3 center, float rIn, float rOut, float y0, float y1, float start)
    {
        float rMid = (rIn + rOut) * 0.5f, stepAngle = SpiralTread / rMid * Mathf.Rad2Deg, r = rOut - 0.08f;
        int steps = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / SpiralRise));
        float rise = (y1 - y0) / steps;
        const int every = 4;
        const float height = 0.95f;
        Vector3 Point(int k) => center + Quaternion.Euler(0f, start + k * stepAngle, 0f) * Vector3.forward * r + Vector3.up * (y0 + k * rise);
        bool NearLanding(float y)
        {
            foreach (float level in StoreyLevels) if (Mathf.Abs(y - level) < 1.3f) return true;
            return false;
        }
        for (int k = every; k + every <= steps; k += every)
        {
            Vector3 a = Point(k), b = Point(k + every);
            if (NearLanding(a.y) || NearLanding(b.y)) continue;
            Vector3 ta = a + Vector3.up * height, tb = b + Vector3.up * height;
            Vector3 d = tb - ta;
            kit.dark.WorldMesh(UnitCube.vertices, UnitCube.triangles, Matrix4x4.TRS((ta + tb) * 0.5f, Quaternion.LookRotation(d), new Vector3(0.05f, 0.05f, d.magnitude)), 1f);
            kit.dark.WorldMesh(UnitCube.vertices, UnitCube.triangles, Matrix4x4.TRS(a + Vector3.up * (height * 0.5f), Quaternion.identity, new Vector3(0.04f, height, 0.04f)), 1f);
            // Collision : une planche inclinée sous la main courante.
            var go = new GameObject("Rambarde");
            go.transform.SetParent(kit.colliders, false);
            go.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.up * (height * 0.5f), Quaternion.LookRotation(b - a));
            go.AddComponent<BoxCollider>().size = new Vector3(0.08f, height, (b - a).magnitude);
        }
    }

    static readonly string[] BlockhausFurniture = { "Lavabo", "Douche", "Armoire", "Urinoir", "Baignoire" };

    static float BlockhausModelHeight(string model) =>
        model == "Armoire" ? 2f : model == "Baignoire" ? 1.7f : model == "Douche" ? 1f : model == "Urinoir" ? 0.8f : model == "BoiteLettres" ? 0.55f : 0.7f;

    // Hauteur du milieu de l'objet au-dessus du sol (contre le mur) ; 0 : posé au sol.
    static float BlockhausMountHeight(string model, System.Random rnd)
    {
        switch (model)
        {
            case "Lavabo": return 0.8f;                 // à hauteur des mains
            case "Douche": return 2.15f;                // au-dessus de la tête
            case "Urinoir": return 0.75f;               // à hauteur du bassin
            default: return 0f;
        }
    }

    // Étage d'un Blockhaus : néons des couloirs, appartements éclairés trois fois sur quatre, plafonds gris (40 %), plinthes noires,
    // néons au-dessus des fenêtres du ravin, mobilier soviétique contre les murs (modèles Meshy), boîtes aux lettres, quelques yeux,
    // emplacements des monticules.
    static void DecorateStorey(Maze m, float y, Transform props, Kit finish, ZoneRefs refs, System.Random rnd, bool hubStorey, List<LockedRoom> lockedHere)
    {
        var placed = new List<Vector3>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                var cell = new Vector2Int(i, j);
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
                    bool doorway = !HasWall(m, i, j, d) && (m.doorEdges.Contains(EdgeKey(i, j, d)) || m.Void(ni, nj));
                    // Plinthes noires au pied des murs (de part et d'autre des portes).
                    if (HasWall(m, i, j, d)) Plinth(finish, face, dir, Cell - Wall, y);
                    else if (doorway)
                    {
                        float part = (Cell - Wall - DoorWidth) * 0.5f;
                        foreach (float sgn in new[] { -1f, 1f }) Plinth(finish, face + side * (sgn * (DoorWidth * 0.5f + part * 0.5f)), dir, part, y);
                    }
                    // Néon au-dessus des vraies fenêtres, côté ravin.
                    if (m.HasWallHole(i, j, d) && m.Void(ni, nj))
                        AddWallNeon(props, c + dir * (Cell * 0.5f + Wall * 0.5f + 0.06f) + Vector3.up * 3.3f, dir, 8f, 1f, rnd);
                }
                // Boîtes aux lettres dans les couloirs, en colonnes contre un mur plein (à toutes les hauteurs).
                if (room == null && !clear && rnd.NextDouble() < 0.1)
                {
                    var box = GameArt.SpawnModelFloor("BoiteLettres", props, BlockhausModelHeight("BoiteLettres"));
                    if (box != null)
                    {
                        float h = GameArt.LocalBounds(box.transform).size.y;
                        float bottom = Rand(rnd, 0.3f, 1f);
                        if (!PlaceAgainstWall(m, box, cell, rnd.Next(4), bottom, 0.02f, placed, rnd, y)) Object.Destroy(box);
                        else
                            for (float above = bottom + h + 0.08f; above + h < 2.9f && rnd.NextDouble() < 0.7; above += h + 0.08f)
                            {
                                var more = Object.Instantiate(box, props);
                                more.transform.SetPositionAndRotation(box.transform.position + Vector3.up * (above - bottom), box.transform.rotation);
                            }
                    }
                }
            }
        foreach (Room room in m.rooms)
        {
            if (room.cells.Count == 0 || m.Fixed(room.cells[0].x, room.cells[0].y)) continue;
            // 25 % des appartements ne sont pas éclairés.
            if (rnd.NextDouble() >= 0.25) AddLamp(props, RoomFloorCenter(m, room, y) + Vector3.up * room.ceil, 10f, 1.3f, rnd);
            // V0.7.12 : 40 % ont un plafond gris.
            if (rnd.NextDouble() < GreyCeilingChance)
                foreach (var cell in room.cells)
                {
                    Vector3 c = m.Center(cell.x, cell.y);
                    float e = Cell * 0.5f, top = y + room.ceil - 0.02f;
                    finish.grey.WorldQuad(new Vector3(c.x - e, top, c.z - e), new Vector3(c.x - e, top, c.z + e), new Vector3(c.x + e, top, c.z + e), new Vector3(c.x + e, top, c.z - e), Vector3.down, 0.5f);
                }
            // L'appartement fermé garde le sol libre pour son contenu.
            if (lockedHere.Exists(l => l.room == room)) continue;
            int count = 1 + rnd.Next(room.cells.Count + 1);
            for (int k = 0; k < count; k++)
            {
                string model = BlockhausFurniture[rnd.Next(BlockhausFurniture.Length)];
                var cell = room.cells[rnd.Next(room.cells.Count)];
                var go = GameArt.SpawnModelFloor(model, props, BlockhausModelHeight(model));
                if (go == null) continue;
                float mount = BlockhausMountHeight(model, rnd);
                float bottom = mount > 0f ? mount - GameArt.LocalBounds(go.transform).size.y * 0.5f : 0f;
                if (!PlaceAgainstWall(m, go, cell, rnd.Next(4), bottom, model == "Baignoire" ? 0.15f : 0.02f, placed, rnd, y))
                {
                    Object.Destroy(go);
                    continue;
                }
                Bounds b = GameArt.LocalBounds(go.transform);
                var col = go.AddComponent<BoxCollider>();
                col.center = b.center;
                col.size = b.size;
            }
        }
    }

    // Plinthe noire de 0,8 m au pied d'un mur ('face' : milieu de la face du mur, 'dir' : vers le mur), sur 'length'.
    static void Plinth(Kit kit, Vector3 face, Vector3 dir, float length, float floorY)
    {
        if (length < 0.05f) return;
        Vector3 c = new Vector3(face.x, floorY + PlinthHeight * 0.5f, face.z) - dir * 0.02f;
        Vector3 size = Mathf.Abs(dir.x) > 0.5f ? new Vector3(0.04f, PlinthHeight, length) : new Vector3(length, PlinthHeight, 0.04f);
        kit.dark.Box(c, size, 1f);
    }
}
