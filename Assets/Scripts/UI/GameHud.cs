using UnityEngine;
using UnityEngine.UI;

// Interface du joueur en jeu : jauges de vie (rouge) et de sérénité (bleue), 5 cases d'inventaire, viseur, messages et écran
// de game over (V0.7.11 : plus de boussole). Textures procédurales selon la DA du GDD : jauges craquelées dans un cadre sombre
// avec un repère blanc, cases arrondies vert-de-gris.
public class GameHud : MonoBehaviour
{
    static readonly Color SlotColor = new Color(0.11f, 0.15f, 0.13f, 0.95f);
    static readonly Color SlotSelectedColor = new Color(0.95f, 0.75f, 0.25f, 0.95f);
    static readonly Color HealthColor = new Color(0.95f, 0.38f, 0.22f);
    static readonly Color SerenityColor = new Color(0.3f, 0.62f, 1f);
    // V0.7.2 (chapitre 3.5) : barre de faim orange, entre la vie et la sérénité.
    static readonly Color HungerColor = new Color(1f, 0.62f, 0.1f);
    public static readonly Color PerfumeColor = new Color(1f, 0.6f, 0.62f);
    const float GaugeWidth = 330f, GaugeHeight = 30f;

    public static bool GameOverVisible { get; private set; }

    class Gauge
    {
        public RawImage fill;
        public RectTransform marker;
        public Text text;
        public float inner;
    }

    GameObject root, gameOverPanel, restartButton;
    // Chaîne du Roi (V0.4.6) : une barre d'espace enfoncée et relâchée frénétiquement.
    GameObject spaceKey, useKey;
    RectTransform spaceKeyFace;
    Image spaceKeyImage;
    // Dans le Canon (V0.4.6) : le viseur devient un rond blanc et l'inventaire est caché.
    GameObject crosshairDot, crosshairRing, inventoryBar;
    Gauge health, hunger, serenity, perfume;
    GameObject perfumeFrame;
    Text promptText, toastText, spectateText, gameOverText;
    // V0.7.9 (9.3) : icône de l'ordre en cours, en bas à droite de l'écran (Resources/Art/Ordres, d'après Assets/Sprite/Order01 à
    // 03), d'un blanc modérément lumineux. Elle clignote 3 fois en apparaissant, reste jusqu'à la fin de l'ordre, puis s'efface.
    RawImage orderIcon;
    readonly Texture2D[] orderTextures = new Texture2D[GameRules.GlyphTypes];
    int orderShown = -1;
    float orderShownAt, orderHiddenAt = -100f;
    const float OrderIconSize = 112f, OrderBlinkPeriod = 0.36f, OrderBlinks = 3f, OrderFadeOut = 1.2f, OrderAlpha = 0.85f;
    readonly Image[] slotBg = new Image[GameRules.InventorySize];
    readonly Text[] slotName = new Text[GameRules.InventorySize];
    readonly Text[] slotNumber = new Text[GameRules.InventorySize];
    // V0.7.12 (Note 02) : compteur d'Os du Sac à Os, en bas à droite de la case (40 % plus grand que le nom d'un objet).
    readonly Text[] slotCount = new Text[GameRules.InventorySize];
    readonly Image[] slotIcon = new Image[GameRules.InventorySize];
    // Icônes d'inventaire (Resources/Icons/<ItemType>, créées par "FunGame/Générer les icônes d'inventaire").
    readonly System.Collections.Generic.Dictionary<ItemType, Sprite> icons = new System.Collections.Generic.Dictionary<ItemType, Sprite>();
    float toastTimer;
    RawImage damageFlash, whiteFlash, blackFade, dreadVeil, dreadDim, peaceVeil, peaceTop, peaceBottom;
    float peace;
    // Pulsations cardiaques : l'écran s'assombrit nettement (bords presque noirs, centre voilé) d'autant plus que le
    // Cauchemar est proche, et fonce à chaque battement. La racine carrée de la tension rend l'effet bien visible dès que
    // les pulsations commencent.
    const float DreadDarkness = 0.92f, DreadDim = 0.3f, DreadBeatBoost = 0.25f;
    float flashTimer, whiteTimer, blackTimer;
    // Fondu au noir d'un spectateur dont le joueur suivi se téléporte : le noir s'efface en 0,4 s.
    const float BlackFadeDuration = 0.4f;
    // Aura blanche du Roi : éclair blanc (luminosité +400 %) qui retombe en 0,5 s.
    const float WhiteFlashDuration = 0.5f, WhiteFlashBoost = 4f;
    // Mort du joueur local : éclair rouge, puis l'écran s'assombrit par les bords (-1 : inactif).
    RawImage deathDim, deathVeil;
    float deathTime = -1f;
    const float FlashDuration = 0.7f;
    // Tous les joueurs sont morts (-1 : inactif) : fondu au noir, silence, puis GAME OVER et bouton de retour au lobby.
    public const float WipeFadeDuration = 4.5f, WipeSilence = 1.2f;
    const float WipeTitleDuration = 2.6f, WipeButtonDelay = 3.5f;
    static readonly Color BloodRed = new Color(0.7f, 0.03f, 0.02f);
    GameObject wipeRoot;
    RawImage wipeBlack, wipeVeil;
    Text wipeTitle, wipeGlow, wipeSubtitle;
    // V0.7.23 (Note 07) : grain sur le rouge du titre GAME OVER (pas sur sa copie animée, en fond).
    RawImage wipeGrain;
    const float TitleGrainAlpha = 1f;
    CanvasGroup wipeButton;
    float wipeTime = -1f;

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

    // ================= Construction =================

    static readonly GameAction[] SlotActions = { GameAction.Slot1, GameAction.Slot2, GameAction.Slot3, GameAction.Slot4, GameAction.Slot5 };

    // Chaque case affiche la touche qui la sélectionne (1 à 5 par défaut).
    void RefreshSlotKeys()
    {
        for (int i = 0; i < slotNumber.Length && i < SlotActions.Length; i++)
            if (slotNumber[i] != null) slotNumber[i].text = Controls.Label(SlotActions[i]);
    }

    void OnDestroy()
    {
        Controls.Changed -= RefreshSlotKeys;
        Loc.Changed -= RefreshSlotKeys;
    }

    public void Init(System.Action onRestart, System.Action onQuit)
    {
        var canvas = UiKit.CreateCanvas(transform, "HudCanvas", 50);
        Texture2D vignette = Vignette(128);

        // V0.7.11 (7.15) : voile rose des Lunettes PEACE, sous tout le reste.
        peaceVeil = FullScreen(canvas.transform, "VoileRosePeace", null);
        // V0.7.13 (Note 08) : bordures arc-en-ciel en haut et en bas de l'écran (à la place des arcs-en-ciel du décor).
        peaceTop = RainbowBorder(canvas.transform, "ArcEnCielHaut", true);
        peaceBottom = RainbowBorder(canvas.transform, "ArcEnCielBas", false);
        dreadDim = FullScreen(canvas.transform, "VoilePulsations", null);
        dreadVeil = FullScreen(canvas.transform, "AssombrissementPulsations", vignette);
        // Flash rouge des dégâts (sous le reste de l'interface).
        damageFlash = FullScreen(canvas.transform, "FlashDegats", vignette);
        whiteFlash = FullScreen(canvas.transform, "FlashBlanc", null);
        blackFade = FullScreen(canvas.transform, "FonduNoir", null);
        deathDim = FullScreen(canvas.transform, "AssombrissementMort", null);
        deathVeil = FullScreen(canvas.transform, "VoileMort", vignette);

        root = new GameObject("Hud", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        UiKit.Stretch((RectTransform)root.transform);
        Transform r = root.transform;

        // Viseur au centre de l'écran.
        var dotOutline = UiKit.Panel(r, "ViseurContour", new Vector2(8, 8), new Color(0f, 0f, 0f, 0.6f));
        UiKit.Place(dotOutline, new Vector2(0.5f, 0.5f), Vector2.zero).GetComponent<Image>().raycastTarget = false;
        crosshairDot = dotOutline;
        crosshairRing = new GameObject("ViseurCanon", typeof(RectTransform), typeof(RawImage));
        crosshairRing.transform.SetParent(r, false);
        ((RectTransform)crosshairRing.transform).sizeDelta = new Vector2(44, 44);
        UiKit.Place(crosshairRing, new Vector2(0.5f, 0.5f), Vector2.zero);
        var ring = crosshairRing.GetComponent<RawImage>();
        ring.texture = RingTexture(64, 0.38f, 0.47f);
        ring.raycastTarget = false;
        crosshairRing.SetActive(false);
        var dot = UiKit.Panel(dotOutline.transform, "Viseur", new Vector2(4, 4), Color.white);
        UiKit.Place(dot, new Vector2(0.5f, 0.5f), Vector2.zero).GetComponent<Image>().raycastTarget = false;

        promptText = Floating(r, new Vector2(0.5f, 0.5f), new Vector2(0, -42), new Vector2(700, 30), 18);
        toastText = Floating(r, new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 34), 20);
        toastText.color = new Color(1f, 0.85f, 0.45f);
        spectateText = Floating(r, new Vector2(0.5f, 0f), new Vector2(0, 196), new Vector2(1000, 54), 18);
        var orderGo = new GameObject("IconeOrdre", typeof(RectTransform), typeof(RawImage));
        orderGo.transform.SetParent(r, false);
        ((RectTransform)orderGo.transform).sizeDelta = new Vector2(OrderIconSize, OrderIconSize);
        UiKit.Place(orderGo, new Vector2(1f, 0f), new Vector2(-OrderIconSize * 0.5f - 28f, OrderIconSize * 0.5f + 28f));
        orderIcon = orderGo.GetComponent<RawImage>();
        orderIcon.raycastTarget = false;
        // Léger halo sombre pour rester lisible sur un décor clair.
        var orderShadow = orderGo.AddComponent<Shadow>();
        orderShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        orderShadow.effectDistance = new Vector2(2f, -2f);
        for (int i = 0; i < orderTextures.Length; i++) orderTextures[i] = Resources.Load<Texture2D>("Art/Ordres/Ordre" + (i + 1));
        orderGo.SetActive(false);

        // Jauges : vie (rouge), faim (orange, V0.7.2), puis sérénité (bleue).
        Texture2D cracked = CrackedTexture(256, 32, 11);
        health = BuildGauge(r, "JaugeDeVie", new Vector2(20, 102), HealthColor, cracked);
        hunger = BuildGauge(r, "JaugeDeFaim", new Vector2(20, 62), HungerColor, cracked);
        serenity = BuildGauge(r, "JaugeDeSerenite", new Vector2(20, 22), SerenityColor, cracked);
        // V0.7.11 (7.9) : jauge du Parfum Odorant (rouge pastel), au-dessus de la vie, seulement quand le joueur est parfumé.
        perfume = BuildGauge(r, "JaugeDeParfum", new Vector2(20, 142), PerfumeColor, cracked);
        perfumeFrame = perfume.text.transform.parent.gameObject;
        perfumeFrame.SetActive(false);

        // Inventaire : 5 cases arrondies sur une barre.
        Sprite rounded = RoundedSprite(48, 10, 2.5f, 0.35f);
        const float size = 72f, gap = 8f;
        float barWidth = GameRules.InventorySize * (size + gap) + gap;
        var bar = UiKit.Panel(r, "Inventaire", new Vector2(barWidth, size + gap * 2f), new Color(0.27f, 0.32f, 0.27f, 0.9f));
        UiKit.Place(bar, new Vector2(0.5f, 0f), new Vector2(0, 16));
        inventoryBar = bar;
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
            // Touche de sélection de la case (modifiable dans le menu pause), remplie par RefreshSlotKeys.
            var num = UiKit.Label(slot.transform, "", 13, FontStyle.Bold, UiKit.MutedText, 20, TextAnchor.UpperLeft);
            UiKit.Stretch(num.rectTransform, 6f);
            // Nom de touche long ("Clic droit"…) : réduit pour tenir dans la case.
            num.resizeTextForBestFit = true;
            num.resizeTextMinSize = 8;
            num.resizeTextMaxSize = 13;
            num.verticalOverflow = VerticalWrapMode.Truncate;
            slotNumber[i] = num;
            slotName[i] = UiKit.Label(slot.transform, "", 11, FontStyle.Bold, Color.white, size);
            UiKit.Stretch(slotName[i].rectTransform, 4f);
            slotName[i].rectTransform.offsetMax = new Vector2(-4, -14);
            slotCount[i] = UiKit.Label(slot.transform, "", Mathf.RoundToInt(11 * 1.4f), FontStyle.Bold, Color.white, size, TextAnchor.LowerRight);
            UiKit.Stretch(slotCount[i].rectTransform, 7f);
            slotCount[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            var countShadow = slotCount[i].gameObject.AddComponent<Shadow>();
            countShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            countShadow.effectDistance = new Vector2(1f, -1f);
        }

        RefreshSlotKeys();
        Controls.Changed += RefreshSlotKeys;
        Loc.Changed += RefreshSlotKeys;

        BuildSpaceKey(r, rounded);
        BuildUseKey(r, rounded);

        // Game over.
        gameOverPanel = UiKit.Panel(canvas.transform, "GameOver", Vector2.zero, new Color(0.12f, 0f, 0f, 0.88f));
        UiKit.Stretch((RectTransform)gameOverPanel.transform);
        var box = new GameObject("Contenu", typeof(RectTransform));
        box.transform.SetParent(gameOverPanel.transform, false);
        ((RectTransform)box.transform).sizeDelta = new Vector2(520, 340);
        UiKit.Column(box, 10, 14);
        AddTitleGrain(UiKit.Label(box.transform, "GAME OVER", 64, FontStyle.Bold, new Color(1f, 0.25f, 0.2f), 80), 3f);
        gameOverText = UiKit.Label(box.transform, "", 20, FontStyle.Normal, Color.white, 90);
        restartButton = UiKit.Button(box.transform, "Nouvelle partie", UiKit.PrimaryColor, 52, () => onRestart());
        UiKit.Button(box.transform, "Quitter la partie", UiKit.NeutralColor, 46, () => onQuit());
        gameOverPanel.SetActive(false);

        BuildWipeOut(canvas.transform, vignette, onQuit);

        Show(false);
    }

    // Écran de fin quand tous les joueurs sont morts, au-dessus de tout le reste.
    void BuildWipeOut(Transform parent, Texture2D vignette, System.Action onLobby)
    {
        wipeRoot = new GameObject("TousMorts", typeof(RectTransform));
        wipeRoot.transform.SetParent(parent, false);
        UiKit.Stretch((RectTransform)wipeRoot.transform);
        Transform w = wipeRoot.transform;
        wipeBlack = FullScreen(w, "Noir", null);
        wipeVeil = FullScreen(w, "VoileDeSang", vignette);
        // Halo rouge derrière le titre (une copie plus grande et transparente qui respire).
        wipeGlow = WipeTitle(w, new Color(0.9f, 0f, 0f, 0f));
        wipeTitle = WipeTitle(w, BloodRed);
        var outline = wipeTitle.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);
        wipeGrain = AddTitleGrain(wipeTitle, 3f);
        wipeSubtitle = Floating(w, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(900, 34), 22);
        wipeSubtitle.fontStyle = FontStyle.Italic;
        Loc.Bind(wipeSubtitle, "Aucun sujet de test en vie. L'expérience peut continuer.");
        var button = UiKit.Button(w, "Revenir au Lobby", BloodRed, 58, () => onLobby());
        ((RectTransform)button.transform).sizeDelta = new Vector2(340, 58);
        UiKit.Place(button, new Vector2(0.5f, 0.5f), new Vector2(0, -150));
        UiKit.Paint(button, new Color(0.45f, 0.02f, 0.02f), 23);
        wipeButton = button.AddComponent<CanvasGroup>();
        wipeRoot.SetActive(false);
    }

    // V0.7.23 (Note 07) : grain par-dessus les lettres d'un titre (seulement dans ses lettres : le titre sert de masque) : petits points
    // sombres et clairs, qui donnent au rouge une texture granuleuse. 'grain' : taille d'un point (unités de l'interface).
    static Texture2D titleGrain;

    static RawImage AddTitleGrain(Text title, float grain)
    {
        if (titleGrain == null)
        {
            const int size = 128;
            titleGrain = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "GrainTitre", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rnd = new System.Random(731);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                double n = rnd.NextDouble();
                // Points sombres (sang caillé), points clairs (reflets), le reste transparent.
                px[i] = n < 0.22 ? new Color32(20, 0, 0, (byte)(90 + rnd.Next(90)))
                      : n < 0.34 ? new Color32(255, 170, 160, (byte)(40 + rnd.Next(50)))
                      : n < 0.5 ? new Color32(60, 0, 0, (byte)(40 + rnd.Next(40)))
                      : new Color32(0, 0, 0, 0);
            }
            titleGrain.SetPixels32(px);
            titleGrain.Apply(false);
        }
        title.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var go = new GameObject("Grain", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(title.transform, false);
        var rt = (RectTransform)go.transform;
        UiKit.Stretch(rt);
        var img = go.GetComponent<RawImage>();
        img.texture = titleGrain;
        img.raycastTarget = false;
        go.AddComponent<GrainTiling>().pixel = grain * 128f;
        return img;
    }

    // Garde la taille des points du grain quelle que soit la taille du titre (fixée par la mise en page).
    class GrainTiling : MonoBehaviour
    {
        public float pixel = 384f;
        RawImage img;
        Vector2 last;

        void LateUpdate()
        {
            if (img == null) img = GetComponent<RawImage>();
            Vector2 size = ((RectTransform)transform).rect.size;
            if (size == last) return;
            last = size;
            img.uvRect = new Rect(0f, 0f, Mathf.Max(1f, size.x) / pixel, Mathf.Max(1f, size.y) / pixel);
        }
    }

    static Text WipeTitle(Transform parent, Color color)
    {
        var t = UiKit.Label(parent, "GAME OVER", 150, FontStyle.Bold, color, 200);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400, 200);
        rt.anchoredPosition = new Vector2(0, 60);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
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
        // Avant le contour : celui-ci reprend les lettres déjà décalées.
        g.text.gameObject.AddComponent<TextShake>();
        g.text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        return g;
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
            wipeTime = -1f;
            wipeRoot.SetActive(false);
            flashTimer = whiteTimer = blackTimer = 0f;
            damageFlash.color = whiteFlash.color = blackFade.color = dreadVeil.color = dreadDim.color = Color.clear;
            ClearDeath();
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
        g.text.text = dead ? Loc.T("MORT") : Loc.T(label) + " " + value + " / " + max;
    }

    public void SetHealth(int hp, bool dead) => SetGauge(health, hp, GameRules.MaxHp, dead, "PV");
    public void SetSerenity(int value, bool dead) => SetGauge(serenity, value, GameRules.MaxSerenity, dead, "Sérénité");
    public void SetHunger(int value, bool dead) => SetGauge(hunger, value, GameRules.MaxHunger, dead, "Faim");

    // V0.7.11 (7.9) : jauge du Parfum (part restante, 0 : cachée). Les 10 dernières secondes, le libellé clignote lentement.
    public void SetPerfume(float fraction, bool blink)
    {
        bool on = fraction > 0f;
        if (perfumeFrame.activeSelf != on) perfumeFrame.SetActive(on);
        if (!on) return;
        float k = Mathf.Clamp01(fraction);
        var frt = perfume.fill.rectTransform;
        frt.offsetMax = new Vector2(frt.offsetMin.x + perfume.inner * k, frt.offsetMax.y);
        perfume.fill.uvRect = new Rect(0f, 0f, k, 1f);
        perfume.fill.enabled = k > 0.001f;
        perfume.marker.anchoredPosition = new Vector2(4f + perfume.inner * k, 0f);
        perfume.marker.gameObject.SetActive(k > 0.001f && k < 0.999f);
        perfume.text.text = Loc.T("Parfum");
        Color c = perfume.text.color;
        c.a = blink ? 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI)) : 1f;
        perfume.text.color = c;
    }

    // V0.7.11 (7.15) : vision rose avec les Lunettes PEACE (apparaît et disparaît en douceur).
    public void SetPeace(bool on)
    {
        peace = Mathf.MoveTowards(peace, on ? 1f : 0f, Time.unscaledDeltaTime * 1.5f);
        peaceVeil.color = new Color(1f, 0.45f, 0.72f, 0.16f * peace);
        // Les bordures ondulent doucement.
        float wave = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 2.2f);
        peaceTop.color = peaceBottom.color = new Color(1f, 1f, 1f, 0.8f * peace * wave);
        peaceTop.uvRect = peaceBottom.uvRect = new Rect(Mathf.Repeat(Time.unscaledTime * 0.05f, 1f), 0f, 1f, 1f);
    }

    const float RainbowBorderHeight = 0.07f;

    // Bande arc-en-ciel collée au bord haut (ou bas) de l'écran : rouge au bord, violet vers l'intérieur, qui s'efface vers le centre.
    static RawImage RainbowBorder(Transform parent, string name, bool top)
    {
        var img = FullScreen(parent, name, RainbowBand(top));
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? 1f - RainbowBorderHeight : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : RainbowBorderHeight);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    static Texture2D RainbowBand(bool top)
    {
        Color[] bands =
        {
            new Color(1f, 0.25f, 0.3f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.92f, 0.3f), new Color(0.35f, 0.9f, 0.4f),
            new Color(0.3f, 0.65f, 1f), new Color(0.55f, 0.4f, 0.95f), new Color(0.85f, 0.45f, 0.95f),
        };
        const int w = 64, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "BordureArcEnCiel" };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            // Distance au bord de l'écran (0 au bord, 1 vers l'intérieur).
            float fromEdge = top ? 1f - y / (h - 1f) : y / (h - 1f);
            float f = fromEdge * (bands.Length - 1);
            int a = Mathf.Min(bands.Length - 2, Mathf.FloorToInt(f));
            Color band = Color.Lerp(bands[a], bands[a + 1], f - a);
            float alpha = Mathf.Clamp01(1.15f - fromEdge * fromEdge * 1.1f);
            for (int x = 0; x < w; x++)
            {
                // Légère ondulation le long de la bande.
                float shimmer = 0.9f + 0.1f * Mathf.Sin(x / (float)w * Mathf.PI * 4f + fromEdge * 3f);
                pixels[y * w + x] = new Color(band.r * shimmer, band.g * shimmer, band.b * shimmer, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

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
            // Canard Coloré : l'icône désaturée du canard, à la couleur du canard ramassé (V0.7.13).
            slotIcon[i].color = Items.IsColoredDuck(t) ? Items.ColoredDuckTint(Items.DuckTier(t)) : Color.white;
            // Le nom ne s'affiche que si l'objet n'a pas d'icône.
            slotName[i].text = def != null && sprite == null ? def.shortName : "";
            // V0.7.11 (7.16) : compteur d'Os sur l'icône du Sac à Os (V0.7.12 : en bas à droite).
            slotCount[i].text = Items.IsBoneBag(t) ? Items.BonesIn(t) + "/" + Items.BoneBagMax : "";
            slotBg[i].color = selected ? SlotSelectedColor : SlotColor;
            // Case sélectionnée (colorée et légèrement grossie) : textes en noir.
            bool light = UiKit.ReadableOn(slotBg[i].color) == Color.black;
            slotName[i].color = light ? Color.black : def != null ? Color.Lerp(def.color, Color.white, 0.35f) : Color.white;
            slotNumber[i].color = light ? Color.black : UiKit.MutedText;
            slotCount[i].color = light ? Color.black : Color.white;
            slotBg[i].rectTransform.localScale = Vector3.one * (selected ? 1.15f : 1f);
        }
    }

    Sprite Icon(ItemType type)
    {
        if (type == ItemType.None) return null;
        if (!icons.TryGetValue(type, out var sprite)) icons[type] = sprite = Resources.Load<Sprite>("Icons/" + (Items.IsColoredDuck(type) ? "CanardGris" : Items.IsZoneBell(type) ? "ClocheDecorative"
            : Items.IsBoneBag(type) ? "SacOs" : Items.IsJournal(type) ? "Journal" : type.ToString()));
        return sprite;
    }

    public void FlashDamage() => flashTimer = FlashDuration;

    public void FlashWhite() => whiteTimer = WhiteFlashDuration;

    // Noir complet, qui s'efface aussitôt (changement brusque de point de vue).
    public void FadeFromBlack() => blackTimer = BlackFadeDuration;

    public void PlayDeath()
    {
        deathTime = Time.unscaledTime;
        flashTimer = 0f;
    }

    public void ClearDeath()
    {
        deathTime = -1f;
        deathDim.color = Color.clear;
        deathVeil.color = Color.clear;
    }
    public void SetPrompt(string text) => promptText.text = text;

    public void SetChainPrompt(bool visible)
    {
        if (spaceKey != null && spaceKey.activeSelf != visible) spaceKey.SetActive(visible);
    }

    public void SetCannonMode(bool on)
    {
        crosshairRing.SetActive(on);
        crosshairDot.SetActive(!on);
        inventoryBar.SetActive(!on);
    }

    // Touche Espace dessinée en bas de l'écran (sans consigne écrite : l'animation suffit).
    void BuildSpaceKey(Transform parent, Sprite rounded)
    {
        spaceKey = new GameObject("ToucheEspace", typeof(RectTransform));
        spaceKey.transform.SetParent(parent, false);
        ((RectTransform)spaceKey.transform).sizeDelta = new Vector2(300, 110);
        UiKit.Place(spaceKey, new Vector2(0.5f, 0.5f), new Vector2(0, -150));
        var shadow = UiKit.Panel(spaceKey.transform, "Ombre", new Vector2(280, 58), new Color(0.05f, 0.05f, 0.06f, 0.9f));
        UiKit.Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0, -6));
        shadow.GetComponent<Image>().sprite = rounded;
        shadow.GetComponent<Image>().type = Image.Type.Sliced;
        var face = UiKit.Panel(spaceKey.transform, "Touche", new Vector2(280, 58), new Color(0.92f, 0.92f, 0.9f));
        spaceKeyFace = UiKit.Place(face, new Vector2(0.5f, 0.5f), Vector2.zero);
        spaceKeyImage = face.GetComponent<Image>();
        spaceKeyImage.sprite = rounded;
        spaceKeyImage.type = Image.Type.Sliced;
        var label = UiKit.Label(face.transform, "ESPACE", 24, FontStyle.Bold, new Color(0.12f, 0.12f, 0.14f), 58);
        UiKit.Stretch(label.rectTransform);
        foreach (var img in spaceKey.GetComponentsInChildren<Graphic>()) img.raycastTarget = false;
        spaceKey.SetActive(false);
    }

    // Machine à pince (V0.6.1) : la barre d'espace dessinée en haut de l'écran, avec le mot "Utiliser" (comme pour la chaîne
    // du Roi).
    void BuildUseKey(Transform parent, Sprite rounded)
    {
        useKey = new GameObject("ToucheUtiliser", typeof(RectTransform));
        useKey.transform.SetParent(parent, false);
        ((RectTransform)useKey.transform).sizeDelta = new Vector2(300, 110);
        UiKit.Place(useKey, new Vector2(0.5f, 1f), new Vector2(0, -90));
        var shadow = UiKit.Panel(useKey.transform, "Ombre", new Vector2(280, 58), new Color(0.05f, 0.05f, 0.06f, 0.9f));
        UiKit.Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0, 4));
        shadow.GetComponent<Image>().sprite = rounded;
        shadow.GetComponent<Image>().type = Image.Type.Sliced;
        var face = UiKit.Panel(useKey.transform, "Touche", new Vector2(280, 58), new Color(0.92f, 0.92f, 0.9f));
        UiKit.Place(face, new Vector2(0.5f, 0.5f), new Vector2(0, 10));
        face.GetComponent<Image>().sprite = rounded;
        face.GetComponent<Image>().type = Image.Type.Sliced;
        var label = UiKit.Label(face.transform, "ESPACE", 24, FontStyle.Bold, new Color(0.12f, 0.12f, 0.14f), 58);
        UiKit.Stretch(label.rectTransform);
        var word = UiKit.Label(useKey.transform, "Utiliser", 22, FontStyle.Bold, Color.white, 30);
        UiKit.Place(word.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0, -42));
        word.rectTransform.sizeDelta = new Vector2(300, 30);
        word.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        foreach (var img in useKey.GetComponentsInChildren<Graphic>()) img.raycastTarget = false;
        useKey.SetActive(false);
    }

    public void SetUsePrompt(bool visible)
    {
        if (useKey != null && useKey.activeSelf != visible) useKey.SetActive(visible);
    }

    // Anneau blanc (viseur du Canon) : rayons intérieur et extérieur en fraction de la taille.
    static Texture2D RingTexture(int size, float inner, float outer)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x - c, y - c).magnitude / size;
                float a = Mathf.Clamp01((d - inner) * size) * Mathf.Clamp01((outer - d) * size);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }
    public void SetSpectate(string text) => spectateText.text = text;

    // Ordre d'un glyphe (0 à 2) : son icône apparaît en clignotant 3 fois ('already' : ordre déjà en cours, pour un joueur qui
    // arrive, sans clignoter).
    public void ShowOrder(int type, bool already = false)
    {
        if (orderIcon == null || type < 0 || type >= orderTextures.Length) return;
        orderShown = type;
        orderShownAt = already ? Time.unscaledTime - OrderBlinks * OrderBlinkPeriod : Time.unscaledTime;
        orderHiddenAt = -100f;
        orderIcon.texture = orderTextures[type];
        orderIcon.gameObject.SetActive(orderIcon.texture != null);
        RefreshOrderIcon();
    }

    // Fin de l'ordre 'type' (-1 : quel qu'il soit) : l'icône s'efface progressivement ('instant' : tout de suite).
    public void HideOrder(int type, bool instant = false)
    {
        if (orderIcon == null || orderShown < 0 || (type >= 0 && type != orderShown)) return;
        orderShown = -1;
        orderHiddenAt = instant ? -100f : Time.unscaledTime;
        if (instant) orderIcon.gameObject.SetActive(false);
    }

    void RefreshOrderIcon()
    {
        if (orderIcon == null || !orderIcon.gameObject.activeSelf) return;
        float a;
        if (orderShown >= 0)
        {
            float t = Time.unscaledTime - orderShownAt;
            a = t < OrderBlinks * OrderBlinkPeriod && Mathf.Repeat(t, OrderBlinkPeriod) >= OrderBlinkPeriod * 0.55f ? 0f : 1f;
        }
        else
        {
            a = 1f - Mathf.Clamp01((Time.unscaledTime - orderHiddenAt) / OrderFadeOut);
            if (a <= 0f) { orderIcon.gameObject.SetActive(false); return; }
        }
        orderIcon.color = new Color(1f, 1f, 1f, OrderAlpha * a);
    }

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

    public void PlayWipeOut()
    {
        if (wipeTime >= 0f) return;
        wipeTime = Time.unscaledTime;
        wipeRoot.SetActive(true);
        GameOverVisible = true;
        UpdateWipeOut(0f);
    }

    void UpdateWipeOut(float t)
    {
        // Le noir monte lentement, d'abord teinté de sang ; le rouge gagne les bords, puis tout s'éteint.
        float f = Mathf.Clamp01(t / WipeFadeDuration);
        float fade = Mathf.SmoothStep(0f, 1f, f);
        wipeBlack.color = Color.Lerp(new Color(0.25f, 0f, 0f, 0f), Color.black, fade);
        float veil = 0.85f * Mathf.Sin(f * Mathf.PI);

        // Après le silence, le titre émerge des ténèbres comme une braise : il se resserre et tremble au coup du glas.
        float r = t - WipeFadeDuration - WipeSilence;
        float k = Mathf.Clamp01(r / WipeTitleDuration);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        float scale = Mathf.Lerp(1.18f, 1f, ease);
        float tremor = r >= 0f ? 9f * Mathf.Exp(-r * 2.2f) : 0f;
        Vector2 jitter = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * tremor;
        // Ensuite, un battement lent, comme un cœur qui s'arrête.
        float beat = r > 0f ? Mathf.Pow(0.5f + 0.5f * Mathf.Sin(r * 1.7f), 3f) : 0f;

        wipeTitle.rectTransform.localScale = Vector3.one * scale;
        wipeTitle.rectTransform.anchoredPosition = new Vector2(0, 60) + jitter;
        wipeTitle.color = Color.Lerp(new Color(0.15f, 0f, 0f, 0f), BloodRed, ease);
        if (wipeGrain != null) wipeGrain.color = new Color(1f, 1f, 1f, TitleGrainAlpha * ease);
        wipeGlow.rectTransform.localScale = Vector3.one * scale * (1.05f + 0.025f * beat);
        wipeGlow.rectTransform.anchoredPosition = new Vector2(0, 60) + jitter * 1.6f;
        wipeGlow.color = new Color(0.9f, 0f, 0f, ease * (0.12f + 0.22f * beat));
        if (r > 0f) veil = Mathf.Max(veil, ease * (0.22f + 0.2f * beat));
        wipeVeil.color = new Color(0.55f, 0f, 0f, veil);

        float sub = Mathf.Clamp01((r - WipeTitleDuration * 0.7f) / 1.5f);
        wipeSubtitle.color = new Color(0.62f, 0.55f, 0.55f, 0.85f * sub);
        float b = Mathf.Clamp01((r - WipeButtonDelay) / 1.2f);
        wipeButton.alpha = b;
        wipeButton.interactable = wipeButton.blocksRaycasts = b > 0.5f;
    }

    void Update()
    {
        float dread = root.activeSelf ? Mathf.Sqrt(OrbitCamera.HeartTension) : 0f;
        float beat = 1f + DreadBeatBoost * OrbitCamera.HeartBeat;
        dreadVeil.color = new Color(0f, 0f, 0f, Mathf.Min(1f, DreadDarkness * dread * beat));
        dreadDim.color = new Color(0f, 0f, 0f, Mathf.Min(1f, DreadDim * dread * beat));
        if (whiteTimer > 0f)
        {
            // L'image devient 5 fois plus claire : un voile blanc qui couvre 4/5 de l'écran, puis s'efface.
            whiteTimer = Mathf.Max(0f, whiteTimer - Time.unscaledDeltaTime);
            float k = whiteTimer / WhiteFlashDuration;
            whiteFlash.color = new Color(1f, 1f, 1f, WhiteFlashBoost / (1f + WhiteFlashBoost) * k * k);
        }
        else if (whiteFlash.color.a > 0f) whiteFlash.color = Color.clear;
        if (blackTimer > 0f)
        {
            blackTimer = Mathf.Max(0f, blackTimer - Time.unscaledDeltaTime);
            float k = blackTimer / BlackFadeDuration;
            blackFade.color = new Color(0f, 0f, 0f, k * k);
        }
        else if (blackFade.color.a > 0f) blackFade.color = Color.clear;
        if (flashTimer > 0f)
        {
            // Montée rapide puis fondu, avec un léger battement, le tout en moins d'une seconde.
            flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime);
            float k = flashTimer / FlashDuration;
            float alpha = Mathf.Min(1f, (1f - k) * 8f) * k * (0.85f + 0.15f * Mathf.Sin(k * 25f));
            damageFlash.color = new Color(0.85f, 0.02f, 0.02f, 0.7f * alpha);
        }
        // Flash fini ou interrompu (mort pendant le flash) : le voile rouge ne doit jamais rester figé.
        else if (damageFlash.color.a > 0f) damageFlash.color = Color.clear;
        if (deathTime >= 0f)
        {
            // Éclair rouge sang au moment de la mort, puis le noir gagne depuis les bords et l'image se ternit.
            float t = Time.unscaledTime - deathTime;
            float red = Mathf.Clamp01(1f - t / 0.6f);
            float dark = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.2f) / 2.5f));
            deathVeil.color = Color.Lerp(new Color(0f, 0f, 0f, 0.92f * dark), new Color(0.75f, 0f, 0f, 0.95f), red);
            deathDim.color = new Color(0.04f, 0.03f, 0.03f, Mathf.Max(0.45f * dark, 0.35f * red));
        }
        if (wipeTime >= 0f) UpdateWipeOut(Time.unscaledTime - wipeTime);
        if (spaceKey != null && spaceKey.activeSelf)
        {
            // Enfoncée, relâchée, une dizaine de fois par seconde.
            bool down = Mathf.Repeat(Time.unscaledTime, 0.16f) < 0.08f;
            spaceKeyFace.anchoredPosition = new Vector2(0f, down ? -6f : 0f);
            spaceKeyImage.color = down ? new Color(0.72f, 0.72f, 0.7f) : new Color(0.92f, 0.92f, 0.9f);
        }

        RefreshOrderIcon();

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
