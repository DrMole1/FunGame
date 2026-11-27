using System.Collections.Generic;
using UnityEngine;

// V0.7.19 (11.3) : décor du Château.
// - Cimetières : les halls qui entourent les Tours et le Donjon sont remplis de croix et de pierres tombales (passages gardés libres
//   devant la porte de chaque Tour et aux ouvertures des halls) ;
// - deux cachots ont une cible humaine de plus, un Collier à Laisse (ramassable) autour du cou ;
// - dans les salles : une corniche de briques noires et blanches (Meshy) sous le plafond, le long des murs, et 5 % du plafond gris
//   couvert de carreaux de damier blancs ou noirs ; dans les couloirs : des arches de briques (Meshy) ;
//   V0.7.20 : corniches sur tout le périmètre des salles (Note 04), voûte en berceau faite du haut des arches tout le long des
//   couloirs (Note 05) ;
// - tonneaux, cibles humaines, cibles d'archerie, croix, mannequins et pierres tombales : 15 % de chance d'être plus gros (+30 à +80 %
//   de volume) ; mannequins : 15 % de chance d'avoir une tête de chien (celles du décor de l'Échiquier) ;
// - (V0.7.20, Note 09 : plus de menottes) ; derrière les remparts, sur les toits, des tours décoratives inaccessibles coiffées du toit de
//   Kremlin des anciennes Maisons-Fou (Note 08).
public static partial class WorldBuilder
{
    const float BigPropChance = 0.15f, DogHeadChance = 0.15f;
    const double CemeteryMoundChance = 0.4;
    const int CollarDungeons = 2;
    // Une salle (ou un couloir) dont un côté fait moins de 10 m : tout objet décoratif y touche un mur.
    const float CastleNarrowSide = 10f;

    static bool NarrowRoom(Room room, float limit) => room != null && Mathf.Min(room.rect.width, room.rect.height) * Cell < limit - 0.01f;

    // Variantes d'un objet après son apparition : à plat, plus gros, tête de chien. À appeler avant de le placer (sa taille compte).
    static void DressProp(GameObject go, string model, System.Random rnd)
    {
        if (go == null) return;
        if (model == "Menottes") LayFlat(go);
        bool canGrow = model.StartsWith("Tonneau") || model == "CibleHumaine" || model == "Cible" || model == "CroixOrthodoxe"
            || model == "Mannequin" || model.StartsWith("PierreTombale");
        if (canGrow && rnd.NextDouble() < BigPropChance)
            go.transform.localScale *= Mathf.Pow(Rand(rnd, 1.3f, 1.8f), 1f / 3f);
        if (model == "Mannequin" && rnd.NextDouble() < DogHeadChance) AddMannequinDogHead(go, rnd);
    }

    // Couche l'objet (sa plus petite dimension à la verticale) et le repose au sol.
    static void LayFlat(GameObject holder)
    {
        Transform model = holder.transform.childCount > 0 ? holder.transform.GetChild(0) : null;
        if (model == null) return;
        Bounds b = GameArt.LocalBounds(holder.transform);
        if (b.size.y <= Mathf.Min(b.size.x, b.size.z) + 0.001f) return;
        Quaternion turn = b.size.x <= b.size.z ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
        model.localRotation = turn * model.localRotation;
        model.localPosition = turn * model.localPosition;
        b = GameArt.LocalBounds(holder.transform);
        model.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
    }

    // La tête du mannequin disparaît sous une tête de chien (un skin), au même endroit, tournée comme lui.
    // V0.7.21 (Note 05) : mesuré sur le modèle Mannequin (hauteur 1,8) : la base du cou à 81,7 % de sa hauteur, au-dessus des
    // épaules ; sa tête (boule de 0,17 m) jusqu'au sommet, un peu en avant. La tête de chien est posée par son propre cou (DogHeads).
    const float MannequinNeck = 0.817f;
    static readonly Vector3 MannequinHeadMin = new Vector3(-0.095f, 0f, -0.075f), MannequinHeadMax = new Vector3(0.095f, 0.335f, 0.11f);

    static void AddMannequinDogHead(GameObject mannequin, System.Random rnd)
    {
        Bounds b = GameArt.LocalBounds(mannequin.transform);
        float k = b.size.y / 1.8f;
        var head = new Bounds();
        head.SetMinMax(MannequinHeadMin * k, MannequinHeadMax * k);
        DogHeads.SpawnOnMannequin(mannequin.transform, new Vector3(b.center.x, b.min.y + MannequinNeck * b.size.y, b.center.z - 0.02f * k), head, Mathf.Max(0.45f, b.size.y * 0.3f), rnd);
    }

    // ---------- Après le mobilier : cimetières, colliers, corniches, plafonds, arches, tours de Kremlin ----------

    static void DressCastle(Maze m, Transform root, ChateauPlan plan, ZoneRefs refs, Transform props)
    {
        var rnd = new System.Random(plan.rnd.Next());
        BuildCemeteries(m, plan, props, refs, rnd);
        AddCollarTargets(m, plan, props, refs, rnd);
        var cornices = new MeshBuilder();
        var beams = new MeshBuilder();
        var rooms = new List<Room>(plan.natures.Keys);
        foreach (Room room in rooms)
        {
            if (room.cells.Count == 0) continue;
            RoomCornices(m, room, cornices);
            // V0.7.21 (Note 07) : plus de carreaux de damier au plafond des salles : des poutres de bois blanc.
            CeilingBeams(m, room, beams);
        }
        TemplateWallCornices(plan.templateWalls, cornices);
        CorridorArches(m, plan, cornices);
        if (!cornices.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_Corniches", root, cornices.Build("Corniches"), GameArt.BrickWall);
        if (!beams.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_Poutres", root, beams.Build("Poutres"), BeamMaterial);
        KremlinTowers(m, plan, root, rnd);
        // Aucun monticule dans un pan de mur des salles sur schéma.
        refs.itemSpots.RemoveAll(p => plan.templateWalls.Exists(b => { var e = b; e.Expand(new Vector3(MoundWidth + 0.2f, 1f, MoundWidth + 0.2f)); return e.Contains(p + Vector3.up * 0.3f); }));
    }

    // Cimetières : dans les halls autour des Tours et du Donjon, des croix et des pierres tombales, à l'écart de la porte des Tours,
    // des ouvertures des halls et les unes des autres.
    static void BuildCemeteries(Maze m, ChateauPlan plan, Transform props, ZoneRefs refs, System.Random rnd)
    {
        var landings = new HashSet<Vector2Int>();
        foreach (var c in plan.site.crossings) landings.Add(c.At(MoatCells + 2));
        var cells = new List<Vector2Int>(plan.yards);
        cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var placed = new List<(Vector3 p, float r)>();
        foreach (var cell in cells)
        {
            if (landings.Contains(cell)) continue;
            Vector3 cc = m.Center(cell.x, cell.y);
            int wanted = 1 + rnd.Next(2);
            for (int attempt = 0; attempt < 10 && wanted > 0; attempt++)
            {
                // V0.7.21 : sur le sol du cimetière, 1 m sous le niveau du Hub.
                Vector3 p = new Vector3(cc.x, -CemeteryDepth, cc.z) + new Vector3(Rand(rnd, -1.8f, 1.8f), 0f, Rand(rnd, -1.8f, 1.8f));
                if (!CemeteryFree(m, plan, cell, p, placed)) continue;
                string model = rnd.NextDouble() < 0.45 ? "CroixOrthodoxe" : ModelVariant("PierreTombale", rnd);
                var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
                if (go == null) continue;
                DressProp(go, model, rnd);
                // Tournées vers la Tour la plus proche (les tombes s'alignent sur elle).
                RoundPlan tower = NearestRound(plan, p);
                Vector3 look = tower != null ? Flat3(tower.center - p) : Vector3.forward;
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? -look : Vector3.forward) * Quaternion.Euler(0f, Rand(rnd, -12f, 12f), 0f));
                Bounds wb = WorldBounds(go.transform);
                float r = Mathf.Max(wb.extents.x, wb.extents.z);
                // Elle doit tenir là (pas dans une Tour, pas trop près d'une autre).
                if (!CemeteryFree(m, plan, cell, new Vector3(wb.center.x, p.y, wb.center.z), placed, r))
                {
                    Object.Destroy(go);
                    continue;
                }
                placed.Add((new Vector3(wb.center.x, p.y, wb.center.z), r));
                AddPropCollider(go);
                wanted--;
                // V0.7.21 : les monticules du cimetière sont au pied des croix et des pierres tombales (et contiennent un Os).
                if (rnd.NextDouble() < CemeteryMoundChance) CemeteryMoundAt(m, plan, cell, go, placed, refs, rnd);
            }
        }
    }

    static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static RoundPlan NearestRound(ChateauPlan plan, Vector3 p)
    {
        RoundPlan best = null;
        float bestD = float.MaxValue;
        foreach (var r in plan.rounds)
        {
            float d = Flat(r.center - p) - r.radius;
            if (d < bestD) { bestD = d; best = r; }
        }
        return best;
    }

    static bool CemeteryFree(Maze m, ChateauPlan plan, Vector2Int cell, Vector3 p, List<(Vector3 p, float r)> placed, float r = 0.8f)
    {
        foreach (var round in plan.rounds)
        {
            float d = Flat(round.center - p);
            if (d < round.radius + r + 1.2f) return false;
            // Devant la porte : un passage de 3 m de large sur 7 m.
            Vector3 door = Quaternion.Euler(0f, round.doorAngle, 0f) * Vector3.forward;
            Vector3 rel = Flat3(p - round.center);
            float along = Vector3.Dot(rel, door), across = Mathf.Abs(Vector3.Dot(rel, new Vector3(door.z, 0f, -door.x)));
            if (along > 0f && along < round.radius + 7f && across < 1.5f + r + 0.6f) return false;
        }
        foreach (var q in placed) if (Flat(q.p - p) < q.r + r + 1.4f) return false;
        // Le hall reste traversable : rien à moins de 2 m d'une ouverture vers une salle, un couloir ou une entrée.
        Vector3 c = m.Center(cell.x, cell.y);
        if (Mathf.Abs(p.x - c.x) > Cell * 0.5f - Wall * 0.5f - r || Mathf.Abs(p.z - c.z) > Cell * 0.5f - Wall * 0.5f - r) return false;
        for (int d = 0; d < 4; d++)
        {
            int ni = cell.x + DI[d], nj = cell.y + DJ[d];
            if (!m.InGrid(ni, nj) || plan.yards.Contains(new Vector2Int(ni, nj)) || HasWall(m, cell.x, cell.y, d)) continue;
            float toSide = Cell * 0.5f - (DI[d] != 0 ? (p.x - c.x) * DI[d] : (p.z - c.z) * DJ[d]);
            if (toSide < 2f + r) return false;
        }
        return true;
    }

    // Cou du modèle CibleHumaine (Resources/Art/CibleHumaine.fbx) : la section la plus étroite entre les épaules et la tête, en
    // fractions de la hauteur du modèle (x, z : depuis le centre de son encombrement ; y : depuis sa base).
    static readonly Vector3 CollarNeck = new Vector3(0.010f, 0.779f, -0.0773f);

    // Deux cachots ont une cible humaine de plus, un Collier à Laisse au cou (ramassable : GameSession, à chaque manche).
    static void AddCollarTargets(Maze m, ChateauPlan plan, Transform props, ZoneRefs refs, System.Random rnd)
    {
        var dungeons = new List<Room>();
        foreach (var kv in plan.natures) if (kv.Value == Dungeon && kv.Key.cells.Count > 0) dungeons.Add(kv.Key);
        Shuffle(dungeons, rnd);
        int done = 0;
        foreach (Room room in dungeons)
        {
            if (done >= CollarDungeons) break;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var cell = room.cells[rnd.Next(room.cells.Count)];
                if (m.keepClear.Contains(cell) || m.archCells.Contains(cell)) continue;
                var go = GameArt.SpawnModelFloor("CibleHumaine", props, ModelHeight("CibleHumaine"));
                if (go == null) return;
                var placed = new List<Vector3>();
                bool ok = NarrowRoom(room, CastleNarrowSide)
                    ? PlaceAgainstWall(m, go, cell, rnd.Next(4), 0f, 0.25f, placed, rnd)
                    : PlaceOnFloor(m, room, go, cell, 0f, Rand(rnd, 0f, 360f), placed, rnd) && FitInCell(m, room, go, cell)
                      && !NearOpening(m, room, cell, go.transform.position);
                if (!ok || Physics.CheckSphere(go.transform.position + Vector3.up * 1f, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    Object.Destroy(go);
                    continue;
                }
                AddPropCollider(go);
                Physics.SyncTransforms();
                // V0.7.20 (Note 02) : centré sur le cou du modèle (mesuré dans le repère du FBX : 15 cm en arrière du centre de la
                // cible, au bas de la tête), l'anneau posé juste sous le menton.
                Bounds lb = GameArt.LocalBounds(go.transform);
                float h = lb.size.y;
                Vector3 neck = go.transform.TransformPoint(new Vector3(lb.center.x + CollarNeck.x * h, lb.min.y + CollarNeck.y * h, lb.center.z + CollarNeck.z * h));
                refs.collarSpots.Add(neck - Vector3.up * 0.05f);
                done++;
                break;
            }
        }
    }

    // Corniche (modèle Meshy normalisé : x le long du mur, y vers le haut, z en profondeur), retournée sous le plafond, le long de
    // chaque mur de la salle. V0.7.20 (Note 04) : sur tout le périmètre : les tronçons se rejoignent d'une cellule à l'autre (plus de
    // vide à chaque jonction de cellules), font le tour des angles saillants et passent au-dessus des ouvertures quand un linteau
    // monte jusqu'au plafond de la salle.
    const float CorniceHeight = 0.85f, CorniceDepth = 0.55f;

    static void RoomCornices(Maze m, Room room, MeshBuilder mb)
    {
        Mesh mesh = Resources.Load<Mesh>("Art/Decor/Corniche");
        if (mesh == null) return;
        Vector3[] v = mesh.vertices, vn = mesh.normals;
        int[] t = mesh.triangles;
        // Côté 'd' de la cellule bordé (mur plein, ou ouverture sous un linteau qui monte jusqu'au plafond de la salle).
        bool Lined(Vector2Int c, int d)
        {
            if (!m.InGrid(c.x, c.y) || m.RoomAt(c.x, c.y) != room) return false;
            int ni = c.x + DI[d], nj = c.y + DJ[d];
            if (m.InGrid(ni, nj) && m.RoomAt(ni, nj) == room) return false;
            if (HasWall(m, c.x, c.y, d)) return true;
            if (!m.InGrid(ni, nj)) return false;
            // V0.7.21 (Note 02) : au-dessus d'une arche, ou d'une porte vers un hall ou un cimetière (le mur monte jusqu'à 12 m), la
            // corniche continue (avant, elle s'interrompait au-dessus de chaque arche).
            if (m.Void(ni, nj) || m.archWalls.Contains(EdgeKey(c.x, c.y, d))) return true;
            return m.Ceil(ni, nj) <= room.ceil - CorniceHeight;
        }
        foreach (var cell in room.cells)
            for (int d = 0; d < 4; d++)
            {
                if (!Lined(cell, d)) continue;
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                // Bout de chaque côté : jusqu'au bord de la cellule si le mur continue dans la cellule voisine de la salle, jusqu'au
                // mur perpendiculaire dans un angle rentrant, au-delà du poteau dans un angle saillant (le tronçon d'en face y arrive).
                float[] ends = new float[2];
                for (int e = 0; e < 2; e++)
                {
                    float sg = e == 0 ? -1f : 1f;
                    int sd = SideDir(side * sg);
                    var nb = new Vector2Int(cell.x + Mathf.RoundToInt(side.x * sg), cell.y + Mathf.RoundToInt(side.z * sg));
                    bool sameRoom = m.InGrid(nb.x, nb.y) && m.RoomAt(nb.x, nb.y) == room && !HasWall(m, cell.x, cell.y, sd);
                    if (!sameRoom) ends[e] = Cell * 0.5f - Wall * 0.5f;
                    else if (Lined(nb, d)) ends[e] = Cell * 0.5f;
                    else ends[e] = Cell * 0.5f + Wall * 0.5f + CorniceDepth;
                }
                float len = ends[0] + ends[1], shift = (ends[1] - ends[0]) * 0.5f;
                Vector3 face = m.Center(cell.x, cell.y) + n * (Cell * 0.5f - Wall * 0.5f) + side * shift;
                Quaternion rot = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, 180f);
                Vector3 at = face - n * (CorniceDepth * 0.5f) + Vector3.up * room.ceil;
                mb.WorldMeshSmooth(v, t, vn, Matrix4x4.TRS(at, rot, new Vector3(len, CorniceHeight, CorniceDepth)), 0.5f);
            }
    }

    // Côté (0 +X, 1 -X, 2 +Z, 3 -Z) d'une direction horizontale unitaire.
    static int SideDir(Vector3 dir) => Mathf.Abs(dir.x) > 0.5f ? (dir.x > 0f ? 0 : 1) : (dir.z > 0f ? 2 : 3);

    // V0.7.21 (Note 07) : poutres de bois blanc (texture Meshy) sous le plafond gris des salles (ni couloirs, ni Tours, ni Donjon) :
    // parallèles au plus petit côté de la salle, régulièrement espacées, le plafond visible entre elles ; elles reposent sur les
    // corniches le long des murs.
    const float BeamWidth = 0.28f, BeamHeight = 0.32f, BeamSpacing = 1.25f;

    static Material beamMaterial;
    static Material BeamMaterial
    {
        get
        {
            if (beamMaterial != null) return beamMaterial;
            beamMaterial = Resources.Load<Material>("Art/Decor/PoutreBois");
            if (beamMaterial == null) beamMaterial = GameArt.Lit("PoutreBois", new Color(0.92f, 0.91f, 0.88f), GameArt.Noise("wood", 128, 0.85f, 0.15f, 5), 0.1f);
            return beamMaterial;
        }
    }

    static void CeilingBeams(Maze m, Room room, MeshBuilder mb)
    {
        // Les poutres franchissent le plus petit côté de la salle : elles courent le long de cet axe.
        bool alongX = room.rect.width <= room.rect.height;
        float y = room.ceil - BeamHeight * 0.5f - 0.02f;
        int dLo = alongX ? 1 : 3, dHi = alongX ? 0 : 2;
        float inset = Wall * 0.5f + CorniceDepth * 0.5f;
        foreach (var cell in room.cells)
        {
            Vector3 c = m.Center(cell.x, cell.y);
            // Bouts de la poutre dans la cellule : au mur (moins la corniche), sinon jusqu'au bord de la cellule.
            bool WallSide(int d)
            {
                int ni = cell.x + DI[d], nj = cell.y + DJ[d];
                return !m.InGrid(ni, nj) || m.RoomAt(ni, nj) != room || HasWall(m, cell.x, cell.y, d);
            }
            float lo = -Cell * 0.5f + (WallSide(dLo) ? inset : 0f), hi = Cell * 0.5f - (WallSide(dHi) ? inset : 0f);
            if (hi - lo < 0.5f) continue;
            // Positions le long de l'autre axe : une grille commune à toute la salle (les poutres se suivent d'une cellule à l'autre).
            float across0 = alongX ? c.z - Cell * 0.5f : c.x - Cell * 0.5f;
            float origin = alongX ? m.origin.z : m.origin.x;
            float first = origin + Mathf.Ceil((across0 + 0.4f - origin) / BeamSpacing) * BeamSpacing;
            float mid = (lo + hi) * 0.5f, len = hi - lo;
            for (float t = first; t <= across0 + Cell - 0.4f; t += BeamSpacing)
            {
                Vector3 center = alongX ? new Vector3(c.x + mid, y, t) : new Vector3(t, y, c.z + mid);
                Vector3 size = alongX ? new Vector3(len, BeamHeight, BeamWidth) : new Vector3(BeamWidth, BeamHeight, len);
                // Sans face du dessus (contre le plafond).
                mb.Box(center, size, 0.6f, 63 & ~(1 << 2));
            }
        }
    }

    // V0.7.21 (Note 06) : la corniche fait aussi le tour des pans de mur des salles sur schéma, sous le plafond.
    static void TemplateWallCornices(List<Bounds> boxes, MeshBuilder mb)
    {
        if (boxes.Count == 0) return;
        Mesh mesh = Resources.Load<Mesh>("Art/Decor/Corniche");
        if (mesh == null) return;
        Vector3[] v = mesh.vertices, vn = mesh.normals;
        int[] t = mesh.triangles;
        foreach (Bounds b in boxes)
            for (int d = 0; d < 4; d++)
            {
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]);
                bool xFace = Mathf.Abs(n.x) > 0.5f;
                float half = xFace ? b.extents.x : b.extents.z, len = xFace ? b.size.z : b.size.x;
                Vector3 face = new Vector3(b.center.x, 0f, b.center.z) + n * half;
                // Face collée à un autre pan de mur : pas de corniche.
                Vector3 probe = face + n * 0.1f + Vector3.up * b.center.y;
                if (boxes.Exists(o => o.Contains(probe))) continue;
                Quaternion rot = Quaternion.LookRotation(n) * Quaternion.Euler(0f, 0f, 180f);
                Vector3 at = face + n * (CorniceDepth * 0.5f) + Vector3.up * b.max.y;
                mb.WorldMeshSmooth(v, t, vn, Matrix4x4.TRS(at, rot, new Vector3(len + CorniceDepth * 2f, CorniceHeight, CorniceDepth)), 0.5f);
            }
    }

    // Arches de briques (Meshy, normalisée : x en largeur, y en hauteur, z en épaisseur) dans les couloirs du Château, d'un mur à
    // l'autre. V0.7.20 (Note 05) : seulement le haut de l'arche (Resources/Art/Decor/ArcheCouloirHaut : la voûte au-dessus de la
    // naissance de l'arc, sans ses pieds, découpée dans le modèle Meshy), élargi d'un mur à l'autre et répété tout du long : une voûte
    // en berceau couvre tout le plafond des couloirs (deux voûtes croisées aux carrefours et aux angles).
    const int VaultPiecesPerCell = 3;
    const float VaultShare = 0.4f;

    static void CorridorArches(Maze m, ChateauPlan plan, MeshBuilder mb)
    {
        Mesh mesh = Resources.Load<Mesh>("Art/Decor/ArcheCouloirHaut");
        if (mesh == null) return;
        Vector3[] v = mesh.vertices, vn = mesh.normals;
        int[] t = mesh.triangles;
        float piece = Cell / VaultPiecesPerCell;
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.InStructure(i, j) || m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j) || m.RoomAt(i, j) != null) continue;
                if (plan.yards.Contains(new Vector2Int(i, j))) continue;
                bool runsZ = !HasWall(m, i, j, 2) || !HasWall(m, i, j, 3);
                bool runsX = !HasWall(m, i, j, 0) || !HasWall(m, i, j, 1);
                if (!runsZ && !runsX) continue;
                float ceil = m.Ceil(i, j), height = ceil * VaultShare;
                Vector3 c = m.Center(i, j) + Vector3.up * (ceil - height);
                for (int axis = 0; axis < 2; axis++)
                {
                    bool alongZ = axis == 0;
                    if (alongZ ? !runsZ : !runsX) continue;
                    // La voûte s'appuie sur les deux murs du couloir (ou, sans mur, d'un bord à l'autre de la cellule).
                    Quaternion rot = alongZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                    Vector3 run = alongZ ? Vector3.forward : Vector3.right;
                    for (int k = 0; k < VaultPiecesPerCell; k++)
                    {
                        Vector3 at = c + run * (-Cell * 0.5f + piece * (k + 0.5f));
                        mb.WorldMeshSmooth(v, t, vn, Matrix4x4.TRS(at, rot, new Vector3(Cell - Wall, height, piece + 0.01f)), 0.5f);
                    }
                }
            }
    }

    static readonly Dictionary<Material, Material> luminous = new Dictionary<Material, Material>();

    static Material LuminousCopy(Material src)
    {
        if (src == null) return null;
        if (luminous.TryGetValue(src, out var m)) return m;
        m = new Material(GameArt.Emissive("Lumineux_" + src.name, Color.white, Color.white)) { name = src.name + "_Lumineux" };
        m.mainTexture = src.mainTexture;
        m.SetTexture("_EmissionMap", src.mainTexture);
        m.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f));
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return luminous[src] = m;
    }

    // Note 08 : tours décoratives derrière les remparts (sur les toits de la première rangée de salles et de couloirs), le corps rond en
    // briques et le toit de Kremlin des anciennes Maisons-Fou ; ni collision ni accès.
    const int MinKremlinPerRampart = 6;

    static void KremlinTowers(Maze m, ChateauPlan plan, Transform root, System.Random rnd)
    {
        var spots = new List<Vector2Int>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
                if (m.InStructure(i, j) && plan.site.dist[i, j] == MoatCells + 2 && !m.Void(i, j) && !m.Reserved(i, j) && !m.Fixed(i, j))
                    spots.Add(new Vector2Int(i, j));
        Shuffle(spots, rnd);
        var chosen = new List<Vector3>();
        var kit = new Kit(root, root, "Z" + m.zone + "_ToursKremlin");
        // V0.7.21 (Note 10) : au moins 6 tours de Kremlin par rempart (avant : 10 pour tout le Château), espacées d'au moins 10 m.
        var counts = new Dictionary<int, int>();
        int Side(Vector2Int c)
        {
            // Rempart le plus proche (dans l'une des quatre directions).
            int best = 0, bestD = int.MaxValue;
            for (int d = 0; d < 4; d++)
            {
                int k = 1;
                while (k < m.N && m.InGrid(c.x + DI[d] * k, c.y + DJ[d] * k) && !plan.rampart.Contains(new Vector2Int(c.x + DI[d] * k, c.y + DJ[d] * k))) k++;
                if (k < bestD) { bestD = k; best = d; }
            }
            return best;
        }
        foreach (var c in spots)
        {
            int side = Side(c);
            counts.TryGetValue(side, out int have);
            if (have >= MinKremlinPerRampart + 2) continue;
            Vector3 p = m.Center(c.x, c.y);
            if (chosen.Exists(q => Flat(q - p) < 10f)) continue;
            counts[side] = have + 1;
            chosen.Add(p);
            float baseY = m.Ceil(c.x, c.y) + RoofSlab, radius = Rand(rnd, 1.8f, 2.3f), body = Rand(rnd, 10f, 14f);
            const int sides = 12;
            float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.06f;
            for (int k = 0; k < sides; k++)
            {
                float a = (k + 0.5f) * 360f / sides;
                kit.TurnedRound(p + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 0.3f) + Vector3.up * (baseY + body * 0.5f), new Vector3(chord, body, 0.6f), a, p, false);
            }
            var roof = GameArt.SpawnModel("ToitKremlin", root, 1f);
            if (roof == null) continue;
            Bounds rb = GameArt.LocalBounds(roof.transform);
            float scale = (radius * 2f + 0.1f) / Mathf.Max(0.01f, Mathf.Max(rb.size.x, rb.size.z));
            float squash = Mathf.Min(1f, 7f / Mathf.Max(0.01f, rb.size.y * scale));
            roof.transform.localScale = new Vector3(scale, scale * squash, scale);
            roof.transform.position = p + Vector3.up * (baseY + body);
            // Lumineux comme les remparts : on les voit depuis les ponts, au-dessus du rempart, malgré l'obscurité.
            foreach (var r in roof.GetComponentsInChildren<Renderer>()) r.sharedMaterial = LuminousCopy(r.sharedMaterial);
        }
        kit.Flush(RampartBrick);
    }
}
