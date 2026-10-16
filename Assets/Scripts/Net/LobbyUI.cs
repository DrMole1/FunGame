using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Interface du lobby et HUD en jeu, construits entièrement par code.
public class LobbyUI : MonoBehaviour
{
    static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.92f);
    static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color PrimaryColor = new Color(0.29f, 0.56f, 1f);
    static readonly Color SecondaryColor = new Color(0.2f, 0.75f, 0.5f);
    static readonly Color NeutralColor = new Color(1f, 1f, 1f, 0.14f);
    static readonly Color MutedText = new Color(1f, 1f, 1f, 0.6f);

    NetworkGame net;
    Font font;

    GameObject lobbyPanel, hudPanel, hintPanel;
    InputField nameField, codeField;
    Button hostButton, joinButton;
    Text statusText, roomText, countText, copyLabel;
    float copyFeedbackTimer;

    public void Init(NetworkGame network, string savedName, string roomFromUrl)
    {
        net = network;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var canvasGo = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        BuildLobby(canvasGo.transform, savedName, roomFromUrl);
        BuildHud(canvasGo.transform);
        ShowLobby(roomFromUrl.Length > 0 ? "Invitation reçue : choisissez un pseudo puis cliquez sur Rejoindre." : "");
    }

    // ---------- États ----------

    public void ShowLobby(string message)
    {
        lobbyPanel.SetActive(true);
        hudPanel.SetActive(false);
        hintPanel.SetActive(false);
        SetInteractable(true);
        statusText.text = message;
    }

    public void ShowConnecting(string message)
    {
        SetInteractable(false);
        statusText.text = message;
    }

    public void ShowHud(string code, bool isHost)
    {
        lobbyPanel.SetActive(false);
        hudPanel.SetActive(true);
        hintPanel.SetActive(true);
        roomText.text = (isHost ? "Vous hébergez la partie  " : "Partie  ") + "<b>" + code + "</b>";
        copyLabel.text = "Copier le lien d'invitation";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void SetPlayerCount(int count)
    {
        countText.text = count + (count > 1 ? " joueurs connectés" : " joueur connecté");
    }

    void SetInteractable(bool on)
    {
        nameField.interactable = on;
        codeField.interactable = on;
        hostButton.interactable = on;
        joinButton.interactable = on;
    }

    void Update()
    {
        if (copyFeedbackTimer > 0f)
        {
            copyFeedbackTimer -= Time.unscaledDeltaTime;
            if (copyFeedbackTimer <= 0f) copyLabel.text = "Copier le lien d'invitation";
        }

    }

    // ---------- Construction ----------

    void BuildLobby(Transform root, string savedName, string roomFromUrl)
    {
        lobbyPanel = Panel(root, "Lobby", new Vector2(460, 560));
        var rt = (RectTransform)lobbyPanel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        var layout = Column(lobbyPanel, 36, 14);

        Label(lobbyPanel.transform, "FunGame", 44, FontStyle.Bold, Color.white, 56);
        Label(lobbyPanel.transform, "Cubes multijoueur", 18, FontStyle.Normal, MutedText, 26);
        Spacer(lobbyPanel.transform, 10);

        Label(lobbyPanel.transform, "Pseudo", 16, FontStyle.Bold, MutedText, 22, TextAnchor.MiddleLeft);
        nameField = Field(lobbyPanel.transform, "Votre pseudo", savedName, 16);

        Spacer(lobbyPanel.transform, 6);
        hostButton = MakeButton(lobbyPanel.transform, "Héberger une partie", PrimaryColor, 54,
            () => net.HostGame(nameField.text)).GetComponent<Button>();

        Spacer(lobbyPanel.transform, 6);
        Label(lobbyPanel.transform, "Rejoindre une partie", 16, FontStyle.Bold, MutedText, 22, TextAnchor.MiddleLeft);
        var row = new GameObject("JoinRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(lobbyPanel.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = 54;
        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 10;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        codeField = Field(row.transform, "Code (ex. K7QXP)", roomFromUrl, 8);
        codeField.characterValidation = InputField.CharacterValidation.Alphanumeric;
        codeField.GetComponent<LayoutElement>().flexibleWidth = 1;
        // Entrée dans le champ code = rejoindre.
        codeField.onEndEdit.AddListener(_ =>
        {
            if (joinButton.interactable && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)))
                net.JoinGame(nameField.text, codeField.text);
        });
        var joinGo = MakeButton(row.transform, "Rejoindre", SecondaryColor, 54,
            () => net.JoinGame(nameField.text, codeField.text));
        joinGo.GetComponent<LayoutElement>().preferredWidth = 150;
        joinButton = joinGo.GetComponent<Button>();

        statusText = Label(lobbyPanel.transform, "", 16, FontStyle.Normal, new Color(1f, 0.82f, 0.45f), 48);
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        layout.padding = new RectOffset(36, 36, 32, 28);
    }

    void BuildHud(Transform root)
    {
        hudPanel = Panel(root, "Hud", new Vector2(330, 172));
        var rt = (RectTransform)hudPanel.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16, -16);
        Column(hudPanel, 16, 8);

        roomText = Label(hudPanel.transform, "", 18, FontStyle.Normal, Color.white, 26, TextAnchor.MiddleLeft);
        countText = Label(hudPanel.transform, "", 15, FontStyle.Normal, MutedText, 20, TextAnchor.MiddleLeft);
        var copy = MakeButton(hudPanel.transform, "Copier le lien d'invitation", PrimaryColor, 38, () =>
        {
            net.CopyInviteLink();
            copyLabel.text = "Lien copié !";
            copyFeedbackTimer = 2f;
        });
        copyLabel = copy.GetComponentInChildren<Text>();
        MakeButton(hudPanel.transform, "Quitter la partie", NeutralColor, 38, () => net.LeaveGame());

        hintPanel = Panel(root, "Hint", new Vector2(760, 40));
        var hrt = (RectTransform)hintPanel.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0, 16);
        hintPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        hintPanel.GetComponent<Image>().raycastTarget = false;
        var hint = Label(hintPanel.transform, "ZQSD / Flèches : bouger   ·   Espace : sauter   ·   Clic : caméra   ·   Échap : libérer la souris",
            15, FontStyle.Normal, MutedText, 40);
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = Vector2.zero;
        hintRt.anchorMax = Vector2.one;
        hintRt.offsetMin = hintRt.offsetMax = Vector2.zero;
    }

    GameObject Panel(Transform parent, string objName, Vector2 size)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        go.GetComponent<Image>().color = PanelColor;
        return go;
    }

    static VerticalLayoutGroup Column(GameObject go, int padding, float spacing)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    static void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    Text Label(Transform parent, string text, int size, FontStyle style, Color color, float height,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = anchor;
        t.supportRichText = true;
        t.raycastTarget = false;
        return t;
    }

    InputField Field(Transform parent, string placeholder, string value, int maxChars)
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

    Text FieldText(Transform parent, string objName, string text, Color color)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16, 6);
        rt.offsetMax = new Vector2(-16, -6);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = 20;
        t.color = color;
        t.alignment = TextAnchor.MiddleLeft;
        t.supportRichText = false;
        return t;
    }

    GameObject MakeButton(Transform parent, string text, Color color, float height, UnityEngine.Events.UnityAction onClick)
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
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return go;
    }
}
