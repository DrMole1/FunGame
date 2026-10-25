using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Lobby de connexion (pseudo, skin, héberger / rejoindre) et menu de pause en jeu (Échap).
// Style du GDD (6.1) : fond noir opaque et boutons / champs en coups de pinceau.
public class LobbyUI : MonoBehaviour
{
    static readonly Color PanelBlack = new Color(0.09f, 0.09f, 0.1f, 1f);
    // Hauteur du fond du menu (le panneau fait 700 pour un écran de référence de 720).
    const float LobbyBackdropHeight = 900f;
    static readonly Color HostRed = new Color(0.93f, 0.27f, 0.33f);
    static readonly Color QuitRed = new Color(0.55f, 0.1f, 0.14f);
    static readonly Color JoinBlue = new Color(0.12f, 0.2f, 0.45f);
    static readonly Color FieldGrey = new Color(0.27f, 0.27f, 0.29f);
    static readonly Color NeutralGrey = new Color(0.33f, 0.33f, 0.35f);

    NetworkGame net;

    GameObject lobbyPanel, pausePanel, hintPanel;
    // Fond peint du lobby (plus haut que le panneau) : la zone survolée qui assombrit la scène.
    RectTransform lobbyBackdrop;
    Text resumeLabel;
    InputField nameField, codeField;
    Button hostButton, joinButton, soloButton;
    GameObject copyButton;
    Text statusText, roomText, countText, copyLabel, lobbySkin, pauseSkin;
    readonly System.Collections.Generic.List<Text> musicLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> sfxLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> brightLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> grainLabels = new System.Collections.Generic.List<Text>();
    readonly System.Collections.Generic.List<Text> sensitivityLabels = new System.Collections.Generic.List<Text>();
    float copyFeedbackTimer;
    Text versionText;
    bool inGame;
    // Drapeaux des langues, en bas à droite (lobby et menu pause).
    GameObject langRow;
    readonly System.Collections.Generic.Dictionary<Lang, MenuButtonHover> flags = new System.Collections.Generic.Dictionary<Lang, MenuButtonHover>();
    // Contrôles du menu pause (touches modifiables) et fenêtre "Modifier Contrôles".
    readonly System.Collections.Generic.List<Text> controlLines = new System.Collections.Generic.List<Text>();
    ControlsUI controlsUI;
    // Panneau des contrôles (à droite du menu pause) : son survol assombrit aussi la scène.
    RectTransform controlsPanel;
    // Partie en cours, pour retraduire le menu pause quand la langue change.
    string roomCode;
    bool roomHost;
    int playerCount = 1;

    public void Init(NetworkGame network, string savedName, string roomFromUrl)
    {
        net = network;
        var canvas = UiKit.CreateCanvas(transform, "LobbyCanvas", 100);
        BuildLobby(canvas.transform, savedName, roomFromUrl);
        BuildPause(canvas.transform);
        BuildVersion(canvas.transform);
        BuildLanguages(canvas.transform);
        MenuButtonHover.AddTo(lobbyPanel);
        MenuButtonHover.AddTo(pausePanel);
        Loc.Changed += OnLanguageChanged;
        ShowLobby(roomFromUrl.Length > 0 ? Loc.T("Invitation reçue : choisissez un pseudo puis cliquez sur Rejoindre.") : "");
    }

    void OnDestroy()
    {
        Loc.Changed -= OnLanguageChanged;
        Controls.Changed -= RefreshControlsList;
    }

    void OnLanguageChanged()
    {
        foreach (var kv in flags) kv.Value.Selected = kv.Key == Loc.Current;
        RefreshRoom();
        SetPlayerCount(playerCount);
        RefreshControlsList();
    }

    // ---------- États ----------

    public void ShowLobby(string message)
    {
        inGame = false;
        if (controlsUI != null) controlsUI.Close(false);
        lobbyPanel.SetActive(true);
        versionText.gameObject.SetActive(true);
        pausePanel.SetActive(false);
        hintPanel.SetActive(false);
        SetInteractable(true);
        SetStatus(message);
        RefreshSkin();
    }

    // Message d'état masqué quand il est vide : le menu garde sa place en bas de l'écran.
    void SetStatus(string message)
    {
        statusText.text = message;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    public void ShowConnecting(string message)
    {
        SetInteractable(false);
        SetStatus(message);
    }

    public void ShowHud(string code, bool isHost)
    {
        inGame = true;
        lobbyPanel.SetActive(false);
        versionText.gameObject.SetActive(false);
        // Partie solo : pas de code, ni de lien d'invitation, ni de nombre de joueurs.
        bool solo = string.IsNullOrEmpty(code);
        roomCode = code;
        roomHost = isHost;
        RefreshRoom();
        copyButton.SetActive(!solo);
        countText.gameObject.SetActive(!solo);
        FitPausePanel();
        Loc.Bind(copyLabel, "Copier le lien d'invitation");
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        RefreshSkin();
    }

    // Menu pause à la hauteur de son contenu (plus court en solo, sans lien d'invitation ni nombre de joueurs) :
    // la jauge de PV reste visible dessous. (Pas de ContentSizeFitter : le coup de pinceau du fond a sa propre
    // hauteur préférée, qui l'emporterait.)
    void FitPausePanel()
    {
        var layout = pausePanel.GetComponent<VerticalLayoutGroup>();
        float h = layout.padding.top + layout.padding.bottom;
        int shown = 0;
        foreach (Transform t in pausePanel.transform)
        {
            // (Menu encore caché ici : on lit la hauteur déclarée de chaque ligne, pas celle calculée par la mise en page.)
            var le = t.GetComponent<LayoutElement>();
            if (!t.gameObject.activeSelf || le == null || le.ignoreLayout) continue;
            h += Mathf.Max(0f, le.preferredHeight);
            shown++;
        }
        h += layout.spacing * Mathf.Max(0, shown - 1);
        var rt = (RectTransform)pausePanel.transform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
    }

    void RefreshRoom()
    {
        if (!inGame) return;
        roomText.text = string.IsNullOrEmpty(roomCode) ? Loc.T("<b>Partie solo</b>")
            : Loc.F(roomHost ? "Vous hébergez la partie  <b>{0}</b>" : "Partie  <b>{0}</b>", roomCode);
    }

    public void SetPlayerCount(int count)
    {
        playerCount = count;
        countText.text = Loc.N(count, "{0} joueur connecté (max {1})|{0} joueurs connectés (max {1})", count, GameRules.MaxPlayers);
    }

    void SetInteractable(bool on)
    {
        nameField.interactable = on;
        codeField.interactable = on;
        hostButton.interactable = on;
        joinButton.interactable = on;
        soloButton.interactable = on;
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
            if (copyFeedbackTimer <= 0f) Loc.Bind(copyLabel, "Copier le lien d'invitation");
        }

        // Menu de pause : visible quand la souris est libérée.
        // Après avoir quitté le Terminal ou un Post-It avec Échap, pas de menu pause : on revient directement au jeu.
        bool paused = inGame && Cursor.lockState != CursorLockMode.Locked && !TerminalUI.IsOpen && !PostItUI.IsOpen && !GameHud.GameOverVisible
            && !OrbitCamera.ResumePending;
        // Fenêtre "Modifier Contrôles" ouverte : les menus pause (gauche, droite, drapeaux) s'effacent, pour la clarté,
        // et reviennent à sa fermeture.
        bool pauseMenus = paused && !ControlsUI.IsOpen;
        if (pausePanel.activeSelf != pauseMenus) pausePanel.SetActive(pauseMenus);
        if (hintPanel.activeSelf != pauseMenus) hintPanel.SetActive(pauseMenus);
        bool flagsShown = lobbyPanel.activeSelf || pauseMenus;
        if (langRow.activeSelf != flagsShown) langRow.SetActive(flagsShown);
        // Souris sur le menu du lobby ou le menu pause : la scène s'assombrit (fondu dans FilmGrain).
        FilmGrain.MenuHover = Hovered(lobbyBackdrop) || Hovered(pausePanel.transform as RectTransform) || Hovered(controlsPanel)
            || controlsUI.Hovered;
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

    // Drapeaux des langues, juste au-dessus du numéro de version ; même place dans le menu pause.
    // La langue choisie garde l'aura beige.
    void BuildLanguages(Transform root)
    {
        Lang[] langs = { Lang.En, Lang.Fr, Lang.Es, Lang.Ru };
        const float w = 48f, h = 32f, gap = 10f;
        langRow = new GameObject("Langues", typeof(RectTransform));
        langRow.transform.SetParent(root, false);
        var rt = UiKit.Place(langRow, new Vector2(1f, 0f), new Vector2(-16f, 44f));
        rt.sizeDelta = new Vector2(langs.Length * w + (langs.Length - 1) * gap, h);
        for (int i = 0; i < langs.Length; i++)
        {
            Lang lang = langs[i];
            var go = new GameObject("Drapeau" + lang, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(langRow.transform, false);
            var frt = (RectTransform)go.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = new Vector2(i * (w + gap), 0f);
            frt.sizeDelta = new Vector2(w, h);
            var img = go.GetComponent<Image>();
            img.sprite = Flags.Get(lang);
            go.GetComponent<Button>().targetGraphic = img;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                Loc.Set(lang);
                GameAudio.Play2D("pop", 0.6f);
            });
            // Aura : cadre beige uni juste derrière le drapeau (4 px de chaque côté).
            var auraGo = new GameObject("Aura" + lang, typeof(RectTransform), typeof(Image));
            auraGo.transform.SetParent(langRow.transform, false);
            auraGo.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
            var art = (RectTransform)auraGo.transform;
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(0f, 0.5f);
            art.anchoredPosition = new Vector2(i * (w + gap) - 4f, 0f);
            art.sizeDelta = new Vector2(w + 8f, h + 8f);
            var auraImg = auraGo.GetComponent<Image>();
            auraImg.raycastTarget = false;
            var hover = go.AddComponent<MenuButtonHover>();
            hover.UseFrame(auraImg);
            hover.Selected = lang == Loc.Current;
            flags[lang] = hover;
        }
    }

    void BuildLobby(Transform root, string savedName, string roomFromUrl)
    {
        lobbyPanel = UiKit.Panel(root, "Lobby", new Vector2(460, 780));
        // À gauche : le personnage choisi reste visible au centre de l'écran.
        // Calé en bas : sous "Quitter", le même écart (8) qu'entre "Quitter" et le champ du code.
        // Toute la hauteur de l'écran : les lignes s'étirent pour la remplir, le titre reste visible en haut.
        var lobbyRect = UiKit.Place(lobbyPanel, Vector2.zero, new Vector2(40, 0));
        lobbyRect.anchorMax = new Vector2(0f, 1f);
        lobbyRect.sizeDelta = new Vector2(460, 0);
        var layout = UiKit.Column(lobbyPanel, 36, 8);
        layout.childAlignment = TextAnchor.LowerCenter;
        Transform p = lobbyPanel.transform;

        // Fond peint plus haut que le panneau : les extrémités du coup de pinceau sortent de l'écran.
        lobbyPanel.GetComponent<Image>().color = Color.clear;
        var backdrop = UiKit.Panel(p, "Fond", new Vector2(460, LobbyBackdropHeight));
        backdrop.AddComponent<LayoutElement>().ignoreLayout = true;
        lobbyBackdrop = backdrop.GetComponent<RectTransform>();
        lobbyBackdrop.anchorMin = lobbyBackdrop.anchorMax = new Vector2(0.5f, 0f);
        lobbyBackdrop.anchoredPosition = new Vector2(0f, LobbyBackdropHeight * 0.5f - 90f);
        backdrop.transform.SetAsFirstSibling();
        UiKit.Paint(backdrop, PanelBlack, 1);
        backdrop.GetComponent<Image>().raycastTarget = false;
        UiKit.Label(p, "Pavlov's Theory", 44, FontStyle.Bold, Color.white, 56);
        UiKit.Spacer(p, 4);

        UiKit.Label(p, "Pseudo", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        // Laissé vide, le pseudo devient le nom du skin choisi.
        nameField = UiKit.Field(p, "Votre pseudo", savedName, 16);
        UiKit.Paint(nameField.gameObject, FieldGrey, 2);
        // Partie seul, sans connexion ni code pour rejoindre.
        var soloGo = UiKit.Button(p, "Partie Solo", HostRed, 48, () => net.SoloGame(nameField.text));
        UiKit.Paint(soloGo, HostRed, 8);
        soloButton = soloGo.GetComponent<Button>();
        var hostGo = UiKit.Button(p, "Héberger une partie", HostRed, 54, () => net.HostGame(nameField.text));
        UiKit.Paint(hostGo, HostRed, 5);
        hostButton = hostGo.GetComponent<Button>();

        UiKit.Label(p, "Skin", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        lobbySkin = UiKit.Selector(p, 48, () => ChangeSkin(-1), () => ChangeSkin(1));
        PaintSelector(lobbySkin, 3);
        VolumeRows(p, 30, 20, false);

        UiKit.Spacer(p, 4);
        statusText = UiKit.Label(p, "", 16, FontStyle.Normal, new Color(1f, 0.82f, 0.45f), 48);
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.gameObject.SetActive(false);
        UiKit.Label(p, "Rejoindre une partie", 16, FontStyle.Bold, UiKit.MutedText, 22, TextAnchor.MiddleLeft);
        var row = UiKit.Row(p, 54, 10);
        codeField = UiKit.Field(row.transform, "Code (ex. K7QXPM)", roomFromUrl, 8);
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
        // Sous le champ du code ; sans effet pour l'instant.
        UiKit.Paint(UiKit.Button(p, "Quitter", QuitRed, 48, () => { }), QuitRed, 9);
        layout.padding = new RectOffset(36, 36, 30, 8);
    }

    // Volumes de la musique et des effets sonores (GDD 6.1 et 6.2), luminosité, sensibilité de la souris et grain, par pas de 10 %.
    // Luminosité, sensibilité et grain : dans le menu pause seulement.
    void VolumeRows(Transform p, float height, int seed, bool brightness)
    {
        musicLabels.Add(VolumeRow(p, "Musique", height, seed, step => GameAudio.MusicVolume = Step(GameAudio.MusicVolume, step)));
        sfxLabels.Add(VolumeRow(p, "Effets sonores", height, seed + 2, step => GameAudio.SfxVolume = Step(GameAudio.SfxVolume, step)));
        // Luminosité hors du Hub (50 % par défaut), dans le menu pause seulement (le lobby est déjà plein).
        if (brightness) brightLabels.Add(VolumeRow(p, "Luminosité", height, seed + 4, step => Brightness.Value = Step(Brightness.Value, step)));
        // Sensibilité de la souris pour la caméra, sous la luminosité (Note 05, V0.5).
        if (brightness) sensitivityLabels.Add(VolumeRow(p, "Sensibilité", height, seed + 6, step => MouseSensitivity.Value = Step(MouseSensitivity.Value, step)));
        // Grain d'écran, dans le menu pause seulement.
        if (brightness) grainLabels.Add(VolumeRow(p, "Grain", height, seed + 8, step => FilmGrain.Value = Step(FilmGrain.Value, step)));
        RefreshVolumes();
    }

    static bool Hovered(RectTransform rect) =>
        rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, null);

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
        foreach (var t in sensitivityLabels) t.text = Mathf.RoundToInt(MouseSensitivity.Value * 100f) + " %";
        foreach (var t in grainLabels) t.text = Mathf.RoundToInt(FilmGrain.Value * 100f) + " %";
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
        pausePanel = UiKit.Panel(root, "Pause", new Vector2(390, 602));
        UiKit.Paint(pausePanel, PanelBlack, 11);
        UiKit.Place(pausePanel, new Vector2(0f, 1f), new Vector2(16, -16));
        UiKit.Column(pausePanel, 34, 8);
        Transform p = pausePanel.transform;

        roomText = UiKit.Label(p, "", 14, FontStyle.Normal, Color.white, 26, TextAnchor.MiddleLeft);
        countText = UiKit.Label(p, "", 15, FontStyle.Normal, UiKit.MutedText, 20, TextAnchor.MiddleLeft);
        var copy = UiKit.Button(p, "Copier le lien d'invitation", JoinBlue, 38, () =>
        {
            net.CopyInviteLink();
            Loc.Bind(copyLabel, "Lien copié !");
            copyFeedbackTimer = 2f;
        });
        copyLabel = copy.GetComponentInChildren<Text>();
        copyButton = copy;
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

        // Lignes remplies selon les touches choisies (RefreshControlsList), sous le bouton "Modifier Contrôles".
        const int lines = 11;
        var list = UiKit.Panel(hintPanel.transform, "Controles", new Vector2(400, 68 + 26 + lines * 25 + 48));
        UiKit.Paint(list, PanelBlack, 19);
        controlsPanel = UiKit.Place(list, new Vector2(1f, 1f), new Vector2(0, -52));
        UiKit.Column(list, 34, 3);
        UiKit.Label(list.transform, "Contrôles", 16, FontStyle.Bold, UiKit.MutedText, 24, TextAnchor.MiddleLeft);
        for (int i = 0; i < lines; i++)
            controlLines.Add(UiKit.Label(list.transform, "", 15, FontStyle.Normal, Color.white, 22, TextAnchor.MiddleLeft));
        foreach (var g in hintPanel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

        UiKit.Spacer(list.transform, 6);
        var edit = UiKit.Button(list.transform, "Modifier Contrôles", JoinBlue, 38, () => controlsUI.Open());
        UiKit.Paint(edit, JoinBlue, 21);
        MenuButtonHover.AddTo(list);

        controlsUI = gameObject.AddComponent<ControlsUI>();
        controlsUI.Init();
        Controls.Changed += RefreshControlsList;
        RefreshControlsList();
    }

    // Liste des contrôles du menu pause, avec les touches choisies par le joueur.
    void RefreshControlsList()
    {
        string L(GameAction a) => Controls.Label(a);
        string move = L(GameAction.Forward) + L(GameAction.Left) + L(GameAction.Back) + L(GameAction.Right);
        if (move.Length != 4) move = L(GameAction.Forward) + " / " + L(GameAction.Left) + " / " + L(GameAction.Back) + " / " + L(GameAction.Right);
        var slots = new[] { L(GameAction.Slot1), L(GameAction.Slot2), L(GameAction.Slot3), L(GameAction.Slot4), L(GameAction.Slot5) };
        string slotKeys = string.Join("", slots) == "12345" ? Loc.F("{0} à {1}", "1", "5") : string.Join(" / ", slots);
        string[] text =
        {
            Loc.F("{0} : se déplacer", move),
            Loc.F("{0} : courir", L(GameAction.Run)),
            Loc.F("{0} : marcher discrètement", L(GameAction.Sneak)),
            Loc.F("{0} : sauter", L(GameAction.Jump)),
            Loc.F("{0} : aboyer", L(GameAction.Bark)),
            Loc.T("Souris : orienter la caméra"),
            Loc.T("Molette : zoom (1re personne au plus près)"),
            Loc.F("{0} : interagir, ramasser, utiliser l'objet", L(GameAction.Interact) + " / " + Loc.T("Clic gauche")),
            Loc.F("{0} : sélectionner un objet", slotKeys),
            Loc.F("{0} : danser", L(GameAction.Dance1) + " / " + L(GameAction.Dance2) + " / " + L(GameAction.Dance3)),
            Loc.T("Échap : menu pause"),
        };
        for (int i = 0; i < controlLines.Count; i++) controlLines[i].text = i < text.Length ? text[i] : "";
    }
}
