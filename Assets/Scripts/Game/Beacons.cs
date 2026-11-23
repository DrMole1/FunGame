using System.Collections.Generic;
using UnityEngine;

// Appareils posés au sol, ramassables, reliés au réseau d'énergie des Balises (GDD 7.1 à 7.5), et objets uniques posés
// de la même manière (7.6 à 7.8, V0.4.7) : la Télévision se relie aux Balises, le Trampoline et la Cloche Décorative non.
// Peluches et Boule de Disco (V0.7.2 : Super Monticules), posées et ramassées comme une Balise, hors du réseau d'énergie.
public enum DeviceKind : byte
{
    // V0.7.10 : plus de Balise Améliorée (1 ne sert plus). V0.7.11 : plus de Station Curative (3 ne sert plus).
    Beacon = 0, Teleporter = 2, Canon = 4, Television = 5, Trampoline = 6, Bell = 7,
    // V0.7.2 : une seule Boule de Disco, arc-en-ciel (13 et 14, anciennes boules or et diamant, ne servent plus).
    Lapin = 8, Pingouin = 9, Singe = 10, Chat = 11, Disco = 12,
    // V0.6.3 (note 06 du chapitre 7) : canards posés au sol. CanardColore : palier 1 ; les paliers suivants suivent.
    Canard = 15, CanardColore = 16,
    // V0.6.13 (7.8) : Cloche Décorative des zones 2+ (BellZone + nZone), suspendue au plafond tant qu'on ne l'a pas récupérée.
    BellZone = 200,
    // V0.6.13 (7.10) : Protocole posé au sol (à plat, le texte vers le haut).
    Protocole = 190,
    // V0.7.11 : Métronome (7.12), Super Balle (7.13), Notice (7.17) et Journaux (17.5) posés au sol ; Sac à Os (7.16) : SacOs + nombre
    // d'Os qu'il contient (150 à 165).
    Metronome = 140, SuperBalle = 141, Notice = 142, Journal1 = 143, Journal5 = 147, SacOs = 150, SacOsPlein = 165,
}

// Balise (GDD 7.1) : petite borne posée au sol, ramassable. La Balise du Hub, plus grande, ne se ramasse pas : elle est
// chargée en permanence (V0.7.2 : plus de Chargeur ni de jauge).
// V0.6 : chaque Hub a sa Balise principale (Hub 1 bleue, Hub 2 violette, Hub 3 verte, Hub 4 dorée, puis on recommence) ;
// elles partagent la même charge.
// V0.7.10 : nouveau modèle (pion d'échecs blanc) ; un orbe fantomatique, à la couleur du flux, flotte au niveau de la boule blanche
// d'une Balise chargée (aucun orbe sur une Balise non chargée). Les Balises des Hubs, plus grandes, ont le même modèle : leur orbe,
// toujours présent, prend la couleur du Hub. Plus de Balise Améliorée. Téléporteur (7.2) et Canon (7.3) :
// chargés s'ils voient une Balise chargée. Le Canon de cirque tourne avec la visée du joueur qui est dedans, et recule en crachant des confettis au tir.
// Note 03 du chapitre 7 (V0.4.7) : un objet posé au sol joue une légère animation rapide (il s'écrase puis rebondit).
public class Beacon : Interactable
{
    public const float HubHeight = 1.5f, Height = 0.85f, PadHeight = 0.16f;
    // Canon posé au sol : 0,64 fois la taille d'un personnage (1,8 m), soit environ 2,4 m de long ; fût à 80 % de sa hauteur.
    public const float CanonHeight = 0.64f * 1.8f, CanonBarrel = CanonHeight * 0.8f, CanonHalfLength = 1.2f;
    public const DeviceKind LastKind = DeviceKind.CanardColore + Items.MaxDuckTier - 1;
    public const DeviceKind MaxKind = DeviceKind.BellZone + Items.MaxBellZone;
    public static bool IsBellKind(DeviceKind kind) => kind == DeviceKind.Bell || kind > DeviceKind.BellZone + 1 && kind <= MaxKind;
    public const float PlushHeight = 0.9f, DuckHeight = 0.45f, DiscoHeight = 0.8f, DiscoChain = 0.25f, ColoredDuckHeight = 0.6f;
    // V0.7.11 : Métronome, Super Balle ("légèrement plus petite que la taille d'un joueur"), Sac à Os posé.
    public const float MetronomeHeight = 0.6f, SuperBallSize = 1.4f, BagHeight = 0.65f;
    public static bool IsPaperKind(DeviceKind kind) => kind == DeviceKind.Notice || kind >= DeviceKind.Journal1 && kind <= DeviceKind.Journal5;
    public static bool IsBagKind(DeviceKind kind) => kind >= DeviceKind.SacOs && kind <= DeviceKind.SacOsPlein;
    static readonly Color ChargedTint = Color.white, DischargedTint = new Color(0.42f, 0.42f, 0.46f);

    public int Id { get; private set; }   // -N : Balise principale du Hub N
    public DeviceKind Kind { get; private set; }
    public bool IsHub => Id < 0;
    public int HubIndex => IsHub ? -Id : 0;

    // Couleur du cristal de la Balise principale du Hub N.
    static readonly Color[] HubColors = { new Color(0.3f, 0.6f, 1f), new Color(0.65f, 0.3f, 1f), new Color(0.3f, 1f, 0.45f), new Color(1f, 0.78f, 0.25f) };
    public static Color HubColor(int hub) => HubColors[(Mathf.Max(1, hub) - 1) % HubColors.Length];
    public static int ColorIndex(int hub) => (Mathf.Max(1, hub) - 1) % HubColors.Length;
    // Hub dont provient le flux qui alimente cet appareil (0 : aucun).
    public int FluxHub { get; private set; }
    // Balises : elles relaient l'énergie ; Téléporteur et Canon ne font que la recevoir.
    public bool Relays => Kind == DeviceKind.Beacon;
    // Relié au réseau d'énergie (chargé s'il voit une Balise chargée) : ni la Télévision (V0.7.8), ni le Trampoline, ni la
    // Cloche Décorative, ni les peluches, ni la Boule de Disco.
    public bool Networked => Kind < DeviceKind.Television;
    public bool IsDisco => IsDiscoKind(Kind);
    // V0.7.3 : une Boule de Disco ne s'allume (lumière, musique) qu'une fois posée par un joueur.
    DiscoBall disco;
    public bool DiscoActive => disco != null && disco.Active;
    public void ActivateDisco() { if (disco != null) disco.Activate(); }
    public static bool IsDiscoKind(DeviceKind kind) => kind == DeviceKind.Disco;
    public bool IsDuck => Kind >= DeviceKind.Canard && Kind <= LastKind;
    // V0.7.11 : Métronome qui bat la mesure, Super Balle qui roule.
    MetronomeDevice metronome;
    SuperBall ball;
    public MetronomeDevice Metronome => metronome;
    public SuperBall Ball => ball;

    public const float ProtocolHeight = 0.06f;
    // Boule à facettes suspendue au plafond (point de pose au plafond).
    public bool Hanging { get; private set; }
    // Télévision : son écran (sans signal, décompte ou vidéo).
    public TvScreen Screen { get; private set; }
    public bool Charged { get; private set; }
    public ItemType Item => ItemOf(Kind);
    float VisualHeight => IsHub ? HubHeight : HeightOf(Kind);
    // Point d'attache des rayons d'énergie : le cristal (le centre du disque, le haut du tube, le fût du Canon, l'antenne).
    public Vector3 Top => transform.position + Vector3.up * (Kind == DeviceKind.Teleporter ? 0.12f : Kind == DeviceKind.Canon ? CanonBarrel : VisualHeight * (Kind == DeviceKind.Beacon ? OrbLevel : 0.86f));

    public static ItemType ItemOf(DeviceKind kind)
    {
        if (kind > DeviceKind.BellZone) return Items.ZoneBellOf(kind - DeviceKind.BellZone);
        // Avant les canards colorés : son numéro (190) est au-delà de leur plage, pas dedans.
        if (kind == DeviceKind.Protocole) return ItemType.Protocole;
        if (kind >= DeviceKind.SacOs && kind <= DeviceKind.SacOsPlein) return Items.BoneBagOf(kind - DeviceKind.SacOs);
        if (kind >= DeviceKind.Journal1 && kind <= DeviceKind.Journal5) return ItemType.Journal1 + (kind - DeviceKind.Journal1);
        if (kind == DeviceKind.Metronome) return ItemType.Metronome;
        if (kind == DeviceKind.SuperBalle) return ItemType.SuperBalle;
        if (kind == DeviceKind.Notice) return ItemType.Notice;
        if (kind >= DeviceKind.CanardColore && kind <= LastKind) return ItemType.CanardColore + (kind - DeviceKind.CanardColore + 1);
        switch (kind)
        {
            case DeviceKind.Teleporter: return ItemType.Teleporteur;
            case DeviceKind.Canon: return ItemType.Canon;
            case DeviceKind.Television: return ItemType.Television;
            case DeviceKind.Trampoline: return ItemType.Trampoline;
            case DeviceKind.Bell: return ItemType.ClocheDecorative;
            case DeviceKind.Lapin: return ItemType.PelucheLapin;
            case DeviceKind.Pingouin: return ItemType.PeluchePingouin;
            case DeviceKind.Singe: return ItemType.PelucheSinge;
            case DeviceKind.Chat: return ItemType.PelucheChat;
            case DeviceKind.Disco: return ItemType.BouleDisco;
            case DeviceKind.Canard: return ItemType.Canard;
            case DeviceKind.Protocole: return ItemType.Protocole;
            default: return ItemType.Balise;
        }
    }

    // Appareil posé par cet objet de l'inventaire (Balise si l'objet ne se pose pas).
    public static DeviceKind KindOf(ItemType item)
    {
        if (Items.IsColoredDuck(item)) return (DeviceKind)((int)DeviceKind.CanardColore + Items.DuckTier(item) - 1);
        if (Items.IsZoneBell(item)) return (DeviceKind)((int)DeviceKind.BellZone + Items.BellZone(item));
        if (item == ItemType.Protocole) return DeviceKind.Protocole;
        if (Items.IsBoneBag(item)) return (DeviceKind)((int)DeviceKind.SacOs + Items.BonesIn(item));
        if (Items.IsJournal(item)) return (DeviceKind)((int)DeviceKind.Journal1 + (item - ItemType.Journal1));
        if (item == ItemType.Metronome) return DeviceKind.Metronome;
        if (item == ItemType.SuperBalle) return DeviceKind.SuperBalle;
        if (item == ItemType.Notice) return DeviceKind.Notice;
        for (var k = DeviceKind.Beacon; k <= DeviceKind.Canard; k++) if (ItemOf(k) == item) return k;
        return DeviceKind.Beacon;
    }

    public static float HeightOf(DeviceKind kind)
    {
        if (IsBellKind(kind)) return GameRules.BellHeight;
        if (kind == DeviceKind.Protocole || IsPaperKind(kind)) return ProtocolHeight;
        if (kind == DeviceKind.Metronome) return MetronomeHeight;
        if (kind == DeviceKind.SuperBalle) return SuperBallSize;
        if (IsBagKind(kind)) return BagHeight;
        if (kind >= DeviceKind.CanardColore) return ColoredDuckHeight;
        switch (kind)
        {
            case DeviceKind.Teleporter: return PadHeight;
            case DeviceKind.Canon: return CanonHeight;
            case DeviceKind.Television: return GameRules.TvHeight;
            case DeviceKind.Trampoline: return GameRules.TrampolineHeight;
            case DeviceKind.Bell: return GameRules.BellHeight;
            case DeviceKind.Lapin: case DeviceKind.Pingouin: case DeviceKind.Singe: case DeviceKind.Chat: return PlushHeight;
            case DeviceKind.Canard: return DuckHeight;
            case DeviceKind.Disco: return DiscoHeight;
            default: return Height;
        }
    }
    // Extrémité du rayon qui alimente cet appareil depuis 'source' : son point le plus proche (disque pour le Téléporteur).
    public Vector3 BeamEnd(Vector3 source) => BeaconNetwork.TouchPoint(bodies, source, Top, Kind == DeviceKind.Teleporter);

    // Bouche du Canon, d'où part le boulet.
    public Vector3 Muzzle => transform.position + Vector3.up * CanonBarrel;

    Renderer[] bodies;
    GameObject halo;
    Light glow;
    MaterialPropertyBlock block;
    Transform visualRoot;
    float recoil = -1f, placedPop = -1f;

    // 'chain' (Cloche Décorative des zones 2+, V0.6.13) : longueur de la chaîne qui la suspend au plafond (0 : posée au sol).
    public static Beacon Create(Transform parent, int id, Vector3 pos, float yaw, DeviceKind kind = DeviceKind.Beacon, bool hanging = false, float chain = 0f)
    {
        // (Modèles Meshy des objets uniques, sans socle : Resources/Art/Television, Trampoline et ClocheDecorative.)
        bool hub = id < 0;
        var go = new GameObject(hub ? "Balise du Hub " + -id : kind + " " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var beacon = go.AddComponent<Beacon>();
        beacon.Id = id;
        beacon.Kind = kind;
        beacon.Hanging = hanging && IsDiscoKind(kind);
        beacon.FluxHub = hub ? -id : 0;
        float height = beacon.VisualHeight;
        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);
        beacon.visualRoot = visual;
        if (beacon.IsDuck) BuildDuck(visual, kind, height);
        else if (kind == DeviceKind.Metronome) beacon.metronome = MetronomeDevice.Build(go, visual, height);
        else if (kind == DeviceKind.SuperBalle) beacon.ball = SuperBall.Build(go, visual, height);
        else if (IsBagKind(kind))
        {
            if (GameArt.SpawnModelFloor("SacOs", visual, height) == null)
                GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, height * 0.5f, 0f), new Vector3(height * 0.7f, height, height * 0.4f), GameArt.Lit("SacOsToile", new Color(0.5f, 0.38f, 0.24f), null, 0.2f));
        }
        else if (kind >= DeviceKind.Journal1 && kind <= DeviceKind.Journal5 && GameArt.SpawnModelFloor("Journal", visual, 0.5f) != null) { }
        else if (kind == DeviceKind.Protocole || IsPaperKind(kind))
        {
            // À plat sur le sol, le texte vers le ciel, le haut de la feuille vers l'avant de l'appareil.
            var board = ProtocolBoard.Build(visual, 1f, true, kind == DeviceKind.Protocole ? DocumentKind.Protocole
                : ProtocolBoard.KindOf(ItemOf(kind)));
            board.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            board.transform.localPosition = new Vector3(0f, ProtocolBoard.Thickness * 0.5f + 0.004f, ProtocolBoard.Height * 0.5f);
        }
        else if (IsBellKind(kind))
        {
            if (GameArt.SpawnModel("ClocheDecorative", visual, height) == null) BuildBell(visual, height);
            // Suspendue : une chaîne monte du haut de la Cloche jusqu'au plafond.
            if (chain > 0f)
            {
                beacon.Hanging = true;
                GameArt.Prim(PrimitiveType.Cylinder, visual, Vector3.up * (height + chain * 0.5f), new Vector3(0.025f, chain * 0.5f, 0.025f), GameArt.Metal);
            }
        }
        else switch (kind)
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
            case DeviceKind.Lapin:
            case DeviceKind.Pingouin:
            case DeviceKind.Singe:
            case DeviceKind.Chat:
                if (GameArt.SpawnModel(Items.ModelName(ItemOf(kind)), visual, height) == null)
                    GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, height * 0.45f, 0f), Vector3.one * height * 0.8f, GameArt.Lit("PelucheRepli", Items.Get(ItemOf(kind)).color));
                break;
            case DeviceKind.Disco:
                beacon.disco = BuildDiscoBall(go, visual, height, beacon.Hanging);
                break;
            default:
                // V0.7.10 : pion d'échecs blanc (Meshy). Repli : un pion blanc en primitives.
                if (GameArt.SpawnModel("Balise", visual, height) == null)
                {
                    Material ivory = GameArt.Lit("BeaconIvory", new Color(0.93f, 0.92f, 0.9f), null, 0.4f);
                    float k = height / Height;
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.06f, 0f) * k, new Vector3(0.36f, 0.06f, 0.36f) * k, ivory);
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.36f, 0f) * k, new Vector3(0.16f, 0.26f, 0.16f) * k, ivory);
                    GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, OrbLevel * height, 0f), Vector3.one * 0.22f * k, ivory);
                }
                break;
        }

        if (kind == DeviceKind.Beacon) AddFarModel(visual, height);
        beacon.bodies = visual.GetComponentsInChildren<Renderer>();
        beacon.block = new MaterialPropertyBlock();
        if (Items.Dances(beacon.Item)) DiscoDancer.Attach(visual);
        // Le Canard Coloré scintille, posé comme à terre.
        if (Items.IsColoredDuck(beacon.Item)) Fx.Sparkle(go.transform, Items.Get(beacon.Item).color);
        // Orbe fantomatique quand l'appareil est chargé : celui d'une Balise, au niveau de la boule blanche, prend la couleur du flux
        // (SetFlux ; bleu du Hub 1 par défaut). Aucun sur le Téléporteur.
        if (kind == DeviceKind.Beacon)
            beacon.halo = NewHalo(go.transform, beacon.Top - pos, height * OrbSize, OrbMaterial(0));

        if (hub)
        {
            Color color = HubColor(-id);
            // V0.7.10 : même pion blanc que les autres Balises ; seul son orbe, toujours présent, prend la couleur du Hub (bleu,
            // violet, vert, doré...).
            if (beacon.halo != null) beacon.halo.GetComponent<Renderer>().sharedMaterial = OrbMaterial(ColorIndex(-id));
            beacon.glow = new GameObject("Lueur").AddComponent<Light>();
            beacon.glow.transform.SetParent(go.transform, false);
            beacon.glow.transform.localPosition = new Vector3(0f, height * 0.9f, 0f);
            beacon.glow.type = LightType.Point;
            beacon.glow.color = Color.Lerp(color, Color.white, 0.15f);
            beacon.glow.range = 4f;
            beacon.glow.intensity = 1.4f;
            beacon.glow.shadows = LightShadows.None;
            // V0.7.11 (9.1, Note 08) : numéro du Hub en chiffres romains, écrit en noir sur le bas de la Balise, du côté que voit le
            // joueur qui entre dans le Hub (par le sud).
            var number = GameArt.CreateText(go.transform, "NumeroHub", Vector3.zero, 0.075f, new Color(0.02f, 0.02f, 0.02f));
            number.text = GameRules.Roman(-id);
            number.fontStyle = FontStyle.Bold;
            number.transform.SetPositionAndRotation(pos + new Vector3(0f, height * 0.16f, -HubNumberRadius), Quaternion.identity);
            // Socle solide : on ne traverse pas la grande Balise.
            var solid = go.AddComponent<CapsuleCollider>();
            solid.center = new Vector3(0f, height * 0.5f, 0f);
            solid.height = height;
            solid.radius = 0.22f;
        }
        beacon.FitTrigger(kind == DeviceKind.Protocole || IsPaperKind(kind) ? new Vector3(ProtocolBoard.Width + 0.1f, 0.2f, ProtocolBoard.Height + 0.1f)
            : kind == DeviceKind.SuperBalle ? Vector3.one * SuperBallSize * 1.05f
            : kind == DeviceKind.Teleporter ? new Vector3(0.9f, 0.4f, 0.9f) : kind == DeviceKind.Canon ? new Vector3(1.76f, CanonHeight, 1.76f)
            : kind == DeviceKind.Trampoline ? new Vector3(1.2f, height, 1.2f) : beacon.IsDisco ? new Vector3(0.9f, 0.9f, 0.9f) : new Vector3(0.6f, height, 0.6f));
        beacon.SetCharged(false);
        return beacon;
    }

    // Rayon du socle de la grande Balise, à la hauteur du numéro du Hub (juste devant sa surface).
    const float HubNumberRadius = 0.36f;

    // L'orbe (halo) est un carré face à la caméra : il ne reçoit pas le contour blanc, sinon un carré apparaît autour de la
    // boule du pion quand on vise la Balise.
    protected override bool HasOutline(Renderer r) => halo == null || r.gameObject != halo;

    static GameObject NewHalo(Transform parent, Vector3 localPos, float size, Material material)
    {
        var halo = GameArt.MeshObject("Halo", parent, GameArt.HaloQuad, material);
        halo.transform.localPosition = localPos;
        halo.transform.localScale = Vector3.one * size;
        return halo;
    }

    // Au-delà de cette distance, la Balise passe à son modèle simplifié (Resources/Art/BaliseLoin, menu "Préparer la Balise simplifiée").
    const float FarModelDistance = 15f;
    static Mesh farModel;

    static void AddFarModel(Transform visual, float height)
    {
        if (farModel == null) farModel = Resources.Load<Mesh>("Art/BaliseLoin");
        Transform model = visual.Find("Balise");
        var near = model != null ? model.GetComponentInChildren<MeshRenderer>() : null;
        if (farModel == null || near == null) return;
        var far = GameArt.MeshObject("BaliseLoin", near.transform, farModel, near.sharedMaterial).GetComponent<MeshRenderer>();
        float fov = Camera.main != null ? Camera.main.fieldOfView : 60f;
        // Part de la hauteur de l'écran occupée par la Balise à cette distance.
        float transition = height * QualitySettings.lodBias / (2f * FarModelDistance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        var lod = visual.gameObject.AddComponent<LODGroup>();
        lod.localReferencePoint = Vector3.up * height * 0.5f;
        lod.size = height;
        lod.SetLODs(new[] { new LOD(Mathf.Clamp01(transition), new Renderer[] { near }), new LOD(0f, new Renderer[] { far }) });
    }

    static void BuildDuck(Transform visual, DeviceKind kind, float height)
    {
        ItemType item = ItemOf(kind);
        if (GameArt.SpawnModel(Items.ModelName(item), visual, height) != null) Items.ApplyLook(visual, item);
        else GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, height * 0.45f, 0f), Vector3.one * height * 0.8f, GameArt.Lit("Canard" + (int)kind, Items.Get(item).color));
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

    // Boule de Disco (V0.7.2) : modèle Meshy arc-en-ciel, brillant. Au plafond, elle pend au bout d'une chaînette ; elle
    // tourne et projette des faisceaux aux couleurs de l'arc-en-ciel (DiscoBall).
    static DiscoBall BuildDiscoBall(GameObject device, Transform visual, float height, bool hanging)
    {
        var ball = new GameObject("Boule").transform;
        ball.SetParent(visual, false);
        float drop = hanging ? DiscoChain + height : 0f;
        ball.localPosition = Vector3.down * drop;
        if (GameArt.SpawnModel("BouleDisco", ball, height) == null)
            GameArt.Prim(PrimitiveType.Sphere, ball, new Vector3(0f, height * 0.5f, 0f), Vector3.one * height, GameArt.Lit("BouleRepli", new Color(0.85f, 0.6f, 1f), null, 0.95f));
        foreach (var r in ball.GetComponentsInChildren<Renderer>())
        {
            if (discoMaterial != null) { r.sharedMaterial = discoMaterial; continue; }
            var m = discoMaterial = new Material(r.sharedMaterial) { name = "BouleDisco" };
            // Facettes miroir : plus claires que la texture Meshy, brillantes et un peu lumineuses dans le noir.
            m.color = Color.white * 1.35f;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.92f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.55f);
            if (m.mainTexture != null && m.HasProperty("_EmissionMap"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", m.mainTexture);
                m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f));
            }
            r.sharedMaterial = m;
        }
        if (hanging)
            GameArt.Prim(PrimitiveType.Cylinder, visual, Vector3.down * DiscoChain * 0.5f, new Vector3(0.012f, DiscoChain * 0.5f, 0.012f), GameArt.Metal);
        // Les faisceaux partent du centre de la boule.
        var center = new GameObject("Centre").transform;
        center.SetParent(ball, false);
        center.localPosition = Vector3.up * height * 0.5f;
        return DiscoBall.Attach(device, ball, center, hanging);
    }

    static Material discoMaterial;

    // Point d'où la musique disco se diffuse (centre de la boule).
    public Vector3 DiscoCenter => transform.position + Vector3.down * (Hanging ? DiscoChain + VisualHeight * 0.5f : -VisualHeight * 0.5f);

    public void SetCharged(bool on)
    {
        // Le Trampoline et la Cloche Décorative ne sont pas reliés au réseau : jamais grisés.
        if (!Networked) on = false;
        Charged = on;
        if (halo != null) halo.SetActive(on);
        if (glow != null) glow.enabled = on;
        if (block == null) block = new MaterialPropertyBlock();
        // La Boule de Disco, les canards colorés et le Protocole (papier noirci, V0.7.2) gardent leur teinte.
        if (IsDisco || IsDuck || Kind == DeviceKind.Protocole) return;
        block.SetColor("_Color", on || !Networked ? ChargedTint : DischargedTint);
        foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
    }

    // Note 02 du chapitre 9.1 (V0.6.9 ; V0.7.10) : l'orbe d'une Balise chargée prend la couleur du flux (celle de son Hub) ;
    // déchargée, elle n'a plus d'orbe (SetCharged).
    public void SetFlux(int hub)
    {
        if (hub == FluxHub || IsHub) return;
        FluxHub = hub;
        if (Kind == DeviceKind.Beacon && halo != null) halo.GetComponent<Renderer>().sharedMaterial = OrbMaterial(ColorIndex(hub));
    }

    // Orbe d'une Balise : au niveau de la boule blanche du pion (part de la hauteur de la Balise), de la couleur du Hub.
    public const float OrbLevel = 0.85f, OrbSize = 0.38f;

    static Material OrbMaterial(int colorIndex) => GameArt.BeaconHalo("Hub" + colorIndex, HubColors[colorIndex]);

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
            return IsHub ? "" : Loc.F("Ramasser {0}", Items.Get(Item).name);
        }
    }

    // V0.7.2 : plus de Chargeur, la Balise du Hub n'est jamais interactive.
    public override bool CanInteract => !IsHub;
    public override void Interact() => GameSession.Instance.InteractBeacon(this);
}

// Ensemble des Balises d'une partie : charge des Balises principales des Hubs, propagation de la charge (vision directe,
// aucun mur entre deux Balises) et rayons d'énergie bleus, qui circulent des Balises éloignées vers leur Hub.
// Téléporteurs et Canons se chargent en voyant une Balise chargée, sans relayer l'énergie.
// V0.6 : une Balise reliée à deux Balises principales se rattache à celle du Hub le plus haut.
// Les lignes de vue sont gardées en mémoire : les Balises ne bougent pas. Elles sont revérifiées quelques-unes par image, et bien plus
// vite juste après un changement du décor (porte, pont cassé) ; le réseau n'est recalculé que si quelque chose a changé.
public class BeaconNetwork : MonoBehaviour
{
    const int IdleSightRays = 6, UrgentSightRays = 32;
    const float UrgentSightWindow = 3f;
    const int GaugeSegments = 120;

    // Balises principales, par numéro de Hub (1 : Hub de départ).
    readonly SortedDictionary<int, Beacon> hubs = new SortedDictionary<int, Beacon>();
    Transform world;
    readonly Dictionary<int, Beacon> placed = new Dictionary<int, Beacon>();
    // Pour chaque Balise chargée, la Balise vers laquelle son énergie s'écoule (plus proche de son Hub).
    readonly Dictionary<Beacon, Beacon> parentOf = new Dictionary<Beacon, Beacon>();
    readonly Dictionary<Beacon, LineRenderer> beams = new Dictionary<Beacon, LineRenderer>();
    // Hub dont dépend chaque appareil chargé : son flux en prend la couleur (Note 01 du chapitre 9.3, V0.6.9).
    readonly Dictionary<Beacon, int> fluxOf = new Dictionary<Beacon, int>();
    static readonly Color[] FluxColors = { new Color(0.15f, 0.45f, 1f), new Color(0.48f, 0.12f, 1f), new Color(0.12f, 0.9f, 0.3f), new Color(1f, 0.62f, 0.08f) };
    static readonly Color[] FluxCores = { new Color(0.75f, 0.9f, 1f), new Color(0.88f, 0.75f, 1f), new Color(0.75f, 1f, 0.82f), new Color(1f, 0.92f, 0.7f) };
    public static Color FluxColor(int index) => FluxColors[Mathf.Clamp(index, 0, FluxColors.Length - 1)];
    public static Color FluxCore(int index) => FluxCores[Mathf.Clamp(index, 0, FluxCores.Length - 1)];
    static readonly int BeamColorId = Shader.PropertyToID("_Color"), BeamCoreId = Shader.PropertyToID("_CoreColor"), BeamLengthId = Shader.PropertyToID("_Length");
    readonly RaycastHit[] hits = new RaycastHit[16];
    MaterialPropertyBlock beamBlock;
    bool dirty = true;

    struct SightPair { public long key; public int first, second; public Vector3 from, to; }
    readonly Dictionary<long, bool> sight = new Dictionary<long, bool>();
    readonly List<SightPair> sightPairs = new List<SightPair>();
    int sightCursor;
    static float urgentSightUntil;

    // Le décor vient de changer (porte, pont) : les lignes de vue sont revérifiées en priorité le temps qu'il finisse de bouger.
    public static void SightChanged() => urgentSightUntil = Time.time + UrgentSightWindow;

    // V0.7.2 : les Balises principales des Hubs restent chargées en permanence (plus de Chargeur).
    public bool HubCharged => true;
    public Beacon Hub => hubs.TryGetValue(1, out var b) ? b : null;
    public IEnumerable<Beacon> Hubs => hubs.Values;
    public IEnumerable<int> Ids => placed.Keys;

    public static BeaconNetwork Create(Transform world, Vector3 hubPos, float hubYaw)
    {
        var net = new GameObject("Balises").AddComponent<BeaconNetwork>();
        net.transform.SetParent(world, false);
        net.world = world;
        net.beamBlock = new MaterialPropertyBlock();
        net.AddHub(1, hubPos, hubYaw);
        return net;
    }

    // Balise principale du Hub 'index' (Hubs 2 et suivants : à leur génération).
    public void AddHub(int index, Vector3 pos, float yaw)
    {
        if (hubs.ContainsKey(index)) return;
        hubs[index] = Beacon.Create(transform, -index, pos, yaw);
        dirty = true;
    }

    // Partie quittée : seuls restent le Hub 1 et sa Balise.
    public void RemoveHubsAbove(int index)
    {
        foreach (int h in new List<int>(hubs.Keys))
        {
            if (h <= index) continue;
            ForgetSight(-h);
            if (hubs[h] != null)
            {
                if (beams.TryGetValue(hubs[h], out var beam) && beam != null) Destroy(beam.gameObject);
                beams.Remove(hubs[h]);
                Destroy(hubs[h].gameObject);
            }
            hubs.Remove(h);
        }
        dirty = true;
    }

    public Beacon GetHub(int index) => hubs.TryGetValue(index, out var b) ? b : null;

    // Chapitre 9.2 (V0.6.5) : les Balises principales des Hubs fermés (jusqu'à 'upTo' compris) cessent de charger le réseau.
    public void SealHubs(int upTo)
    {
        foreach (var kv in hubs)
        {
            bool on = kv.Key > upTo;
            if (kv.Value != null) kv.Value.gameObject.SetActive(on);
        }
        dirty = true;
        SightChanged();
    }

    // Balise principale la plus proche d'un point (à l'horizontale).
    public Beacon NearestHub(Vector3 p)
    {
        Beacon best = null;
        float bestDist = float.MaxValue;
        foreach (var h in hubs.Values)
        {
            if (h == null || !h.gameObject.activeSelf) continue;
            Vector3 d = h.transform.position - p;
            float dist = new Vector2(d.x, d.z).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = h; }
        }
        return best;
    }

    public Beacon Add(int id, Vector3 pos, DeviceKind kind = DeviceKind.Beacon, float yaw = float.NaN, bool hanging = false, float chain = 0f)
    {
        if (placed.TryGetValue(id, out var existing) && existing != null) return existing;
        // Orientation : celle choisie à la pose (la Télévision fait face au joueur), sinon au hasard.
        var b = Beacon.Create(transform, id, pos, float.IsNaN(yaw) ? Random.Range(0f, 360f) : yaw, kind, hanging, chain);
        placed[id] = b;
        dirty = true;
        return b;
    }

    public Beacon Get(int id) => placed.TryGetValue(id, out var b) ? b : null;
    public IEnumerable<Beacon> Placed => placed.Values;
    // Balise (simple ou améliorée) d'où provient la charge d'un appareil (null s'il n'est pas chargé).
    public Beacon ParentOf(Beacon b) => b != null && parentOf.TryGetValue(b, out var p) ? p : null;

    // Liens d'énergie d'un appareil chargé jusqu'à la Balise principale de son Hub (points d'attache), ou null s'il n'est
    // pas relié.
    public List<Vector3> PathToHub(Beacon from)
    {
        var points = new List<Vector3>();
        for (Beacon b = from; b != null; b = parentOf.TryGetValue(b, out var next) ? next : null)
        {
            points.Add(b.Top);
            if (b.IsHub) return points;
            if (points.Count > 256) break;
        }
        return null;
    }

    public void Remove(int id)
    {
        if (!placed.TryGetValue(id, out var b)) return;
        placed.Remove(id);
        ForgetSight(id);
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
        sight.Clear();
        sightPairs.Clear();
        dirty = true;
    }

    void Update()
    {
        RefreshSight();
        if (dirty) Recompute();
        UpdateBeams();
    }

    // Propagation de la charge : depuis les Balises principales (si elles sont chargées), de proche en proche vers toute
    // Balise en vision directe d'une Balise déjà chargée. Les Hubs les plus hauts se servent d'abord.
    void Recompute()
    {
        dirty = false;
        parentOf.Clear();
        fluxOf.Clear();
        var charged = new List<Beacon>();
        var pending = new List<Beacon>();
        var leaves = new List<Beacon>();
        foreach (var b in placed.Values) if (b != null && b.Networked) (b.Relays ? pending : leaves).Add(b);
        var roots = new List<Beacon>(hubs.Values);
        roots.Reverse();
        foreach (var root in roots)
        {
            if (root == null || !root.gameObject.activeSelf) continue;
            int head = charged.Count;
            charged.Add(root);
            fluxOf[root] = root.HubIndex;
            for (; head < charged.Count; head++)
            {
                Beacon from = charged[head];
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (!Reachable(root.HubIndex, pending[i]) || !InSight(from, pending[i])) continue;
                    parentOf[pending[i]] = from;
                    fluxOf[pending[i]] = root.HubIndex;
                    charged.Add(pending[i]);
                    pending.RemoveAt(i);
                }
            }
        }
        // Téléporteurs et Canons : reliés à la Balise chargée la plus proche qu'ils voient.
        foreach (var leaf in leaves)
        {
            Beacon best = null;
            float bestDist = float.MaxValue;
            foreach (var from in charged)
            {
                float d = (from.Top - leaf.Top).sqrMagnitude;
                if (d < bestDist && Reachable(fluxOf[from], leaf) && InSight(from, leaf)) { best = from; bestDist = d; }
            }
            if (best == null) continue;
            parentOf[leaf] = best;
            fluxOf[leaf] = fluxOf[best];
        }
        foreach (var h in hubs.Values) if (h != null) h.SetCharged(HubCharged);
        foreach (var b in placed.Values)
        {
            if (b == null) continue;
            if (b.Charged != parentOf.ContainsKey(b)) b.SetCharged(parentOf.ContainsKey(b));
            b.SetFlux(fluxOf.TryGetValue(b, out int hub) ? hub : 0);
        }
    }

    // V0.7.9 (Note 01) : le flux d'un Hub ne descend jamais vers une zone inférieure. Le Hub N est entre la zone 2N - 2 et la
    // zone 2N - 1 : un appareil posé dans son chapiteau (ou celui d'un Hub plus loin) ou à partir de la zone 2N - 1 peut être
    // relié ; dans une zone d'avant (ou le chapiteau d'un Hub d'avant), aucun lien.
    static bool Reachable(int hub, Beacon b)
    {
        if (hub <= 1 || b == null) return true;
        Vector3 p = b.transform.position;
        int inHub = WorldBuilder.HubAt(p);
        if (inHub > 0) return inHub >= hub;
        int zone = WorldBuilder.ZoneAt(p);
        return zone < 1 || zone >= hub;   // V0.7.11 : le Hub N est au sud de la zone N
    }

    // Une Balise principale (chargée ou non) voit cet appareil, sans élément du décor entre eux.
    public bool SeesHub(Beacon b)
    {
        if (b == null) return false;
        foreach (var h in hubs.Values) if (h != null && h.gameObject.activeSelf && InSight(h, b)) return true;
        return false;
    }

    // Aucun élément du décor entre le cristal et ce point (les objets interactifs, joueurs et entités ne bloquent pas).
    public bool InSight(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
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

    bool InSight(Beacon a, Beacon b) => InSight(a, b.Id, b.Top);

    // Ligne de vue entre une Balise et une cible immobile, gardée en mémoire.
    public bool InSight(Beacon a, int target, Vector3 position)
    {
        long key = a.Id < target ? ((long)a.Id << 32) | (uint)target : ((long)target << 32) | (uint)a.Id;
        if (sight.TryGetValue(key, out bool seen)) return seen;
        Vector3 from = a.Top;
        seen = InSight(from, position);
        sight[key] = seen;
        sightPairs.Add(new SightPair { key = key, first = a.Id, second = target, from = from, to = position });
        return seen;
    }

    void ForgetSight(int id)
    {
        for (int i = sightPairs.Count - 1; i >= 0; i--)
        {
            if (sightPairs[i].first != id && sightPairs[i].second != id) continue;
            sight.Remove(sightPairs[i].key);
            sightPairs[i] = sightPairs[sightPairs.Count - 1];
            sightPairs.RemoveAt(sightPairs.Count - 1);
        }
    }

    // Revérifie quelques lignes de vue à chaque image ; le réseau n'est recalculé que si l'une d'elles a changé.
    void RefreshSight()
    {
        int budget = Mathf.Min(Time.time < urgentSightUntil ? UrgentSightRays : IdleSightRays, sightPairs.Count);
        for (int k = 0; k < budget; k++)
        {
            if (sightCursor >= sightPairs.Count) sightCursor = 0;
            SightPair pair = sightPairs[sightCursor++];
            bool seen = InSight(pair.from, pair.to);
            if (seen == sight[pair.key]) continue;
            sight[pair.key] = seen;
            dirty = true;
        }
    }

    void UpdateBeams()
    {
        foreach (var kv in parentOf)
        {
            if (kv.Key == null || kv.Value == null) continue;
            if (!beams.TryGetValue(kv.Key, out var line) || line == null) beams[kv.Key] = line = NewBeam();
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            // De la Balise éloignée vers celle qui la relie au Hub : l'énergie circule dans ce sens. Un appareil (Téléporteur,
            // Canon, Télévision) : le rayon touche son point le plus proche de la Balise.
            Vector3 from = kv.Key.Relays ? kv.Key.Top : kv.Key.BeamEnd(kv.Value.Top);
            ShowBeam(line, kv.Key, from, kv.Value.Top);
        }
        foreach (var kv in beams)
            if (kv.Value != null && (kv.Key == null || !parentOf.ContainsKey(kv.Key)) && kv.Value.gameObject.activeSelf)
                kv.Value.gameObject.SetActive(false);
    }

    // Place un rayon d'énergie, à la couleur du Hub dont dépend l'appareil 'source'.
    public void ShowBeam(LineRenderer line, Beacon source, Vector3 from, Vector3 to)
    {
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        int color = Beacon.ColorIndex(source.FluxHub);
        beamBlock.SetColor(BeamColorId, FluxColors[color]);
        beamBlock.SetColor(BeamCoreId, FluxCores[color]);
        beamBlock.SetFloat(BeamLengthId, Vector3.Distance(from, to));
        line.SetPropertyBlock(beamBlock);
    }

    // Extrémité d'un rayon sur un objet (appareil, borne, Téléporteur Inversé) : le point de ses modèles le plus proche de
    // 'from' (la Balise qui l'alimente), pris sur la boîte de chaque maillage, puis un peu enfoncé pour que le rayon touche
    // l'objet (ses extrémités s'estompent). 'round' : objet en disque (Téléporteurs), le point reste sur le disque.
    public static Vector3 TouchPoint(Renderer[] bodies, Vector3 from, Vector3 fallback, bool round = false)
    {
        if (bodies == null) return fallback;
        Vector3 best = fallback;
        float bestDist = float.MaxValue;
        Bounds all = default;
        bool any = false;
        foreach (var r in bodies)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            Bounds local = mesh.bounds;
            Vector3 p = r.transform.TransformPoint(local.ClosestPoint(r.transform.InverseTransformPoint(from)));
            float d = (p - from).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = p; }
            if (any) all.Encapsulate(r.bounds); else { all = r.bounds; any = true; }
        }
        if (!any) return fallback;
        if (round)
        {
            Vector3 c = all.center;
            Vector2 flat = new Vector2(best.x - c.x, best.z - c.z);
            float radius = Mathf.Min(all.extents.x, all.extents.z);
            if (flat.magnitude > radius) { flat = flat.normalized * radius; best.x = c.x + flat.x; best.z = c.z + flat.y; }
        }
        Vector3 into = best - from;
        return into.sqrMagnitude > 0.0001f ? best + into.normalized * BeamTouchDepth : best;
    }

    const float BeamTouchDepth = 0.12f;

    public LineRenderer NewBeam()
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

    // ---------- Jauges (Téléporteur Inversé) : anneau en coup de pinceau ----------

    public static Material BrushMaterial(string name, Color color, int seed, int queue)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { name = name, color = color };
        m.mainTexture = UiKit.BrushTexture(2048, 96, seed);
        m.renderQueue = queue;
        return m;
    }

    // Anneau plat (vu du dessus), rempli dans le sens horaire à partir du nord sur la fraction donnée.
    // Le coup de pinceau s'étire sur la partie remplie : il garde ses deux extrémités (poils du pinceau).
    public static Mesh Ring(float fraction, float inner, float outer)
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
