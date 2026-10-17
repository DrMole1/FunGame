using UnityEngine;

// Pseudo affiché au-dessus d'un joueur, toujours tourné vers la caméra et masqué par les murs.
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

    public void Show(bool visible)
    {
        if (tagRoot != null) tagRoot.gameObject.SetActive(visible);
    }

    void Build()
    {
        tagRoot = new GameObject("NameTag").transform;
        tagRoot.SetParent(transform, false);
        tagRoot.localPosition = Vector3.up * height;

        shadow = CreateText("Shadow", new Color(0f, 0f, 0f, 0.75f));
        shadow.transform.localPosition = new Vector3(0.025f, -0.025f, 0.01f);
        label = CreateText("Label", Color.white);
    }

    TextMesh CreateText(string objName, Color color)
    {
        var tm = GameArt.CreateText(tagRoot, objName, Vector3.zero, 0.04f, color, TextAnchor.LowerCenter);
        tm.fontStyle = FontStyle.Bold;
        tm.richText = false;
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
