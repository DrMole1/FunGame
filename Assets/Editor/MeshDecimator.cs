using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Réduit le nombre de triangles d'un maillage (fusion d'arêtes guidée par l'erreur quadrique). Le travail se fait sur la forme
// réelle (sommets de même position soudés) : les coordonnées de texture sont gardées par coin de triangle, et interpolées seulement
// hors des coutures, pour que le dessin reste en place. Les bords réellement ouverts ne bougent pas.
public static class MeshDecimator
{
    const int MaxIterations = 100;
    const double Aggressiveness = 7.0;
    // Fusions refusées là où trop de triangles se rejoignent (coût quadratique), et durée maximale par maillage.
    const int MaxValence = 64;
    const long MaxMilliseconds = 25000;
    const int WeldOffset = 1 << 20;
    // Sommets soudés à 1/100 000 de la taille du modèle ; coordonnées de texture comparées à 1/4096 près.
    const float WeldGrid = 100000f, UvGrid = 4096f, UvEpsilon = 1e-7f, WorkScale = 100f;

    struct Quadric
    {
        public double a, b, c, d, e, f, g, h, i, j;

        public static Quadric FromPlane(double x, double y, double z, double w) => new Quadric
        {
            a = x * x, b = x * y, c = x * z, d = x * w, e = y * y, f = y * z, g = y * w, h = z * z, i = z * w, j = w * w,
        };

        public static Quadric operator +(Quadric p, Quadric q) => new Quadric
        {
            a = p.a + q.a, b = p.b + q.b, c = p.c + q.c, d = p.d + q.d, e = p.e + q.e,
            f = p.f + q.f, g = p.g + q.g, h = p.h + q.h, i = p.i + q.i, j = p.j + q.j,
        };

        public double Error(Vector3 v) =>
            a * v.x * v.x + 2 * b * v.x * v.y + 2 * c * v.x * v.z + 2 * d * v.x + e * v.y * v.y
            + 2 * f * v.y * v.z + 2 * g * v.y + h * v.z * v.z + 2 * i * v.z + j;
    }

    class Vert
    {
        public Vector3 position;
        public Vector2 uv;
        public Quadric quadric;
        public int refStart, refCount;
        public bool border, seam, hasUv;
    }

    class Tri
    {
        public readonly int[] v = new int[3];
        public readonly Vector2[] uv = new Vector2[3];
        public readonly double[] error = new double[4];
        public Vector3 normal;
        public bool deleted, dirty;
    }

    struct Ref
    {
        public int tri, corner;
    }

    public static void Simplify(Mesh mesh, int target)
    {
        if (!mesh.isReadable) return;
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        int[] indices = mesh.triangles;
        if (indices.Length / 3 <= target) return;
        bool hasUv = uvs != null && uvs.Length == positions.Length;
        bool hasNormals = normals != null && normals.Length == positions.Length;

        // Calculs sur un maillage ramené à l'unité : les seuils d'erreur ne dépendent plus de sa taille.
        Bounds bounds = mesh.bounds;
        float scale = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (scale <= 0f) return;
        Vector3 center = bounds.center;

        // Sommets soudés par position : les découpes de texture ne sont plus des bords.
        var verts = new List<Vert>(positions.Length / 2);
        var welded = new Dictionary<long, int>(positions.Length);
        var weldMap = new int[positions.Length];
        for (int k = 0; k < positions.Length; k++)
        {
            Vector3 p = (positions[k] - center) / scale;
            // Clé entière sur 63 bits : le hachage de Vector3Int provoque trop de collisions sur ces grilles (import de plusieurs minutes).
            long key = ((long)(Mathf.RoundToInt(p.x * WeldGrid) + WeldOffset) << 42)
                | ((long)(Mathf.RoundToInt(p.y * WeldGrid) + WeldOffset) << 21)
                | (long)(Mathf.RoundToInt(p.z * WeldGrid) + WeldOffset);
            if (!welded.TryGetValue(key, out int id))
            {
                id = welded[key] = verts.Count;
                verts.Add(new Vert { position = p });
            }
            weldMap[k] = id;
        }
        var tris = new List<Tri>(indices.Length / 3);
        for (int k = 0; k + 2 < indices.Length; k += 3)
        {
            var t = new Tri();
            for (int j = 0; j < 3; j++)
            {
                int source = indices[k + j];
                t.v[j] = weldMap[source];
                t.uv[j] = hasUv ? uvs[source] : Vector2.zero;
                Vert v = verts[t.v[j]];
                if (!v.hasUv)
                {
                    v.hasUv = true;
                    v.uv = t.uv[j];
                }
                else if ((v.uv - t.uv[j]).sqrMagnitude > UvEpsilon) v.seam = true;
            }
            tris.Add(t);
        }

        var refs = new List<Ref>(indices.Length);
        var flags0 = new List<bool>();
        var flags1 = new List<bool>();
        int removed = 0, startCount = tris.Count;
        // Garde-fou : un maillage pathologique ne doit jamais bloquer l'éditeur (le résultat obtenu à ce moment est conservé).
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int iteration = 0; iteration < MaxIterations && startCount - removed > target && watch.ElapsedMilliseconds < MaxMilliseconds; iteration++)
        {
            if (iteration % 5 == 0) UpdateMesh(verts, tris, refs, iteration, ref startCount, ref removed);
            foreach (var t in tris) t.dirty = false;
            double threshold = 1e-9 * System.Math.Pow(iteration + 3, Aggressiveness);
            for (int n = 0; n < tris.Count && startCount - removed > target; n++)
            {
                if ((n & 1023) == 0 && watch.ElapsedMilliseconds >= MaxMilliseconds) break;
                Tri t = tris[n];
                if (t.error[3] > threshold || t.deleted || t.dirty) continue;
                for (int j = 0; j < 3; j++)
                {
                    if (t.error[j] >= threshold) continue;
                    int i0 = t.v[j], i1 = t.v[(j + 1) % 3];
                    Vert v0 = verts[i0], v1 = verts[i1];
                    if (v0.border || v1.border || v0.refCount + v1.refCount > MaxValence) continue;
                    EdgeError(verts, i0, i1, out Vector3 p);
                    Resize(flags0, v0.refCount);
                    Resize(flags1, v1.refCount);
                    if (Flipped(verts, tris, refs, p, i1, v0, flags0) || Flipped(verts, tris, refs, p, i0, v1, flags1)) continue;

                    float along = AlongEdge(v0.position, v1.position, p);
                    v0.position = p;
                    v0.quadric += v1.quadric;
                    // Hors des coutures, le coin fusionné reçoit la texture interpolée ; sur une couture, chaque coin garde la sienne.
                    bool blend = !v0.seam && !v1.seam;
                    if (blend) v0.uv = Vector2.Lerp(v0.uv, v1.uv, along);
                    else v0.seam = true;
                    int start = refs.Count;
                    UpdateTriangles(verts, tris, refs, i0, v0, flags0, ref removed, blend);
                    UpdateTriangles(verts, tris, refs, i0, v1, flags1, ref removed, blend);
                    int count = refs.Count - start;
                    if (count <= v0.refCount)
                    {
                        for (int c = 0; c < count; c++) refs[v0.refStart + c] = refs[start + c];
                    }
                    else v0.refStart = start;
                    v0.refCount = count;
                    break;
                }
            }
        }
        Write(mesh, verts, tris, center, scale, hasUv, hasNormals);
    }

    // Vector3.normalized renvoie zéro sous 1e-5 : les tout petits triangles des modèles denses seraient tous refusés.
    static Vector3 Unit(Vector3 v)
    {
        double length = System.Math.Sqrt((double)v.x * v.x + (double)v.y * v.y + (double)v.z * v.z);
        return length > 0.0 ? new Vector3((float)(v.x / length), (float)(v.y / length), (float)(v.z / length)) : Vector3.zero;
    }

    static void Resize(List<bool> list, int size)
    {
        while (list.Count < size) list.Add(false);
    }

    static float AlongEdge(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 edge = b - a;
        float length = edge.sqrMagnitude;
        return length > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, edge) / length) : 0f;
    }

    static double Determinant(Quadric q, double a11, double a12, double a13, double a21, double a22, double a23, double a31, double a32, double a33) =>
        a11 * a22 * a33 + a13 * a21 * a32 + a12 * a23 * a31 - a13 * a22 * a31 - a11 * a23 * a32 - a12 * a21 * a33;

    static double EdgeError(List<Vert> verts, int id0, int id1, out Vector3 result)
    {
        Quadric q = verts[id0].quadric + verts[id1].quadric;
        double det = Determinant(q, q.a, q.b, q.c, q.b, q.e, q.f, q.c, q.f, q.h);
        if (System.Math.Abs(det) > 1e-14)
        {
            result = new Vector3(
                (float)(-1.0 / det * Determinant(q, q.b, q.c, q.d, q.e, q.f, q.g, q.f, q.h, q.i)),
                (float)(1.0 / det * Determinant(q, q.a, q.c, q.d, q.b, q.f, q.g, q.c, q.h, q.i)),
                (float)(-1.0 / det * Determinant(q, q.a, q.b, q.d, q.b, q.e, q.g, q.c, q.f, q.i)));
            return q.Error(result);
        }
        Vector3 p0 = verts[id0].position, p1 = verts[id1].position, mid = (p0 + p1) * 0.5f;
        double e0 = q.Error(p0), e1 = q.Error(p1), e2 = q.Error(mid);
        double best = System.Math.Min(e0, System.Math.Min(e1, e2));
        result = best == e0 ? p0 : best == e1 ? p1 : mid;
        return best;
    }

    // Vrai si déplacer le sommet en 'p' retournerait ou écraserait un de ses triangles.
    static bool Flipped(List<Vert> verts, List<Tri> tris, List<Ref> refs, Vector3 p, int other, Vert vert, List<bool> flags)
    {
        for (int k = 0; k < vert.refCount; k++)
        {
            Ref r = refs[vert.refStart + k];
            Tri t = tris[r.tri];
            if (t.deleted) continue;
            int id1 = t.v[(r.corner + 1) % 3], id2 = t.v[(r.corner + 2) % 3];
            if (id1 == other || id2 == other)
            {
                flags[k] = true;
                continue;
            }
            Vector3 d1 = Unit(verts[id1].position - p), d2 = Unit(verts[id2].position - p);
            if (Mathf.Abs(Vector3.Dot(d1, d2)) > 0.999f) return true;
            Vector3 n = Unit(Vector3.Cross(d1, d2));
            flags[k] = false;
            if (Vector3.Dot(n, t.normal) < 0.2f) return true;
        }
        return false;
    }

    static void UpdateTriangles(List<Vert> verts, List<Tri> tris, List<Ref> refs, int i0, Vert vert, List<bool> flags, ref int removed, bool blend)
    {
        for (int k = 0; k < vert.refCount; k++)
        {
            Ref r = refs[vert.refStart + k];
            Tri t = tris[r.tri];
            if (t.deleted) continue;
            if (flags[k])
            {
                t.deleted = true;
                removed++;
                continue;
            }
            t.v[r.corner] = i0;
            if (blend) t.uv[r.corner] = verts[i0].uv;
            t.dirty = true;
            ComputeErrors(verts, t);
            refs.Add(r);
        }
    }

    static void ComputeErrors(List<Vert> verts, Tri t)
    {
        t.error[0] = EdgeError(verts, t.v[0], t.v[1], out _);
        t.error[1] = EdgeError(verts, t.v[1], t.v[2], out _);
        t.error[2] = EdgeError(verts, t.v[2], t.v[0], out _);
        t.error[3] = System.Math.Min(t.error[0], System.Math.Min(t.error[1], t.error[2]));
    }

    static void UpdateMesh(List<Vert> verts, List<Tri> tris, List<Ref> refs, int iteration, ref int startCount, ref int removed)
    {
        if (iteration > 0)
        {
            tris.RemoveAll(t => t.deleted);
            startCount = tris.Count;
            removed = 0;
        }
        BuildRefs(verts, tris, refs);
        if (iteration != 0) return;

        foreach (var v in verts) v.quadric = default;
        foreach (var t in tris)
        {
            Vector3 p0 = verts[t.v[0]].position;
            Vector3 n = Unit(Vector3.Cross(verts[t.v[1]].position - p0, verts[t.v[2]].position - p0));
            t.normal = n;
            Quadric q = Quadric.FromPlane(n.x, n.y, n.z, -Vector3.Dot(n, p0));
            for (int j = 0; j < 3; j++) verts[t.v[j]].quadric += q;
        }
        foreach (var t in tris) ComputeErrors(verts, t);

        // Bord : arête portée par un seul triangle (bord réel ou couture de texture) : ses sommets ne bougent pas.
        foreach (var v in verts) v.border = false;
        var neighbours = new Dictionary<int, int>();
        foreach (var v in verts)
        {
            neighbours.Clear();
            for (int k = 0; k < v.refCount; k++)
            {
                Tri t = tris[refs[v.refStart + k].tri];
                for (int j = 0; j < 3; j++)
                {
                    int id = t.v[j];
                    neighbours.TryGetValue(id, out int seen);
                    neighbours[id] = seen + 1;
                }
            }
            foreach (var kv in neighbours)
                if (kv.Value == 1) verts[kv.Key].border = true;
        }
    }

    static void BuildRefs(List<Vert> verts, List<Tri> tris, List<Ref> refs)
    {
        foreach (var v in verts) v.refStart = v.refCount = 0;
        foreach (var t in tris)
            for (int j = 0; j < 3; j++) verts[t.v[j]].refCount++;
        int start = 0;
        foreach (var v in verts)
        {
            v.refStart = start;
            start += v.refCount;
            v.refCount = 0;
        }
        refs.Clear();
        for (int k = 0; k < start; k++) refs.Add(default);
        for (int n = 0; n < tris.Count; n++)
            for (int j = 0; j < 3; j++)
            {
                Vert v = verts[tris[n].v[j]];
                refs[v.refStart + v.refCount] = new Ref { tri = n, corner = j };
                v.refCount++;
            }
    }

    static void Write(Mesh mesh, List<Vert> verts, List<Tri> tris, Vector3 center, float scale, bool hasUv, bool hasNormals)
    {
        // Normales lissées sur la forme réelle : les coutures de texture ne se voient pas.
        var smooth = new Vector3[verts.Count];
        foreach (var t in tris)
        {
            if (t.deleted) continue;
            Vector3 a = verts[t.v[0]].position;
            Vector3 face = Vector3.Cross((verts[t.v[1]].position - a) * WorkScale, (verts[t.v[2]].position - a) * WorkScale);
            for (int j = 0; j < 3; j++) smooth[t.v[j]] += face;
        }
        var remap = new Dictionary<(int, Vector2Int), int>();
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        foreach (var t in tris)
        {
            if (t.deleted) continue;
            for (int j = 0; j < 3; j++)
            {
                var key = (t.v[j], new Vector2Int(Mathf.RoundToInt(t.uv[j].x * UvGrid), Mathf.RoundToInt(t.uv[j].y * UvGrid)));
                if (!remap.TryGetValue(key, out int id))
                {
                    id = remap[key] = positions.Count;
                    positions.Add(verts[t.v[j]].position * scale + center);
                    normals.Add(Unit(smooth[t.v[j]]));
                    uvs.Add(t.uv[j]);
                }
                indices.Add(id);
            }
        }
        mesh.Clear();
        mesh.indexFormat = positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(positions);
        if (hasNormals) mesh.SetNormals(normals);
        if (hasUv) mesh.SetUVs(0, uvs);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
    }

    // Têtes de chiens du décor : répétées dans tout le labyrinthe, elles sont ramenées au niveau de détail des plus légères.
    const int DogHeadTriangles = 4000;

    [MenuItem("FunGame/Alléger les têtes de chiens")]
    public static void SimplifyDogHeads()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Resources/Art/Decor/TetesChiens" }))
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            if (mesh == null || mesh.GetIndexCount(0) / 3 <= DogHeadTriangles) continue;
            Simplify(mesh, DogHeadTriangles);
            EditorUtility.SetDirty(mesh);
        }
        AssetDatabase.SaveAssets();
    }
}

// Modèles démesurés par rapport à leur taille à l'écran : allégés à l'import, le fichier d'origine reste intact.
public class HeavyModelImport : AssetPostprocessor
{
    public static readonly Dictionary<string, int> Targets = new Dictionary<string, int>
    {
        { "Trampoline", 8000 },
        { "MasseCavalier", 6000 },
    };

    // Le maillage doit être lisible à l'import pour être réduit.
    void OnPreprocessModel()
    {
        if (Targets.ContainsKey(Path.GetFileNameWithoutExtension(assetPath))) ((ModelImporter)assetImporter).isReadable = true;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!Targets.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out int target)) return;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            MeshDecimator.Simplify(filter.sharedMesh, target);
    }
}
