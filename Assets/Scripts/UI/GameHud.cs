using UnityEngine;
using UnityEngine.UI;

// Interface du joueur en jeu : jauges de vie (rouge) et de sérénité (bleue), 5 cases d'inventaire, boussole,
// viseur, messages et écran de game over. Textures procédurales selon la DA du GDD :
// jauges craquelées dans un cadre sombre avec un repère blanc, cases arrondies vert-de-gris, boussole graduée.
public class GameHud : MonoBehaviour
{
    static readonly Color SlotColor = new Color(0.11f, 0.15f, 0.13f, 0.95f);
    static readonly Color SlotSelectedColor = new Color(0.95f, 0.75f, 0.25f, 0.95f);
    static readonly Color HealthColor = new Color(0.95f, 0.38f, 0.22f);
    static readonly Color SerenityColor = new Color(0.3f, 0.62f, 1f);
    const float GaugeWidth = 330f, GaugeHeight = 30f;
    const float CompassWidth = 420f, CompassHeight = 40f, CompassSpan = 180f;

    public static bool GameOverVisible { get; private set; }

    class Gauge
    {
        public RawImage fill;
        public RectTransform marker;
        public Text text;
        public float inner;
    }

    GameObject root, gameOverPanel, restartButton, compassRoot;
    Gauge health, serenity;
    Text promptText, toastText, spectateText, gameOverText;
    readonly Image[] slotBg = new Image[GameRules.InventorySize];
    readonly Text[] slotName = new Text[GameRules.InventorySize];
    readonly Text[] slotNumber = new Text[GameRules.InventorySize];
    readonly Image[] slotIcon = new Image[GameRules.InventorySize];
    // Icônes d'inventaire (Resources/Icons/<ItemType>, créées par "FunGame/Générer les icônes d'inventaire").
    readonly System.Collections.Generic.Dictionary<ItemType, Sprite> icons = new System.Collections.Generic.Dictionary<ItemType, Sprite>();
    float toastTimer;
    RawImage damageFlash;
    float flashTimer;
    const float FlashDuration = 0.7f;
    readonly System.Collections.Generic.List<(RectTransform rt, float angle)> compassMarks = new System.Collections.Generic.List<(RectTransform, float)>();

    // ================= Textures procédurales =================

    // Bords opaques, centre transparent.
    static Texture2D Vignette(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, d));
                pixels[y * size + x] = new Color(1f, 1f, 1f, 0.25f + 0.75f * a);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // Matière craquelée de la jauge (blanc à gris, teintée par la couleur de l'image) : plaques irrégulières
    // séparées par des fissures sombres (bruit cellulaire), grain fin et reflet en haut.
    static Texture2D CrackedTexture(int w, int h, int seed)
    {
        var rnd = new System.Random(seed);
        const int cells = 18;
        var points = new Vector2[cells * 3];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector2((float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d1 = float.MaxValue, d2 = float.MaxValue;
                foreach (var p in points)
                {
                    // Distances étirées en hauteur : plaques plus larges que hautes.
                    float dx = Mathf.Abs(x - p.x);
                    dx = Mathf.Min(dx, w - dx);
                    float dy = (y - p.y) * 2.2f;
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                float crack = 1f - Mathf.SmoothStep(0f, 2.2f, edge);
                float grain = 0.88f + 0.12f * (float)rnd.NextDouble();
                float shade = Mathf.Lerp(0.78f, 1f, Mathf.PerlinNoise(x * 0.05f + seed, y * 0.2f)) * grain;
                float top = 1f + 0.18f * Mathf.Clamp01((y / (float)h - 0.55f) * 3f);
                float v = shade * top * (1f - crack * 0.65f);
                px[y * w + x] = new Color(v, v, v, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // Carré arrondi avec bordure claire et léger relief (cases d'inventaire, fonds).
    static Sprite RoundedSprite(int size, int radius, float border, float bevel)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = radius - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float inside = Mathf.Clamp01(d + 0.5f);
                float rim = 1f - Mathf.Clamp01((d - border) / 1.5f);
                float light = 1f + bevel * ((y / (float)size) - 0.5f);
                float v = Mathf.Lerp(light, 1.6f, rim * 0.5f);
                px[y * size + x] = new Color(v * 0.62f, v * 0.62f, v * 0.62f, inside);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
    }

    // Bande horizontale qui s'efface sur les bords (fond de la boussole).
    static Texture2D FadeBand(int w)
    {
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float k = Mathf.Abs(x / (float)(w - 1) * 2f - 1f);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.55f, 1f, k)));
        }
        tex.Apply();
        return tex;
    }

    // ================= Construction =================

    public void Init(System.Action onRestart, System.Action onQuit)
    {
        var canvas = UiKit.CreateCanvas(transform, "HudCanvas", 50);
        Texture2D vignette = Vignette(128);

        // Flash rouge des dégâts (sous le reste de l'interface).
        damageFlash = FullScreen(canvas.transform, "FlashDegats", vignette);

        root = new GameObject("Hud", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        UiKit.Stretch((RectTransform)root.transform);
        Transform r = root.transform;

        // Viseur au centre de l'écran.
        var dotOutline = UiKit.Panel(r, "ViseurContour", new Vector2(8, 8), new Color(0f, 0f, 0f, 0.6f));
        UiKit.Place(dotOutline, new Vector2(0.5f, 0.5f), Vector2.zero).GetComponent<Image>().raycastTarget = false;
        var dot = UiKit.Panel(dotOutline.transform, "Viseur", new Vector2(4, 4), Color.white);
        UiKit.Place(dot, new Vector2(0.5f, 0.5f), Vector2.zero).GetComponent<Image>().raycastTarget = false;

        promptText = Floating(r, new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(700, 30), 18);
        toastText = Floating(r, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 34), 20);
        toastText.color = new Color(1f, 0.85f, 0.45f);
        spectateText = Floating(r, new Vector2(0.5f, 0f), new Vector2(0, 196), new Vector2(1000, 54), 18);

        // Jauges : vie (rouge) au-dessus de la sérénité (bleue).
        Texture2D cracked = CrackedTexture(256, 32, 11);
        health = BuildGauge(r, "JaugeDeVie", new Vector2(20, 62), HealthColor, cracked);
        serenity = BuildGauge(r, "JaugeDeSerenite", new Vector2(20, 22), SerenityColor, cracked);

        // Inventaire : 5 cases arrondies sur une barre.
        Sprite rounded = RoundedSprite(48, 10, 2.5f, 0.35f);
        const float size = 72f, gap = 8f;
        float barWidth = GameRules.InventorySize * (size + gap) + gap;
        var bar = UiKit.Panel(r, "Inventaire", new Vector2(barWidth, size + gap * 2f), new Color(0.27f, 0.32f, 0.27f, 0.9f));
        UiKit.Place(bar, new Vector2(0.5f, 0f), new Vector2(0, 16));
        var barImage = bar.GetComponent<Image>();
        barImage.sprite = rounded;
        barImage.type = Image.Type.Sliced;
        barImage.raycastTarget = false;
        for (int i = 0; i < GameRules.InventorySize; i++)
        {
            var slot = UiKit.Panel(bar.transform, "Case" + (i + 1), new Vector2(size, size), SlotColor);
            float x = (i - (GameRules.InventorySize - 1) * 0.5f) * (size + gap);
            UiKit.Place(slot, new Vector2(0.5f, 0.5f), new Vector2(x, 0));
            slotBg[i] = slot.GetComponent<Image>();
            slotBg[i].sprite = rounded;
            slotBg[i].type = Image.Type.Sliced;
            slotBg[i].raycastTarget = false;
            var icon = new GameObject("Icone", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(slot.transform, false);
            UiKit.Stretch((RectTransform)icon.transform, 8f);
            slotIcon[i] = icon.GetComponent<Image>();
            slotIcon[i].preserveAspect = true;
            slotIcon[i].raycastTarget = false;
            slotIcon[i].enabled = false;
            var num = UiKit.Label(slot.transform, (i + 1).ToString(), 13, FontStyle.Bold, UiKit.MutedText, 20, TextAnchor.UpperLeft);
            UiKit.Stretch(num.rectTransform, 6f);
            slotNumber[i] = num;
            slotName[i] = UiKit.Label(slot.transform, "", 11, FontStyle.Bold, Color.white, size);
            UiKit.Stretch(slotName[i].rectTransform, 4f);
            slotName[i].rectTransform.offsetMax = new Vector2(-4, -14);
        }

        BuildCompass(r);

        // Game over.
        gameOverPanel = UiKit.Panel(canvas.transform, "GameOver", Vector2.zero, new Color(0.12f, 0f, 0f, 0.88f));
        UiKit.Stretch((RectTransform)gameOverPanel.transform);
        var box = new GameObject("Contenu", typeof(RectTransform));
        box.transform.SetParent(gameOverPanel.transform, false);
        ((RectTransform)box.transform).sizeDelta = new Vector2(520, 340);
        UiKit.Column(box, 10, 14);
        UiKit.Label(box.transform, "GAME OVER", 64, FontStyle.Bold, new Color(1f, 0.25f, 0.2f), 80);
        gameOverText = UiKit.Label(box.transform, "", 20, FontStyle.Normal, Color.white, 90);
        restartButton = UiKit.Button(box.transform, "Nouvelle partie", UiKit.PrimaryColor, 52, () => onRestart());
        UiKit.Button(box.transform, "Quitter la partie", UiKit.NeutralColor, 46, () => onQuit());
        gameOverPanel.SetActive(false);

        Show(false);
    }

    static RawImage FullScreen(Transform parent, string name, Texture texture)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        UiKit.Stretch((RectTransform)go.transform);
        var img = go.GetComponent<RawImage>();
        img.texture = texture;
        img.raycastTarget = false;
        img.color = Color.clear;
        return img;
    }

    // Jauge : cadre sombre, matière craquelée assombrie pour la partie vide, vive pour la partie pleine,
    // repère blanc lumineux au bord du remplissage et valeur actuelle / maximale.
    static Gauge BuildGauge(Transform parent, string name, Vector2 pos, Color color, Texture2D texture)
    {
        var g = new Gauge();
        var frame = UiKit.Panel(parent, name, new Vector2(GaugeWidth, GaugeHeight), new Color(0.16f, 0.09f, 0.1f, 0.95f));
        UiKit.Place(frame, Vector2.zero, pos).GetComponent<Image>().raycastTarget = false;
        frame.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        const float pad = 4f;
        g.inner = GaugeWidth - pad * 2f;

        var empty = new GameObject("Vide", typeof(RectTransform), typeof(RawImage));
        empty.transform.SetParent(frame.transform, false);
        UiKit.Stretch((RectTransform)empty.transform, pad);
        var emptyImg = empty.GetComponent<RawImage>();
        emptyImg.texture = texture;
        emptyImg.color = Color.Lerp(color, Color.black, 0.72f);
        emptyImg.raycastTarget = false;

        var fill = new GameObject("Remplissage", typeof(RectTransform), typeof(RawImage));
        fill.transform.SetParent(frame.transform, false);
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.offsetMin = new Vector2(pad, pad);
        frt.offsetMax = new Vector2(pad + g.inner, -pad);
        g.fill = fill.GetComponent<RawImage>();
        g.fill.texture = texture;
        g.fill.color = color;
        g.fill.raycastTarget = false;

        var marker = UiKit.Panel(frame.transform, "Repere", new Vector2(3f, GaugeHeight - 4f), new Color(1f, 1f, 1f, 0.95f));
        g.marker = (RectTransform)marker.transform;
        g.marker.anchorMin = g.marker.anchorMax = new Vector2(0f, 0.5f);
        g.marker.pivot = new Vector2(0.5f, 0.5f);
        marker.GetComponent<Image>().raycastTarget = false;
        marker.AddComponent<Shadow>().effectColor = new Color(1f, 1f, 1f, 0.35f);

        g.text = UiKit.Label(frame.transform, "", 15, FontStyle.Bold, Color.white, GaugeHeight);
        UiKit.Stretch(g.text.rectTransform);
        g.text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        return g;
    }

    // Boussole en haut de l'écran : graduations tous les 15°, points cardinaux, repère central.
    void BuildCompass(Transform parent)
    {
        compassRoot = new GameObject("Boussole", typeof(RectTransform), typeof(RawImage), typeof(RectMask2D));
        compassRoot.transform.SetParent(parent, false);
        var rt = (RectTransform)compassRoot.transform;
        rt.sizeDelta = new Vector2(CompassWidth, CompassHeight);
        UiKit.Place(compassRoot, new Vector2(0.5f, 1f), new Vector2(0, -14));
        var bg = compassRoot.GetComponent<RawImage>();
        bg.texture = FadeBand(128);
        bg.color = new Color(0.05f, 0.04f, 0.06f, 0.85f);
        bg.raycastTarget = false;

        // Ligne horizontale lumineuse.
        var line = UiKit.Panel(compassRoot.transform, "Ligne", new Vector2(CompassWidth * 0.9f, 1.5f), new Color(1f, 1f, 1f, 0.55f));
        UiKit.Place(line, new Vector2(0.5f, 0.5f), new Vector2(0, -6)).GetComponent<Image>().raycastTarget = false;

        string[] cardinals = { "N", "E", "S", "O" };
        for (int a = 0; a < 360; a += 15)
        {
            GameObject mark;
            if (a % 90 == 0)
            {
                var label = UiKit.Label(compassRoot.transform, cardinals[a / 90], 17, FontStyle.Bold, Color.white, 22);
                label.rectTransform.sizeDelta = new Vector2(30, 22);
                label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                mark = label.gameObject;
            }
            else
            {
                bool major = a % 45 == 0;
                mark = UiKit.Panel(compassRoot.transform, "Grad" + a, new Vector2(2f, major ? 12f : 8f), new Color(1f, 1f, 1f, major ? 0.85f : 0.6f));
                mark.GetComponent<Image>().raycastTarget = false;
            }
            var mrt = (RectTransform)mark.transform;
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.anchoredPosition = new Vector2(0, 3);
            compassMarks.Add((mrt, a));
        }
        // Repère central (petit losange sous le centre).
        var center = UiKit.Panel(compassRoot.transform, "Repere", new Vector2(6, 6), Color.white);
        var crt = UiKit.Place(center, new Vector2(0.5f, 0.5f), new Vector2(0, -12));
        crt.localRotation = Quaternion.Euler(0f, 0f, 45f);
        center.GetComponent<Image>().raycastTarget = false;
        compassRoot.SetActive(false);
    }

    static Text Floating(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize)
    {
        var t = UiKit.Label(parent, "", fontSize, FontStyle.Bold, Color.white, size.y);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        return t;
    }

    // ================= Mises à jour =================

    public void Show(bool visible)
    {
        root.SetActive(visible);
        if (!visible)
        {
            HideGameOver();
            flashTimer = 0f;
            damageFlash.color = Color.clear;
        }
    }

    static void SetGauge(Gauge g, int value, int max, bool dead, string label)
    {
        float k = dead ? 0f : Mathf.Clamp01(value / (float)max);
        var frt = g.fill.rectTransform;
        frt.offsetMax = new Vector2(frt.offsetMin.x + g.inner * k, frt.offsetMax.y);
        // La matière n'est pas étirée : on n'en montre que la part remplie.
        g.fill.uvRect = new Rect(0f, 0f, k, 1f);
        g.fill.enabled = k > 0.001f;
        g.marker.anchoredPosition = new Vector2(4f + g.inner * k, 0f);
        g.marker.gameObject.SetActive(k > 0.001f && k < 0.999f);
        g.text.text = dead ? "MORT" : label + " " + value + " / " + max;
    }

    public void SetHealth(int hp, bool dead) => SetGauge(health, hp, GameRules.MaxHp, dead, "PV");
    public void SetSerenity(int value, bool dead) => SetGauge(serenity, value, GameRules.MaxSerenity, dead, "Sérénité");

    public void SetInventory(PlayerStatus status)
    {
        for (int i = 0; i < GameRules.InventorySize; i++)
        {
            ItemType t = status[i];
            ItemDef def = Items.Get(t);
            bool selected = status.Selected == i;
            Sprite sprite = Icon(t);
            slotIcon[i].sprite = sprite;
            slotIcon[i].enabled = sprite != null;
            // Le nom ne s'affiche que si l'objet n'a pas d'icône.
            slotName[i].text = def != null && sprite == null ? def.shortName : "";
            slotBg[i].color = selected ? SlotSelectedColor : SlotColor;
            // Case sélectionnée (colorée et légèrement grossie) : textes en noir.
            bool light = UiKit.ReadableOn(slotBg[i].color) == Color.black;
            slotName[i].color = light ? Color.black : def != null ? Color.Lerp(def.color, Color.white, 0.35f) : Color.white;
            slotNumber[i].color = light ? Color.black : UiKit.MutedText;
            slotBg[i].rectTransform.localScale = Vector3.one * (selected ? 1.15f : 1f);
        }
    }

    Sprite Icon(ItemType type)
    {
        if (type == ItemType.None) return null;
        if (!icons.TryGetValue(type, out var sprite)) icons[type] = sprite = Resources.Load<Sprite>("Icons/" + type);
        return sprite;
    }

    // Cap du personnage en degrés (0 = Nord, 90 = Est).
    public void SetCompass(bool visible, float heading)
    {
        if (compassRoot.activeSelf != visible) compassRoot.SetActive(visible);
        if (!visible) return;
        float pxPerDegree = CompassWidth / CompassSpan;
        foreach (var (rt, angle) in compassMarks)
        {
            float delta = Mathf.DeltaAngle(heading, angle);
            bool shown = Mathf.Abs(delta) < CompassSpan * 0.5f;
            if (rt.gameObject.activeSelf != shown) rt.gameObject.SetActive(shown);
            if (shown) rt.anchoredPosition = new Vector2(delta * pxPerDegree, rt.anchoredPosition.y);
        }
    }

    public void FlashDamage() => flashTimer = FlashDuration;
    public void SetPrompt(string text) => promptText.text = text;
    public void SetSpectate(string text) => spectateText.text = text;

    public void Toast(string message)
    {
        toastText.text = message;
        toastTimer = 3f;
        toastText.color = new Color(1f, 0.85f, 0.45f, 1f);
    }

    public void ShowGameOver(string details, bool canRestart)
    {
        gameOverText.text = details;
        restartButton.SetActive(canRestart);
        gameOverPanel.SetActive(true);
        GameOverVisible = true;
    }

    public void HideGameOver()
    {
        gameOverPanel.SetActive(false);
        GameOverVisible = false;
    }

    void Update()
    {
        if (flashTimer > 0f)
        {
            // Montée rapide puis fondu, avec un léger battement, le tout en moins d'une seconde.
            flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime);
            float k = flashTimer / FlashDuration;
            float alpha = Mathf.Min(1f, (1f - k) * 8f) * k * (0.85f + 0.15f * Mathf.Sin(k * 25f));
            damageFlash.color = new Color(0.85f, 0.02f, 0.02f, 0.7f * alpha);
        }


        if (toastTimer > 0f)
        {
            toastTimer -= Time.unscaledDeltaTime;
            var c = toastText.color;
            c.a = Mathf.Clamp01(toastTimer / 0.5f);
            toastText.color = c;
            if (toastTimer <= 0f) toastText.text = "";
        }
    }
}
