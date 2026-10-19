using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Le maillage Meshy du Perso 3 a une fente dans le dos de la robe (une bande de faces manquantes entre
// les omoplates). À l'import, on y ajoute une pièce de tissu : une grille posée dans la même île UV que le dos
// de la robe, 4 mm sous la surface, avec position, normale et poids d'os interpolés depuis les sommets voisins.
// Le tartan continue ainsi sans raccord visible (la bande abîmée de la texture est repeinte dans
// Material_0_baseColor_Fixed.png).
public class Perso3MeshFix : AssetPostprocessor
{
    const string ModelPath = "Assets/Models/Characters/Perso3/Perso3_Rigged.fbx";

    // Île UV du dos de la robe et bande de la fente.
    static readonly Rect BackIsland = new Rect(0.26f, 0f, 0.2f, 0.2f);
    const float U0 = 0.272f, U1 = 0.442f, V0 = 0.046f, V1 = 0.096f;
    const int Columns = 20, Rows = 6;
    const float Inset = 0.004f;

    public override uint GetVersion() => 1;

    // Le maillage n'est modifiable pendant l'import que s'il est lisible.
    void OnPreprocessModel()
    {
        if (assetPath == ModelPath) ((ModelImporter)assetImporter).isReadable = true;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (assetPath != ModelPath) return;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.sharedMesh != null) Patch(smr.sharedMesh);
    }

    static void Patch(Mesh mesh)
    {
        var verts = new List<Vector3>(); mesh.GetVertices(verts);
        var normals = new List<Vector3>(); mesh.GetNormals(normals);
        var uvs = new List<Vector2>(); mesh.GetUVs(0, uvs);
        var weights = mesh.boneWeights.ToList();
        var tris = new List<int>(); mesh.GetTriangles(tris, 0);
        int first = verts.Count;

        // Sommets du dos de la robe (l'île UV contient aussi des bouts d'autres zones, d'où le filtre 3D).
        var back = Enumerable.Range(0, first).Where(i => BackIsland.Contains(uvs[i]) && normals[i].z < 0f
            && verts[i].z < 0.2f && verts[i].y > 0.7f && verts[i].y < 1.1f).ToList();
        if (back.Count < 6)
        {
            Debug.LogWarning("Perso3MeshFix : dos de la robe introuvable, maillage non corrigé.");
            return;
        }

        for (int r = 0; r <= Rows; r++)
            for (int c = 0; c <= Columns; c++)
            {
                var uv = new Vector2(Mathf.Lerp(U0, U1, c / (float)Columns), Mathf.Lerp(V0, V1, r / (float)Rows));
                // Les 3 voisins les plus proches de chaque côté de la fente, pondérés par l'inverse de la distance.
                var sorted = back.OrderBy(i => (uvs[i] - uv).sqrMagnitude).ToList();
                var near = sorted.Where(i => uvs[i].y >= uv.y).Take(3).Concat(sorted.Where(i => uvs[i].y < uv.y).Take(3));

                Vector3 pos = Vector3.zero, normal = Vector3.zero;
                float total = 0f;
                var bones = new Dictionary<int, float>();
                foreach (int i in near)
                {
                    float w = 1f / ((uvs[i] - uv).sqrMagnitude + 1e-7f);
                    total += w;
                    pos += verts[i] * w;
                    normal += normals[i] * w;
                    AddBone(bones, weights[i].boneIndex0, weights[i].weight0 * w);
                    AddBone(bones, weights[i].boneIndex1, weights[i].weight1 * w);
                    AddBone(bones, weights[i].boneIndex2, weights[i].weight2 * w);
                    AddBone(bones, weights[i].boneIndex3, weights[i].weight3 * w);
                }
                normal.Normalize();
                verts.Add(pos / total - normal * Inset);
                normals.Add(normal);
                uvs.Add(uv);
                weights.Add(ToBoneWeight(bones));
            }

        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                int a = first + r * (Columns + 1) + c, b = a + 1, d = a + Columns + 1, e = d + 1;
                tris.AddRange(new[] { a, d, b, b, d, e });
            }

        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.boneWeights = weights.ToArray();
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    static void AddBone(Dictionary<int, float> bones, int index, float weight)
    {
        if (weight <= 0f) return;
        bones.TryGetValue(index, out float current);
        bones[index] = current + weight;
    }

    // Garde les 4 os les plus influents, poids renormalisés.
    static BoneWeight ToBoneWeight(Dictionary<int, float> bones)
    {
        var top = bones.OrderByDescending(kv => kv.Value).Take(4).ToArray();
        float sum = top.Sum(kv => kv.Value);
        var bw = new BoneWeight();
        if (top.Length > 0) { bw.boneIndex0 = top[0].Key; bw.weight0 = top[0].Value / sum; }
        if (top.Length > 1) { bw.boneIndex1 = top[1].Key; bw.weight1 = top[1].Value / sum; }
        if (top.Length > 2) { bw.boneIndex2 = top[2].Key; bw.weight2 = top[2].Value / sum; }
        if (top.Length > 3) { bw.boneIndex3 = top[3].Key; bw.weight3 = top[3].Value / sum; }
        return bw;
    }
}
