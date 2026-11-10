using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Chapitre 6, Note 01 (V0.6.9) : les textes des menus tremblent très légèrement, lettre par lettre. Le maillage n'est refait que
// quelques fois par seconde, et seulement pour les textes affichés.
public class TextShake : BaseMeshEffect
{
    const float Rate = 14f, Amplitude = 0.65f;
    static readonly List<UIVertex> stream = new List<UIVertex>();

    int shownStep = -1;

    public static int Step => Mathf.FloorToInt(Time.unscaledTime * Rate);

    // Décalage (composantes entre -1 et 1) d'un élément à un instant donné.
    public static Vector2 Jitter(int seed, int step) => new Vector2(Hash(seed, step, 1), Hash(seed, step, 2));

    static float Hash(int seed, int step, int axis)
    {
        uint h = unchecked((uint)(seed * 73856093) ^ (uint)(step * 19349663) ^ (uint)(axis * 83492791));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return (h & 0xFFFF) / 32767.5f - 1f;
    }

    // Tous les textes sous 'root', sauf ceux que le joueur a saisis.
    public static void AddTo(GameObject root)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            var field = text.GetComponentInParent<InputField>();
            if (field != null && field.textComponent == text) continue;
            if (text.GetComponent<TextShake>() == null) text.gameObject.AddComponent<TextShake>();
        }
    }

    void Update()
    {
        int step = Step;
        if (step == shownStep) return;
        shownStep = step;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        stream.Clear();
        mesh.GetUIVertexStream(stream);
        // Six sommets par lettre.
        for (int i = 0; i < stream.Count; i++)
        {
            UIVertex vertex = stream[i];
            vertex.position += (Vector3)(Jitter(i / 6, shownStep) * Amplitude);
            stream[i] = vertex;
        }
        mesh.Clear();
        mesh.AddUIVertexTriangleStream(stream);
    }
}
