using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// V0.6.8 : Balise Améliorée blanche (texture, matériau, icône) et corps sans tête de la statue de Pavlov (sa variante à tête de chien).
public static class V068Setup
{
    const string StatueBody = "Assets/Resources/Art/Decor/StatuePavlovSansTete.asset";

    [MenuItem("FunGame/Préparer les modèles V0.6.8")]
    public static void Run()
    {
        EntitySetup.CreateWhiteBeacon();
        AssetDatabase.SaveAssets();
        IconRenderer.RenderEnhancedBeacon();
        BuildHeadlessStatue();
        AssetDatabase.SaveAssets();
        Debug.Log("Modèles V0.6.8 prêts.");
    }

    // Retire les triangles au-dessus du cou (repère du modèle, debout).
    static void BuildHeadlessStatue()
    {
        var prefab = Resources.Load<GameObject>("Art/Decor/StatuePavlov");
        var go = Object.Instantiate(prefab);
        try
        {
            go.transform.position = Vector3.zero;
            var filter = go.GetComponentInChildren<MeshFilter>();
            Mesh source = filter.sharedMesh;
            Vector3[] verts = source.vertices;
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            float low = float.MaxValue, high = float.MinValue;
            var y = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                y[i] = toWorld.MultiplyPoint3x4(verts[i]).y;
                low = Mathf.Min(low, y[i]);
                high = Mathf.Max(high, y[i]);
            }
            float neck = Mathf.Lerp(low, high, GameRules.StatueNeckHeight);
            int[] tris = source.triangles;
            var map = new Dictionary<int, int>();
            var kept = new List<int>();
            Vector3[] normals = source.normals;
            Vector2[] uv = source.uv;
            var newVerts = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUv = new List<Vector2>();
            int Remap(int v)
            {
                if (map.TryGetValue(v, out int n)) return n;
                map[v] = n = newVerts.Count;
                newVerts.Add(verts[v]);
                if (normals.Length > 0) newNormals.Add(normals[v]);
                if (uv.Length > 0) newUv.Add(uv[v]);
                return n;
            }
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                if ((y[tris[t]] + y[tris[t + 1]] + y[tris[t + 2]]) / 3f > neck) continue;
                kept.Add(Remap(tris[t]));
                kept.Add(Remap(tris[t + 1]));
                kept.Add(Remap(tris[t + 2]));
            }
            var mesh = new Mesh { name = "StatuePavlovSansTete" };
            if (newVerts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(newVerts);
            if (newNormals.Count > 0) mesh.SetNormals(newNormals);
            if (newUv.Count > 0) mesh.SetUVs(0, newUv);
            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(StatueBody);
            AssetDatabase.CreateAsset(mesh, StatueBody);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
