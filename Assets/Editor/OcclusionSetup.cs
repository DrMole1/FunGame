using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Mesure, pour chaque modèle humanoïde (personnages jouables, entités), le rayon des capsules qui découpent sa
// silhouette dans le lecteur YouTube de la Télévision : chaque sommet du maillage est rattaché au segment osseux le plus
// proche, et le rayon couvre 92 % de ses sommets (les mèches et pointes isolées sont ignorées), sans dépasser 2,5 fois
// le rayon par défaut (sommets égarés loin du corps).
public static class OcclusionSetup
{
    const string AssetPath = "Assets/Resources/OcclusionProfiles.asset";
    const float Coverage = 0.92f, MaxGrowth = 2.5f;

    public static void Generate()
    {
        var models = new List<GameObject>();
        var library = Object.FindObjectOfType<CharacterLibrary>();
        if (library != null)
            foreach (var entry in library.characters)
                if (entry != null && entry.model != null) models.Add(entry.model);
        foreach (string guid in AssetDatabase.FindAssets("t:CreatureArt"))
        {
            var art = AssetDatabase.LoadAssetAtPath<CreatureArt>(AssetDatabase.GUIDToAssetPath(guid));
            if (art != null && art.model != null) models.Add(art.model);
        }

        var profiles = new List<OcclusionProfiles.Profile>();
        var done = new HashSet<string>();
        foreach (var model in models)
        {
            var profile = Measure(model);
            if (profile != null && done.Add(profile.avatar)) profiles.Add(profile);
        }

        var asset = AssetDatabase.LoadAssetAtPath<OcclusionProfiles>(AssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<OcclusionProfiles>();
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        asset.profiles = profiles.ToArray();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log("Silhouettes de la Télévision mesurées : " + profiles.Count + " modèle(s).");
    }

    static OcclusionProfiles.Profile Measure(GameObject prefab)
    {
        var go = Object.Instantiate(prefab);
        try
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null) return null;
            var segments = new OcclusionProfiles.Segment[OcclusionProfiles.SegmentCount];
            if (!OcclusionProfiles.Build(animator, segments, out float size)) return null;

            var distances = new List<float>[segments.Length];
            for (int i = 0; i < distances.Length; i++) distances[i] = new List<float>();
            foreach (Vector3 v in Vertices(go))
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (!segments[i].valid) continue;
                    float d = OcclusionProfiles.Distance(v, segments[i].a, segments[i].b);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                if (best >= 0) distances[best].Add(bestDistance);
            }

            var radii = new float[segments.Length];
            for (int i = 0; i < radii.Length; i++)
            {
                if (!segments[i].valid) continue;
                var list = distances[i];
                float r = segments[i].defaultRadius;
                if (list.Count >= 8)
                {
                    list.Sort();
                    r = Mathf.Min(list[Mathf.Min(list.Count - 1, Mathf.FloorToInt(list.Count * Coverage))], r * MaxGrowth);
                }
                radii[i] = r / size;
            }
            return new OcclusionProfiles.Profile { avatar = animator.avatar.name, radii = radii };
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // Sommets en coordonnées monde : maillages skinnés dans leur pose actuelle, puis maillages simples.
    static IEnumerable<Vector3> Vertices(GameObject go)
    {
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            Matrix4x4 m = smr.transform.localToWorldMatrix;
            foreach (var v in baked.vertices) yield return m.MultiplyPoint3x4(v);
            Object.DestroyImmediate(baked);
        }
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) yield return m.MultiplyPoint3x4(v);
        }
    }
}
