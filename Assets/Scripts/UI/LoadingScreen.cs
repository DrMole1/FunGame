using UnityEngine;
using UnityEngine.UI;

// Chapitre 9.2 (V0.6.6) : écran noir affiché pendant que les premières zones se génèrent, au-dessus de toute l'interface.
public static class LoadingScreen
{
    static GameObject root;
    static Text label;
    // Écran de chargement affiché : les menus (lobby, pause, drapeaux) sont retirés et rien n'est cliquable dessous.
    public static bool Visible => root != null && root.activeSelf;

    // 'percent' : de 0 à 100 (par tranches de 2 %, V0.6.10).
    public static void Show(int percent)
    {
        if (root == null) Build();
        if (!root.activeSelf && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        root.SetActive(true);
        label.text = Loc.T("Chargement…") + "  " + Mathf.Clamp(percent, 0, 100) + " %";
    }

    public static void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    static void Build()
    {
        root = new GameObject("EcranDeChargement", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        var back = new GameObject("Fond", typeof(RectTransform), typeof(Image));
        back.transform.SetParent(root.transform, false);
        var rect = back.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        back.GetComponent<Image>().color = Color.black;
        // Le fond noir intercepte la souris : ni clic ni survol (et son) sur les menus cachés dessous.
        back.GetComponent<Image>().raycastTarget = true;

        var text = new GameObject("Texte", typeof(RectTransform), typeof(Text));
        text.transform.SetParent(root.transform, false);
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        label = text.GetComponent<Text>();
        label.font = UiKit.Font;
        label.fontSize = 34;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        TextShake.AddTo(text);
    }
}
