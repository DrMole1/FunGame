using System.Collections.Generic;
using UnityEngine;

// Glyphes (GDD 9, V0.6) : logos inspirés des sinogrammes chinois, gravés sur des plaques de pierre.
//   0 : "immobile et accroupi" (d'après 坐, s'asseoir)   1 : "aller à la niche" (宀, le toit, sur 犬, le chien)
//   2 : "aboyer" (吠 : 口, la bouche, et 犬)
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

    Renderer face;
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

        // Plaque de pierre sombre, encadrée de métal terni.
        float depth = 0.08f;
        GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, depth * 0.5f), new Vector3(size * 1.12f, size * 1.12f, depth), GameArt.Metal, true);
        GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, depth + 0.01f), new Vector3(size, size, 0.02f),
            GameArt.Lit("GlyphStone", new Color(0.16f, 0.16f, 0.17f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f));
        var quad = GameArt.Prim(PrimitiveType.Quad, go.transform, new Vector3(0f, 0f, depth + 0.025f), new Vector3(size * 0.92f, size * 0.92f, 1f), GlyphArt.Material(type));
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
        glyph.SetCharged(false);
        return glyph;
    }

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

// Textures des glyphes : traits de pinceau (polylignes dans un carré unité, y vers le haut), gravés en clair sur la pierre.
// La même forme sert de carte d'émission : le glyphe chargé s'illumine.
public static class GlyphArt
{
    static readonly Vector2[][][] Strokes =
    {
        // 0 : 坐 — deux 人 au-dessus de 土.
        new[]
        {
            new[] { new Vector2(0.31f, 0.84f), new Vector2(0.20f, 0.58f) }, new[] { new Vector2(0.27f, 0.72f), new Vector2(0.40f, 0.58f) },
            new[] { new Vector2(0.71f, 0.84f), new Vector2(0.60f, 0.58f) }, new[] { new Vector2(0.67f, 0.72f), new Vector2(0.80f, 0.58f) },
            new[] { new Vector2(0.50f, 0.90f), new Vector2(0.50f, 0.17f) }, new[] { new Vector2(0.27f, 0.44f), new Vector2(0.73f, 0.44f) },
            new[] { new Vector2(0.12f, 0.17f), new Vector2(0.88f, 0.17f) },
        },
        // 1 : 宀 (le toit) sur 犬 (le chien) : rentrer à la niche.
        new[]
        {
            new[] { new Vector2(0.50f, 0.95f), new Vector2(0.53f, 0.86f) },
            new[] { new Vector2(0.14f, 0.70f), new Vector2(0.15f, 0.80f), new Vector2(0.85f, 0.80f), new Vector2(0.80f, 0.70f) },
            new[] { new Vector2(0.22f, 0.50f), new Vector2(0.78f, 0.50f) },
            new[] { new Vector2(0.50f, 0.70f), new Vector2(0.48f, 0.50f), new Vector2(0.22f, 0.10f) },
            new[] { new Vector2(0.53f, 0.46f), new Vector2(0.82f, 0.10f) },
            new[] { new Vector2(0.65f, 0.66f), new Vector2(0.72f, 0.59f) },
        },
        // 2 : 吠 — 口 (la bouche) et 犬 (le chien) : aboyer.
        new[]
        {
            new[] { new Vector2(0.07f, 0.64f), new Vector2(0.31f, 0.64f), new Vector2(0.31f, 0.30f), new Vector2(0.07f, 0.30f), new Vector2(0.07f, 0.64f) },
            new[] { new Vector2(0.39f, 0.58f), new Vector2(0.94f, 0.58f) },
            new[] { new Vector2(0.66f, 0.88f), new Vector2(0.64f, 0.58f), new Vector2(0.39f, 0.10f) },
            new[] { new Vector2(0.68f, 0.52f), new Vector2(0.94f, 0.10f) },
            new[] { new Vector2(0.80f, 0.82f), new Vector2(0.88f, 0.74f) },
        },
    };

    static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
    static readonly Dictionary<int, Texture2D> masks = new Dictionary<int, Texture2D>();

    // Gravure seule (blanc sur transparent), pour les glyphes de la porte du chapiteau.
    public static Texture2D Mask(int type)
    {
        if (masks.TryGetValue(type, out var tex) && tex != null) return tex;
        const int size = 128;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Glyphe" + type };
        var rnd = new System.Random(type * 977 + 5);
        var px = new Color[size * size];
        Vector2[][] strokes = Strokes[Mathf.Clamp(type, 0, Strokes.Length - 1)];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float best = 0f;
                foreach (var stroke in strokes)
                    for (int k = 0; k + 1 < stroke.Length; k++)
                    {
                        Vector2 a = stroke[k], b = stroke[k + 1], ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                        float d = Vector2.Distance(p, a + ab * t);
                        // Coup de pinceau : plus épais au départ, effilé à la fin, bord rongé.
                        float along = (k + t) / (stroke.Length - 1);
                        float width = 0.055f * Mathf.Lerp(1.15f, 0.55f, along);
                        width *= 0.85f + 0.3f * Mathf.PerlinNoise(p.x * 23f + type * 7f, p.y * 23f);
                        best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width * 0.75f, width, d)));
                    }
                float grain = 0.8f + 0.2f * (float)rnd.NextDouble();
                px[y * size + x] = new Color(grain, grain, grain, best);
            }
        tex.SetPixels(px);
        tex.Apply();
        return masks[type] = tex;
    }

    // Gravure seule (traits du glyphe sur fond transparent, non éclairée), posée à même un médaillon (porte du chapiteau).
    public static Material Engraving(int type)
    {
        var mat = new Material(Shader.Find("Sprites/Default")) { name = "Gravure" + type, color = new Color(0.72f, 0.68f, 0.6f) };
        mat.mainTexture = Mask(type);
        return mat;
    }

    // Pierre sombre gravée du glyphe en os pâle ; l'émission (bleue quand le glyphe est chargé) suit la gravure.
    public static Material Material(int type)
    {
        if (materials.TryGetValue(type, out var mat) && mat != null) return mat;
        const int size = 128;
        Texture2D mask = Mask(type);
        var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "GlypheAlbedo" + type };
        var emission = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "GlypheEmission" + type };
        Color[] m = mask.GetPixels();
        var a = new Color[m.Length];
        var e = new Color[m.Length];
        for (int i = 0; i < m.Length; i++)
        {
            int x = i % size, y = i / size;
            float stone = 0.13f + 0.07f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
            a[i] = Color.Lerp(new Color(stone, stone, stone * 1.05f), new Color(0.78f, 0.74f, 0.66f) * m[i].r, m[i].a);
            a[i].a = 1f;
            e[i] = new Color(m[i].a, m[i].a, m[i].a, 1f);
        }
        albedo.SetPixels(a);
        albedo.Apply();
        emission.SetPixels(e);
        emission.Apply();
        mat = new Material(GameArt.Lit("GlyphFaceTemplate", Color.white, null, 0.25f)) { name = "Glyphe" + type };
        mat.mainTexture = albedo;
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetTexture("_EmissionMap", emission);
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

        // Battant rouge du chapiteau, rayé de crème, cerclé d'or ; les glyphes gravés à même des médaillons sombres (V0.6.1 :
        // plus de carré de pierre autour de la gravure).
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
        Material plate = GameArt.Lit("GlyphStone", new Color(0.16f, 0.16f, 0.17f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f);
        for (int k = 0; k < count; k++)
        {
            int row = k / 3, col = k % 3, inRow = Mathf.Min(3, count - row * 3);
            var at = new Vector3((col - (inRow - 1) * 0.5f) * cell, height * 0.55f + ((rows - 1) * 0.5f - row) * cell, 0.085f);
            GameArt.Prim(PrimitiveType.Cylinder, door.slab, at, new Vector3(cell * 0.85f, 0.01f, cell * 0.85f), plate).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
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
        // Gravure couleur d'os pâle, bleue et lumineuse quand son glyphe est chargé.
        icons[index].material.color = on ? new Color(0.45f, 0.8f, 1f) : new Color(0.72f, 0.68f, 0.6f);
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
