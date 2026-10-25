using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Fenêtre "Modifier Contrôles", au centre de l'écran, ouverte depuis le menu pause : un clic sur la touche d'une action,
// puis la nouvelle touche (clavier ou souris) ; Échap annule, puis ferme la fenêtre. Une touche déjà prise est échangée.
public class ControlsUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    // Image où la fenêtre s'est fermée par Échap : ce même Échap ne referme pas le menu pause.
    public static int ClosedFrame { get; private set; } = -1;

    static readonly Color PanelBlack = new Color(0.09f, 0.09f, 0.1f, 1f);
    static readonly Color KeyGrey = new Color(0.33f, 0.33f, 0.35f);
    static readonly Color CaptureBlue = new Color(0.12f, 0.2f, 0.45f);

    static readonly (GameAction action, string name)[] Rows =
    {
        (GameAction.Forward, "Avancer"), (GameAction.Back, "Reculer"), (GameAction.Left, "Gauche"), (GameAction.Right, "Droite"),
        (GameAction.Run, "Courir"), (GameAction.Sneak, "Marcher discrètement"), (GameAction.Jump, "Sauter"), (GameAction.Bark, "Aboyer"),
        (GameAction.Interact, "Interagir"),
        (GameAction.Use, "Utiliser l'objet"), (GameAction.Slot1, "Objet 1"), (GameAction.Slot2, "Objet 2"), (GameAction.Slot3, "Objet 3"),
        (GameAction.Slot4, "Objet 4"), (GameAction.Slot5, "Objet 5"), (GameAction.Dance1, "Danse 1"), (GameAction.Dance2, "Danse 2"),
        (GameAction.Dance3, "Danse 3"),
    };

    GameObject panel;
    readonly Dictionary<GameAction, (Image image, Text label)> keys = new Dictionary<GameAction, (Image, Text)>();
    GameAction? capturing;
    int captureFrame;
    static KeyCode[] candidates;

    public void Init()
    {
        var canvas = UiKit.CreateCanvas(transform, "ControlsCanvas", 110);
        panel = UiKit.Panel(canvas.transform, "ModifierControles", new Vector2(780, 560));
        UiKit.Paint(panel, PanelBlack, 31);
        UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        UiKit.Column(panel, 34, 10);
        Transform p = panel.transform;

        UiKit.Label(p, "Contrôles", 26, FontStyle.Bold, Color.white, 34);
        var hint = UiKit.Label(p, "Cliquez sur une touche pour la modifier, puis appuyez sur la nouvelle touche (Échap : annuler).",
            14, FontStyle.Italic, UiKit.MutedText, 22);
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;

        // Deux colonnes de 9 actions.
        var columns = UiKit.Row(p, 9 * 36 + 8 * 6, 24);
        columns.childForceExpandHeight = false;
        int perColumn = (Rows.Length + 1) / 2;
        for (int c = 0; c < 2; c++)
        {
            var col = new GameObject("Colonne", typeof(RectTransform), typeof(LayoutElement));
            col.transform.SetParent(columns.transform, false);
            col.GetComponent<LayoutElement>().flexibleWidth = 1;
            var v = UiKit.Column(col, 0, 6);
            v.childAlignment = TextAnchor.UpperLeft;
            for (int i = c * perColumn; i < Mathf.Min(Rows.Length, (c + 1) * perColumn); i++)
                BuildRow(col.transform, i, c * 2 + i);
        }

        UiKit.Spacer(p, 2);
        var bottom = UiKit.Row(p, 44, 16);
        bottom.childForceExpandWidth = true;
        UiKit.Paint(UiKit.Button(bottom.transform, "Réinitialiser", KeyGrey, 44, () =>
        {
            capturing = null;
            Controls.ResetAll();
            GameAudio.Play2D("pop", 0.6f);
        }), KeyGrey, 37);
        UiKit.Paint(UiKit.Button(bottom.transform, "Fermer", CaptureBlue, 44, () => Close(false)), CaptureBlue, 38);

        MenuButtonHover.AddTo(panel);
        Controls.Changed += Refresh;
        Loc.Changed += Refresh;
        Refresh();
        panel.SetActive(false);
    }

    void OnDestroy()
    {
        Controls.Changed -= Refresh;
        Loc.Changed -= Refresh;
    }

    void BuildRow(Transform parent, int index, int seed)
    {
        var (action, name) = Rows[index];
        var row = UiKit.Row(parent, 36, 10);
        var label = UiKit.Label(row.transform, name, 15, FontStyle.Bold, Color.white, 36, TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().flexibleWidth = 1;
        var button = UiKit.Button(row.transform, "", KeyGrey, 36, () => StartCapture(action));
        button.GetComponent<LayoutElement>().preferredWidth = 140;
        UiKit.Paint(button, KeyGrey, 40 + seed);
        keys[action] = (button.GetComponent<Image>(), button.GetComponentInChildren<Text>());
    }

    // Souris sur la fenêtre : la scène s'assombrit, comme pour le menu pause (FilmGrain).
    public bool Hovered => IsOpen && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)panel.transform, Input.mousePosition, null);

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        capturing = null;
        Refresh();
        panel.SetActive(true);
        GameAudio.Play2D("pop", 0.6f);
    }

    public void Close(bool byEscape)
    {
        if (!IsOpen) return;
        IsOpen = false;
        capturing = null;
        if (byEscape) ClosedFrame = Time.frameCount;
        panel.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void StartCapture(GameAction action)
    {
        capturing = action;
        // Le clic qui a choisi l'action ne devient pas la nouvelle touche.
        captureFrame = Time.frameCount;
        Refresh();
        GameAudio.Play2D("pop", 0.6f);
    }

    void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (capturing.HasValue)
            {
                capturing = null;
                Refresh();
            }
            else Close(true);
            return;
        }
        if (!capturing.HasValue || Time.frameCount == captureFrame) return;
        if (candidates == null)
        {
            var list = new List<KeyCode>();
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                if (k != KeyCode.None && k != KeyCode.Escape && k < KeyCode.JoystickButton0 && !list.Contains(k)) list.Add(k);
            candidates = list.ToArray();
        }
        foreach (var k in candidates)
        {
            if (!Input.GetKeyDown(k)) continue;
            // Clic de souris sur un autre élément de la fenêtre ("Fermer"…) : la saisie est annulée, le clic suit son cours.
            bool mouse = k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
            var waitingRect = (RectTransform)keys[capturing.Value].image.transform;
            if (mouse && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()
                && !RectTransformUtility.RectangleContainsScreenPoint(waitingRect, Input.mousePosition, null))
            {
                capturing = null;
                Refresh();
                return;
            }
            // Lettre réellement tapée (clavier AZERTY dans le navigateur : la touche A arrive comme KeyCode.Q).
            string typed = Input.inputString;
            string label = typed.Length == 1 && char.IsLetterOrDigit(typed[0]) && k >= KeyCode.A && k <= KeyCode.Z
                ? typed.ToUpperInvariant() : Controls.KeyName(k);
            var action = capturing.Value;
            capturing = null;
            Controls.Bind(action, k, label);
            GameAudio.Play2D("pop", 0.6f);
            return;
        }
    }

    void Refresh()
    {
        foreach (var kv in keys)
        {
            bool waiting = capturing.HasValue && capturing.Value == kv.Key;
            kv.Value.label.text = waiting ? Loc.T("Appuyez sur une touche…") : Controls.Label(kv.Key);
            kv.Value.label.fontSize = waiting ? 13 : 18;
            kv.Value.image.color = waiting ? CaptureBlue : KeyGrey;
        }
    }
}
