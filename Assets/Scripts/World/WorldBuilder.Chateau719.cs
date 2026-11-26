using System.Collections.Generic;
using UnityEngine;

// V0.7.19 (11.3) : décor du Château.
// - Cimetières : les halls qui entourent les Tours et le Donjon sont remplis de croix et de pierres tombales (passages gardés libres
//   devant la porte de chaque Tour et aux ouvertures des halls) ;
// - deux cachots ont une cible humaine de plus, un Collier à Laisse (ramassable) autour du cou ;
// - dans les salles : une corniche de briques noires et blanches (Meshy) sous le plafond, le long des murs, et 5 % du plafond gris
//   couvert de carreaux de damier blancs ou noirs ; dans les couloirs : des arches de briques (Meshy) ;
// - tonneaux, cibles humaines, cibles d'archerie, croix, mannequins et pierres tombales : 15 % de chance d'être plus gros (+30 à +80 %
//   de volume) ; mannequins : 15 % de chance d'avoir une tête de chien (celles du décor de l'Échiquier) ;
// - menottes posées à plat (Note 09) ; derrière les remparts, sur les toits, des tours décoratives inaccessibles coiffées du toit de
//   Kremlin des anciennes Maisons-Fou (Note 08).
public static partial class WorldBuilder
{
    const float BigPropChance = 0.15f, DogHeadChance = 0.15f;
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
    static void AddMannequinDogHead(GameObject mannequin, System.Random rnd)
    {
        Bounds b = GameArt.LocalBounds(mannequin.transform);
        float size = Mathf.Max(0.45f, b.size.y * 0.3f);
        DogHeads.SpawnUpright(mannequin.transform, new Vector3(b.center.x, b.max.y - size * 0.8f, b.center.z), size, rnd);
    }

    // ---------- Après le mobilier : cimetières, colliers, corniches, plafonds, arches, tours de Kremlin ----------

    static void DressCastle(Maze m, Transform root, ChateauPlan plan, ZoneRefs refs, Transform props)
    {
        var rnd = new System.Random(plan.rnd.Next());
        BuildCemeteries(m, plan, props, rnd);
        AddCollarTargets(m, plan, props, refs, rnd);
        var cornices = new MeshBuilder();
        var tilesWhite = new MeshBuilder();
        var tilesBlack = new MeshBuilder();
        var rooms = new List<Room>(plan.natures.Keys);
        foreach (Room room in rooms)
        {
            if (room.cells.Count == 0) continue;
            RoomCornices(m, room, cornices);
            CeilingTiles(m, room, tilesWhite, tilesBlack, rnd);
        }
        CorridorArches(m, cornices, rnd);
        if (!cornices.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_Corniches", root, cornices.Build("Corniches"), GameArt.BrickWall);
        if (!tilesWhite.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_CarreauxBlancs", root, tilesWhite.Build("CarreauxBlancs"), GameArt.Lit("CarreauBlanc", new Color(0.9f, 0.9f, 0.9f), null, 0.3f));
        if (!tilesBlack.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_CarreauxNoirs", root, tilesBlack.Build("CarreauxNoirs"), GameArt.Lit("CarreauNoir", new Color(0.07f, 0.07f, 0.07f), null, 0.3f));
        KremlinTowers(m, plan, root, rnd);
    }

    // Cimetières : dans les halls autour des Tours et du Donjon, des croix et des pierres tombales, à l'écart de la porte des Tours,
    // des ouvertures des halls et les unes des autres.
    static void BuildCemeteries(Maze m, ChateauPlan plan, Transform props, System.Random rnd)
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
                Vector3 p = cc + new Vector3(Rand(rnd, -1.8f, 1.8f), 0f, Rand(rnd, -1.8f, 1.8f));
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
                Bounds b = WorldBounds(go.transform);
                // Au cou : sous la tête (un sac d'environ 0,4 m).
                refs.collarSpots.Add(new Vector3(b.center.x, b.max.y - 0.42f, b.center.z));
                done++;
                break;
            }
        }
    }

    // Corniche (modèle Meshy normalisé : x le long du mur, y vers le haut, z en profondeur), retournée sous le plafond, le long de
    // chaque mur plein de la salle.
    const float CorniceHeight = 0.85f, CorniceDepth = 0.55f;

    static void RoomCornices(Maze m, Room room, MeshBuilder mb)
    {
        Mesh mesh = Resources.Load<Mesh>("Art/Decor/Corniche");
        if (mesh == null) return;
        Vector3[] v = mesh.vertices;
        int[] t = mesh.triangles;
        foreach (var cell in room.cells)
            for (int d = 0; d < 4; d++)
            {
                int ni = cell.x + DI[d], nj = cell.y + DJ[d];
                if (m.InGrid(ni, nj) && m.RoomAt(ni, nj) == room) continue;
                if (!HasWall(m, cell.x, cell.y, d)) continue;
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]);
                Vector3 face = m.Center(cell.x, cell.y) + n * (Cell * 0.5f - Wall * 0.5f);
                Quaternion rot = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, 180f);
                Vector3 at = face - n * (CorniceDepth * 0.5f) + Vector3.up * room.ceil;
                mb.WorldMesh(v, t, Matrix4x4.TRS(at, rot, new Vector3(Cell - Wall, CorniceHeight, CorniceDepth)), 0.5f);
            }
    }

    // 5 % du plafond gris de la salle couvert de carreaux de damier (1,25 m), blancs ou noirs, juste sous lui.
    static void CeilingTiles(Maze m, Room room, MeshBuilder white, MeshBuilder black, System.Random rnd)
    {
        const int per = 4;
        float tile = Cell / per, y = room.ceil - 0.012f;
        foreach (var cell in room.cells)
        {
            Vector3 c = m.Center(cell.x, cell.y);
            for (int a = 0; a < per; a++)
                for (int b = 0; b < per; b++)
                {
                    if (rnd.NextDouble() >= 0.05) continue;
                    float x0 = c.x - Cell * 0.5f + a * tile, z0 = c.z - Cell * 0.5f + b * tile;
                    (rnd.NextDouble() < 0.5 ? white : black).WorldQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z0 + tile), new Vector3(x0 + tile, y, z0 + tile), new Vector3(x0 + tile, y, z0), Vector3.down, 0.5f);
                }
        }
    }

    // Arches de briques (Meshy, normalisée : x en largeur, y en hauteur, z en épaisseur) dans les couloirs du Château, d'un mur à
    // l'autre, au milieu de chaque cellule de couloir droit.
    static void CorridorArches(Maze m, MeshBuilder mb, System.Random rnd)
    {
        Mesh mesh = Resources.Load<Mesh>("Art/Decor/ArcheCouloir");
        if (mesh == null) return;
        Vector3[] v = mesh.vertices;
        int[] t = mesh.triangles;
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.InStructure(i, j) || m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j) || m.RoomAt(i, j) != null) continue;
                // Couloir traversant (ouvert aux deux bouts, un mur au moins sur le côté) : l'arche va d'un côté à l'autre.
                bool eastWest = (HasWall(m, i, j, 0) || HasWall(m, i, j, 1)) && !HasWall(m, i, j, 2) && !HasWall(m, i, j, 3);
                bool northSouth = (HasWall(m, i, j, 2) || HasWall(m, i, j, 3)) && !HasWall(m, i, j, 0) && !HasWall(m, i, j, 1);
                if (!eastWest && !northSouth) continue;
                Quaternion rot = eastWest ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                float ceil = m.Ceil(i, j);
                mb.WorldMesh(v, t, Matrix4x4.TRS(m.Center(i, j), rot, new Vector3(Cell - Wall, ceil, 0.55f)), 0.5f);
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
        foreach (var c in spots)
        {
            if (chosen.Count >= 10) break;
            Vector3 p = m.Center(c.x, c.y);
            if (chosen.Exists(q => Flat(q - p) < 22f)) continue;
            chosen.Add(p);
            float baseY = m.Ceil(c.x, c.y) + RoofSlab, radius = Rand(rnd, 1.8f, 2.3f), body = Rand(rnd, 10f, 14f);
            const int sides = 12;
            float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.06f;
            for (int k = 0; k < sides; k++)
            {
                float a = (k + 0.5f) * 360f / sides;
                kit.Turned(p + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 0.3f) + Vector3.up * (baseY + body * 0.5f), new Vector3(chord, body, 0.6f), a, false);
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
