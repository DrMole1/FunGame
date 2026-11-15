using UnityEngine;

// Chapitre 15 (V0.6.14) : la Carte Magnétique 1 et la porte du tunnel de la Control Room. L'hôte tient l'état (carte prise,
// porte ouverte), transmis avec l'état de la partie (G, 22e champ) pour ceux qui arrivent en cours de route.
// Messages (préfixes en X : SC et SK sont déjà pris par les statistiques de manche et le Bouton Skip) :
// XC (joueur -> hôte : prendre la carte) -> GV|S1 au joueur + XK (tous : la carte disparaît, le cadavre s'agite) ;
// XD|porte (joueur -> hôte : carte passée dans le lecteur) -> XO|porte (tous : la porte s'ouvre). Porte 1 : tunnel (Carte
// Magnétique 1) ; porte 2 (V0.7.4) : Vestiaire (Carte Magnétique 2). XP (joueur -> hôte : pousse la porte du Bureau L.I.V.)
// -> XQ (tous). V0.7.7 (Note 08) : XE|casier (joueur -> hôte : ouvre ou ferme une porte des casiers du Vestiaire ou de la Control Room) ->
// XF|casier|ouvert (tous). Rares : aucun coût réseau notable.
public partial class GameSession
{
    public static bool SecretCardTaken { get; private set; }
    public static bool ControlRoomOpen { get; private set; }
    public static bool VestiaireOpen { get; private set; }
    public static bool LivDoorPushed { get; private set; }

    // V0.7.7 (Note 08) : portes ouvertes des casiers du Vestiaire (un bit par porte).
    static int lockerBits;
    public static bool LockerOpen(int i) => (lockerBits & (1 << i)) != 0;

    public static bool SecretDoorOpen(int door) => door == 2 ? VestiaireOpen : ControlRoomOpen;

    int SecretFlags => (SecretCardTaken ? 1 : 0) | (ControlRoomOpen ? 2 : 0) | (VestiaireOpen ? 4 : 0) | (LivDoorPushed ? 8 : 0) | (lockerBits << 4);

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
        net.ToHost("XC");
    }

    // Le joueur local passe la carte dans le lecteur de la porte : elle est consommée.
    public void UseControlCard(int door = 1)
    {
        int slot = status.Selected;
        ItemType card = door == 2 ? ItemType.CarteMagnetique2 : ItemType.CarteMagnetique1;
        if (SecretDoorOpen(door) || slot < 0 || status[slot] != card) return;
        status.RemoveAt(slot);
        Gesture(PlayerGesture.Use);
        GameAudio.Play2D("beep", 0.6f, 0f, 1.3f);
        net.ToHost("XD|" + door);
    }

    // V0.7.4 : le joueur local pousse la porte entrouverte du Bureau L.I.V.
    public void PushLivDoor()
    {
        if (LivDoorPushed || status.Dead) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("XP");
    }

    // V0.7.7 : le joueur local ouvre ou ferme une porte des casiers.
    public void ToggleLocker(int i)
    {
        if (status.Dead || i < 0 || i >= Lockers.TotalDoors) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("XE|" + i);
    }

    static void SetLocker(int i, bool open)
    {
        if (open) lockerBits |= 1 << i;
        else lockerBits &= ~(1 << i);
    }

    bool HandleSecretMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "XE" when host:
            {
                int i = f.Length >= 2 ? NetworkGame.I(f[1]) : -1;
                if (i < 0 || i >= Lockers.TotalDoors) return true;
                bool open = !LockerOpen(i);
                net.ToAll("XF|" + i + "|" + (open ? 1 : 0));
                return true;
            }
            case "XF":
            {
                int i = f.Length >= 3 ? NetworkGame.I(f[1]) : -1;
                if (i < 0 || i >= Lockers.TotalDoors) return true;
                SetLocker(i, f[2] == "1");
                LockerDoor.Get(i)?.SetOpen(LockerOpen(i), true);
                if (host) BroadcastState();
                return true;
            }
            case "XC" when host:
                if (!SecretCardTaken)
                {
                    SecretCardTaken = true;
                    net.ToPeer(from, "GV|" + (int)ItemType.CarteMagnetique1);
                    net.ToAll("XK");
                    BroadcastState();
                }
                else net.ToPeer(from, "GV|0");
                return true;
            case "XK":
                SecretCardTaken = true;
                if (SecretCorpse.Current != null) SecretCorpse.Current.SetCardTaken(true, true);
                return true;
            case "XD" when host:
            {
                int door = f.Length >= 2 && f[1] == "2" ? 2 : 1;
                if (!SecretDoorOpen(door))
                {
                    SetSecretDoor(door, true);
                    net.ToAll("XO|" + door);
                    BroadcastState();
                }
                return true;
            }
            case "XO":
            {
                int door = f.Length >= 2 && f[1] == "2" ? 2 : 1;
                SetSecretDoor(door, true);
                ControlDoor.Get(door)?.SetOpen(true, true);
                return true;
            }
            case "XP" when host:
                if (!LivDoorPushed)
                {
                    LivDoorPushed = true;
                    net.ToAll("XQ");
                    BroadcastState();
                }
                return true;
            case "XQ":
                LivDoorPushed = true;
                if (LivDoor.Current != null) LivDoor.Current.SetPushed(true, true);
                return true;
        }
        return false;
    }

    static void SetSecretDoor(int door, bool open)
    {
        if (door == 2) VestiaireOpen = open;
        else ControlRoomOpen = open;
    }

    void ApplySecretFlags(int flags)
    {
        bool taken = (flags & 1) != 0, open = (flags & 2) != 0, vestiaire = (flags & 4) != 0, pushed = (flags & 8) != 0;
        int lockers = (flags >> 4) & ((1 << Lockers.TotalDoors) - 1);
        if (lockers != lockerBits)
        {
            lockerBits = lockers;
            for (int i = 0; i < Lockers.TotalDoors; i++) LockerDoor.Get(i)?.SetOpen(LockerOpen(i), false);
        }
        if (taken != SecretCardTaken || open != ControlRoomOpen || vestiaire != VestiaireOpen || pushed != LivDoorPushed)
        {
            SecretCardTaken = taken;
            ControlRoomOpen = open;
            VestiaireOpen = vestiaire;
            LivDoorPushed = pushed;
            if (SecretCorpse.Current != null) SecretCorpse.Current.SetCardTaken(taken, false);
            ControlDoor.Get(1)?.SetOpen(open, false);
            ControlDoor.Get(2)?.SetOpen(vestiaire, false);
            if (LivDoor.Current != null) LivDoor.Current.SetPushed(pushed, false);
        }
    }

    // Nouvelle partie : la carte est de nouveau dans la main du cadavre, les portes refermées (celle du Bureau entrouverte).
    void ResetSecret()
    {
        ApplySecretFlags(0);
        ResetControlScreen();
    }
}
