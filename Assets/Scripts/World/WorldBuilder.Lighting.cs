using System.Collections.Generic;
using UnityEngine;

// V0.7.20 : éclairage lissé dans les structures (Château, Blockhaus).
// Le rendu (Forward) éclaire chaque objet avec ses quelques lampes les plus proches de son centre (les autres en approximation) et
// lui donne la sonde de reflets la plus proche de son centre. Les pièces construites d'un seul tenant pour toute une structure
// (murs et sols du Kit, plafonds gris, plinthes, corniches et voûtes, moquettes, douve...) mesuraient jusqu'à 170 m : elles
// prenaient les lampes et les reflets d'un endroit lointain, d'où des coupures nettes de lumière là où elles touchent les murs du
// labyrinthe (découpés, eux, en blocs de 10 m). Elles sont redécoupées en blocs de 10 m, comme les murs : de part et d'autre d'une
// jonction, les mêmes lampes et le même reflet.
public static partial class WorldBuilder
{
    const float LightChunk = 10f;

    // V0.7.23 (Note 08) : 'storey' > 0 (Blockhaus) : les pans sont aussi découpés par étage (tranches de 'storey' mètres à partir de
    // 'floor0'). Les finitions, moquettes et façades d'un seul tenant couvraient les trois étages : elles comptaient les lampes de tous
    // les étages (jusqu'à 24), bien au-delà des lampes calculées au pixel, d'où des coupures nettes de lumière.
    static void SplitLargeMeshes(Transform root, float storey = 0f, float floor0 = 0f)
    {
        var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>(true));
        foreach (var mr in renderers)
        {
            if (mr == null || mr.transform.childCount > 0) continue;
            // V0.7.24 (Note 05) : jamais les câbles des glyphes et des leviers (non éclairés) : redécoupé, l'original était détruit, et
            // le glyphe (ou le levier) qui le tenait ne pouvait plus le rendre blanc lumineux une fois actionné.
            if (mr.name == "Cable") continue;
            var mf = mr.GetComponent<MeshFilter>();
            // Maillages construits pendant la partie seulement (identifiant négatif), pas les modèles importés.
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.GetInstanceID() >= 0 || !mf.sharedMesh.isReadable || mf.sharedMesh.subMeshCount != 1) continue;
            // Seulement les maillages de décor (aucun script dessus ; une collision reste sur l'objet d'origine, qui n'est plus affiché).
            bool onlyDecor = true;
            foreach (var c in mr.GetComponents<Component>())
                if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider)) onlyDecor = false;
            if (!onlyDecor) continue;
            Bounds b = mr.bounds;
            bool tall = storey > 0f && b.size.y > storey * 1.2f;
            if (Mathf.Max(b.size.x, b.size.z) <= LightChunk * 1.5f && !tall) continue;
            SplitMesh(mr, mf, storey, floor0);
        }
    }

    static void SplitMesh(MeshRenderer mr, MeshFilter mf, float storey, float floor0)
    {
        Mesh mesh = mf.sharedMesh;
        Transform tr = mr.transform;
        var srcV = mesh.vertices;
        var srcN = mesh.normals;
        var srcU = mesh.uv;
        bool hasN = srcN != null && srcN.Length == srcV.Length, hasU = srcU != null && srcU.Length == srcV.Length;
        int[] srcT = mesh.triangles;
        var parts = new Dictionary<(int, int, int), Part>();

        // Longueur maximale d'une arête (repère local) : un long triangle est recoupé pour que chaque morceau tombe dans un bloc.
        float scale = Mathf.Max(0.0001f, Mathf.Max(tr.lossyScale.x, tr.lossyScale.z));
        float maxEdge = LightChunk / scale;
        var stack = new Stack<(V, V, V, int)>();
        for (int k = 0; k + 2 < srcT.Length; k += 3)
        {
            V Get(int i) => new V { p = srcV[i], n = hasN ? srcN[i] : Vector3.up, u = hasU ? srcU[i] : Vector2.zero };
            stack.Push((Get(srcT[k]), Get(srcT[k + 1]), Get(srcT[k + 2]), 0));
            while (stack.Count > 0)
            {
                var (a, b, c, depth) = stack.Pop();
                float ab = (a.p - b.p).sqrMagnitude, bc = (b.p - c.p).sqrMagnitude, ca = (c.p - a.p).sqrMagnitude;
                float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                if (longest > maxEdge * maxEdge && depth < 12)
                {
                    // Coupe l'arête la plus longue en son milieu (même orientation pour les deux moitiés).
                    if (longest == ab) { var m = V.Mid(a, b); stack.Push((a, m, c, depth + 1)); stack.Push((m, b, c, depth + 1)); }
                    else if (longest == bc) { var m = V.Mid(b, c); stack.Push((a, b, m, depth + 1)); stack.Push((a, m, c, depth + 1)); }
                    else { var m = V.Mid(c, a); stack.Push((a, b, m, depth + 1)); stack.Push((m, b, c, depth + 1)); }
                    continue;
                }
                Vector3 g = tr.TransformPoint((a.p + b.p + c.p) / 3f);
                // Étage : un sol (à la hauteur de son étage) compte avec lui, pas avec l'étage du dessous (5 cm de marge).
                int level = storey > 0f ? Mathf.FloorToInt((g.y - floor0 + 0.05f) / storey) : 0;
                var key = (Mathf.FloorToInt(g.x / LightChunk), Mathf.FloorToInt(g.z / LightChunk), level);
                if (!parts.TryGetValue(key, out var part)) parts[key] = part = new Part();
                part.Add(a); part.Add(b); part.Add(c);
            }
        }
        if (parts.Count <= 1) return;
        foreach (var kv in parts)
        {
            var part = kv.Value;
            var m = new Mesh { name = mesh.name + "_" + kv.Key.Item1 + "_" + kv.Key.Item2 + "_" + kv.Key.Item3 };
            if (part.verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(part.verts);
            if (hasN) m.SetNormals(part.normals);
            if (hasU) m.SetUVs(0, part.uvs);
            m.SetTriangles(part.tris, 0);
            m.RecalculateBounds();
            var go = new GameObject(mr.name + "_" + kv.Key.Item1 + "_" + kv.Key.Item2 + "_" + kv.Key.Item3);
            go.layer = mr.gameObject.layer;
            go.transform.SetParent(tr.parent, false);
            go.transform.localPosition = tr.localPosition;
            go.transform.localRotation = tr.localRotation;
            go.transform.localScale = tr.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = mr.shadowCastingMode;
            r.receiveShadows = mr.receiveShadows;
            r.lightProbeUsage = mr.lightProbeUsage;
            r.reflectionProbeUsage = mr.reflectionProbeUsage;
            r.enabled = mr.enabled;
            go.SetActive(mr.gameObject.activeSelf);
        }
        if (mr.GetComponent<Collider>() != null) mr.enabled = false;
        else
        {
            Object.Destroy(mr.gameObject);
            Object.Destroy(mesh);
        }
    }

    // V0.7.22 : nombre de lampes calculées au pixel par objet au réglage "Haut" (LightQuality). Au-delà, le rendu éclaire les lampes en
    // trop de manière approchée (par sommet) : sur les grands pans de 10 m, l'éclairage change d'un coup à la jonction de deux blocs qui
    // n'ont pas retenu les mêmes lampes (salles du Château riches en lustres et en torches, salles uniques). Les lampes en trop sont
    // retirées une à une jusqu'à ce qu'aucun mur, sol ou plafond n'en reçoive plus : à chaque fois celle qui touche le plus de pans
    // surchargés, la plus faible à égalité. Son ampoule (néon, flamme, lustre) reste allumée ; seule sa lumière disparaît.
    const int MaxLightsPerSurface = 6;
    // Pans pris en compte : les surfaces assez grandes pour que l'écart se voie.
    const float ThinSurfaceSize = 2.5f;
    // Distance (en part de sa portée) sous laquelle une autre lampe gardée doit se trouver pour qu'une lampe soit retirée.
    const float ThinCoverage = 0.6f;

    static void ThinLights(Transform root)
    {
        Physics.SyncTransforms();
        var lights = new List<Light>();
        foreach (var l in root.GetComponentsInChildren<Light>(true))
            if (l.type == LightType.Point && l.renderMode == LightRenderMode.Auto && l.intensity > 0.01f && l.range > 0.01f) lights.Add(l);
        if (lights.Count <= MaxLightsPerSurface) return;
        // Ordre fixe (le même chez tous les joueurs) : par position.
        lights.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position, pb = b.transform.position;
            return pa.x != pb.x ? pa.x.CompareTo(pb.x) : pa.z != pb.z ? pa.z.CompareTo(pb.z) : pa.y.CompareTo(pb.y);
        });
        var touching = new List<List<int>>();   // par surface : lampes qui l'atteignent
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!mr.enabled || mr.GetComponentInParent<FlickerLight>() != null) continue;
            Bounds b = mr.bounds;
            if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < ThinSurfaceSize) continue;
            List<int> list = null;
            for (int k = 0; k < lights.Count; k++)
            {
                Light l = lights[k];
                if (b.SqrDistance(l.transform.position) >= l.range * l.range) continue;
                // V0.7.23 : une lampe qui ignore le calque de la surface (autre étage du Blockhaus) ne compte pas.
                if ((l.cullingMask & (1 << mr.gameObject.layer)) == 0) continue;
                (list ??= new List<int>()).Add(k);
            }
            if (list != null && list.Count > MaxLightsPerSurface) touching.Add(list);
        }
        // Seules les lampes du décor (néons, lustres, torches) sont retirées ; les autres (glyphes...) comptent mais restent.
        var owners = new FlickerLight[lights.Count];
        for (int k = 0; k < lights.Count; k++)
        {
            var owner = lights[k].GetComponentInParent<FlickerLight>();
            owners[k] = owner != null && !owner.IgnoreCurfew ? owner : null;
        }
        var removed = new bool[lights.Count];
        var counts = new int[touching.Count];
        for (int s = 0; s < touching.Count; s++) counts[s] = touching[s].Count;
        var score = new int[lights.Count];
        // Une lampe n'est retirée que si une autre, gardée, proche et en vue directe (aucun mur entre elles), éclaire déjà son coin :
        // aucune pièce ne se retrouve dans le noir.
        // La lampe qui prend le relais d'une lampe retirée est gardée pour de bon (sinon, de relais en relais, une pièce s'éteindrait).
        var keep = new bool[lights.Count];
        int Cover(int k)
        {
            Vector3 p = lights[k].transform.position;
            float reach = lights[k].range * ThinCoverage;
            for (int j = 0; j < lights.Count; j++)
            {
                if (j == k || removed[j]) continue;
                Vector3 q = lights[j].transform.position;
                if ((q - p).sqrMagnitude >= reach * reach) continue;
                if (!Physics.Linecast(p, q, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return j;
            }
            return -1;
        }
        while (true)
        {
            System.Array.Clear(score, 0, score.Length);
            bool any = false;
            for (int s = 0; s < touching.Count; s++)
            {
                if (counts[s] <= MaxLightsPerSurface) continue;
                any = true;
                foreach (int k in touching[s]) if (!removed[k]) score[k]++;
            }
            if (!any) break;
            int best = -1;
            for (int k = 0; k < lights.Count; k++)
            {
                if (removed[k] || keep[k] || score[k] == 0 || owners[k] == null) continue;
                if (best >= 0 && !(score[k] > score[best] || score[k] == score[best] && Strength(lights[k]) < Strength(lights[best]))) continue;
                if (Cover(k) < 0) continue;
                best = k;
            }
            if (best < 0) break;
            keep[Cover(best)] = true;
            removed[best] = true;
            for (int s = 0; s < touching.Count; s++) if (touching[s].Contains(best)) counts[s]--;
            owners[best].MakeLightless();
        }
    }

    static float Strength(Light l) => l.intensity * l.range;

    // V0.7.23 (Note 08) : étages du Blockhaus séparés pour l'éclairage. Une lampe accrochée sous le plafond d'un étage touchait aussi le sol
    // (la moquette) de l'étage du dessus, à quelques centimètres au-dessus d'elle : sans l'éclairer (elle est dessous), elle prenait l'une
    // des places des lampes calculées au pixel, et la lumière s'arrêtait net entre deux pans (jusqu'à 18 lampes sur un pan de moquette).
    // Les surfaces de chaque étage passent sur un calque à lui (26, 27, 28), et ses lampes ignorent ceux des deux autres étages. Les cages
    // d'escalier et les appartements de double hauteur (ouverts d'un étage à l'autre) gardent le calque par défaut, éclairé par tous.
    public const int StoreyLayerBase = 26;
    // Façades du Blockhaus côté ravin (et leurs fenêtres) : les lampes des pièces derrière elles les touchaient sans pouvoir les éclairer
    // (elles sont de l'autre côté) ; sur leur propre calque, seules les lampes du dehors les comptent.
    public const int FacadeLayer = 25;

    static void SeparateStoreyLights(Transform root, Maze main, BlockhausPlan plan)
    {
        int storeys = StoreyLevels.Length;
        int allStoreys = 0;
        for (int s = 0; s < storeys; s++) allStoreys |= 1 << (StoreyLayerBase + s);
        // Par étage : cellules ouvertes sur un autre étage (cages d'escalier ; appartement de double hauteur, à son étage et à celui du
        // dessus).
        var open = new HashSet<Vector2Int>[storeys];
        for (int s = 0; s < storeys; s++) open[s] = new HashSet<Vector2Int>(plan.wellCells);
        for (int s = 0; s < storeys && s < plan.storeys.Length; s++)
            foreach (Room room in plan.storeys[s].rooms)
                if (room.ceil > StoreyHeight + 0.01f)
                    for (int t = s; t <= s + 1 && t < storeys; t++) open[t].UnionWith(room.cells);
        // Étage d'un point de la structure (-1 : hors de la structure, ou dans une partie ouverte d'un étage à l'autre).
        int StoreyAt(Vector3 p)
        {
            if (!main.CellAt(p, out int i, out int j) || !main.InStructure(i, j)) return -1;
            int s = Mathf.FloorToInt((p.y - StoreyLevels[0] + 0.05f) / StoreyHeight);
            return s >= 0 && s < storeys && !open[s].Contains(new Vector2Int(i, j)) ? s : -1;
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.layer != 0 || r is ParticleSystemRenderer) continue;
            if (r.name.Contains("_FacadesBlockhaus") || r.name.Contains("_FenetresRavin"))
            {
                r.gameObject.layer = FacadeLayer;
                continue;
            }
            Bounds b = r.bounds;
            int s = StoreyAt(b.center);
            if (s < 0) continue;
            // Tout entier dans son étage (un pan qui déborde sur un autre étage reste éclairé par tous).
            float floor = StoreyLevels[s];
            if (b.min.y < floor - 0.1f || b.max.y > floor + StoreyHeight + 0.1f) continue;
            r.gameObject.layer = StoreyLayerBase + s;
        }
        foreach (var l in root.GetComponentsInChildren<Light>(true))
        {
            int s = StoreyAt(l.transform.position);
            if (s < 0) continue;
            l.cullingMask &= ~(allStoreys & ~(1 << (StoreyLayerBase + s))) & ~(1 << FacadeLayer);
        }
    }

    struct V
    {
        public Vector3 p, n;
        public Vector2 u;

        public static V Mid(V a, V b) => new V { p = (a.p + b.p) * 0.5f, n = (a.n + b.n).normalized, u = (a.u + b.u) * 0.5f };
    }

    class Part
    {
        public readonly List<Vector3> verts = new List<Vector3>(), normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> tris = new List<int>();

        public void Add(V v)
        {
            tris.Add(verts.Count);
            verts.Add(v.p);
            normals.Add(v.n);
            uvs.Add(v.u);
        }
    }
}
