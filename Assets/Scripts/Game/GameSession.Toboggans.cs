using UnityEngine;

// Toboggans (GDD 10.3, V0.6.6) : le personnage qui s'approche de l'entrée, en haut, y entre et descend le long du tube. Dedans, il
// ne peut rien faire ; il ne subit aucun dégât et aucune entité ne peut le repérer ni le traquer (comme dans le Canon). Le
// trajet est connu de tous : seul le drapeau "caché", déjà envoyé avec la position, circule sur le réseau.
public partial class GameSession
{
    const float SlideSpeed = 11f, SlideEntryRange = 1.25f, SlideEntryHeight = 1.7f, SlideReentryDelay = 1.5f, SlideLookAhead = 3f;

    public static bool Sliding { get; private set; }
    Toboggan slide;
    float slideDistance, slideLeftAt = -10f;

    void UpdateToboggans()
    {
        if (Sliding) { RideToboggan(); return; }
        if (status.Dead || PlayerController.InputLocked || InKennel || InCannon || CannonFlying || Teleporting || localDig >= 0 || Time.time - slideLeftAt < SlideReentryDelay) return;
        Vector3 me = player.transform.position;
        int here = WorldBuilder.ZoneAt(me);
        foreach (var z in zones)
        {
            if (Mathf.Abs(z.index - here) > 1) continue;
            foreach (var t in z.slides)
            {
                Vector3 d = me - t.Entry;
                if (Mathf.Abs(d.y) <= SlideEntryHeight && new Vector2(d.x, d.z).sqrMagnitude <= SlideEntryRange * SlideEntryRange)
                {
                    EnterToboggan(t);
                    return;
                }
            }
        }
    }

    void EnterToboggan(Toboggan t)
    {
        slide = t;
        slideDistance = 0f;
        Sliding = true;
        player.SetFrozen(true);
        avatar.SetHidden(true);
        hud.SetCannonMode(true);
        GameAudio.Play2D("pop", 0.7f);
    }

    void RideToboggan()
    {
        if (status.Dead || phase == Phase.GameOver) { LeaveToboggan(false); return; }
        slideDistance += SlideSpeed * Time.deltaTime;
        if (slideDistance >= slide.Length) { LeaveToboggan(true); return; }
        Vector3 at = slide.At(slideDistance);
        player.Teleport(at, player.transform.eulerAngles.y);
        OrbitCamera.CannonEye = at;
        OrbitCamera.SlideLook = slide.At(slideDistance + SlideLookAhead);
    }

    // Arrivée : debout, devant la sortie du tube.
    void LeaveToboggan(bool arrived)
    {
        Sliding = false;
        slideLeftAt = Time.time;
        OrbitCamera.CannonEye = null;
        OrbitCamera.SlideLook = null;
        hud.SetCannonMode(false);
        avatar.SetHidden(false);
        player.SetFrozen(false);
        if (arrived && slide != null)
        {
            player.Teleport(slide.Exit, player.transform.eulerAngles.y);
            GameAudio.Play2D("pop", 0.7f);
        }
        slide = null;
    }

    // ---------- Ponts suspendus (hôte) ----------

    float bridgeTimer;

    // Trois joueurs ou plus sur un même pont : il casse, chez tous.
    void UpdateHostBridges()
    {
        bridgeTimer -= Time.unscaledDeltaTime;
        if (bridgeTimer > 0f) return;
        bridgeTimer = 0.2f;
        foreach (var z in zones)
            foreach (var b in z.bridges)
                if (!b.Broken && !brokenBridges.Contains(b.Id) && b.PlayersOn() >= GameRules.BridgeBreakPlayers)
                {
                    brokenBridges.Add(b.Id);
                    UpdateDoors(true);
                    BroadcastState();
                }
    }

    // ---------- Chargement du début de partie (chapitre 9.2) ----------

    bool worldLoading, uniquesPending, loadingSounds;
    float zipperEnd = -1f;
    const float ZipperDuration = 1.5f;

    // Chapitre 6.3 (V0.6.7) : aucune musique pendant le chargement, seulement le grésillement de la télévision ; une fermeture éclair à la fin.
    void UpdateWorldLoading()
    {
        if (!worldLoading) return;
        if (!loadingSounds)
        {
            loadingSounds = true;
            GameAudio.SetLoading(true);
        }
        LoadingScreen.Show((float)zones.Count / Mathf.Max(1, zoneCount));
        if (zipperEnd >= 0f)
        {
            if (Time.unscaledTime >= zipperEnd) EndWorldLoading();
            return;
        }
        if (zones.Count < zoneCount || zoneBuild != null || WorldStreamer.Busy) return;
        // Hôte : la navigation des deux premières zones est prête dès le début.
        if (net.IsHost && (!zones[0].nav.Ready || !zones[1].nav.Ready)) return;
        GameAudio.StopLoadingStatic();
        GameAudio.Play2D("zipper", 1f);
        zipperEnd = Time.unscaledTime + ZipperDuration;
    }

    void EndWorldLoading()
    {
        zipperEnd = -1f;
        loadingSounds = false;
        GameAudio.SetLoading(false);
        worldLoading = false;
        WorldStreamer.Loading = false;
        LoadingScreen.Hide();
        UpdateDoors(false);
        if (uniquesPending)
        {
            uniquesPending = false;
            PlaceUniqueObjects();
        }
    }

    void PlaceUniquesWhenReady()
    {
        if (worldLoading) uniquesPending = true;
        else PlaceUniqueObjects();
    }
}
