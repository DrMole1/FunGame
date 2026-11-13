using UnityEngine;

// Protocole (GDD 7.10, objet O19, V0.6.13) : une vieille feuille de papier abîmée, scotchée sur une plaque de métal rouillée.
// Posé au sol du Hub de départ en début de partie ; un joueur peut le prendre (inventaire) et le reposer comme un objet
// unique. Sélectionné dans l'inventaire, le personnage le tient entre ses mains ; à la première personne, le joueur le voit devant
// lui, lisible. Le texte, écrit à la main (police Caveat, licence OFL), est traduit dans la langue du joueur (même posé au sol).
// Textures Meshy (Resources/Art/Protocole : feuille jaunie, tachée et pliée ; plaque de métal rouillée), détourées ; le texte est
// posé par le jeu sur la feuille, pour rester net et traduit.
public static class ProtocolBoard
{
    // Plaque : largeur x hauteur (m), le papier un peu plus petit.
    public const float Width = 0.4f, Height = 0.58f, Thickness = 0.012f;
    // Feuille (proportions de la texture Meshy, 760 x 1084).
    const float PaperW = 0.33f, PaperH = 0.47f;
    // Taille des caractères, réduite si la plus longue ligne (selon la langue) dépasse FitChars caractères.
    public const float TextSize = 0.0042f;
    const int FitChars = 30;

    // Police manuscrite (Resources/Fonts/Caveat) et son matériau : l'atlas de la police dynamique est reconstruit quand de
    // nouveaux caractères apparaissent (changement de langue), le matériau suit.
    static Font hand;
    static Material handMaterial;

    public static Font Hand => hand != null ? hand : hand = Resources.Load<Font>("Fonts/Caveat");

    static Material HandMaterial
    {
        get
        {
            if (handMaterial != null) return handMaterial;
            if (Hand == null) return GameArt.TextMaterial;
            var template = Resources.Load<Material>("Materials/Text3D");
            handMaterial = template != null ? new Material(template) { name = "TexteManuscrit" } : new Material(Hand.material);
            handMaterial.mainTexture = Hand.material.mainTexture;
            Font.textureRebuilt += f => { if (f == hand && handMaterial != null) handMaterial.mainTexture = hand.material.mainTexture; };
            return handMaterial;
        }
    }

    public static float SizeFor(string text)
    {
        int longest = 1;
        foreach (string line in text.Split('\n')) longest = Mathf.Max(longest, line.Length);
        return TextSize * Mathf.Min(1f, FitChars / (float)longest);
    }

    public static string Text() =>
        Loc.T("Protocole :") + "\n" +
        Loc.T("1) Tu peux rester assis.") + "\n" +
        Loc.T("2) Tu peux aboyer.") + "\n" +
        Loc.T("3) Tu peux aller à la niche.") + "\n\n" +
        Loc.T("Maintenant, survis.") + "\n\n" +
        "L.I.V.\nWOOF Corp.";

    static Material plate, paper;

    // Planche dressée : la face avec le texte regarde vers +Z (local), le bas à y = 0. 'scale' : 1 au sol ou en main.
    public static GameObject Build(Transform parent, float scale = 1f, bool castShadows = true)
    {
        if (plate == null)
        {
            // Matériaux préparés dans Resources (découpe des bords par la transparence) ; à défaut, des couleurs unies.
            plate = Resources.Load<Material>("Art/Protocole/PlaqueMat");
            if (plate == null) plate = GameArt.Lit("ProtocolePlaque", new Color(0.4f, 0.24f, 0.16f), null, 0.4f);
            paper = Resources.Load<Material>("Art/Protocole/PapierMat");
            if (paper == null) paper = GameArt.Lit("ProtocolePapier", new Color(0.88f, 0.8f, 0.62f), null, 0.05f);
        }
        var root = new GameObject("Protocole");
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * scale;
        var t = root.transform;
        // Plaque rouillée (texture sur ses deux faces).
        Prim(t, new Vector3(0f, Height * 0.5f, 0f), new Vector3(Width, Height, Thickness), plate);
        // Feuille de papier, légèrement de travers, à plat sur la plaque.
        var sheet = new GameObject("Feuille").transform;
        sheet.SetParent(t, false);
        sheet.localPosition = new Vector3(0.004f, Height * 0.5f, Thickness * 0.5f + 0.0015f);
        sheet.localRotation = Quaternion.Euler(0f, 0f, -1.6f);
        // Le quad se voit depuis son -Z : retourné vers l'avant de la plaque.
        var quad = GameArt.Prim(PrimitiveType.Quad, sheet, Vector3.zero, new Vector3(PaperW, PaperH, 1f), paper);
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var c in quad.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        float hx = PaperW * 0.5f, hy = PaperH * 0.5f;
        // Texte manuscrit à l'encre.
        // TextMesh se lit depuis -Z : retourné, il se lit depuis l'avant de la plaque (+Z), où le haut gauche du texte est en +X.
        var text = GameArt.CreateText(sheet, "Texte", new Vector3(hx - 0.035f, hy - 0.045f, 0.0015f), TextSize, new Color(0.08f, 0.06f, 0.07f),
            TextAnchor.UpperLeft, TextAlignment.Left);
        text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        if (Hand != null)
        {
            text.font = Hand;
            text.GetComponent<MeshRenderer>().sharedMaterial = HandMaterial;
        }
        text.color = new Color(0.02f, 0.02f, 0.025f);   // encre noire
        text.lineSpacing = 0.95f;
        text.gameObject.AddComponent<ProtocolText>().Init(text);
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (!castShadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    static GameObject Prim(Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, m);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        return go;
    }
}

// Texte du Protocole : retraduit au changement de langue.
public class ProtocolText : MonoBehaviour
{
    TextMesh text;

    public void Init(TextMesh mesh)
    {
        text = mesh;
        Refresh();
        Loc.Changed += Refresh;
    }

    void OnDestroy() => Loc.Changed -= Refresh;

    void Refresh()
    {
        if (text == null) return;
        text.text = ProtocolBoard.Text();
        text.characterSize = ProtocolBoard.SizeFor(text.text);
    }
}

// Vue du Protocole tenu par le joueur local, devant la caméra, en bas au centre de l'écran : seulement à la première personne
// (à la troisième, on ne voit que le personnage qui le tient).
public class ProtocolView : MonoBehaviour
{
    GameObject board;
    float shown;

    public static ProtocolView Create(Camera cam)
    {
        var go = new GameObject("VueProtocole");
        go.transform.SetParent(cam.transform, false);
        var view = go.AddComponent<ProtocolView>();
        view.board = ProtocolBoard.Build(go.transform, 1f, false);
        // Le texte regarde vers +Z : on le tourne vers la caméra.
        view.board.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var r in view.board.GetComponentsInChildren<Renderer>()) r.gameObject.layer = 2;
        go.SetActive(false);
        return view;
    }

    public void Show(bool on)
    {
        if (on && !gameObject.activeSelf)
        {
            shown = 0f;
            gameObject.SetActive(true);
        }
        else if (!on && gameObject.activeSelf) gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        shown = Mathf.Min(1f, shown + Time.deltaTime * 4f);
        float k = Mathf.SmoothStep(0f, 1f, shown);
        // Monte depuis le bas de l'écran ; léger balancement des mains.
        float sway = Mathf.Sin(Time.time * 1.3f) * 0.004f;
        transform.localPosition = new Vector3(0f, -0.36f, 0.62f) + Vector3.down * (1f - k) * 0.35f + new Vector3(sway, sway * 0.5f, 0f);
        transform.localRotation = Quaternion.Euler(8f, 0f, -1.5f);
    }
}
