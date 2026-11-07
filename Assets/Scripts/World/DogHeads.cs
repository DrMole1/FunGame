using System.Collections.Generic;
using UnityEngine;

// Têtes de chiens du décor (Note 05 de la DA, V0.6) : prises sur les skins jouables (maillage figé de la tête), sans
// yeux (orbites noires), la peau remplacée par des pièces grisâtres cousues et déchirées, et mutilées : il manque la mâchoire, une oreille
// ou une partie du front, et des lambeaux sont arrachés. Les variantes sont calculées une fois, au premier besoin.
public static class DogHeads
{
    const int VariantsPerSkin = 3;

    class Variant
    {
        public Mesh mesh;
        public Material material;
        public Vector3[] eyes;     // orbites (repère de la tête, base au sol)
        public float height;
    }

    static List<Variant> variants;
    static Material socket;

    public static void Spawn(Transform parent, Vector3 surface, Vector3 normal, float size, System.Random rnd, bool onFloor)
    {
        if (variants == null) Build();
        if (variants.Count == 0) return;
        Variant v = variants[rnd.Next(variants.Count)];
        float spin = (float)rnd.NextDouble() * 360f;
        float tilt = (float)rnd.NextDouble() * 50f - 25f;

        var go = new GameObject("TeteDeChien");
        go.transform.SetParent(parent, false);
        // Au sol : couchée sur le côté, à moitié enfoncée ; au mur : le cou sort du mur, le museau vers la pièce.
        Quaternion rot = onFloor
            ? Quaternion.Euler(0f, spin, 75f + tilt)
            : Quaternion.LookRotation(Vector3.up, normal) * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, tilt, tilt * 0.5f);
        float scale = size / Mathf.Max(0.05f, v.height);
        go.transform.SetPositionAndRotation(surface + (onFloor ? Vector3.down * size * 0.15f : normal * -size * 0.1f), rot);
        go.transform.localScale = Vector3.one * scale;

        var model = new GameObject("Tete");
        model.transform.SetParent(go.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = v.mesh;
        var r = model.AddComponent<MeshRenderer>();
        r.sharedMaterial = v.material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        foreach (Vector3 eye in v.eyes)
        {
            var hole = GameArt.Prim(PrimitiveType.Sphere, go.transform, eye, Vector3.one * v.height * 0.13f, socket);
            hole.name = "Orbite";
        }
        var col = go.AddComponent<SphereCollider>();
        col.center = Vector3.up * v.height * 0.5f;
        col.radius = v.height * 0.4f;
    }

    static void Build()
    {
        variants = new List<Variant>();
        socket = GameArt.Lit("OrbiteVide", new Color(0.01f, 0.005f, 0.005f), null, 0.9f);
        var library = Object.FindObjectOfType<CharacterLibrary>(true);
        if (library == null) return;
        var rnd = new System.Random(4242);
        foreach (var entry in library.characters)
        {
            if (entry == null || entry.model == null) continue;
            var parts = Extract(entry.model);
            if (parts.vertices == null) continue;
            for (int k = 0; k < VariantsPerSkin; k++)
                variants.Add(Mutilate(parts, rnd, entry.name + k));
        }
    }

    // Tête du skin : maillage figé (pose de repos), triangles rattachés aux os de la tête (sinon, le haut du modèle).
    struct HeadParts
    {
        public Vector3[] vertices;
        public Vector2[] uv;
        public int[] triangles;
        public Vector3[] eyes;
        public float height;
    }

    static HeadParts Extract(GameObject prefab)
    {
        var parts = new HeadParts();
        var go = Object.Instantiate(prefab);
        go.SetActive(true);
        try
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return parts;
            var animator = go.GetComponentInChildren<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            Vector3[] verts = baked.vertices;
            Vector2[] uv = baked.uv;
            int[] tris = baked.triangles;
            Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
            // Mise à l'échelle ignorée par BakeMesh : on repasse dans le repère du monde.
            for (int i = 0; i < verts.Length; i++) verts[i] = toWorld.MultiplyPoint3x4(verts[i]);

            var inHead = new bool[verts.Length];
            var weights = smr.sharedMesh.boneWeights;
            Transform[] bones = smr.bones;
            if (head != null && weights.Length == verts.Length)
            {
                var headBones = new HashSet<int>();
                for (int b = 0; b < bones.Length; b++) if (bones[b] != null && bones[b].IsChildOf(head)) headBones.Add(b);
                for (int i = 0; i < verts.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float h = (headBones.Contains(w.boneIndex0) ? w.weight0 : 0f) + (headBones.Contains(w.boneIndex1) ? w.weight1 : 0f)
                            + (headBones.Contains(w.boneIndex2) ? w.weight2 : 0f) + (headBones.Contains(w.boneIndex3) ? w.weight3 : 0f);
                    inHead[i] = h > 0.5f;
                }
            }
            else
            {
                float top = float.MinValue, bottom = float.MaxValue;
                foreach (var v in verts) { top = Mathf.Max(top, v.y); bottom = Mathf.Min(bottom, v.y); }
                float cut = Mathf.Lerp(bottom, top, 0.82f);
                for (int i = 0; i < verts.Length; i++) inHead[i] = verts[i].y > cut;
            }

            var keep = new List<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
                if (inHead[tris[t]] && inHead[tris[t + 1]] && inHead[tris[t + 2]]) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            if (keep.Count < 30) return parts;

            // Repère de la tête : base (bas du cou) à l'origine, museau vers +Z (l'avant du personnage).
            float minY = float.MaxValue, maxY = float.MinValue;
            Vector3 sum = Vector3.zero;
            foreach (int i in keep) { minY = Mathf.Min(minY, verts[i].y); maxY = Mathf.Max(maxY, verts[i].y); sum += verts[i]; }
            Vector3 center = sum / keep.Count;
            Vector3 origin = new Vector3(center.x, minY, center.z);
            Quaternion facing = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(go.transform.forward, Vector3.up)));
            for (int i = 0; i < verts.Length; i++) verts[i] = facing * (verts[i] - origin);

            parts.vertices = verts;
            parts.uv = uv;
            parts.triangles = keep.ToArray();
            parts.height = Mathf.Max(0.05f, maxY - minY);
            var eyes = new List<Vector3>();
            if (animator != null && animator.isHuman)
                foreach (var bone in new[] { HumanBodyBones.LeftEye, HumanBodyBones.RightEye })
                {
                    Transform eye = animator.GetBoneTransform(bone);
                    if (eye != null) eyes.Add(facing * (eye.position - origin) + Vector3.forward * parts.height * 0.04f);
                }
            if (eyes.Count == 0)
            {
                // Sans os des yeux : à l'avant du crâne, aux deux tiers de la hauteur.
                float front = float.MinValue;
                foreach (int i in keep) front = Mathf.Max(front, verts[i].z);
                foreach (float side in new[] { -1f, 1f })
                    eyes.Add(new Vector3(side * parts.height * 0.16f, parts.height * 0.62f, front - parts.height * 0.12f));
            }
            parts.eyes = eyes.ToArray();
            return parts;
        }
        finally
        {
            Object.Destroy(go);
        }
    }

    // Mutilation : une partie de la tête retirée (mâchoire, oreille, front), des lambeaux arrachés au hasard, et une peau
    // rapiécée (pièces cousues, posées sur le dépliage du skin).
    static Variant Mutilate(HeadParts parts, System.Random rnd, string name)
    {
        float h = parts.height;
        Vector3 cutPoint, cutNormal;
        switch (rnd.Next(3))
        {
            case 0: cutPoint = new Vector3(0f, h * 0.3f, h * 0.15f); cutNormal = new Vector3(0f, -1f, 0.6f).normalized; break;   // mâchoire
            case 1: float side = rnd.Next(2) == 0 ? -1f : 1f; cutPoint = new Vector3(side * h * 0.22f, h * 0.75f, 0f); cutNormal = new Vector3(side, 0.5f, 0f).normalized; break; // oreille
            default: cutPoint = new Vector3(0f, h * 0.82f, h * 0.1f); cutNormal = new Vector3(0f, 0.8f, 0.6f).normalized; break; // front
        }
        var holes = new List<(Vector3 c, float r)>();
        for (int k = 0, n = 2 + rnd.Next(3); k < n; k++)
        {
            int t = parts.triangles[rnd.Next(parts.triangles.Length / 3) * 3];
            holes.Add((parts.vertices[t], h * (0.06f + (float)rnd.NextDouble() * 0.08f)));
        }
        var tris = new List<int>();
        for (int t = 0; t + 2 < parts.triangles.Length; t += 3)
        {
            Vector3 c = (parts.vertices[parts.triangles[t]] + parts.vertices[parts.triangles[t + 1]] + parts.vertices[parts.triangles[t + 2]]) / 3f;
            if (Vector3.Dot(c - cutPoint, cutNormal) > 0f) continue;
            if (holes.Exists(hole => (c - hole.c).sqrMagnitude < hole.r * hole.r)) continue;
            tris.Add(parts.triangles[t]); tris.Add(parts.triangles[t + 1]); tris.Add(parts.triangles[t + 2]);
        }
        var mesh = new Mesh { name = "TeteDeChien_" + name, indexFormat = parts.vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = parts.vertices;
        if (parts.uv != null && parts.uv.Length == parts.vertices.Length) mesh.uv = parts.uv;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mat = new Material(GameArt.Lit("TeteDeChienBase", Color.white, null, 0.25f)) { name = "PeauRapiecee_" + name };
        mat.mainTexture = Patchwork(rnd.Next());
        mat.color = new Color(0.62f, 0.6f, 0.58f);
        return new Variant { mesh = mesh, material = mat, eyes = parts.eyes, height = h };
    }

    // Peau rapiécée : pièces irrégulières de cuir et de tissu grisâtres (valeurs différentes), coutures sombres et points
    // de suture, déchirures noires.
    static Texture2D Patchwork(int seed)
    {
        const int size = 256;
        var rnd = new System.Random(seed);
        var points = new Vector2[22];
        var tones = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2((float)rnd.NextDouble() * size, (float)rnd.NextDouble() * size);
            tones[i] = 0.35f + (float)rnd.NextDouble() * 0.5f;
        }
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Rapiecage" };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int best = 0;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int i = 0; i < points.Length; i++)
                {
                    float dx = Mathf.Abs(x - points[i].x), dy = Mathf.Abs(y - points[i].y);
                    dx = Mathf.Min(dx, size - dx); dy = Mathf.Min(dy, size - dy);
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; d1 = d; best = i; }
                    else if (d < d2) d2 = d;
                }
                float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                float v = tones[best] * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.07f + best, y * 0.07f));
                // Couture : liseré sombre, avec des points de suture réguliers en travers.
                if (edge < 2.2f) v *= 0.25f;
                else if (edge < 5f && ((x + y) / 4) % 3 == 0) v *= 0.45f;
                // Déchirures : quelques zones presque noires.
                if (Mathf.PerlinNoise(x * 0.03f + seed % 97, y * 0.03f) > 0.78f) v *= 0.12f;
                px[y * size + x] = new Color(v * 1.04f, v, v * 0.95f, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
