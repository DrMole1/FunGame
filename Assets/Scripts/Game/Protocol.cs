using UnityEngine;

// Protocole (GDD 7.10, objet O12 en V0.7.2, V0.6.13) : une vieille feuille de papier abîmée, scotchée sur une plaque de métal rouillée.
// Posé au sol du Hub de départ en début de partie ; un joueur peut le prendre (inventaire) et le reposer comme un objet
// unique. Sélectionné dans l'inventaire, le personnage le tient entre ses mains ; à la première personne, le joueur le voit devant
// lui, lisible. Le texte, écrit à la main (police Caveat, licence OFL), est traduit dans la langue du joueur (même posé au sol).
// Textures Meshy (Resources/Art/Protocole : feuille jaunie, tachée et pliée ; plaque de métal rouillée), détourées ; le texte est
// posé par le jeu sur la feuille, pour rester net et traduit. V0.7.2 : la feuille est noircie et le texte écrit en blanc
// (lisible en blanc sur fond noir). V0.7.9 : chaque règle finit, sur la même ligne, par l'image de son glyphe, sans arrière-plan,
// à l'encre blanche comme le texte (rester assis : glyphe 1 ; aboyer : glyphe 3 ; aller à la niche : glyphe 2).
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

    // Lignes 1 à 3 : la règle, puis l'image de son glyphe (type du glyphe : 0 = glyphe 1, 2 = glyphe 3, 1 = glyphe 2).
    public static readonly int[] RuleGlyphs = { 0, 2, 1 };
    // Place laissée à l'image du glyphe, en caractères.
    const int GlyphChars = 3;

    public static float SizeFor(string text)
    {
        int longest = 1;
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            longest = Mathf.Max(longest, lines[i].Length + (i >= 1 && i <= RuleGlyphs.Length ? GlyphChars : 0));
        return TextSize * Mathf.Min(1f, FitChars / (float)longest);
    }

    public static string Text() =>
        Loc.T("Protocole :") + "\n" +
        Loc.T("1) Tu peux rester assis :") + "\n" +
        Loc.T("2) Tu peux aboyer :") + "\n" +
        Loc.T("3) Tu peux aller à la niche :") + "\n\n" +
        Loc.T("Maintenant, survis.") + "\n\n" +
        // V0.7.3 (Note 01) : plus de "L.I.V." au-dessus de la signature.
        "WOOF Corp.";

    static Material plate, paper;
    public static readonly Color Ink = new Color(0.95f, 0.95f, 0.93f);
    static Material[] glyphInk;

    // Image d'un glyphe (motif seul, sans arrière-plan), à l'encre du texte.
    public static Material GlyphInk(int type)
    {
        if (glyphInk == null) glyphInk = new Material[GameRules.GlyphTypes];
        if (glyphInk[type] == null)
            glyphInk[type] = new Material(Shader.Find("Sprites/Default")) { name = "GlypheProtocole" + (type + 1), color = Ink, mainTexture = GlyphArt.Load("Glyphe" + (type + 1)) };
        return glyphInk[type];
    }

    // Planche dressée : la face avec le texte regarde vers +Z (local), le bas à y = 0. 'scale' : 1 au sol ou en main.
    public static GameObject Build(Transform parent, float scale = 1f, bool castShadows = true)
    {
        if (plate == null)
        {
            // Matériaux préparés dans Resources (découpe des bords par la transparence) ; à défaut, des couleurs unies.
            plate = Resources.Load<Material>("Art/Protocole/PlaqueMat");
            if (plate == null) plate = GameArt.Lit("ProtocolePlaque", new Color(0.4f, 0.24f, 0.16f), null, 0.4f);
            var sheetMat = Resources.Load<Material>("Art/Protocole/PapierMat");
            // Papier noirci : même feuille abîmée (pliures, taches, bords découpés), presque noire.
            paper = sheetMat != null ? new Material(sheetMat) { name = "PapierNoirci", color = new Color(0.07f, 0.065f, 0.065f) }
                                     : GameArt.Lit("ProtocolePapier", new Color(0.05f, 0.05f, 0.05f), null, 0.05f);
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
        // Texte manuscrit, à l'encre blanche.
        // TextMesh se lit depuis -Z : retourné, il se lit depuis l'avant de la plaque (+Z), où le haut gauche du texte est en +X.
        var text = GameArt.CreateText(sheet, "Texte", new Vector3(hx - 0.035f, hy - 0.045f, 0.0015f), TextSize, new Color(0.08f, 0.06f, 0.07f),
            TextAnchor.UpperLeft, TextAlignment.Left);
        text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        if (Hand != null)
        {
            text.font = Hand;
            text.GetComponent<MeshRenderer>().sharedMaterial = HandMaterial;
        }
        text.color = Ink;   // encre blanche
        text.lineSpacing = 0.95f;
        text.gameObject.AddComponent<ProtocolText>().Init(text, sheet);
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

// Texte du Protocole : retraduit au changement de langue ; les images des glyphes suivent la fin de leur ligne.
public class ProtocolText : MonoBehaviour
{
    TextMesh text;
    Transform sheet;
    readonly Transform[] glyphs = new Transform[ProtocolBoard.RuleGlyphs.Length];

    public void Init(TextMesh mesh, Transform paper)
    {
        text = mesh;
        sheet = paper;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var quad = GameArt.Prim(PrimitiveType.Quad, sheet, Vector3.zero, Vector3.one, ProtocolBoard.GlyphInk(ProtocolBoard.RuleGlyphs[i]));
            quad.name = "Glyphe" + (ProtocolBoard.RuleGlyphs[i] + 1);
            // Comme le texte : le quad se voit depuis son -Z, retourné vers l'avant de la plaque.
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            foreach (var c in quad.GetComponentsInChildren<Collider>()) Destroy(c);
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad.layer = mesh.gameObject.layer;
            glyphs[i] = quad.transform;
        }
        Refresh();
        Loc.Changed += Refresh;
    }

    void OnDestroy() => Loc.Changed -= Refresh;

    void Refresh()
    {
        if (text == null) return;
        text.text = ProtocolBoard.Text();
        text.characterSize = ProtocolBoard.SizeFor(text.text);
        PlaceGlyphs();
    }

    // Une TextMesh mesure, en unités locales, son avance en pixels (à sa taille de police) x characterSize x 0,1. Le texte est
    // retourné : il s'écrit vers le -X de la feuille.
    void PlaceGlyphs()
    {
        Font font = text.font;
        if (font == null) return;
        int size = text.fontSize > 0 ? text.fontSize : font.fontSize;
        float unit = text.characterSize * 0.1f;
        float lineStep = font.lineHeight * (font.fontSize > 0 ? size / (float)font.fontSize : 1f) * unit * text.lineSpacing;
        string[] lines = text.text.Split('\n');
        font.RequestCharactersInTexture(text.text, size, text.fontStyle);
        Vector3 origin = text.transform.localPosition;
        float glyph = lineStep * 1.25f;
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i] == null) continue;
            string line = i + 1 < lines.Length ? lines[i + 1] : "";
            int advance = 0;
            foreach (char c in line)
                if (font.GetCharacterInfo(c, out CharacterInfo info, size, text.fontStyle)) advance += info.advance;
            float x = origin.x - advance * unit - lineStep * 0.3f - glyph * 0.5f;
            float y = origin.y - (i + 1.5f) * lineStep;
            glyphs[i].localPosition = new Vector3(x, y, origin.z);
            glyphs[i].localScale = new Vector3(glyph, glyph, 1f);
        }
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
