using UnityEngine;
using UnityEngine.UI;

// Interface du joueur en jeu : jauge de vie, 5 cases d'inventaire, viseur, messages et écran de game over.
public class GameHud : MonoBehaviour
{
    static readonly Color SlotColor = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color SlotSelectedColor = new Color(1f, 0.78f, 0.2f, 0.85f);

    public static bool GameOverVisible { get; private set; }

    GameObject root, gameOverPanel, restartButton;
    Image healthFill;
    Text healthText, promptText, toastText, spectateText, gameOverText;
    readonly Image[] slotBg = new Image[GameRules.InventorySize];
    readonly Text[] slotName = new Text[GameRules.InventorySize];
    float toastTimer;

    public void Init(System.Action onRestart, System.Action onQuit)
    {
        var canvas = UiKit.CreateCanvas(transform, "HudCanvas", 50);
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
        toastText = Floating(r, new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(900, 34), 20);
        toastText.color = new Color(1f, 0.85f, 0.45f);
        spectateText = Floating(r, new Vector2(0.5f, 0f), new Vector2(0, 196), new Vector2(1000, 54), 18);


        // Jauge de vie.
        var hp = UiKit.Panel(r, "JaugeDeVie", new Vector2(320, 34), new Color(0f, 0f, 0f, 0.6f));
        UiKit.Place(hp, new Vector2(0f, 0f), new Vector2(20, 20));
        var fill = UiKit.Panel(hp.transform, "Remplissage", Vector2.zero, new Color(0.85f, 0.2f, 0.2f));
        var frt = (RectTransform)fill.transform;
        frt.pivot = new Vector2(0f, 0.5f);
        UiKit.Stretch(frt, 3f);
        healthFill = fill.GetComponent<Image>();
        healthText = UiKit.Label(hp.transform, "", 17, FontStyle.Bold, Color.white, 34);
        UiKit.Stretch(healthText.rectTransform);
        healthText.gameObject.AddComponent<Shadow>();

        // Inventaire : 5 cases.
        const float size = 76f, gap = 10f;
        for (int i = 0; i < GameRules.InventorySize; i++)
        {
            var slot = UiKit.Panel(r, "Case" + (i + 1), new Vector2(size, size), SlotColor);
            float x = (i - (GameRules.InventorySize - 1) * 0.5f) * (size + gap);
            UiKit.Place(slot, new Vector2(0.5f, 0f), new Vector2(x, 24));
            slotBg[i] = slot.GetComponent<Image>();
            var num = UiKit.Label(slot.transform, (i + 1).ToString(), 14, FontStyle.Bold, UiKit.MutedText, 20, TextAnchor.UpperLeft);
            UiKit.Stretch(num.rectTransform, 5f);
            slotName[i] = UiKit.Label(slot.transform, "", 11, FontStyle.Bold, Color.white, size);
            UiKit.Stretch(slotName[i].rectTransform, 4f);
            slotName[i].rectTransform.offsetMax = new Vector2(-4, -14);
        }

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

    public void Show(bool visible)
    {
        root.SetActive(visible);
        if (!visible) HideGameOver();
    }

    public void SetHealth(int hp, bool dead)
    {
        float k = hp / (float)GameRules.MaxHp;
        healthFill.rectTransform.localScale = new Vector3(Mathf.Max(0.001f, k), 1f, 1f);
        healthFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f), new Color(0.25f, 0.8f, 0.3f), k);
        healthText.text = dead ? "MORT" : "PV : " + hp + " / " + GameRules.MaxHp;
    }

    public void SetInventory(PlayerStatus status)
    {
        for (int i = 0; i < GameRules.InventorySize; i++)
        {
            ItemType t = status[i];
            ItemDef def = Items.Get(t);
            bool selected = status.Selected == i;
            slotName[i].text = def != null ? def.shortName : "";
            slotName[i].color = selected ? new Color(0.12f, 0.08f, 0f) : def != null ? Color.Lerp(def.color, Color.white, 0.35f) : Color.white;
            slotBg[i].color = selected ? SlotSelectedColor : SlotColor;
            slotBg[i].rectTransform.localScale = Vector3.one * (selected ? 1.15f : 1f);
        }
    }

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
