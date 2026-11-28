using System.Collections.Generic;
using UnityEngine;

// Lecture des sons : effets ponctuels (2D ou spatialisés en 3D, via un groupe de sources réutilisées),
// boucles attachées aux objets, et ambiances qui se croisent entre le chapiteau et le labyrinthe.
public class GameAudio : MonoBehaviour
{
    const int PoolSize = 28;
    const string MusicKey = "fungame_music", SfxKey = "fungame_sfx";

    static GameAudio instance;

    readonly List<AudioSource> pool = new List<AudioSource>();
    // Boucles attachées aux objets, avec leur volume de base (suivent le volume des effets sonores).
    readonly List<KeyValuePair<AudioSource, float>> loops = new List<KeyValuePair<AudioSource, float>>();
    readonly HashSet<AudioSource> nightmareLoops = new HashSet<AudioSource>();
    // Sources (du groupe ou en boucle) qui jouent un son d'entité : muettes dans le Hub.
    readonly HashSet<AudioSource> entitySources = new HashSet<AudioSource>();
    int next;
    AudioSource hubMusic, mazeDrone, peaceMusic;
    // V0.7.15 (14) : musique d'ambiance de chaque environnement (Échiquier, Château, Blockhaus), dans l'ordre de TerrainKind. Elles
    // démarrent doucement et tournent en boucle ; en changeant d'environnement (ou en rentrant au Hub), l'une s'efface pendant que
    // l'autre monte. Ambiance_Echiquier remplace le bourdon synthétisé du labyrinthe (Note 08 : 'maze_drone', gardé de côté, ne
    // joue plus).
    AudioSource[] environmentMusic;
    public static TerrainKind Environment = TerrainKind.Echiquier;
    // V0.7.16 (Note 01) : au-dessus d'un ravin (Blockhaus, douve du Château), la musique d'ambiance s'efface.
    public static bool InRavine;
    const float EnvironmentMusicVolume = 0.35f;
    const bool UseMazeDrone = false;
    // V0.7.11 (7.15) : Lunettes PEACE dans l'inventaire : hors du Hub, plus de musique angoissante, mais une musique douce et
    // joyeuse, avec de petits bruits d'oiseaux (y compris pendant le Couvre Feu).
    public static bool PeaceMode;
    const float HubMusicVolume = 0.2f;

    // Couvre Feu en cours (0 ou 1, V0.6.8) : la musique du labyrinthe se coupe net (celle du Hub continue), puis revient progressivement.
    public static float CurfewLevel { get; set; }
    float mazeReturn = 1f;
    // Ambiances actives (en jeu ou dans le menu).
    public static bool AmbienceEnabled { get; set; } = true;
    // Séquence de mort : 1 = normal, 0 = ambiances coupées.
    float deathDuck = 1f;
    // Tous les joueurs sont morts : plus rien ne s'entend, sauf les sons 2D (le glas).
    static bool silenced;

    // Volumes réglés dans le lobby et le menu pause (0 à 1, 50 % à la première connexion) :
    // la musique (ambiances du chapiteau et du labyrinthe) et les effets sonores (tout le reste).
    // 100 % correspond à 60 % du niveau brut des sons, trop fort sinon.
    const float MasterGain = 0.6f;
    static float music = 0.5f, sfx = 0.5f;

    public static float MusicVolume
    {
        get => music;
        set
        {
            music = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicKey, music);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }

    public static float SfxVolume
    {
        get => sfx;
        set
        {
            sfx = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxKey, sfx);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }

    public static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("Audio");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameAudio>();
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.5f));
        sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 0.5f));
        AudioListener.volume = MasterGain;
        instance.StartCoroutine(SoundBank.Preload());
        instance.Setup();
    }

    void Setup()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var src = new GameObject("Son").AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
            pool.Add(src);
        }
        hubMusic = AmbientLoop("hub_music");
        mazeDrone = AmbientLoop("maze_drone");
        peaceMusic = AmbientLoop("peace_music");
        environmentMusic = new[] { AmbientLoop("music_echiquier"), AmbientLoop("music_chateau"), AmbientLoop("music_blockhaus") };
    }

    AudioSource AmbientLoop(string clip)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = SoundBank.Get(clip);
        src.loop = true;
        src.volume = 0f;
        src.spatialBlend = 0f;
        src.Play();
        return src;
    }

    AudioSource NextSource()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            var src = pool[(next + i) % pool.Count];
            if (!src.isPlaying)
            {
                next = (next + i + 1) % pool.Count;
                return src;
            }
        }
        // Tout est occupé : on coupe le plus ancien.
        var oldest = pool[next];
        next = (next + 1) % pool.Count;
        return oldest;
    }

    // Retourne la source utilisée, pour pouvoir couper le son avant la fin.
    public static AudioSource Play2D(string clip, float volume = 1f, float pitchVariation = 0f, float pitch = 1f)
    {
        if (instance == null) return null;
        var src = instance.NextSource();
        src.transform.localPosition = Vector3.zero;
        src.spatialBlend = 0f;
        Fire(src, clip, volume, pitchVariation, pitch);
        return src;
    }

    // Sons émis par les entités (pièges, Couvre Feu, Normal, Cauchemar, Sanction) : on ne les entend pas depuis le Hub.
    // (Le Réveil, qui peut poursuivre un joueur jusque dans le Hub, s'y entend.)
    static readonly string[] EntityPrefixes = { "laser_", "dame_", "tour_", "pion_", "cloche_", "spider_", "fou_", "roi_", "cavalier_", "tete_" };

    static bool IsEntitySound(string clip)
    {
        foreach (string prefix in EntityPrefixes) if (clip.StartsWith(prefix)) return true;
        return false;
    }

    // Sons des Cauchemars (La Dame, La Tour, Le Fou, Le Roi, Le Cavalier) : un peu plus forts que les autres effets.
    const float NightmareGain = 1.3f;
    static readonly string[] NightmarePrefixes = { "dame_", "tour_", "fou_", "roi_", "cavalier_" };

    static float Gain(string clip)
    {
        foreach (string prefix in NightmarePrefixes) if (clip.StartsWith(prefix)) return NightmareGain;
        return 1f;
    }

    static bool ListenerInHub
    {
        get
        {
            Camera cam = Camera.main;
            return cam != null && WorldBuilder.IsInHub(cam.transform.position);
        }
    }

    // Retourne la source utilisée (null si le son n'est pas joué), pour pouvoir le couper avant la fin.
    public static AudioSource Play3D(string clip, Vector3 position, float volume = 1f, float maxDistance = 20f, float pitchVariation = 0.05f, float pitch = 1f)
    {
        if (instance == null || silenced || !InRange(position, maxDistance)) return null;
        if (IsEntitySound(clip) && ListenerInHub) return null;
        var src = instance.NextSource();
        src.transform.position = position;
        Configure3D(src, maxDistance);
        Fire(src, clip, volume, pitchVariation, pitch);
        return src;
    }

    // Son spatialisé dont la fin tombe dans 'seconds' secondes (GDD 12) : un son plus long démarre en cours de route,
    // un son plus court démarre en retard.
    public static AudioSource Play3DEndingIn(string clip, Vector3 position, float volume, float maxDistance, float seconds)
    {
        var src = Play3D(clip, position, volume, maxDistance, 0f);
        if (src == null || src.clip == null) return src;
        float lead = src.clip.length - Mathf.Max(0f, seconds);
        if (lead > 0f) src.time = Mathf.Min(lead, src.clip.length - 0.01f);
        else
        {
            src.Stop();
            src.PlayDelayed(-lead);
        }
        return src;
    }

    // Son spatialisé qui démarre dans 'delay' secondes.
    public static AudioSource Play3DDelayed(string clip, Vector3 position, float volume, float maxDistance, float pitchVariation, float delay)
    {
        var src = Play3D(clip, position, volume, maxDistance, pitchVariation);
        if (src == null || delay <= 0f) return src;
        src.Stop();
        src.PlayDelayed(delay);
        return src;
    }

    // V0.7.23 : son spatialisé qui « plane » légèrement à la fin, sans arrêt abrupt (cloche de la Télévision) : suivi de quelques copies
    // retardées, de plus en plus faibles et un peu plus graves (pas de filtre audio en WebGL).
    static readonly (float delay, float gain)[] TailTaps = { (0.16f, 0.32f), (0.34f, 0.17f), (0.55f, 0.08f) };

    public static AudioSource Play3DTail(string clip, Vector3 position, float volume, float maxDistance, float pitch)
    {
        var src = Play3D(clip, position, volume, maxDistance, 0f, pitch);
        if (src == null) return null;
        for (int k = 0; k < TailTaps.Length; k++)
        {
            var tap = Play3DDelayed(clip, position, volume * TailTaps[k].gain, maxDistance, 0f, TailTaps[k].delay);
            if (tap != null) tap.pitch = pitch * (1f - 0.01f * (k + 1));
        }
        return src;
    }

    // Son spatialisé à décroissance douce : plein près de la source, puis s'éteint progressivement jusqu'à maxDistance.
    static readonly AnimationCurve SmoothRolloff = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.15f, 0.92f), new Keyframe(0.5f, 0.4f), new Keyframe(1f, 0f, 0f, 0f));

    public static void Play3DSmooth(string clip, Vector3 position, float volume, float maxDistance, float pitch)
    {
        if (instance == null || silenced || !InRange(position, maxDistance)) return;
        if (IsEntitySound(clip) && ListenerInHub) return;
        var src = instance.NextSource();
        src.transform.position = position;
        Configure3D(src, maxDistance);
        src.rolloffMode = AudioRolloffMode.Custom;
        src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SmoothRolloff);
        Fire(src, clip, volume, 0f, pitch);
    }

    static void Fire(AudioSource src, string clip, float volume, float pitchVariation, float pitch)
    {
        src.Stop();
        if (IsEntitySound(clip)) instance.entitySources.Add(src);
        else instance.entitySources.Remove(src);
        src.clip = SoundBank.Get(clip);
        src.loop = false;
        src.volume = volume * Gain(clip) * sfx;
        src.pitch = pitch * (1f + Random.Range(-pitchVariation, pitchVariation));
        src.Play();
    }

    static void Configure3D(AudioSource src, float maxDistance)
    {
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 1f;
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0f;
    }

    // Arrêt d'une boucle en laissant planer un écho : WebGL ne gère pas les filtres audio, l'écho est donc fait de copies
    // retardées du son, de plus en plus faibles.
    const float EchoDelay = 0.22f, EchoDecay = 0.5f, EchoLife = 0.8f, EchoAttack = 0.03f, EchoReleaseFade = 0.3f;
    const int EchoTaps = 4;

    public static void ReleaseLoop(AudioSource src)
    {
        if (src == null) return;
        if (instance == null)
        {
            Destroy(src.gameObject);
            return;
        }
        int i = instance.loops.FindIndex(l => l.Key == src);
        float volume = i >= 0 ? instance.loops[i].Value : src.volume / Mathf.Max(sfx, 0.001f);
        if (i >= 0) instance.loops.RemoveAt(i);
        instance.StartCoroutine(instance.EchoOut(src, volume));
    }

    System.Collections.IEnumerator EchoOut(AudioSource src, float volume)
    {
        var taps = new AudioSource[EchoTaps];
        float position = src.time, start = Time.unscaledTime;
        float total = EchoDelay * EchoTaps + EchoLife;
        while (src != null && Time.unscaledTime - start < total)
        {
            float t = Time.unscaledTime - start;
            float level = silenced ? 0f : volume * sfx;
            src.volume = level * Mathf.Clamp01(1f - t / EchoReleaseFade);
            for (int k = 0; k < EchoTaps; k++)
            {
                float local = t - EchoDelay * (k + 1);
                if (local < 0f) continue;
                if (taps[k] == null) taps[k] = StartEchoTap(src, position);
                float envelope = Mathf.Clamp01(local / EchoAttack) * Mathf.Clamp01(1f - local / EchoLife);
                taps[k].volume = level * Mathf.Pow(EchoDecay, k + 1) * envelope;
            }
            yield return null;
        }
        // Les copies sont des enfants de la boucle : détruites avec elle.
        if (src != null) Destroy(src.gameObject);
    }

    static AudioSource StartEchoTap(AudioSource src, float position)
    {
        var go = new GameObject("Echo");
        go.transform.SetParent(src.transform, false);
        var tap = go.AddComponent<AudioSource>();
        tap.spatialBlend = 0f;
        tap.clip = src.clip;
        tap.loop = true;
        tap.volume = 0f;
        tap.playOnAwake = false;
        tap.time = position;
        tap.Play();
        return tap;
    }

    // V0.7.7 (Note 03) : son non spatialisé suivi d'un effet planant de réverbération (sons du Pompon et du Tableau de Bord
    // de la Control Room). Pas de filtre audio en WebGL : la réverbération est faite de copies retardées du son, de plus en
    // plus faibles, qui s'éteignent lentement après lui. 'length' > 0 : le son est coupé (en fondu) au bout de 'length' s.
    const int ReverbTaps = 6;
    const float ReverbSpacing = 0.12f, ReverbFirst = 0.3f, ReverbDecay = 0.72f, ReverbRelease = 0.25f, ReverbTailRelease = 0.35f;

    public static void Play2DReverb(string clip, float volume, float length = 0f)
    {
        if (instance == null || silenced) return;
        var audio = SoundBank.Get(clip);
        if (audio == null) return;
        instance.StartCoroutine(instance.ReverbSound(audio, volume, length > 0f ? Mathf.Min(length, audio.length) : audio.length));
    }

    System.Collections.IEnumerator ReverbSound(AudioClip clip, float volume, float length)
    {
        var holder = new GameObject("Reverb_" + clip.name);
        holder.transform.SetParent(transform, false);
        // Filet de sécurité : une mort (StopAllCoroutines) ne laisse pas traîner les sources.
        Destroy(holder, ReverbTaps * ReverbSpacing + length + ReverbTailRelease + 0.12f * ReverbTaps + 1f);
        var sources = new AudioSource[ReverbTaps + 1];
        var gains = new float[ReverbTaps + 1];
        var starts = new float[ReverbTaps + 1];
        var releases = new float[ReverbTaps + 1];
        for (int k = 0; k <= ReverbTaps; k++)
        {
            var src = holder.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.clip = clip;
            src.loop = false;
            // Les dernières copies sont légèrement plus graves : la queue « plane ».
            src.pitch = 1f - 0.008f * k;
            gains[k] = k == 0 ? 1f : ReverbFirst * Mathf.Pow(ReverbDecay, k - 1);
            starts[k] = k * ReverbSpacing;
            // Les copies s'éteignent de plus en plus lentement.
            releases[k] = k == 0 ? ReverbRelease : ReverbTailRelease + 0.12f * k;
            src.volume = 0f;
            if (k == 0) src.Play(); else src.PlayDelayed(starts[k]);
            sources[k] = src;
        }
        float t0 = Time.unscaledTime, end = ReverbTaps * ReverbSpacing + length + releases[ReverbTaps];
        while (Time.unscaledTime - t0 < end)
        {
            float t = Time.unscaledTime - t0;
            float level = silenced ? 0f : volume * sfx;
            for (int k = 0; k <= ReverbTaps; k++)
            {
                float local = t - starts[k];
                float envelope = local < 0f ? 0f : Mathf.Clamp01(1f - (local - length) / releases[k]);
                sources[k].volume = level * gains[k] * envelope;
            }
            yield return null;
        }
        Destroy(holder);
    }

    // Boucle non spatialisée (ordre d'un glyphe, entendu partout) ; ReleaseLoop l'arrête en douceur.
    // Écran de chargement : pas de musique ni de boucle sonore, seulement le grésillement d'une télévision.
    public static bool Loading { get; private set; }
    AudioSource loadingStatic;

    public static void SetLoading(bool on)
    {
        Loading = on;
        if (instance == null) return;
        if (!on) { StopLoadingStatic(); return; }
        if (instance.loadingStatic != null) return;
        var go = new GameObject("Chargement_Gresillement");
        go.transform.SetParent(instance.transform, false);
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.clip = SoundBank.Get("tv_static");
        src.loop = true;
        src.volume = 0.45f * sfx;
        src.Play();
        instance.loadingStatic = src;
    }

    public static void StopLoadingStatic()
    {
        if (instance == null || instance.loadingStatic == null) return;
        Destroy(instance.loadingStatic.gameObject);
        instance.loadingStatic = null;
    }

    public static AudioSource Loop2D(string clip, float volume)
    {
        if (instance == null) return null;
        var go = new GameObject("Boucle2D_" + clip);
        go.transform.SetParent(instance.transform, false);
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.clip = SoundBank.Get(clip);
        src.loop = true;
        src.volume = volume * sfx;
        src.playOnAwake = false;
        instance.loops.Add(new KeyValuePair<AudioSource, float>(src, volume));
        src.Play();
        return src;
    }

    // Boucle spatialisée attachée à un objet (bourdonnement d'un laser...).
    // smooth : décroissance douce avec la distance (le son s'éteint progressivement quand on s'éloigne).
    public static AudioSource Loop3D(string clip, Transform parent, Vector3 localPosition, float volume, float maxDistance, bool smooth = false)
    {
        if (instance == null) return null;
        var go = new GameObject("Boucle_" + clip);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var src = go.AddComponent<AudioSource>();
        Configure3D(src, maxDistance);
        if (smooth)
        {
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SmoothRolloff);
        }
        src.clip = SoundBank.Get(clip);
        src.loop = true;
        src.volume = volume * Gain(clip) * sfx;
        src.playOnAwake = false;
        if (IsEntitySound(clip)) instance.entitySources.Add(src);
        if (Gain(clip) != 1f) instance.nightmareLoops.Add(src);
        instance.loops.Add(new KeyValuePair<AudioSource, float>(src, volume * Gain(clip)));
        return src;
    }

    // Change le volume de base d'une boucle (fondu de sortie, par exemple) ; le volume des effets s'y applique.
    public static void SetLoopVolume(AudioSource src, float volume)
    {
        if (instance == null || src == null) return;
        int i = instance.loops.FindIndex(l => l.Key == src);
        if (i >= 0) instance.loops[i] = new KeyValuePair<AudioSource, float>(src, volume * (instance.nightmareLoops.Contains(src) ? NightmareGain : 1f));
    }

    // Boucle d'une entité (GDD 12 : pas de La Dame et du Cavalier, ambiance du Roi) : elle démarre en fondu quand 'on'
    // passe à vrai, s'éteint en fondu sinon (puis se met en pause, pour reprendre au même endroit), et suit le tempo
    // demandé (hauteur de lecture). Créée au premier besoin, attachée à l'entité.
    public class FadedLoop
    {
        readonly string clip;
        readonly float volume, maxDistance, fade;
        AudioSource src;
        float level;

        public FadedLoop(string clip, float volume, float maxDistance, float fade)
        {
            this.clip = clip;
            this.volume = volume;
            this.maxDistance = maxDistance;
            this.fade = fade;
        }

        public void Update(Transform parent, Vector3 localPosition, bool on, float pitch, float dt)
        {
            if (src == null)
            {
                if (!on) return;
                src = Loop3D(clip, parent, localPosition, 0f, maxDistance, true);
                if (src == null || src.clip == null) return;
                src.Play();
                // Départ au hasard dans la boucle : deux entités voisines ne jouent pas à l'unisson.
                src.time = Random.Range(0f, src.clip.length * 0.95f);
            }
            level = Mathf.MoveTowards(level, on ? 1f : 0f, dt / fade);
            if (level > 0f && !src.isPlaying) src.UnPause();
            else if (level <= 0f && src.isPlaying) src.Pause();
            src.pitch = Mathf.MoveTowards(src.pitch, pitch, dt * 1.5f);
            SetLoopVolume(src, volume * level);
        }
    }

    // Mort du joueur local : le râle, puis un silence total d'une seconde, puis un cœur qui ralentit et s'arrête,
    // les ambiances revenant doucement ensuite.
    // 'delay' : attente avant le râle (GDD 12 : 1 s après la chute à 0 PV).
    public static void PlayDeathSequence(float delay = 0f)
    {
        if (instance == null) return;
        instance.StopAllCoroutines();
        instance.StartCoroutine(instance.DeathSequence(delay));
    }

    public static void CancelDeathSequence()
    {
        if (instance == null) return;
        instance.StopAllCoroutines();
        instance.deathDuck = 1f;
        silenced = false;
        AudioListener.volume = MasterGain;
    }

    // Tous les joueurs sont morts : le son s'éteint avec l'image, un silence total, puis le glas.
    public static void PlayWipeOutSequence(float fade, float silence)
    {
        if (instance == null) return;
        instance.StopAllCoroutines();
        instance.StartCoroutine(instance.WipeOutSequence(fade, silence));
    }

    System.Collections.IEnumerator WipeOutSequence(float fade, float silence)
    {
        deathDuck = 0f;
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            AudioListener.volume = MasterGain * (1f - t / fade);
            yield return null;
        }
        AudioListener.volume = 0f;
        silenced = true;
        foreach (var src in pool) src.Stop();
        yield return new WaitForSecondsRealtime(silence);
        AudioListener.volume = MasterGain;
        Play2D("doom", 1f);
        Play2D("gameover", 0.55f, 0f, 0.8f);
    }

    System.Collections.IEnumerator DeathSequence(float delay)
    {
        deathDuck = 0f;
        yield return new WaitForSecondsRealtime(delay + 0.8f);
        for (float t = 0f; t < 0.15f; t += Time.unscaledDeltaTime)
        {
            AudioListener.volume = MasterGain * (1f - t / 0.15f);
            yield return null;
        }
        AudioListener.volume = 0f;
        yield return new WaitForSecondsRealtime(1f);
        AudioListener.volume = MasterGain;
        Play2D("heartbeat", 1f);
        yield return new WaitForSecondsRealtime(5f);
        for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime)
        {
            deathDuck = t / 3f;
            yield return null;
        }
        deathDuck = 1f;
    }

    // Explosion toute proche (0 à 1) : les oreilles sifflent. Tout le reste s'assourdit d'un coup puis revient en
    // quelques secondes (pas de filtre audio en WebGL : on baisse le volume général) ; le sifflement s'estompe avec.
    public static void Deafen(float strength)
    {
        if (instance == null || silenced || instance.deathDuck < 1f || strength <= 0f) return;
        if (instance.deafen != null) instance.StopCoroutine(instance.deafen);
        instance.deafen = instance.StartCoroutine(instance.DeafenSequence(Mathf.Clamp01(strength)));
    }

    Coroutine deafen;

    System.Collections.IEnumerator DeafenSequence(float strength)
    {
        const float Recovery = 4f;
        Play2D("tinnitus", 0.5f + 0.5f * strength);
        float low = Mathf.Lerp(0.6f, 0.15f, strength);
        for (float t = 0f; t < Recovery; t += Time.unscaledDeltaTime)
        {
            // Une mort ou la fin de partie reprennent la main sur le volume.
            if (silenced || deathDuck < 1f) { deafen = null; yield break; }
            AudioListener.volume = MasterGain * Mathf.Lerp(low, 1f, Mathf.SmoothStep(0f, 1f, t / Recovery));
            yield return null;
        }
        AudioListener.volume = MasterGain;
        deafen = null;
    }

    // Position du personnage suivi par la caméra (la caméra elle-même s'il n'y en a pas).
    static Vector3 ListenerBody(Camera cam)
    {
        if (cam == null) return Vector3.zero;
        var orbit = cam.GetComponent<OrbitCamera>();
        return orbit != null && orbit.target != null ? orbit.target.position : cam.transform.position;
    }

    // À 10 m ou moins d'une boule à facettes posée (GameSession).
    public static bool DiscoNear;

    public static bool InRange(Vector3 position, float distance)
    {
        Camera cam = Camera.main;
        return cam == null || (cam.transform.position - position).sqrMagnitude < distance * distance;
    }

    void Update()
    {
        Camera cam = Camera.main;
        bool inHub = cam == null || WorldBuilder.IsInHub(cam.transform.position);
        float k = 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime);
        float on = AmbienceEnabled && !silenced && !Loading ? 1f : 0f;
        on *= deathDuck;
        // V0.6.1 : près d'une boule à facettes posée, les musiques d'ambiance s'effacent progressivement (sa musique disco
        // les remplace), et reviennent quand on s'en éloigne.
        float tv = DiscoNear ? 0f : 1f;
        // V0.6.14 (15.3) : dans le tunnel derrière l'Écran des Logs, la musique du Hub s'efface progressivement ; plus aucune
        // musique dans la Control Room. Elle revient en rentrant dans le Hub.
        float tunnel = cam != null ? ControlWing.MusicSilence(ListenerBody(cam)) : 0f;
        bool hubSide = inHub || tunnel > 0f;
        // Musique_Hub (enregistrée, en boucle) : gardée discrète, même avec la musique réglée à 50 %.
        hubMusic.volume = Mathf.Lerp(hubMusic.volume, hubSide ? HubMusicVolume * on * tv * music * (1f - tunnel) : 0f, k);
        mazeReturn = CurfewLevel > 0f ? 0f : Mathf.MoveTowards(mazeReturn, 1f, Time.unscaledDeltaTime / GameRules.CurfewMusicReturn);
        mazeDrone.volume = Mathf.Lerp(mazeDrone.volume, hubSide || !UseMazeDrone ? 0f : 0.55f * on * tv * mazeReturn * music, k);
        if (CurfewLevel > 0f) mazeDrone.volume = 0f;
        if (PeaceMode) mazeDrone.volume = 0f;
        // Musiques des environnements : fondu lent (environ 2 s) d'une musique à l'autre ; coupées par le Couvre Feu (retour
        // progressif ensuite) et remplacées par la musique douce des Lunettes PEACE.
        float km = 1f - Mathf.Exp(-0.8f * Time.unscaledDeltaTime);
        for (int e = 0; e < environmentMusic.Length; e++)
        {
            var src = environmentMusic[e];
            bool active = !hubSide && !PeaceMode && !InRavine && (int)Environment == e;
            src.volume = Mathf.Lerp(src.volume, active ? EnvironmentMusicVolume * on * tv * mazeReturn * music : 0f, km);
            if (CurfewLevel > 0f || Loading) src.volume = 0f;
        }
        peaceMusic.volume = Mathf.Lerp(peaceMusic.volume, PeaceMode && !hubSide ? 0.45f * on * tv * music : 0f, k);
        if (Loading) hubMusic.volume = mazeDrone.volume = peaceMusic.volume = 0f;
        // Dans le Hub, les sons des entités se taisent (y compris ceux déjà commencés).
        bool hubListener = cam != null && inHub;
        for (int i = loops.Count - 1; i >= 0; i--)
        {
            if (loops[i].Key == null)
            {
                loops.RemoveAt(i);
                entitySources.RemoveWhere(src => src == null);
                nightmareLoops.RemoveWhere(src => src == null);
            }
            else loops[i].Key.volume = silenced || Loading || hubListener && entitySources.Contains(loops[i].Key) ? 0f : loops[i].Value * sfx;
        }
        if (hubListener)
            foreach (var src in pool)
                if (src.isPlaying && entitySources.Contains(src)) src.Stop();
    }
}
