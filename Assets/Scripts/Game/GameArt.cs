using System.Collections.Generic;
using UnityEngine;

// Matériaux, textures procédurales et modèles 3D du jeu.
// Les matériaux de base sont dans Resources/Materials (créés par "FunGame/Configurer le jeu")
// pour que leurs shaders soient inclus dans le build WebGL.
public static class GameArt
{
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static Material lit, litEmissive, unlit, outline, text3D;
    static Font font;

    public static Font Font
    {
        get
        {
            if (font != null) return font;
            var fonts = Resources.Load<GameFonts>("GameFonts");
            font = fonts != null && fonts.main != null ? fonts.main : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    static Material Template(ref Material field, string path, string fallbackShader)
    {
        if (field != null) return field;
        field = Resources.Load<Material>("Materials/" + path);
        if (field == null) field = new Material(Shader.Find(fallbackShader));
        return field;
    }

    public static Material Lit(string key, Color color, Texture tex = null, float gloss = 0.2f)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Template(ref lit, "Lit", "Standard")) { name = key, color = color };
        if (tex != null) m.mainTexture = tex;
        m.SetFloat("_Glossiness", gloss);
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        return cache[key] = m;
    }

    public static Material Emissive(string key, Color color, Color emission)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Template(ref litEmissive, "LitEmissive", "Standard")) { name = key, color = color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        return cache[key] = m;
    }

    public static Material Unlit(string key, Color color)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Template(ref unlit, "Unlit", "Unlit/Color")) { name = key, color = color };
        return cache[key] = m;
    }

    public static Material Outline => Template(ref outline, "Outline", "FunGame/Outline");

    // Contour blanc (boîte de Pogo).
    public static Material WhiteOutline
    {
        get
        {
            if (cache.TryGetValue("WhiteOutline", out var m) && m != null) return m;
            m = new Material(Outline) { name = "WhiteOutline" };
            m.SetColor("_Color", Color.white);
            return cache["WhiteOutline"] = m;
        }
    }

    // Texte 3D masqué par les murs (le matériau par défaut des polices s'affiche à travers tout).
    public static Material TextMaterial
    {
        get
        {
            if (text3D != null) return text3D;
            var template = Resources.Load<Material>("Materials/Text3D");
            text3D = template != null ? new Material(template) : new Material(Font.material);
            text3D.mainTexture = Font.material.mainTexture;
            Font.textureRebuilt -= OnFontRebuilt;
            Font.textureRebuilt += OnFontRebuilt;
            return text3D;
        }
    }

    static void OnFontRebuilt(Font f)
    {
        if (f == Font && text3D != null) text3D.mainTexture = f.material.mainTexture;
    }

    public static TextMesh CreateText(Transform parent, string name, Vector3 localPos, float charSize, Color color,
        TextAnchor anchor = TextAnchor.MiddleCenter, TextAlignment align = TextAlignment.Center)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = Font;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.anchor = anchor;
        tm.alignment = align;
        tm.color = color;
        tm.richText = true;
        go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial;
        return tm;
    }

    // ---------- Matériaux du décor ----------

    // Damier astiqué : très lisse, les lampes s'y reflètent.
    public static Material CheckerFloor => Lit("CheckerFloor", Color.white, Checker(256, 2, new Color(0.92f, 0.92f, 0.9f), new Color(0.05f, 0.05f, 0.05f)), 0.88f);
    public static Material CheckerWall => Lit("CheckerWall", new Color(0.85f, 0.85f, 0.85f), Checker(256, 2, Color.white, new Color(0.08f, 0.08f, 0.08f)), 0.8f);
    public static Material Concrete => Lit("Concrete", new Color(0.62f, 0.62f, 0.6f), Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
    // Murs des couloirs : shader d'insectes grouillants (Resources/Materials/InfestedWall, créé par GameSetup).
    public static Material InfestedWall
    {
        get
        {
            if (cache.TryGetValue("InfestedWall", out var m) && m != null) return m;
            var template = Resources.Load<Material>("Materials/InfestedWall");
            if (template == null) return Concrete;
            m = new Material(template) { name = "InfestedWall" };
            m.mainTexture = Noise("concrete", 128, 0.75f, 0.2f, 11);
            return cache["InfestedWall"] = m;
        }
    }
    // Faisceau laser animé (Resources/Materials/LaserBeam) : cœur concentré, ou halo diffus autour.
    public static Material LaserBeam(string key, float intensity, float corePower)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        var template = Resources.Load<Material>("Materials/LaserBeam");
        if (template == null) return Unlit(key, new Color(1f, 0.05f, 0.05f));
        m = new Material(template) { name = key };
        m.SetFloat("_Intensity", intensity);
        m.SetFloat("_CorePower", corePower);
        return cache[key] = m;
    }

    // Membres du décor : plâtre gris poussiéreux (Resources/Materials/DustyLimb).
    public static Material DustyLimb
    {
        get
        {
            if (cache.TryGetValue("DustyLimb", out var m) && m != null) return m;
            var template = Resources.Load<Material>("Materials/DustyLimb");
            m = template != null ? new Material(template) { name = "DustyLimb", enableInstancing = true }
                                 : Lit("DustyLimb", new Color(0.4f, 0.39f, 0.37f), null, 0.05f);
            return cache["DustyLimb"] = m;
        }
    }

    // Matériau tiré d'un modèle de Resources/Materials (shader du jeu), avec repli si absent.
    static Material FromTemplate(string name, System.Func<Material> fallback)
    {
        if (cache.TryGetValue(name, out var m) && m != null) return m;
        var template = Resources.Load<Material>("Materials/" + name);
        m = template != null ? new Material(template) { name = name } : fallback();
        return cache[name] = m;
    }

    // Lueur qui se voit dans le noir (ni lumière ni brouillard) : yeux de La Tour.
    public static Material Glow(string key, Color color, float intensity)
    {
        if (cache.TryGetValue("Glow" + key, out var m) && m != null) return m;
        var template = Resources.Load<Material>("Materials/Glow");
        m = template != null ? new Material(template) : Unlit("GlowFallback" + key, color);
        m.name = "Glow" + key;
        m.SetColor("_Color", color);
        m.SetFloat("_Intensity", intensity);
        return cache["Glow" + key] = m;
    }

    // Brume de l'Abjuration (shader Mist, intensité globale _Abjuration).
    public static Material Mist => FromTemplate("Mist", () => Unlit("MistFallback", new Color(0.2f, 0.4f, 0.8f)));
    // Battant ectoplasmique de la porte de la Zone 2.
    public static Material Ectoplasm => FromTemplate("Ectoplasm", () => Emissive("EctoFallback", new Color(0.1f, 0.3f, 0.7f), new Color(0.1f, 0.35f, 0.9f)));
    // Aura bleue des orbes activés.
    public static Material GhostAura => FromTemplate("GhostAura", () => Unlit("AuraFallback", new Color(0.2f, 0.5f, 1f)));

    // Matière légèrement transparente, noire ou blanche (Resources/Materials/Translucent ; sinon opaque).
    public static Material Translucent(bool dark)
    {
        string key = dark ? "TranslucentBlack" : "TranslucentWhite";
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        var template = Resources.Load<Material>("Materials/Translucent");
        Color c = dark ? new Color(0.03f, 0.03f, 0.03f, 0.55f) : new Color(0.9f, 0.9f, 0.9f, 0.45f);
        m = template != null ? new Material(template) { name = key, color = c } : Lit(key, c, null, 0.9f);
        return cache[key] = m;
    }

    // Variante colorée de l'aura (Balise Améliorée, Téléporteur, Station Curative, boule d'énergie).
    public static Material GhostAuraTinted(string key, Color color)
    {
        if (cache.TryGetValue("GhostAura" + key, out var m) && m != null) return m;
        m = new Material(GhostAura) { name = "GhostAura" + key };
        m.SetColor("_Color", color);
        m.color = color;
        return cache["GhostAura" + key] = m;
    }
    // Rayon d'énergie entre Balises chargées (LineRenderer).
    public static Material EnergyBeam => FromTemplate("EnergyBeam", () => Unlit("BeamFallback", new Color(0.2f, 0.5f, 1f)));

    // Explosion (Resources/Materials/Explosion) : boule de feu qui retombe en fumée, ou fumée seule ; repli : étincelles.
    public static Material ExplosionFire => ExplosionMaterial("ExplosionFeu", 1f, 2.2f, new Color(0.1f, 0.09f, 0.085f));
    public static Material ExplosionSmoke => ExplosionMaterial("ExplosionFumee", 0f, 0f, new Color(0.2f, 0.19f, 0.18f));
    public static Material ExplosionDust => ExplosionMaterial("ExplosionPoussiere", 0f, 0f, new Color(0.36f, 0.31f, 0.26f));

    static Material ExplosionMaterial(string key, float fire, float intensity, Color smoke)
    {
        if (cache.TryGetValue(key, out var m) && m != null) return m;
        var template = Resources.Load<Material>("Materials/Explosion");
        if (template == null) return cache[key] = Spark;
        m = new Material(template) { name = key };
        m.SetFloat("_Fire", fire);
        m.SetFloat("_Intensity", intensity);
        m.SetColor("_SmokeColor", smoke);
        return cache[key] = m;
    }

    // Particules d'étincelles (Resources/Materials/Spark).
    public static Material Spark
    {
        get
        {
            if (cache.TryGetValue("Spark", out var m) && m != null) return m;
            var template = Resources.Load<Material>("Materials/Spark");
            m = template != null ? new Material(template) { name = "Spark" } : Unlit("Spark", new Color(1f, 0.6f, 0.2f));
            return cache["Spark"] = m;
        }
    }
    public static Material DarkConcrete => Lit("DarkConcrete", new Color(0.3f, 0.3f, 0.3f), Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
    public static Material Metal => Lit("Metal", new Color(0.25f, 0.25f, 0.27f), null, 0.6f);

    // ---------- Textures procédurales ----------

    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    // Damier de 'cells' x 'cells' cases (une texture = 2 mètres avec le mapping monde à 0,5).
    public static Texture2D Checker(int size, int cells, Color a, Color b)
    {
        string key = "checker" + size + cells + a + b;
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        t = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        int cell = size / cells;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = ((x / cell + y / cell) & 1) == 0 ? a : b;
        t.SetPixels(px);
        t.Apply(true, true);
        return textures[key] = t;
    }

    // Bruit de valeur à plusieurs octaves (béton) + quelques fissures sombres.
    public static Texture2D Noise(string name, int size, float baseValue, float amplitude, int seed)
    {
        string key = "noise" + name + size + seed;
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        var rnd = new System.Random(seed);
        t = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
        var px = new Color[size * size];
        float ox = (float)rnd.NextDouble() * 100f, oy = (float)rnd.NextDouble() * 100f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = 0f, amp = 0.5f, freq = 4f / size;
                for (int o = 0; o < 4; o++)
                {
                    n += amp * TileableNoise(x * freq + ox, y * freq + oy, size * freq);
                    amp *= 0.5f; freq *= 2f;
                }
                float v = baseValue + (n - 0.47f) * amplitude * 2f;
                px[y * size + x] = new Color(v, v, v);
            }
        for (int c = 0; c < 6; c++)
        {
            float x = rnd.Next(size), y = rnd.Next(size), angle = (float)rnd.NextDouble() * Mathf.PI * 2f;
            int len = rnd.Next(size / 4, size / 2);
            for (int i = 0; i < len; i++)
            {
                angle += ((float)rnd.NextDouble() - 0.5f) * 0.6f;
                x += Mathf.Cos(angle); y += Mathf.Sin(angle);
                int ix = ((int)x % size + size) % size, iy = ((int)y % size + size) % size;
                px[iy * size + ix] *= 0.55f;
            }
        }
        t.SetPixels(px);
        t.Apply(true, true);
        return textures[key] = t;
    }

    static float TileableNoise(float x, float y, float period)
    {
        int p = Mathf.Max(1, Mathf.RoundToInt(period));
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(x0 % p, y0 % p), b = Hash((x0 + 1) % p, y0 % p);
        float c = Hash(x0 % p, (y0 + 1) % p), d = Hash((x0 + 1) % p, (y0 + 1) % p);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h & 0xFFFF) / 65535f;
    }

    // Toile de chapiteau vernie (gris clair, teintée par la couleur du matériau) : trame fine, plis, taches et
    // petites aspérités, pour que le rouge et le beige ne soient plus unis.
    public static Texture2D Canvas()
    {
        if (textures.TryGetValue("canvas", out var t) && t != null) return t;
        const int size = 256;
        var rnd = new System.Random(29);
        t = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Trame de tissu : fils horizontaux et verticaux alternés.
                float weave = ((x / 2 + y / 2) & 1) == 0 ? 1f : 0.93f;
                float fibres = 0.96f + 0.04f * Mathf.Sin(x * 0.9f + TileableNoise(x * 0.125f, y * 0.125f, 32f) * 6f);
                // Plis et usure : bruit à grande échelle.
                float folds = 0.86f + 0.14f * TileableNoise(x * 0.03125f, y * 0.03125f + 7f, 8f);
                float blotch = 0.92f + 0.08f * TileableNoise(x * 0.0625f + 5f, y * 0.0625f, 16f);
                float v = weave * fibres * folds * blotch * (0.97f + 0.03f * (float)rnd.NextDouble());
                px[y * size + x] = new Color(v, v, v);
            }
        // Aspérités : petits éclats et traces plus sombres.
        for (int k = 0; k < 60; k++)
        {
            int cx = rnd.Next(size), cy = rnd.Next(size), r = 1 + rnd.Next(3);
            float dark = 0.82f + (float)rnd.NextDouble() * 0.12f;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r)
                    {
                        int ix = ((cx + dx) % size + size) % size, iy = ((cy + dy) % size + size) % size;
                        px[iy * size + ix] *= dark;
                    }
        }
        t.SetPixels(px);
        // Reste lisible : CanvasStripes en reprend les pixels.
        t.Apply(true, false);
        return textures["canvas"] = t;
    }

    // Rayures de toile (toit du chapiteau) : moitié 'a', moitié 'b', avec la matière de Canvas().
    public static Texture2D CanvasStripes(Color a, Color b)
    {
        string key = "canvasStripes" + a + b;
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        Texture2D canvas = Canvas();
        int size = canvas.width;
        t = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var src = canvas.GetPixels();
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = (x < size / 2 ? a : b) * src[y * size + x].r;
        t.SetPixels(px);
        t.Apply(true, true);
        return textures[key] = t;
    }

    // Rayures verticales (chapiteau du Hub).
    public static Texture2D Stripes(Color a, Color b)
    {
        string key = "stripes" + a + b;
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        t = new Texture2D(2, 1, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        t.SetPixels(new[] { a, b });
        t.Apply();
        return textures[key] = t;
    }

    // Blanc de l'œil veiné (projection équirectangulaire de la sphère low-poly).
    public static Texture2D EyeSclera()
    {
        if (textures.TryGetValue("sclera", out var t) && t != null) return t;
        const int w = 256, h = 128;
        var rnd = new System.Random(5);
        t = new Texture2D(w, h, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.86f, 0.85f, 0.82f);
        for (int v = 0; v < 70; v++)
        {
            // Les veines partent de l'arrière de l'œil (u = 0,25) vers l'iris (u = 0,75).
            float x = w * 0.25f + (float)rnd.NextDouble() * 30f - 15f, y = (float)rnd.NextDouble() * h;
            float dir = (float)(rnd.NextDouble() - 0.5) * 0.8f;
            int len = rnd.Next(40, 120);
            float shade = 0.25f + (float)rnd.NextDouble() * 0.3f;
            for (int i = 0; i < len; i++)
            {
                dir += (float)(rnd.NextDouble() - 0.5) * 0.5f;
                x += Mathf.Cos(dir); y += Mathf.Sin(dir) * 0.6f;
                int ix = ((int)x % w + w) % w, iy = Mathf.Clamp((int)y, 0, h - 1);
                px[iy * w + ix] = Color.Lerp(px[iy * w + ix], new Color(shade, shade * 0.9f, shade * 0.9f), 0.7f);
            }
        }
        t.SetPixels(px);
        t.Apply(true, true);
        return textures["sclera"] = t;
    }

    // ---------- Modèles Meshy (Resources/Art) ----------

    // Instancie le modèle 'name' mis à l'échelle 'height', posé au sol sur le pivot.
    // Retourne null si le modèle n'existe pas (l'appelant construit alors une version en primitives).
    public static GameObject SpawnModel(string name, Transform parent, float height)
    {
        var prefab = Resources.Load<GameObject>("Art/" + name);
        if (prefab == null) return null;

        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        var go = Object.Instantiate(prefab, holder.transform);
        go.transform.localPosition = Vector3.zero;
        // Rotation d'import conservée : certains FBX Meshy (remaillés) sont tournés de -90° en X pour être debout.
        go.transform.localRotation = prefab.transform.localRotation;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);

        Bounds b = LocalBounds(holder.transform);
        float scale = b.size.y > 0.0001f ? height / b.size.y : 1f;
        go.transform.localScale *= scale;
        b = LocalBounds(holder.transform);
        go.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        return holder;
    }

    public static Bounds LocalBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        bool any = false;
        var b = new Bounds();
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            var mf = r.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
            if (mesh == null) continue;
            Bounds mb = mesh.bounds;
            Matrix4x4 m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ---------- Primitives ----------

    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider)
        {
            // Désactivé tout de suite (la destruction n'a lieu qu'en fin d'image). En WebGL, le SphereCollider est retiré du
            // build : une sphère n'en a pas.
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                Object.Destroy(col);
            }
        }
        return go;
    }

    public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static Mesh lowSphere;
    public static Mesh LowSphere => lowSphere != null ? lowSphere : (lowSphere = MeshBuilder.UvSphere(14, 9));
}
