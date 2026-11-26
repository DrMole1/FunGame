using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Outil de vérification (éditeur, en Play) : faces de maillages générés qui se superposent dans une zone (même plan et même
// orientation : scintillement garanti ; à moins de 1 cm : scintillement de loin). Rapport texte, groupé par paire d'objets.
public static class StructureAudit
{
    struct Face { public Vector2 a, b, c, ua, ub, uc; public int owner, mat; public float coord; public int type; }

    public static string Run(string zoneName, int max = 60)
    {
        var zone = GameObject.Find(zoneName);
        if (zone == null) return zoneName + " absente";
        var faces = new List<Face>();
        var names = new List<string>();
        foreach (var mr in zone.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || !mr.enabled) continue;
            if (mr.sharedMaterial != null && mr.sharedMaterial.renderQueue >= 3000) continue;
            // Les blocs d'éclairage (nom_x_z) comptent comme leur pièce d'origine.
            string baseName = System.Text.RegularExpressions.Regex.Replace(mr.name, @"_-?\d+_-?\d+$", "");
            names.Add(baseName + "[" + (mr.sharedMaterial ? mr.sharedMaterial.name : "") + "]");
            int id = names.Count - 1;
            var v = mf.sharedMesh.vertices;
            var t = mf.sharedMesh.triangles;
            var uv = mf.sharedMesh.uv;
            bool hasUv = uv != null && uv.Length == v.Length;
            int mat = mr.sharedMaterial != null ? mr.sharedMaterial.GetInstanceID() : 0;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = mr.transform.TransformPoint(v[i]);
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 a = w[t[k]], b = w[t[k + 1]], c = w[t[k + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                float len = n.magnitude;
                if (len < 1e-5f) continue;
                n /= len;
                int axis = Mathf.Abs(n.x) > 0.999f ? 0 : Mathf.Abs(n.y) > 0.999f ? 1 : Mathf.Abs(n.z) > 0.999f ? 2 : -1;
                if (axis < 0) continue;
                int u = (axis + 1) % 3, vv = (axis + 2) % 3;
                faces.Add(new Face
                {
                    a = new Vector2(a[u], a[vv]), b = new Vector2(b[u], b[vv]), c = new Vector2(c[u], c[vv]),
                    ua = hasUv ? uv[t[k]] : Vector2.zero, ub = hasUv ? uv[t[k + 1]] : Vector2.zero, uc = hasUv ? uv[t[k + 2]] : Vector2.zero, mat = mat,
                    owner = id, coord = (a[axis] + b[axis] + c[axis]) / 3f, type = axis * 2 + (n[axis] > 0f ? 1 : 0),
                });
            }
        }
        // Par orientation et par plan (pas de 1 cm) : balayage selon x des rectangles englobants, triés.
        var planes = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < faces.Count; i++)
        {
            var key = (faces[i].type, Mathf.FloorToInt(faces[i].coord / 0.01f));
            if (!planes.TryGetValue(key, out var l)) planes[key] = l = new List<int>();
            l.Add(i);
        }
        var count = new Dictionary<string, int>();
        var sample = new Dictionary<string, string>();
        float Lo(int i) => Mathf.Min(faces[i].a.x, Mathf.Min(faces[i].b.x, faces[i].c.x));
        float Hi(int i) => Mathf.Max(faces[i].a.x, Mathf.Max(faces[i].b.x, faces[i].c.x));
        float LoY(int i) => Mathf.Min(faces[i].a.y, Mathf.Min(faces[i].b.y, faces[i].c.y));
        float HiY(int i) => Mathf.Max(faces[i].a.y, Mathf.Max(faces[i].b.y, faces[i].c.y));
        foreach (var kv in planes)
        {
            // Ce plan et le suivant (écart de moins de 1 cm).
            var list = new List<int>(kv.Value);
            if (planes.TryGetValue((kv.Key.Item1, kv.Key.Item2 + 1), out var next)) list.AddRange(next);
            int own = kv.Value.Count;
            var order = new List<int>();
            for (int k = 0; k < list.Count; k++) order.Add(k);
            order.Sort((p, q) => Lo(list[p]).CompareTo(Lo(list[q])));
            for (int oi = 0; oi < order.Count; oi++)
            {
                int ki = order[oi], i = list[ki];
                float hi = Hi(i);
                for (int oj = oi + 1; oj < order.Count; oj++)
                {
                    int kj = order[oj], j = list[kj];
                    if (Lo(j) >= hi - 0.01f) break;
                    // Chaque paire une seule fois : au moins une des deux faces dans ce plan-ci ; deux faces du plan suivant : ailleurs.
                    if (ki >= own && kj >= own) continue;
                    if (LoY(j) >= HiY(i) - 0.01f || LoY(i) >= HiY(j) - 0.01f) continue;
                    var fi = faces[i];
                    var fj = faces[j];
                    float gap = Mathf.Abs(fi.coord - fj.coord);
                    if (gap > 0.01f) continue;
                    if (!Overlap(fi, fj)) continue;
                    // Point commun ; même matériau et même texture à cet endroit : mêmes pixels, rien ne se voit.
                    Vector2 gi = (fi.a + fi.b + fi.c) / 3f, gj = (fj.a + fj.b + fj.c) / 3f;
                    Vector2 pt = Inside(fj, gi) ? gi : Inside(fi, gj) ? gj : (gi + gj) * 0.5f;
                    if (fi.mat == fj.mat)
                    {
                        Vector2 d = Uv(fi, pt) - Uv(fj, pt);
                        float dx = Mathf.Abs(d.x - Mathf.Round(d.x)), dy = Mathf.Abs(d.y - Mathf.Round(d.y));
                        if (dx < 0.01f && dy < 0.01f) continue;
                    }
                    // Enfouie dans un bloc solide (devant la face) : invisible.
                    int axis0 = fi.type / 2, u0 = (axis0 + 1) % 3, v0 = (axis0 + 2) % 3;
                    Vector3 wp = Vector3.zero;
                    wp[axis0] = fi.coord; wp[u0] = pt.x; wp[v0] = pt.y;
                    Vector3 nrm = Vector3.zero;
                    nrm[axis0] = fi.type % 2 == 1 ? 1f : -1f;
                    if (Physics.CheckSphere(wp + nrm * 0.06f, 0.03f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    string kind = gap < 0.0005f ? "COPLANAIRE" : "PROCHE (" + (gap * 1000f).ToString("F0") + " mm)";
                    string na = names[fi.owner], nb = names[fj.owner];
                    string key = kind + "  " + (string.CompareOrdinal(na, nb) <= 0 ? na + " | " + nb : nb + " | " + na);
                    count.TryGetValue(key, out int cnt);
                    count[key] = cnt + 1;
                    if (!sample.ContainsKey(key))
                    {
                        int axis = fi.type / 2, u = (axis + 1) % 3, vv = (axis + 2) % 3;
                        Vector2 g = (fi.a + fi.b + fi.c) / 3f;
                        Vector3 p = Vector3.zero;
                        p[axis] = fi.coord; p[u] = g.x; p[vv] = g.y;
                        sample[key] = p.ToString("F2") + "  A" + Extent(fi) + "  B" + Extent(fj);
                    }
                }
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine("== " + zoneName + " : " + names.Count + " maillages, " + faces.Count + " facettes alignées");
        var keys = new List<string>(count.Keys);
        keys.Sort((p, q) => count[q].CompareTo(count[p]));
        for (int k = 0; k < keys.Count && k < max; k++) sb.AppendLine(count[keys[k]] + "  " + keys[k] + "  @" + sample[keys[k]]);
        return sb.ToString();
    }

    // Étendue d'une facette dans le monde (min - max).
    static string Extent(Face f)
    {
        int axis = f.type / 2, u = (axis + 1) % 3, vv = (axis + 2) % 3;
        Vector2 lo = Vector2.Min(f.a, Vector2.Min(f.b, f.c)), hi = Vector2.Max(f.a, Vector2.Max(f.b, f.c));
        Vector3 a = Vector3.zero, b = Vector3.zero;
        a[axis] = b[axis] = f.coord;
        a[u] = lo.x; a[vv] = lo.y; b[u] = hi.x; b[vv] = hi.y;
        return "[" + a.ToString("F2") + " - " + b.ToString("F2") + (f.type % 2 == 1 ? " +" : " -") + "xyz"[axis] + "]";
    }

    static Vector3 Bary(Face f, Vector2 p)
    {
        Vector2 v0 = f.b - f.a, v1 = f.c - f.a, v2 = p - f.a;
        float d = v0.x * v1.y - v1.x * v0.y;
        if (Mathf.Abs(d) < 1e-9f) return new Vector3(1f, 0f, 0f);
        float l1 = (v2.x * v1.y - v1.x * v2.y) / d, l2 = (v0.x * v2.y - v2.x * v0.y) / d;
        return new Vector3(1f - l1 - l2, l1, l2);
    }

    static bool Inside(Face f, Vector2 p)
    {
        Vector3 b = Bary(f, p);
        return b.x >= 0f && b.y >= 0f && b.z >= 0f;
    }

    static Vector2 Uv(Face f, Vector2 p)
    {
        Vector3 b = Bary(f, p);
        return f.ua * b.x + f.ub * b.y + f.uc * b.z;
    }

    // Les deux triangles (réduits de 1 cm vers leur centre) se recouvrent-ils ? Séparation d'axes.
    static bool Overlap(Face p, Face q)
    {
        var P = Shrink(p.a, p.b, p.c);
        var Q = Shrink(q.a, q.b, q.c);
        return !Separated(P, Q) && !Separated(Q, P);
    }

    static Vector2[] Shrink(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 g = (a + b + c) / 3f;
        Vector2 S(Vector2 v) { var d = g - v; float m = d.magnitude; return m < 0.02f ? g : v + d / m * 0.01f; }
        return new[] { S(a), S(b), S(c) };
    }

    static bool Separated(Vector2[] T, Vector2[] U)
    {
        for (int e = 0; e < 3; e++)
        {
            Vector2 ed = T[(e + 1) % 3] - T[e];
            var ax = new Vector2(-ed.y, ed.x);
            float t0 = float.MaxValue, t1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
            foreach (var p in T) { float d = Vector2.Dot(p, ax); t0 = Mathf.Min(t0, d); t1 = Mathf.Max(t1, d); }
            foreach (var p in U) { float d = Vector2.Dot(p, ax); u0 = Mathf.Min(u0, d); u1 = Mathf.Max(u1, d); }
            if (t1 <= u0 || u1 <= t0) return true;
        }
        return false;
    }
}
