using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Zone de saisie du Terminal, affichée uniquement chez le joueur qui l'utilise.
// Commandes insensibles à la casse : /sell, /buy, /info et la commande secrète /kick (hôte).
public class TerminalUI : MonoBehaviour
{
    public const string Menu = "Bienvenue sur le Terminal !\nTaper une commande pour continuer :\n/sell\n/buy\n/info";
    const string Quit = "\nFaire ECHAP pour quitter.";
    const int MaxLines = 18;

    enum Mode { Command, Sell, Buy }

    public static bool IsOpen { get; private set; }

    GameSession session;
    GameObject panel;
    Text output;
    InputField input;
    Mode mode;
    readonly List<string> lines = new List<string>();
    bool waitingBuy;

    public void Init(GameSession owner)
    {
        session = owner;
        var canvas = UiKit.CreateCanvas(transform, "TerminalCanvas", 80);
        panel = UiKit.Panel(canvas.transform, "Terminal", new Vector2(760, 470), new Color(0.01f, 0.03f, 0.01f, 0.96f));
        UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        panel.AddComponent<Outline>().effectColor = new Color(0.2f, 0.9f, 0.3f, 0.6f);

        output = UiKit.Label(panel.transform, "", 18, FontStyle.Normal, new Color(0.45f, 1f, 0.5f), 0, TextAnchor.LowerLeft);
        var ort = output.rectTransform;
        ort.anchorMin = new Vector2(0f, 0f);
        ort.anchorMax = new Vector2(1f, 1f);
        ort.offsetMin = new Vector2(22, 74);
        ort.offsetMax = new Vector2(-22, -18);
        output.verticalOverflow = VerticalWrapMode.Truncate;
        output.supportRichText = false;

        input = UiKit.Field(panel.transform, "Taper une commande puis Entrée…", "", 40);
        var irt = (RectTransform)input.transform;
        irt.anchorMin = new Vector2(0f, 0f);
        irt.anchorMax = new Vector2(1f, 0f);
        irt.pivot = new Vector2(0.5f, 0f);
        irt.offsetMin = new Vector2(16, 14);
        irt.offsetMax = new Vector2(-16, 64);
        input.textComponent.color = new Color(0.6f, 1f, 0.6f);
        input.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.1f, 0.6f);
        input.onEndEdit.AddListener(OnEndEdit);

        panel.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        mode = Mode.Command;
        waitingBuy = false;
        lines.Clear();
        Print(Menu);
        panel.SetActive(true);
        OrbitCamera.LockCursor(false);
        Focus();
    }

    public void Close(bool notify = true)
    {
        if (!IsOpen) return;
        IsOpen = false;
        panel.SetActive(false);
        input.text = "";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (notify) session.ReleaseTerminal();
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Focus()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    void OnEndEdit(string text)
    {
        if (!IsOpen) return;
        if (!(Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))) return;
        input.text = "";
        Focus();
        text = text.Trim();
        if (text.Length == 0) return;
        Print("> " + text);
        Execute(text);
    }

    void Execute(string text)
    {
        string lower = text.ToLowerInvariant();
        switch (mode)
        {
            case Mode.Sell:
                mode = Mode.Command;
                Sell(lower);
                return;
            case Mode.Buy:
                mode = Mode.Command;
                Buy(lower);
                return;
        }

        if (lower == "/sell")
        {
            mode = Mode.Sell;
            Print("Écrire le numéro de l'objet à vendre (1-2-3-4-5).");
        }
        else if (lower == "/buy")
        {
            var sb = new System.Text.StringBuilder("Objets achetables :");
            foreach (var def in Items.Buyable())
                sb.Append("\n  ID ").Append((int)def.type).Append(" - ").Append(def.name).Append(" : ").Append(def.buyPrice).Append(" golds");
            sb.Append("\nÉcrire l'ID de l'objet à acheter.");
            Print(sb.ToString());
            mode = Mode.Buy;
        }
        else if (lower == "/info")
        {
            Print("Aucune information disponible pour le moment." + Quit);
        }
        else if (lower.StartsWith("/kick ") && session.IsHost)
        {
            string target = text.Substring(6).Trim();
            string kicked = session.Kick(target);
            Print(kicked != null ? "Le joueur " + kicked + " a été expulsé de la partie." + Quit
                                 : "Aucun autre joueur nommé " + target + "." + Quit);
        }
        else
        {
            Print("Commande Incorrecte." + Quit);
        }
    }

    void Sell(string arg)
    {
        if (arg.Length != 1 || arg[0] < '1' || arg[0] > '5')
        {
            Print("Commande invalide." + Quit);
            return;
        }
        int slot = arg[0] - '1';
        ItemDef sold = session.SellSlot(slot);
        if (sold == null) Print("Vous n'avez pas d'objet " + (slot + 1) + " en inventaire !" + Quit);
        else Print("Vous avez vendu " + sold.name + " pour " + sold.sellPrice + " golds !" + Quit);
    }

    void Buy(string arg)
    {
        ItemDef def = int.TryParse(arg, out int id) ? Items.Get(id) : null;
        if (def == null || def.buyPrice <= 0)
        {
            Print("Commande invalide." + Quit);
            return;
        }
        if (waitingBuy) return;
        if (!session.HasFreeSlot)
        {
            Print("Votre inventaire est plein ! Impossible d'acheter " + def.name + "." + Quit);
            return;
        }
        if (session.Cash < def.buyPrice)
        {
            Print("Cagnotte insuffisante pour acheter " + def.name + " (" + def.buyPrice + " golds)." + Quit);
            return;
        }
        waitingBuy = true;
        session.Buy(def);
    }

    // Réponse de l'hôte à une demande d'achat.
    public void OnBuyResult(bool ok, ItemDef def, int cash)
    {
        waitingBuy = false;
        if (!IsOpen || def == null) return;
        Print(ok ? "Vous avez acheté " + def.name + " pour " + def.buyPrice + " golds !" + Quit
                 : "Cagnotte insuffisante pour acheter " + def.name + " (" + def.buyPrice + " golds)." + Quit);
    }

    void Print(string text)
    {
        lines.AddRange(text.Split('\n'));
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        output.text = string.Join("\n", lines);
    }
}
