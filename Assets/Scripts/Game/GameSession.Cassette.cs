using UnityEngine;

// V0.7.24 (17.4, S3) : la Cassette, vieille cassette VHS. En début de partie, l'hôte la pose dans l'un des trois casiers de la Control
// Room (porte fermée : on la découvre en ouvrant le casier). Objet interactif posé au sol comme une Notice (ramassé, reposé).
// Un joueur qui vise une Télévision avec la Cassette en main la lui fait lire : la Cassette quitte son inventaire, et la Télévision
// diffuse l'enregistrement (FR ou EN selon la langue de chaque joueur, à 10 m) pendant 50 s avant de reprendre son cours.
// Messages : XT|télé (joueur -> hôte) -> XU|télé (tous). Rares : aucun coût réseau notable. Un joueur qui arrive pendant la lecture ne
// l'entend pas (50 s au plus).
public partial class GameSession
{
    // Hôte, début de partie : la Cassette dans un casier de la Control Room tiré au hasard.
    void PlaceCassette()
    {
        int door = Lockers.Count + Random.Range(0, Lockers.Count);
        if (!Lockers.CompartmentPoint(door, out Vector3 point, out float yaw))
        {
            Debug.LogWarning("Cassette : casiers de la Control Room introuvables.");
            return;
        }
        PlaceDevice(DeviceKind.Cassette, point, yaw + Random.Range(-12f, 12f), "");
    }

    void UseCassette(Beacon tv)
    {
        int slot = status.Selected;
        if (tv == null || status.Dead || slot < 0 || status[slot] != ItemType.Cassette) return;
        status.RemoveAt(slot);
        Gesture(PlayerGesture.Use);
        net.ToHost("XT|" + tv.Id);
    }

    void HandleCassetteMessage(string[] f, bool host)
    {
        int id = f.Length >= 2 ? NetworkGame.I(f[1]) : int.MinValue;
        if (f[0] == "XT")
        {
            // La Télévision a pu être ramassée entre-temps : la Cassette est alors perdue, comme une Carte passée dans un lecteur.
            if (beacons.Get(id) != null) net.ToAll("XU|" + id);
            return;
        }
        var tv = beacons.Get(id);
        if (tv != null && tv.Screen != null) tv.Screen.PlayCassette();
    }
}
