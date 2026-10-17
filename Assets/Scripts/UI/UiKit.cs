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

        var label = Label(go.transform, text, 18, FontStyle.Bold, Color.white, height);
        Stretch(label.rectTransform);
        return go;
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
