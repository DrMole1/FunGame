using System.Collections.Generic;
using UnityEngine;

// V0.7.23 (11.3) : Château.
// - Étages intermédiaires des Tours et du Donjon (à 10 et 20 m pour une Tour) : toujours garnis de caisses, de tonneaux et de gros
//   tonneaux, empilés les uns sur les autres en amas de stockage (l'arrivée de l'escalier reste dégagée).
// - Cimetières : au pied des murs, des cailloux et rochers (de 0,2 à 0,35 fois la taille d'un personnage), de tailles et rotations
//   différentes (modèles Meshy Resources/Art/Caillou1 et Caillou2, même texture de roche grise ; sinon des rochers taillés à facettes).
public static partial class WorldBuilder
{
    // Arrivée de l'escalier sur un étage : rien n'est posé à moins de cet angle (degrés) de part et d'autre.
    const float StorageAccessClear = 55f;

    // Angle (degrés) où le colimaçon de KitSpiral (de y0 à y1, départ 'start') atteint la hauteur 'level'.
    static float SpiralAngleAt(float rIn, float rOut, float y0, float y1, float start, float level)
    {
        float rMid = (rIn + rOut) * 0.5f;
        float stepAngle = SpiralTread / rMid * Mathf.Rad2Deg;
        int steps = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / SpiralRise));
        float rise = (y1 - y0) / steps;
        return start + (level - y0) / rise * stepAngle;
    }

    static void FurnishTowerFloors(RoundPlan tower, Transform props, System.Random rnd)
    {
        float disc = tower.stairInner - 0.05f;
        if (disc < 2f) return;
        foreach (var (y, access) in tower.midFloors)
        {
            Vector3 c = new Vector3(tower.center.x, y, tower.center.z);
            var placed = new List<(Vector3 p, float r)>();
            // Les amas font le tour de l'étage (hors arrivée de l'escalier), à intervalles d'environ 1,6 m le long du bord.
            float step = Mathf.Clamp(1.6f / disc * Mathf.Rad2Deg, 18f, 60f);
            int piles = Mathf.Max(3, Mathf.FloorToInt((360f - 2f * StorageAccessClear) / step * 0.7f));
            float angle = access + StorageAccessClear + step * 0.5f;
            for (int k = 0; k < piles * 3 && piles > 0; k++, angle += step * Rand(rnd, 0.9f, 1.3f))
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angle, access)) < StorageAccessClear) continue;
                if (StoragePile(c, disc, angle, placed, props, rnd)) piles--;
            }
        }
    }

    // Un amas : 1 à 3 objets au sol, côte à côte contre le bord de l'étage, et sur chacun une pile de 0 à 2 objets.
    static bool StoragePile(Vector3 c, float disc, float angle, List<(Vector3 p, float r)> placed, Transform props, System.Random rnd)
    {
        Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        Vector3 along = Vector3.Cross(Vector3.up, outward);
        int bases = 1 + rnd.Next(2);
        bool any = false;
        float offset = -(bases - 1) * 0.45f;
        for (int b = 0; b < bases; b++, offset += 0.9f)
        {
            double pick = rnd.NextDouble();
            string model = pick < 0.45 ? ModelVariant("Caisse", rnd) : pick < 0.75 ? "Tonneau" : "TonneauGros";
            var go = SpawnStorage(model, props, rnd);
            if (go == null) continue;
            Bounds lb = GameArt.LocalBounds(go.transform);
            float r = Mathf.Max(lb.extents.x, lb.extents.z) * go.transform.lossyScale.x;
            // Contre le bord de l'étage (le vide de l'escalier autour), à 10 cm.
            Vector3 p = c + outward * (disc - r - 0.1f) + along * offset;
            if (Flat(p - c) + r > disc - 0.05f || placed.Exists(q => Flat(q.p - p) < q.r + r + 0.05f)) { Object.Destroy(go); continue; }
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, angle + Rand(rnd, -15f, 15f), 0f));
            AddPropCollider(go);
            placed.Add((p, r));
            any = true;
            // Pile : on pose sur le dessus de l'objet du dessous (pas sur un gros tonneau, déjà haut).
            GameObject below = go;
            string belowModel = model;
            int stack = belowModel == "TonneauGros" ? rnd.Next(2) : 1 + rnd.Next(2);
            for (int s = 0; s < stack; s++)
            {
                string up = rnd.NextDouble() < 0.6 ? ModelVariant("Caisse", rnd) : "Tonneau";
                var top = SpawnStorage(up, props, rnd);
                if (top == null) break;
                Bounds wb = WorldBounds(below.transform);
                Bounds tb = GameArt.LocalBounds(top.transform);
                float tr = Mathf.Max(tb.extents.x, tb.extents.z) * top.transform.lossyScale.x;
                // Pas plus large que ce qui le porte (il ne déborde ni dans le vide ni sur l'amas voisin).
                if (tr > Mathf.Max(wb.extents.x, wb.extents.z) + 0.1f) { Object.Destroy(top); break; }
                Vector3 at = new Vector3(wb.center.x, wb.max.y, wb.center.z) + Random2(rnd, 0.08f);
                top.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f));
                AddPropCollider(top);
                below = top;
                belowModel = up;
            }
            if (belowModel.StartsWith("Caisse")) AnvilOnCrate(below, props, rnd);
        }
        return any;
    }

    static GameObject SpawnStorage(string model, Transform props, System.Random rnd)
    {
        var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
        if (go == null) return null;
        DressProp(go, model, rnd);
        return go;
    }

    static Vector3 Random2(System.Random rnd, float max) => new Vector3(Rand(rnd, -max, max), 0f, Rand(rnd, -max, max));

    // ---------- Cailloux des cimetières ----------

    const float RockMinScale = 0.2f, RockMaxScale = 0.35f, RockChance = 0.6f;

    // Le long de chaque mur d'un cimetière (côté gravier), un caillou ou un rocher tous les 0,7 à 1,8 m, six fois sur dix ; jamais devant
    // une porte, son petit escalier, l'entrée d'une Tour, ni contre une croix, une pierre tombale ou un monticule.
    static void BuildCemeteryRocks(Maze m, ChateauPlan plan, Transform props, List<(Vector3 p, float r)> placed, System.Random rnd)
    {
        if (plan.yards.Count == 0) return;
        var holder = new GameObject("Z" + m.zone + "_CimetieresCailloux").transform;
        holder.SetParent(props, false);
        var cells = new List<Vector2Int>(plan.yards);
        cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        float ground = -CemeteryDepth;
        float face = Cell * 0.5f - Wall * 0.5f;
        float doorClear = DoorWidth * 0.5f + 0.4f, stairRun = StairRun(CemeteryDepth) + 0.3f;
        foreach (var cell in cells)
        {
            Vector3 cc = m.Center(cell.x, cell.y);
            for (int d = 0; d < 4; d++)
            {
                var nb = new Vector2Int(cell.x + DI[d], cell.y + DJ[d]);
                if (plan.yards.Contains(nb)) continue;
                Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                bool door = m.InGrid(nb.x, nb.y) && !HasWall(m, cell.x, cell.y, d);
                for (float t = -face + Rand(rnd, 0.2f, 0.8f); t < face - 0.2f; t += Rand(rnd, 0.7f, 1.8f))
                {
                    if (rnd.NextDouble() >= RockChance) continue;
                    if (door && Mathf.Abs(t) < doorClear) continue;
                    float h = CharacterHeight * Rand(rnd, RockMinScale, RockMaxScale);
                    var rock = SpawnRock(holder, h, rnd);
                    if (rock == null) return;
                    rock.transform.rotation = Quaternion.Euler(Rand(rnd, -12f, 12f), Rand(rnd, 0f, 360f), Rand(rnd, -12f, 12f));
                    Bounds wb = WorldBounds(rock.transform);
                    float depth = Mathf.Abs(n.x) * wb.extents.x + Mathf.Abs(n.z) * wb.extents.z;
                    float r = Mathf.Max(wb.extents.x, wb.extents.z);
                    // Contre le mur, un peu enfoncé dans le gravier (il n'a pas l'air posé dessus).
                    Vector3 p = new Vector3(cc.x, ground, cc.z) + n * (face - depth - 0.02f) + side * t;
                    Vector3 offset = p - new Vector3(wb.center.x, wb.min.y, wb.center.z);
                    rock.transform.position += offset + Vector3.down * (h * 0.08f);
                    if (!RockFree(plan, p, r, door ? cc + n * face : (Vector3?)null, n, doorClear, stairRun, placed))
                    {
                        Object.Destroy(rock);
                        continue;
                    }
                    placed.Add((p, r));
                    AddPropCollider(rock);
                }
            }
        }
    }

    static bool RockFree(ChateauPlan plan, Vector3 p, float r, Vector3? door, Vector3 n, float doorClear, float stairRun, List<(Vector3 p, float r)> placed)
    {
        foreach (var round in plan.rounds)
        {
            if (Flat(round.center - p) < round.radius + r + 0.4f) return false;
            // Devant l'entrée de la Tour : un passage de 3 m de large sur 7 m.
            Vector3 entry = Quaternion.Euler(0f, round.doorAngle, 0f) * Vector3.forward;
            Vector3 rel = Flat3(p - round.center);
            float along = Vector3.Dot(rel, entry), across = Mathf.Abs(Vector3.Dot(rel, new Vector3(entry.z, 0f, -entry.x)));
            if (along > 0f && along < round.radius + 7f && across < 1.5f + r + 0.3f) return false;
        }
        // Le petit escalier qui descend de la porte.
        if (door.HasValue)
        {
            Vector3 rel = Flat3(p - door.Value);
            float inward = -Vector3.Dot(rel, n), across = Mathf.Abs(Vector3.Dot(rel, new Vector3(n.z, 0f, n.x)));
            if (inward < stairRun + r && across < doorClear + r) return false;
        }
        foreach (var q in placed) if (Flat(q.p - p) < q.r + r + 0.15f) return false;
        return true;
    }

    // Caillou de hauteur 'height' : un des deux modèles Meshy (Resources/Art/Caillou1, Caillou2), sinon un rocher taillé à facettes.
    static GameObject SpawnRock(Transform parent, float height, System.Random rnd)
    {
        var go = GameArt.SpawnModelFloor("Caillou" + (1 + rnd.Next(2)), parent, height);
        if (go != null)
        {
            // Plus ou moins trapu (sa plus grande dimension reste 'height').
            go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(Rand(rnd, 0.85f, 1f), Rand(rnd, 0.7f, 1f), Rand(rnd, 0.85f, 1f)));
            return go;
        }
        var rock = GameArt.MeshObject("Caillou", parent, FacetedRock(rnd.Next(RockVariants)), RockMaterial);
        // Sa plus grande dimension vaut 'height' (comme un modèle posé par SpawnModelFloor) ; plus ou moins trapu.
        rock.transform.localScale = new Vector3(Rand(rnd, 0.85f, 1f), Rand(rnd, 0.7f, 1f), Rand(rnd, 0.75f, 1f)) * height;
        return rock;
    }

    const int RockVariants = 4;
    static readonly Mesh[] rockMeshes = new Mesh[RockVariants];
    static Material rockMaterial;
    static Material RockMaterial => rockMaterial != null ? rockMaterial
        : rockMaterial = GameArt.Lit("CaillouRocheGrise", new Color(0.5f, 0.5f, 0.49f), GameArt.Noise("rock", 128, 0.7f, 0.35f, 11), 0.08f);

    // Icosaèdre subdivisé, aux sommets déplacés au hasard, à facettes plates ; base aplatie à y = 0, hauteur 1.
    static Mesh FacetedRock(int variant)
    {
        if (rockMeshes[variant] != null) return rockMeshes[variant];
        var rnd = new System.Random(4721 + variant * 97);
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        // Une subdivision : chaque arête coupée en deux.
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(key, out int i)) return i;
            v.Add((v[a].normalized + v[b].normalized) * 0.5f);
            return mid[key] = v.Count - 1;
        }
        var tris = new List<int>();
        for (int k = 0; k < f.Length; k += 3)
        {
            int a = f[k], b = f[k + 1], c = f[k + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        // Bosses et méplats : rayon au hasard, puis le dessous écrasé (il repose sur le sol).
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 p = v[i].normalized * Rand(rnd, 0.72f, 1.08f);
            if (p.y < -0.35f) p.y = -0.35f - (p.y + 0.35f) * 0.15f;
            v[i] = p;
        }
        // Plus grande dimension ramenée à 1 (comme un modèle posé par SpawnModelFloor), base à y = 0.
        var box = new Bounds(v[0], Vector3.zero);
        foreach (var p in v) box.Encapsulate(p);
        float minY = box.min.y, span = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();
        for (int k = 0; k < tris.Count; k += 3)
        {
            // Face tournée vers l'extérieur (sens de Unity : normale = (b - a) x (c - a)).
            Vector3 a0 = v[tris[k]], b0 = v[tris[k + 1]], c0 = v[tris[k + 2]];
            if (Vector3.Dot(Vector3.Cross(b0 - a0, c0 - a0), a0 + b0 + c0) < 0f) (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);
            for (int s = 0; s < 3; s++)
            {
                Vector3 p = v[tris[k + s]];
                p = new Vector3(p.x / span, (p.y - minY) / span, p.z / span);
                verts.Add(p);
                uvs.Add(new Vector2(p.x + p.z * 0.7f, p.y + p.z * 0.4f));
                idx.Add(verts.Count - 1);
            }
        }
        var mesh = new Mesh { name = "Caillou" + variant };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return rockMeshes[variant] = mesh;
    }
}
