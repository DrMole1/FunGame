using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V0.7.5 (chapitre 15.3) : l'écran de la Control Room et le Pompon du Hub de départ.
// - Allumé tant qu'une Cloche Décorative est posée sur le promontoire : chaque joueur le voit d'après les appareils posés
//   (déjà synchronisés). L'enlever l'éteint, et l'écran se reverrouille (saisie effacée).
// - Mot de passe sonore de 6 glyphes (1, 2 ou 3), tiré au début de la partie d'après la graine de la partie (le même chez tous).
// - L'hôte tient la saisie, le déverrouillage et la zone affichée ; transmis avec l'état de la partie (G, 23e champ).
// Messages : XG|glyphe (joueur -> hôte : bouton de glyphe) -> XH|glyphe|saisis|résultat (tous ; 0 : en cours, 1 : refusé,
// 2 : accepté) ; XA|sens (joueur -> hôte : flèche) -> XZ|zone (tous) ; XM (joueur -> hôte : Pompon) -> XN (tous).
public partial class GameSession
{
    const float ControlSoundLength = 1.5f, PomponSequence = ControlScreen.PasswordLength * ControlSoundLength;
    // L'écran n'est rendu que regardé de près (depuis la Control Room).
    const float FeedViewRange = 18f;

    readonly List<int> controlEntry = new List<int>();
    int controlCount, controlZone = 1;
    bool controlUnlocked, controlPowered;
    int[] controlPassword;
    int controlPasswordSeed = int.MinValue;
    float pomponBusyUntil = -10f;
    int controlFeedZone;            // zone montrée par les caméras et regardée par ce joueur (0 : aucune)
    int feedsZone = -1, feedsEyeCount = -1;
    float controlTimer, feedsCheckAt;
    ControlScreen lastScreen;

    int[] ControlPassword
    {
        get
        {
            if (controlPassword == null || controlPasswordSeed != seed)
            {
                var rnd = new System.Random(seed ^ 0x2E7A51);
                controlPassword = new int[ControlScreen.PasswordLength];
                for (int i = 0; i < controlPassword.Length; i++) controlPassword[i] = rnd.Next(GameRules.GlyphTypes);
                controlPasswordSeed = seed;
            }
            return controlPassword;
        }
    }

    string ControlState => controlCount + "." + (controlUnlocked ? 1 : 0) + "." + controlZone;

    void ApplyControlState(string field)
    {
        string[] p = field.Split('.');
        controlCount = p.Length > 0 ? Mathf.Clamp(NetworkGame.I(p[0]), 0, ControlScreen.PasswordLength) : 0;
        controlUnlocked = p.Length > 1 && p[1] == "1";
        controlZone = p.Length > 2 ? Mathf.Clamp(NetworkGame.I(p[2]), 1, ControlScreen.MaxZone) : 1;
        RefreshControlScreen();
    }

    void ResetControlScreen()
    {
        controlEntry.Clear();
        controlCount = 0;
        controlUnlocked = false;
        controlZone = 1;
        WorldStreamer.ExtraEyes.Clear();
        RefreshControlScreen();
    }

    // Une Cloche Décorative (quelle qu'elle soit) posée sur le dessus du promontoire.
    bool BellOnPedestal()
    {
        var cs = ControlScreen.Current;
        if (cs == null) return false;
        Vector3 top = cs.PedestalTop;
        foreach (var b in beacons.Placed)
        {
            if (b == null || !Beacon.IsBellKind(b.Kind)) continue;
            Vector3 d = b.transform.position - top;
            if (new Vector2(d.x, d.z).magnitude <= cs.PedestalReach && d.y > -0.15f && d.y < 0.4f) return true;
        }
        return false;
    }

    void RefreshControlScreen() => ControlScreen.Current?.Apply(controlPowered, controlCount, controlUnlocked, controlZone);

    // Le joueur (ou la caméra) est dans la Control Room.
    static bool InControlRoom(Vector3 p) => ControlScreen.Current != null && ControlWing.InControlRoom(p);

    static Vector3 ListenerPosition => Camera.main != null ? Camera.main.transform.position : Vector3.zero;

    void UpdateControlScreen()
    {
        var cs = ControlScreen.Current;
        if (cs == null) { controlFeedZone = 0; return; }
        // Nouveau monde (nouvelle partie) : nouvel écran, tout est à revoir.
        if (cs != lastScreen)
        {
            lastScreen = cs;
            controlPowered = false;
            feedsZone = feedsEyeCount = -1;
            controlTimer = 0f;
            RefreshControlScreen();
        }
        controlTimer -= Time.deltaTime;
        if (controlTimer <= 0f)
        {
            controlTimer = 0.2f;
            bool on = BellOnPedestal();
            if (on != controlPowered)
            {
                controlPowered = on;
                // Plus de courant : la saisie est perdue et l'écran se reverrouille (chez chacun, l'hôte compris).
                if (!on)
                {
                    controlEntry.Clear();
                    controlCount = 0;
                    controlUnlocked = false;
                    controlZone = 1;
                }
                RefreshControlScreen();
            }
        }

        // Caméras de sécurité : seulement quand ce joueur regarde l'écran de près.
        Vector3 eye = ListenerPosition;
        bool watching = cs.Powered && cs.Unlocked && InControlRoom(eye) && Vector3.Distance(eye, cs.ScreenPosition) < FeedViewRange;
        controlFeedZone = watching ? controlZone : 0;
        WorldStreamer.ExtraEyes.Clear();
        if (!watching) return;
        UpdateFeedEyes(cs);
        for (int i = 0; i < ControlScreen.FeedCount; i++)
            if (cs.FeedLive(i)) WorldStreamer.ExtraEyes.Add(cs.FeedPosition(i));
        cs.RenderFeeds();
    }

    // 6 yeux au hasard dans la zone affichée (les mêmes chez tous), à au moins 20 m les uns des autres. Les yeux d'une zone
    // sont construits sur plusieurs images : le choix est refait tant que leur nombre change.
    void UpdateFeedEyes(ControlScreen cs)
    {
        // Parcourir toute une zone coûte cher : au plus une fois par seconde (et aussitôt la zone changée).
        if (controlZone == feedsZone && Time.time < feedsCheckAt) return;
        feedsCheckAt = Time.time + 1f;
        WorldBuilder.ZoneRefs zone = null;
        foreach (var z in zones) if (z.index == controlZone && z.root != null && z.root.activeSelf) zone = z;
        var all = zone != null ? zone.root.GetComponentsInChildren<Eye>(true) : new Eye[0];
        if (controlZone == feedsZone && all.Length == feedsEyeCount) return;
        feedsZone = controlZone;
        feedsEyeCount = all.Length;
        // D'abord les yeux des murs (une vue de face), puis ceux du sol et du plafond.
        var wall = new List<Transform>();
        var other = new List<Transform>();
        foreach (var e in all) (Mathf.Abs(e.transform.forward.y) < 0.5f ? wall : other).Add(e.transform);
        var rnd = new System.Random(seed * 31 + controlZone * 7919);
        Shuffle(wall, rnd);
        Shuffle(other, rnd);
        wall.AddRange(other);
        var chosen = new List<Transform>();
        float min = GameRules.ControlCameraSpacing * GameRules.ControlCameraSpacing;
        foreach (var t in wall)
        {
            if (chosen.Count >= ControlScreen.FeedCount) break;
            bool far = true;
            foreach (var c in chosen) if ((c.position - t.position).sqrMagnitude < min) { far = false; break; }
            if (far) chosen.Add(t);
        }
        cs.SetFeeds(chosen);
    }

    // ---------- Actions du joueur local ----------

    public void PressControlButton(int kind)
    {
        var cs = ControlScreen.Current;
        if (cs == null || !cs.Powered || status.Dead) return;
        Gesture(PlayerGesture.Use);
        if (kind < ControlButton.Left)
        {
            if (!cs.Unlocked) net.ToHost("XG|" + kind);
        }
        else if (cs.Unlocked) net.ToHost("XA|" + (kind == ControlButton.Left ? -1 : 1));
    }

    public void PullPompon()
    {
        if (status.Dead) return;
        net.ToHost("XM");
    }

    // Son d'un glyphe, coupé au bout de 1,5 seconde.
    void PlayControlSound(int glyph, float volume)
    {
        var src = GameAudio.Play2D(Glyph.Sounds[Mathf.Clamp(glyph, 0, Glyph.Sounds.Length - 1)], volume);
        if (src != null) StartCoroutine(StopAfter(src, src.clip, ControlSoundLength));
    }

    static IEnumerator StopAfter(AudioSource src, AudioClip clip, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (src != null && src.clip == clip && src.isPlaying) src.Stop();
    }

    // Les 6 sons du mot de passe, dans l'ordre, pour les joueurs dans le chapiteau du Hub de départ.
    IEnumerator PlayPomponSequence(int[] password)
    {
        for (int i = 0; i < password.Length; i++)
        {
            if (WorldBuilder.HubAt(ListenerPosition) == 1) PlayControlSound(password[i], 0.9f);
            yield return new WaitForSeconds(ControlSoundLength);
        }
    }

    bool HandleControlMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "XG" when host:
            {
                if (f.Length < 2 || !controlPowered || controlUnlocked) return true;
                int glyph = Mathf.Clamp(NetworkGame.I(f[1]), 0, GameRules.GlyphTypes - 1);
                controlEntry.Add(glyph);
                int result = 0;
                if (controlEntry.Count >= ControlScreen.PasswordLength)
                {
                    bool ok = true;
                    int[] pw = ControlPassword;
                    for (int i = 0; i < pw.Length; i++) if (controlEntry[i] != pw[i]) ok = false;
                    result = ok ? 2 : 1;
                    controlEntry.Clear();
                }
                net.ToAll("XH|" + glyph + "|" + (result == 0 ? controlEntry.Count : 0) + "|" + result);
                BroadcastState();
                return true;
            }
            case "XH":
            {
                if (f.Length < 4) return true;
                int glyph = NetworkGame.I(f[1]), result = NetworkGame.I(f[3]);
                bool inRoom = InControlRoom(ListenerPosition);
                // Le son du bouton ne résonne que dans la pièce.
                if (inRoom) PlayControlSound(glyph, 0.8f);
                controlCount = Mathf.Clamp(NetworkGame.I(f[2]), 0, ControlScreen.PasswordLength);
                if (result == 2) { controlUnlocked = true; controlZone = 1; }
                if (inRoom && result == 1) GameAudio.Play2D("error", 0.8f);
                if (inRoom && result == 2) GameAudio.Play2D("beep", 0.7f, 0f, 1.2f);
                RefreshControlScreen();
                return true;
            }
            case "XA" when host:
                if (f.Length >= 2 && controlPowered && controlUnlocked)
                {
                    controlZone = Mathf.Clamp(controlZone + (NetworkGame.I(f[1]) < 0 ? -1 : 1), 1, ControlScreen.MaxZone);
                    net.ToAll("XZ|" + controlZone);
                    BroadcastState();
                }
                return true;
            case "XZ":
                if (f.Length >= 2) controlZone = Mathf.Clamp(NetworkGame.I(f[1]), 1, ControlScreen.MaxZone);
                if (InControlRoom(ListenerPosition)) GameAudio.Play2D("tick", 0.5f);
                RefreshControlScreen();
                return true;
            case "XM" when host:
                // Une séquence à la fois.
                if (Time.time >= pomponBusyUntil)
                {
                    pomponBusyUntil = Time.time + PomponSequence;
                    net.ToAll("XN");
                }
                return true;
            case "XN":
                Pompon.Current?.Jerk();
                StartCoroutine(PlayPomponSequence(ControlPassword));
                return true;
        }
        return false;
    }
}
