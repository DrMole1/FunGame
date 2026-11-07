using System.Collections.Generic;
using UnityEngine;

// Têtes de chiens du décor (Note 05 de la DA, V0.6) : la tête complète de chaque skin jouable, avec sa texture, les yeux
// noirs unis (ni pupille, ni blanc de l'œil). Préparées dans l'éditeur par "FunGame/Préparer les têtes de chiens"
// (Resources/Art/Decor/TetesChiens : maillage de hauteur 1, base du cou en 0, museau vers +Z, et sa texture).
public static class DogHeads
{
    class Variant
    {
        public Mesh mesh;
        public Material material;
    }

    static List<Variant> variants;

    public static void Spawn(Transform parent, Vector3 surface, Vector3 normal, float size, System.Random rnd, bool onFloor)
    {
        if (variants == null) Load();
        if (variants.Count == 0) return;
        Variant v = variants[rnd.Next(variants.Count)];
        float spin = (float)rnd.NextDouble() * 360f;
        float tilt = (float)rnd.NextDouble() * 40f - 20f;

        var go = new GameObject("TeteDeChien");
        go.transform.SetParent(parent, false);
        // Au sol : posée sur le côté, un peu enfoncée ; au mur : le cou sort du mur, le museau vers la pièce.
        Quaternion rot = onFloor
            ? Quaternion.Euler(0f, spin, 70f + tilt)
            : Quaternion.LookRotation(Vector3.up, normal) * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, tilt, tilt * 0.5f);
        go.transform.SetPositionAndRotation(surface + (onFloor ? Vector3.down * size * 0.12f : normal * -size * 0.1f), rot);
        go.transform.localScale = Vector3.one * size;

        var model = new GameObject("Tete");
        model.transform.SetParent(go.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = v.mesh;
        var r = model.AddComponent<MeshRenderer>();
        r.sharedMaterial = v.material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var col = go.AddComponent<SphereCollider>();
        col.center = Vector3.up * 0.5f;
        col.radius = 0.4f;
    }

    static void Load()
    {
        variants = new List<Variant>();
        for (int k = 1; ; k++)
        {
            var mesh = Resources.Load<Mesh>("Art/Decor/TetesChiens/Tete" + k);
            if (mesh == null) break;
            var tex = Resources.Load<Texture2D>("Art/Decor/TetesChiens/Tete" + k);
            variants.Add(new Variant { mesh = mesh, material = GameArt.Lit("TeteDeChien" + k, Color.white, tex, 0.2f) });
        }
    }
}
