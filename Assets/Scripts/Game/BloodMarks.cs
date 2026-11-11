using UnityEngine;

// Traces de sang au sol (GDD 3.4, notes 05 et 06, V0.6.7) : empreintes de pattes et gouttes qui s'effacent en 10 minutes. Un seul maillage,
// mis à jour par morceaux, pour tout le monde ; une fois plein, les plus anciennes traces laissent leur place.
// Les gouttes ont trois modèles (flaque éclaboussée, coulure dans le sens de la marche, éclaboussures en grappe), tirés au hasard.
public class BloodMarks : MonoBehaviour
{
    const int Capacity = 600;
    const float Lifetime = 600f, FadeRefresh = 2f, Lift = 0.02f, GroundProbe = 4f;
    // Empreintes : rouge sombre uni. Gouttes : sang frais plus vif, la texture l'ombre (bords séchés, centre épais).
    static readonly Color PawBlood = new Color(0.32f, 0.015f, 0.02f, 0.92f);
    static readonly Color DropBlood = new Color(0.5f, 0.012f, 0.02f, 0.97f);
    const int Cells = 4, DropVariants = 3, Cell = 256;

    // Pool : la flaque éclaboussée seule (mare de sang d'un personnage qui explose).
    public enum Kind { Paw, Drop, Pool }

    static BloodMarks instance;

    Mesh mesh;
    readonly Vector3[] vertices = new Vector3[Capacity * 4];
    readonly Vector2[] uvs = new Vector2[Capacity * 4];
    readonly Color[] colors = new Color[Capacity * 4];
    readonly float[] born = new float[Capacity];
    readonly bool[] paw = new bool[Capacity];
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
        // Case 0 : la patte ; cases 1 à 3 : les trois modèles de goutte.
        int cell = kind == Kind.Paw ? 0 : kind == Kind.Pool ? 1 : 1 + Random.Range(0, DropVariants);
        float u0 = cell / (float)Cells, u1 = (cell + 1) / (float)Cells;
        uvs[v] = new Vector2(u0, 0f);
        uvs[v + 1] = new Vector2(u0, 1f);
        uvs[v + 2] = new Vector2(u1, 1f);
        uvs[v + 3] = new Vector2(u1, 0f);
        paw[next] = kind == Kind.Paw;
        Color c = paw[next] ? PawBlood : DropBlood;
        for (int n = 0; n < 4; n++) colors[v + n] = c;
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
                Color c = paw[k] ? PawBlood : DropBlood;
                c.a *= Mathf.Clamp01(1f - (Time.time - born[k]) / Lifetime);
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

    // ================= Atlas =================

    // Quatre cases de 256 px : la patte (coussinet et quatre doigts), puis les trois gouttes.
    static Texture2D BuildAtlas()
    {
        var tex = new Texture2D(Cell * Cells, Cell, TextureFormat.RGBA32, true) { name = "TracesDeSang", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[Cell * Cells * Cell];

        // Patte : formes douces, teinte unie (inchangée).
        var field = new float[Cell * Cell];
        Splat s = new Splat(field, 0);
        s.Disc(128f, 92f, 54f, 46f);
        s.Disc(64f, 144f, 22f, 30f);
        s.Disc(104f, 184f, 22f, 30f);
        s.Disc(152f, 184f, 22f, 30f);
        s.Disc(192f, 144f, 22f, 30f);
        Write(pixels, 0, field, null);

        for (int variant = 0; variant < DropVariants; variant++)
        {
            field = new float[Cell * Cell];
            s = new Splat(field, 31 + variant * 17);
            switch (variant)
            {
                case 0: Pool(s); break;
                case 1: Drip(s); break;
                default: Cluster(s); break;
            }
            Write(pixels, variant + 1, field, Shade(field, s.seed));
        }
        tex.SetPixels32(pixels);
        tex.Apply(true);
        return tex;
    }

    // Flaque éclaboussée : une tache irrégulière, des lobes, des jets qui rayonnent et finissent en gouttelette, des
    // postillons tout autour.
    static void Pool(Splat s)
    {
        s.Blob(128f, 128f, 44f, 0.3f);
        for (int k = 0; k < 6; k++)
        {
            float a = s.Angle(), d = s.Range(26f, 44f);
            s.Blob(128f + Mathf.Cos(a) * d, 128f + Mathf.Sin(a) * d, s.Range(12f, 22f), 0.35f);
        }
        // Jets courts et irréguliers, chacun terminé par une gouttelette un peu plus loin.
        for (int k = 0; k < 6; k++)
        {
            float a = s.Angle(), len = s.Range(30f, 62f);
            float x, y;
            s.Streak(128f + Mathf.Cos(a) * 42f, 128f + Mathf.Sin(a) * 42f, a, len, s.Range(7f, 11f), 1.5f, s.Range(-0.012f, 0.012f), out x, out y);
            if (s.Range(0f, 1f) < 0.7f) s.Blob(x + Mathf.Cos(a) * s.Range(5f, 12f), y + Mathf.Sin(a) * s.Range(5f, 12f), s.Range(2.5f, 5.5f), 0.2f);
        }
        s.Specks(128f, 128f, 62f, 120f, 30, 1f, 4f, 0f, Mathf.PI * 2f);
    }

    // Coulure : la goutte s'écrase puis s'étire en une traînée qui s'affine dans le sens de la marche (vers +V, l'avant du
    // personnage), suivie de gouttelettes de plus en plus petites.
    static void Drip(Splat s)
    {
        s.Blob(128f, 62f, 34f, 0.3f);
        s.Blob(112f, 52f, 18f, 0.3f);
        s.Blob(146f, 74f, 16f, 0.3f);
        float x, y;
        s.Streak(128f, 70f, Mathf.PI * 0.5f, 120f, 22f, 3f, 0.004f, out x, out y);
        s.Streak(116f, 60f, Mathf.PI * 0.62f, 42f, 8f, 1.5f, -0.01f, out _, out _);
        float px = x, py = y, r = 6f;
        for (int k = 0; k < 4; k++)
        {
            py += r * 2.4f + s.Range(2f, 6f);
            px += s.Range(-4f, 4f);
            if (py > 246f) break;
            s.Blob(px, py, r, 0.2f);
            r *= 0.7f;
        }
        s.Specks(128f, 120f, 30f, 120f, 26, 1f, 3.5f, Mathf.PI * 0.25f, Mathf.PI * 0.75f);
        s.Specks(128f, 62f, 42f, 62f, 14, 1f, 3f, 0f, Mathf.PI * 2f);
    }

    // Éclaboussures en grappe : plusieurs taches reliées par des traînées, une pluie de petites gouttes.
    static void Cluster(Splat s)
    {
        s.Blob(118f, 132f, 30f, 0.35f);
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f + s.Range(-0.5f, 0.5f), d = s.Range(52f, 82f);
            float tx = 118f + Mathf.Cos(a) * d, ty = 132f + Mathf.Sin(a) * d;
            s.Streak(118f + Mathf.Cos(a) * 20f, 132f + Mathf.Sin(a) * 20f, a, d - 20f, s.Range(7f, 10f), s.Range(4f, 6f), s.Range(-0.01f, 0.01f), out _, out _);
            s.Blob(tx, ty, s.Range(11f, 20f), 0.35f);
        }
        for (int k = 0; k < 6; k++)
        {
            float a = s.Angle(), d = s.Range(70f, 105f);
            s.Blob(118f + Mathf.Cos(a) * d, 132f + Mathf.Sin(a) * d, s.Range(3f, 7f), 0.25f);
        }
        s.Specks(118f, 132f, 45f, 115f, 45, 0.9f, 3.2f, 0f, Mathf.PI * 2f);
    }

    // Ombrage d'une goutte : le centre épais est plus clair et brillant, le bord séché plus sombre, de légères marbrures.
    static float[] Shade(float[] field, int seed)
    {
        float[] thick = Blur(Blur(field, 7), 7);
        var shade = new float[field.Length];
        float ox = seed * 3.7f, oy = seed * 1.3f;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                int i = y * Cell + x;
                float t = thick[i];
                float rim = Mathf.Clamp01(field[i] - t * 1.15f) * 1.6f;              // bord fin : sang séché, plus sombre
                float mottle = Mathf.PerlinNoise(x * 0.045f + ox, y * 0.045f + oy) * 0.6f + Mathf.PerlinNoise(x * 0.16f + oy, y * 0.16f + ox) * 0.4f;
                // Plaques coagulées, plus sombres, dans les parties épaisses.
                float clot = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.07f + oy + 20f, y * 0.07f + ox) - 0.58f) * 5f) * t;
                float lum = 0.55f + 0.4f * t + 0.22f * (mottle - 0.5f) - 0.35f * rim - 0.3f * clot;
                // Reflet humide sur les parties épaisses.
                float wet = Mathf.Clamp01((t - 0.75f) * 4f) * Mathf.Clamp01((Mathf.PerlinNoise(x * 0.03f + 50f + ox, y * 0.03f) - 0.55f) * 6f);
                shade[i] = Mathf.Clamp01(lum + 0.25f * wet);
            }
        return shade;
    }

    static float[] Blur(float[] src, int radius)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        float norm = 1f / (radius * 2 + 1);
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += src[y * Cell + Mathf.Clamp(x + k, 0, Cell - 1)];
                tmp[y * Cell + x] = sum * norm;
            }
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += tmp[Mathf.Clamp(y + k, 0, Cell - 1) * Cell + x];
                dst[y * Cell + x] = sum * norm;
            }
        return dst;
    }

    static void Write(Color32[] pixels, int cell, float[] field, float[] shade)
    {
        int width = Cell * Cells;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                int i = y * Cell + x;
                // Une marge transparente évite que les mipmaps mélangent les cases.
                bool edge = x < 2 || y < 2 || x >= Cell - 2 || y >= Cell - 2;
                float a = edge ? 0f : field[i];
                byte l = (byte)(255f * (shade == null ? 1f : shade[i]));
                pixels[y * width + cell * Cell + x] = new Color32(l, l, l, (byte)(Mathf.Clamp01(a) * 255f));
            }
    }

    // Champ d'opacité d'une case : union de formes aux bords antialiasés.
    class Splat
    {
        readonly float[] field;
        public readonly int seed;
        readonly System.Random rnd;

        public Splat(float[] field, int seed)
        {
            this.field = field;
            this.seed = seed;
            rnd = new System.Random(seed + 1);
        }

        public float Range(float min, float max) => min + (float)rnd.NextDouble() * (max - min);
        public float Angle() => Range(0f, Mathf.PI * 2f);

        void Put(int x, int y, float d)
        {
            if (x < 0 || y < 0 || x >= Cell || y >= Cell) return;
            float a = Mathf.Clamp01(0.5f - d / 1.4f);
            int i = y * Cell + x;
            if (a > field[i]) field[i] = a;
        }

        // Ellipse au bord flou (empreinte).
        public void Disc(float cx, float cy, float rx, float ry)
        {
            for (int y = Mathf.FloorToInt(cy - ry - 2f); y <= cy + ry + 2f; y++)
                for (int x = Mathf.FloorToInt(cx - rx - 2f); x <= cx + rx + 2f; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (x < 0 || y < 0 || x >= Cell || y >= Cell) continue;
                    float a = Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry) * 0.35f);
                    int i = y * Cell + x;
                    if (a > field[i]) field[i] = a;
                }
        }

        // Tache au contour irrégulier : le rayon ondule selon l'angle.
        public void Blob(float cx, float cy, float radius, float wobble)
        {
            float p1 = Angle(), p2 = Angle(), p3 = Angle(), p4 = Angle();
            float reach = radius * (1f + wobble * 1.2f) + 2f;
            for (int y = Mathf.FloorToInt(cy - reach); y <= cy + reach; y++)
                for (int x = Mathf.FloorToInt(cx - reach); x <= cx + reach; x++)
                {
                    float dx = x - cx, dy = y - cy, a = Mathf.Atan2(dy, dx);
                    float r = radius * (1f + wobble * (0.45f * Mathf.Sin(3f * a + p1) + 0.3f * Mathf.Sin(5f * a + p2)
                        + 0.18f * Mathf.Sin(8f * a + p3) + 0.12f * Mathf.Sin(13f * a + p4)));
                    Put(x, y, Mathf.Sqrt(dx * dx + dy * dy) - r);
                }
        }

        // Filet effilé d'un point à l'autre (rayon r0 au départ, r1 à l'arrivée).
        public void Capsule(float x0, float y0, float x1, float y1, float r0, float r1)
        {
            float vx = x1 - x0, vy = y1 - y0, len2 = Mathf.Max(0.0001f, vx * vx + vy * vy);
            float reach = Mathf.Max(r0, r1) + 2f;
            for (int y = Mathf.FloorToInt(Mathf.Min(y0, y1) - reach); y <= Mathf.Max(y0, y1) + reach; y++)
                for (int x = Mathf.FloorToInt(Mathf.Min(x0, x1) - reach); x <= Mathf.Max(x0, x1) + reach; x++)
                {
                    float t = Mathf.Clamp01(((x - x0) * vx + (y - y0) * vy) / len2);
                    float dx = x - (x0 + vx * t), dy = y - (y0 + vy * t);
                    Put(x, y, Mathf.Sqrt(dx * dx + dy * dy) - Mathf.Lerp(r0, r1, t));
                }
        }

        // Traînée organique : une chaîne de taches irrégulières qui s'affine de r0 à r1 et se courbe légèrement ('curve',
        // radians par pixel). Rend le bout de la traînée.
        public void Streak(float x, float y, float angle, float length, float r0, float r1, float curve, out float endX, out float endY)
        {
            const float step = 1.5f;
            for (float d = 0f; d <= length; d += step)
            {
                float t = d / length;
                Blob(x, y, Mathf.Lerp(r0, r1, t) * (0.9f + 0.2f * Mathf.PerlinNoise(d * 0.08f + seed, x * 0.01f)), 0.12f);
                angle += curve * step;
                x += Mathf.Cos(angle) * step;
                y += Mathf.Sin(angle) * step;
            }
            endX = x;
            endY = y;
        }

        // Postillons : petites gouttes entre 'dMin' et 'dMax' du centre, dans un secteur d'angles (radians).
        public void Specks(float cx, float cy, float dMin, float dMax, int count, float rMin, float rMax, float aMin, float aMax)
        {
            for (int k = 0; k < count; k++)
            {
                float a = Range(aMin, aMax), d = Range(dMin, dMax);
                Blob(cx + Mathf.Cos(a) * d, cy + Mathf.Sin(a) * d, Range(rMin, rMax), 0.1f);
            }
        }
    }
}
