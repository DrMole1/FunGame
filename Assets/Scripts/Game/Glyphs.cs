using System.Collections.Generic;
using UnityEngine;

// Glyphes (GDD 9, V0.6.4) : motifs rouges sur plaque crème, dans un contour blanc lumineux (Resources/Art/Glyphes, d'après
// Assets/Sprite). 0 : "immobile et accroupi"   1 : "aller à la niche"   2 : "aboyer"
// Dans la Neutral Room, les glyphes neutres jouent seulement leur son quand on clique dessus. Dans le reste des zones
// paires, les glyphes d'acquisition (plus grands) se chargent quand une Balise chargée les voit : ils brillent de bleu,
// un rayon les relie à la Balise, et leur ordre s'impose à tous les joueurs.
public class Glyph : Interactable
{
    public const float NeutralSize = 0.9f, AcquisitionSize = 1.5f;
    static readonly Color GlowColor = new Color(0.35f, 0.7f, 1.6f);

    public int Id { get; private set; }
    public int Type { get; private set; }
    public int Zone { get; private set; }
    public bool Neutral { get; private set; }
    public bool Charged { get; private set; }
    // Centre de la face gravée (point d'attache du rayon d'énergie).
    public Vector3 Anchor => face != null ? face.transform.position : transform.position;

    Renderer face, frame;
    Light glow;
    MaterialPropertyBlock block;

    // Sons propres à chaque glyphe (alertes terrifiantes de moins de 2 secondes).
    public static readonly string[] Sounds = { "glyph_sit", "glyph_kennel", "glyph_bark" };

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
        glyph.glow.color = new Color(0.35f, 0.65f, 1f);
        glyph.glow.range = size * 4f;
        glyph.glow.intensity = 1.5f;
        glyph.glow.shadows = LightShadows.None;
        glyph.glow.enabled = false;

        glyph.FitTrigger(new Vector3(size, size, 0.4f));
        // Après la zone de sélection : elle reste à la taille de la plaque.
        glyph.AddFrame(size);
        glyph.SetCharged(false);
        return glyph;
    }

    // Contour blanc lumineux, toujours visible, glissé derrière la plaque qui en cache le bord intérieur.
    const float FrameWidthRatio = 1.75f, FrameOffset = 0.03f;
    static Material frameMaterial;

    void AddFrame(float size)
    {
        if (frameMaterial == null) frameMaterial = new Material(Shader.Find("Sprites/Default")) { name = "GlypheCadre", mainTexture = GlyphArt.Load("Cadre") };
        var quad = GameArt.Prim(PrimitiveType.Quad, transform, Vector3.forward * FrameOffset, Vector3.one * size * FrameWidthRatio, frameMaterial);
        quad.name = "Cadre";
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        frame = quad.GetComponent<Renderer>();
        frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        frame.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // Pas de contour de sélection autour du carré invisible qui porte le cadre.
    protected override bool HasOutline(Renderer r) => r != frame;

    public void SetCharged(bool on)
    {
        Charged = on;
        glow.enabled = on;
        block.SetColor("_EmissionColor", on ? GlowColor : Color.black);
        face.SetPropertyBlock(block);
    }

    void Update()
    {
        if (!Charged) return;
        float k = 0.8f + 0.2f * Mathf.Sin(Time.time * 3.1f + Id);
        glow.intensity = 1.5f * k;
        block.SetColor("_EmissionColor", GlowColor * k);
        face.SetPropertyBlock(block);
    }

    // Seuls les glyphes neutres se touchent (pour entendre leur son).
    public override bool CanInteract => Neutral;
    public override string Prompt => Loc.T("Toucher le glyphe");
    public override void Interact() => GameSession.Instance.TouchGlyph(this);
}

// Textures des glyphes : motif seul (blanc sur transparent), sert aussi de carte d'émission pour que le glyphe chargé s'illumine.
public static class GlyphArt
{
    public static readonly Color Red = new Color32(224, 60, 64, 255);
    public static Material Cream => GameArt.Lit("GlyphCream", new Color32(245, 235, 210, 255), null, 0.25f);

    static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

    public static Texture2D Load(string name) => Resources.Load<Texture2D>("Art/Glyphes/" + name);

    // Motif seul sur fond transparent, non éclairé, posé à même un médaillon (porte du chapiteau).
    public static Material Engraving(int type) =>
        new Material(Shader.Find("Sprites/Default")) { name = "Gravure" + type, color = Red, mainTexture = Load("Glyphe" + (type + 1)) };

    // Plaque crème au motif rouge ; l'émission (bleue quand le glyphe est chargé) suit le motif.
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
// d'acquisition de la zone ; chacun s'allume en bleu quand son glyphe est chargé. Elle s'ouvre (le battant s'enfonce dans
// le sol) quand tous le sont, et ne se referme plus.
public class AcquisitionDoor : MonoBehaviour
{
    const float OpenDuration = 3f;
    // Battant devant la face du mur (le mur fait 30 cm d'épaisseur), débordant de 18 cm sur les montants et le linteau.
    const float SlabOffset = 0.24f, SlabOverlap = 0.18f;
    float slabZ;

    Transform slab;
    BoxCollider blocker;
    readonly List<Renderer> icons = new List<Renderer>();
    readonly List<bool> lit = new List<bool>();
    Light glow;
    float height, openAmount;
    bool open;

    public bool IsOpen => open;

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
            GameArt.Prim(PrimitiveType.Cylinder, door.slab, at, new Vector3(cell * 0.85f, 0.01f, cell * 0.85f), GlyphArt.Cream).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
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
        door.glow.color = new Color(0.35f, 0.65f, 1f);
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
        // Motif rouge, bleu et lumineux quand son glyphe est chargé.
        icons[index].material.color = on ? new Color(0.45f, 0.8f, 1f) : GlyphArt.Red;
        int count = 0;
        foreach (bool l in lit) if (l) count++;
        glow.intensity = icons.Count > 0 ? 1.4f * count / icons.Count : 0f;
    }

    public void SetOpen(bool value, bool instant)
    {
        if (value && !open && !instant) GameAudio.Play3D("zone_door", transform.position + Vector3.up * 2f, 1f, 60f, 0f, 1.25f);
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
