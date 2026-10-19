using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Construction d'interface par code (lobby, HUD, Terminal).
public static class UiKit
{
    public static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.92f);
    public static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color PrimaryColor = new Color(0.29f, 0.56f, 1f);
    public static readonly Color SecondaryColor = new Color(0.2f, 0.75f, 0.5f);
    public static readonly Color DangerColor = new Color(0.85f, 0.2f, 0.2f);
    public static readonly Color NeutralColor = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color MutedText = new Color(1f, 1f, 1f, 0.6f);

    public static Font Font => GameArt.Font;

    // Noir sur fond clair, blanc sur fond sombre. Un fond semi-transparent est considéré posé sur un panneau sombre.
    public static Color ReadableOn(Color background)
    {
        Color c = Color.Lerp(PanelColor, background, background.a);
        float luminance = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        return luminance > 0.45f ? Color.black : Color.white;
    }

    public static Canvas CreateCanvas(Transform parent, string name, int order)
    {
        if (Object.FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static GameObject Panel(Transform parent, string objName, Vector2 size, Color? color = null)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        go.GetComponent<Image>().color = color ?? PanelColor;
        return go;
    }

    public static RectTransform Place(GameObject go, Vector2 anchor, Vector2 pos)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        return rt;
    }

    public static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static VerticalLayoutGroup Column(GameObject go, int padding, float spacing)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        return h;
    }

    public static void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    public static Text Label(Transform parent, string text, int size, FontStyle style, Color color, float height,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.supportRichText = true;
        t.raycastTarget = false;
        // Sinon le texte disparaît entièrement si la hauteur de ligne de la police dépasse la boîte.
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static InputField Field(Transform parent, string placeholder, string value, int maxChars)
    {
        var go = new GameObject("Field", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 54;
        go.GetComponent<Image>().color = FieldColor;

        Text ph = FieldText(go.transform, "Placeholder", placeholder, new Color(1f, 1f, 1f, 0.35f));
        ph.fontStyle = FontStyle.Italic;
        Text txt = FieldText(go.transform, "Text", "", Color.white);

        var input = go.GetComponent<InputField>();
        input.textComponent = txt;
        input.placeholder = ph;
        input.characterLimit = maxChars;
        input.lineType = InputField.LineType.SingleLine;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.29f, 0.56f, 1f, 0.5f);
        input.text = value;
        return input;
    }

    static Text FieldText(Transform parent, string objName, string text, Color color)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16, 6);
        rt.offsetMax = new Vector2(-16, -6);
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = 20;
        t.color = color;
        t.alignment = TextAnchor.MiddleLeft;
        t.supportRichText = false;
        return t;
    }

    public static GameObject Button(Transform parent, string text, Color color, float height, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        colors.colorMultiplier = 1f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var label = Label(go.transform, text, 18, FontStyle.Bold, ReadableOn(color), height);
        Stretch(label.rectTransform);
        return go;
    }

    // ---------- Style « coup de pinceau » (lobby) ----------

    static readonly System.Collections.Generic.Dictionary<string, Sprite> brushes = new System.Collections.Generic.Dictionary<string, Sprite>();

    static float Hash(int n)
    {
        uint h = (uint)n * 374761393u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h & 0xFFFF) / 65535f;
    }

    // Bruit de valeur 1D lissé.
    static float Noise1(float x, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i;
        f = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i * 7 + seed * 131), Hash((i + 1) * 7 + seed * 131), f);
    }

    // Trace de peinture blanche (teintée par la couleur de l'image) : bords déchirés, poils du pinceau aux extrémités,
    // traînées le long du geste. 'vertical' : coup de pinceau de haut en bas (grands panneaux).
    static Sprite BrushSprite(int w, int h, int seed, bool vertical)
    {
        string key = w + "x" + h + "_" + seed + (vertical ? "v" : "h");
        if (brushes.TryGetValue(key, out var cached) && cached != null) return cached;
        int len = vertical ? h : w, thick = vertical ? w : h;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        float side = thick * 0.05f, end = Mathf.Min(len * 0.02f, thick * 0.35f);
        for (int a = 0; a < len; a++)       // le long du geste
            for (int b = 0; b < thick; b++) // en travers
            {
                float u = a, v = b;
                // Bords le long du geste : ondulation lente + petites déchirures.
                float lowEdge = side * (0.3f + 0.7f * Noise1(u / 40f, seed)) + side * 0.5f * Mathf.Pow(Noise1(u / 9f, seed + 1), 6f);
                float highEdge = side * (0.3f + 0.7f * Noise1(u / 37f, seed + 2)) + side * 0.5f * Mathf.Pow(Noise1(u / 8f, seed + 3), 6f);
                // Extrémités : chaque poil du pinceau commence et finit à sa propre position.
                float bristle = Noise1(v / 2.5f, seed + 4);
                float startEdge = end * (0.3f + 0.9f * bristle * Noise1(v / 18f, seed + 5));
                float endEdge = end * (0.3f + 0.9f * Noise1(v / 2.2f, seed + 6) * Noise1(v / 15f, seed + 7));
                float d = Mathf.Min(Mathf.Min(v - lowEdge, thick - 1 - v - highEdge), Mathf.Min(u - startEdge, len - 1 - u - endEdge));
                float alpha = Mathf.Clamp01(d + 0.5f);
                // Pinceau sec : quelques trous près des extrémités.
                float dry = Mathf.Min(u - startEdge, len - 1 - u - endEdge) / (end * 2f);
                if (dry < 1f && Noise1(v / 1.7f + u / 30f, seed + 8) > 0.7f + dry * 0.4f) alpha = 0f;
                // Traînées de peinture le long du geste.
                float streak = Noise1(v / 1.6f, seed + 9) * 0.6f + Noise1(v / 7f + u / 90f, seed + 10) * 0.4f;
                float shade = 0.84f + 0.16f * streak + 0.04f * (Noise1(u / 12f + v / 3f, seed + 11) - 0.5f);
                int x = vertical ? b : a, y = vertical ? a : b;
                px[y * w + x] = new Color(shade, shade, shade, alpha);
            }
        tex.SetPixels(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        return brushes[key] = sprite;
    }

    // Texture d'un coup de pinceau horizontal (le long de x), pour l'appliquer sur un maillage (jauge de la Balise du Hub).
    public static Texture2D BrushTexture(int w, int h, int seed) => BrushSprite(w, h, seed, false).texture;

    // Applique une trace de pinceau de la couleur voulue à l'image d'un panneau, d'un bouton ou d'un champ.
    public static void Paint(GameObject go, Color color, int seed)
    {
        var img = go.GetComponent<Image>();
        var rt = (RectTransform)go.transform;
        Vector2 size = rt.sizeDelta;
        var le = go.GetComponent<LayoutElement>();
        if (le != null && le.preferredHeight > 0f) size.y = le.preferredHeight;
        if (le != null && le.preferredWidth > 0f) size.x = le.preferredWidth;
        bool vertical = size.y > size.x * 1.2f;
        float aspect = size.x > 1f && size.y > 1f ? size.x / size.y : 5f;
        int h = vertical ? 720 : 96;
        int w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 64, 1024);
        img.sprite = BrushSprite(w, h, seed, vertical);
        img.type = Image.Type.Simple;
        img.color = color;
    }

    // Sélecteur "<  Perso 2  >".
    public static Text Selector(Transform parent, float height, UnityEngine.Events.UnityAction prev, UnityEngine.Events.UnityAction next)
    {
        var row = Row(parent, height, 8);
        var left = Button(row.transform, "<", NeutralColor, height, prev);
        left.GetComponent<LayoutElement>().preferredWidth = 54;
        var label = Label(row.transform, "", 20, FontStyle.Bold, Color.white, height);
        label.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;
        var right = Button(row.transform, ">", NeutralColor, height, next);
        right.GetComponent<LayoutElement>().preferredWidth = 54;
        return label;
    }
}
