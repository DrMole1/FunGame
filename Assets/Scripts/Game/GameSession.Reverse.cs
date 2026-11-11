using System.Collections.Generic;
using UnityEngine;

// Téléporteur Inversé (GDD 7.9, V0.6.10) : un par Hub au plus, acheté au Terminal de ce Hub (il n'y est alors plus proposé),
// il apparaît au centre du Hub. Relié à la Balise du Hub chargée, il fait voyager tous les joueurs qui se tiennent dessus,
// après la même canalisation qu'un Téléporteur (ils dansent 3 secondes), jusqu'à la Balise (ou Balise Améliorée) la plus
// éloignée de la Balise du Hub parmi celles du flux de ce Hub ; le voyage suit les liens d'énergie, dans l'autre sens.
// Ensuite : 120 secondes de recharge (teinte terne, jauge circulaire).
// Messages : VN|hub (hôte -> tous : il apparaît), VJ|hub|1/0 (joueur -> hôte : sur le disque ou non),
// VT|hub|balise (hôte -> tous : départ du voyage et recharge ; balise "x" : aucune destination), VR|hub|reste (hôte ->
// joueur qui arrive : recharge restante).
public partial class GameSession
{
    readonly Dictionary<int, ReverseTeleporter> reversePads = new Dictionary<int, ReverseTeleporter>();
    // Hôte : joueurs sur chaque disque, et début de la canalisation commune.
    readonly Dictionary<int, HashSet<string>> reverseRiders = new Dictionary<int, HashSet<string>>();
    readonly Dictionary<int, float> reverseChannelStart = new Dictionary<int, float>();
    // Sans destination, l'hôte réessaie un peu plus tard (une Balise a pu être posée entre-temps).
    const float ReverseRetryDelay = 3f;
    // Hub du disque sous le joueur local (0 : aucun) ; message "aucune destination" déjà affiché pendant qu'il y est.
    int reverseOn;
    bool reverseWarned;
    Vector3? teleportArrival;

    int CurrentHub() => NearestHub(player.transform.position).index;

    // Proposé au Terminal : la Boussole jusqu'à son achat, le Téléporteur Inversé tant que ce Hub n'en a pas.
    public bool ForSale(ItemDef def)
    {
        if (def.type == ItemType.Boussole) return !compass;
        if (def.type == ItemType.TeleporteurInverse) return !reversePads.ContainsKey(CurrentHub());
        return true;
    }

    void SpawnReverseTeleporter(int hub)
    {
        if (hub < 1 || reversePads.ContainsKey(hub)) return;
        var pad = ReverseTeleporter.Create(worldRoot, hub, WorldBuilder.HubCenter(hub));
        reversePads[hub] = pad;
        GameAudio.Play3D("pop", pad.transform.position, 0.9f, 30f, 0.05f, 0.6f);
        Fx.Burst(pad.transform.position + Vector3.up * 0.2f, Fx.Dirt, 20, 2f, 0.08f);
    }

    void ResetReverseTeleporters()
    {
        foreach (var pad in reversePads.Values) if (pad != null) Destroy(pad.gameObject);
        reversePads.Clear();
        reverseRiders.Clear();
        reverseChannelStart.Clear();
        reverseOn = 0;
        reverseWarned = false;
    }

    // À chaque image : lien, teinte et jauge des disques ; le joueur local signale à l'hôte s'il se tient sur l'un d'eux.
    void UpdateReverseTeleporters()
    {
        foreach (var kv in reversePads)
        {
            var pad = kv.Value;
            if (pad == null) continue;
            pad.Refresh(beacons);
            // GDD 12 (V0.6.11) : Teleporteur_Pret, recharge terminée ; entendu (spatialisé) par les joueurs présents dans son Hub.
            if (pad.JustReady && WorldBuilder.IsInHub(player.transform.position) && CurrentHub() == kv.Key)
                GameAudio.Play3D("teleporter_ready", pad.Top, 1f, 40f, 0f);
        }
        int on = 0;
        if (!status.Dead && !Teleporting && phase != Phase.GameOver && !InCannon && !CannonFlying && !InKennel && !Sliding)
        {
            Vector3 feet = player.transform.position + Vector3.down * 0.9f;
            foreach (var kv in reversePads)
                if (kv.Value != null && kv.Value.Ready(beacons) && kv.Value.Holds(feet)) { on = kv.Key; break; }
        }
        if (on != reverseOn)
        {
            if (reverseOn != 0) net.ToHost("VJ|" + reverseOn + "|0");
            if (on != 0) net.ToHost("VJ|" + on + "|1");
            reverseOn = on;
            reverseWarned = false;
            // Le son de canalisation dure toute l'attente ; il est coupé si le joueur descend du disque.
            StopChannelSound();
            if (on != 0) channelSound = GameAudio.Play2D("teleport_charge", 0.6f);
        }
        if (net.IsHost) UpdateHostReverse();
    }

    // Hôte : dès qu'un joueur se tient sur un disque prêt, la canalisation commence ; 3 secondes plus tard, tous ceux qui
    // y sont partent ensemble.
    void UpdateHostReverse()
    {
        foreach (var kv in reverseRiders)
        {
            int hub = kv.Key;
            kv.Value.RemoveWhere(id => (id != net.MyId && !net.Roster.ContainsKey(id)) || IsDeadForHost(id));
            bool ready = reversePads.TryGetValue(hub, out var pad) && pad != null && pad.Ready(beacons);
            if (kv.Value.Count == 0 || !ready)
            {
                reverseChannelStart.Remove(hub);
                continue;
            }
            if (!reverseChannelStart.TryGetValue(hub, out float start))
            {
                reverseChannelStart[hub] = Time.time;
                continue;
            }
            if (Time.time - start < GameRules.TeleportChannel) continue;
            Beacon dest = FarthestFluxBeacon(hub);
            if (dest == null)
            {
                reverseChannelStart[hub] = Time.time + ReverseRetryDelay;
                net.ToAll("VT|" + hub + "|x");
                continue;
            }
            reverseChannelStart.Remove(hub);
            net.ToAll("VT|" + hub + "|" + dest.Id);
        }
    }

    // Balise (ou Balise Améliorée) chargée par le flux de ce Hub, la plus éloignée de sa Balise principale.
    Beacon FarthestFluxBeacon(int hub)
    {
        Beacon root = beacons.GetHub(hub);
        if (root == null) return null;
        Beacon best = null;
        float bestDist = -1f;
        foreach (var b in beacons.Placed)
        {
            if (b == null || !b.Relays || !b.Charged || b.FluxHub != hub) continue;
            float d = (b.transform.position - root.transform.position).sqrMagnitude;
            if (d > bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    // Départ : chez chaque joueur qui se tient sur le disque, la boule d'énergie suit les liens de la Balise du Hub jusqu'à
    // la destination ; il arrive à côté d'elle.
    void LaunchReverse(int hub, string destination)
    {
        if (!reversePads.TryGetValue(hub, out var pad) || pad == null) return;
        if (destination == "x")
        {
            if (reverseOn == hub && !reverseWarned)
            {
                reverseWarned = true;
                hud.Toast(Loc.T("Aucune Balise du flux de ce Hub : le Téléporteur Inversé n'a nulle part où vous envoyer."));
            }
            return;
        }
        pad.StartCooldown(GameRules.ReverseTeleporterCooldown);
        GameAudio.Play3D("teleport", pad.transform.position, 1f, 35f, 0f, 0.8f);
        if (reverseOn != hub || status.Dead || Teleporting) return;
        Beacon target = beacons.Get(NetworkGame.I(destination));
        List<Vector3> path = target != null ? beacons.PathToHub(target) : null;
        if (path == null || path.Count == 0) return;
        path.Reverse();
        Vector3 last = target.transform.position;
        Vector3 from = path.Count >= 2 ? path[path.Count - 2] : pad.transform.position;
        Vector3 back = new Vector3(from.x - last.x, 0f, from.z - last.z);
        back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.forward;
        StartTeleport(pad.transform.position, path, last + back * 1.3f + Vector3.up * 0.95f);
    }

    bool HandleReverseMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "VN" when f.Length >= 2:
                SpawnReverseTeleporter(NetworkGame.I(f[1]));
                return true;
            case "VR" when f.Length >= 3:
                if (reversePads.TryGetValue(NetworkGame.I(f[1]), out var cooling) && cooling != null) cooling.StartCooldown(NetworkGame.P(f[2]));
                return true;
            case "VJ" when host && f.Length >= 3:
            {
                int hub = NetworkGame.I(f[1]);
                if (!reverseRiders.TryGetValue(hub, out var riders)) reverseRiders[hub] = riders = new HashSet<string>();
                if (f[2] == "1") riders.Add(from);
                else riders.Remove(from);
                return true;
            }
            case "VT" when f.Length >= 3:
                LaunchReverse(NetworkGame.I(f[1]), f[2]);
                return true;
        }
        return false;
    }

    // Hôte -> joueur qui arrive : disques déjà là, et leur recharge.
    void SendReverseSnapshot(string to)
    {
        foreach (var kv in reversePads)
        {
            if (kv.Value == null) continue;
            net.ToPeer(to, "VN|" + kv.Key);
            if (kv.Value.CoolingDown) net.ToPeer(to, "VR|" + kv.Key + "|" + NetworkGame.F(kv.Value.CooldownLeft));
        }
    }
}
