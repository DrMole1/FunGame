using System.Collections.Generic;
using UnityEngine;

// V0.7.21 (11.3) : Château.
// - Cimetières : les halls qui entourent les Tours et le Donjon ont leur sol 1 m sous le niveau du Hub (gravier gris, celui des
//   Hubs ; bruits de pas sur gravier), sans plafond ; on y descend par de petits escaliers depuis les salles, couloirs et halls
//   voisins. Ils sont ceints de murs de 13 m à créneaux, comme les remparts, prolongés vers le haut par des murs invisibles (pas
//   de Trampoline pour monter dessus). Leurs monticules sont au pied d'une croix ou d'une pierre tombale et contiennent un Os.
// - Tours et Donjon : leur rez-de-chaussée au niveau du cimetière ; une herse ouverte dans l'entrée du Donjon ; au dernier étage,
//   des poupées russes (Meshy, noir et blanc) de 1 à 1,7 fois la taille d'un personnage, contre les murs, dont les yeux suivent le
//   joueur le plus proche ; dans 5 % des Tours et Donjons, ce dernier étage est plongé dans le noir (sans lustre ni musique,
//   poupées au visage effrayant, -1 de sérénité par seconde, caméra qui tremble). En Zone 3, le cadavre de Tête-de-Cloche gît au
//   dernier étage du Donjon, au centre des poupées.
// - 10 % des caisses portent une enclume.
public static partial class WorldBuilder
{
    const float CemeteryDepth = 1f;
    // (Champ statique et non constante : un test peut le forcer avant de générer une zone.)
    public static double DarkTopChance = 0.05;
    const double AnvilOnCrateChance = 0.1;
    // Taille d'un personnage, et taille des poupées russes (de 1 à 1,7 fois la sienne).
    const float CharacterHeight = 1.8f, DollMaxScale = 1.7f;
    // Yeux des poupées (fractions de la hauteur du modèle : x depuis le centre, y depuis la base, z la surface du visage), mesurés sur
    // le modèle Meshy (Resources/Art/Matriochka) ; la poupée effrayante a de plus grands yeux, un peu plus bas.
    static readonly Vector3 DollEye = new Vector3(0.06f, 0.77f, 0.18f), ScaryDollEye = new Vector3(0.068f, 0.735f, 0.185f);

    static Material cemeteryGravel;
    static Material CemeteryGravel
    {
        get
        {
            if (cemeteryGravel != null) return cemeteryGravel;
            cemeteryGravel = Resources.Load<Material>("Materials/CimetiereGravier");
            if (cemeteryGravel == null) cemeteryGravel = GameArt.Lit("CimetiereGravier", new Color(0.45f, 0.45f, 0.46f), GameArt.Noise("concrete", 128, 0.75f, 0.3f, 7), 0.05f);
            return cemeteryGravel;
        }
    }

    // ---------- Cimetières ----------

    static void BuildCemeteryShell(Maze m, ChateauPlan plan, Transform root, Kit shell, Kit walls)
    {
        if (plan.yards.Count == 0) return;
        float ground = -CemeteryDepth;
        var gravel = new MeshBuilder();
        var mask = new bool[m.N, m.N];
        foreach (var c in plan.yards) mask[c.x, c.y] = true;
        var areas = new List<Rect>();
        foreach (Rect r in MaskRects(mask, m.origin.x, m.origin.z, Cell))
        {
            shell.Floor(r, ground, 0.5f, gravel);
            areas.Add(r);
        }
        if (!gravel.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_CimetieresSol", root, gravel.Build("CimetieresSol"), CemeteryGravel);

        var sorted = new List<Vector2Int>(plan.yards);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in sorted)
            for (int d = 0; d < 4; d++)
            {
                var nb = new Vector2Int(c.x + DI[d], c.y + DJ[d]);
                // (Au bord de la zone aussi : le mur du bord monte jusqu'au plafond des halls, il faut le compléter dessous et dessus.)
                if (plan.yards.Contains(nb) || plan.rampart.Contains(nb)) continue;
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                Vector3 edge = m.Center(c.x, c.y) + n * (Cell * 0.5f);
                bool alongX = d >= 2;
                Vector3 Size(float length, float h) => alongX ? new Vector3(length, h, Wall) : new Vector3(Wall, h, length);
                float full = Cell + Wall;
                bool open = m.InGrid(nb.x, nb.y) && !HasWall(m, c.x, c.y, d);
                bool hall = m.InGrid(nb.x, nb.y) && m.Void(nb.x, nb.y);
                if (hall)
                {
                    // Contre un hall (cellule vide, sans mur bâti par l'Échiquier) : le mur entier, de -1 à 13 m, percé d'une porte.
                    if (!open) walls.Box(edge + Vector3.up * ((ground + RampartHeight) * 0.5f), Size(full, RampartHeight - ground));
                    else
                    {
                        float pier = (full - DoorWidth) * 0.5f;
                        foreach (float s in new[] { -1f, 1f })
                            walls.Box(edge + side * (s * (DoorWidth * 0.5f + pier * 0.5f)) + Vector3.up * ((ground + RampartHeight) * 0.5f), Size(pier, RampartHeight - ground));
                        walls.Box(edge + Vector3.up * ((DoorHeight + RampartHeight) * 0.5f), Size(DoorWidth, RampartHeight - DoorHeight));
                        DoorArch(walls.walls, edge, alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f), DoorWidth, DoorHeight, Wall + 0.2f);
                    }
                }
                else
                {
                    // Contre une salle ou un couloir : le mur de l'Échiquier monte jusqu'au plafond des halls (12 m) ; il est
                    // prolongé jusqu'à 13 m, et descend jusqu'au sol du cimetière (sauf sous la porte : l'escalier).
                    float wallTop = m.voidCeil;
                    walls.Box(edge + Vector3.up * ((wallTop + RampartHeight) * 0.5f), Size(full, RampartHeight - wallTop));
                    // V0.7.22 : sans face du dessus (au niveau du Hub, sous le mur qu'il prolonge, elle se confondait avec le sol voisin).
                    void Buried(Vector3 c, Vector3 size)
                    {
                        walls.walls.Box(c, size, 0.5f, 63 & ~(1 << 2));
                        walls.Collider(c, size);
                    }
                    if (!open) Buried(edge + Vector3.up * (ground * 0.5f), Size(full, -ground));
                    else
                    {
                        float pier = (full - DoorWidth) * 0.5f;
                        foreach (float s in new[] { -1f, 1f })
                            Buried(edge + side * (s * (DoorWidth * 0.5f + pier * 0.5f)) + Vector3.up * (ground * 0.5f), Size(pier, -ground));
                    }
                }
                // Créneaux au sommet, mur invisible au-dessus (jusqu'au plafond du Château).
                KitMerlons(walls, edge - side * (Cell * 0.5f), edge + side * (Cell * 0.5f), RampartHeight, 0f, n);
                walls.Collider(edge + Vector3.up * ((RampartHeight + ChateauHeight) * 0.5f), Size(full, ChateauHeight - RampartHeight));
                // Petit escalier qui descend dans le cimetière depuis la porte.
                if (open) KitStairs(shell, edge - n * StairRun(CemeteryDepth), n, DoorWidth, ground, 0f);
            }

        var towers = new List<(Vector2, float)>();
        foreach (var r in plan.rounds) towers.Add((new Vector2(r.center.x, r.center.z), r.radius));
        var area = new GameObject("Cimetieres").AddComponent<CemeteryArea>();
        area.transform.SetParent(root, false);
        area.Init(areas, towers, ground);
    }

    // Emplacement d'un monticule au pied d'une croix ou d'une pierre tombale du cimetière (contient toujours un Os).
    static void CemeteryMoundAt(Maze m, ChateauPlan plan, Vector2Int cell, GameObject prop, List<(Vector3 p, float r)> placed, ZoneRefs refs, System.Random rnd)
    {
        Bounds wb = WorldBounds(prop.transform);
        Vector3 front = prop.transform.forward;
        float reach = Mathf.Abs(front.x) * wb.extents.x + Mathf.Abs(front.z) * wb.extents.z + MoundWidth * 0.5f + 0.15f;
        // Les autres objets du cimetière (pas celui au pied duquel il est posé, le dernier ajouté).
        var others = placed.GetRange(0, Mathf.Max(0, placed.Count - 1));
        foreach (float s in new[] { 1f, -1f })
        {
            Vector3 p = new Vector3(wb.center.x, -CemeteryDepth, wb.center.z) + front * (s * reach);
            if (!m.CellAt(p, out int ci, out int cj) || !plan.yards.Contains(new Vector2Int(ci, cj))) continue;
            if (!CemeteryFree(m, plan, new Vector2Int(ci, cj), p, others, MoundWidth * 0.5f)) continue;
            placed.Add((p, MoundWidth * 0.5f));
            refs.itemSpots.Add(p);
            refs.boneSpots.Add(p);
            return;
        }
    }

    // ---------- Dernier étage des Tours et du Donjon ----------

    static void DressTowerTop(Maze m, RoundPlan round, Vector3 floor, float headroom, Transform root, ZoneRefs refs, int zone, System.Random rnd)
    {
        float wallIn = round.radius - 1f;
        // En Zone 3, le cadavre de Tête-de-Cloche au centre du dernier étage du Donjon (les poupées autour de lui).
        if (round.kind == 1 && zone == GameRules.SecretCorpseZone && !refs.secretCorpsePlaced)
        {
            SecretCorpse.Build(root, floor, Rand(rnd, 0f, 360f), 2f);
            refs.secretCorpsePlaced = true;
        }
        var holder = new GameObject(round.kind == 1 ? "PoupeesDonjon" : "PoupeesTour").transform;
        holder.SetParent(root, false);
        string model = round.dark ? "MatriochkaEffrayante" : "Matriochka";
        float angle = Rand(rnd, 0f, 360f), travelled = 0f;
        float circumference = 2f * Mathf.PI * wallIn;
        while (travelled < 360f)
        {
            float height = CharacterHeight * Rand(rnd, 1f, DollMaxScale);
            float half = height * 0.27f;
            float r = wallIn - half - 0.08f;
            float span = (half * 2f + Rand(rnd, 0.25f, 0.9f)) / (2f * Mathf.PI * r) * 360f;
            float a = angle + travelled + span * 0.5f;
            travelled += span;
            if (travelled > 360f) break;
            // Rien sur l'arrivée de l'escalier (ni juste à côté).
            if (InSector(a, round.arrivalFrom - 12f, round.arrivalTo + 12f)) continue;
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Vector3 p = floor + dir * r;
            var doll = SpawnDoll(holder, model, height, round.dark);
            if (doll == null) break;
            doll.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-dir));
        }
        if (circumference <= 0f) return;
        if (round.dark)
        {
            var dark = new GameObject("EtageNoir").AddComponent<DarkTowerRoom>();
            dark.transform.SetParent(root, false);
            dark.Init(floor, wallIn, headroom);
        }
    }

    // Poupée russe posée au sol (base en 0), tournée vers +Z, ses deux yeux mobiles devant ceux du modèle.
    static GameObject SpawnDoll(Transform parent, string model, float height, bool scary)
    {
        var go = GameArt.SpawnModel(model, parent, height);
        if (go == null) return null;
        go.name = "PoupeeRusse";
        Bounds b = GameArt.LocalBounds(go.transform);
        float h = b.size.y;
        var col = go.AddComponent<CapsuleCollider>();
        col.center = new Vector3(b.center.x, h * 0.5f, b.center.z);
        col.height = h;
        col.radius = Mathf.Min(b.extents.x, b.extents.z) * 0.9f;
        Vector3 eye = scary ? ScaryDollEye : DollEye;
        float ball = h * (scary ? 0.036f : 0.026f);
        Material sclera = scary ? GameArt.Lit("OeilPoupeeNoir", new Color(0.02f, 0.02f, 0.02f), null, 0.7f) : GameArt.Lit("OeilPoupeeBlanc", new Color(0.92f, 0.92f, 0.9f), null, 0.6f);
        Material pupil = scary ? GameArt.Unlit("PupillePoupeeBlanche", new Color(0.95f, 0.95f, 0.95f)) : GameArt.Lit("PupillePoupee", new Color(0.03f, 0.03f, 0.03f), null, 0.9f);
        var balls = new Transform[2];
        for (int k = 0; k < 2; k++)
        {
            float sx = k == 0 ? -1f : 1f;
            var pivot = new GameObject("Oeil").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(b.center.x + sx * eye.x * h, b.min.y + eye.y * h, b.center.z + eye.z * h - ball * 0.35f);
            var white = GameArt.Prim(PrimitiveType.Sphere, pivot, Vector3.zero, Vector3.one * (ball * 2f), sclera);
            // V0.7.22 : l'iris blanc de la poupée effrayante est deux fois plus large (posé un peu plus en arrière et plus bombé, pour
            // épouser l'œil sans s'y enfoncer).
            var dot = scary
                ? GameArt.Prim(PrimitiveType.Sphere, pivot, Vector3.forward * (ball * 0.55f), new Vector3(ball * 1.8f, ball * 1.8f, ball * 1f), pupil)
                : GameArt.Prim(PrimitiveType.Sphere, pivot, Vector3.forward * (ball * 0.72f), new Vector3(ball * 0.9f, ball * 0.9f, ball * 0.5f), pupil);
            foreach (var c in pivot.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in pivot.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            balls[k] = pivot;
        }
        go.AddComponent<DollEyes>().Init(balls);
        return go;
    }

    // ---------- Enclumes ----------

    // 10 % des caisses portent une enclume, posée sur leur dessus.
    static void AnvilOnCrate(GameObject crate, Transform props, System.Random rnd)
    {
        if (crate == null || rnd.NextDouble() >= AnvilOnCrateChance) return;
        var anvil = GameArt.SpawnModelFloor("Enclume", props, ModelHeight("Enclume") * 0.8f);
        if (anvil == null) return;
        Bounds cb = WorldBounds(crate.transform);
        anvil.transform.SetPositionAndRotation(new Vector3(cb.center.x, cb.max.y, cb.center.z), Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f));
        // Elle tient sur le dessus de la caisse.
        Bounds ab = WorldBounds(anvil.transform);
        float fit = Mathf.Min(1f, Mathf.Min(cb.size.x, cb.size.z) * 0.95f / Mathf.Max(0.01f, Mathf.Max(ab.size.x, ab.size.z)));
        if (fit < 1f) anvil.transform.localScale *= fit;
        AddPropCollider(anvil);
    }
}
