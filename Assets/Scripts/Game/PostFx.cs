using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Effets post-process (GDD chapitre 12, V0.6.13), toujours actifs et superposés. Chaque effet tient dans sa propre partie de
// code, pour pouvoir le retirer sans toucher au reste :
//   FX1 vignette irrégulière, FX4 image qui tremble (pulsations cardiaques), FX5 couleurs qui se séparent (près d'un
//   Cauchemar) : shader plein écran FunGame/OldFilm (PostFxCamera).
//   FX6 écrans du Hub façon vieux moniteur (CrtScreen) ; FX7 poussière en suspension (PostFxCamera) ; FX8 bandes noires de
//   cinéma pendant les cinématiques de Game Over (PostFxCamera).
public static class PostFx
{
    // Réglés par GameSession : proximité du Cauchemar le plus proche (0 à 1, FX5) et cinématique de Game Over (FX8).
    public static float NightmareProximity;
    public static bool GameOverCinematic;

    // ---------- FX6 : texture de vieux moniteur (lignes de balayage, bords bombés assombris) ----------

    static Texture2D crtTexture;

    public static Texture2D CrtTexture()
    {
        if (crtTexture != null) return crtTexture;
        const int w = 128, h = 256;
        crtTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VieuxMoniteur", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                // Ligne de balayage : une rangée sombre sur trois.
                float scan = y % 3 == 0 ? 0.42f : y % 3 == 1 ? 0.12f : 0f;
                // Bords bombés : l'écran s'assombrit en s'arrondissant vers les coins.
                float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(u), 4f) + Mathf.Pow(Mathf.Abs(v), 4f), 0.25f);
                float bulge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, r)) * 0.85f;
                float a = Mathf.Clamp01(scan + bulge - scan * bulge);
                px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        crtTexture.SetPixels32(px);
        crtTexture.Apply();
        return crtTexture;
    }

    // Écran du Hub aux coins arrondis ('size' et 'corner' en mètres) : le calque suit l'arrondi de l'écran (transparent hors de
    // l'écran, sans pointe noire aux angles) et ses bords s'assombrissent le long de cet arrondi.
    static readonly Dictionary<string, Material> roundedCrt = new Dictionary<string, Material>();

    public static Material CrtMaterial(Vector2 size, float corner)
    {
        string key = size.x.ToString("F2") + "x" + size.y.ToString("F2") + "r" + corner.ToString("F2");
        if (roundedCrt.TryGetValue(key, out var cached) && cached != null) return cached;
        const int h = 256;
        int w = Mathf.Clamp(Mathf.RoundToInt(h * size.x / Mathf.Max(0.01f, size.y)), 32, 1024);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VieuxMoniteur" + key, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        float pixel = size.y / h, fade = Mathf.Min(size.x, size.y) * 0.18f;
        Vector2 half = size * 0.5f;
        corner = Mathf.Min(corner, Mathf.Min(half.x, half.y));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Point en mètres depuis le centre ; distance au bord du rectangle arrondi (positive à l'intérieur).
                var p = new Vector2(((x + 0.5f) / w - 0.5f) * size.x, ((y + 0.5f) / h - 0.5f) * size.y);
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - Vector2.one * corner);
                float outside = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - corner;
                float inside = -outside;
                float mask = Mathf.Clamp01(inside / pixel + 0.5f);
                float scan = y % 3 == 0 ? 0.42f : y % 3 == 1 ? 0.12f : 0f;
                float bulge = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / fade))) * 0.85f;
                float a = Mathf.Clamp01(scan + bulge - scan * bulge) * mask;
                px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        var m = new Material(Shader.Find("Sprites/Default")) { name = "VieuxMoniteur", mainTexture = tex, color = Color.white };
        return roundedCrt[key] = m;
    }
}

// FX6 : écran d'un Hub (Logs, quota, gamelle, timer, Tableau de Manche...) façon vieux moniteur : lignes de balayage, bords
// bombés et image fantôme (une copie du texte, légèrement décalée et pâle).
public class CrtScreen : MonoBehaviour
{
    TextMesh main, ghost;
    GameObject overlay;

    public void Init(TextMesh text, Vector2 size, float corner)
    {
        main = text;
        // Calque posé juste devant le texte (on lit l'écran depuis son -Z), aux mêmes coins arrondis que l'écran.
        overlay = GameArt.Prim(PrimitiveType.Quad, transform, new Vector3(0f, 0f, -0.02f), new Vector3(size.x, size.y, 1f), PostFx.CrtMaterial(size, corner));
        overlay.name = "VieuxMoniteur";
        foreach (var c in overlay.GetComponentsInChildren<Collider>()) Destroy(c);
        overlay.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghost = Instantiate(text.gameObject, text.transform.parent).GetComponent<TextMesh>();
        ghost.name = "TexteFantome";
        ghost.transform.localPosition = text.transform.localPosition + new Vector3(0.025f, -0.012f, 0.004f);
    }

    void LateUpdate()
    {
        if (ghost == null || !ghost.gameObject.activeSelf || main == null) return;
        if (ghost.text != main.text) ghost.text = main.text;
        Color c = main.color;
        // L'image fantôme tremble à peine.
        ghost.color = new Color(c.r * 0.8f, c.g, c.b, c.a * (0.2f + 0.06f * Mathf.Sin(Time.time * 9f)));
    }
}

// Sur la caméra du jeu : FX1, FX4 et FX5 (OnRenderImage), FX7 (poussière en suspension) et FX8 (bandes noires de cinéma).
[RequireComponent(typeof(Camera))]
public class PostFxCamera : MonoBehaviour
{
    static readonly int VignetteId = Shader.PropertyToID("_Vignette"), ShakeId = Shader.PropertyToID("_Shake"), SplitId = Shader.PropertyToID("_Split");
    Material film;
    bool filmLooked;
    const float LobbyVignette = 1.3f;
    float split;

    void Start()
    {
        BuildDust();
        BuildBars();
    }

    Material Film
    {
        get
        {
            if (filmLooked) return film;
            filmLooked = true;
            var template = Resources.Load<Material>("Materials/OldFilm");
            Shader shader = template != null ? template.shader : Shader.Find("FunGame/OldFilm");
            if (shader != null && shader.isSupported) film = new Material(shader) { name = "VieuxFilm" };
            return film;
        }
    }

    void Update()
    {
        split = Mathf.MoveTowards(split, PostFx.NightmareProximity, Time.deltaTime * 1.5f);
        UpdateDust();
        UpdateBars();
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (Film == null)
        {
            Graphics.Blit(src, dst);
            return;
        }
        // Dans le menu du lobby (aucune partie en cours), la vignette est un peu plus marquée.
        bool lobby = NetworkGame.Instance != null && !NetworkGame.Instance.InGame;
        film.SetFloat(VignetteId, lobby ? LobbyVignette : 1f);
        // FX4 : seulement pendant les pulsations cardiaques, d'un pixel à deux selon la tension.
        float tension = OrbitCamera.HeartTension;
        film.SetFloat(ShakeId, tension > 0.05f ? Mathf.Lerp(1f, 2f, tension) : 0f);
        film.SetFloat(SplitId, split);
        Graphics.Blit(src, dst, film);
    }

    // ---------- FX7 : poussière en suspension ----------

    // Fines particules qui flottent autour du joueur ; elles ne brillent que dans la lumière des lampes (chaque particule
    // prend l'éclairage des lampes proches, mesuré quelques fois par seconde).
    const int DustCount = 220;
    const float DustBox = 9f, DustLightRefresh = 0.75f;
    ParticleSystem dust;
    ParticleSystem.Particle[] motes;
    readonly List<Light> nearLights = new List<Light>();
    float lightsAt = -10f;

    void BuildDust()
    {
        var go = new GameObject("PoussiereEnSuspension");
        go.transform.SetParent(transform, false);
        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dust.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new Color(1f, 0.96f, 0.85f, 0f);
        main.maxParticles = DustCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.002f;
        var emission = dust.emission;
        emission.rateOverTime = DustCount / 8f;
        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(DustBox, DustBox * 0.5f, DustBox);
        shape.position = new Vector3(0f, 0f, DustBox * 0.35f);
        var noise = dust.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.05f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GameArt.Spark;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        motes = new ParticleSystem.Particle[DustCount];
        dust.Play();
    }

    void UpdateDust()
    {
        if (dust == null) return;
        if (Time.time - lightsAt > DustLightRefresh)
        {
            lightsAt = Time.time;
            nearLights.Clear();
            Vector3 me = transform.position;
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Point && l.isActiveAndEnabled && (l.transform.position - me).sqrMagnitude < (l.range + DustBox) * (l.range + DustBox))
                    nearLights.Add(l);
        }
        // Un rechargement des scripts en cours de partie (éditeur) vide ce tableau, qui n'est pas sérialisable, sans
        // rappeler Start : on le recrée au besoin.
        if (motes == null) motes = new ParticleSystem.Particle[DustCount];
        int n = dust.GetParticles(motes);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = motes[i].position;
            float lit = 0f;
            foreach (var l in nearLights)
            {
                if (l == null || !l.enabled) continue;
                float d = Vector3.Distance(p, l.transform.position);
                if (d < l.range) lit += l.intensity * (1f - d / l.range) * (1f - d / l.range);
            }
            // Apparition et disparition en douceur au long de leur vie.
            float life = motes[i].remainingLifetime / Mathf.Max(0.01f, motes[i].startLifetime);
            float fade = Mathf.Sin(Mathf.Clamp01(life) * Mathf.PI);
            float a = Mathf.Clamp01(lit * 0.5f) * fade * 0.85f;
            motes[i].startColor = new Color(1f, 0.95f, 0.82f, a);
        }
        dust.SetParticles(motes, n);
    }

    // ---------- FX8 : bandes noires de cinéma ----------

    const float BarHeight = 0.11f, BarSpeed = 0.6f;
    RectTransform topBar, bottomBar;
    float bars;

    void BuildBars()
    {
        var canvas = UiKit.CreateCanvas(transform, "BandesCinema", 95);
        topBar = Bar(canvas.transform, true);
        bottomBar = Bar(canvas.transform, false);
        UpdateBars();
    }

    // Même texture que les boutons (trace de pinceau aux bords irréguliers), en noir.
    static RectTransform Bar(Transform parent, bool top)
    {
        var go = new GameObject(top ? "BandeHaut" : "BandeBas", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        // Bien plus longue que l'écran : les extrémités du coup de pinceau (pointes noires) restent hors champ.
        rt.anchorMin = new Vector2(-0.35f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1.35f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 0f : 1f);
        rt.sizeDelta = new Vector2(0f, 160f);
        UiKit.Paint(go, Color.black, top ? 71 : 73);
        go.GetComponent<Image>().raycastTarget = false;
        // Rectangle plein qui prolonge la bande hors de l'écran (le bord peint reste visible côté image).
        var fill = new GameObject("Plein", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(go.transform, false);
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = new Vector2(0f, top ? 0.5f : -4f);
        frt.anchorMax = new Vector2(1f, top ? 5f : 0.5f);
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        var img = fill.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        return rt;
    }

    void UpdateBars()
    {
        if (topBar == null) return;
        bool show = PostFx.GameOverCinematic;
        bars = Mathf.MoveTowards(bars, show ? 1f : 0f, Time.unscaledDeltaTime / BarSpeed);
        float k = Mathf.SmoothStep(0f, 1f, bars);
        var canvas = (RectTransform)topBar.parent;
        float h = canvas.rect.height * BarHeight;
        // Hauteur visible de chaque bande ; la bande glisse depuis le bord de l'écran.
        topBar.sizeDelta = new Vector2(0f, h * 1.2f);
        bottomBar.sizeDelta = new Vector2(0f, h * 1.2f);
        topBar.anchoredPosition = new Vector2(0f, -h * k + h * 0.2f * (1f - k));
        bottomBar.anchoredPosition = new Vector2(0f, h * k - h * 0.2f * (1f - k));
        topBar.gameObject.SetActive(k > 0.001f);
        bottomBar.gameObject.SetActive(k > 0.001f);
    }
}
