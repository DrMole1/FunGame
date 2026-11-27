using System.Collections.Generic;
using UnityEngine;

// V0.7.19 (11.4) : aménagement du Blockhaus.
// - 3 à 5 appartements uniques par structure suivent les 8 schémas du GDD (orientés au hasard, deux portes ; une case noire est un
//   pan de mur) ; les autres pièces sont meublées au hasard selon les règles des objets décoratifs ;
// - nouveaux objets (Meshy) : tapis (4 modèles), banc (4 par structure, dans les couloirs), chaise et table (propres au Blockhaus),
//   commode (2 au plus par pièce ; deux Télévisions ramassables posées sur deux commodes de la structure), luminaire (2 au plus par
//   pièce, lumière étouffée par l'abat-jour), machine à coudre (sur une table 25 %, une commode 15 %, un vieux mobilier 10 %), vieux
//   canapé (1 au plus par pièce) ;
// - dans une pièce ou un couloir de moins de 7 m de côté, tout objet touche un mur ;
// - les pommeaux de douche des baignoires s'ouvrent et se ferment d'un clic (bulles de savon : BlockhausProps).
// V0.7.20 (Note 11) : moquette en damier dans toutes les pièces, les tapis posés dessus ; plus de fuites d'eau ni de flaques ; plinthes
// au pied des pans de mur des schémas ; commodes et tables deux fois plus volumineuses ; machine à coudre posée sur le plateau du vieux
// mobilier (sous sa crête) ; Tapis2 retexturé (motif noir et blanc d'un tapis de référence du GDD), Tapis1 et Tapis3 ramenés au
// format des tapis de référence (2 x 3) au lieu d'être carrés.
public static partial class WorldBuilder
{
    const float BlockNarrowSide = 7f, RugChance = 0.5f;
    const int MinBlockTemplates = 3, MaxBlockTemplates = 5, BlockTemplateDoors = 2, BenchesPerBlockhaus = 4, TvsPerBlockhaus = 2;
    // Dessus du vieux mobilier (Armoire) : son plateau est à 88,6 % de sa hauteur (une crête dépasse au fond).
    const float WardrobeTopFraction = 0.886f;
    const float SewingOnTable = 0.25f, SewingOnCommode = 0.15f, SewingOnWardrobe = 0.1f;

    // Schémas du GDD (11.4), la ligne 0 au "nord". Symboles : Co Commode, VC Vieux canapé, L Luminaire, C Chaise, Ta Table, VU Vieil
    // urinoir, TE Tableau électrique, VB Vieille baignoire, VL Vieux lavabo, VM Vieux mobilier ; W : pan de mur (case noire).
    static readonly string[][] BlockTemplates =
    {
        new[]   // Template 1 (9 x 7)
        {
            "Co . . . . . . VC Co",
            "L . . . C . . . .",
            "W . . C . C . . W",
            ". . C . Ta . C . .",
            "W . . C . C . . W",
            "C . . . C . . . L",
            "Co . . . . . . . Co",
        },
        new[]   // Template 2 (8 x 8)
        {
            "L . . . . Co . .",
            "VU . . . . . . .",
            "W . . Ta C . . TE",
            "VU . . C . . . VB",
            "W . . . . . . VL",
            "VU . . . . . . .",
            "W . . . . . C .",
            "VU . . . . C Ta .",
        },
        new[]   // Template 3 (9 x 9)
        {
            "VB Co W VL . . . . VB",
            ". . W VL . . . . Co",
            ". . W . . W W W W",
            ". . W . . . . VL VL",
            ". . . . Ta . . . .",
            "VL VL . . . . W . .",
            "W W W W . . W . .",
            "Co . . . . VL W . .",
            "VB . . . . VL W Co VB",
        },
        new[]   // Template 4 (9 x 9)
        {
            "W VU L VU . VU . . .",
            "VL . . . . . . . .",
            "W W W W W W W . .",
            ". . TE VB . . . . Co",
            ". . . . . . . C Ta",
            ". VM . . Co . . . Ta",
            "W W W W W W W . .",
            "VL . . . . . . . .",
            "W VU . VU L VU . . .",
        },
        new[]   // Template 5 (7 x 13)
        {
            ". VM . . TE VB .",
            ". . . . . . .",
            "W W . . . W W",
            "VU . . . . . VU",
            "W . . . . . W",
            "VU . VL W VL . VU",
            "W . Ta W L . W",
            "VU . VL W VL . VU",
            "W . . . . . W",
            "VU . . . . . VU",
            "W W . . . W W",
            ". L . . . . .",
            ". . . . Co . .",
        },
        new[]   // Template 6 (7 x 12)
        {
            "W VU W VU W L VB",
            "VU . . . W . .",
            "W . . . . . .",
            "VU . . . . VL VL",
            "W W . . . W W",
            ". . . . . C .",
            ". L . . . . .",
            "W W . . . W W",
            "Ta VL . . . Co TE",
            ". . . . . . .",
            ". . . . . . .",
            "VU W VU W VU W VU",
        },
        new[]   // Template 7 (7 x 9)
        {
            ". . . . . . .",
            ". . VL TE VL . Co",
            ". . W W W . VM",
            ". . C . C . .",
            ". . . VB . . .",
            "Co . C . C . .",
            ". . W W W . .",
            ". . VL L VL . .",
            "VM . . . . . .",
        },
        new[]   // Template 8 (7 x 7)
        {
            ". . C Ta L . .",
            ". . . . . . .",
            "W . VU W VU . W",
            "VU . VL W VL . VU",
            "W . VL W VL . W",
            "VU . VL W VL . VU",
            "W . VU W VU . W",
        },
    };
    // Découpage en cellules de 5 m (cases d'environ 1,9 à 2,5 m).
    static readonly int[][] BlockTemplateCols = { new[] { 9, 4 }, new[] { 8, 3 }, new[] { 9, 4 }, new[] { 9, 4 }, new[] { 7, 3 }, new[] { 7, 3 }, new[] { 7, 3 }, new[] { 7, 3 } };
    static readonly int[][] BlockTemplateRows = { new[] { 7, 3 }, new[] { 8, 3 }, new[] { 9, 4 }, new[] { 9, 4 }, new[] { 13, 6 }, new[] { 12, 5 }, new[] { 9, 4 }, new[] { 7, 3 } };

    static TemplateSet blockSet;
    static TemplateSet BlockSet => blockSet ??= new TemplateSet { templates = BlockTemplates, cols = BlockTemplateCols, rows = BlockTemplateRows };

    // ---------- Plan : les schémas tirés pour la structure, répartis entre les étages ----------

    internal class BlockTemplatePlan
    {
        public readonly List<int> pending = new List<int>();
        public int[] quotas;
    }

    static BlockTemplatePlan PickBlockTemplates(System.Random rnd)
    {
        var order = new List<int>();
        for (int k = 0; k < BlockTemplates.Length; k++) order.Add(k);
        Shuffle(order, rnd);
        int count = MinBlockTemplates + rnd.Next(MaxBlockTemplates - MinBlockTemplates + 1);
        var plan = new BlockTemplatePlan { quotas = new int[StoreyLevels.Length] };
        plan.pending.AddRange(order.GetRange(0, count));
        // Les plus grands d'abord (ils se casent moins bien).
        plan.pending.Sort((a, b) => (SegCells(BlockTemplateCols[b]) * SegCells(BlockTemplateRows[b])).CompareTo(SegCells(BlockTemplateCols[a]) * SegCells(BlockTemplateRows[a])));
        for (int k = 0; k < count; k++) plan.quotas[rnd.Next(plan.quotas.Length)]++;
        return plan;
    }

    // Avant la génération d'un étage : les schémas qui lui reviennent (le dernier étage reprend ceux qui n'ont pas trouvé de place).
    static void PrepareBlockTemplates(ZoneStyle style, BlockTemplatePlan tp, int storey)
    {
        style.bigPlaced.Clear();
        int quota = storey == StoreyLevels.Length - 1 ? tp.pending.Count : Mathf.Min(tp.quotas[storey], tp.pending.Count);
        style.bigShapes = tp.pending.ConvertAll(t => TemplateMask(t, BlockSet));
        style.bigGroups = tp.pending.ToArray();
        style.bigCount = quota;
    }

    // Après la génération : les salles posées sur schéma (deux portes chacune).
    static void TakeBlockTemplates(Maze m, ZoneStyle style, BlockTemplatePlan tp, BlockhausPlan plan, int storey, System.Random rnd)
    {
        var placed = new List<int>();
        foreach (var (room, shape, turns) in style.bigPlaced)
        {
            if (room.cells.Count == 0) continue;
            int t = tp.pending[shape];
            placed.Add(t);
            plan.templated[room] = (t, turns);
            LimitDoors(m, room, BlockTemplateDoors, rnd);
        }
        foreach (int t in placed) tp.pending.Remove(t);
        int left = 0;
        for (int s = storey + 1; s < tp.quotas.Length; s++) left += tp.quotas[s];
        // Ce qui n'a pas trouvé sa place passe aux étages suivants.
        if (storey + 1 < tp.quotas.Length) tp.quotas[storey + 1] += Mathf.Max(0, tp.pending.Count - left);
        style.bigShapes = null;
        style.bigPlaced.Clear();
    }

    // ---------- Objets ----------

    // Taille demandée au modèle (sa plus grande dimension, GameArt.SpawnModelFloor) des nouveaux objets.
    static float BlockPropSize(string model)
    {
        switch (model)
        {
            case "Commode": return 1.4f * VolumeX2;          // V0.7.20 (Note 11) : volume doublé
            case "Canape": return 1.6f;
            case "Luminaire": return 1.7f;
            case "TableBlockhaus": return 1.05f * VolumeX2;  // V0.7.20 (Note 11) : volume doublé
            case "ChaiseBlockhaus": return 1f;
            case "MachineCoudre": return 0.5f;
            case "BancBlockhaus": return 1.7f;
            default: return BlockhausModelHeight(model);
        }
    }

    // Contexte d'un étage pendant son ameublement.
    internal class StoreyFurnish
    {
        public Maze m;
        public BlockhausPlan plan;
        public float y;
        public Transform props;
        public Kit finish;
        public ZoneRefs refs;
        public List<Vector3> placed;
        public readonly List<Bounds> taken = new List<Bounds>();
        public MeshBuilder carpet = new MeshBuilder();
        public int storey;
    }

    static bool FreeSpace(StoreyFurnish f, Bounds b)
    {
        b.Expand(new Vector3(-0.05f, -0.05f, -0.05f));
        foreach (var t in f.taken) if (t.Intersects(b)) return false;
        return true;
    }

    static GameObject SpawnBlockProp(StoreyFurnish f, string model) => GameArt.SpawnModelFloor(model, f.props, BlockPropSize(model));

    // Après la pose d'un objet : collision, place prise, et ce qu'il entraîne (lumière, machine à coudre, Télévision possible).
    static void SettleBlockProp(StoreyFurnish f, GameObject go, string model, System.Random rnd)
    {
        if (model == "Baignoire") BathCollider(go);
        else AddPropCollider(go);
        Physics.SyncTransforms();
        f.taken.Add(WorldBounds(go.transform));
        if (model == "Luminaire")
        {
            // Lumière d'un néon, étouffée par l'abat-jour : faible, chaude, sous l'abat-jour.
            float h = GameArt.LocalBounds(go.transform).size.y;
            AddLight(go.transform, Vector3.up * (h * 0.8f), new Color(1f, 0.86f, 0.66f), 0.55f, 4.5f);
        }
        float sewing = model == "TableBlockhaus" ? SewingOnTable : model == "Commode" ? SewingOnCommode : model == "Armoire" ? SewingOnWardrobe : 0f;
        bool machine = sewing > 0f && rnd.NextDouble() < sewing && PutOnTop(f, go, "MachineCoudre", rnd);
        if (model == "Commode" && !machine) f.plan.commodes.Add(go);
    }

    // Petit objet posé sur le dessus d'un meuble, tourné comme lui.
    static bool PutOnTop(StoreyFurnish f, GameObject under, string model, System.Random rnd)
    {
        var go = SpawnBlockProp(f, model);
        if (go == null) return false;
        Bounds b = WorldBounds(under.transform);
        // V0.7.20 (Note 11) : sur le plateau du vieux mobilier, pas sur sa crête (la machine flottait).
        float topY = under.name.Contains("Armoire") ? b.min.y + b.size.y * WardrobeTopFraction : b.max.y;
        go.transform.SetPositionAndRotation(new Vector3(b.center.x, topY, b.center.z), under.transform.rotation * Quaternion.Euler(0f, Rand(rnd, -20f, 20f), 0f));
        AddPropCollider(go);
        return true;
    }

    // Objet posé au sol d'une pièce, loin des murs (ou contre un mur si la pièce est étroite).
    static bool PlaceBlockFloor(StoreyFurnish f, Room room, GameObject go, System.Random rnd, bool narrow, float gap = 0.1f)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var cell = room.cells[rnd.Next(room.cells.Count)];
            if (narrow)
            {
                var row = PlaceRowAgainstWall(f.m, go, cell, rnd.Next(4), 0f, PlinthRelief + gap, f.placed, rnd, f.y, 1, null);
                if (row == null) continue;
                if (FitInRoom(f.m, room, go) && FreeSpace(f, WorldBounds(go.transform))) return true;
                f.placed.RemoveAt(f.placed.Count - 1);
                continue;
            }
            Vector3 c = f.m.Center(cell.x, cell.y) + new Vector3(Rand(rnd, -1.2f, 1.2f), f.y, Rand(rnd, -1.2f, 1.2f));
            go.transform.SetPositionAndRotation(c, Quaternion.Euler(0f, rnd.Next(4) * 90f + Rand(rnd, -15f, 15f), 0f));
            Physics.SyncTransforms();
            if (FitInRoom(f.m, room, go) && FreeSpace(f, WorldBounds(go.transform)) && !NearOpening(f.m, room, cell, c)) return true;
        }
        return false;
    }

    // Table et ses chaises (2 à 4) autour d'elle, tournées vers elle.
    static void PlaceTableSet(StoreyFurnish f, Room room, System.Random rnd, bool narrow)
    {
        var table = SpawnBlockProp(f, "TableBlockhaus");
        if (table == null) return;
        if (!PlaceBlockFloor(f, room, table, rnd, narrow)) { Object.Destroy(table); return; }
        SettleBlockProp(f, table, "TableBlockhaus", rnd);
        Bounds tb = WorldBounds(table.transform);
        float reach = Mathf.Max(tb.extents.x, tb.extents.z) + 0.45f;
        int chairs = 2 + rnd.Next(3);
        float start = Rand(rnd, 0f, 360f);
        for (int k = 0; k < chairs; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, start + k * 360f / chairs, 0f) * Vector3.forward;
            var chair = SpawnBlockProp(f, "ChaiseBlockhaus");
            if (chair == null) return;
            chair.transform.SetPositionAndRotation(new Vector3(tb.center.x, f.y, tb.center.z) + dir * reach, Quaternion.LookRotation(-dir));
            Physics.SyncTransforms();
            if (!FitInRoom(f.m, room, chair) || !FreeSpace(f, WorldBounds(chair.transform))) { Object.Destroy(chair); continue; }
            SettleBlockProp(f, chair, "ChaiseBlockhaus", rnd);
        }
    }

    // Moquette en damier sur tout le sol de la pièce (jusqu'aux murs).
    static void LayCarpet(StoreyFurnish f, Room room)
    {
        foreach (var cell in room.cells) LayCarpetCell(f, cell);
    }

    // V0.7.21 (Note 13) : la moquette aussi dans les couloirs (cellule par cellule, jusqu'aux murs).
    static void LayCarpetCell(StoreyFurnish f, Vector2Int cell)
    {
        {
            Vector3 c = f.m.Center(cell.x, cell.y);
            float e0 = HasWall(f.m, cell.x, cell.y, 1) ? Cell * 0.5f - Wall * 0.5f : Cell * 0.5f, e1 = HasWall(f.m, cell.x, cell.y, 0) ? Cell * 0.5f - Wall * 0.5f : Cell * 0.5f;
            float e3 = HasWall(f.m, cell.x, cell.y, 3) ? Cell * 0.5f - Wall * 0.5f : Cell * 0.5f, e2 = HasWall(f.m, cell.x, cell.y, 2) ? Cell * 0.5f - Wall * 0.5f : Cell * 0.5f;
            // V0.7.20 : 2 cm au-dessus du sol (à 1,2 cm, elle scintillait avec les bandes de sol relevées de 1 cm).
            float y = f.y + 0.02f;
            f.carpet.WorldQuad(new Vector3(c.x - e0, y, c.z - e3), new Vector3(c.x + e1, y, c.z - e3), new Vector3(c.x + e1, y, c.z + e2), new Vector3(c.x - e0, y, c.z + e2), Vector3.up, 0.25f);
        }
    }

    // Tapis (un des 4 modèles) couché au sol, au milieu de la pièce ; d'autres objets peuvent être posés dessus. V0.7.20 : posé sur la
    // moquette.
    static void LayRug(StoreyFurnish f, Room room, System.Random rnd)
    {
        int kind = 1 + rnd.Next(4);
        var go = GameArt.SpawnModelFloor("Tapis" + kind, f.props, Rand(rnd, 2.4f, 3.2f));
        if (go == null) return;
        LayFlat(go);
        RugFaceUp(go);
        if (kind == 2) RetextureRug2(go);
        // Tapis carrés : ramenés au format 2 x 3 des tapis de référence (ils paraissaient élargis).
        if (kind == 1 || kind == 3) go.transform.localScale = new Vector3(2f / 3f, 1f, 1f);
        Vector3 c = RoomFloorCenter(f.m, room, f.y);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            go.transform.SetPositionAndRotation(c + Vector3.up * 0.03f, Quaternion.Euler(0f, rnd.Next(4) * 90f, 0f));
            Physics.SyncTransforms();
            if (FitInRoom(f.m, room, go)) { f.plan.rugs.Add(WorldBounds(go.transform)); return; }
            go.transform.localScale *= 0.8f;
        }
        Object.Destroy(go);
    }

    // V0.7.20 (Note 11) : Tapis2 (texture Meshy presque toute blanche) : même maillage, coordonnées de texture projetées du dessus
    // (Resources/Art/Decor/Tapis2Plat), motif noir et blanc laineux (Resources/Art/Decor/Tapis2Motif).
    static Material rug2Material;

    static void RetextureRug2(GameObject holder)
    {
        Mesh flat = Resources.Load<Mesh>("Art/Decor/Tapis2Plat");
        var motif = Resources.Load<Texture2D>("Art/Decor/Tapis2Motif");
        if (flat == null || motif == null) return;
        if (rug2Material == null) rug2Material = GameArt.Lit("Tapis2Motif", Color.white, motif, 0f);
        foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
        {
            mf.sharedMesh = flat;
            var r = mf.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = rug2Material;
        }
    }

    // Certains tapis Meshy ont leur face dessinée vers le bas : on les retourne (moyenne des normales vers le haut).
    static void RugFaceUp(GameObject holder)
    {
        Vector3 sum = Vector3.zero;
        foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            foreach (var n in mf.sharedMesh.normals) sum += mf.transform.TransformDirection(n);
        }
        if (sum.y >= 0f || holder.transform.childCount == 0) return;
        Transform model = holder.transform.GetChild(0);
        model.localRotation = Quaternion.Euler(180f, 0f, 0f) * model.localRotation;
        model.localPosition = Quaternion.Euler(180f, 0f, 0f) * model.localPosition;
        Bounds b = GameArt.LocalBounds(holder.transform);
        model.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
    }

    // Pièce meublée au hasard selon les règles des objets décoratifs (V0.7.19) ; 'BlockhausFurniture' ajoute les nouveaux objets.
    static void FurnishApartment(StoreyFurnish f, Room room, System.Random rnd)
    {
        bool narrow = NarrowRoom(room, BlockNarrowSide);
        int count = 1 + rnd.Next(room.cells.Count + 1);
        int commodes = 0, lamps = 0, sofas = 0;
        bool table = false;
        for (int k = 0; k < count; k++)
        {
            string model = BlockhausFurniture[rnd.Next(BlockhausFurniture.Length)];
            if (model == "Commode" && commodes >= 2 || model == "Luminaire" && lamps >= 2 || model == "Canape" && sofas >= 1 || model == "TableBlockhaus" && table) continue;
            if (model == "TableBlockhaus")
            {
                table = true;
                PlaceTableSet(f, room, rnd, narrow);
                continue;
            }
            var cell = room.cells[rnd.Next(room.cells.Count)];
            int d = rnd.Next(4);
            var go = SpawnBlockProp(f, model);
            if (go == null) continue;
            float mount = BlockhausMountHeight(model);
            float bottom = mount > 0f ? mount - GameArt.LocalBounds(go.transform).size.y * 0.5f : 0f;
            // Au-dessus des plinthes, ou devant elles (rien n'entre dans le mur).
            float gap = bottom < PlinthHeight + 0.02f ? PlinthRelief + 0.04f : 0.02f;
            if (model == "Baignoire") gap = Mathf.Max(gap, 0.25f);
            if (model == "Commode" || model == "Canape" || model == "Luminaire") gap = PlinthRelief + 0.1f;
            var row = PlaceRowAgainstWall(f.m, go, cell, d, bottom, gap, f.placed, rnd, f.y, model == "Urinoir" ? UrinalRow(rnd) : 1, null);
            // V0.7.14 (Note 06) : une baignoire ne rentre dans aucun mur (ni dans les cellules voisines hors de la pièce).
            if (row != null && (model == "Baignoire" || bottom <= 0.01f) && (!FitInRoom(f.m, room, go) || !FreeSpace(f, WorldBounds(go.transform))))
            {
                f.placed.RemoveAt(f.placed.Count - 1);
                row = null;
            }
            if (row == null)
            {
                Object.Destroy(go);
                continue;
            }
            foreach (var obj in row) SettleBlockProp(f, obj, model, rnd);
            if (model == "Commode") commodes++;
            else if (model == "Luminaire") lamps++;
            else if (model == "Canape") sofas++;
            if (model == "Baignoire") AddBathShower(f, go, cell, d, rnd);
        }
    }

    // V0.7.13 : une baignoire contre un mur a son pommeau de douche sur ce mur, au-dessus d'un de ses bouts. V0.7.19 : il s'ouvre d'un
    // clic.
    static void AddBathShower(StoreyFurnish f, GameObject bath, Vector2Int cell, int d, System.Random rnd)
    {
        var shower = GameArt.SpawnModelFloor("Douche", f.props, BlockhausModelHeight("Douche"));
        if (shower == null) return;
        Vector3 c = f.m.Center(cell.x, cell.y), side = new Vector3(DJ[d], 0f, DI[d]);
        Bounds wb = WorldBounds(bath.transform);
        float end = Vector3.Dot(wb.center - c, side) + (rnd.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Abs(Vector3.Dot(wb.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z)))) * 0.6f;
        float showerBottom = BlockhausMountHeight("Douche") + 0.3f - GameArt.LocalBounds(shower.transform).size.y * 0.5f;
        var put = PlaceRowAgainstWall(f.m, shower, cell, d, showerBottom, 0.02f, f.placed, rnd, f.y, 1, end);
        if (put == null) { Object.Destroy(shower); return; }
        MakeShowerInteractive(f, shower, -new Vector3(DI[d], 0f, DJ[d]));
    }

    static void MakeShowerInteractive(StoreyFurnish f, GameObject shower, Vector3 outward)
    {
        Physics.SyncTransforms();
        int id = f.m.zone * 1000 + f.refs.showers.Count;
        f.refs.showers.Add(ShowerHead.Attach(shower, id, outward, f.y));
    }

    // ---------- Salle sur schéma ----------

    static string BlockTemplateModel(string sym)
    {
        switch (sym)
        {
            case "Co": return "Commode";
            case "VC": return "Canape";
            case "L": return "Luminaire";
            case "C": return "ChaiseBlockhaus";
            case "Ta": return "TableBlockhaus";
            case "VU": return "Urinoir";
            case "VB": return "Baignoire";
            case "VL": return "Lavabo";
            case "VM": return "Armoire";
            default: return null;
        }
    }

    // Comme FurnishFromTemplate (Château) : chaque case fait environ 2 m ; un objet du bord (ou voisin d'un pan de mur) y est plaqué,
    // le dos au mur ; rien devant une porte ni une fenêtre.
    static void FurnishBlockTemplate(StoreyFurnish f, Room room, int layout, int turns, System.Random rnd)
    {
        Maze m = f.m;
        var set = BlockSet;
        int[] cols = set.cols[layout], rows = set.rows[layout];
        int W = SegSquares(cols), H = SegSquares(rows), CW = SegCells(cols), CH = SegCells(rows);
        string[,] grid = ParseTemplate(layout, out bool[,] inside, set);
        bool InRoom(int c, int r) => c >= 0 && r >= 0 && c < W && r < H && inside[c, r];
        bool Open(int c, int r) => InRoom(c, r) && grid[c, r] != "W";
        string At(int c, int r) => InRoom(c, r) ? grid[c, r] : null;
        int Run(int c, int r, int dc, int dr)
        {
            int k = 0;
            while (Open(c + dc * (k + 1), r + dr * (k + 1))) k++;
            return k;
        }
        float left = m.Center(room.rect.xMin, room.rect.yMin).x - Cell * 0.5f;
        float top = m.Center(room.rect.xMin, room.rect.yMax - 1).z + Cell * 0.5f;
        Vector3 ToWorld(float u, float v)
        {
            float w = CW, h = CH;
            for (int k = 0; k < turns; k++) { float t = u; u = h - v; v = t; float s = w; w = h; h = s; }
            return new Vector3(left + u * Cell, f.y, top - v * Cell);
        }
        Vector3 TurnDir(int dc, int dr) { for (int k = 0; k < turns; k++) { int t = dc; dc = -dr; dr = t; } return new Vector3(dc, 0f, -dr); }

        // Pans de mur d'abord (les objets se plaquent contre eux).
        var wallRects = new List<Rect>();
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                if (At(c, r) != "W") continue;
                Vector3 a = ToWorld(SegPos(cols, c), SegPos(rows, r)), b = ToWorld(SegPos(cols, c + 1f), SegPos(rows, r + 1f));
                Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
                var size = new Vector3(hi.x - lo.x, room.ceil, hi.z - lo.z);
                var center = new Vector3((lo.x + hi.x) * 0.5f, f.y + room.ceil * 0.5f, (lo.z + hi.z) * 0.5f);
                // V0.7.20 : sans ses faces du dessus et du dessous (cachées, mais au niveau des sols : elles scintillaient avec eux).
                f.finish.walls.Box(center, size, 0.5f, 63 & ~((1 << 2) | (1 << 3)));
                f.finish.Collider(center, size);
                f.taken.Add(new Bounds(center, size));
                wallRects.Add(Rect.MinMaxRect(lo.x, lo.z, hi.x, hi.z));
            }
        // V0.7.20 (Note 11) : plinthes au pied des pans de mur, sur chaque face qui donne sur la pièce (pas contre un autre pan ni
        // contre le mur de la pièce) ; leurs bouts se rejoignent aux angles.
        foreach (Rect wr in wallRects)
            for (int d = 0; d < 4; d++)
            {
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]);
                bool alongZ = DI[d] != 0;
                float len = alongZ ? wr.height : wr.width;
                Vector3 mid = new Vector3(wr.center.x + DI[d] * wr.width * 0.5f, f.y, wr.center.y + DJ[d] * wr.height * 0.5f);
                Vector3 probe = mid + n * 0.3f;
                if (wallRects.Exists(o => o != wr && o.Contains(new Vector2(probe.x, probe.z)))) continue;
                var pc = new Vector2Int(Mathf.FloorToInt((probe.x - m.origin.x) / Cell), Mathf.FloorToInt((probe.z - m.origin.z) / Cell));
                if (!m.InGrid(pc.x, pc.y) || m.RoomAt(pc.x, pc.y) != room) continue;
                Vector3 at = mid + n * (PlinthRelief * 0.5f) + Vector3.up * (PlinthHeight * 0.5f);
                float full = len + PlinthRelief * 2f;
                f.finish.grey.Box(at, alongZ ? new Vector3(PlinthRelief, PlinthHeight, full) : new Vector3(full, PlinthHeight, PlinthRelief), 1f);
            }

        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                string sym = At(c, r);
                if (sym == null || sym == "W") continue;
                int dTop = Run(c, r, 0, -1), dBottom = Run(c, r, 0, 1), dLeft = Run(c, r, -1, 0), dRight = Run(c, r, 1, 0);
                int edge = Mathf.Min(Mathf.Min(dTop, dBottom), Mathf.Min(dLeft, dRight));
                int fc, fr;
                if (edge == dTop) { fc = 0; fr = 1; }
                else if (edge == dBottom) { fc = 0; fr = -1; }
                else if (edge == dLeft) { fc = 1; fr = 0; }
                else { fc = -1; fr = 0; }
                bool against = edge == 0;
                // Derrière l'objet : un pan de mur du schéma, ou le bord de la salle.
                bool blockBehind = against && At(c - fc, r - fr) == "W";
                Vector3 pos = ToWorld(SegPos(cols, c + 0.5f), SegPos(rows, r + 0.5f));
                Vector3 facing = TurnDir(fc, fr), back = -facing;
                bool backX = Mathf.Abs(back.x) > 0.5f;
                var cell = new Vector2Int(Mathf.FloorToInt((pos.x - m.origin.x) / Cell), Mathf.FloorToInt((pos.z - m.origin.z) / Cell));
                Vector3 cc = m.Center(cell.x, cell.y);
                float su = SegSize(cols, c), sv = SegSize(rows, r);
                float sBack = backX ? ((turns & 1) == 0 ? su : sv) : ((turns & 1) == 0 ? sv : su);
                float plane = blockBehind
                    ? (backX ? pos.x + Mathf.Sign(back.x) * sBack * 0.5f : pos.z + Mathf.Sign(back.z) * sBack * 0.5f)
                    : (backX ? cc.x + Mathf.Sign(back.x) * (Cell * 0.5f - Wall * 0.5f) : cc.z + Mathf.Sign(back.z) * (Cell * 0.5f - Wall * 0.5f));
                if (m.RoomAt(cell.x, cell.y) != room || NearOpening(m, room, cell, pos)) continue;
                if (against && !blockBehind)
                {
                    int dd = backX ? (back.x > 0f ? 0 : 1) : (back.z > 0f ? 2 : 3);
                    if (!HasWall(m, cell.x, cell.y, dd) || m.HasWallHole(cell.x, cell.y, dd)) continue;
                }
                if (sym == "TE")
                {
                    Vector3 wp = backX ? new Vector3(plane, pos.y, pos.z) : new Vector3(pos.x, pos.y, plane);
                    WallBox(f.props, "Decor/Ventilation", wp + Vector3.up * 1.6f, facing, 0.9f);
                    continue;
                }
                string model = BlockTemplateModel(sym);
                if (model == null) continue;
                var go = SpawnBlockProp(f, model);
                if (go == null) continue;
                if (sym == "C")
                {
                    // Chaise tournée vers la table la plus proche du schéma (dans la direction dominante).
                    int bestD = int.MaxValue;
                    for (int r2 = 0; r2 < H; r2++)
                        for (int c2 = 0; c2 < W; c2++)
                        {
                            if (At(c2, r2) != "Ta") continue;
                            int dc = c2 - c, dr = r2 - r, dist = dc * dc + dr * dr;
                            if (dist >= bestD) continue;
                            bestD = dist;
                            Vector3 toward = ToWorld(SegPos(cols, c2 + 0.5f), SegPos(rows, r2 + 0.5f)) - pos;
                            toward.y = 0f;
                            if (toward.sqrMagnitude > 0.01f) facing = toward.normalized;
                        }
                }
                float mount = BlockhausMountHeight(model);
                float bottom = mount > 0f && against ? mount - GameArt.LocalBounds(go.transform).size.y * 0.5f : 0f;
                go.transform.SetPositionAndRotation(pos + Vector3.up * bottom, Quaternion.LookRotation(facing));
                if (against)
                {
                    float gap = bottom < PlinthHeight + 0.02f && !blockBehind ? PlinthRelief + 0.04f : 0.02f;
                    if (model == "Baignoire") gap = Mathf.Max(gap, 0.15f);
                    Bounds wb = WorldBounds(go.transform);
                    float reach = Vector3.Dot(wb.center, back) + Mathf.Abs(back.x) * wb.extents.x + Mathf.Abs(back.z) * wb.extents.z;
                    float target = backX ? plane * back.x : plane * back.z;
                    go.transform.position += back * (target - gap - reach);
                }
                SettleBlockProp(f, go, model, rnd);
                // Pommeau de douche au-dessus de la baignoire plaquée contre un mur.
                if (model == "Baignoire" && against)
                {
                    var shower = GameArt.SpawnModelFloor("Douche", f.props, BlockhausModelHeight("Douche"));
                    if (shower == null) continue;
                    Bounds bb = WorldBounds(go.transform);
                    float sh = GameArt.LocalBounds(shower.transform).size.y;
                    Vector3 along = new Vector3(Mathf.Abs(back.z), 0f, Mathf.Abs(back.x));
                    float off = Mathf.Abs(Vector3.Dot(bb.extents, along)) * 0.6f * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                    Vector3 sp = new Vector3(bb.center.x, f.y + BlockhausMountHeight("Douche") + 0.3f - sh * 0.5f, bb.center.z) + along * off;
                    shower.transform.SetPositionAndRotation(sp, Quaternion.LookRotation(facing));
                    Bounds swb = WorldBounds(shower.transform);
                    float sReach = Vector3.Dot(swb.center, back) + Mathf.Abs(back.x) * swb.extents.x + Mathf.Abs(back.z) * swb.extents.z;
                    shower.transform.position += back * ((backX ? plane * back.x : plane * back.z) - 0.02f - sReach);
                    MakeShowerInteractive(f, shower, facing);
                }
            }
    }

    // ---------- Couloirs : bancs ----------

    static void PlaceBenches(StoreyFurnish f, List<Vector2Int> corridorCells, HashSet<(int, int, int)> usedFaces, int wanted, System.Random rnd)
    {
        Shuffle(corridorCells, rnd);
        foreach (var cell in corridorCells)
        {
            if (wanted <= 0) return;
            int d = rnd.Next(4);
            if (!HasWall(f.m, cell.x, cell.y, d) || f.m.HasWallHole(cell.x, cell.y, d) || usedFaces.Contains((cell.x, cell.y, d))) continue;
            var go = SpawnBlockProp(f, "BancBlockhaus");
            if (go == null) return;
            var row = PlaceRowAgainstWall(f.m, go, cell, d, 0f, PlinthRelief + 0.08f, f.placed, rnd, f.y, 1, null);
            if (row == null || !FreeSpace(f, WorldBounds(go.transform))) { if (row != null) f.placed.RemoveAt(f.placed.Count - 1); Object.Destroy(go); continue; }
            usedFaces.Add((cell.x, cell.y, d));
            SettleBlockProp(f, go, "BancBlockhaus", rnd);
            wanted--;
        }
    }

    // ---------- Après tous les étages : Télévisions ----------

    static void FinishBlockhausDecor(BlockhausPlan plan, ZoneRefs refs, Transform props, System.Random rnd)
    {
        // Deux Télévisions sur deux commodes (sans machine à coudre) de la structure.
        var commodes = new List<GameObject>(plan.commodes);
        Shuffle(commodes, rnd);
        foreach (var co in commodes)
        {
            if (refs.tvSpots.Count >= TvsPerBlockhaus || co == null) continue;
            Bounds b = WorldBounds(co.transform);
            refs.tvSpots.Add((new Vector3(b.center.x, b.max.y + 0.005f, b.center.z), co.transform.eulerAngles.y));
        }

        // V0.7.20 (Note 11) : plus de fuites d'eau ni de flaques.
    }

    // ---------- Moquette (Note 13) : damier noir et blanc, texture laineuse ----------

    static Material carpetMaterial;
    static Material CarpetMaterial
    {
        get
        {
            if (carpetMaterial != null) return carpetMaterial;
            const int size = 256, checks = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { name = "MoquetteDamier", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var rnd = new System.Random(1913);
            var px = new Color32[size * size];
            // Fibres : bruit fin, quelques poils plus clairs ou plus sombres, bords des cases un peu irréguliers.
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wob = Mathf.Sin(y * 0.9f) * 1.2f + Mathf.Sin(x * 0.7f) * 1.2f;
                    bool white = (((int)((x + wob) / (size / checks)) + (int)((y - wob) / (size / checks))) & 1) == 0;
                    float n = (float)rnd.NextDouble();
                    float fiber = (n - 0.5f) * 0.22f + (n > 0.97 ? 0.15f : n < 0.03 ? -0.15f : 0f);
                    float v = Mathf.Clamp01((white ? 0.82f : 0.1f) + fiber * (white ? 1f : 0.6f));
                    byte b = (byte)(v * 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            carpetMaterial = GameArt.Lit("MoquetteDamier", Color.white, tex, 0f);
            return carpetMaterial;
        }
    }
}
