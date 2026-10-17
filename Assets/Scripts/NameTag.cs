using UnityEngine;

// Pseudo affiché au-dessus d'un joueur, toujours tourné vers la caméra.
public class NameTag : MonoBehaviour
{
    public float height = 1.05f;

    Transform tagRoot;
    TextMesh label, shadow;

    public void Set(string playerName, Color color)
    {
        if (tagRoot == null) Build();
        label.text = playerName;
        shadow.text = playerName;
        label.color = Color.Lerp(color, Color.white, 0.55f);
    }

    void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        tagRoot = new GameObject("NameTag").transform;
        tagRoot.SetParent(transform, false);
        tagRoot.localPosition = Vector3.up * height;

        shadow = CreateText("Shadow", font, new Color(0f, 0f, 0f, 0.75f), 0);
        shadow.transform.localPosition = new Vector3(0.025f, -0.025f, 0.01f);
        label = CreateText("Label", font, Color.white, 1);
    }

    TextMesh CreateText(string objName, Font font, Color color, int order)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(tagRoot, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 64;
        tm.characterSize = 0.04f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        tm.color = color;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = font.material;
        mr.sortingOrder = order;
        return tm;
    }

    void LateUpdate()
    {
        if (tagRoot == null) return;
        Camera cam = Camera.main;
        tagRoot.position = transform.position + Vector3.up * height;
        if (cam != null) tagRoot.rotation = cam.transform.rotation;
    }
}
