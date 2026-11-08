using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Porte du chapiteau : le maillage Meshy garde son relief, mais l'image validée (bandes, arabesques, empreintes) est projetée à
// plat dès l'import, sur ses deux faces : la face arrière est inversée pour que le dessin se lise à l'endroit des deux côtés.
public class HubDoorImport : AssetPostprocessor
{
    const string ModelName = "PorteChapiteau";
    const float BackFraction = 0.25f;
    // Le modèle est minuscule avant mise à l'échelle : calculs sur des positions agrandies.
    const float WorkScale = 1000f;

    void OnPostprocessModel(GameObject root)
    {
        if (Path.GetFileNameWithoutExtension(assetPath) != ModelName) return;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>()) Project(filter.sharedMesh);
    }

    static void Project(Mesh mesh)
    {
        Bounds b = mesh.bounds;
        // Le panneau avant est vers le milieu de l'épaisseur : seul le bas de celle-ci est la face arrière.
        float backLimit = b.min.z + b.size.z * BackFraction;
        var vertices = new List<Vector3>(mesh.vertices);
        var normals = new List<Vector3>(mesh.normals);
        int[] triangles = mesh.triangles;

        Vector2 FrontUv(Vector3 p) => new Vector2(1f - (p.x - b.min.x) / b.size.x, (p.y - b.min.y) / b.size.y);
        Vector2 BackUv(Vector3 p) => new Vector2((p.x - b.min.x) / b.size.x, (p.y - b.min.y) / b.size.y);

        var uv = new List<Vector2>(vertices.Count);
        var isBack = new List<bool>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            bool back = vertices[i].z < backLimit || normals[i].z < -0.3f;
            isBack.Add(back);
            uv.Add(back ? BackUv(vertices[i]) : FrontUv(vertices[i]));
        }

        // Quelques triangles tournés vers l'arrière partagent des sommets avec l'avant (éclats déformés, normales tordues) :
        // ces sommets sont dédoublés, avec la projection et la normale de la face arrière.
        var duplicates = new Dictionary<int, int>();
        int BackVertex(int index)
        {
            if (isBack[index] && normals[index].z < -0.7f) return index;
            if (duplicates.TryGetValue(index, out int copy)) return copy;
            copy = vertices.Count;
            vertices.Add(vertices[index]);
            normals.Add(Vector3.back);
            uv.Add(BackUv(vertices[index]));
            isBack.Add(true);
            duplicates[index] = copy;
            return copy;
        }

        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]];
            Vector3 face = Vector3.Cross((vertices[triangles[t + 1]] - a) * WorkScale, (vertices[triangles[t + 2]] - a) * WorkScale);
            if (face.z >= -0.5f * face.magnitude) continue;
            for (int k = 0; k < 3; k++) triangles[t + k] = BackVertex(triangles[t + k]);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.triangles = triangles;
    }
}
