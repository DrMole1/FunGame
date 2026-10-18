using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lobby de connexion (pseudo, skin, héberger / rejoindre) et menu de pause en jeu (Échap).
// Style du GDD (6.1) : fond noir opaque et boutons / champs en coups de pinceau.
public class LobbyUI : MonoBehaviour
{
    static readonly Color PanelBlack = new Color(0.09f, 0.09f, 0.1f, 1f);
    static readonly Color HostRed = new Color(0.93f, 0.27f, 0.33f);
    static readonly Color JoinBlue = new Color(0.12f, 0.2f, 0.45f);
    static readonly Color FieldGrey = new Color(0.27f, 0.27f, 0.29f);
    static readonly Color NeutralGrey = new Color(0.33f, 0.33f, 0.35f);

    NetworkGame net;

    GameObject lobbyPanel, pausePanel, hintPanel;
    InputField nameField, codeField;
    Button hostButton, joinButton;
    Text statusText, roomText, countText, copyLabel, lobbySkin, pauseSkin;
    readonly System.Collections.Generic.List<Text> musicLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> sfxLabels = new System.Collections.Generic.List<Text>();
    float copyFeedbackTimer;
    bool inGame;

    public void Init(NetworkGame network, string savedName, string roomFromUrl)
    {
        net = network;
        var canvas = UiKit.CreateCanvas(transform, "LobbyCanvas", 100);
        BuildLobby(canvas.transform, savedName, roomFromUrl);
        BuildPause(canvas.transform);
        ShowLobby(roomFromUrl.Length > 0 ? "Invitation reçue : choisissez un pseudo puis cliquez sur Rejoindre." : "");
    }

    // ---------- États ----------

    public void ShowLobby(string message)
    {
        inGame = false;
        lobbyPanel.SetActive(true);
        pausePanel.SetActive(false);
        hintPanel.SetActive(false);
        SetInteractable(true);
        statusText.text = message;
        RefreshSkin();
    }

    public void ShowConnecting(string message)
    {
        SetInteractable(false);
        statusText.text = message;
    }

    public void ShowHud(string code, bool isHost)
    {
        inGame = true;
        lobbyPanel.SetActive(false);
        roomText.text = (isHost ? "Vous hébergez la partie  " : "Partie  ") + "<b>" + code + "</b>";
        copyLabel.text = "Copier le lien d'invitation";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        RefreshSkin();
    }

    public void SetPlayerCount(int count)
    {
        countText.text = count + (count > 1 ? " joueurs connectés" : " joueur connecté") + " (max " + GameRules.MaxPlayers + ")";
    }

    void SetInteractable(bool on)
    {
        nameField.interactable = on;
        codeField.interactable = on;
        hostButton.interactable = on;
        joinButton.interactable = on;
    }

    void ChangeSkin(int delta)
    {
        net.SelectSkin(net.MySkin + delta);
        RefreshSkin();
    }

    void RefreshSkin()
    {
        string n = net.SkinCount > 0 ? net.SkinName(net.MySkin) : "-";
        lobbySkin.text = n;
        pauseSkin.text = n;
    }

    void Update()
    {
        if (copyFeedbackTimer > 0f)
        {
            copyFeedbackTimer -= Time.unscaledDeltaTime;
            if (copyFeedbackTimer <= 0f) copyLabel.text = "Copier le lien d'invitation";
        }

        // Menu de pause : visible quand la souris est libérée.
        bool paused = inGame && Cursor.lockState != CursorLockMode.Locked && !TerminalUI.IsOpen && !GameHud.GameOverVisible;
        if (pausePanel.activeSelf != paused) pausePanel.SetActive(paused);
        bool hint = inGame && !TerminalUI.IsOpen && !GameHud.GameOverVisible && paused;
        if (hintPanel.activeSelf != hint) hintPanel.SetActive(hint);
    }

    // ---------- Construction ----------

    void BuildLobby(Transform root, string savedName, string roomFromUrl)
    {
        lobbyPanel = UiKit.Panel(root, "Lobby", new Vector2(460, 700));
        // À gauche : le personnage choisi reste visible au centre de l'écran.
        UiKit.Place(lobbyPanel, new Vector2(0f, 0.5f), new Vector2(40, 0));
        var layout = UiKit.Column(lobbyPanel, 36, 10);
        Transform p = lobbyPanel.transform;

        UiKit.Paint(lobbyPanel, PanelBlack, 1);
        UiKit.Label(p, "Pavlov's Theory", 44, FontStyle.Bold, Color.white, 56);
        UiKit.Spacer(p, 4);

        UiKit.Label(p, "Pseudo", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        nameField = UiKit.Field(p, "Votre pseudo", savedName, 16);
        UiKit.Paint(nameField.gameObject, FieldGrey, 2);

        UiKit.Label(p, "Skin", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        lobbySkin = UiKit.Selector(p, 48, () => ChangeSkin(-1), () => ChangeSkin(1));
        PaintSelector(lobbySkin, 3);
        VolumeRows(p, 36, 20);

        UiKit.Spacer(p, 4);
        var hostGo = UiKit.Button(p, "Héberger une partie", HostRed, 54, () => net.HostGame(nameField.text));
        UiKit.Paint(hostGo, HostRed, 5);
        hostButton = hostGo.GetComponent<Button>();

        UiKit.Spacer(p, 4);
        UiKit.Label(p, "Rejoindre une partie", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        var row = UiKit.Row(p, 54, 10);
        codeField = UiKit.Field(row.transform, "Code (ex. K7QXP)", roomFromUrl, 8);
        codeField.characterValidation = InputField.CharacterValidation.Alphanumeric;
        codeField.GetComponent<LayoutElement>().flexibleWidth = 1;
        UiKit.Paint(codeField.gameObject, FieldGrey, 6);
        // Entrée dans le champ code = rejoindre.
        codeField.onEndEdit.AddListener(_ =>
        {
            if (joinButton.interactable && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)))
                net.JoinGame(nameField.text, codeField.text);
        });
        var joinGo = UiKit.Button(row.transform, "Rejoindre", JoinBlue, 54,
            () => net.JoinGame(nameField.text, codeField.text));
        joinGo.GetComponent<LayoutElement>().preferredWidth = 150;
        UiKit.Paint(joinGo, JoinBlue, 7);
        joinButton = joinGo.GetComponent<Button>();

        statusText = UiKit.Label(p, "", 16, FontStyle.Normal, new Color(1f, 0.82f, 0.45f), 48);
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        layout.padding = new RectOffset(36, 36, 30, 26);
    }

    // Volumes de la musique et des effets sonores (GDD 6.1 et 6.2), par pas de 10 %.
    void VolumeRows(Transform p, float height, int seed)
    {
        musicLabels.Add(VolumeRow(p, "Musique", height, seed, step => GameAudio.MusicVolume = Step(GameAudio.MusicVolume, step)));
        sfxLabels.Add(VolumeRow(p, "Effets sonores", height, seed + 2, step => GameAudio.SfxVolume = Step(GameAudio.SfxVolume, step)));
        RefreshVolumes();
    }

    static float Step(float value, int step) => Mathf.Round(value * 10f + step) / 10f;

    Text VolumeRow(Transform p, string title, float height, int seed, System.Action<int> change)
    {
        var row = UiKit.Row(p, height, 8);
        var label = UiKit.Label(row.transform, title, 15, FontStyle.Bold, UiKit.MutedText, height, TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().preferredWidth = 118;
        void Change(int step)
        {
            change(step);
            RefreshVolumes();
            GameAudio.Play2D("pop", 0.6f);
        }
        Text value = UiKit.Selector(row.transform, height, () => Change(-1), () => Change(1));
        value.transform.parent.GetComponent<LayoutElement>().flexibleWidth = 1;
        value.fontSize = 17;
        PaintSelector(value, seed);
        return value;
    }

    void RefreshVolumes()
    {
        foreach (var t in musicLabels) t.text = Mathf.RoundToInt(GameAudio.MusicVolume * 100f) + " %";
        foreach (var t in sfxLabels) t.text = Mathf.RoundToInt(GameAudio.SfxVolume * 100f) + " %";
    }

    // Boutons "<" et ">" d'un sélecteur, en coups de pinceau.
    static void PaintSelector(Text label, int seed)
    {
        foreach (var b in label.transform.parent.GetComponentsInChildren<Button>())
            UiKit.Paint(b.gameObject, NeutralGrey, seed++);
    }

    void BuildPause(Transform root)
    {
        pausePanel = UiKit.Panel(root, "Pause", new Vector2(340, 410));
        UiKit.Paint(pausePanel, PanelBlack, 11);
        UiKit.Place(pausePanel, new Vector2(0f, 1f), new Vector2(16, -16));
        UiKit.Column(pausePanel, 16, 8);
        Transform p = pausePanel.transform;

        roomText = UiKit.Label(p, "", 14, FontStyle.Normal, Color.white, 26, TextAnchor.MiddleLeft);
        countText = UiKit.Label(p, "", 15, FontStyle.Normal, UiKit.MutedText, 20, TextAnchor.MiddleLeft);
        var copy = UiKit.Button(p, "Copier le lien d'invitation", JoinBlue, 38, () =>
        {
            net.CopyInviteLink();
            copyLabel.text = "Lien copié !";
            copyFeedbackTimer = 2f;
        });
        copyLabel = copy.GetComponentInChildren<Text>();
        UiKit.Paint(copy, JoinBlue, 12);
        UiKit.Label(p, "Changer de skin", 15, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        pauseSkin = UiKit.Selector(p, 40, () => ChangeSkin(-1), () => ChangeSkin(1));
        PaintSelector(pauseSkin, 13);
        VolumeRows(p, 36, 15);
        UiKit.Spacer(p, 4);
        UiKit.Paint(UiKit.Button(p, "Quitter la partie", HostRed, 38, () => net.LeaveGame()), HostRed, 17);
        UiKit.Label(p, "Cliquez dans le jeu pour reprendre", 14, FontStyle.Italic, UiKit.MutedText, 22);

        hintPanel = UiKit.Panel(root, "Aide", new Vector2(1180, 64), new Color(0f, 0f, 0f, 0.55f));
        UiKit.Place(hintPanel, new Vector2(0.5f, 0f), new Vector2(0, 116));
        hintPanel.GetComponent<Image>().raycastTarget = false;
        var hint = UiKit.Label(hintPanel.transform,
            "ZQSD : bouger  ·  Shift : courir  ·  Ctrl (ou C) : marcher discrètement  ·  Espace : sauter  ·  A : aboyer  ·  Souris : caméra  ·  Molette : zoom\n" +
            "E / Clic gauche : interagir, ramasser, utiliser l'objet sélectionné  ·  1 à 5 : sélectionner un objet  ·  Échap : menu",
            15, FontStyle.Normal, UiKit.MutedText, 64);
        UiKit.Stretch(hint.rectTransform);
    }
}
