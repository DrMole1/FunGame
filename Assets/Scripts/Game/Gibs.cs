using System.Collections.Generic;
using UnityEngine;

// Quota non atteint : le personnage explose. Ses maillages animés sont figés dans la pose du moment, puis découpés selon
// les os en six morceaux (tête, bras, jambes, torse), projetés de part et d'autre par la physique. Chaque plaie est un
// moignon sanglant ; les morceaux pissent le sang en volant et tachent le sol là où ils retombent.
public static class Gibs
{
    const float Lifetime = 40f, LegCutBelowHip = 0.08f;
    enum Part { Torso, Head, LeftArm, RightArm, LeftLeg, RightLeg }
    const int PartCount = 6;

    // Os racine de chaque membre (tout ce qui est en dessous lui appartient) ; le cou part avec la tête.
    static readonly (HumanBodyBones bone, Part part)[] Roots =
    {
        (HumanBodyBones.Neck, Part.Head), (HumanBodyBones.Head, Part.Head),
        (HumanBodyBones.LeftUpperArm, Part.LeftArm), (HumanBodyBones.RightUpperArm, Part.RightArm),
        (HumanBodyBones.LeftUpperLeg, Part.LeftLeg), (HumanBodyBones.RightUpperLeg, Part.RightLeg),
    };

    static Material blood, flesh;
    public static Material Blood => blood != null ? blood : (blood = GameArt.Lit("SangGibs", new Color(0.35f, 0.01f, 0.015f), null, 0.85f));
    static Material Flesh => flesh != null ? flesh : (flesh = GameArt.Lit("ChairGibs", new Color(0.45f, 0.04f, 0.05f), null, 0.7f));

    class Piece
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Material> materials = new List<Material>();
        public readonly List<List<int>> triangles = new List<List<int>>();
        public readonly List<(Vector3 at, float size)> wounds = new List<(Vector3, float)>();
    }

    // 'center' : centre de l'explosion (le milieu du corps). Rend false si le modèle n'a pas pu être découpé.
    public static bool Spawn(GameObject model, Animator animator, Vector3 center)
    {
        var groups = BoneGroups(animator);
        var pieces = new Piece[PartCount];
        for (int i = 0; i < PartCount; i++) pieces[i] = new Piece();
        // Les jambes partent sous la hanche : le bas de la chemise, pondéré sur les cuisses, reste au torse.
        float legCut = float.MaxValue;
        if (animator != null && animator.isHuman)
            foreach (var b in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
            {
                Transform t = animator.GetBoneTransform(b);
                if (t != null) legCut = Mathf.Min(legCut, t.position.y - LegCutBelowHip);
            }
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.enabled && smr.sharedMesh != null) Split(smr, groups, legCut, pieces);
        AddWounds(animator, pieces);

        bool any = false;
        for (int i = 0; i < PartCount; i++)
            if (pieces[i].vertices.Count >= 3)
            {
                Launch(pieces[i], (Part)i, center);
                any = true;
            }
        return any;
    }

    static Dictionary<Transform, Part> BoneGroups(Animator animator)
    {
        var map = new Dictionary<Transform, Part>();
        if (animator == null || !animator.isHuman) return map;
        foreach (var (bone, part) in Roots)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t == null) continue;
            foreach (var child in t.GetComponentsInChildren<Transform>(true)) map[child] = part;
        }
        return map;
    }

    // Moignons : un à l'arrachement de chaque membre, et le même sur le torse.
    static void AddWounds(Animator animator, Piece[] pieces)
    {
        if (animator == null || !animator.isHuman) return;
        AddWound(animator, HumanBodyBones.Neck, Part.Head, 0.14f, pieces);
        AddWound(animator, HumanBodyBones.LeftUpperArm, Part.LeftArm, 0.12f, pieces);
        AddWound(animator, HumanBodyBones.RightUpperArm, Part.RightArm, 0.12f, pieces);
        AddWound(animator, HumanBodyBones.LeftUpperLeg, Part.LeftLeg, 0.17f, pieces);
        AddWound(animator, HumanBodyBones.RightUpperLeg, Part.RightLeg, 0.17f, pieces);
    }

    static void AddWound(Animator animator, HumanBodyBones bone, Part part, float size, Piece[] pieces)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null && bone == HumanBodyBones.Neck) t = animator.GetBoneTransform(HumanBodyBones.Head);
        if (t == null) return;
        // Les jambes sont coupées un peu sous la hanche.
        Vector3 at = part == Part.LeftLeg || part == Part.RightLeg ? t.position + Vector3.down * LegCutBelowHip : t.position;
        pieces[(int)part].wounds.Add((at, size));
        pieces[(int)Part.Torso].wounds.Add((at, size * 0.75f));
    }

    static void Split(SkinnedMeshRenderer smr, Dictionary<Transform, Part> groups, float legCut, Piece[] pieces)
    {
        Mesh source = smr.sharedMesh;
        BoneWeight[] weights = source.boneWeights;
        Transform[] bones = smr.bones;
        if (weights.Length != source.vertexCount || bones.Length == 0) return;

        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        Vector3[] verts = baked.vertices, normals = baked.normals;
        Vector2[] uv = source.uv;
        Object.Destroy(baked);
        Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

        // Partie de chaque sommet : celle de l'os qui pèse le plus.
        var vertexPart = new Part[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            int b = weights[i].boneIndex0;
            Part part = b >= 0 && b < bones.Length && bones[b] != null && groups.TryGetValue(bones[b], out var p) ? p : Part.Torso;
            if ((part == Part.LeftLeg || part == Part.RightLeg) && toWorld.MultiplyPoint3x4(verts[i]).y > legCut) part = Part.Torso;
            vertexPart[i] = part;
        }

        Material[] mats = smr.sharedMaterials;
        var remap = new Dictionary<int, int>[PartCount];
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var lists = new List<int>[PartCount];
            int[] t = source.GetTriangles(s);
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                Part a = vertexPart[t[k]], b = vertexPart[t[k + 1]], c = vertexPart[t[k + 2]];
                int part = (int)(b == c ? b : a);
                Piece piece = pieces[part];
                var map = remap[part] ?? (remap[part] = new Dictionary<int, int>());
                if (lists[part] == null)
                {
                    lists[part] = new List<int>();
                    piece.triangles.Add(lists[part]);
                    piece.materials.Add(s < mats.Length ? mats[s] : null);
                }
                for (int j = 0; j < 3; j++)
                {
                    int v = t[k + j];
                    if (!map.TryGetValue(v, out int n))
                    {
                        n = piece.vertices.Count;
                        map[v] = n;
                        piece.vertices.Add(toWorld.MultiplyPoint3x4(verts[v]));
                        piece.normals.Add(toWorld.MultiplyVector(v < normals.Length ? normals[v] : Vector3.up));
                        piece.uvs.Add(v < uv.Length ? uv[v] : Vector2.zero);
                    }
                    lists[part].Add(n);
                }
            }
        }
    }

    static void Launch(Piece piece, Part part, Vector3 center)
    {
        // Pivot au centre du morceau.
        var bounds = new Bounds(piece.vertices[0], Vector3.zero);
        foreach (var p in piece.vertices) bounds.Encapsulate(p);
        var local = new List<Vector3>(piece.vertices.Count);
        foreach (var p in piece.vertices) local.Add(p - bounds.center);
        var mesh = new Mesh { name = "Morceau" + part };
        if (local.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(local);
        mesh.SetNormals(piece.normals);
        mesh.SetUVs(0, piece.uvs);
        mesh.subMeshCount = piece.triangles.Count;
        for (int s = 0; s < piece.triangles.Count; s++) mesh.SetTriangles(piece.triangles[s], s);
        mesh.RecalculateBounds();

        var go = new GameObject("Morceau_" + part);
        go.layer = 2; // Ignore Raycast : ni la visée ni la caméra ne s'y accrochent
        go.transform.position = bounds.center;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = piece.materials.ToArray();
        // Le modèle est creux : un moignon de chair sanglante bouche chaque plaie.
        foreach (var (at, size) in piece.wounds)
        {
            var stump = GameArt.Prim(PrimitiveType.Sphere, go.transform, at - bounds.center, Vector3.one * size * 0.85f, Flesh);
            stump.layer = 2;
            var gore = GameArt.Prim(PrimitiveType.Sphere, stump.transform, Random.insideUnitSphere * 0.25f, Vector3.one * 0.7f, Blood);
            gore.layer = 2;
        }

        var box = go.AddComponent<BoxCollider>();
        box.size = Vector3.Max(bounds.size * 0.85f, Vector3.one * 0.08f);
        var rb = go.AddComponent<Rigidbody>();
        bool torso = part == Part.Torso;
        rb.mass = torso ? 20f : part == Part.Head ? 5f : 6f;
        rb.drag = 0.05f;
        rb.angularDrag = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Membres projetés loin de part et d'autre (et vers le haut), le torse plus lourdement.
        Vector3 away = bounds.center - center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0004f) away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
        Vector3 jitter = Random.insideUnitSphere * 0.35f;
        jitter.y = 0f;
        away = (away.normalized + jitter).normalized;
        rb.velocity = away * (torso ? Random.Range(1f, 2.5f) : Random.Range(5f, 9f)) + Vector3.up * (torso ? Random.Range(2f, 3.5f) : Random.Range(4.5f, 8f));
        rb.angularVelocity = Random.insideUnitSphere * (torso ? 6f : 18f);

        go.AddComponent<Gib>();
        foreach (var (at, _) in piece.wounds) Spurt(go.transform, at - bounds.center, torso ? 0.6f : 1f);
        Object.Destroy(go, Lifetime);
        Object.Destroy(mesh, Lifetime);
    }

    // Jet de sang qui part de la plaie et suit le morceau en vol.
    static void Spurt(Transform parent, Vector3 localPos, float amount)
    {
        var go = new GameObject("JetDeSang");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 1.8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.gravityModifier = 1.4f;
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(55f * amount, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        r.sharedMaterial = Blood;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    // Morceau en vol : il tache le sol aux premiers impacts.
    class Gib : MonoBehaviour
    {
        int stains;
        float lastStain = -1f;

        void OnCollisionEnter(Collision c)
        {
            if (stains >= 3 || Time.time - lastStain < 0.15f || c.relativeVelocity.sqrMagnitude < 2f) return;
            stains++;
            lastStain = Time.time;
            Vector3 v = c.relativeVelocity;
            float yaw = Mathf.Atan2(-v.x, -v.z) * Mathf.Rad2Deg;
            BloodMarks.Stamp(BloodMarks.Kind.Drop, transform.position + Vector3.up * 0.5f, yaw, Random.Range(0.45f, 0.75f));
        }
    }
}
