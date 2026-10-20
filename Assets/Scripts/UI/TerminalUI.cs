using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Zone de saisie du Terminal, affichée uniquement chez le joueur qui l'utilise.
// Commandes insensibles à la casse : /buy (ou /buy <ID>), /info, /skip round, /sell (cachée depuis l'arrivée de Pogo)
// et les commandes secrètes /kick, /spawn et /tp (hôte).
public class TerminalUI : MonoBehaviour
{
    // /skip round n'apparaît qu'une fois le quota de la manche atteint.
    public static string Menu(bool canSkip) =>
        "Bienvenue sur le Terminal !\nTaper une commande pour continuer :\n/buy\n/info" + (canSkip ? "\n/skip round" : "");
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
    bool waitingBuy, waitingSkip;

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
        input.onValueChanged.AddListener(text => { if (IsOpen && text.Length > 0) GameAudio.Play2D("key", 0.3f, 0.2f); });

        panel.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        mode = Mode.Command;
        waitingBuy = waitingSkip = false;
        lines.Clear();
        Print(Menu(session.CanSkipRound));
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
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            OrbitCamera.ResumeGame();
        }
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
        GameAudio.Play2D("beep", 0.35f);
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
                // Une fois achetée, la Boussole disparaît des propositions.
                if (def.type != ItemType.Boussole || !session.CompassUnlocked)
                    sb.Append("\n  ID ").Append(def.terminalId).Append(" - ").Append(def.name).Append(" : ").Append(def.buyPrice).Append(" golds");
            sb.Append("\nÉcrire l'ID de l'objet à acheter.");
            Print(sb.ToString());
            mode = Mode.Buy;
        }
        else if (lower.StartsWith("/buy "))
        {
            // Raccourci : /buy <ID> achète directement, sans passer par la liste.
            Buy(lower.Substring(5).Trim());
        }
        else if (lower == "/info")
        {
            Print("Aucune information disponible pour le moment." + Quit);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^/skip\s+round$"))
        {
            if (waitingSkip) return;
            string refused = session.RequestSkipRound();
            if (refused != null) Error(refused + Quit);
            else waitingSkip = true;
        }
        else if (lower.StartsWith("/tp ") && session.IsHost)
        {
            string result = session.Teleport(text.Substring(4).Trim());
            if (result == null) Error("Commande invalide." + Quit);
            else
            {
                GameAudio.Play2D("beep", 0.35f, 0f, 1.3f);
                Close();
                session.Toast(result);
            }
        }
        else if (lower.StartsWith("/kick ") && session.IsHost)
        {
            string target = text.Substring(6).Trim();
            string kicked = session.Kick(target);
            Print(kicked != null ? "Le joueur " + kicked + " a été expulsé de la partie." + Quit
                                 : "Aucun autre joueur nommé " + target + "." + Quit);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^/godmod\s+(on|off)$") && session.IsHost)
        {
            Print(session.SetGodmode(lower.EndsWith("on")) + Quit);
        }
        else if (lower.StartsWith("/spawn ") && session.IsHost)
        {
            string arg = text.Substring(7).Trim();
            if (int.TryParse(arg, out int id)) Print(session.SpawnEntity(id) + Quit);
            else Error("Commande invalide." + Quit);
        }
        else
        {
            Error("Commande Incorrecte." + Quit);
        }
    }

    void Sell(string arg)
    {
        if (arg.Length != 1 || arg[0] < '1' || arg[0] > '5')
        {
            Error("Commande invalide." + Quit);
            return;
        }
        int slot = arg[0] - '1';
        ItemDef sold = session.SellSlot(slot);
        if (sold == null) Error("Vous n'avez pas d'objet " + (slot + 1) + " en inventaire !" + Quit);
        else Print("Vous avez vendu " + sold.name + " pour " + sold.sellPrice + " golds !" + Quit);
    }

    void Buy(string arg)
    {
        ItemDef def = int.TryParse(arg, out int id) ? Items.ByTerminalId(id) : null;
        if (def == null || def.buyPrice <= 0)
        {
            Error("Commande invalide." + Quit);
            return;
        }
        if (waitingBuy) return;
        if (def.type == ItemType.Boussole && session.CompassUnlocked)
        {
            Error("Commande invalide." + Quit);
            return;
        }
        if (def.inventory && !session.HasFreeSlot)
        {
            Error("Votre inventaire est plein ! Impossible d'acheter " + def.name + "." + Quit);
            return;
        }
        if (session.Cash < def.buyPrice && !session.Godmode)
        {
            Error("Cagnotte insuffisante pour acheter " + def.name + " (" + def.buyPrice + " golds)." + Quit);
            return;
        }
        waitingBuy = true;
        session.Buy(def);
    }

    // Réponse de l'hôte à une demande d'achat.
    public void OnBuyResult(bool ok, ItemDef def, int cash, bool alreadyOwned)
    {
        waitingBuy = false;
        if (!IsOpen || def == null) return;
        if (ok && def.type == ItemType.Boussole)
            Print("Vous avez acheté la Boussole pour " + (session.Godmode ? 0 : def.buyPrice) + " golds !\nElle s'affiche désormais en haut de l'écran de tous les joueurs." + Quit);
        else if (ok)
            Print("Vous avez acheté " + def.name + " pour " + (session.Godmode ? 0 : def.buyPrice) + " golds !" + Quit);
        else if (alreadyOwned)
            Print("La Boussole a déjà été achetée." + Quit);
        else
            Print("Cagnotte insuffisante pour acheter " + def.name + " (" + def.buyPrice + " golds)." + Quit);
    }

    // Réponse de l'hôte à /skip round (bonus : golds ajoutés à la cagnotte, -1 si refusé).
    public void OnSkipResult(int bonus)
    {
        waitingSkip = false;
        if (!IsOpen) return;
        if (bonus < 0) Error("Le quota de la manche n'est pas encore atteint." + Quit);
        else Print("Manche passée ! " + bonus + " golds ajoutés à la cagnotte pour le temps restant." + Quit);
    }

    void Error(string text)
    {
        GameAudio.Play2D("error", 0.35f);
        Print(text);
    }

    // Le texte tronque par le bas : on retire les lignes les plus anciennes jusqu'à ce que tout tienne
    // (retours à la ligne automatiques compris), pour que le message le plus récent reste toujours visible.
    void Print(string text)
    {
        lines.AddRange(text.Split('\n'));
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        output.text = string.Join("\n", lines);
        while (lines.Count > 1 && TextHeight(output.text) > output.rectTransform.rect.height)
        {
            lines.RemoveAt(0);
            output.text = string.Join("\n", lines);
        }
    }

    float TextHeight(string text)
    {
        TextGenerationSettings settings = output.GetGenerationSettings(new Vector2(output.rectTransform.rect.width, 0f));
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        return output.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / output.pixelsPerUnit;
    }
}
