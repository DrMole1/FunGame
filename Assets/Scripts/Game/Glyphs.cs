using System.Collections.Generic;
using UnityEngine;

// Glyphes (GDD 9, V0.6.5) : motifs rouges sur plaque crème, dans un contour (Resources/Art/Glyphes, d'après Assets/Sprite).
// 0 : "immobile et accroupi"   1 : "aller à la niche"   2 : "aboyer"
// Zones impaires : 3 glyphes neutres, enfermés dans un boîtier de verre et reliés à la porte de la Neutral Room par un câble
// noir. Une Clé en main brise le boîtier, un clic sur le glyphe le déclenche : son contour et son câble deviennent blancs.
// V0.6.13 : au-dessus de chaque glyphe neutre, des icônes de pattes de chien d'un blanc très peu lumineux (chapitre 16.1,
// Calcul des Icônes : 1 icône à 1 joueur, 2 à 2 ou 3 joueurs, 3 à 4 ou 5, 4 à partir de 6). V0.7.2 : pastilles en léger
// relief, disque noir cerclé de blanc. Chaque joueur qui maintient le clic sur le glyphe en allume une ; toutes
// allumées en même temps, le glyphe est actionné, et elles le restent.
// Zones paires : les glyphes d'acquisition (plus grands, jamais interactifs) se chargent quand une Balise chargée les voit :
// ils brillent de blanc, un rayon les relie à la Balise, et leur ordre s'impose à tous les joueurs.
public class Glyph : Interactable
{
    public const float NeutralSize = 0.9f, AcquisitionSize = 1.5f;
    static readonly Color GlowColor = new Color(1.5f, 1.5f, 1.5f);

    public int Id { get; private set; }
    public int Type { get; private set; }
    public int Zone { get; private set; }
    public bool Neutral { get; private set; }
    public bool Charged { get; private set; }
    // Glyphes neutres : boîtier brisé, puis glyphe actionné (une seule fois).
    public bool CaseBroken { get; private set; }
    public bool Activated { get; private set; }
    // Centre de la face gravée (point d'attache du rayon d'énergie).
    public Vector3 Anchor => face != null ? face.transform.position : transform.position;

    Renderer face, frame;
    Renderer cable;
    GameObject glassCase;
    Light glow;
    MaterialPropertyBlock block;

    // Sons propres à chaque glyphe (alertes terrifiantes de moins de 2 secondes).
    public static readonly string[] Sounds = { "glyph_sit", "glyph_kennel", "glyph_bark" };

    // ---------- Icônes de pattes (glyphes neutres, V0.6.13) ----------
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
        if (!Neutral) return;
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

    public static Glyph Create(Transform parent, int id, int type, bool neutral, int zone, Vector3 pos, Vector3 normal, float size)
    {
        var go = new GameObject((neutral ? "GlypheNeutre_" : "GlypheAcquisition_") + id);
        go.transform.SetParent(parent, false);
        // L'avant (+Z) sort du mur.
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
        var glyph = go.AddComponent<Glyph>();
        glyph.Id = id;
        glyph.Type = type;
        glyph.Zone = zone;
        glyph.Neutral = neutral;
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
        glyph.block = new MaterialPropertyBlock();

        glyph.glow = new GameObject("Lueur").AddComponent<Light>();
        glyph.glow.transform.SetParent(go.transform, false);
        glyph.glow.transform.localPosition = new Vector3(0f, 0f, 0.8f);
        glyph.glow.type = LightType.Point;
        glyph.glow.color = Color.white;
        glyph.glow.range = size * 4f;
        glyph.glow.intensity = 1.5f;
        glyph.glow.shadows = LightShadows.None;
        glyph.glow.enabled = false;

        if (neutral) glyph.AddCase(size);
        glyph.FitTrigger(new Vector3(size, size, 0.4f));
        // Après la zone de sélection : elle reste à la taille de la plaque.
        glyph.AddFrame(size);
        glyph.SetCharged(false);
        return glyph;
    }

    // Contour glissé derrière la plaque qui en cache le bord intérieur (V0.6.5) : blanc lumineux pour un glyphe d'acquisition ;
    // noir pour un glyphe neutre, puis blanc lumineux une fois actionné.
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
        var quad = GameArt.Prim(PrimitiveType.Quad, transform, Vector3.forward * FrameOffset, Vector3.one * size * FrameWidthRatio, FrameMaterial(!Neutral));
        quad.name = "Cadre";
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        frame = quad.GetComponent<Renderer>();
        frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        frame.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Pas de contour de sélection autour du carré invisible qui porte le cadre ; boîtier intact, seul lui est sélectionnable.
    protected override bool HasOutline(Renderer r) => r != frame && (glassCase == null || CaseBroken || r.transform.IsChildOf(glassCase.transform));

    // Glyphe neutre : état du boîtier et du glyphe (identique chez tous les joueurs).
    public void SetNeutralState(bool broken, bool activated, bool animate)
    {
        if (!Neutral) return;
        if (broken && !CaseBroken && animate) ShatterCase();
        bool caseChanged = broken != CaseBroken;
        CaseBroken = broken;
        Activated = activated;
        glassCase.SetActive(!broken);
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

    public void SetCharged(bool on)
    {
        Charged = on;
        glow.enabled = on;
        block.SetColor("_EmissionColor", on ? GlowColor : Color.black);
        face.SetPropertyBlock(block);
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

    void Update()
    {
        UpdateGlass();
        if (!Charged) return;
        float k = 0.8f + 0.2f * Mathf.Sin(Time.time * 3.1f + Id);
        glow.intensity = 1.5f * k;
        block.SetColor("_EmissionColor", GlowColor * k);
        face.SetPropertyBlock(block);
    }

    // Seuls les glyphes neutres sont interactifs : le boîtier avec une Clé en main (en godmod, sans Clé), puis le glyphe, une fois.
    public override bool CanInteract
    {
        get
        {
            if (!Neutral || Activated) return false;
            if (CaseBroken) return true;
            var session = GameSession.Instance;
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

// Textures des glyphes : motif seul (blanc sur transparent), sert aussi de carte d'émission pour que le glyphe chargé s'illumine.
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

// Porte du chapiteau suivant, dans le mur nord de l'Acquisition Room (GDD 9, V0.6) : elle porte tous les glyphes
// d'acquisition de la zone ; chacun s'allume en blanc, avec un contour blanc lumineux (V0.6.5), quand son glyphe est chargé.
// Elle s'ouvre (le battant s'enfonce dans le sol) quand tous le sont. Elle ne se referme que pour sceller les zones trop en
// arrière (chapitre 9.2).
public class AcquisitionDoor : MonoBehaviour
{
    const float OpenDuration = 3f;
    // Battant devant la face du mur (le mur fait 30 cm d'épaisseur), débordant de 18 cm sur les montants et le linteau.
    const float SlabOffset = 0.24f, SlabOverlap = 0.18f;
    float slabZ;

    Transform slab;
    BoxCollider blocker;
    readonly List<Renderer> icons = new List<Renderer>(), plates = new List<Renderer>();
    readonly List<GameObject> halos = new List<GameObject>();
    readonly List<bool> lit = new List<bool>();
    Light glow;
    float height, openAmount;
    bool open;

    public bool IsOpen => open;
    public bool FullyClosed => !open && openAmount <= 0.001f;

    public static AcquisitionDoor Create(Transform parent, Vector3 pos, Quaternion rot, float width, float height, List<int> glyphTypes)
    {
        var go = new GameObject("Porte_Chapiteau");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var door = go.AddComponent<AcquisitionDoor>();
        door.height = height;

        // Battant rouge du chapiteau, rayé de crème, cerclé d'or ; les glyphes rouges à même des médaillons crème (V0.6.1 :
        // pas de carré autour du motif).
        // V0.6.1 (note 01) : le battant n'est plus pris dans l'épaisseur du mur (ses bords se mêlaient aux montants et au
        // chapiteau voisin) ; il est posé devant la face du mur, côté Acquisition Room, et déborde sur les montants.
        door.slab = new GameObject("Battant").transform;
        door.slab.SetParent(go.transform, false);
        door.slabZ = SlabOffset;
        door.slab.localPosition = Vector3.forward * SlabOffset;
        Material red = GameArt.Lit("HubRed", new Color(0.72f, 0.08f, 0.1f), GameArt.Canvas(), 0.85f);
        Material gold = GameArt.Lit("HubGold", new Color(0.85f, 0.65f, 0.2f), null, 0.6f);
        float leaf = width + SlabOverlap * 2f;
        // Note 06 (V0.7) : encadrement noir au creux de l'ouverture, du côté du battant (qui est posé devant le mur).
        DoorFrame.Inset(go.transform, Vector3.forward * (WorldBuilder.Wall * 0.5f - 0.1f), width * 0.5f, height, 0.18f, 0.2f);
        GameArt.Prim(PrimitiveType.Cube, door.slab, new Vector3(0f, (height + SlabOverlap) * 0.5f, 0f), new Vector3(leaf, height + SlabOverlap, 0.14f), red);
        GameArt.Prim(PrimitiveType.Cube, door.slab, new Vector3(0f, height + SlabOverlap - 0.08f, 0.08f), new Vector3(leaf, 0.12f, 0.03f), gold);
        GameArt.Prim(PrimitiveType.Cube, door.slab, new Vector3(0f, 0.08f, 0.08f), new Vector3(leaf, 0.12f, 0.03f), gold);

        int count = glyphTypes.Count;
        int columns = Mathf.Max(1, Mathf.Min(count, 3)), rows = Mathf.Max(1, Mathf.CeilToInt(count / 3f));
        float cell = Mathf.Min((width - 0.3f) / columns, (height - 0.6f) / rows);
        for (int k = 0; k < count; k++)
        {
            int row = k / 3, col = k % 3, inRow = Mathf.Min(3, count - row * 3);
            var at = new Vector3((col - (inRow - 1) * 0.5f) * cell, height * 0.55f + ((rows - 1) * 0.5f - row) * cell, 0.085f);
            var plate = GameArt.Prim(PrimitiveType.Cylinder, door.slab, at, new Vector3(cell * 0.85f, 0.01f, cell * 0.85f), GlyphArt.Cream);
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            door.plates.Add(plate.GetComponent<Renderer>());
            // Contour blanc lumineux, autour du médaillon, quand son glyphe est chargé.
            var halo = GameArt.Prim(PrimitiveType.Cylinder, door.slab, at - Vector3.forward * 0.004f, new Vector3(cell * 0.97f, 0.008f, cell * 0.97f), GameArt.Emissive("HaloGlyphe", Color.white, Color.white * 2f));
            halo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            halo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            door.halos.Add(halo);
            var quad = GameArt.Prim(PrimitiveType.Quad, door.slab, at + Vector3.forward * 0.012f, Vector3.one * cell * 0.62f, GlyphArt.Engraving(glyphTypes[k]));
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            door.icons.Add(quad.GetComponent<Renderer>());
            door.lit.Add(false);
        }

        door.glow = new GameObject("Lueur").AddComponent<Light>();
        door.glow.transform.SetParent(go.transform, false);
        door.glow.transform.localPosition = new Vector3(0f, height * 0.6f, 1.2f);
        door.glow.type = LightType.Point;
        door.glow.color = Color.white;
        door.glow.range = 6f;
        door.glow.intensity = 0f;
        door.glow.shadows = LightShadows.None;

        door.blocker = go.AddComponent<BoxCollider>();
        door.blocker.center = new Vector3(0f, height * 0.5f, 0f);
        door.blocker.size = new Vector3(width + 0.2f, height, 0.4f);
        for (int k = 0; k < count; k++) door.SetGlyphLit(k, false);
        return door;
    }

    public void SetGlyphLit(int index, bool on)
    {
        if (index < 0 || index >= icons.Count) return;
        lit[index] = on;
        // Motif rouge ; blanc lumineux, sur son médaillon assombri pour rester lisible, quand son glyphe est chargé.
        icons[index].material.color = on ? Color.white : GlyphArt.Red;
        plates[index].sharedMaterial = on ? GlyphArt.CreamDim : GlyphArt.Cream;
        halos[index].SetActive(on);
        int count = 0;
        foreach (bool l in lit) if (l) count++;
        glow.intensity = icons.Count > 0 ? 1.4f * count / icons.Count : 0f;
    }

    public void SetOpen(bool value, bool instant)
    {
        if (value != open && !instant) GameAudio.Play3D("zone_door", transform.position + Vector3.up * 2f, 1f, 60f, 0f, 1.25f);
        // GDD 12 (V0.7.2) : Ambiance_Ouverture, porte d'acquisition qui s'ouvre (chez chaque joueur, non spatialisé, -50 %).
        if (value && !open && !instant) GameAudio.Play2D("ambience_opening", 0.5f);
        if (value != open) BeaconNetwork.SightChanged();
        open = value;
        blocker.enabled = !open;
        if (instant) Apply(open ? 1f : 0f);
    }

    void Update()
    {
        float goal = open ? 1f : 0f;
        if (Mathf.Approximately(openAmount, goal)) return;
        Apply(Mathf.MoveTowards(openAmount, goal, Time.deltaTime / OpenDuration));
    }

    void Apply(float amount)
    {
        openAmount = amount;
        slab.localPosition = Vector3.forward * slabZ + Vector3.down * (Mathf.SmoothStep(0f, 1f, amount) * (height + SlabOverlap + 0.1f));
        slab.gameObject.SetActive(amount < 0.999f);
    }
}
