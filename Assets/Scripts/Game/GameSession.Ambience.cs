using UnityEngine;

// V0.7.2 (GDD 12) : sons d'ambiance joués une seule fois par partie (remis à zéro à la partie suivante, après un game over),
// pour ce joueur seulement, non spatialisés et à 50 % du volume :
// - Ambiance_Statue : il s'approche à moins de 10 m d'une statue de Pavlov ;
// - Ambiance_Entree : il pénètre pour la première fois dans le labyrinthe.
// - Ambiance_Ouverture (V0.7.5, avant : à chaque porte neutre ou d'acquisition qui s'ouvre) : il pénètre pour la première fois
//   dans la Zone 2.
public partial class GameSession
{
    const float StatueCueRange = 10f, AmbienceCueVolume = 0.5f;
    bool statueCuePlayed, entranceCuePlayed, zone2CuePlayed;
    float cueTimer;

    void ResetAmbienceCues() => statueCuePlayed = entranceCuePlayed = zone2CuePlayed = false;

    void UpdateAmbienceCues()
    {
        if (status.Dead || worldLoading || (statueCuePlayed && entranceCuePlayed && zone2CuePlayed)) return;
        cueTimer -= Time.deltaTime;
        if (cueTimer > 0f) return;
        cueTimer = 0.25f;
        Vector3 p = player.transform.position;
        if (!entranceCuePlayed && !Teleporting && !WorldBuilder.IsInHub(p) && WorldBuilder.ZoneAt(p) > 0)
        {
            entranceCuePlayed = true;
            GameAudio.Play2D("ambience_entrance", AmbienceCueVolume);
        }
        if (!zone2CuePlayed && !Teleporting && !WorldBuilder.IsInHub(p) && WorldBuilder.ZoneAt(p) == 2)
        {
            zone2CuePlayed = true;
            GameAudio.Play2D("ambience_opening", AmbienceCueVolume);
        }
        if (statueCuePlayed) return;
        foreach (var z in zones)
            foreach (Vector3 statue in z.statues)
            {
                Vector3 d = statue - p;
                if (new Vector2(d.x, d.z).magnitude > StatueCueRange || Mathf.Abs(d.y) > 20f) continue;
                statueCuePlayed = true;
                GameAudio.Play2D("ambience_statue", AmbienceCueVolume);
                return;
            }
    }
}
