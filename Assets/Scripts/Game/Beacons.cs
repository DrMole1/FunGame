using System.Collections.Generic;
using UnityEngine;

// Appareils posés au sol, ramassables, reliés au réseau d'énergie des Balises (GDD 7.1 à 7.5), et objets uniques posés
// de la même manière (7.6 à 7.8, V0.4.7) : la Télévision se relie aux Balises, le Trampoline et la Cloche Décorative non.
public enum DeviceKind : byte { Beacon = 0, Enhanced = 1, Teleporter = 2, Station = 3, Canon = 4, Television = 5, Trampoline = 6, Bell = 7 }

// Balise (GDD 7.1) : petite borne posée au sol, ramassable. La Balise du Hub, plus grande, ne se ramasse pas :
// elle est chargée 10 minutes au début du jeu (jauge circulaire autour d'elle) et se recharge avec un Chargeur.
// Balise Améliorée (7.3) : cristal et aura rouges, coordonnées affichées au-dessus d'elle ; ni Abjuration ni apparition
// d'entité à moins de 5 m. Téléporteur (7.2), Station Curative (7.4) et Canon (7.5) : chargés s'ils voient une Balise
// chargée. Le Canon de cirque tourne avec la visée du joueur qui est dedans, et recule en crachant des confettis au tir.
// Note 03 du chapitre 7 (V0.4.7) : un objet posé au sol joue une légère animation rapide (il s'écrase puis rebondit).
public class Beacon : Interactable
{
    public const float HubHeight = 1.5f, Height = 0.85f, PadHeight = 0.16f, StationHeight = 1.15f;
    // Canon posé au sol : 0,8 fois la taille d'un personnage (1,8 m), soit environ 3 m de long ; fût à 80 % de sa hauteur.
    public const float CanonHeight = 0.8f * 1.8f, CanonBarrel = CanonHeight * 0.8f, CanonHalfLength = 1.5f;
    public const DeviceKind LastKind = DeviceKind.Bell;
    static readonly Color ChargedTint = Color.white, DischargedTint = new Color(0.42f, 0.42f, 0.46f);

    public int Id { get; private set; }   // -1 : Balise du Hub
    public DeviceKind Kind { get; private set; }
    public bool IsHub => Id < 0;
    // Balises (simples ou améliorées) : elles relaient l'énergie ; Téléporteur et Station ne font que la recevoir.
    public bool Relays => Kind == DeviceKind.Beacon || Kind == DeviceKind.Enhanced;
    // Relié au réseau d'énergie (chargé s'il voit une Balise chargée) : tout sauf le Trampoline et la Cloche Décorative.
    public bool Networked => Kind != DeviceKind.Trampoline && Kind != DeviceKind.Bell;
    // Télévision : son écran (sans signal, décompte ou vidéo).
    public TvScreen Screen { get; private set; }
    public bool Charged { get; private set; }
    public ItemType Item => ItemOf(Kind);
    float VisualHeight => IsHub ? HubHeight : HeightOf(Kind);
    // Point d'attache des rayons d'énergie : le cristal (le centre du disque, le haut du tube, le fût du Canon, l'antenne).
    public Vector3 Top => transform.position + Vector3.up * (Kind == DeviceKind.Teleporter ? 0.12f : Kind == DeviceKind.Canon ? CanonBarrel : VisualHeight * 0.86f);

    public static ItemType ItemOf(DeviceKind kind)
    {
        switch (kind)
        {
            case DeviceKind.Enhanced: return ItemType.BaliseAmelioree;
            case DeviceKind.Teleporter: return ItemType.Teleporteur;
            case DeviceKind.Station: return ItemType.StationCurative;
            case DeviceKind.Canon: return ItemType.Canon;
            case DeviceKind.Television: return ItemType.Television;
            case DeviceKind.Trampoline: return ItemType.Trampoline;
            case DeviceKind.Bell: return ItemType.ClocheDecorative;
            default: return ItemType.Balise;
        }
    }

    // Appareil posé par cet objet de l'inventaire (Balise si l'objet ne se pose pas).
    public static DeviceKind KindOf(ItemType item)
    {
        for (var k = DeviceKind.Beacon; k <= LastKind; k++) if (ItemOf(k) == item) return k;
        return DeviceKind.Beacon;
    }

    public static float HeightOf(DeviceKind kind)
    {
        switch (kind)
        {
            case DeviceKind.Teleporter: return PadHeight;
            case DeviceKind.Station: return StationHeight;
            case DeviceKind.Canon: return CanonHeight;
            case DeviceKind.Television: return GameRules.TvHeight;
            case DeviceKind.Trampoline: return GameRules.TrampolineHeight;
            case DeviceKind.Bell: return GameRules.BellHeight;
            default: return Height;
        }
    }
    // Bouche du Canon, d'où part le boulet.
    public Vector3 Muzzle => transform.position + Vector3.up * CanonBarrel;

    Renderer[] bodies;
    GameObject halo;
    Light glow;
    MaterialPropertyBlock block;
    TextMesh coordinates;
    ParticleSystem gas;
    Transform visualRoot;
    float recoil = -1f, placedPop = -1f;

    public static Beacon Create(Transform parent, int id, Vector3 pos, float yaw, DeviceKind kind = DeviceKind.Beacon)
    {
        // (Modèles Meshy des objets uniques, sans socle : Resources/Art/Television, Trampoline et ClocheDecorative.)
        bool hub = id < 0;
        var go = new GameObject(hub ? "Balise du Hub" : kind + " " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var beacon = go.AddComponent<Beacon>();
        beacon.Id = id;
        beacon.Kind = kind;
        float height = beacon.VisualHeight;
        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);
        beacon.visualRoot = visual;
        switch (kind)
        {
            case DeviceKind.Canon:
                // Le fût du modèle Meshy pointe vers +X : on le tourne vers l'avant de l'appareil.
                visual.localRotation = Quaternion.Euler(0f, -90f, 0f);
                if (GameArt.SpawnModel("CanonSolo", visual, height) == null)
                {
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.05f, 0f), new Vector3(1.6f, 0.05f, 1.6f), GameArt.Lit("CanonPiste", new Color(0.5f, 0.06f, 0.06f), null, 0.3f));
                    var barrel = GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0.1f, 0.5f, 0f), new Vector3(0.35f, 0.5f, 0.35f), GameArt.Lit("CanonFut", new Color(0.85f, 0.15f, 0.1f), null, 0.6f));
                    barrel.transform.localRotation = Quaternion.Euler(0f, 0f, -70f);
                }
                break;
            case DeviceKind.Teleporter:
                if (GameArt.SpawnModel("Teleporteur", visual, height) == null)
                {
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.04f, 0.9f), GameArt.Lit("PadBronze", new Color(0.55f, 0.42f, 0.25f), null, 0.6f));
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.09f, 0f), new Vector3(0.6f, 0.03f, 0.6f), GameArt.Emissive("PadCore", new Color(1f, 0.65f, 0.1f), new Color(0.8f, 0.45f, 0.05f)));
                }
                break;
            case DeviceKind.Television:
                bool meshyTv = GameArt.SpawnModel("Television", visual, height) != null;
                if (!meshyTv) BuildTelevision(visual, height);
                beacon.Screen = TvScreen.Create(go.transform, visual, height, meshyTv);
                break;
            case DeviceKind.Trampoline:
                if (GameArt.SpawnModel("Trampoline", visual, height) == null) BuildTrampoline(visual, height);
                break;
            case DeviceKind.Bell:
                if (GameArt.SpawnModel("ClocheDecorative", visual, height) == null) BuildBell(visual, height);
                break;
            case DeviceKind.Station:
                if (GameArt.SpawnModel("StationCurative", visual, height) == null)
                {
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.12f, 0f), new Vector3(0.45f, 0.12f, 0.45f), GameArt.Metal);
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.6f, 0f), new Vector3(0.32f, 0.38f, 0.32f), GameArt.Lit("StationGlass", new Color(0.7f, 0.9f, 0.75f), null, 0.9f));
                }
                beacon.gas = BuildGas(go.transform, height);
                break;
            default:
                if (GameArt.SpawnModel("Balise", visual, height) == null)
                {
                    Material stone = GameArt.Lit("BeaconStone", new Color(0.45f, 0.43f, 0.38f), null, 0.15f);
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, height * 0.35f, 0f), new Vector3(0.22f, height * 0.35f, 0.22f) * (height / Height), stone);
                    GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, height * 0.84f, 0f), new Vector3(0.3f, 0.26f, 0.3f) * (height / Height),
                        kind == DeviceKind.Enhanced ? GameArt.Emissive("BeaconCrystalRed", new Color(1f, 0.2f, 0.15f), new Color(0.9f, 0.1f, 0.05f))
                                                    : GameArt.Emissive("BeaconCrystal", new Color(0.2f, 0.45f, 1f), new Color(0.1f, 0.3f, 0.9f)));
                }
                else if (kind == DeviceKind.Enhanced)
                {
                    // Même modèle, cristal rouge (texture virée au rouge par "Installer les entités V0.4").
                    var red = Resources.Load<Material>("Art/BaliseRouge");
                    if (red != null) foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = red;
                }
                break;
        }

        beacon.bodies = visual.GetComponentsInChildren<Renderer>();
        beacon.block = new MaterialPropertyBlock();
        // Halo autour du cristal quand l'appareil est chargé (rouge pour la Balise Améliorée). Aucun sur le Téléporteur.
        if (kind == DeviceKind.Beacon || kind == DeviceKind.Enhanced || kind == DeviceKind.Station)
        {
            Material aura = kind == DeviceKind.Enhanced ? GameArt.GhostAuraTinted("Red", new Color(1f, 0.2f, 0.15f))
                : kind == DeviceKind.Station ? GameArt.GhostAuraTinted("Green", new Color(0.3f, 1f, 0.4f)) : GameArt.GhostAura;
            float haloSize = kind == DeviceKind.Station ? 0.55f : height * 0.26f;
            beacon.halo = GameArt.Prim(PrimitiveType.Sphere, go.transform, beacon.Top - pos, Vector3.one * haloSize, aura);
            Object.Destroy(beacon.halo.GetComponent<Collider>());
        }
        if (hub)
        {
            beacon.glow = new GameObject("Lueur").AddComponent<Light>();
            beacon.glow.transform.SetParent(go.transform, false);
            beacon.glow.transform.localPosition = new Vector3(0f, height * 0.9f, 0f);
            beacon.glow.type = LightType.Point;
            beacon.glow.color = new Color(0.35f, 0.6f, 1f);
            beacon.glow.range = 4f;
            beacon.glow.intensity = 1.4f;
            beacon.glow.shadows = LightShadows.None;
            // Socle solide : on ne traverse pas la grande Balise.
            var solid = go.AddComponent<CapsuleCollider>();
            solid.center = new Vector3(0f, height * 0.5f, 0f);
            solid.height = height;
            solid.radius = 0.22f;
        }
        if (kind == DeviceKind.Enhanced)
        {
            // Distance jusqu'au Hub, dans l'air au-dessus d'elle (renseignée par BeaconNetwork.Add).
            beacon.coordinates = GameArt.CreateText(go.transform, "DistanceHub", new Vector3(0f, height + 0.45f, 0f), 0.02f, new Color(1f, 0.45f, 0.4f));
        }
        beacon.FitTrigger(kind == DeviceKind.Teleporter ? new Vector3(0.9f, 0.4f, 0.9f) : kind == DeviceKind.Canon ? new Vector3(2.2f, CanonHeight, 2.2f)
            : kind == DeviceKind.Trampoline ? new Vector3(1.2f, height, 1.2f) : new Vector3(0.6f, height, 0.6f));
        beacon.SetCharged(false);
        return beacon;
    }

    // ---------- Objets uniques (repli en primitives si le modèle Meshy manque) ----------

    // Poste de télévision cathodique : caisse en bois, écran bombé vers l'avant (+Z), antennes en V.
    static void BuildTelevision(Transform parent, float height)
    {
        float s = height / 1.44f;
        Material wood = GameArt.Lit("TvWood", new Color(0.36f, 0.22f, 0.12f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.35f);
        Material plastic = GameArt.Lit("TvPlastic", new Color(0.16f, 0.15f, 0.14f), null, 0.5f);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0f, 0.12f, 0f) * s, new Vector3(0.7f, 0.24f, 0.55f) * s, plastic);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0f, 0.68f, 0f) * s, new Vector3(1.05f, 0.85f, 0.8f) * s, wood);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0f, 0.68f, 0.401f) * s, new Vector3(0.95f, 0.75f, 0.02f) * s, plastic);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0.38f, 0.68f, 0.42f) * s, new Vector3(0.12f, 0.6f, 0.03f) * s, wood);
        foreach (float side in new[] { -1f, 1f })
        {
            var rod = GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(side * 0.16f, 1.3f, -0.05f) * s, new Vector3(0.025f, 0.24f, 0.025f) * s, GameArt.Metal);
            rod.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 28f);
        }
    }

    // Trampoline rond : cadre bleu, toile noire tendue, quatre pieds.
    static void BuildTrampoline(Transform parent, float height)
    {
        Material frame = GameArt.Lit("TrampoFrame", new Color(0.15f, 0.35f, 0.95f), null, 0.5f);
        Material mat = GameArt.Lit("TrampoMat", new Color(0.05f, 0.05f, 0.06f), null, 0.3f);
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, height - 0.05f, 0f), new Vector3(1.15f, 0.05f, 1.15f), frame);
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, height - 0.02f, 0f), new Vector3(0.95f, 0.03f, 0.95f), mat);
        for (int k = 0; k < 4; k++)
        {
            Vector3 leg = Quaternion.Euler(0f, 45f + k * 90f, 0f) * Vector3.forward * 0.5f;
            GameArt.Prim(PrimitiveType.Cylinder, parent, leg + Vector3.up * (height - 0.1f) * 0.5f, new Vector3(0.05f, (height - 0.1f) * 0.5f, 0.05f), GameArt.Metal);
        }
    }

    // Cloche de bronze décorative, sans socle : robe évasée, anse sur le dessus.
    static void BuildBell(Transform parent, float height)
    {
        float s = height / 1.44f;
        Material bronze = GameArt.Lit("BellBronze", new Color(0.78f, 0.6f, 0.28f), null, 0.8f);
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.08f, 0f) * s, new Vector3(1f, 0.08f, 1f) * s, bronze);
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.5f, 0f) * s, new Vector3(0.82f, 0.4f, 0.82f) * s, bronze);
        GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(0f, 0.95f, 0f) * s, new Vector3(0.82f, 0.6f, 0.82f) * s, bronze);
        var handle = GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, 1.3f, 0f) * s, new Vector3(0.28f, 0.04f, 0.28f) * s, bronze);
        handle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // Gaz vert pétillant dans le tube de la Station Curative.
    static ParticleSystem BuildGas(Transform parent, float height)
    {
        var go = new GameObject("Gaz");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, height * 0.25f, 0f);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.3f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 1f, 0.35f, 0.9f), new Color(0.75f, 1f, 0.5f, 0.9f));
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = 28f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GameArt.Spark;
        return ps;
    }

    public void SetCharged(bool on)
    {
        // Le Trampoline et la Cloche Décorative ne sont pas reliés au réseau : jamais grisés.
        if (!Networked) on = false;
        Charged = on;
        if (Screen != null) Screen.SetSignal(on);
        if (halo != null) halo.SetActive(on);
        if (glow != null) glow.enabled = on;
        if (gas != null) { if (on && !gas.isPlaying) gas.Play(); else if (!on && gas.isPlaying) gas.Stop(); }
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_Color", on || !Networked || Kind == DeviceKind.Television ? ChargedTint : DischargedTint);
        foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
    }

    // Balise Améliorée : distance au sol jusqu'à la Balise du Hub, arrondie au mètre (ex. "29m"), suivie de sa profondeur
    // ou de sa hauteur par rapport au niveau du Hub quand elle dépasse 2 m (ex. "29m ↓3m", "29m ↑4m").
    const float LevelShownBeyond = 2f;

    public void ShowHubDistance(Vector3 hubPos)
    {
        if (coordinates == null) return;
        Vector3 d = transform.position - hubPos;
        string text = Mathf.RoundToInt(new Vector2(d.x, d.z).magnitude) + "m";
        float level = transform.position.y - hubPos.y;
        if (Mathf.Abs(level) > LevelShownBeyond) text += (level < 0f ? " ↓" : " ↑") + Mathf.RoundToInt(Mathf.Abs(level)) + "m";
        coordinates.text = text;
    }

    // Posé au sol (Note 03 du chapitre 7) : il s'écrase légèrement puis rebondit, en un tiers de seconde.
    public void PlayPlaced() => placedPop = 0f;

    void UpdatePlacedPop()
    {
        if (placedPop < 0f || visualRoot == null) return;
        placedPop += Time.deltaTime;
        const float duration = 0.32f;
        float k = Mathf.Clamp01(placedPop / duration);
        float wobble = Mathf.Sin(k * Mathf.PI * 2.5f) * (1f - k);
        visualRoot.localScale = new Vector3(1f + 0.18f * wobble, 1f - 0.22f * wobble, 1f + 0.18f * wobble);
        if (k >= 1f) { placedPop = -1f; visualRoot.localScale = Vector3.one; }
    }

    void Update()
    {
        UpdateRecoil();
        UpdatePlacedPop();
        if (coordinates != null && Camera.main != null)
            coordinates.transform.rotation = Quaternion.LookRotation(coordinates.transform.position - Camera.main.transform.position);
        if (!Charged || halo == null) return;
        // Le halo respire doucement.
        float s = (Kind == DeviceKind.Station ? 0.55f : VisualHeight * 0.26f) * (1f + 0.12f * Mathf.Sin(Time.time * 2.4f + Id));
        halo.transform.localScale = Vector3.one * s;
    }

    // Canon : le fût suit la visée (cap en degrés, dans le monde).
    public void AimCanon(float yaw)
    {
        if (visualRoot != null) visualRoot.rotation = Quaternion.Euler(0f, yaw - 90f, 0f);
    }

    // Tir du Canon : recul du fût, confettis, fumée et détonation, vus et entendus par tous.
    public void FireCanon(float yaw)
    {
        AimCanon(yaw);
        recoil = 0f;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 mouth = Muzzle + dir * (CanonHalfLength + 0.1f);
        GameAudio.Play3D("cannon_fire", mouth, 1f, 35f, 0.05f);
        foreach (var c in new[] { new Color(1f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.2f), new Color(0.95f, 0.95f, 0.95f), new Color(0.3f, 0.55f, 1f) })
            Fx.Burst(mouth, GameArt.Emissive("Confetti" + c, c, c * 0.6f), 14, 6f, 0.07f, 1.6f);
        Fx.Burst(mouth, GameArt.Lit("Fumee", new Color(0.55f, 0.55f, 0.55f), null, 0f), 10, 2f, 0.3f, 1.2f);
    }

    void UpdateRecoil()
    {
        if (recoil < 0f || visualRoot == null) return;
        // Le fût recule d'un coup sec puis revient en place.
        recoil += Time.deltaTime;
        float k = recoil < 0.06f ? recoil / 0.06f : 1f - Mathf.SmoothStep(0f, 1f, (recoil - 0.06f) / 0.5f);
        Vector3 back = -visualRoot.parent.InverseTransformDirection(visualRoot.rotation * Vector3.right);
        visualRoot.localPosition = back * (0.45f * k);
        visualRoot.localScale = Vector3.one * (1f + 0.08f * k);
        if (recoil > 0.6f) { recoil = -1f; visualRoot.localPosition = Vector3.zero; visualRoot.localScale = Vector3.one; }
    }

    public override string Prompt
    {
        get
        {
            return IsHub ? "Recharger la Balise du Hub" : "Ramasser " + Items.Get(Item).name;
        }
    }

    // La Balise du Hub n'est interactive (contour, message, touche E) que Chargeur sélectionné.
    public override bool CanInteract => !IsHub || (GameSession.Instance != null && GameSession.Instance.SelectedItem == ItemType.Chargeur);
    public override void Interact() => GameSession.Instance.InteractBeacon(this);
}

// Ensemble des Balises d'une partie : charge de la Balise du Hub, propagation de la charge (vision directe,
// aucun mur entre deux Balises) et rayons d'énergie bleus, qui circulent des Balises éloignées vers le Hub.
// Téléporteurs et Stations Curatives se chargent en voyant une Balise chargée, sans relayer l'énergie.
public class BeaconNetwork : MonoBehaviour
{
    const float RecomputeInterval = 0.4f;
    const int GaugeSegments = 120;
    const float GaugeInner = 0.55f, GaugeOuter = 0.68f;
    const float BackInner = 0.52f, BackOuter = 0.71f;

    Beacon hub;
    Transform world;
    readonly Dictionary<int, Beacon> placed = new Dictionary<int, Beacon>();
    // Pour chaque Balise chargée, la Balise vers laquelle son énergie s'écoule (plus proche du Hub).
    readonly Dictionary<Beacon, Beacon> parentOf = new Dictionary<Beacon, Beacon>();
    readonly Dictionary<Beacon, LineRenderer> beams = new Dictionary<Beacon, LineRenderer>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    MaterialPropertyBlock beamBlock;
    float recomputeTimer;
    bool dirty = true;

    // Charge restante de la Balise du Hub (secondes).
    public float Charge { get; set; } = GameRules.BeaconCharge;
    public bool HubCharged => Charge > 0f;
    public Beacon Hub => hub;
    public IEnumerable<int> Ids => placed.Keys;

    MeshFilter gauge;
    int gaugeShown = -1;

    public static BeaconNetwork Create(Transform world, Vector3 hubPos, float hubYaw)
    {
        var net = new GameObject("Balises").AddComponent<BeaconNetwork>();
        net.transform.SetParent(world, false);
        net.world = world;
        net.beamBlock = new MaterialPropertyBlock();
        net.hub = Beacon.Create(net.transform, -1, hubPos, hubYaw);
        net.BuildGauge(hubPos);
        return net;
    }

    public Beacon Add(int id, Vector3 pos, DeviceKind kind = DeviceKind.Beacon, float yaw = float.NaN)
    {
        if (placed.TryGetValue(id, out var existing) && existing != null) return existing;
        // Orientation : celle choisie à la pose (la Télévision fait face au joueur), sinon au hasard.
        var b = Beacon.Create(transform, id, pos, float.IsNaN(yaw) ? Random.Range(0f, 360f) : yaw, kind);
        b.ShowHubDistance(hub.transform.position);
        placed[id] = b;
        dirty = true;
        return b;
    }

    public Beacon Get(int id) => placed.TryGetValue(id, out var b) ? b : null;
    public IEnumerable<Beacon> Placed => placed.Values;

    // Liens d'énergie d'un appareil chargé jusqu'à la Balise du Hub (points d'attache), ou null s'il n'est pas relié.
    public List<Vector3> PathToHub(Beacon from)
    {
        var points = new List<Vector3>();
        for (Beacon b = from; b != null; b = parentOf.TryGetValue(b, out var next) ? next : null)
        {
            points.Add(b.Top);
            if (b == hub) return points;
            if (points.Count > 256) break;
        }
        return null;
    }

    public void Remove(int id)
    {
        if (!placed.TryGetValue(id, out var b)) return;
        placed.Remove(id);
        if (b != null)
        {
            if (beams.TryGetValue(b, out var beam)) { if (beam != null) Destroy(beam.gameObject); beams.Remove(b); }
            Destroy(b.gameObject);
        }
        dirty = true;
    }

    public void Clear()
    {
        foreach (int id in new List<int>(placed.Keys)) Remove(id);
        dirty = true;
    }

    void Update()
    {
        recomputeTimer -= Time.deltaTime;
        if (dirty || recomputeTimer <= 0f) Recompute();
        UpdateGauge();
        UpdateBeams();
    }

    // Propagation de la charge : depuis la Balise du Hub (si elle est chargée), de proche en proche
    // vers toute Balise en vision directe d'une Balise déjà chargée.
    void Recompute()
    {
        dirty = false;
        recomputeTimer = RecomputeInterval;
        parentOf.Clear();
        var charged = new List<Beacon>();
        if (HubCharged) charged.Add(hub);
        var pending = new List<Beacon>();
        var leaves = new List<Beacon>();
        foreach (var b in placed.Values) if (b != null && b.Networked) (b.Relays ? pending : leaves).Add(b);
        for (int head = 0; head < charged.Count; head++)
        {
            Beacon from = charged[head];
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (!InSight(from, pending[i])) continue;
                parentOf[pending[i]] = from;
                charged.Add(pending[i]);
                pending.RemoveAt(i);
            }
        }
        // Téléporteurs et Stations : reliés à la Balise chargée la plus proche qu'ils voient.
        foreach (var leaf in leaves)
        {
            Beacon best = null;
            float bestDist = float.MaxValue;
            foreach (var from in charged)
            {
                float d = (from.Top - leaf.Top).sqrMagnitude;
                if (d < bestDist && InSight(from, leaf)) { best = from; bestDist = d; }
            }
            if (best != null) parentOf[leaf] = best;
        }
        hub.SetCharged(HubCharged);
        foreach (var b in placed.Values)
            if (b != null && b.Charged != parentOf.ContainsKey(b)) b.SetCharged(parentOf.ContainsKey(b));
    }

    // La Balise du Hub (chargée ou non) voit cet appareil, sans élément du décor entre eux.
    public bool SeesHub(Beacon b) => b != null && InSight(hub, b);

    // Aucun élément du décor entre les deux cristaux (les objets interactifs, joueurs et entités ne bloquent pas).
    bool InSight(Beacon a, Beacon b)
    {
        Vector3 from = a.Top, to = b.Top, d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(from, d / len, hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (!c.transform.IsChildOf(world)) continue;
            if (c.GetComponentInParent<Interactable>() != null || c.GetComponentInParent<Entity>() != null) continue;
            return false;
        }
        return true;
    }

    void UpdateBeams()
    {
        foreach (var kv in parentOf)
        {
            if (kv.Key == null || kv.Value == null) continue;
            if (!beams.TryGetValue(kv.Key, out var line) || line == null) beams[kv.Key] = line = NewBeam();
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            // De la Balise éloignée vers celle qui la relie au Hub : l'énergie circule dans ce sens.
            Vector3 a = kv.Key.Top, b = kv.Value.Top;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            beamBlock.SetFloat("_Length", Vector3.Distance(a, b));
            line.SetPropertyBlock(beamBlock);
        }
        foreach (var kv in beams)
            if (kv.Value != null && (kv.Key == null || !parentOf.ContainsKey(kv.Key)) && kv.Value.gameObject.activeSelf)
                kv.Value.gameObject.SetActive(false);
    }

    LineRenderer NewBeam()
    {
        var go = new GameObject("Rayon");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.09f;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.sharedMaterial = GameArt.EnergyBeam;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    // ---------- Jauge circulaire autour de la Balise du Hub (pas de compteur numérique) ----------

    void BuildGauge(Vector3 hubPos)
    {
        // Même style que les boutons du menu : coups de pinceau (fond sombre, un peu plus large, puis trait bleu).
        var back = new GameObject("Jauge (fond)");
        back.transform.SetParent(transform, false);
        back.transform.position = hubPos + Vector3.up * 0.015f;
        back.AddComponent<MeshFilter>().sharedMesh = Ring(1f, BackInner, BackOuter);
        back.AddComponent<MeshRenderer>().sharedMaterial = BrushMaterial("BeaconGaugeBack", new Color(0.09f, 0.09f, 0.1f), 21, 3000);

        var fill = new GameObject("Jauge");
        fill.transform.SetParent(transform, false);
        fill.transform.position = hubPos + Vector3.up * 0.025f;
        gauge = fill.AddComponent<MeshFilter>();
        fill.AddComponent<MeshRenderer>().sharedMaterial = BrushMaterial("BeaconGauge", new Color(0.3f, 0.65f, 1f), 22, 3001);
    }

    static Material BrushMaterial(string name, Color color, int seed, int queue)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { name = name, color = color };
        m.mainTexture = UiKit.BrushTexture(2048, 96, seed);
        m.renderQueue = queue;
        return m;
    }

    void UpdateGauge()
    {
        int shown = Mathf.CeilToInt(Mathf.Clamp01(Charge / GameRules.BeaconCharge) * GaugeSegments);
        if (shown == gaugeShown) return;
        gaugeShown = shown;
        if (gauge.sharedMesh != null) Destroy(gauge.sharedMesh);
        gauge.sharedMesh = shown > 0 ? Ring((float)shown / GaugeSegments, GaugeInner, GaugeOuter) : null;
        if (shown == 0 || HubCharged != hub.Charged) dirty = true;
    }

    // Anneau plat (vu du dessus), rempli dans le sens horaire à partir du nord sur la fraction donnée.
    // Le coup de pinceau s'étire sur la partie remplie : il garde ses deux extrémités (poils du pinceau).
    static Mesh Ring(float fraction, float inner, float outer)
    {
        int segs = Mathf.Max(1, Mathf.RoundToInt(GaugeSegments * fraction));
        var verts = new Vector3[(segs + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var colors = new Color[verts.Length];
        var tris = new int[segs * 6];
        for (int i = 0; i <= segs; i++)
        {
            float a = (float)i / GaugeSegments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = dir * inner;
            verts[i * 2 + 1] = dir * outer;
            uvs[i * 2] = new Vector2((float)i / segs, 0f);
            uvs[i * 2 + 1] = new Vector2((float)i / segs, 1f);
            colors[i * 2] = colors[i * 2 + 1] = Color.white;
            if (i == segs) break;
            int v = i * 2;
            tris[i * 6] = v; tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 3;
            tris[i * 6 + 3] = v; tris[i * 6 + 4] = v + 3; tris[i * 6 + 5] = v + 2;
        }
        var mesh = new Mesh { vertices = verts, uv = uvs, colors = colors, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
