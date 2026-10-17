using System.Collections.Generic;
using UnityEngine;

// Construction de meshes low-poly : boîtes avec UV en coordonnées monde (texture continue d'un mur à l'autre),
// cônes, disques et sphères.
public class MeshBuilder
{
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> tris = new List<int>();

    public bool IsEmpty => verts.Count == 0;

    // Ajoute un triangle orienté vers 'facing' (l'ordre des sommets est corrigé si besoin).
    void Tri(int a, int b, int c, Vector3 facing)
    {
        Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (Vector3.Dot(n, facing) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else { tris.Add(a); tris.Add(c); tris.Add(b); }
    }

    int Vert(Vector3 p, Vector3 n, Vector2 uv)
    {
        verts.Add(p); normals.Add(n); uvs.Add(uv);
        return verts.Count - 1;
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int i0 = Vert(a, normal, ua), i1 = Vert(b, normal, ub), i2 = Vert(c, normal, uc), i3 = Vert(d, normal, ud);
        Tri(i0, i1, i2, normal);
        Tri(i0, i2, i3, normal);
    }

    // Quad dont les UV sont projetées depuis la position (1 unité de texture = 1/texScale mètres).
    public void WorldQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float texScale)
    {
        Quad(a, b, c, d, normal, Project(a, normal) * texScale, Project(b, normal) * texScale,
            Project(c, normal) * texScale, Project(d, normal) * texScale);
    }

    static Vector2 Project(Vector3 p, Vector3 n)
    {
        Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        if (an.y >= an.x && an.y >= an.z) return new Vector2(p.x, p.z);
        if (an.x >= an.z) return new Vector2(p.z, p.y);
        return new Vector2(p.x, p.y);
    }

    // Boîte alignée sur les axes. 'faces' : masque des faces à générer (bit 0..5 : +X -X +Y -Y +Z -Z).
    public void Box(Vector3 center, Vector3 size, float texScale, int faces = 63)
    {
        Vector3 e = size * 0.5f;
        for (int f = 0; f < 6; f++)
        {
            if ((faces & (1 << f)) == 0) continue;
            Vector3 n = f switch
            {
                0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.up,
                3 => Vector3.down, 4 => Vector3.forward, _ => Vector3.back,
            };
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(n, u);
            Vector3 c = center + Vector3.Scale(n, e);
            Vector3 eu = Vector3.Scale(u, e), ev = Vector3.Scale(v, e);
            WorldQuad(c - eu - ev, c + eu - ev, c + eu + ev, c - eu + ev, n, texScale);
        }
    }

    // Cône intérieur (toit de chapiteau) : rayures selon l'angle, faces tournées vers l'intérieur.
    public void InnerCone(Vector3 baseCenter, float radius, float height, int segments, float stripeRepeat)
    {
        Vector3 apex = baseCenter + Vector3.up * height;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 p0 = baseCenter + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * radius;
            Vector3 p1 = baseCenter + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * radius;
            Vector3 mid = (p0 + p1) * 0.5f;
            Vector3 inward = ((baseCenter - mid).normalized * height + Vector3.down * radius).normalized;
            float u0 = (float)i / segments * stripeRepeat, u1 = (float)(i + 1) / segments * stripeRepeat;
            int i0 = Vert(p0, inward, new Vector2(u0, 0f));
            int i1 = Vert(p1, inward, new Vector2(u1, 0f));
            int i2 = Vert(apex, inward, new Vector2((u0 + u1) * 0.5f, 1f));
            Tri(i0, i1, i2, inward);
        }
    }

    public void Disc(Vector3 center, float radius, int segments, float texScale)
    {
        int c = Vert(center, Vector3.up, new Vector2(center.x, center.z) * texScale);
        int first = -1, prev = -1;
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 p = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            int idx = i == segments ? first : Vert(p, Vector3.up, new Vector2(p.x, p.z) * texScale);
            if (i == 0) first = idx;
            if (prev >= 0) Tri(c, prev, idx, Vector3.up);
            prev = idx;
        }
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Sphère de rayon 0,5 à facettes ; l'avant (+Z) est en u = 0,75.
    public static Mesh UvSphere(int segments, int rings)
    {
        var mb = new MeshBuilder();
        for (int r = 0; r <= rings; r++)
        {
            float theta = Mathf.PI * r / rings;
            for (int s = 0; s <= segments; s++)
            {
                float phi = Mathf.PI * 2f * s / segments;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), -Mathf.Sin(theta) * Mathf.Sin(phi));
                mb.Vert(n * 0.5f, n, new Vector2((float)s / segments, 1f - (float)r / rings));
            }
        }
        int row = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                Vector3 outward = (mb.verts[a] + mb.verts[d]).normalized;
                if (r != 0) mb.Tri(a, c, b, outward);
                if (r != rings - 1) mb.Tri(b, c, d, outward);
            }
        return mb.Build("LowSphere");
    }
}
