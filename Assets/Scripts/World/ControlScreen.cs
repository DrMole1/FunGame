using System.Collections.Generic;
using UnityEngine;

// V0.7.5 (chapitre 15.3) : le grand écran de la Control Room, sous l'enseigne (même style que les écrans des Hubs : police,
// vieux moniteur). Un câble le relie à un promontoire cylindrique de 0,5 m (au sud-est de la salle) marqué d'un dessin de
// cloche ; devant l'écran, un tableau de bord : 2 boutons de flèches et 3 boutons de glyphes, éteints et inactifs.
// Une Cloche Décorative posée sur le promontoire allume l'écran (le câble devient blanc lumineux) ; l'enlever l'éteint.
// Allumé : "W.O.O.F. Corp." en haut à gauche, "PASSWORD..." et 6 caractères attendus au centre ; les boutons de glyphes
// s'allument. Mot de passe juste : les caméras de sécurité (6 yeux de la zone affichée, de la Zone 1 à la Zone 4) et les flèches.
// L'état (caractères saisis, déverrouillage, zone) est tenu par l'hôte (GameSession.ControlScreen).
public class ControlScreen : MonoBehaviour
{
    public static ControlScreen Current { get; private set; }

    public const int PasswordLength = 6, FeedCount = 6, MaxZone = 4;
    static readonly Vector2 ScreenSize = new Vector2(5f, 2.7f);
    const float ScreenY = 2.05f, PedestalHeight = 0.5f;
    float pedestalRadius = 0.35f;
    // V0.7.6 (Note 03) : icône de cloche (BellIcon) sur le promontoire, d'un blanc légèrement lumineux, nettement plus
    // lumineux quand une Cloche Décorative y est posée.
    Material bellIcon;
    Light bellGlow;
    static readonly Color IconDim = new Color(0.62f, 0.62f, 0.6f, 1f), IconLit = Color.white;
    const float GlowDim = 0.25f, GlowLit = 2.2f;
    const int FeedWidth = 256, FeedHeight = 144;
    const float FeedFar = 35f;

    TextMesh center, brand, zoneLabel;
    readonly TextMesh[] noSignal = new TextMesh[FeedCount];
    readonly Renderer[] feedQuads = new Renderer[FeedCount];
    readonly Camera[] feedCams = new Camera[FeedCount];
    readonly RenderTexture[] feedTextures = new RenderTexture[FeedCount];
    readonly bool[] feedLive = new bool[FeedCount];
    readonly List<Renderer> cable = new List<Renderer>();
    readonly List<ControlButton> glyphButtons = new List<ControlButton>(), arrowButtons = new List<ControlButton>();
    Transform screen, pedestal;
    int nextFeed;
    bool built, applied, powered, unlocked, feedsShown;
    int count, zone = 1;

    public bool Powered => powered;
    public bool Unlocked => unlocked;
    public Vector3 ScreenPosition => screen != null ? screen.position : transform.position;
    // Dessus du promontoire (là où se pose la Cloche Décorative).
    public Vector3 PedestalTop => pedestal.position + Vector3.up * PedestalHeight;
    public float PedestalReach => pedestalRadius + 0.15f;

    // 'south' : mur du fond (z), 'halfWidth' : demi-largeur de la salle ; tout est dans le repère de la Control Room.
    public static ControlScreen Build(Transform room, float south, float halfWidth)
    {
        var go = new GameObject("EcranControlRoom");
        go.transform.SetParent(room, false);
        var cs = go.AddComponent<ControlScreen>();
        cs.Construct(south, halfWidth);
        return cs;
    }

    void Construct(float south, float hw)
    {
        // Écran : son +Z pointe vers le mur du fond (sud) ; on le lit depuis le nord. Sa droite (+X local) est à l'ouest.
        Vector3 screenPos = new Vector3(0f, ScreenY, south + 0.12f);
        center = WorldBuilder.ScreenAt(transform, "Ecran", screenPos, Quaternion.Euler(0f, 180f, 0f), ScreenSize, 0.05f);
        screen = center.transform.parent;
        center.fontStyle = FontStyle.Bold;
        var crt = screen.GetComponent<CrtScreen>();
        brand = GameArt.CreateText(screen, "Marque", new Vector3(-ScreenSize.x * 0.5f + 0.25f, ScreenSize.y * 0.5f - 0.25f, -0.01f), 0.026f, Color.white,
            TextAnchor.UpperLeft, TextAlignment.Left);
        brand.text = "W.O.O.F. Corp.";
        brand.fontStyle = FontStyle.Bold;
        zoneLabel = GameArt.CreateText(screen, "Zone", new Vector3(-ScreenSize.x * 0.5f + 0.25f, ScreenSize.y * 0.5f - 0.2f, -0.01f), 0.03f, Color.white,
            TextAnchor.UpperLeft, TextAlignment.Left);
        zoneLabel.fontStyle = FontStyle.Bold;
        if (crt != null) { crt.AddText(brand); crt.AddText(zoneLabel); }

        // 6 vues de caméras (2 rangées de 3), sous le calque du vieux moniteur.
        Vector2 feedSize = new Vector2(1.35f, 0.76f);
        for (int i = 0; i < FeedCount; i++)
        {
            float x = (i % 3 - 1) * 1.6f, y = i < 3 ? 0.36f : -0.52f;
            feedTextures[i] = new RenderTexture(FeedWidth, FeedHeight, 16) { name = "CameraSecurite" + (i + 1) };
            var mat = new Material(Shader.Find("Sprites/Default")) { name = "VueCamera" + (i + 1), mainTexture = feedTextures[i], color = Color.white };
            var quad = GameArt.Prim(PrimitiveType.Quad, screen, new Vector3(x, y, -0.012f), new Vector3(feedSize.x, feedSize.y, 1f), mat);
            quad.name = "Vue" + (i + 1);
            feedQuads[i] = quad.GetComponent<Renderer>();
            feedQuads[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            noSignal[i] = GameArt.CreateText(screen, "PasDeSignal" + (i + 1), new Vector3(x, y, -0.013f), 0.018f, new Color(0.7f, 0.7f, 0.7f));
            noSignal[i].text = "NO SIGNAL";

            var camGo = new GameObject("CameraSecurite" + (i + 1));
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = feedTextures[i];
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = FeedFar;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            feedCams[i] = cam;
        }

        // Promontoire au sud-est (l'est est +X) : V0.7.6 (Note 04), socle rond en métal (Meshy, Resources/Art/Decor/Promontoire),
        // à défaut un simple cylindre. L'icône de cloche est sur le dessus.
        pedestal = new GameObject("PiedPromontoire").transform;
        pedestal.SetParent(transform, false);
        pedestal.localPosition = new Vector3(hw - 2.5f, 0f, south + 2.5f);
        var model = GameArt.SpawnModel("Decor/Promontoire", pedestal, PedestalHeight);
        if (model != null)
        {
            Bounds mb = GameArt.LocalBounds(model.transform);
            pedestalRadius = Mathf.Clamp(Mathf.Min(mb.size.x, mb.size.z) * 0.5f, 0.3f, 0.7f);
        }
        Material metal = GameArt.Lit("PromontoireControl", new Color(0.09f, 0.09f, 0.1f), null, 0.5f);
        var ped = GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(hw - 2.5f, PedestalHeight * 0.5f, south + 2.5f),
            new Vector3(pedestalRadius * 2f, PedestalHeight * 0.5f, pedestalRadius * 2f), metal);
        ped.name = "Promontoire";
        // Dessus parfaitement plat : la Cloche s'y pose (la capsule d'un cylindre Unity serait bombée). Avec le modèle Meshy,
        // le cylindre ne sert plus qu'aux collisions.
        var pc = ped.AddComponent<MeshCollider>();
        pc.sharedMesh = ped.GetComponent<MeshFilter>().sharedMesh;
        pc.convex = true;
        if (model != null) ped.GetComponent<Renderer>().enabled = false;
        var icon = Resources.Load<Texture2D>("Icons/BellIcon");
        bellIcon = new Material(Shader.Find("Sprites/Default")) { name = "IconeCloche", mainTexture = icon, color = icon != null ? IconDim : Color.clear };
        var drawing = GameArt.Prim(PrimitiveType.Quad, pedestal, new Vector3(0f, PedestalHeight + 0.004f, 0f), new Vector3(pedestalRadius * 1.14f, pedestalRadius * 1.3f, 1f), bellIcon);   // BellIcon : 350 x 399
        drawing.name = "IconeCloche";
        // À l'endroit pour qui arrive du tunnel (au nord).
        drawing.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
        drawing.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bellGlow = new GameObject("LueurCloche").AddComponent<Light>();
        bellGlow.transform.SetParent(pedestal, false);
        bellGlow.transform.localPosition = new Vector3(0f, PedestalHeight + 0.35f, 0f);
        bellGlow.type = LightType.Point;
        bellGlow.color = new Color(0.95f, 0.97f, 1f);
        bellGlow.range = 2.5f;
        bellGlow.intensity = GlowDim;
        bellGlow.shadows = LightShadows.None;

        // Câble : du bas de l'écran (côté est), le long du mur puis au sol jusqu'au promontoire.
        float cableX = ScreenSize.x * 0.5f - 0.3f, wallZ = south + 0.05f, pz = south + 2.5f, px = hw - 2.5f;
        CableSegment(new Vector3(cableX, 0f, wallZ), new Vector3(cableX, ScreenY - ScreenSize.y * 0.5f, wallZ));
        CableSegment(new Vector3(cableX, 0.02f, wallZ), new Vector3(px, 0.02f, wallZ));
        CableSegment(new Vector3(px, 0.02f, wallZ), new Vector3(px, 0.02f, pz - pedestalRadius));

        // Tableau de bord devant l'écran : pupitre incliné, flèches aux extrémités, glyphes au milieu.
        var desk = new GameObject("TableauDeBord").transform;
        desk.SetParent(transform, false);
        desk.localPosition = new Vector3(0f, 0f, south + 1.7f);
        Material deskMat = GameArt.Lit("PupitreControl", new Color(0.06f, 0.06f, 0.065f), null, 0.4f);
        var body = GameArt.Prim(PrimitiveType.Cube, desk, new Vector3(0f, 0.45f, 0f), new Vector3(3.4f, 0.9f, 0.6f), deskMat, true);
        body.name = "Pupitre";
        var top = new GameObject("Dessus").transform;
        top.SetParent(desk, false);
        top.localPosition = new Vector3(0f, 0.93f, 0f);
        // V0.7.6 (Note 01) : incliné vers le tunnel (au nord, +Z), où se tient le joueur, et non vers l'écran.
        top.localRotation = Quaternion.Euler(18f, 0f, 0f);
        var plate = GameArt.Prim(PrimitiveType.Cube, top, Vector3.zero, new Vector3(3.5f, 0.06f, 0.75f), deskMat, true);
        plate.name = "Plateau";
        // V0.7.7 (Note 05) : pupitre Meshy en métal rouillé (Resources/Art/Decor/TableauDeBord), aux dimensions du corps actuel
        // (3,4 x 0,9 x 0,6 m), sa façade tournée vers le tunnel. Le modèle a un dessus plat : le plateau incliné vers le tunnel
        // (V0.7.6) est gardé, en tôle rouillée assortie, et les boutons restent aux mêmes positions.
        var deskModel = GameArt.SpawnModel("Decor/TableauDeBord", desk, 0.9f);
        if (deskModel != null)
        {
            var inner = deskModel.transform.GetChild(0);
            Bounds mb = GameArt.LocalBounds(deskModel.transform);
            inner.localScale = Vector3.Scale(inner.localScale, new Vector3(3.4f / Mathf.Max(0.01f, mb.size.x), 1f, 0.6f / Mathf.Max(0.01f, mb.size.z)));
            mb = GameArt.LocalBounds(deskModel.transform);
            inner.localPosition += new Vector3(-mb.center.x, 0f, -mb.center.z);
            foreach (var r in deskModel.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.GetComponent<Renderer>().enabled = false;
            Material rusty = GameArt.Lit("PupitreRouille", new Color(0.2f, 0.21f, 0.21f), GameArt.Noise("rust", 64, 0.55f, 0.4f, 29), 0.35f);
            rusty.SetFloat("_Metallic", 0.6f);
            plate.GetComponent<Renderer>().sharedMaterial = rusty;
        }
        // Vu depuis le nord, la gauche du joueur est à l'est (+X) : flèche gauche à +X, flèche droite à -X.
        arrowButtons.Add(ControlButton.Create(top, ControlButton.Left, new Vector3(1.3f, 0.05f, 0f)));
        for (int g = 0; g < 3; g++)
            glyphButtons.Add(ControlButton.Create(top, g, new Vector3((1 - g) * 0.5f, 0.05f, 0f)));
        arrowButtons.Add(ControlButton.Create(top, ControlButton.Right, new Vector3(-1.3f, 0.05f, 0f)));

        built = true;
        Current = this;
        Apply(false, 0, false, 1);
    }

    void CableSegment(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        if (d.magnitude < 0.01f) return;
        var seg = GameArt.Prim(PrimitiveType.Cube, transform, (a + b) * 0.5f, Vector3.one, Glyph.CableMaterial(false));
        seg.name = "Cable";
        seg.transform.localRotation = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
        seg.transform.localScale = new Vector3(0.04f, 0.04f, d.magnitude + 0.04f);
        var r = seg.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cable.Add(r);
    }

    // État affiché : allumé (Cloche sur le promontoire), caractères saisis, mot de passe juste, zone des caméras.
    public void Apply(bool on, int entered, bool open, int shownZone)
    {
        if (!built) return;
        bool changed = on != powered || entered != count || open != unlocked || shownZone != zone;
        powered = on;
        count = Mathf.Clamp(entered, 0, PasswordLength);
        unlocked = open && on;
        zone = Mathf.Clamp(shownZone, 1, MaxZone);
        if (!changed && applied && feedsShown == (powered && unlocked)) return;
        applied = true;

        foreach (var r in cable) if (r != null) r.sharedMaterial = Glyph.CableMaterial(powered);
        if (bellIcon.mainTexture != null) bellIcon.color = powered ? IconLit : IconDim;
        bellGlow.intensity = powered ? GlowLit : GlowDim;
        foreach (var b in glyphButtons) b.SetLit(powered);
        foreach (var b in arrowButtons) b.SetLit(powered && unlocked);

        brand.gameObject.SetActive(powered && !unlocked);
        zoneLabel.gameObject.SetActive(powered && unlocked);
        zoneLabel.text = "Zone " + zone;
        if (!powered || unlocked) center.text = "";
        else
        {
            var field = new System.Text.StringBuilder();
            for (int i = 0; i < PasswordLength; i++)
            {
                if (i > 0) field.Append(' ');
                field.Append(i < count ? '*' : '_');
            }
            center.text = "PASSWORD...\n" + field;
        }
        feedsShown = powered && unlocked;
        for (int i = 0; i < FeedCount; i++)
        {
            feedQuads[i].gameObject.SetActive(feedsShown && feedLive[i]);
            noSignal[i].gameObject.SetActive(feedsShown && !feedLive[i]);
        }
    }

    // Caméras de la zone affichée : position et regard de chaque œil choisi (null : pas de signal).
    public void SetFeeds(IList<Transform> eyes)
    {
        for (int i = 0; i < FeedCount; i++)
        {
            Transform eye = eyes != null && i < eyes.Count ? eyes[i] : null;
            feedLive[i] = eye != null;
            if (eye == null) continue;
            Vector3 fwd = eye.forward;
            feedCams[i].transform.SetPositionAndRotation(eye.position + fwd * (0.45f * eye.lossyScale.x),
                Quaternion.LookRotation(fwd, Mathf.Abs(fwd.y) > 0.9f ? Vector3.forward : Vector3.up));
        }
        for (int i = 0; i < FeedCount; i++)
        {
            feedQuads[i].gameObject.SetActive(feedsShown && feedLive[i]);
            noSignal[i].gameObject.SetActive(feedsShown && !feedLive[i]);
        }
    }

    public Vector3 FeedPosition(int i) => feedCams[i].transform.position;
    public bool FeedLive(int i) => feedsShown && feedLive[i];

    // Une caméra rendue par image, seulement si l'écran est regardé de près (tour à tour : chaque vue à 1/6 de la cadence).
    public void RenderFeeds()
    {
        if (!feedsShown) return;
        for (int k = 0; k < FeedCount; k++)
        {
            int i = (nextFeed + k) % FeedCount;
            if (!feedLive[i]) continue;
            nextFeed = (i + 1) % FeedCount;
            feedCams[i].Render();
            return;
        }
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        foreach (var rt in feedTextures) if (rt != null) rt.Release();
    }
}

// Bouton du tableau de bord de la Control Room : glyphe 1 à 3 (0 à 2) ou flèche (gauche, droite).
public class ControlButton : Interactable
{
    public const int Left = 3, Right = 4;
    public int Kind { get; private set; }
    bool lit;
    Renderer cap, mark;
    Material capOff, capOn, markOff, markOn;

    public static ControlButton Create(Transform parent, int kind, Vector3 localPos)
    {
        var go = new GameObject(kind < 3 ? "BoutonGlyphe" + (kind + 1) : kind == Left ? "FlecheGauche" : "FlecheDroite");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var b = go.AddComponent<ControlButton>();
        b.Kind = kind;
        b.capOff = GameArt.Lit("BoutonControlEteint", new Color(0.12f, 0.12f, 0.13f), null, 0.5f);
        b.capOn = GameArt.Emissive("BoutonControlAllume", new Color(0.85f, 0.85f, 0.8f), new Color(0.55f, 0.55f, 0.5f));
        var capGo = GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.02f, 0.3f), b.capOff);
        capGo.name = "Bouton";
        b.cap = capGo.GetComponent<Renderer>();
        // Motif : le glyphe (texture du jeu) ou une flèche, à peine visible éteint, lumineux allumé.
        Texture tex = kind < 3 ? GlyphArt.Load("Glyphe" + (kind + 1)) : null;
        b.markOff = new Material(Shader.Find("Sprites/Default")) { name = "MotifEteint", mainTexture = tex, color = new Color(0.3f, 0.1f, 0.1f) };
        b.markOn = new Material(Shader.Find("Sprites/Default")) { name = "MotifAllume", mainTexture = tex, color = GlyphArt.Red };
        if (kind < 3)
        {
            var quad = GameArt.Prim(PrimitiveType.Quad, go.transform, new Vector3(0f, 0.042f, 0f), Vector3.one * 0.24f, b.markOff);
            quad.name = "Motif";
            quad.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            b.mark = quad.GetComponent<Renderer>();
        }
        else
        {
            var arrow = GameArt.MeshObject("Fleche", go.transform, ArrowMesh(kind == Left ? 1f : -1f), b.markOff);
            arrow.transform.localPosition = new Vector3(0f, 0.042f, 0f);
            b.mark = arrow.GetComponent<Renderer>();
        }
        b.FitTrigger(new Vector3(0.32f, 0.12f, 0.32f));
        return b;
    }

    // Triangle à plat pointant vers 'dir' (+X ou -X, dans le repère du bouton).
    static Mesh ArrowMesh(float dir)
    {
        var m = new Mesh { name = "Fleche" };
        m.vertices = new[] { new Vector3(0.09f * dir, 0f, 0f), new Vector3(-0.07f * dir, 0f, 0.09f), new Vector3(-0.07f * dir, 0f, -0.09f) };
        m.uv = new[] { new Vector2(1f, 0.5f), new Vector2(0f, 1f), new Vector2(0f, 0f) };
        m.triangles = dir > 0f ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public void SetLit(bool on)
    {
        if (on == lit && cap.sharedMaterial != null) return;
        lit = on;
        cap.sharedMaterial = on ? capOn : capOff;
        mark.sharedMaterial = on ? markOn : markOff;
        RefreshOutline();
    }

    public override bool CanInteract
    {
        get
        {
            var cs = ControlScreen.Current;
            if (cs == null || !cs.Powered) return false;
            return Kind < 3 ? !cs.Unlocked : cs.Unlocked;
        }
    }

    public override string Prompt => Kind < 3 ? Loc.T("Appuyer sur le bouton") : Kind == Left ? Loc.T("Zone précédente") : Loc.T("Zone suivante");
    public override void Interact() => GameSession.Instance.PressControlButton(Kind);
}

// V0.7.5 (15.3) : pompon de foire rouge suspendu à 1 m sous le sommet du chapiteau du Hub de départ, par une ficelle blanche.
// Aucun texte à son survol ; on ne l'atteint qu'en sautant sur un Trampoline tout près. Cliqué, il donne un à-coup vers le sol
// et revient ; les joueurs dans le chapiteau entendent les 6 sons du mot de passe de la Control Room (GameSession).
public class Pompon : Interactable
{
    public static Pompon Current { get; private set; }
    const float StringLength = 1f, Size = 0.6f, JerkDepth = 0.35f, JerkDown = 0.08f, JerkBack = 0.6f;
    Transform hang;
    float jerkAt = -10f;

    public static Pompon Create(Transform hub, float apex)
    {
        var go = new GameObject("Pompon");
        go.transform.SetParent(hub, false);
        go.transform.localPosition = new Vector3(0f, apex, 0f);
        var p = go.AddComponent<Pompon>();
        p.hang = new GameObject("Suspendu").transform;
        p.hang.SetParent(go.transform, false);
        var ficelle = GameArt.Prim(PrimitiveType.Cylinder, p.hang, Vector3.down * StringLength * 0.5f, new Vector3(0.012f, StringLength * 0.5f, 0.012f),
            GameArt.Lit("FicellePompon", new Color(0.95f, 0.95f, 0.93f), null, 0.1f));
        ficelle.name = "Ficelle";
        var model = GameArt.SpawnModel("Decor/Pompon", p.hang, Size);
        if (model != null)
        {
            // Le modèle est posé par sa base : sous la ficelle, sa poignée en haut.
            model.transform.localPosition = Vector3.down * (StringLength + Size);
        }
        else BuildFallback(p.hang, Vector3.down * (StringLength + Size * 0.5f));
        Current = p;
        // Sélection : une boîte un peu plus large que le pompon (et sa ficelle), traversable.
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = Vector3.down * (StringLength + Size * 0.5f);
        col.size = Vector3.one * (Size + 0.3f);
        return p;
    }

    // Sans modèle Meshy : une boule de franges rouges.
    static void BuildFallback(Transform parent, Vector3 center)
    {
        Material red = GameArt.Lit("PomponRouge", new Color(0.85f, 0.05f, 0.07f), null, 0.55f);
        var core = GameArt.Prim(PrimitiveType.Sphere, parent, center, Vector3.one * Size * 0.55f, red);
        core.name = "Coeur";
        var rnd = new System.Random(17);
        for (int i = 0; i < 70; i++)
        {
            Vector3 dir = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.down;
            dir.Normalize();
            float len = Size * (0.35f + 0.15f * (float)rnd.NextDouble());
            var strand = GameArt.Prim(PrimitiveType.Cube, parent, center + dir * len * 0.5f, new Vector3(0.025f, 0.006f, len), red);
            strand.name = "Frange";
            strand.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up);
        }
    }

    public void Jerk() => jerkAt = Time.time;

    void Update()
    {
        float t = Time.time - jerkAt;
        float y;
        if (t < JerkDown) y = -JerkDepth * (t / JerkDown);
        else if (t < JerkDown + JerkBack)
        {
            // Retour amorti, avec un petit rebond.
            float k = (t - JerkDown) / JerkBack;
            y = -JerkDepth * Mathf.Cos(k * Mathf.PI * 1.5f) * (1f - k);
        }
        else y = 0f;
        hang.localPosition = new Vector3(0f, y, 0f);
    }

    public override string Prompt => "";
    public override void Interact() => GameSession.Instance.PullPompon();

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
