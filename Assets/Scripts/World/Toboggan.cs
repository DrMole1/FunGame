using System.Collections.Generic;
using UnityEngine;

// Toboggan (GDD 10.3, V0.6.6) : tube fermé au damier noir et blanc, en colimaçon serré ou légèrement courbé, du haut d'une
// plateforme jusqu'au sol de la salle. On n'y entre que par le haut.
public class Toboggan
{
    public const float Radius = 0.75f;
    const float ColliderSpacing = 1.5f, FirstCollider = 2.2f, SegmentLength = 0.85f;
    static Mesh troncon;
    static Vector3[] tronconVertices;
    static int[] tronconTriangles;

    public readonly List<Vector3> Path;
    public readonly Vector3 Exit;
    readonly float[] distances;
    public float Length => distances[distances.Length - 1];
    public Vector3 Entry => Path[0];

    public Toboggan(List<Vector3> path, Vector3 exit)
    {
        Path = path;
        Exit = exit;
        distances = new float[path.Count];
        for (int k = 1; k < path.Count; k++) distances[k] = distances[k - 1] + Vector3.Distance(path[k - 1], path[k]);
    }

    public Vector3 At(float distance)
    {
        int low = 0, high = distances.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (distances[mid] <= distance) low = mid;
            else high = mid;
        }
        float span = Mathf.Max(0.0001f, distances[high] - distances[low]);
        return Vector3.Lerp(Path[low], Path[high], Mathf.Clamp01((distance - distances[low]) / span));
    }

    // Le tronçon de tube (modèle Meshy) est répété le long du trajet et plié pour le suivre. Collisions sur tout le tube, sauf les
    // premiers mètres : l'entrée reste accessible.
    public void Build(Transform parent, Material material)
    {
        if (troncon == null) troncon = Resources.Load<Mesh>("Art/Decor/TobogganTroncon");
        if (tronconVertices == null) { tronconVertices = troncon.vertices; tronconTriangles = troncon.triangles; }
        var frames = new Vector3[Path.Count];
        Vector3 normal = Vector3.Cross(Tangent(0), Mathf.Abs(Tangent(0).y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        for (int k = 0; k < Path.Count; k++)
        {
            normal = Vector3.ProjectOnPlane(normal, Tangent(k)).normalized;
            frames[k] = normal;
        }

        var mb = new MeshBuilder();
        int segments = Mathf.Max(1, Mathf.RoundToInt(Length / SegmentLength));
        float step = Length / segments;
        var warped = new Vector3[tronconVertices.Length];
        var uvs = new Vector2[tronconVertices.Length];
        for (int s = 0; s < segments; s++)
        {
            for (int v = 0; v < tronconVertices.Length; v++)
            {
                Vector3 unit = tronconVertices[v];
                float d = (s + Mathf.Clamp01(unit.z)) * step;
                Frame(d, frames, out Vector3 center, out Vector3 n, out Vector3 b);
                warped[v] = center + (n * unit.x + b * unit.y) * Radius;
                // Damier : 4 cases autour du tube, une case tous les 1,2 m.
                float angle = Mathf.Atan2(unit.y, unit.x) / (Mathf.PI * 2f);
                uvs[v] = new Vector2((angle + 1f) % 1f * 2f, d / 2.4f);
            }
            for (int t = 0; t + 2 < tronconTriangles.Length; t += 3)
            {
                int i = tronconTriangles[t], j = tronconTriangles[t + 1], k = tronconTriangles[t + 2];
                Vector2 ua = uvs[i], ub = uvs[j], uc = uvs[k];
                // Un triangle à cheval sur la couture du tube : ses coordonnées restent continues.
                if (Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x)) - Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)) > 1f)
                {
                    if (ua.x < 1f) ua.x += 2f;
                    if (ub.x < 1f) ub.x += 2f;
                    if (uc.x < 1f) uc.x += 2f;
                }
                mb.Facet(warped[i], warped[j], warped[k], ua, ub, uc);
            }
        }
        var go = GameArt.MeshObject("Toboggan", parent, mb.Build("Toboggan"), material);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        for (float d = FirstCollider; d < Length; d += ColliderSpacing)
        {
            Vector3 a = At(d), b = At(Mathf.Min(Length, d + ColliderSpacing));
            Vector3 along = b - a;
            if (along.sqrMagnitude < 0.0001f) continue;
            var segment = new GameObject("Toboggan_Collision");
            segment.transform.SetParent(parent, false);
            segment.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(along));
            var capsule = segment.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = Radius;
            capsule.height = along.magnitude + Radius * 2f;
        }
    }

    // Centre du tube à cette distance, et deux directions perpendiculaires qui ne tournent pas brusquement le long du trajet.
    void Frame(float distance, Vector3[] frames, out Vector3 center, out Vector3 normal, out Vector3 binormal)
    {
        int low = 0, high = distances.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (distances[mid] <= distance) low = mid;
            else high = mid;
        }
        float t = Mathf.Clamp01((distance - distances[low]) / Mathf.Max(0.0001f, distances[high] - distances[low]));
        center = Vector3.Lerp(Path[low], Path[high], t);
        Vector3 tangent = Vector3.Lerp(Tangent(low), Tangent(high), t).normalized;
        normal = Vector3.ProjectOnPlane(Vector3.Lerp(frames[low], frames[high], t), tangent).normalized;
        binormal = Vector3.Cross(tangent, normal);
    }

    Vector3 Tangent(int k) => (Path[Mathf.Min(k + 1, Path.Count - 1)] - Path[Mathf.Max(k - 1, 0)]).normalized;
}
