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
        // V0.7.21 (Note 05) : centre (x, z) de la base du cou, et l'encombrement (x, z) de la tête par tranche de hauteur.
        public Vector2 neck;
        public Rect[] slices;
    }

    const int Slices = 20;

    static List<Variant> variants;
    const float SizeFactor = 0.75f;

    public static void Spawn(Transform parent, Vector3 surface, Vector3 normal, float size, System.Random rnd, bool onFloor)
    {
        if (variants == null) Load();
        if (variants.Count == 0) return;
        // V0.6.1 : 25 % plus petites.
        size *= SizeFactor;
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

    // V0.7.19 (11.3) : tête droite, la base du cou en 'neck' (repère de 'parent'), le museau vers son avant (+Z) ; 'size' : sa hauteur.
    public static void SpawnUpright(Transform parent, Vector3 neck, float size, System.Random rnd)
    {
        if (variants == null) Load();
        if (variants.Count == 0) return;
        Variant v = variants[rnd.Next(variants.Count)];
        var go = new GameObject("TeteDeChien");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = neck;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * size;
        var model = new GameObject("Tete");
        model.transform.SetParent(go.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = v.mesh;
        var r = model.AddComponent<MeshRenderer>();
        r.sharedMaterial = v.material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // V0.7.21 (Note 05) : tête de chien posée sur un mannequin, pour chaque skin : la base de son cou (mesurée sur le modèle) sur le
    // cou du mannequin ('neck', repère de 'parent'), le museau vers +Z ; elle est assez grande pour que la tête du mannequin (la boîte
    // 'head', relative à 'neck') tienne tout entière dedans : rien du mannequin ne ressort plus de la tête (avant, la tête était
    // posée par son origine, que certains modèles ont loin du cou : celle de Snoopy était trop reculée et le mannequin ressortait
    // par son nez).
    // V0.7.23 (Note 05) : tête de chien de la taille de celle d'un personnage ('size' : sa hauteur ; avant, elle grossissait jusqu'à
    // couvrir la tête du mannequin et devenait énorme). Pour cacher la tête du mannequin ('head', relative à la base du cou) sans la
    // grossir, elle descend sur le cou, jusqu'à la couvrir ; on prend le modèle qui y parvient en descendant le moins.
    public static void SpawnOnMannequin(Transform parent, Vector3 neck, Bounds head, float size, System.Random rnd)
    {
        if (variants == null) Load();
        if (variants.Count == 0) return;
        int first = rnd.Next(variants.Count);
        Variant v = variants[first];
        float drop = -1f;
        for (int k = 0; k < variants.Count; k++)
        {
            Variant candidate = variants[(first + k) % variants.Count];
            float d = CoveringDrop(candidate, head, size);
            if (d < 0f || drop >= 0f && d >= drop - 0.001f) continue;
            v = candidate;
            drop = d;
            if (d < 0.001f) break;
        }
        if (drop < 0f) drop = size * MaxNeckDrop;
        var go = new GameObject("TeteDeChien");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = neck + Vector3.down * drop;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * size;
        var model = new GameObject("Tete");
        model.transform.SetParent(go.transform, false);
        model.transform.localPosition = new Vector3(-v.neck.x, 0f, -v.neck.y);
        model.AddComponent<MeshFilter>().sharedMesh = v.mesh;
        var r = model.AddComponent<MeshRenderer>();
        r.sharedMaterial = v.material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Jusqu'où (au plus 'MaxNeckDrop' de sa hauteur) la tête descend sur le cou pour couvrir la tête du mannequin (-1 : jamais).
    const float MaxNeckDrop = 0.45f;

    static float CoveringDrop(Variant v, Bounds head, float size)
    {
        for (float d = 0f; d <= size * MaxNeckDrop + 0.0001f; d += 0.01f)
        {
            var lifted = head;
            lifted.center += Vector3.up * d;
            if (Covers(v, lifted, size)) return d;
        }
        return -1f;
    }

    // La boîte 'head' (relative à la base du cou) tient dans la tête à l'échelle 'size' (tranche par tranche, avec 1,5 cm de marge).
    static bool Covers(Variant v, Bounds head, float size)
    {
        const float margin = 0.015f;
        for (float y = Mathf.Max(0f, head.min.y); y <= head.max.y + 0.001f; y += 0.025f)
        {
            int slice = Mathf.FloorToInt(y / size * Slices);
            if (slice < 0 || slice >= Slices) return false;
            Rect r = v.slices[slice];
            if (r.width <= 0f) return false;
            float x0 = (r.xMin - v.neck.x) * size, x1 = (r.xMax - v.neck.x) * size, z0 = (r.yMin - v.neck.y) * size, z1 = (r.yMax - v.neck.y) * size;
            if (x0 > head.min.x - margin || x1 < head.max.x + margin || z0 > head.min.z - margin || z1 < head.max.z + margin) return false;
        }
        return true;
    }

    static void Measure(Variant v)
    {
        Vector3[] pts = v.mesh.vertices;
        Bounds b = v.mesh.bounds;
        Vector2 neck = Vector2.zero;
        int count = 0;
        var min = new Vector2[Slices];
        var max = new Vector2[Slices];
        for (int k = 0; k < Slices; k++) { min[k] = Vector2.one * 99f; max[k] = -Vector2.one * 99f; }
        foreach (var p in pts)
        {
            if (p.y < b.min.y + 0.06f) { neck += new Vector2(p.x, p.z); count++; }
            int k = Mathf.Clamp(Mathf.FloorToInt((p.y - b.min.y) / Mathf.Max(0.001f, b.size.y) * Slices), 0, Slices - 1);
            min[k] = Vector2.Min(min[k], new Vector2(p.x, p.z));
            max[k] = Vector2.Max(max[k], new Vector2(p.x, p.z));
        }
        v.neck = count > 0 ? neck / count : Vector2.zero;
        v.slices = new Rect[Slices];
        for (int k = 0; k < Slices; k++) v.slices[k] = max[k].x >= min[k].x ? Rect.MinMaxRect(min[k].x, min[k].y, max[k].x, max[k].y) : new Rect();
    }

    static void Load()
    {
        variants = new List<Variant>();
        for (int k = 1; ; k++)
        {
            var mesh = Resources.Load<Mesh>("Art/Decor/TetesChiens/Tete" + k);
            if (mesh == null) break;
            var tex = Resources.Load<Texture2D>("Art/Decor/TetesChiens/Tete" + k);
            var variant = new Variant { mesh = mesh, material = GameArt.Lit("TeteDeChien" + k, Color.white, tex, 0.2f) };
            Measure(variant);
            variants.Add(variant);
        }
    }
}
