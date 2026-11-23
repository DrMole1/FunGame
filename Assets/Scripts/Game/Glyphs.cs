using System.Collections.Generic;
using UnityEngine;

// Glyphes (GDD 9, V0.6.5) : motifs rouges sur plaque crème, dans un contour (Resources/Art/Glyphes, d'après Assets/Sprite).
// 0 : "immobile et accroupi"   1 : "aller à la niche"   2 : "aboyer"
// V0.7.9 (GDD 9.2 et 9.3) : chaque zone a 3 glyphes différents, un de chaque, reliés par un câble noir à la porte de la Neutral
// Room (V0.7.11 : toutes les zones ont une Neutral Room, plus d'Acquisition Room). Certains sont enfermés dans un boîtier de verre
// (1 en Zone 1, 2 en Zone 2, tous à partir de la Zone 3) : une Clé en main le brise.
// V0.6.13 : au-dessus de chaque glyphe neutre, des icônes de pattes de chien d'un blanc très peu lumineux (chapitre 16.1,
// Calcul des Icônes : 1 icône à 1 joueur, 2 à 2 ou 3 joueurs, 3 à 4 ou 5, 4 à partir de 6). V0.7.2 : pastilles en léger
// relief, disque noir cerclé de blanc. Chaque joueur qui maintient le clic sur le glyphe en allume une ; toutes
// allumées en même temps, le glyphe est actionné, et elles le restent.
// Actionné, un glyphe (pair ou impair) impose son ordre à tous les joueurs (V0.7.9 : plus de texte sous le glyphe ni d'ordre
// sonore ; une icône de l'ordre s'affiche en bas à droite de l'écran). Un seul ordre à la fois : pendant qu'un glyphe sonne, les
// autres ne sont plus interactifs.
public class Glyph : Interactable
{
    public const float Size = 0.9f;

    public int Id { get; private set; }
    public int Type { get; private set; }
    public int Zone { get; private set; }
    // V0.7.11 : toujours vrai (tous les glyphes ouvrent la porte de leur Neutral Room).
    public bool Odd { get; private set; }
    // V0.7.9 : sous boîtier de verre (brisé avec une Clé), puis glyphe actionné (une seule fois).
    public bool Boxed { get; private set; }
    public bool CaseBroken => !Boxed || caseBroken;
    bool caseBroken;
    public bool Activated { get; private set; }

    Renderer face, frame;
    Renderer cable;
    GameObject glassCase;

    // Sons propres à chaque glyphe (alertes terrifiantes de moins de 2 secondes).
    public static readonly string[] Sounds = { "glyph_sit", "glyph_kennel", "glyph_bark" };

    // ---------- Icônes de pattes (V0.6.13) ----------
    const float PawIconSize = 0.2f, PawIconGap = 0.06f, PawIconRaise = 0.2f;
    static readonly Color PawDim = new Color(0.26f, 0.26f, 0.27f, 0.85f), PawLit = new Color(1f, 1f, 1f, 1f);
    static Texture2D pawTexture;
    static Material pawDim, pawLit;
    readonly List<Renderer> paws = new List<Renderer>();
    readonly List<GameObject> pawBadges = new List<GameObject>();
    static Material badgeBlack, badgeWhite;
    Light pawGlow;
    int pawsLit;
    float plateSize;

    public int PawCount => paws.Count;

    // Patte : coussinet et quatre doigts, blanc sur transparent.
    public static Texture2D PawTexture()
    {
        if (pawTexture != null) return pawTexture;
        const int n = 64;
        pawTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "PatteGlyphe", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[n * n];
        var blobs = new (Vector2 c, Vector2 r)[]
        {
            (new Vector2(0.5f, 0.33f), new Vector2(0.22f, 0.18f)),
            (new Vector2(0.24f, 0.6f), new Vector2(0.09f, 0.12f)), (new Vector2(0.4f, 0.74f), new Vector2(0.09f, 0.12f)),
            (new Vector2(0.6f, 0.74f), new Vector2(0.09f, 0.12f)), (new Vector2(0.76f, 0.6f), new Vector2(0.09f, 0.12f)),
        };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float a = 0f;
                foreach (var (c, r) in blobs)
                {
                    Vector2 d = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
                    a = Mathf.Max(a, Mathf.Clamp01((1f - d.magnitude) * 12f));
                }
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        pawTexture.SetPixels32(pixels);
        pawTexture.Apply();
        return pawTexture;
    }

    static Material PawMaterial(bool lit)
    {
        if (pawDim == null)
        {
            pawDim = new Material(Shader.Find("Sprites/Default")) { name = "PatteEteinte", mainTexture = PawTexture(), color = PawDim };
            pawLit = new Material(Shader.Find("Sprites/Default")) { name = "PatteAllumee", mainTexture = PawTexture(), color = PawLit };
        }
        return lit ? pawLit : pawDim;
    }

    // Nombre d'icônes (selon le nombre de joueurs en jeu).
    public void SetPawCount(int count)
    {
        count = Mathf.Clamp(count, 1, 8);
        if (count == paws.Count) return;
        foreach (var r in paws) if (r != null) Destroy(r.gameObject);
        paws.Clear();
        foreach (var b in pawBadges) if (b != null) Destroy(b);
        pawBadges.Clear();
        if (badgeBlack == null)
        {
            badgeBlack = GameArt.Lit("PastilleNoire", new Color(0.03f, 0.03f, 0.035f), null, 0.35f);
            badgeWhite = GameArt.Lit("PastilleBlanche", new Color(0.92f, 0.92f, 0.92f), null, 0.3f);
        }
        float width = count * PawIconSize + (count - 1) * PawIconGap;
        for (int k = 0; k < count; k++)
        {
            float x = -width * 0.5f + PawIconSize * 0.5f + k * (PawIconSize + PawIconGap);
            Vector3 at = new Vector3(x, plateSize * FrameWidthRatio * 0.5f + PawIconRaise, 0f);
            // V0.7.2 (Note 06) : chaque icône est une pastille en léger relief sur le mur : un disque noir bombé, cerclé de
            // blanc, avec la patte de chien blanche en son centre.
            var badge = new GameObject("Pastille" + (k + 1));
            badge.transform.SetParent(transform, false);
            badge.transform.localPosition = at;
            var rim = GameArt.Prim(PrimitiveType.Sphere, badge.transform, new Vector3(0f, 0f, 0.004f), new Vector3(PawIconSize * 1.12f, PawIconSize * 1.12f, 0.05f), badgeWhite);
            var disc = GameArt.Prim(PrimitiveType.Sphere, badge.transform, new Vector3(0f, 0f, 0.01f), new Vector3(PawIconSize * 0.96f, PawIconSize * 0.96f, 0.06f), badgeBlack);
            foreach (var part in new[] { rim, disc }) part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pawBadges.Add(badge);
            var quad = GameArt.Prim(PrimitiveType.Quad, badge.transform, new Vector3(0f, 0f, 0.042f), Vector3.one * PawIconSize * 0.72f, PawMaterial(false));
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.name = "Patte" + (k + 1);
            foreach (var c in quad.GetComponentsInChildren<Collider>()) Destroy(c);
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            paws.Add(r);
        }
        if (pawGlow == null)
        {
            pawGlow = new GameObject("LueurPattes").AddComponent<Light>();
            pawGlow.transform.SetParent(transform, false);
            pawGlow.type = LightType.Point;
            pawGlow.color = Color.white;
            pawGlow.range = 1.6f;
            pawGlow.shadows = LightShadows.None;
        }
        pawGlow.transform.localPosition = new Vector3(0f, plateSize * FrameWidthRatio * 0.5f + PawIconRaise, 0.35f);
        RefreshPaws();
    }

    // Icônes allumées (le glyphe actionné les garde toutes allumées).
    public void SetPawsLit(int lit)
    {
        pawsLit = Mathf.Max(0, lit);
        RefreshPaws();
    }

    void RefreshPaws()
    {
        int lit = Activated ? paws.Count : Mathf.Min(pawsLit, paws.Count);
        for (int k = 0; k < paws.Count; k++) if (paws[k] != null) paws[k].sharedMaterial = PawMaterial(k < lit);
        if (pawGlow != null)
        {
            pawGlow.enabled = lit > 0;
            pawGlow.intensity = 0.25f + 0.2f * lit;
        }
    }

    public static Glyph Create(Transform parent, int id, int type, bool odd, bool boxed, int zone, Vector3 pos, Vector3 normal, float size)
    {
        var go = new GameObject((odd ? "GlypheImpair_" : "GlyphePair_") + id);
        go.transform.SetParent(parent, false);
        // L'avant (+Z) sort du mur.
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
        var glyph = go.AddComponent<Glyph>();
        glyph.Id = id;
        glyph.Type = type;
        glyph.Zone = zone;
        glyph.Odd = odd;
        glyph.Boxed = boxed;
        glyph.plateSize = size;

        // Plaque crème.
        float depth = 0.08f;
        GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, depth * 0.5f), new Vector3(size, size, depth), GlyphArt.Cream, true);
        var quad = GameArt.Prim(PrimitiveType.Quad, go.transform, new Vector3(0f, 0f, depth + 0.006f), new Vector3(size, size, 1f), GlyphArt.Material(type));
        // Le quad est visible depuis son -Z : retourné vers l'avant.
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        quad.name = "Gravure";
        glyph.face = quad.GetComponent<Renderer>();
        glyph.face.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (boxed) glyph.AddCase(size);
        glyph.FitTrigger(new Vector3(size, size, 0.4f));
        // Après la zone de sélection : elle reste à la taille de la plaque.
        glyph.AddFrame(size);
        return glyph;
    }

    // Contour glissé derrière la plaque qui en cache le bord intérieur (V0.6.5) : noir, puis blanc lumineux une fois le glyphe
    // actionné (V0.7.9 : pour tous les glyphes).
    const float FrameWidthRatio = 1.75f, FrameOffset = 0.03f;
    static Material frameLit, frameDark, glassMaterial, shardMaterial;

    static Material FrameMaterial(bool lit)
    {
        if (frameLit == null)
        {
            frameLit = new Material(Shader.Find("Sprites/Default")) { name = "GlypheCadre", mainTexture = GlyphArt.Load("Cadre") };
            frameDark = new Material(frameLit) { name = "GlypheCadreNoir", color = Color.black };
        }
        return lit ? frameLit : frameDark;
    }

    // Boîtier de verre : il enferme la plaque et son contour, dans une fine armature de métal.
    const float CaseRatio = 1.85f, CaseDepth = 0.28f, CaseBar = 0.035f;
    // Verre à peine visible de près, totalement transparent de loin (distance à la caméra).
    const float GlassNearAlpha = 0.006f, GlassFadeStart = 5f, GlassFadeEnd = 10f;
    static readonly Color GlassColor = new Color(0.92f, 0.95f, 1f, 0f);
    Renderer glass;
    MaterialPropertyBlock glassBlock;

    void AddCase(float size)
    {
        if (glassMaterial == null) glassMaterial = new Material(Shader.Find("Sprites/Default")) { name = "BoitierVerre", color = GlassColor };
        glassCase = new GameObject("Boitier");
        glassCase.transform.SetParent(transform, false);
        float w = size * CaseRatio;
        glass = GameArt.Prim(PrimitiveType.Cube, glassCase.transform, Vector3.forward * CaseDepth * 0.5f, new Vector3(w, w, CaseDepth), glassMaterial).GetComponent<Renderer>();
        glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glassBlock = new MaterialPropertyBlock();
        Material metal = GameArt.Lit("BoitierMetal", new Color(0.35f, 0.37f, 0.4f), null, 0.6f);
        foreach (float s in new[] { -1f, 1f })
        {
            GameArt.Prim(PrimitiveType.Cube, glassCase.transform, new Vector3(0f, s * w * 0.5f, CaseDepth), new Vector3(w + CaseBar, CaseBar, CaseBar), metal);
            GameArt.Prim(PrimitiveType.Cube, glassCase.transform, new Vector3(s * w * 0.5f, 0f, CaseDepth), new Vector3(CaseBar, w + CaseBar, CaseBar), metal);
        }
        padlock = BuildPadlock(glassCase.transform, new Vector3(0f, -w * 0.5f, CaseDepth));
    }

    // Serrure : petit cadenas de fer rouillé (comme la Clé) suspendu au milieu de la barre basse, côté joueur. L'anse
    // entoure la barre ; le corps porte un trou de serrure.
    const float LockWidth = 0.12f, LockHeight = 0.1f, LockDepth = 0.045f, ShackleRadius = 0.038f, ShackleTube = 0.009f, LockScale = 1.5f;
    static Mesh shackleMesh;
    GameObject padlock;

    static GameObject BuildPadlock(Transform parent, Vector3 bar)
    {
        Material iron = ItemPickup.KeyIron;
        var root = new GameObject("Serrure");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = bar;
        root.transform.localScale = Vector3.one * LockScale;
        // Anse : un anneau autour de la barre (dans le plan perpendiculaire à la barre), son bas pris dans le corps.
        if (shackleMesh == null) shackleMesh = ItemPickup.Torus(ShackleRadius, ShackleTube, 24, 8);
        var shackle = new GameObject("Anse", typeof(MeshFilter), typeof(MeshRenderer));
        shackle.transform.SetParent(root.transform, false);
        shackle.transform.localPosition = new Vector3(0f, -0.012f, 0f);
        shackle.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        shackle.GetComponent<MeshFilter>().sharedMesh = shackleMesh;
        shackle.GetComponent<MeshRenderer>().sharedMaterial = iron;
        // Corps, arrondi en bas, et trou de serrure (un rond et une fente) sur la face avant.
        float top = -0.012f - ShackleRadius + 0.012f;
        float cy = top - LockHeight * 0.5f;
        GameArt.Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, cy + LockHeight * 0.1f, 0f), new Vector3(LockWidth, LockHeight * 0.8f, LockDepth), iron);
        var bottom = GameArt.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cy - LockHeight * 0.28f, 0f), new Vector3(LockWidth, LockDepth * 0.5f, LockWidth * 0.45f), iron);
        bottom.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Material hole = GameArt.Lit("TrouSerrure", new Color(0.02f, 0.015f, 0.01f), null, 0f);
        float front = LockDepth * 0.5f + 0.001f;
        var round = GameArt.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cy + 0.008f, front), new Vector3(0.022f, 0.002f, 0.022f), hole);
        round.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameArt.Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, cy - 0.012f, front), new Vector3(0.009f, 0.028f, 0.004f), hole);
        foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    // Câble entre le glyphe et la porte : noir et peu visible, blanc lumineux une fois le glyphe actionné.
    static Material cableDark, cableLit;

    public static Material CableMaterial(bool lit)
    {
        if (cableDark == null)
        {
            cableDark = new Material(Shader.Find("Sprites/Default")) { name = "CableNoir", color = new Color(0.02f, 0.02f, 0.025f) };
            cableLit = new Material(Shader.Find("Sprites/Default")) { name = "CableBlanc", color = Color.white };
        }
        return lit ? cableLit : cableDark;
    }

    public void SetCable(Renderer renderer) => cable = renderer;

    void AddFrame(float size)
    {
        var quad = GameArt.Prim(PrimitiveType.Quad, transform, Vector3.forward * FrameOffset, Vector3.one * size * FrameWidthRatio, FrameMaterial(false));
        quad.name = "Cadre";
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        frame = quad.GetComponent<Renderer>();
        frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        frame.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Pas de contour de sélection autour du carré invisible qui porte le cadre ; boîtier intact, seul lui est sélectionnable.
    protected override bool HasOutline(Renderer r) => r != frame && (glassCase == null || CaseBroken || r.transform.IsChildOf(glassCase.transform));

    // État du boîtier et du glyphe (identique chez tous les joueurs).
    public void SetState(bool broken, bool activated, bool animate)
    {
        broken |= !Boxed;
        if (broken && !CaseBroken && animate) ShatterCase();
        bool caseChanged = broken != CaseBroken;
        if (Boxed) caseBroken = broken;
        Activated = activated;
        if (glassCase != null) glassCase.SetActive(!broken);
        frame.sharedMaterial = FrameMaterial(activated);
        if (cable != null) cable.sharedMaterial = CableMaterial(activated);
        if (activated) pawsLit = paws.Count;
        RefreshPaws();
        if (caseChanged) RefreshOutline();
    }

    // Éclats de verre qui retombent.
    void ShatterCase()
    {
        Vector3 center = transform.TransformPoint(Vector3.forward * CaseDepth * 0.5f);
        GameAudio.Play3D("orb_break", center, 1f, 35f, 0.05f, 1.35f);
        if (shardMaterial == null) shardMaterial = new Material(Shader.Find("Sprites/Default")) { name = "EclatVerre", color = new Color(0.8f, 0.92f, 1f, 0.55f) };
        for (int k = 0; k < 16; k++)
        {
            var piece = GameArt.Prim(PrimitiveType.Cube, null, center + Random.insideUnitSphere * 0.4f, new Vector3(Random.Range(0.05f, 0.16f), Random.Range(0.05f, 0.16f), 0.01f), shardMaterial, true);
            piece.transform.rotation = Random.rotation;
            var rb = piece.AddComponent<Rigidbody>();
            rb.velocity = transform.forward * Random.Range(0.5f, 2.5f) + Random.insideUnitSphere * 1.5f;
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            piece.layer = 2;
            Destroy(piece, 4f);
        }
        // La serrure brisée tombe avec le verre.
        if (padlock != null)
        {
            var broken = Instantiate(padlock, padlock.transform.position, padlock.transform.rotation);
            broken.transform.localScale = padlock.transform.lossyScale;
            broken.layer = 2;
            var box = broken.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.07f, 0f);
            box.size = new Vector3(LockWidth, 0.12f, LockDepth);
            var rb = broken.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.velocity = transform.forward * Random.Range(1f, 2f) + Vector3.up * Random.Range(0.5f, 1.5f);
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            Destroy(broken, 6f);
        }
    }

    void UpdateGlass()
    {
        if (glass == null || CaseBroken || !glass.isVisible) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Color c = GlassColor;
        c.a = GlassNearAlpha * Mathf.InverseLerp(GlassFadeEnd, GlassFadeStart, Vector3.Distance(cam.transform.position, transform.position));
        glassBlock.SetColor("_Color", c);
        // Sur le verre seul : le contour de sélection, ajouté en second matériau, garde sa couleur.
        glass.SetPropertyBlock(glassBlock, 0);
    }

    void Update() => UpdateGlass();

    // Le boîtier avec une Clé en main (en godmod, sans Clé), puis le glyphe, une fois. V0.7.9 : aucun glyphe n'est interactif
    // tant qu'un autre fait entendre son ordre.
    public override bool CanInteract
    {
        get
        {
            if (Activated) return false;
            var session = GameSession.Instance;
            if (CaseBroken) return session == null || !session.OrderPlaying;
            return session != null && (session.SelectedItem == ItemType.Cle || session.Godmode);
        }
    }

    public override string Prompt => CaseBroken ? Loc.T("Maintenir le clic sur le glyphe") : Loc.T("Briser le boîtier");

    public override void Interact()
    {
        if (CaseBroken) GameSession.Instance.TouchGlyph(this);
        else GameSession.Instance.BreakGlyphCase(this);
    }
}

// Textures des glyphes : motif seul (blanc sur transparent) ; plaque crème au motif rouge.
public static class GlyphArt
{
    public static readonly Color Red = new Color32(224, 60, 64, 255);
    public static Material Cream => GameArt.Lit("GlyphCream", new Color32(245, 235, 210, 255), null, 0.25f);
    public static Material CreamDim => GameArt.Lit("GlyphCreamDim", new Color32(92, 84, 72, 255), null, 0.25f);

    static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

    public static Texture2D Load(string name) => Resources.Load<Texture2D>("Art/Glyphes/" + name);

    // Motif seul sur fond transparent, non éclairé, posé à même un médaillon (porte du chapiteau).
    public static Material Engraving(int type) =>
        new Material(Shader.Find("Sprites/Default")) { name = "Gravure" + type, color = Red, mainTexture = Load("Glyphe" + (type + 1)) };

    // Plaque crème au motif rouge ; l'émission (blanche quand le glyphe est chargé) suit le motif.
    public static Material Material(int type)
    {
        if (materials.TryGetValue(type, out var mat) && mat != null) return mat;
        mat = new Material(GameArt.Lit("GlyphFaceTemplate", Color.white, null, 0.25f)) { name = "Glyphe" + type };
        mat.mainTexture = Load("Glyphe" + (type + 1) + "_Plaque");
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetTexture("_EmissionMap", Load("Glyphe" + (type + 1)));
        mat.SetColor("_EmissionColor", Color.black);
        return materials[type] = mat;
    }
}
