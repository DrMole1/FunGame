using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Étale sur plusieurs images ce qui figerait le jeu d'un coup à la création d'une zone (décor purement visuel, captures de
// reflets), et éteint les lampes trop loin de la caméra pour éclairer quoi que ce soit de visible.
public class WorldStreamer : MonoBehaviour
{
    // Temps consacré à la construction à chaque image, et captures de reflets par image (chacune rend 6 vues de la zone).
    const long BudgetMs = 3;
    const int ProbesPerFrame = 1;

    // La caméra voit à 45 m et une lampe porte à moins de 10 m : au-delà, elle n'éclaire rien de visible.
    const float LampRange = 55f;
    const int LampsPerFrame = 64;

    static WorldStreamer instance;
    readonly Queue<Action> work = new Queue<Action>();
    readonly Queue<ReflectionProbe> probes = new Queue<ReflectionProbe>();
    readonly List<FlickerLight> lamps = new List<FlickerLight>();
    readonly Stopwatch watch = new Stopwatch();
    int nextLamp;
    bool allLampsLit;

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

    // Hors du jeu (outils de l'éditeur), le travail est fait tout de suite.
    public static void Enqueue(Action action)
    {
        if (Application.isPlaying) Instance.work.Enqueue(action);
        else action();
    }

    public static void EnqueueProbe(ReflectionProbe probe)
    {
        if (Application.isPlaying) Instance.probes.Enqueue(probe);
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
        // Les reflets montrent le décor et les lampes : ils attendent que tout soit en place, lampes lointaines rallumées.
        if (work.Count == 0 && probes.Count > 0) RenderProbes();
        else if (probes.Count == 0) allLampsLit = false;
        if (probes.Count == 0) CullLamps();
    }

    void RenderProbes()
    {
        if (!allLampsLit)
        {
            allLampsLit = true;
            foreach (var lamp in lamps) if (lamp != null) lamp.SetLit(true);
        }
        for (int done = 0; done < ProbesPerFrame && probes.Count > 0; )
        {
            ReflectionProbe probe = probes.Dequeue();
            if (probe == null) continue;
            probe.RenderProbe();
            done++;
        }
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
