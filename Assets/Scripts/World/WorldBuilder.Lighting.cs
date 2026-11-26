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

    static void SplitLargeMeshes(Transform root)
    {
        var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>(true));
        foreach (var mr in renderers)
        {
            if (mr == null || mr.transform.childCount > 0) continue;
            var mf = mr.GetComponent<MeshFilter>();
            // Maillages construits pendant la partie seulement (identifiant négatif), pas les modèles importés.
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.GetInstanceID() >= 0 || !mf.sharedMesh.isReadable || mf.sharedMesh.subMeshCount != 1) continue;
            // Seulement les maillages de décor (aucun script dessus ; une collision reste sur l'objet d'origine, qui n'est plus affiché).
            bool onlyDecor = true;
            foreach (var c in mr.GetComponents<Component>())
                if (!(c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider)) onlyDecor = false;
            if (!onlyDecor) continue;
            Bounds b = mr.bounds;
            if (Mathf.Max(b.size.x, b.size.z) <= LightChunk * 1.5f) continue;
            SplitMesh(mr, mf);
        }
    }

    static void SplitMesh(MeshRenderer mr, MeshFilter mf)
    {
        Mesh mesh = mf.sharedMesh;
        Transform tr = mr.transform;
        var srcV = mesh.vertices;
        var srcN = mesh.normals;
        var srcU = mesh.uv;
        bool hasN = srcN != null && srcN.Length == srcV.Length, hasU = srcU != null && srcU.Length == srcV.Length;
        int[] srcT = mesh.triangles;
        var parts = new Dictionary<(int, int), Part>();

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
                var key = (Mathf.FloorToInt(g.x / LightChunk), Mathf.FloorToInt(g.z / LightChunk));
                if (!parts.TryGetValue(key, out var part)) parts[key] = part = new Part();
                part.Add(a); part.Add(b); part.Add(c);
            }
        }
        if (parts.Count <= 1) return;
        foreach (var kv in parts)
        {
            var part = kv.Value;
            var m = new Mesh { name = mesh.name + "_" + kv.Key.Item1 + "_" + kv.Key.Item2 };
            if (part.verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(part.verts);
            if (hasN) m.SetNormals(part.normals);
            if (hasU) m.SetUVs(0, part.uvs);
            m.SetTriangles(part.tris, 0);
            m.RecalculateBounds();
            var go = new GameObject(mr.name + "_" + kv.Key.Item1 + "_" + kv.Key.Item2);
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
