using System.Collections.Generic;
using UnityEngine;

// Bornes d'arcade (GDD 10.2, V0.6.1) : Roulette, Machine à pince et Arcade Dance (modèles Meshy, Resources/Art).
// Posées par la génération du terrain à partir de la Zone 3 (et une de chaque dans le Hub 1 en godmod). Ternes tant
// qu'aucune Balise chargée ne les voit ; chargées, leurs couleurs s'avivent et une petite musique de foire s'en échappe
// (10 m). Un Jeton WOOF inséré les active : l'hôte décide des tirages et des lots ; le joueur qui joue pilote la pince ou
// danse chez lui, et n'envoie que le résultat.
public enum ArcadeKind : byte { Roulette = 0, Claw = 1, Dance = 2 }

public class ArcadeMachine : Interactable
{
    public const float Height = 2f;
    // Roulette : lot de chaque section, dans l'ordre de la roue (0 : ticket 50, 1 : 100, 2 : 200, 3 : 500, 4 : Réveil) ;
    // 9 tickets 50, 5 tickets 100, 3 tickets 200, 1 ticket 500 et 2 Réveils.
    public static readonly int[] Sections = { 0, 1, 0, 2, 0, 4, 0, 1, 0, 3, 0, 1, 0, 2, 0, 4, 1, 0, 2, 1 };
    static readonly Color[] SectionColors =
    {
        new Color(0.2f, 0.75f, 0.3f), new Color(0.2f, 0.45f, 0.95f), new Color(0.6f, 0.25f, 0.9f), new Color(1f, 0.78f, 0.15f), new Color(0.12f, 0.1f, 0.1f),
    };
    static readonly ItemType[] Tickets = { ItemType.Ticket50, ItemType.Ticket100, ItemType.Ticket200, ItemType.Ticket500 };
    // Section tirée : un ticket, ou ItemType.Reveil (le Réveil apparaît et poursuit le joueur).
    public static ItemType SectionPrize(int section) =>
        Sections[Mathf.Clamp(section, 0, Sections.Length - 1)] is int t && t < Tickets.Length ? Tickets[t] : ItemType.Reveil;

    public static readonly ItemType[] Plushes = { ItemType.PelucheLapin, ItemType.PeluchePingouin, ItemType.PelucheSinge, ItemType.PelucheChat };

    public int Id { get; private set; }
    public ArcadeKind Kind { get; private set; }
    public int Zone { get; private set; }
    public bool Charged { get; private set; }
    public string User { get; private set; } = "";
    public bool Busy => User.Length > 0;
    // Point d'attache du rayon d'énergie (haut de la borne) et emplacement du lot gagné (à terre, devant elle).
    public Vector3 Anchor => transform.TransformPoint(new Vector3(0f, Height * 0.93f, 0.15f));
    public Vector3 PrizeSpot => transform.TransformPoint(Kind == ArcadeKind.Dance ? new Vector3(1.05f, 0.02f, 0.75f) : new Vector3(0.35f, 0.02f, 1.05f));
    public string KindName => Kind == ArcadeKind.Roulette ? "Roulette" : Kind == ArcadeKind.Claw ? "Machine à pince" : "Arcade Dance";

    Transform visual;
    Renderer[] bodies;
    MaterialPropertyBlock block;
    Light glow;
    AudioSource music;
    float musicPitch = 1f;

    public static ArcadeMachine Create(Transform parent, int id, ArcadeKind kind, int zone, Vector3 pos, float yaw)
    {
        var go = new GameObject("Borne_" + kind + "_" + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var m = go.AddComponent<ArcadeMachine>();
        m.Id = id;
        m.Kind = kind;
        m.Zone = zone;
        m.visual = new GameObject("Visuel").transform;
        m.visual.SetParent(go.transform, false);
        // L'avant des modèles Meshy (+Z) fait face aux joueurs.
        if (kind == ArcadeKind.Roulette) m.BuildRoulette();
        else if (kind == ArcadeKind.Claw) m.BuildClaw();
        else m.BuildDance();
        // La teinte terne ne s'applique qu'au modèle Meshy (les pièces ajoutées gardent leur couleur et gèrent leur éclat).
        var tinted = new List<Renderer>();
        foreach (var r in (m.modelRoot != null ? m.modelRoot : m.visual).GetComponentsInChildren<Renderer>())
            if (!m.dynamicParts.Exists(d => d != null && r.transform.IsChildOf(d))) tinted.Add(r);
        m.bodies = tinted.ToArray();
        m.block = new MaterialPropertyBlock();

        m.glow = new GameObject("Lueur").AddComponent<Light>();
        m.glow.transform.SetParent(go.transform, false);
        m.glow.transform.localPosition = new Vector3(0f, 1.6f, 0.9f);
        m.glow.type = LightType.Point;
        m.glow.color = kind == ArcadeKind.Roulette ? new Color(1f, 0.75f, 0.35f) : kind == ArcadeKind.Claw ? new Color(1f, 0.45f, 0.45f) : new Color(0.9f, 0.4f, 1f);
        m.glow.range = 4.5f;
        m.glow.intensity = 1.3f;
        m.glow.shadows = LightShadows.None;

        // Caisse solide (on ne traverse pas la borne) ; la sélection l'englobe.
        Bounds b = GameArt.LocalBounds(m.visual);
        var solid = go.AddComponent<BoxCollider>();
        solid.center = new Vector3(0f, Height * 0.5f, b.center.z);
        solid.size = new Vector3(b.size.x, Height, b.size.z);
        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, Height * 0.5f, b.center.z + 0.1f);
        trigger.size = new Vector3(b.size.x + 0.2f, Height, b.size.z + 0.4f);
        m.SetCharged(false);
        return m;
    }

    // Chargée : couleurs vives, lueur et musique de foire (10 m) ; déchargée : couleurs ternes, silence.
    public void SetCharged(bool on)
    {
        Charged = on;
        block.SetColor("_Color", on ? Color.white : new Color(0.38f, 0.38f, 0.42f));
        foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
        glow.enabled = on;
        if (screenBg != null) screenBg.enabled = on;
        if (on && music == null)
        {
            music = GameAudio.Loop3D("arcade_music", transform, new Vector3(0f, 1.3f, 0.3f), 0.5f, GameRules.ArcadeMusicRange, true);
            if (music != null) { music.time = Random.Range(0f, 4f); music.Play(); }
        }
        else if (!on && music != null)
        {
            Destroy(music.gameObject);
            music = null;
        }
        if (Kind == ArcadeKind.Dance) RefreshPads();
    }

    // Une partie commence ('user' : joueur qui joue ; 'data' : section de la Roulette ; 'elapsed' : secondes déjà écoulées).
    public void StartSession(string user, int data, float elapsed)
    {
        User = user ?? "";
        if (Kind == ArcadeKind.Roulette) StartSpin(data, elapsed);
        else if (Kind == ArcadeKind.Dance) { danceShown = -1; ShowDance(-1, 0, 0); }
    }

    public void EndSession()
    {
        User = "";
        musicPitch = 1f;
        // L'écran garde le résultat de la danse quelques secondes.
        if (Kind == ArcadeKind.Dance) { dancing = false; if (Time.time - danceEndAt > 3f) ShowDance(-1, 0, 0); }
        if (Kind == ArcadeKind.Claw && clawPhase != ClawPhase.Idle && !clawLocal) clawPhase = ClawPhase.Idle;
    }

    void Update()
    {
        if (music != null) music.pitch = Mathf.MoveTowards(music.pitch, musicPitch, Time.deltaTime * 0.6f);
        if (Kind == ArcadeKind.Roulette) UpdateRoulette();
        else if (Kind == ArcadeKind.Claw) UpdateClawVisual();
        else UpdatePads();
    }

    public override string Prompt
    {
        get
        {
            // Seul texte d'une borne : "Utiliser", Jeton WOOF sélectionné ; sinon, aucun.
            var session = GameSession.Instance;
            return session != null && session.SelectedItem == ItemType.JetonWoof ? Loc.T("Utiliser") : "";
        }
    }

    public override void Interact() => GameSession.Instance.UseArcade(this);

    readonly List<Transform> dynamicParts = new List<Transform>();
    Transform modelRoot;

    GameObject SpawnBody(string model)
    {
        var holder = GameArt.SpawnModel(model, visual, Height);
        if (holder != null) modelRoot = holder.transform;
        return holder;
    }

    static Material glass;
    static Material Glass()
    {
        if (glass != null) return glass;
        glass = new Material(GameArt.Translucent(false)) { name = "VitreArcade", color = new Color(0.8f, 0.92f, 1f, 0.13f) };
        return glass;
    }

    static Material Unlit(string name, Color color, Texture tex = null)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { name = name, color = color };
        if (tex != null) m.mainTexture = tex;
        return m;
    }

    // ================= Roulette =================

    // Roue de la borne Meshy (repère de la borne de 2 m) : centre, rayon de la face peinte, et pointeur au-dessus.
    const float WheelY = 1.39f, WheelZ = 0.215f, WheelRadius = 0.415f;
    Transform wheel;
    Material wheelMat;
    float spinStart = -1f, spinFrom, spinTo, wheelAngle, nextSpark;

    void BuildRoulette()
    {
        if (SpawnBody("Roulette") == null)
        {
            Material wood = GameArt.Lit("RouletteBois", new Color(0.45f, 0.08f, 0.08f), null, 0.4f);
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, 0.45f, 0f), new Vector3(0.4f, 0.9f, 0.3f), wood);
            GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, WheelY, 0.12f), new Vector3(0.95f, 0.05f, 0.95f), GameArt.Lit("RouletteOr", new Color(0.8f, 0.62f, 0.2f), null, 0.6f))
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        wheel = new GameObject("Roue").transform;
        wheel.SetParent(visual, false);
        wheel.localPosition = new Vector3(0f, WheelY, WheelZ);
        wheelMat = Unlit("RoueRoulette", Color.white);
        var disc = new GameObject("Secteurs", typeof(MeshFilter), typeof(MeshRenderer));
        disc.transform.SetParent(wheel, false);
        disc.GetComponent<MeshFilter>().sharedMesh = WheelMesh();
        disc.GetComponent<MeshRenderer>().sharedMaterial = wheelMat;
        var hub = GameArt.Prim(PrimitiveType.Sphere, wheel, new Vector3(0f, 0f, 0.02f), new Vector3(0.09f, 0.09f, 0.05f), GameArt.Lit("MoyeuRoulette", new Color(0.85f, 0.65f, 0.2f), null, 0.8f));
        foreach (var c in wheel.GetComponentsInChildren<Collider>()) Destroy(c);
        dynamicParts.Add(wheel);
    }

    // Disque des 20 sections (couleur par sommet), visible des deux côtés ; la section s est centrée sur l'angle s x 18 + 9
    // (sens horaire depuis le haut, en regardant le repère local).
    static Mesh wheelMesh;
    static Mesh WheelMesh()
    {
        if (wheelMesh != null) return wheelMesh;
        const int sub = 6;
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        // Fond doré sous les sections (les liserés), dessiné en premier : le matériau n'écrit pas la profondeur.
        int bg = verts.Count;
        verts.Add(new Vector3(0f, 0f, -0.002f));
        colors.Add(new Color(0.85f, 0.65f, 0.2f));
        for (int k = 0; k <= 60; k++)
        {
            float a = k / 60f * Mathf.PI * 2f;
            verts.Add(new Vector3(Mathf.Sin(a), Mathf.Cos(a), -0.002f / WheelRadius) * WheelRadius);
            colors.Add(new Color(0.85f, 0.65f, 0.2f));
        }
        for (int k = 0; k < 60; k++)
        {
            tris.Add(bg); tris.Add(bg + 1 + k); tris.Add(bg + 2 + k);
            tris.Add(bg); tris.Add(bg + 2 + k); tris.Add(bg + 1 + k);
        }
        for (int s = 0; s < Sections.Length; s++)
        {
            Color c = SectionColors[Sections[s]];
            Color inner = Color.Lerp(c, Color.black, 0.25f);
            int center = verts.Count;
            verts.Add(Vector3.zero);
            colors.Add(inner);
            for (int k = 0; k <= sub; k++)
            {
                // Fin liseré doré entre deux sections.
                float t = (float)k / sub;
                float a = (s * 18f + Mathf.Lerp(0.6f, 17.4f, t)) * Mathf.Deg2Rad;
                verts.Add(new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * WheelRadius);
                colors.Add(c);
            }
            for (int k = 0; k < sub; k++)
            {
                tris.Add(center); tris.Add(center + 1 + k); tris.Add(center + 2 + k);
                tris.Add(center); tris.Add(center + 2 + k); tris.Add(center + 1 + k);
            }
        }
        wheelMesh = new Mesh { name = "RoueRoulette" };
        wheelMesh.SetVertices(verts);
        wheelMesh.SetColors(colors);
        wheelMesh.SetTriangles(tris, 0);
        wheelMesh.RecalculateBounds();
        return wheelMesh;
    }

    // La roue tourne 7 secondes et s'arrête sur la section tirée (sous le pointeur, en haut).
    void StartSpin(int section, float elapsed)
    {
        spinFrom = Mathf.Repeat(wheelAngle, 360f);
        float target = section * 18f + 9f;
        spinTo = target + 360f * 6f;
        spinStart = Time.time - Mathf.Max(0f, elapsed);
        nextSpark = 0f;
        GameAudio.Play3D("roulette_start", transform.position + Vector3.up * WheelY, 0.8f, 15f, 0.05f);
    }

    void UpdateRoulette()
    {
        if (wheel == null) return;
        wheelMat.color = Charged ? Color.white : new Color(0.42f, 0.42f, 0.45f);
        if (spinStart < 0f) return;
        float t = Mathf.Clamp01((Time.time - spinStart) / GameRules.RouletteSpin);
        float ease = 1f - Mathf.Pow(1f - t, 3f);
        wheelAngle = Mathf.Lerp(spinFrom, spinTo, ease);
        wheel.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);
        // La musique s'accélère, la roue (et la borne) tremble, des particules multicolores jaillissent du pointeur.
        musicPitch = t < 1f ? 1.15f + 0.3f * t : 1f;
        visual.localPosition = t < 1f ? Random.insideUnitSphere * 0.006f * (1f - t * 0.5f) : Vector3.zero;
        if (t < 1f && Time.time >= nextSpark)
        {
            nextSpark = Time.time + 0.12f;
            Vector3 tip = transform.TransformPoint(new Vector3(0f, WheelY + WheelRadius + 0.05f, WheelZ + 0.04f));
            Color c = Color.HSVToRGB(Random.value, 0.8f, 1f);
            Fx.Burst(tip, GameArt.Emissive("EtincelleRoulette" + Mathf.RoundToInt(c.r * 4f) + Mathf.RoundToInt(c.g * 4f) + Mathf.RoundToInt(c.b * 4f), c, c),
                4, 1.6f, 0.035f, 0.6f, 0.8f);
        }
        if (t >= 1f)
        {
            spinStart = -1f;
            GameAudio.Play3D("roulette_stop", transform.position + Vector3.up * WheelY, 0.9f, 15f, 0f);
        }
    }

    // ================= Machine à pince =================

    // Vitrine de la borne Meshy (repère de 2 m) : le maillage est coupé entre le haut du meuble et le toit, remplacé par
    // des montants chromés, des vitres, le plateau aux peluches, le trou et la pince.
    const float BedY = 0.8f, RoofY = 1.69f, ClawTopY = 1.58f, ClawBottomY = 1.0f;
    const float BoxMinX = -0.42f, BoxMaxX = 0.42f, BoxMinZ = -0.44f, BoxMaxZ = 0.3f;
    static readonly Vector2 Hole = new Vector2(0.27f, 0.13f);
    const float HoleSize = 0.24f, GrabRadius = 0.13f;
    // Emplacements des 5 peluches sur le plateau (à l'écart du trou).
    static readonly Vector2[] Slots = { new Vector2(-0.25f, -0.28f), new Vector2(0.02f, -0.3f), new Vector2(0.27f, -0.25f), new Vector2(-0.22f, 0.08f), new Vector2(0.0f, -0.02f) };

    Transform clawCarriage, clawHead, clawCable;
    readonly Transform[] prongs = new Transform[3];
    readonly int[] contents = { -1, -1, -1, -1, -1 };
    readonly Transform[] plushViews = new Transform[5];

    public enum ClawPhase { Idle, Move, Down, Up, ToHole, Open }
    ClawPhase clawPhase = ClawPhase.Idle;
    bool clawLocal;
    Vector2 clawXZ;
    float clawDrop, clawTimer, prongOpen = 1f;
    int grabbed = -1;
    Transform falling;
    float fallTimer;

    public bool HasPlush
    {
        get
        {
            foreach (int c in contents) if (c >= 0) return true;
            return false;
        }
    }

    public int PlushAt(int slot) => slot >= 0 && slot < contents.Length ? contents[slot] : -1;

    static readonly Dictionary<Mesh, Mesh> cutMeshes = new Dictionary<Mesh, Mesh>();

    void BuildClaw()
    {
        var model = SpawnBody("MachinePince");
        if (model != null)
        {
            // Vitrine pleine chez Meshy : seuls le meuble du bas et le toit sont gardés.
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                Mesh src = mf.sharedMesh;
                if (src == null) continue;
                if (!cutMeshes.TryGetValue(src, out Mesh cut) || cut == null)
                {
                    Vector3[] v = src.vertices;
                    int[] t = src.triangles;
                    var keep = new List<int>();
                    Matrix4x4 toVisual = visual.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var y = new float[v.Length];
                    for (int i = 0; i < v.Length; i++) y[i] = toVisual.MultiplyPoint3x4(v[i]).y;
                    for (int k = 0; k + 2 < t.Length; k += 3)
                    {
                        float a = y[t[k]], b = y[t[k + 1]], c = y[t[k + 2]];
                        if (a < BedY - 0.01f && b < BedY - 0.01f && c < BedY - 0.01f || a > RoofY && b > RoofY && c > RoofY)
                        {
                            keep.Add(t[k]); keep.Add(t[k + 1]); keep.Add(t[k + 2]);
                        }
                    }
                    cut = Instantiate(src);
                    cut.name = src.name + "_Vitrine";
                    cut.SetTriangles(keep, 0);
                    cutMeshes[src] = cut;
                }
                mf.sharedMesh = cut;
            }
        }
        else
        {
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, BedY * 0.5f, -0.07f), new Vector3(0.95f, BedY, 0.95f), GameArt.Lit("PinceMeuble", new Color(0.9f, 0.9f, 0.88f), null, 0.5f));
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, (RoofY + Height) * 0.5f, -0.07f), new Vector3(1f, Height - RoofY, 1f), GameArt.Lit("PinceToit", new Color(0.8f, 0.1f, 0.1f), null, 0.5f));
        }
        // Plateau de feutre, toit intérieur, montants chromés et vitres.
        GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, BedY - 0.01f, (BoxMinZ + BoxMaxZ) * 0.5f), new Vector3(BoxMaxX - BoxMinX + 0.08f, 0.04f, BoxMaxZ - BoxMinZ + 0.08f),
            GameArt.Lit("PinceFeutre", new Color(0.35f, 0.05f, 0.08f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f));
        GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, RoofY + 0.005f, (BoxMinZ + BoxMaxZ) * 0.5f), new Vector3(BoxMaxX - BoxMinX + 0.1f, 0.03f, BoxMaxZ - BoxMinZ + 0.1f),
            GameArt.Lit("PinceToitInterieur", new Color(0.92f, 0.9f, 0.88f), null, 0.4f));
        Material chrome = GameArt.Lit("Chrome", new Color(0.82f, 0.84f, 0.88f), null, 0.92f);
        float h = RoofY - BedY;
        foreach (float x in new[] { BoxMinX - 0.04f, BoxMaxX + 0.04f })
            foreach (float z in new[] { BoxMinZ - 0.04f, BoxMaxZ + 0.04f })
                GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(x, BedY + h * 0.5f, z), new Vector3(0.035f, h * 0.5f, 0.035f), chrome);
        float midZ = (BoxMinZ + BoxMaxZ) * 0.5f, depth = BoxMaxZ - BoxMinZ + 0.08f, width = BoxMaxX - BoxMinX + 0.08f;
        AddGlass(new Vector3(0f, BedY + h * 0.5f, BoxMaxZ + 0.04f), Quaternion.Euler(0f, 180f, 0f), new Vector2(width, h));
        AddGlass(new Vector3(0f, BedY + h * 0.5f, BoxMinZ - 0.04f), Quaternion.identity, new Vector2(width, h));
        AddGlass(new Vector3(BoxMinX - 0.04f, BedY + h * 0.5f, midZ), Quaternion.Euler(0f, 90f, 0f), new Vector2(depth, h));
        AddGlass(new Vector3(BoxMaxX + 0.04f, BedY + h * 0.5f, midZ), Quaternion.Euler(0f, -90f, 0f), new Vector2(depth, h));
        // Le trou : un puits sombre cerclé de plexiglas.
        GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(Hole.x, BedY + 0.012f, Hole.y), new Vector3(HoleSize, 0.005f, HoleSize), GameArt.Unlit("PinceTrou", new Color(0.02f, 0.02f, 0.02f)));
        foreach (var (o, s) in new[] { (new Vector3(-HoleSize * 0.5f, 0f, 0f), new Vector3(0.01f, 0.1f, HoleSize)), (new Vector3(0f, 0f, -HoleSize * 0.5f), new Vector3(HoleSize, 0.1f, 0.01f)) })
            dynamicParts.Add(GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(Hole.x, BedY + 0.06f, Hole.y) + o, s, Glass()).transform);

        // Pince : chariot sous le toit, câble, tête et trois griffes.
        Material steel = GameArt.Lit("PinceAcier", new Color(0.7f, 0.72f, 0.75f), null, 0.85f);
        clawCarriage = new GameObject("Chariot").transform;
        clawCarriage.SetParent(visual, false);
        GameArt.Prim(PrimitiveType.Cube, clawCarriage, Vector3.zero, new Vector3(0.1f, 0.05f, 0.1f), steel);
        clawCable = GameArt.Prim(PrimitiveType.Cylinder, clawCarriage, Vector3.zero, new Vector3(0.008f, 0.5f, 0.008f), GameArt.Lit("PinceCable", new Color(0.15f, 0.15f, 0.15f), null, 0.3f)).transform;
        clawHead = new GameObject("Tete").transform;
        clawHead.SetParent(clawCarriage, false);
        GameArt.Prim(PrimitiveType.Cylinder, clawHead, Vector3.zero, new Vector3(0.06f, 0.025f, 0.06f), steel);
        for (int k = 0; k < 3; k++)
        {
            var pivot = new GameObject("Griffe" + k).transform;
            pivot.SetParent(clawHead, false);
            pivot.localRotation = Quaternion.Euler(0f, k * 120f, 0f);
            var arm = new GameObject("Bras").transform;
            arm.SetParent(pivot, false);
            arm.localPosition = new Vector3(0f, -0.01f, 0.025f);
            GameArt.Prim(PrimitiveType.Cube, arm, new Vector3(0f, -0.055f, 0f), new Vector3(0.012f, 0.11f, 0.012f), steel);
            GameArt.Prim(PrimitiveType.Cube, arm, new Vector3(0f, -0.11f, -0.018f), new Vector3(0.012f, 0.012f, 0.04f), steel);
            prongs[k] = arm;
        }
        foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
        dynamicParts.Add(clawCarriage);
        clawXZ = new Vector2(0f, -0.1f);
        PlaceClaw(0f);
    }

    void AddGlass(Vector3 pos, Quaternion rot, Vector2 size)
    {
        var q = GameArt.Prim(PrimitiveType.Quad, visual, pos, new Vector3(size.x, size.y, 1f), Glass());
        q.transform.localRotation = rot;
        dynamicParts.Add(q.transform);
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Peluches de la machine (types 0 à 3 par emplacement, -1 : vide), envoyées par l'hôte.
    public void SetContents(int[] types)
    {
        for (int i = 0; i < contents.Length; i++)
        {
            int t = types != null && i < types.Length ? types[i] : -1;
            if (contents[i] == t && (t < 0) == (plushViews[i] == null)) continue;
            contents[i] = t;
            if (plushViews[i] != null && plushViews[i] != falling) Destroy(plushViews[i].gameObject);
            plushViews[i] = null;
            if (t < 0) continue;
            var holder = new GameObject("Peluche" + i).transform;
            holder.SetParent(visual, false);
            holder.localPosition = new Vector3(Slots[i].x, BedY + 0.01f, Slots[i].y);
            holder.localRotation = Quaternion.Euler(0f, (Id * 53 + i * 71) % 360 - 180f, 0f);
            if (GameArt.SpawnModel(Items.ModelName(Plushes[t]), holder, 0.2f) == null)
                GameArt.Prim(PrimitiveType.Sphere, holder, new Vector3(0f, 0.09f, 0f), Vector3.one * 0.17f, GameArt.Lit("PelucheRepli", Items.Get(Plushes[t]).color));
            foreach (var c in holder.GetComponentsInChildren<Collider>()) Destroy(c);
            plushViews[i] = holder;
        }
    }

    public int[] Contents => (int[])contents.Clone();

    // Joueur local : la pince est à lui (déplacement, descente).
    public void BeginClaw()
    {
        clawLocal = true;
        clawPhase = ClawPhase.Move;
        clawXZ = new Vector2(0f, -0.1f);
        clawDrop = 0f;
        prongOpen = 1f;
        grabbed = -1;
        musicPitch = 1.1f;
    }

    // Vue fixe de la caméra pendant la partie : devant et au-dessus de la vitrine.
    public Pose ClawView
    {
        get
        {
            Vector3 eye = transform.TransformPoint(new Vector3(0f, 1.95f, 1.35f));
            Vector3 look = transform.TransformPoint(new Vector3(0f, 1.2f, -0.12f));
            return new Pose(eye, Quaternion.LookRotation(look - eye));
        }
    }

    // Une image de la partie du joueur local. 'move' : déplacement voulu (droite, avant) vu de la caméra ; 'drop' : Espace.
    // Retourne vrai quand la pince s'est ouverte au-dessus du trou ; 'slot' : emplacement de la peluche gagnée (-1 : aucune).
    public bool UpdateClaw(float dt, Vector2 move, bool drop, out int slot)
    {
        slot = -1;
        if (clawPhase == ClawPhase.Move)
        {
            // La caméra regarde la borne de face : sa droite est le -X de la borne, son avant le -Z.
            clawXZ += new Vector2(-move.x, -move.y) * 0.4f * dt;
            ClampClaw();
            if (drop)
            {
                clawPhase = ClawPhase.Down;
                GameAudio.Play3D("claw_down", ClawWorld(), 0.7f, 12f, 0.05f);
            }
        }
        StepClaw(dt);
        if (clawPhase == ClawPhase.Idle && clawLocal)
        {
            clawLocal = false;
            slot = grabbed;
            musicPitch = 1f;
            return true;
        }
        return false;
    }

    // Un autre joueur joue : sa pince descend à cet endroit, remonte et s'ouvre au-dessus du trou.
    public void PlayRemoteDrop(Vector2 xz)
    {
        if (clawLocal) return;
        clawXZ = xz;
        ClampClaw();
        clawPhase = ClawPhase.Down;
        grabbed = -1;
        GameAudio.Play3D("claw_down", ClawWorld(), 0.7f, 12f, 0.05f);
    }

    public Vector2 ClawPosition => clawXZ;

    void ClampClaw()
    {
        clawXZ.x = Mathf.Clamp(clawXZ.x, BoxMinX + 0.06f, BoxMaxX - 0.06f);
        clawXZ.y = Mathf.Clamp(clawXZ.y, BoxMinZ + 0.06f, BoxMaxZ - 0.06f);
    }

    Vector3 ClawWorld() => transform.TransformPoint(new Vector3(clawXZ.x, ClawTopY, clawXZ.y));

    void StepClaw(float dt)
    {
        switch (clawPhase)
        {
            case ClawPhase.Down:
                clawDrop = Mathf.MoveTowards(clawDrop, 1f, dt / 1.1f);
                if (clawDrop >= 1f)
                {
                    // En bas : la pince se referme ; une peluche sous elle est saisie.
                    prongOpen = 0f;
                    if (clawLocal)
                    {
                        float best = GrabRadius;
                        for (int i = 0; i < contents.Length; i++)
                        {
                            if (contents[i] < 0 || plushViews[i] == null) continue;
                            float d = Vector2.Distance(clawXZ, Slots[i]);
                            if (d < best) { best = d; grabbed = i; }
                        }
                    }
                    clawPhase = ClawPhase.Up;
                    clawTimer = 0.35f;
                }
                break;
            case ClawPhase.Up:
                if ((clawTimer -= dt) > 0f) break;
                clawDrop = Mathf.MoveTowards(clawDrop, 0f, dt / 1.1f);
                if (clawDrop <= 0f) clawPhase = ClawPhase.ToHole;
                break;
            case ClawPhase.ToHole:
                clawXZ = Vector2.MoveTowards(clawXZ, Hole, 0.35f * dt);
                if (Vector2.Distance(clawXZ, Hole) < 0.001f)
                {
                    clawPhase = ClawPhase.Open;
                    clawTimer = 0.8f;
                    prongOpen = 1f;
                    GameAudio.Play3D("claw_open", ClawWorld(), 0.6f, 12f, 0.05f);
                    if (grabbed >= 0 && plushViews[grabbed] != null)
                    {
                        falling = plushViews[grabbed];
                        fallTimer = 0f;
                    }
                }
                break;
            case ClawPhase.Open:
                if ((clawTimer -= dt) <= 0f) clawPhase = ClawPhase.Idle;
                break;
        }
        PlaceClaw(dt);
    }

    void UpdateClawVisual()
    {
        if (!clawLocal && clawPhase != ClawPhase.Idle && clawPhase != ClawPhase.Move) StepClaw(Time.deltaTime);
        if (falling != null)
        {
            // La peluche lâchée tombe dans le trou et disparaît.
            fallTimer += Time.deltaTime;
            falling.localPosition = new Vector3(Hole.x, Mathf.Lerp(ClawBottomY - 0.15f, BedY - 0.35f, Mathf.Clamp01(fallTimer / 0.5f)), Hole.y);
            if (fallTimer >= 0.5f) { falling.gameObject.SetActive(false); falling = null; }
        }
    }

    void PlaceClaw(float dt)
    {
        if (clawCarriage == null) return;
        prongOpenShown = Mathf.MoveTowards(prongOpenShown, prongOpen, dt * 4f);
        float headY = Mathf.Lerp(ClawTopY, ClawBottomY, Mathf.SmoothStep(0f, 1f, clawDrop));
        clawCarriage.localPosition = new Vector3(clawXZ.x, RoofY - 0.03f, clawXZ.y);
        float drop = (RoofY - 0.03f) - headY;
        clawHead.localPosition = Vector3.down * drop;
        clawCable.localPosition = Vector3.down * drop * 0.5f;
        clawCable.localScale = new Vector3(0.008f, drop * 0.5f, 0.008f);
        for (int k = 0; k < prongs.Length; k++) prongs[k].localRotation = Quaternion.Euler(-35f * prongOpenShown + 8f, 0f, 0f);
        // La peluche saisie suit la pince.
        if (grabbed >= 0 && plushViews[grabbed] != null && plushViews[grabbed] != falling && (clawPhase == ClawPhase.Up || clawPhase == ClawPhase.ToHole))
            plushViews[grabbed].localPosition = new Vector3(clawXZ.x, headY - 0.2f, clawXZ.y);
    }

    float prongOpenShown = 1f;

    // ================= Arcade Dance =================

    // Écran de la borne Meshy (repère de 2 m), incliné vers l'arrière ; quatre plaques de pression devant elle.
    const float ScreenY = 1.37f, ScreenZ = 0.115f, ScreenTilt = 13f, PadSize = 0.72f, PadStep = 0.76f, PadCenterZ = 1.8f;
    public static readonly Color[] ArrowColors = { new Color(1f, 0.3f, 0.75f), new Color(0.3f, 0.9f, 1f), new Color(0.45f, 1f, 0.4f), new Color(1f, 0.7f, 0.2f) };
    // Flèches : 0 gauche, 1 bas, 2 haut, 3 droite (vues par le joueur, face à la borne).
    static readonly Vector3[] PadOffsets = { new Vector3(PadStep, 0f, 0f), new Vector3(0f, 0f, PadStep), new Vector3(0f, 0f, -PadStep), new Vector3(-PadStep, 0f, 0f) };
    static readonly float[] PadArrowYaw = { 90f, 0f, 180f, -90f };
    static readonly float[] ScreenArrowRoll = { -90f, 180f, 0f, 90f };

    Renderer screenBg, screenArrow;
    TextMesh screenText;
    readonly Renderer[] pads = new Renderer[4];
    int padLit = -1, padFlash = -1;
    bool padFlashGood;
    float padFlashAt = -10f;
    int danceShown = -1;

    void BuildDance()
    {
        if (SpawnBody("ArcadeDance") == null)
        {
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, Height * 0.5f, 0f), new Vector3(0.8f, Height, 0.85f), GameArt.Lit("DanceMeuble", new Color(0.1f, 0.12f, 0.3f), null, 0.5f));
        }
        var screen = new GameObject("Ecran").transform;
        screen.SetParent(visual, false);
        screen.localPosition = new Vector3(0f, ScreenY, ScreenZ);
        screen.localRotation = Quaternion.Euler(-ScreenTilt, 0f, 0f);
        var bg = GameArt.Prim(PrimitiveType.Quad, screen, Vector3.zero, new Vector3(0.6f, 0.48f, 1f), Unlit("EcranDance", new Color(0.05f, 0.02f, 0.12f)));
        bg.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        screenBg = bg.GetComponent<Renderer>();
        var arrow = GameArt.Prim(PrimitiveType.Quad, screen, new Vector3(0f, 0.03f, 0.004f), Vector3.one * 0.26f, Unlit("FlecheDance", Color.white, ArrowTexture()));
        screenArrow = arrow.GetComponent<Renderer>();
        screenText = GameArt.CreateText(screen, "Score", new Vector3(0f, -0.17f, 0.005f), 0.006f, Color.white);
        screenText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        dynamicParts.Add(screen);

        // Plaques de pression : métal, flèche peinte, éclairées par la partie en cours.
        var padTex = ArrowTexture();
        for (int k = 0; k < 4; k++)
        {
            Vector3 c = new Vector3(0f, 0f, PadCenterZ) + PadOffsets[k];
            var plate = GameArt.Prim(PrimitiveType.Cube, transform, c + Vector3.up * 0.02f, new Vector3(PadSize, 0.04f, PadSize), GameArt.Lit("PlaqueDance", new Color(0.55f, 0.56f, 0.6f), null, 0.75f), true);
            plate.name = "Plaque" + k;
            var arrowPad = GameArt.Prim(PrimitiveType.Quad, plate.transform.parent, c + Vector3.up * 0.042f, Vector3.one * PadSize * 0.78f,
                Unlit("FlechePlaque" + k, ArrowColors[k], padTex));
            arrowPad.transform.localRotation = Quaternion.Euler(0f, PadArrowYaw[k], 0f) * Quaternion.Euler(90f, 0f, 0f);
            pads[k] = arrowPad.GetComponent<Renderer>();
        }
        ShowDance(-1, 0, 0);
    }

    // Flèche pleine pointant vers le haut, en blanc sur transparent.
    static Texture2D arrowTex;
    static Texture2D ArrowTexture()
    {
        if (arrowTex != null) return arrowTex;
        const int size = 128;
        arrowTex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FlecheArcade" };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size;
                bool head = v > 0.5f && v < 0.92f && Mathf.Abs(u) < (0.92f - v) * 1.05f;
                bool shaft = v >= 0.1f && v <= 0.52f && Mathf.Abs(u) < 0.15f;
                px[y * size + x] = new Color(1f, 1f, 1f, head || shaft ? 1f : 0f);
            }
        arrowTex.SetPixels(px);
        arrowTex.Apply();
        return arrowTex;
    }

    // Plaque sous les pieds d'un joueur (-1 : aucune).
    public int PadAt(Vector3 feet)
    {
        Vector3 local = transform.InverseTransformPoint(feet);
        if (local.y < -0.5f || local.y > 1f) return -1;
        for (int k = 0; k < 4; k++)
        {
            Vector3 c = new Vector3(0f, 0f, PadCenterZ) + PadOffsets[k];
            if (Mathf.Abs(local.x - c.x) <= PadSize * 0.5f && Mathf.Abs(local.z - c.z) <= PadSize * 0.5f) return k;
        }
        return -1;
    }

    // Écran (et plaques) : flèche demandée (-1 : aucune), numéro de la flèche (1 à 8), erreurs.
    public void ShowDance(int arrow, int step, int errors)
    {
        if (screenArrow == null) return;
        danceShown = arrow;
        screenArrow.enabled = arrow >= 0 && Charged;
        if (arrow >= 0)
        {
            screenArrow.transform.localRotation = Quaternion.AngleAxis(ScreenArrowRoll[arrow], Vector3.forward) * Quaternion.Euler(0f, 180f, 0f);
            screenArrow.sharedMaterial.color = ArrowColors[arrow];
        }
        screenText.text = !Charged ? "" : step > 0 ? step + " / " + GameRules.DanceArrows + (errors > 0 ? "   X" + errors : "") : Busy ? "" : "WOOF";
        padLit = arrow;
        RefreshPads();
    }

    // Réussite ou erreur sur une plaque : elle s'allume en vert ou en rouge un court instant.
    public void FlashPad(int pad, bool good)
    {
        padFlash = pad;
        padFlashGood = good;
        padFlashAt = Time.time;
        GameAudio.Play3D(good ? "dance_hit" : "error", transform.position + Vector3.up, good ? 0.7f : 0.5f, 12f, 0.03f);
    }

    float danceEndAt = -10f;

    public void ShowDanceEnd(int errors)
    {
        if (screenText == null) return;
        danceEndAt = Time.time;
        screenArrow.enabled = false;
        padLit = -1;
        screenText.text = errors == 0 ? Loc.T("PARFAIT !") : errors == 1 ? Loc.T("BRAVO !") : errors == 2 ? Loc.T("PAS MAL !") : Loc.T("PERDU...");
    }

    void RefreshPads()
    {
        for (int k = 0; k < pads.Length; k++)
        {
            if (pads[k] == null) continue;
            // Flèche peinte (non éclairée) : terne déchargée, vive chargée, clignotante quand c'est elle qu'il faut viser.
            Color c = ArrowColors[k];
            Color shown = !Charged ? c * 0.3f : k == padLit ? Color.Lerp(c, Color.white, Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.6f) : c * 0.55f;
            if (k == padFlash && Time.time - padFlashAt < 0.4f) shown = padFlashGood ? new Color(0.35f, 1f, 0.4f) : new Color(1f, 0.2f, 0.2f);
            shown.a = 1f;
            pads[k].sharedMaterial.color = shown;
        }
    }

    void UpdatePads()
    {
        if (padLit >= 0 || Time.time - padFlashAt < 0.5f) RefreshPads();
        if (!Busy && danceEndAt > 0f && Time.time - danceEndAt > 3f) { danceEndAt = -10f; ShowDance(-1, 0, 0); }
        musicPitch = Busy ? 1.12f : 1f;
    }

    // ---------- Partie du joueur local ----------

    bool dancing;
    int danceStep, danceArrow, danceErrors, padAtArrow, lastArrow = -1;
    float arrowAt;

    // 'announce' (flèche, numéro, erreurs) : relayé aux autres joueurs pour leur écran.
    public void BeginDance(int padNow)
    {
        dancing = true;
        danceStep = 0;
        danceErrors = 0;
        lastArrow = -1;
        arrowAt = Time.time + 1f;
        danceArrow = -1;
        padAtArrow = padNow;
    }

    // Une image de la danse ; retourne vrai à la fin des 8 flèches (erreurs : 'errors').
    public bool UpdateDance(int padNow, System.Action<int, int, int, int> announce, out int errors)
    {
        errors = danceErrors;
        if (!dancing) return true;
        if (danceArrow < 0)
        {
            if (Time.time < arrowAt) return false;
            NextArrow(padNow, announce);
            return false;
        }
        bool timeout = Time.time - arrowAt >= GameRules.DanceArrowTime;
        bool stepped = padNow >= 0 && padNow != padAtArrow;
        if (!timeout && !stepped) return false;
        bool good = stepped && padNow == danceArrow;
        if (!good) danceErrors++;
        FlashPad(stepped ? padNow : danceArrow, good);
        announce(danceArrow, danceStep, danceErrors, good ? 1 : 0);
        if (danceStep >= GameRules.DanceArrows)
        {
            dancing = false;
            errors = danceErrors;
            ShowDanceEnd(danceErrors);
            return true;
        }
        NextArrow(padNow, announce);
        return false;
    }

    // Nouvelle flèche : différente de la précédente et de la plaque où se trouve le joueur.
    void NextArrow(int padNow, System.Action<int, int, int, int> announce)
    {
        int arrow;
        do arrow = Random.Range(0, 4); while (arrow == lastArrow || arrow == padNow);
        lastArrow = danceArrow = arrow;
        danceStep++;
        padAtArrow = padNow;
        arrowAt = Time.time;
        ShowDance(arrow, danceStep, danceErrors);
        GameAudio.Play3D("dance_arrow", transform.position + Vector3.up * ScreenY, 0.6f, 12f, 0f);
        announce(arrow, danceStep, danceErrors, -1);
    }
}

// Boule à facettes posée (au sol ou au plafond) : elle tourne lentement et projette des faisceaux colorés (argent, or ou
// diamant). La musique disco qu'elle diffuse est gérée par GameSession (une seule à la fois).
public class DiscoBall : MonoBehaviour
{
    Transform ball;
    readonly List<Light> spots = new List<Light>();
    Color[] palette;

    // 'ball' tourne sur lui-même ; les faisceaux partent de 'center'.
    public static DiscoBall Attach(GameObject device, Transform ball, Transform center, DeviceKind kind, bool hanging)
    {
        var d = device.AddComponent<DiscoBall>();
        d.ball = ball;
        d.palette = kind == DeviceKind.DiscoOr ? new[] { new Color(1f, 0.78f, 0.3f), new Color(1f, 0.55f, 0.2f) }
                  : kind == DeviceKind.DiscoDiamant ? new[] { new Color(0.6f, 0.95f, 1f), new Color(1f, 0.6f, 0.95f) }
                  : new[] { new Color(0.92f, 0.94f, 1f), new Color(0.7f, 0.75f, 1f) };
        for (int k = 0; k < 2; k++)
        {
            var spot = new GameObject("Faisceau" + k).AddComponent<Light>();
            spot.transform.SetParent(center, false);
            spot.type = LightType.Spot;
            spot.range = 9f;
            spot.spotAngle = 28f;
            spot.intensity = 2.2f;
            spot.color = d.palette[k];
            spot.shadows = LightShadows.None;
            // Vers le bas et vers l'extérieur au plafond, vers le haut au sol.
            spot.transform.localRotation = Quaternion.Euler(hanging ? 40f : -40f, k * 180f, 0f);
            d.spots.Add(spot);
        }
        return d;
    }

    void Update()
    {
        if (ball == null) return;
        ball.Rotate(0f, 35f * Time.deltaTime, 0f, Space.World);
        float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 5f);
        foreach (var s in spots) s.intensity = 2.2f * k;
    }
}
