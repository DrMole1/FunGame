using System.IO;
using UnityEditor;
using UnityEngine;

// Ajout V0.6.2 du chapitre 4 : le couvercle de Pogo garde le maillage Meshy (plaque, rebord, rivets), mais la texture générée
// par Meshy ne respectait pas le dessin. L'image du couvercle est donc projetée à plat sur sa face avant, dès l'import.
public class PogoLidImport : AssetPostprocessor
{
    const string ModelName = "PogoCouvercle";
    // Le dessous du couvercle, visible boîte ouverte, reprend le rouge du rebord de l'image.
    static readonly Vector2 BackUv = new Vector2(0.5f, 0.985f);
    // Part de l'épaisseur, en partant de l'arrière, considérée comme le dessous du couvercle.
    const float BackFraction = 0.12f;
    const float WorkScale = 1000f;

    void OnPostprocessModel(GameObject root)
    {
        if (Path.GetFileNameWithoutExtension(assetPath) != ModelName) return;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            Bounds b = mesh.bounds;
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            int[] triangles = mesh.triangles;

            // Une partie de la surface Meshy est orientée vers le bas : invisible du dessus, elle laisse voir une couche interne
            // sombre. Ces triangles sont retournés, et les normales opposées à leurs faces recalculées. Le modèle est minuscule
            // avant mise à l'échelle : on travaille sur des positions agrandies, sinon les vecteurs seraient normalisés à zéro.
            var faceSum = new Vector3[vertices.Length];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                Vector3 face = Vector3.Cross((vertices[i1] - vertices[i0]) * WorkScale, (vertices[i2] - vertices[i0]) * WorkScale);
                float height = (vertices[i0].z + vertices[i1].z + vertices[i2].z) / 3f;
                if (face.z < 0f && height > b.center.z)
                {
                    triangles[t + 1] = i2;
                    triangles[t + 2] = i1;
                    face = -face;
                }
                faceSum[i0] += face;
                faceSum[i1] += face;
                faceSum[i2] += face;
            }

            float backLimit = b.min.z + b.size.z * BackFraction;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Vector3.Dot(normals[i], faceSum[i]) < 0f) normals[i] = faceSum[i].normalized;
                uv[i] = vertices[i].z < backLimit
                    ? BackUv
                    : new Vector2((b.max.x - vertices[i].x) / b.size.x, (vertices[i].y - b.min.y) / b.size.y);
            }
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uv;
        }
    }
}
