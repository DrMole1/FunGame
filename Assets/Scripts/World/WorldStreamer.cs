using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Étale sur plusieurs images ce qui figerait le jeu d'un coup à la création d'une zone (décor purement visuel, captures de
// reflets), et éteint les lampes trop loin de la caméra pour éclairer quoi que ce soit de visible.
public class WorldStreamer : MonoBehaviour
{
    // Temps consacré à la construction à chaque image.
    const long BudgetMs = 3;
    // Reflets : capturés seulement près de la caméra, le plus proche d'abord, une image sur deux (chacun rend 6 vues). La caméra
    // voit à 45 m : à 70 m, le reflet est prêt avant d'être visible.
    const float ProbeRange = 70f;
    const int ProbeFrameInterval = 2;
    // Portée utile d'une capture : sa distance de vue (30 m) et la portée des lampes qu'elle montre.
    const float ProbeLampRange = 50f;

    // La caméra voit à 45 m et une lampe porte à moins de 10 m : au-delà, elle n'éclaire rien de visible.
    const float LampRange = 55f;
    const int LampsPerFrame = 64;

    static WorldStreamer instance;
    readonly Queue<Action> work = new Queue<Action>();
    readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
    readonly List<FlickerLight> lamps = new List<FlickerLight>();
    readonly Stopwatch watch = new Stopwatch();
    int nextLamp;

    static WorldStreamer Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("WorldStreamer");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<WorldStreamer>();
            }
            return instance;
        }
    }

    // Du décor reste à poser.
    public static bool Busy => instance != null && instance.work.Count > 0;

    // Hors du jeu (outils de l'éditeur), le travail est fait tout de suite.
    public static void Enqueue(Action action)
    {
        if (Application.isPlaying) Instance.work.Enqueue(action);
        else action();
    }

    public static void EnqueueProbe(ReflectionProbe probe)
    {
        if (Application.isPlaying) Instance.probes.Add(probe);
        else probe.RenderProbe();
    }

    public static void Register(FlickerLight lamp)
    {
        if (Application.isPlaying) Instance.lamps.Add(lamp);
    }

    void Update()
    {
        watch.Restart();
        while (work.Count > 0 && watch.ElapsedMilliseconds < BudgetMs) work.Dequeue()();
        // Les reflets montrent le décor et les lampes : ils attendent que tout soit en place.
        bool captured = work.Count == 0 && probes.Count > 0 && Time.frameCount % ProbeFrameInterval == 0 && CaptureNearestProbe();
        // La capture se fait plus tard dans l'image : ses lampes doivent rester allumées jusque-là.
        if (!captured) CullLamps();
    }

    bool CaptureNearestProbe()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 eye = cam.transform.position;
        int best = -1;
        float bestDist = ProbeRange * ProbeRange;
        for (int k = probes.Count - 1; k >= 0; k--)
        {
            ReflectionProbe candidate = probes[k];
            if (candidate == null)
            {
                probes[k] = probes[probes.Count - 1];
                probes.RemoveAt(probes.Count - 1);
                if (best == probes.Count) best = k;
                continue;
            }
            if (!candidate.isActiveAndEnabled) continue;
            float d = (candidate.transform.position - eye).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = k; }
        }
        if (best < 0) return false;

        ReflectionProbe probe = probes[best];
        probes[best] = probes[probes.Count - 1];
        probes.RemoveAt(probes.Count - 1);
        // Seules les lampes autour du reflet : les autres restent éteintes.
        Vector3 at = probe.transform.position;
        float range = ProbeLampRange * ProbeLampRange;
        foreach (var lamp in lamps)
            if (lamp != null && (lamp.transform.position - at).sqrMagnitude < range) lamp.SetLit(true);
        probe.RenderProbe();
        return true;
    }

    void CullLamps()
    {
        Camera cam = Camera.main;
        if (cam == null || lamps.Count == 0) return;
        Vector3 eye = cam.transform.position;
        float limit = LampRange * LampRange;
        for (int k = 0; k < LampsPerFrame && lamps.Count > 0; k++)
        {
            if (nextLamp >= lamps.Count) nextLamp = 0;
            FlickerLight lamp = lamps[nextLamp];
            if (lamp == null)
            {
                lamps[nextLamp] = lamps[lamps.Count - 1];
                lamps.RemoveAt(lamps.Count - 1);
                continue;
            }
            lamp.SetLit((lamp.transform.position - eye).sqrMagnitude < limit);
            nextLamp++;
        }
    }
}
