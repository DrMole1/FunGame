using UnityEngine;

// Chapitre 15 (V0.6.14) : la Carte Magnétique 1 et la porte du tunnel de la Control Room. L'hôte tient l'état (carte prise,
// porte ouverte), transmis avec l'état de la partie (G, 22e champ) pour ceux qui arrivent en cours de route.
// Messages : SC (joueur -> hôte : prendre la carte) -> GV|S1 au joueur + SK (tous : la carte disparaît, le cadavre s'agite) ;
// SD (joueur -> hôte : carte passée dans le lecteur) -> SO (tous : la porte s'ouvre). Rares : aucun coût réseau notable.
public partial class GameSession
{
    public static bool SecretCardTaken { get; private set; }
    public static bool ControlRoomOpen { get; private set; }

    int SecretFlags => (SecretCardTaken ? 1 : 0) | (ControlRoomOpen ? 2 : 0);

    // Le joueur local ramasse la carte dans la main du cadavre.
    public void TakeSecretCard()
    {
        if (SecretCardTaken || status.Dead) return;
        if (!HasFreeSlot)
        {
            hud.Toast(Loc.T("Votre inventaire est plein !"));
            return;
        }
        pendingPickups++;
        Gesture(PlayerGesture.PickUp);
        net.ToHost("SC");
    }

    // Le joueur local passe la carte dans le lecteur de la porte : elle est consommée.
    public void UseControlCard()
    {
        int slot = status.Selected;
        if (ControlRoomOpen || slot < 0 || status[slot] != ItemType.CarteMagnetique1) return;
        status.RemoveAt(slot);
        Gesture(PlayerGesture.Use);
        GameAudio.Play2D("beep", 0.6f, 0f, 1.3f);
        net.ToHost("SD");
    }

    bool HandleSecretMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "SC" when host:
                if (!SecretCardTaken)
                {
                    SecretCardTaken = true;
                    net.ToPeer(from, "GV|" + (int)ItemType.CarteMagnetique1);
                    net.ToAll("SK");
                    BroadcastState();
                }
                else net.ToPeer(from, "GV|0");
                return true;
            case "SK":
                SecretCardTaken = true;
                if (SecretCorpse.Current != null) SecretCorpse.Current.SetCardTaken(true, true);
                return true;
            case "SD" when host:
                if (!ControlRoomOpen)
                {
                    ControlRoomOpen = true;
                    net.ToAll("SO");
                    BroadcastState();
                }
                return true;
            case "SO":
                ControlRoomOpen = true;
                if (ControlDoor.Current != null) ControlDoor.Current.SetOpen(true, true);
                return true;
        }
        return false;
    }

    void ApplySecretFlags(int flags)
    {
        bool taken = (flags & 1) != 0, open = (flags & 2) != 0;
        if (taken != SecretCardTaken || open != ControlRoomOpen)
        {
            SecretCardTaken = taken;
            ControlRoomOpen = open;
            if (SecretCorpse.Current != null) SecretCorpse.Current.SetCardTaken(taken, false);
            if (ControlDoor.Current != null) ControlDoor.Current.SetOpen(open, false);
        }
    }

    // Nouvelle partie : la carte est de nouveau dans la main du cadavre, la porte refermée.
    void ResetSecret() => ApplySecretFlags(0);
}
