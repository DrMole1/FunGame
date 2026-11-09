using UnityEngine;

// Traces de sang au sol (GDD 3.4, notes 05 et 06, V0.6.7) : empreintes de pattes et gouttes qui s'effacent en 10 minutes. Un seul maillage,
// mis à jour par morceaux, pour tout le monde ; une fois plein, les plus anciennes traces laissent leur place.
public class BloodMarks : MonoBehaviour
{
    const int Capacity = 600;
    const float Lifetime = 600f, FadeRefresh = 2f, Lift = 0.02f, GroundProbe = 4f;
    static readonly Color Blood = new Color(0.32f, 0.015f, 0.02f, 0.92f);

    public enum Kind { Paw, Drop }

    static BloodMarks instance;

    Mesh mesh;
    readonly Vector3[] vertices = new Vector3[Capacity * 4];
    readonly Vector2[] uvs = new Vector2[Capacity * 4];
    readonly Color[] colors = new Color[Capacity * 4];
    readonly float[] born = new float[Capacity];
    int next;
    bool dirty;
    float fadeTimer;

    public static void Create(Transform parent)
    {
        var go = new GameObject("TracesDeSang");
        go.transform.SetParent(parent, false);
        var marks = go.AddComponent<BloodMarks>();
        instance = marks;
        marks.Build();
    }

    void Build()
    {
        var triangles = new int[Capacity * 6];
        for (int k = 0; k < Capacity; k++)
        {
            int t = k * 6, v = k * 4;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            born[k] = -Lifetime;
        }
        mesh = new Mesh { name = "TracesDeSang", vertices = vertices, uv = uvs, colors = colors, triangles = triangles };
        mesh.MarkDynamic();
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "TracesDeSang", mainTexture = BuildAtlas() };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // Nouvelle partie : plus aucune trace.
    public static void Clear()
    {
        if (instance == null) return;
        for (int k = 0; k < Capacity; k++) instance.born[k] = -Lifetime;
        System.Array.Clear(instance.colors, 0, instance.colors.Length);
        instance.dirty = true;
    }

    // 'position' : au niveau du corps ; la trace se pose sur le sol en dessous.
    public static void Stamp(Kind kind, Vector3 position, float yaw, float size)
    {
        if (instance == null) return;
        if (!Physics.Raycast(position, Vector3.down, out RaycastHit hit, GroundProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.6f) return;
        instance.Add(kind, hit.point + hit.normal * Lift, hit.normal, yaw, size);
    }

    void Add(Kind kind, Vector3 center, Vector3 normal, float yaw, float size)
    {
        Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward);
        Vector3 f = forward * (size * 0.5f), r = right * (size * 0.5f);
        int v = next * 4;
        vertices[v] = center - r - f;
        vertices[v + 1] = center - r + f;
        vertices[v + 2] = center + r + f;
        vertices[v + 3] = center + r - f;
        float u0 = kind == Kind.Paw ? 0f : 0.5f;
        uvs[v] = new Vector2(u0, 0f);
        uvs[v + 1] = new Vector2(u0, 1f);
        uvs[v + 2] = new Vector2(u0 + 0.5f, 1f);
        uvs[v + 3] = new Vector2(u0 + 0.5f, 0f);
        for (int c = 0; c < 4; c++) colors[v + c] = Blood;
        born[next] = Time.time;
        next = (next + 1) % Capacity;
        dirty = true;
    }

    void LateUpdate()
    {
        fadeTimer -= Time.unscaledDeltaTime;
        if (fadeTimer <= 0f)
        {
            fadeTimer = FadeRefresh;
            for (int k = 0; k < Capacity; k++)
            {
                float alpha = Blood.a * Mathf.Clamp01(1f - (Time.time - born[k]) / Lifetime);
                Color c = Blood;
                c.a = alpha;
                for (int n = 0; n < 4; n++) colors[k * 4 + n] = c;
            }
            dirty = true;
        }
        if (!dirty) return;
        dirty = false;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
    }

    // Moitié gauche : une empreinte de patte (coussinet et quatre doigts) ; moitié droite : une goutte éclaboussée.
    static Texture2D BuildAtlas()
    {
        const int cell = 128;
        var tex = new Texture2D(cell * 2, cell, TextureFormat.RGBA32, true) { name = "TracesDeSang", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[cell * 2 * cell];
        void Blob(float cx, float cy, float rx, float ry)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx - 2f)), x1 = Mathf.Min(cell * 2 - 1, Mathf.CeilToInt(cx + rx + 2f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 2f)), y1 = Mathf.Min(cell - 1, Mathf.CeilToInt(cy + ry + 2f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry, d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry) * 0.7f) * 255f);
                    if (a > pixels[y * cell * 2 + x].a) pixels[y * cell * 2 + x] = new Color32(255, 255, 255, a);
                }
        }
        // Patte.
        Blob(64f, 46f, 27f, 23f);
        Blob(32f, 72f, 11f, 15f);
        Blob(52f, 92f, 11f, 15f);
        Blob(76f, 92f, 11f, 15f);
        Blob(96f, 72f, 11f, 15f);
        // Goutte.
        var rnd = new System.Random(7);
        Blob(192f, 64f, 20f, 17f);
        for (int k = 0; k < 9; k++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = 22f + (float)rnd.NextDouble() * 22f, r = 2.5f + (float)rnd.NextDouble() * 6f;
            Blob(192f + Mathf.Cos(a) * d, 64f + Mathf.Sin(a) * d, r, r);
        }
        tex.SetPixels32(pixels);
        tex.Apply(true);
        return tex;
    }
}
