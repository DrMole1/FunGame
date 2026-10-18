using System.Collections.Generic;
using UnityEngine;

// Lecture des sons : effets ponctuels (2D ou spatialisés en 3D, via un groupe de sources réutilisées),
// boucles attachées aux objets, et ambiances qui se croisent entre le chapiteau et le labyrinthe.
public class GameAudio : MonoBehaviour
{
    const int PoolSize = 28;
    const string VolumeKey = "fungame_volume";

    static GameAudio instance;

    readonly List<AudioSource> pool = new List<AudioSource>();
    int next;
    AudioSource hubMusic, mazeDrone, insects, abjurationMusic;

    // Proximité des murs infestés (0 à 1), fournie par la session de jeu.
    public static float InsectLevel { get; set; }
    // Abjuration en cours (0 ou 1) : sa musique lente remplace les autres ambiances.
    public static float AbjurationLevel { get; set; }
    // Ambiances actives (en jeu ou dans le menu).
    public static bool AmbienceEnabled { get; set; } = true;

    public static float Volume
    {
        get => AudioListener.volume;
        set
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        }
    }

    public static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("Audio");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameAudio>();
        AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 0.8f);
        SoundBank.Preload();
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
        insects = AmbientLoop("insects");
        abjurationMusic = AmbientLoop("abjuration_music");
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

    public static void Play2D(string clip, float volume = 1f, float pitchVariation = 0f, float pitch = 1f)
    {
        if (instance == null) return;
        var src = instance.NextSource();
        src.transform.localPosition = Vector3.zero;
        src.spatialBlend = 0f;
        Fire(src, clip, volume, pitchVariation, pitch);
    }

    public static void Play3D(string clip, Vector3 position, float volume = 1f, float maxDistance = 20f, float pitchVariation = 0.05f, float pitch = 1f)
    {
        if (instance == null || !InRange(position, maxDistance)) return;
        var src = instance.NextSource();
        src.transform.position = position;
        Configure3D(src, maxDistance);
        Fire(src, clip, volume, pitchVariation, pitch);
    }

    // Son spatialisé à décroissance douce : plein près de la source, puis s'éteint progressivement jusqu'à maxDistance.
    static readonly AnimationCurve SmoothRolloff = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.15f, 0.92f), new Keyframe(0.5f, 0.4f), new Keyframe(1f, 0f, 0f, 0f));

    public static void Play3DSmooth(string clip, Vector3 position, float volume, float maxDistance, float pitch)
    {
        if (instance == null || !InRange(position, maxDistance)) return;
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
        src.clip = SoundBank.Get(clip);
        src.loop = false;
        src.volume = volume;
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

    // Boucle spatialisée attachée à un objet (bourdonnement d'un laser...).
    public static AudioSource Loop3D(string clip, Transform parent, Vector3 localPosition, float volume, float maxDistance)
    {
        if (instance == null) return null;
        var go = new GameObject("Boucle_" + clip);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var src = go.AddComponent<AudioSource>();
        Configure3D(src, maxDistance);
        src.clip = SoundBank.Get(clip);
        src.loop = true;
        src.volume = volume;
        src.playOnAwake = false;
        return src;
    }

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
        float on = AmbienceEnabled ? 1f : 0f;
        float abj = AbjurationLevel * on;
        hubMusic.volume = Mathf.Lerp(hubMusic.volume, inHub ? 0.32f * on * (1f - abj * 0.8f) : 0f, k);
        mazeDrone.volume = Mathf.Lerp(mazeDrone.volume, inHub ? 0f : 0.55f * on * (1f - abj * 0.5f), k);
        float ka = 1f - Mathf.Exp(-0.6f * Time.unscaledDeltaTime);
        abjurationMusic.volume = Mathf.Lerp(abjurationMusic.volume, abj * 0.5f, ka);
        float ki = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
        insects.volume = Mathf.Lerp(insects.volume, inHub ? 0f : InsectLevel * 0.9f * on, ki);
    }
}
