using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lobby de connexion (pseudo, skin, héberger / rejoindre) et menu de pause en jeu (Échap).
// Style du GDD (6.1) : fond noir opaque et boutons / champs en coups de pinceau.
public class LobbyUI : MonoBehaviour
{
    static readonly Color PanelBlack = new Color(0.09f, 0.09f, 0.1f, 1f);
    // Hauteur du fond du menu (le panneau fait 700 pour un écran de référence de 720).
    const float LobbyBackdropHeight = 820f;
    static readonly Color HostRed = new Color(0.93f, 0.27f, 0.33f);
    static readonly Color JoinBlue = new Color(0.12f, 0.2f, 0.45f);
    static readonly Color FieldGrey = new Color(0.27f, 0.27f, 0.29f);
    static readonly Color NeutralGrey = new Color(0.33f, 0.33f, 0.35f);

    NetworkGame net;

    GameObject lobbyPanel, pausePanel, hintPanel;
    Text resumeLabel;
    InputField nameField, codeField;
    Button hostButton, joinButton;
    Text statusText, roomText, countText, copyLabel, lobbySkin, pauseSkin;
    readonly System.Collections.Generic.List<Text> musicLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> sfxLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> brightLabels = new System.Collections.Generic.List<Text>();
    float copyFeedbackTimer;
    Text versionText;
    bool inGame;

    public void Init(NetworkGame network, string savedName, string roomFromUrl)
    {
        net = network;
        var canvas = UiKit.CreateCanvas(transform, "LobbyCanvas", 100);
        BuildLobby(canvas.transform, savedName, roomFromUrl);
        BuildPause(canvas.transform);
        BuildVersion(canvas.transform);
        ShowLobby(roomFromUrl.Length > 0 ? "Invitation reçue : choisissez un pseudo puis cliquez sur Rejoindre." : "");
    }

    // ---------- États ----------

    public void ShowLobby(string message)
    {
        inGame = false;
        lobbyPanel.SetActive(true);
        versionText.gameObject.SetActive(true);
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
        versionText.gameObject.SetActive(false);
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
        GameAudio.Play2D("skin", 0.6f, 0.05f);
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
        // Après avoir quitté le Terminal ou un Post-It avec Échap, pas de menu pause : on revient directement au jeu.
        bool paused = inGame && Cursor.lockState != CursorLockMode.Locked && !TerminalUI.IsOpen && !PostItUI.IsOpen && !GameHud.GameOverVisible
            && !OrbitCamera.ResumePending;
        if (pausePanel.activeSelf != paused) pausePanel.SetActive(paused);
        if (hintPanel.activeSelf != paused) hintPanel.SetActive(paused);
        // Clignotement lent, en fondu.
        if (paused)
        {
            var c = resumeLabel.color;
            c.a = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / 2.6f));
            resumeLabel.color = c;
        }
    }

    // ---------- Construction ----------

    // Version du jeu, discrète, en bas à droite de l'écran du lobby.
    void BuildVersion(Transform root)
    {
        versionText = UiKit.Label(root, GameRules.Version, 16, FontStyle.Bold, new Color(1f, 1f, 1f, 0.75f), 24, TextAnchor.LowerRight);
        var rt = UiKit.Place(versionText.gameObject, new Vector2(1f, 0f), new Vector2(-16f, 12f));
        rt.sizeDelta = new Vector2(200f, 24f);
        versionText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
    }

    void BuildLobby(Transform root, string savedName, string roomFromUrl)
    {
        lobbyPanel = UiKit.Panel(root, "Lobby", new Vector2(460, 700));
        // À gauche : le personnage choisi reste visible au centre de l'écran.
        UiKit.Place(lobbyPanel, new Vector2(0f, 0.5f), new Vector2(40, 0));
        var layout = UiKit.Column(lobbyPanel, 36, 10);
        Transform p = lobbyPanel.transform;

        // Fond peint plus haut que le panneau : les extrémités du coup de pinceau sortent de l'écran.
        lobbyPanel.GetComponent<Image>().color = Color.clear;
        var backdrop = UiKit.Panel(p, "Fond", new Vector2(460, LobbyBackdropHeight));
        backdrop.AddComponent<LayoutElement>().ignoreLayout = true;
        backdrop.transform.SetAsFirstSibling();
        UiKit.Paint(backdrop, PanelBlack, 1);
        backdrop.GetComponent<Image>().raycastTarget = false;
        UiKit.Label(p, "Pavlov's Theory", 44, FontStyle.Bold, Color.white, 56);
        UiKit.Spacer(p, 4);

        UiKit.Label(p, "Pseudo", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        // Laissé vide, le pseudo devient le nom du skin choisi.
        nameField = UiKit.Field(p, "Votre pseudo", savedName, 16);
        UiKit.Paint(nameField.gameObject, FieldGrey, 2);

        UiKit.Label(p, "Skin", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        lobbySkin = UiKit.Selector(p, 48, () => ChangeSkin(-1), () => ChangeSkin(1));
        PaintSelector(lobbySkin, 3);
        VolumeRows(p, 36, 20, false);

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

    // Volumes de la musique et des effets sonores (GDD 6.1 et 6.2), et luminosité, par pas de 10 %.
    void VolumeRows(Transform p, float height, int seed, bool brightness)
    {
        musicLabels.Add(VolumeRow(p, "Musique", height, seed, step => GameAudio.MusicVolume = Step(GameAudio.MusicVolume, step)));
        sfxLabels.Add(VolumeRow(p, "Effets sonores", height, seed + 2, step => GameAudio.SfxVolume = Step(GameAudio.SfxVolume, step)));
        // Luminosité hors du Hub (50 % par défaut), dans le menu pause seulement (le lobby est déjà plein).
        if (brightness) brightLabels.Add(VolumeRow(p, "Luminosité", height, seed + 4, step => Brightness.Value = Step(Brightness.Value, step)));
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
        foreach (var t in brightLabels) t.text = Mathf.RoundToInt(Brightness.Value * 100f) + " %";
    }

    // Boutons "<" et ">" d'un sélecteur, en coups de pinceau.
    static void PaintSelector(Text label, int seed)
    {
        foreach (var b in label.transform.parent.GetComponentsInChildren<Button>())
            UiKit.Paint(b.gameObject, NeutralGrey, seed++);
    }

    void BuildPause(Transform root)
    {
        // À gauche : partie, skin, volumes, quitter.
        pausePanel = UiKit.Panel(root, "Pause", new Vector2(390, 514));
        UiKit.Paint(pausePanel, PanelBlack, 11);
        UiKit.Place(pausePanel, new Vector2(0f, 1f), new Vector2(16, -16));
        UiKit.Column(pausePanel, 34, 8);
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
        VolumeRows(p, 36, 15, true);
        UiKit.Spacer(p, 4);
        UiKit.Paint(UiKit.Button(p, "Quitter la partie", HostRed, 38, () => net.LeaveGame()), HostRed, 17);

        // À droite : "Échap pour reprendre le jeu" (clignote lentement), puis les contrôles, un par ligne.
        // Le centre de l'écran reste transparent : on voit le jeu.
        hintPanel = new GameObject("Droite", typeof(RectTransform));
        hintPanel.transform.SetParent(root, false);
        UiKit.Place(hintPanel, new Vector2(1f, 1f), new Vector2(-16, -16));
        ((RectTransform)hintPanel.transform).sizeDelta = new Vector2(400, 520);

        resumeLabel = UiKit.Label(hintPanel.transform, "Échap pour reprendre le jeu", 22, FontStyle.BoldAndItalic, Color.white, 40, TextAnchor.MiddleRight);
        UiKit.Place(resumeLabel.gameObject, new Vector2(1f, 1f), new Vector2(-12, 0));
        resumeLabel.rectTransform.sizeDelta = new Vector2(400, 40);
        resumeLabel.raycastTarget = false;

        string[] controls =
        {
            "ZQSD : se déplacer",
            "Shift : courir",
            "C : marcher discrètement",
            "Espace : sauter",
            "A : aboyer",
            "Souris : orienter la caméra",
            "Molette : zoom (1re personne au plus près)",
            "E / Clic gauche : interagir, ramasser",
            "Clic gauche : utiliser l'objet sélectionné",
            "1 à 5 : sélectionner un objet",
            "Échap : menu pause",
        };
        var list = UiKit.Panel(hintPanel.transform, "Controles", new Vector2(400, 68 + 26 + controls.Length * 25));
        UiKit.Paint(list, PanelBlack, 19);
        UiKit.Place(list, new Vector2(1f, 1f), new Vector2(0, -52));
        UiKit.Column(list, 34, 3);
        UiKit.Label(list.transform, "Contrôles", 16, FontStyle.Bold, UiKit.MutedText, 24, TextAnchor.MiddleLeft);
        foreach (string line in controls)
            UiKit.Label(list.transform, line, 15, FontStyle.Normal, Color.white, 22, TextAnchor.MiddleLeft);
        foreach (var g in hintPanel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
    }
}
