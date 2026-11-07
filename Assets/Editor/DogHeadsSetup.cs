using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Têtes de chiens du décor (Note 05 de la DA, V0.6) : la tête complète de chaque skin (triangles rattachés aux os de la
// tête, pose de repos), avec la texture du skin, sauf les yeux, repeints en noir uni (ni pupille, ni blanc de l'œil).
// Résultat : Resources/Art/Decor/TetesChiens/TeteN.asset (maillage : base du cou en 0, museau vers +Z, hauteur 1) et
// TeteN.png (texture). Les yeux de chaque skin sont repérés à la main (EyeSpots), en coordonnées de la tête.
public static class DogHeadsSetup
{
    public const string Folder = "Assets/Resources/Art/Decor/TetesChiens/";

    // Par skin : centres des deux yeux (x, y : face vue de devant, hauteur de la tête = 1) et rayon de la zone noircie.
    public static readonly Dictionary<string, (Vector2 left, Vector2 right, float radius)> EyeSpots = new Dictionary<string, (Vector2, Vector2, float)>
    {
        { "Perso1", (new Vector2(0.255f, 0.56f), new Vector2(-0.249f, 0.56f), 0.08f) },
        { "Perso2", (new Vector2(0.225f, 0.53f), new Vector2(-0.216f, 0.53f), 0.09f) },
        { "Perso3", (new Vector2(0.225f, 0.39f), new Vector2(-0.186f, 0.39f), 0.08f) },
        { "Perso4", (new Vector2(0.225f, 0.725f), new Vector2(-0.225f, 0.725f), 0.065f) },
        { "Perso5", (new Vector2(0.18f, 0.35f), new Vector2(-0.186f, 0.35f), 0.095f) },
        { "Perso6", (new Vector2(0.105f, 0.35f), new Vector2(-0.135f, 0.35f), 0.06f) },
        { "Perso7", (new Vector2(0.195f, 0.365f), new Vector2(-0.144f, 0.365f), 0.07f) },
        { "Perso8", (new Vector2(0.18f, 0.365f), new Vector2(-0.15f, 0.365f), 0.065f) },
    };

    [MenuItem("FunGame/Préparer les têtes de chiens")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder.TrimEnd('/')))
            AssetDatabase.CreateFolder("Assets/Resources/Art/Decor", "TetesChiens");
        var library = Object.FindObjectOfType<CharacterLibrary>(true);
        if (library == null) { Debug.LogError("CharacterLibrary introuvable (ouvrir la scène du jeu)."); return; }
        for (int k = 0; k < library.characters.Length; k++)
        {
            var entry = library.characters[k];
            if (entry == null || entry.model == null) continue;
            var head = Extract(entry.model, out Texture source);
            if (head == null) continue;
            string name = "Tete" + (k + 1);
            AssetDatabase.DeleteAsset(Folder + name + ".asset");
            AssetDatabase.CreateAsset(head, Folder + name + ".asset");
            var tex = Readable(source);
            if (EyeSpots.TryGetValue(entry.name, out var eyes)) PaintEyes(head, tex, eyes.left, eyes.right, eyes.radius);
            File.WriteAllBytes(Folder + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        AssetDatabase.Refresh();
        Debug.Log("Têtes de chiens prêtes.");
    }

    // Tête du skin : triangles dont les sommets suivent surtout les os de la tête ; base du cou en (0, 0, 0), museau vers
    // +Z, hauteur ramenée à 1.
    public static Mesh Extract(GameObject prefab, out Texture texture)
    {
        texture = null;
        var go = Object.Instantiate(prefab);
        try
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var animator = go.GetComponentInChildren<Animator>();
            Transform headBone = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (smr == null || headBone == null) return null;
            texture = smr.sharedMaterial != null ? smr.sharedMaterial.mainTexture : null;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            Vector3[] verts = baked.vertices;
            Vector2[] uv = baked.uv;
            Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
            for (int i = 0; i < verts.Length; i++) verts[i] = toWorld.MultiplyPoint3x4(verts[i]);
            var headBones = new HashSet<int>();
            for (int b = 0; b < smr.bones.Length; b++) if (smr.bones[b] != null && smr.bones[b].IsChildOf(headBone)) headBones.Add(b);
            BoneWeight[] w = smr.sharedMesh.boneWeights;
            bool InHead(int i) =>
                (headBones.Contains(w[i].boneIndex0) ? w[i].weight0 : 0f) + (headBones.Contains(w[i].boneIndex1) ? w[i].weight1 : 0f)
                + (headBones.Contains(w[i].boneIndex2) ? w[i].weight2 : 0f) + (headBones.Contains(w[i].boneIndex3) ? w[i].weight3 : 0f) > 0.5f;
            int[] tris = baked.triangles;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                if (!InHead(tris[k]) || !InHead(tris[k + 1]) || !InHead(tris[k + 2])) continue;
                for (int j = 0; j < 3; j++)
                {
                    int i = tris[k + j];
                    if (!map.TryGetValue(i, out int n)) { n = map[i] = v.Count; v.Add(verts[i]); u.Add(uv[i]); }
                    t.Add(n);
                }
            }
            if (t.Count < 30) return null;
            float minY = float.MaxValue, maxY = float.MinValue;
            Vector3 sum = Vector3.zero;
            foreach (var p in v) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); sum += p; }
            Vector3 c = sum / v.Count;
            float h = Mathf.Max(0.01f, maxY - minY);
            Quaternion facing = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(go.transform.forward, Vector3.up)));
            for (int i = 0; i < v.Count; i++) v[i] = facing * (v[i] - new Vector3(c.x, minY, c.z)) / h;
            var mesh = new Mesh { name = prefab.name + "_Tete" };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetUVs(0, u);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        finally { Object.DestroyImmediate(go); }
    }

    // Copie lisible d'une texture importée (non lisible) : passage par une RenderTexture.
    public static Texture2D Readable(Texture source)
    {
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    // Noircit, dans la texture, les points de la face avant compris dans le disque de chaque œil (vu de devant) : chaque
    // pixel des triangles proches retrouve sa position 3D, d'où des yeux ronds et nets, quelle que soit la finesse du maillage.
    static void PaintEyes(Mesh head, Texture2D tex, Vector2 left, Vector2 right, float radius)
    {
        Vector3[] v = head.vertices;
        Vector2[] uv = head.uv;
        int[] t = head.triangles;
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        bool InEye(Vector3 p) => p.z > 0f && (Vector2.Distance(new Vector2(p.x, p.y), left) <= radius || Vector2.Distance(new Vector2(p.x, p.y), right) <= radius);
        for (int k = 0; k + 2 < t.Length; k += 3)
        {
            Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]];
            Vector3 mid = (a + b + c) / 3f;
            float reach = radius + Mathf.Max((a - mid).magnitude, Mathf.Max((b - mid).magnitude, (c - mid).magnitude));
            if (Vector2.Distance(new Vector2(mid.x, mid.y), left) > reach && Vector2.Distance(new Vector2(mid.x, mid.y), right) > reach) continue;
            Vector2 A = new Vector2(uv[t[k]].x * w, uv[t[k]].y * h), B = new Vector2(uv[t[k + 1]].x * w, uv[t[k + 1]].y * h), C = new Vector2(uv[t[k + 2]].x * w, uv[t[k + 2]].y * h);
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-6f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))) - 1), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))) - 1), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float l1 = ((B.x - q.x) * (C.y - q.y) - (C.x - q.x) * (B.y - q.y)) / area;
                    float l2 = ((C.x - q.x) * (A.y - q.y) - (A.x - q.x) * (C.y - q.y)) / area;
                    float l3 = 1f - l1 - l2;
                    // Petite marge : les pixels des bords de triangle sont couverts aussi.
                    const float margin = -0.1f;
                    if (l1 < margin || l2 < margin || l3 < margin) continue;
                    if (InEye(a * l1 + b * l2 + c * l3)) px[y * w + x] = new Color32(4, 3, 3, 255);
                }
        }
        tex.SetPixels32(px);
        tex.Apply();
    }
}
