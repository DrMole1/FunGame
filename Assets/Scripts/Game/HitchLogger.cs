using UnityEngine;

// Diagnostic temporaire (V0.7.5) : signale dans la console chaque image anormalement longue, avec le ramasse-miettes (GC)
// s'il est passé pendant cette image, et ce qui était à l'écran (lobby ou partie). Seulement dans l'éditeur et les builds de
// développement. À retirer une fois la cause des saccades du lobby trouvée.
public class HitchLogger : MonoBehaviour
{
    const float Threshold = 0.12f;
    int lastGc;
    long lastMemory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Debug.isDebugBuild) return;
        var go = new GameObject("HitchLogger");
        DontDestroyOnLoad(go);
        go.AddComponent<HitchLogger>();
    }

    void Update()
    {
        int gc = System.GC.CollectionCount(0);
        long memory = System.GC.GetTotalMemory(false);
        float dt = Time.unscaledDeltaTime;
        if (dt > Threshold && Time.frameCount > 30)
        {
            bool lobby = NetworkGame.Instance == null || !NetworkGame.Instance.InGame;
            Debug.LogWarning("[Saccade] " + Mathf.RoundToInt(dt * 1000f) + " ms à t=" + Time.realtimeSinceStartup.ToString("F1")
                + " s | GC pendant l'image : " + (gc != lastGc ? "OUI" : "non") + " | mémoire gérée : " + (memory / (1024 * 1024)) + " Mo (avant : "
                + (lastMemory / (1024 * 1024)) + " Mo) | " + (lobby ? "lobby" : "partie") + " | " + Time.frameCount);
        }
        lastGc = gc;
        lastMemory = memory;
    }
}
