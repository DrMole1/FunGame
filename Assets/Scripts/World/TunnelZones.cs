using System.Collections.Generic;
using UnityEngine;

// V0.6.14 : règle générale, dans un tunnel la caméra passe à la première personne (OrbitCamera). Chaque tunnel s'enregistre
// ici comme un segment (axe du tube) et un rayon : les liaisons souterraines et aériennes des zones, le tunnel de la Control
// Room derrière l'Écran des Logs. Un tunnel disparaît avec son propriétaire (zone détruite, nouvelle partie).
public static class TunnelZones
{
    struct Tube { public Transform owner; public Vector3 a, b; public float radius; }

    static readonly List<Tube> tubes = new List<Tube>();

    public static void Register(Transform owner, Vector3 a, Vector3 b, float radius) =>
        tubes.Add(new Tube { owner = owner, a = a, b = b, radius = radius });

    // Le point (centre du personnage) est-il dans un tunnel ?
    public static bool Contains(Vector3 p)
    {
        for (int i = tubes.Count - 1; i >= 0; i--)
        {
            Tube t = tubes[i];
            if (t.owner == null)
            {
                tubes.RemoveAt(i);
                continue;
            }
            Vector3 ab = t.b - t.a;
            float k = Mathf.Clamp01(Vector3.Dot(p - t.a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            if ((t.a + ab * k - p).sqrMagnitude <= t.radius * t.radius) return true;
        }
        return false;
    }
}
