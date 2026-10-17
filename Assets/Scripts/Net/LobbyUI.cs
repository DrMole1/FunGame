using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lobby de connexion (pseudo, skin, héberger / rejoindre) et menu de pause en jeu (Échap).
public class LobbyUI : MonoBehaviour
{
    NetworkGame net;

    GameObject lobbyPanel, pausePanel, hintPanel;
    InputField nameField, codeField;
    Button hostButton, joinButton;
    Text statusText, roomText, countText, copyLabel, lobbySkin, pauseSkin;
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
        lobbyPanel = UiKit.Panel(root, "Lobby", new Vector2(460, 640));
        // À gauche : le personnage choisi reste visible au centre de l'écran.
        UiKit.Place(lobbyPanel, new Vector2(0f, 0.5f), new Vector2(40, 0));
        var layout = UiKit.Column(lobbyPanel, 36, 12);
        Transform p = lobbyPanel.transform;

        UiKit.Label(p, "FunGame", 44, FontStyle.Bold, Color.white, 56);
        UiKit.Label(p, "Survivez au labyrinthe, remplissez le quota", 16, FontStyle.Normal, UiKit.MutedText, 24);
        UiKit.Spacer(p, 6);

        UiKit.Label(p, "Pseudo", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        nameField = UiKit.Field(p, "Votre pseudo", savedName, 16);

        UiKit.Label(p, "Skin", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        lobbySkin = UiKit.Selector(p, 48, () => ChangeSkin(-1), () => ChangeSkin(1));

        UiKit.Spacer(p, 6);
        hostButton = UiKit.Button(p, "Héberger une partie", UiKit.PrimaryColor, 54,
            () => net.HostGame(nameField.text)).GetComponent<Button>();

        UiKit.Spacer(p, 4);
        UiKit.Label(p, "Rejoindre une partie", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        var row = UiKit.Row(p, 54, 10);
        codeField = UiKit.Field(row.transform, "Code (ex. K7QXP)", roomFromUrl, 8);
        codeField.characterValidation = InputField.CharacterValidation.Alphanumeric;
        codeField.GetComponent<LayoutElement>().flexibleWidth = 1;
        // Entrée dans le champ code = rejoindre.
        codeField.onEndEdit.AddListener(_ =>
        {
            if (joinButton.interactable && (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)))
                net.JoinGame(nameField.text, codeField.text);
        });
        var joinGo = UiKit.Button(row.transform, "Rejoindre", UiKit.SecondaryColor, 54,
            () => net.JoinGame(nameField.text, codeField.text));
        joinGo.GetComponent<LayoutElement>().preferredWidth = 150;
        joinButton = joinGo.GetComponent<Button>();

        statusText = UiKit.Label(p, "", 16, FontStyle.Normal, new Color(1f, 0.82f, 0.45f), 48);
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        layout.padding = new RectOffset(36, 36, 30, 26);
    }

    void BuildPause(Transform root)
    {
        pausePanel = UiKit.Panel(root, "Pause", new Vector2(340, 330));
        UiKit.Place(pausePanel, new Vector2(0f, 1f), new Vector2(16, -16));
        UiKit.Column(pausePanel, 16, 8);
        Transform p = pausePanel.transform;

        roomText = UiKit.Label(p, "", 14, FontStyle.Normal, Color.white, 26, TextAnchor.MiddleLeft);
        countText = UiKit.Label(p, "", 15, FontStyle.Normal, UiKit.MutedText, 20, TextAnchor.MiddleLeft);
        var copy = UiKit.Button(p, "Copier le lien d'invitation", UiKit.PrimaryColor, 38, () =>
        {
            net.CopyInviteLink();
            copyLabel.text = "Lien copié !";
            copyFeedbackTimer = 2f;
        });
        copyLabel = copy.GetComponentInChildren<Text>();
        UiKit.Label(p, "Changer de skin", 15, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        pauseSkin = UiKit.Selector(p, 40, () => ChangeSkin(-1), () => ChangeSkin(1));
        UiKit.Spacer(p, 4);
        UiKit.Button(p, "Quitter la partie", UiKit.NeutralColor, 38, () => net.LeaveGame());
        UiKit.Label(p, "Cliquez dans le jeu pour reprendre", 14, FontStyle.Italic, UiKit.MutedText, 22);

        hintPanel = UiKit.Panel(root, "Aide", new Vector2(1180, 64), new Color(0f, 0f, 0f, 0.55f));
        UiKit.Place(hintPanel, new Vector2(0.5f, 0f), new Vector2(0, 116));
        hintPanel.GetComponent<Image>().raycastTarget = false;
        var hint = UiKit.Label(hintPanel.transform,
            "ZQSD : bouger  ·  Shift : courir  ·  Ctrl (ou C) : marcher discrètement  ·  Espace : sauter  ·  Souris : caméra  ·  Molette : zoom\n" +
            "E / Clic gauche : interagir, ramasser, utiliser l'objet sélectionné  ·  1 à 5 : sélectionner un objet  ·  Échap : menu",
            15, FontStyle.Normal, UiKit.MutedText, 64);
        UiKit.Stretch(hint.rectTransform);
    }
}
