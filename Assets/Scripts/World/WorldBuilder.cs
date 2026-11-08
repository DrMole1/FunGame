using System.Collections.Generic;
using UnityEngine;

// Construit les Hubs (chapiteaux circulaires) et les zones du labyrinthe procédural (V0.6) : le terrain se génère à
// l'infini vers le nord (+Z), en suivant le schéma Hub > zone avec porte > zone avec Neutral Room et Acquisition Room >
// Hub suivant... Chaque zone fait 170 m x 170 m ; le Hub N est au sud de la zone 2N - 1, et la porte de son chapiteau
// est dans l'Acquisition Room de la zone 2N - 2. Une zone dépend uniquement de la graine et de son numéro : tous les
// clients génèrent la même, et on ne la génère qu'au moment où des joueurs s'en approchent.
public static class WorldBuilder
{
    public const float Cell = 5f;
    // V0.6.1 : chaque zone fait 170 m de large et de long (34 x 34 cellules de 5 m ; 200 m en V0.6).
    public const int ZoneCells = 34;
    public const float ZoneSize = ZoneCells * Cell;
    // Colonne du Hub (et de la porte de chaque chapiteau) ; le Hub occupe 5 x 5 cellules au sud des zones impaires.
    public const int HubCol = ZoneCells / 2;
    public const int HubMin = HubCol - 2, HubMax = HubCol + 2;
    const int HubRows = 5;
    public const float WallHeight = 4f;      // hauteur des couloirs (les salles ont leur propre hauteur)
    public const float Wall = 0.3f;
    public const float HubRadius = 12f;
    const float HubWallHeight = 5f;
    // Sommet du toit du chapiteau (cône de 6 m posé sur les murs), au-dessus du sol du Hub.
    public const float ChapiteauTop = HubWallHeight + 6f;
    const float DoorWidth = 2.4f, DoorHeight = 2.8f, SillTop = 0.015f, SillDepth = 0.05f;
    const int ChunkSize = 5, WallChunkSize = 2;
    // Jointures des murs : chaque pan s'engage de 1 cm sous son poteau (plus de fins traits là où les hauteurs diffèrent). Les deux
    // faces restent dans le même plan : un poteau en saillie, même de 1 mm, dessine une ligne pointillée le long du mur.
    const float JointOverlap = 0.01f;
    const float WallRun = Cell - Wall + JointOverlap * 2f;
    const float StepHeight = 0.2f, StepDepth = 0.3f;

    // Coin sud-ouest des zones : le centre de la colonne du Hub est en x = 0, le centre du Hub 1 à l'origine.
    public const float OriginX = -(HubCol + 0.5f) * Cell;
    public static float ZoneSouth(int zone) => -HubRows * Cell * 0.5f + (zone - 1) * ZoneSize;
    public static Vector3 ZoneOrigin(int zone) => new Vector3(OriginX, 0f, ZoneSouth(zone));
    // Centre du Hub N (N >= 1) : un Hub toutes les deux zones.
    public static Vector3 HubCenter(int hub) => new Vector3(0f, 0f, (hub - 1) * 2f * ZoneSize);
    // Zone impaire qui contient le Hub N, et Hub d'une zone impaire.
    public static int ZoneOfHub(int hub) => hub * 2 - 1;

    // Angles (degrés, 0 = porte au nord) des emplacements du Hub.
    public static readonly float[] KennelAngles = { 55f, 78f, 101f, 124f, -55f, -78f, -101f, -124f };
    const float TerminalAngle = 150f, LogAngle = 180f, QuotaAngle = -28f, CashAngle = 28f, RoundBoardAngle = -142f;
    const float PogoAngle = -41f, BeaconAngle = 14f;

    public class HubRefs
    {
        public int index;                // 1 : Hub de départ
        public Vector3 center;
        public Transform root;
        public HubDoor door;             // porte vers la zone suivante (au nord)
        public Transform buttonAnchor, terminalAnchor, pogoAnchor, skipAnchor;
        public Vector3 beaconPosition;   // Balise principale du Hub, près de la porte
        public float beaconYaw;
        public TextMesh timerText, quotaText, cashText, logText, roundBoardText; // pas d'Écran des Logs hors du Hub 1
        public ReflectionProbe hubProbe;
        public Vector3[] kennelPositions;
        public Quaternion[] kennelRotations;
    }

    // Une zone générée : son bâti, ses emplacements d'objets, sa navigation, sa brume, et selon sa parité la porte à orbes
    // (zones impaires, au nord) ou la Neutral Room, les glyphes et l'Acquisition Room (zones paires).
    public class ZoneRefs
    {
        public int index;
        public GameObject root;
        public Transform structure;  // collisions du bâti (sols, murs, rampes), sans le décor
        public List<Bounds> underground = new List<Bounds>();  // liaisons souterraines (chapitre 10.3)
        public readonly List<Vector3> itemSpots = new List<Vector3>();
        public MazeNav nav;
        public GameObject mist;      // brume du Couvre Feu
        public ZoneDoor door;        // zones impaires : porte vers la zone paire suivante
        public ZoneOrb[] orbs;
        public int doorNumber;       // 1 : porte entre la Zone 1 et la Zone 2
        public readonly List<Glyph> neutralGlyphs = new List<Glyph>(), acquisitionGlyphs = new List<Glyph>();
        public AcquisitionDoor acquisitionDoor;   // zones paires : porte du chapiteau suivant
        public Bounds neutralRoom;
        public Vector3 entrance;     // point d'arrivée dans la zone (derrière la porte du Hub ou la porte à orbes)
        public readonly List<ArcadeMachine> arcades = new List<ArcadeMachine>();   // V0.6.1 : à partir de la Zone 3
        internal Maze maze;
    }

    // Zone (1, 2, 3...) qui contient le point, 0 en dehors (au sud du Hub 1 ou hors des murs).
    public static int ZoneAt(Vector3 p)
    {
        if (p.x < OriginX || p.x > OriginX + ZoneSize || p.z < ZoneSouth(1)) return 0;
        return Mathf.FloorToInt((p.z - ZoneSouth(1)) / ZoneSize) + 1;
    }

    // Hub (1, 2, 3...) qui contient le point, 0 ailleurs.
    public static int HubAt(Vector3 p)
    {
        int hub = Mathf.RoundToInt(p.z / (2f * ZoneSize)) + 1;
        if (hub < 1) return 0;
        Vector3 d = p - HubCenter(hub);
        return new Vector2(d.x, d.z).sqrMagnitude < HubRadius * HubRadius && d.y < HubWallHeight + 6f ? hub : 0;
    }

    public static bool IsInHub(Vector3 p) => HubAt(p) > 0;

    static Vector3 Polar(float angleDeg, float radius, float y)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
    }

    // Rotation dont l'avant (+Z) pointe vers le centre du Hub.
    static Quaternion FacingCenter(float angleDeg) => Quaternion.Euler(0f, angleDeg + 180f, 0f);

    // ================= Hub =================

    // Hub N : les Hubs 2 et suivants sont identiques au Hub 1, sans l'Écran des Logs, avec en plus une entrée au sud
    // (la porte du chapiteau, dans l'Acquisition Room de la zone précédente).
    public static HubRefs BuildHub(Transform parent, int index = 1)
    {
        var refs = new HubRefs { index = index, center = HubCenter(index) };
        var root = new GameObject(index == 1 ? "Hub" : "Hub " + index).transform;
        root.SetParent(parent, false);
        root.localPosition = refs.center;
        refs.root = root;
        bool entrance = index > 1;

        Color red = new Color(0.72f, 0.08f, 0.1f), cream = new Color(0.95f, 0.9f, 0.78f);
        // Toile vernie : brillante comme le damier du labyrinthe, avec des aspérités.
        Material redMat = GameArt.Lit("HubRed", red, GameArt.Canvas(), 0.85f);
        Material creamMat = GameArt.Lit("HubCream", cream, GameArt.Canvas(), 0.85f);
        Material gold = GameArt.Lit("HubGold", new Color(0.85f, 0.65f, 0.2f), null, 0.6f);

        // Sol (terre gravillonnée, matériau créé par GameSetup) et collision.
        var floor = new MeshBuilder();
        floor.Disc(Vector3.zero, HubRadius + 0.3f, 48, 0.25f);
        Material floorMat = Resources.Load<Material>("Materials/HubFloor");
        if (floorMat == null) floorMat = GameArt.Lit("HubFloor", new Color(0.62f, 0.42f, 0.26f), GameArt.Noise("wood", 128, 0.7f, 0.25f, 3), 0.15f);
        GameArt.MeshObject("Floor", root, floor.Build("HubFloor"), floorMat);
        var floorCol = new GameObject("FloorCollider").AddComponent<BoxCollider>();
        floorCol.transform.SetParent(root, false);
        floorCol.center = new Vector3(0f, -0.5f, 0f);
        floorCol.size = new Vector3(Cell * 5f, 1f, Cell * 5f);

        // Piste centrale.
        AddRing(root, "PisteRouge", 3.5f, 0.01f, redMat);
        AddRing(root, "PisteCreme", 3.1f, 0.015f, creamMat);

        // Mur circulaire à rayures, ouvert au nord pour la porte.
        const int segments = 32;
        float segLen = 2f * Mathf.PI * (HubRadius + 0.15f) / segments + 0.08f;
        for (int i = 1; i < segments; i++)
        {
            if (entrance && i == segments / 2) continue;
            float angle = i * 360f / segments;
            var seg = GameArt.Prim(PrimitiveType.Cube, root, Polar(angle, HubRadius + 0.15f, HubWallHeight * 0.5f),
                new Vector3(segLen, HubWallHeight, 0.3f), i % 2 == 0 ? redMat : creamMat, true);
            seg.name = "HubWall";
            seg.transform.localRotation = FacingCenter(angle);
        }
        // Guirlande de cirque dorée en haut et en bas (sauf au sol devant les portes), un morceau par pan de mur.
        var garlands = new GameObject("Guirlandes").transform;
        garlands.SetParent(root, false);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            for (int k = 0; k < 2; k++)
            {
                if (k == 0 && (i == 0 || entrance && i == segments / 2)) continue;
                AddGarland(garlands, angle, k == 0, segLen, gold);
            }
        }

        // Toit du chapiteau.
        var roof = new MeshBuilder();
        roof.InnerCone(new Vector3(0f, HubWallHeight, 0f), HubRadius + 0.6f, 6f, segments, 16f);
        GameArt.MeshObject("Roof", root, roof.Build("HubRoof"), GameArt.Lit("HubRoof", Color.white, GameArt.CanvasStripes(red, cream), 0.8f));
        var roofCol = new GameObject("RoofCollider").AddComponent<BoxCollider>();
        roofCol.transform.SetParent(root, false);
        roofCol.center = new Vector3(0f, HubWallHeight + 6.5f, 0f);
        roofCol.size = new Vector3(Cell * 5f, 1f, Cell * 5f);
        GameArt.Prim(PrimitiveType.Sphere, root, new Vector3(0f, HubWallHeight + 5.6f, 0f), Vector3.one * 0.6f, gold);

        // Passage vers le labyrinthe et porte.
        // Montants et linteau s'arrêtent à la face du mur du dédale (axe à HubRadius + 0,5) : entrer dans son épaisseur
        // superposerait leurs faces à celles de l'ouverture (scintillement des textures).
        float zIn = HubRadius - 0.4f, zOut = HubRadius + 0.5f - Wall * 0.5f;
        float lintelIn = HubRadius - 0.15f, lintelZ = (lintelIn + zOut) * 0.5f, lintelDepth = zOut - lintelIn;
        float side = DoorWidth * 0.5f + 0.1f;
        foreach (float sx in new[] { -side, side })
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(sx, DoorHeight * 0.5f, (zIn + zOut) * 0.5f), new Vector3(0.2f, DoorHeight, zOut - zIn), redMat, true);
        GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, (DoorHeight + HubWallHeight) * 0.5f, lintelZ),
            new Vector3(DoorWidth + 0.6f, HubWallHeight - DoorHeight, lintelDepth), creamMat, true);
        // Seuil : couvre l'écart entre le sol rond du chapiteau et celui du dédale, à peine au-dessus d'eux, derrière la porte.
        float sillIn = HubRadius + 0.22f, sillOut = HubRadius + 0.5f + Wall * 0.5f;
        Vector3 sillCenter = new Vector3(0f, SillTop - SillDepth * 0.5f, (sillIn + sillOut) * 0.5f);
        Vector3 sillSize = new Vector3(DoorWidth, SillDepth, sillOut - sillIn);
        GameArt.Prim(PrimitiveType.Cube, root, sillCenter, sillSize, gold);
        // Entrée du chapiteau (modèle Meshy) : arche de cirque à colonnes, rideaux de velours, panneau à ampoules où
        // s'affiche le Compteur de Temps. Sans le modèle : simple liseré doré au-dessus de la porte.
        Transform arch = BuildEntrance(root);
        if (arch == null)
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, DoorHeight + 0.1f, HubRadius - 0.2f), new Vector3(DoorWidth + 0.6f, 0.2f, 0.12f), gold);

        var door = BuildHubDoor(root, new Vector3(0f, DoorHeight * 0.5f, HubRadius + 0.15f), new Vector3(DoorWidth + 0.1f, DoorHeight, 0.12f));
        refs.door = door.AddComponent<HubDoor>();
        refs.door.Init(DoorHeight - 0.05f);

        // Hubs 2 et suivants : passage au sud, fermé par la porte du chapiteau (dans le mur de l'Acquisition Room).
        if (entrance)
        {
            foreach (float sx in new[] { -side, side })
                GameArt.Prim(PrimitiveType.Cube, root, new Vector3(sx, DoorHeight * 0.5f, -(zIn + zOut) * 0.5f), new Vector3(0.2f, DoorHeight, zOut - zIn), redMat, true);
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, (DoorHeight + HubWallHeight) * 0.5f, -lintelZ),
                new Vector3(DoorWidth + 0.6f, HubWallHeight - DoorHeight, lintelDepth), creamMat, true);
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, DoorHeight + 0.1f, -(HubRadius - 0.2f)), new Vector3(DoorWidth + 0.6f, 0.2f, 0.12f), gold);
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, sillCenter.y, -sillCenter.z), sillSize, gold);
        }

        // Éclairage chaud du chapiteau.
        for (int i = 0; i < 4; i++)
            AddLight(root, Polar(45f + i * 90f, 6.5f, 4.3f), new Color(1f, 0.85f, 0.65f), 1.1f, 13f);
        AddLight(root, new Vector3(0f, 6f, 0f), new Color(1f, 0.9f, 0.75f), 1.2f, 12f);

        // Sonde de réflexion : la toile vernie reflète les lampes et le chapiteau.
        var probe = new GameObject("Reflets_Hub").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(root, false);
        probe.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        probe.size = new Vector3(HubRadius * 2f + 1f, HubWallHeight + 7f, HubRadius * 2f + 1f);
        probe.center = new Vector3(0f, (HubWallHeight + 7f) * 0.5f - 2.5f, 0f);
        probe.boxProjection = true;
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 128;
        probe.hdr = false;
        probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        probe.cullingMask = ~((1 << 2) | (1 << 5));
        probe.farClipPlane = 30f;
        refs.hubProbe = probe;

        // Écrans.
        refs.timerText = Screen(root, "Compteur de Temps", 0f, HubRadius - 0.75f, 4.05f, new Vector2(3.2f, 1.1f), 0.075f);
        // Dans le panneau à ampoules de l'entrée.
        if (arch != null)
        {
            Transform timer = refs.timerText.transform.parent;
            timer.localPosition = new Vector3(0f, 3.92f, EntranceZ - 0.45f);
            timer.localScale = Vector3.one * 0.68f;
        }
        refs.quotaText = Screen(root, "Rendu de Quota", QuotaAngle, HubRadius - 0.45f, 2.5f, new Vector2(3.6f, 1.1f), 0.042f);
        // V0.4.6 : Bouton Skip sous le Rendu de Quota (affiché une fois le quota atteint), face au centre.
        refs.skipAnchor = new GameObject("SkipAnchor").transform;
        refs.skipAnchor.SetParent(root, false);
        refs.skipAnchor.localPosition = Polar(QuotaAngle, HubRadius - 0.5f, 1.45f);
        refs.skipAnchor.localRotation = FacingCenter(QuotaAngle);
        refs.cashText = Screen(root, "Rendu de Cagnotte", CashAngle, HubRadius - 0.45f, 2.5f, new Vector2(3.6f, 1.1f), 0.042f);
        // V0.6 : l'Écran des Logs (et le portrait caché derrière) n'existe que dans le Hub 1 ; au sud des autres Hubs, l'entrée.
        if (index == 1)
        {
            refs.logText = Screen(root, "Ecran de Logs", LogAngle, HubRadius - 0.9f, 2.6f, new Vector2(8f, 3.6f), 0.027f, TextAnchor.MiddleLeft);
            AddPavlov(refs.logText.transform.parent);
        }
        refs.timerText.color = new Color(1f, 0.35f, 0.3f);
        refs.roundBoardText = Screen(root, "Tableau de Manche", RoundBoardAngle, HubRadius - 0.45f, 2.7f, new Vector2(4.4f, 2.8f), 0.026f);
        refs.roundBoardText.color = new Color(1f, 0.9f, 0.6f);

        // Terminal.
        refs.terminalAnchor = new GameObject("TerminalAnchor").transform;
        refs.terminalAnchor.SetParent(root, false);
        refs.terminalAnchor.localPosition = Polar(TerminalAngle, HubRadius - 1.8f, 0f);
        refs.terminalAnchor.localRotation = FacingCenter(TerminalAngle);

        // Balise du Hub, à côté du passage vers le labyrinthe (vision directe par la porte).
        refs.beaconPosition = refs.center + Polar(BeaconAngle, 8.6f, 0f);
        refs.beaconYaw = BeaconAngle + 180f;

        // Bouton Rouge au centre.
        refs.buttonAnchor = new GameObject("ButtonAnchor").transform;
        refs.buttonAnchor.SetParent(root, false);

        // Emplacements des Chenils.
        refs.kennelPositions = new Vector3[KennelAngles.Length];
        refs.kennelRotations = new Quaternion[KennelAngles.Length];
        for (int i = 0; i < KennelAngles.Length; i++)
        {
            refs.kennelPositions[i] = refs.center + Polar(KennelAngles[i], HubRadius - 1.6f, 0f);
            refs.kennelRotations[i] = FacingCenter(KennelAngles[i]);
        }

        // Pogo (entre le Rendu de Quota et les chenils, sans masquer d'écran) et ballons de cirque.
        refs.pogoAnchor = new GameObject("PogoAnchor").transform;
        refs.pogoAnchor.SetParent(root, false);
        refs.pogoAnchor.localPosition = Polar(PogoAngle, 9.2f, 0f);
        refs.pogoAnchor.localRotation = FacingCenter(PogoAngle);
        GameArt.Prim(PrimitiveType.Sphere, root, Polar(-24f, 10.8f, 0.45f), Vector3.one * 0.9f, gold, true);
        GameArt.Prim(PrimitiveType.Sphere, root, Polar(-33f, 10.7f, 0.35f), Vector3.one * 0.7f, redMat, true);

        return refs;
    }

    // Disque de la piste centrale : assez de côtés pour que son contour paraisse parfaitement rond, texture à l'échelle du disque.
    const int RingSegments = 128;

    static void AddRing(Transform root, string name, float radius, float y, Material mat)
    {
        var ring = new MeshBuilder();
        ring.Disc(new Vector3(0f, y, 0f), radius, RingSegments, 0.5f / radius);
        GameArt.MeshObject(name, root, ring.Build(name), mat);
    }

    // Morceau de guirlande (modèle Meshy : deux festons arrondis sous une barre, décor tourné vers son +Z), adossé au mur,
    // posé au sol ou sous le toit. Sans le modèle : simple liseré doré.
    const float GarlandHeight = 0.44f, GarlandGround = 0.02f, GarlandEmbed = 0.005f;

    static void AddGarland(Transform parent, float angle, bool ground, float length, Material gold)
    {
        var garland = GameArt.SpawnModel("Decor/GuirlandeChapiteau", parent, GarlandHeight);
        if (garland == null)
        {
            var trim = GameArt.Prim(PrimitiveType.Cube, parent, Polar(angle, HubRadius - 0.02f, ground ? 0.1f : HubWallHeight - 0.1f), new Vector3(length, 0.2f, 0.1f), gold);
            trim.transform.localRotation = FacingCenter(angle);
            return;
        }
        float depth = GameArt.LocalBounds(garland.transform).size.z;
        // Au sol, la pièce est retournée (barre en bas, festons vers le haut) : son origine, à sa base, passe au sommet.
        float y = ground ? GarlandGround + GarlandHeight : HubWallHeight - GarlandHeight - GarlandGround;
        garland.transform.localPosition = Polar(angle, HubRadius - depth * 0.5f + GarlandEmbed, y);
        garland.transform.localRotation = FacingCenter(angle) * (ground ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity);
        foreach (var r in garland.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = gold;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // Porte coulissante du chapiteau (modèle Meshy : bandes rouges et crème, arabesques dorées, empreintes ensanglantées),
    // ajustée aux dimensions du passage. Sans le modèle : simple panneau sombre.
    static GameObject BuildHubDoor(Transform root, Vector3 center, Vector3 size)
    {
        var door = new GameObject("HubDoor");
        door.transform.SetParent(root, false);
        door.transform.localPosition = center;
        door.AddComponent<BoxCollider>().size = size;
        var model = GameArt.SpawnModel("PorteChapiteau", door.transform, size.y);
        if (model == null)
        {
            GameArt.Prim(PrimitiveType.Cube, door.transform, Vector3.zero, size, GameArt.Lit("Door", new Color(0.25f, 0.05f, 0.06f), null, 0.5f));
            return door;
        }
        Bounds b = GameArt.LocalBounds(model.transform);
        model.transform.localScale = new Vector3(size.x / b.size.x, 1f, size.z / b.size.z);
        model.transform.localPosition = new Vector3(0f, -size.y * 0.5f, 0f);
        return door;
    }

    // Arche de l'entrée : 5,4 m de haut, élargie de 15 % pour que le passage entre les rideaux dégage toute la porte.
    const float EntranceHeight = 5.4f, EntranceStretch = 1.15f, EntranceZ = HubRadius - 0.45f;

    static Transform BuildEntrance(Transform root)
    {
        var arch = GameArt.SpawnModel("EntreeChapiteau", root, EntranceHeight);
        if (arch == null) return null;
        arch.name = "EntreeChapiteau";
        // Le modèle regarde vers -Z : retourné face au centre du chapiteau.
        arch.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        arch.transform.localScale = new Vector3(EntranceStretch, 1f, 1f);
        arch.transform.localPosition = new Vector3(0f, 0f, EntranceZ);
        // Les colonnes arrêtent les joueurs (le modèle n'a pas de collision).
        Bounds b = GameArt.LocalBounds(arch.transform);
        float x = b.extents.x * EntranceStretch - 0.35f;
        foreach (float sx in new[] { -x, x })
        {
            var col = new GameObject("ColonneEntree").AddComponent<BoxCollider>();
            col.transform.SetParent(root, false);
            col.transform.localPosition = new Vector3(sx, 1.6f, EntranceZ);
            col.size = new Vector3(0.6f, 3.2f, 0.6f);
        }
        return arch.transform;
    }

    const float ScreenCorner = 0.25f, ScreenBorder = 0.1f;
    const int ScreenCornerSegments = 10;

    static void ScreenPlate(Transform parent, string name, Vector3 position, Vector2 size, float depth, float corner, Material mat)
    {
        var plate = new MeshBuilder();
        plate.RoundedPlate(size, depth, corner, ScreenCornerSegments);
        GameArt.MeshObject(name, parent, plate.Build(name), mat).transform.localPosition = position;
    }

    static TextMesh Screen(Transform root, string name, float angle, float radius, float y, Vector2 size, float charSize,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name).transform;
        go.SetParent(root, false);
        go.localPosition = Polar(angle, radius, y);
        // Le texte se lit quand on le regarde selon son axe +Z : l'avant pointe vers le mur.
        go.localRotation = Quaternion.Euler(0f, angle, 0f);

        // Coins arrondis, ceux du cadre concentriques à ceux de l'écran.
        ScreenPlate(go, "Cadre", new Vector3(0f, 0f, 0.06f), new Vector2(size.x + ScreenBorder * 2f, size.y + ScreenBorder * 2f), 0.08f, ScreenCorner + ScreenBorder,
            GameArt.Lit("ScreenFrame", new Color(0.1f, 0.1f, 0.1f), null, 0.6f));
        ScreenPlate(go, "Fond", new Vector3(0f, 0f, 0.01f), size, 0.02f, ScreenCorner, GameArt.Unlit("ScreenBg", new Color(0.02f, 0.03f, 0.04f)));

        Vector3 textPos = anchor == TextAnchor.MiddleLeft ? new Vector3(-size.x * 0.5f + 0.15f, 0f, -0.01f) : new Vector3(0f, 0f, -0.01f);
        var tm = GameArt.CreateText(go, "Text", textPos, charSize, Color.white, anchor,
            anchor == TextAnchor.MiddleLeft ? TextAlignment.Left : TextAlignment.Center);
        return tm;
    }

    // Easter egg : le portrait d'Ivan Pavlov, collé au dos de l'écran de Logs (visible seulement en passant derrière).
    static void AddPavlov(Transform screen)
    {
        var tex = Resources.Load<Texture2D>("EasterEgg/IvanPavlov");
        if (tex == null) return;
        float h = 2.4f, w = h * tex.width / tex.height;
        Material mat = GameArt.Lit("Pavlov", Color.white, tex, 0.05f);
        // Un peu lumineux par lui-même : l'espace derrière l'écran est dans l'ombre.
        mat.EnableKeyword("_EMISSION");
        mat.SetTexture("_EmissionMap", tex);
        mat.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f));
        // Le quad est visible depuis son -Z : retourné vers le mur, derrière le cadre de l'écran.
        var quad = GameArt.Prim(PrimitiveType.Quad, screen, new Vector3(0f, 0f, 0.105f), new Vector3(w, h, 1f), mat);
        quad.name = "Pavlov";
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    static Light AddLight(Transform parent, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        return l;
    }

    // ================= Labyrinthe =================

    internal enum RoomStyle { Columns, Spheres, Chaotic, Blocks, Pit, Template }

    internal class Room
    {
        public RectInt rect;
        public float floor, ceil; // hauteur du plancher et du plafond (absolues)
        public float pitDepth;    // style Pit : profondeur maximale sous le niveau du Hub
        public RoomStyle style;
        public bool chess;
        public bool plain;        // Neutral Room, Acquisition Room : sans décor
        public float statueHeight; // chapitre 10.3 : hauteur de la statue de Pavlov dressée au centre (0 : aucune)
        public readonly List<Vector2Int> cells = new List<Vector2Int>();

        // Style Pit (salle à profondeurs, chapitre 9.1) : grille fine de 0,5 m sur toute la salle.
        public float[,] levels;     // hauteur du sol de chaque case (0 : pourtour au niveau du Hub)
        public bool[,] stairCell;   // case occupée par un escalier
        public readonly List<Stair> stairs = new List<Stair>();
        public readonly List<(List<Vector2Int> cells, float level)> depths = new List<(List<Vector2Int>, float)>();
        // Arêtes sans paroi : le haut de chaque escalier, qui donne sur le niveau supérieur.
        public readonly HashSet<(Vector2Int, Vector2Int)> openEdges = new HashSet<(Vector2Int, Vector2Int)>();
        // Passerelles (larges, praticables par les entités) et poutres (étroites) qui enjambent les fosses au niveau du Hub.
        public readonly List<(RectInt cells, bool beam)> bridges = new List<(RectInt, bool)>();
        public bool[,] bridgeCell;
    }

    // Escalier d'une profondeur : bande de cases de la grille fine, descendue dans le sens 'dir' (0 = +X, 1 = -X, 2 = +Z, 3 = -Z).
    internal class Stair
    {
        public RectInt strip;
        public int dir, steps;
        public float top, bottom;
    }

    // Une zone du labyrinthe : grille de 40 x 40 cellules de 5 m. Coordonnées (i, j) : i le long de X, j le long de Z
    // (vers le nord, la zone suivante). Zones impaires : le Hub au sud (5 x 5 cellules), la porte à orbes au nord.
    // Zones paires : la Neutral Room au sud (derrière la porte à orbes), l'Acquisition Room au nord (porte du chapiteau).
    internal class Maze
    {
        public readonly int zone, N;
        public readonly List<Rect> blocks = new List<Rect>();  // chapitre 10.3 : emprises (X, Z) des plateformes pleines et de leurs escaliers
        public readonly List<Star> stars = new List<Star>();   // chapitre 10.3 : emplacements prioritaires des bornes d'arcade
        // Liaisons entre salles : trémies (cellule, sens de l'escalier), ouvertures dans les murs (arête est ou nord d'une cellule),
        // volumes souterrains (la brume du Couvre Feu n'y entre pas).
        public readonly List<RoomLink> links = new List<RoomLink>();
        public readonly Dictionary<Vector2Int, int> shafts = new Dictionary<Vector2Int, int>();
        public readonly Dictionary<(int, int, bool), (float low, float high)> wallHoles = new Dictionary<(int, int, bool), (float, float)>();
        public readonly List<Bounds> undergroundBounds = new List<Bounds>();
        public readonly List<Rect> airLines = new List<Rect>();  // emprises au sol des tubes aériens
        public readonly List<Rect> deckBlocks = new List<Rect>();  // paliers et escaliers des liaisons aériennes
        public readonly Vector3 origin;   // coin (x et z minimaux) de la cellule (0, 0)
        public readonly bool hub;
        public readonly Vector2Int start; // cellule d'arrivée (derrière la porte du Hub ou la porte à orbes)
        public int doorCol = -1;          // zones impaires : colonne de la porte à orbes (mur nord)
        public int entryCol = -1;         // zones paires : colonne de la porte à orbes de la zone précédente (mur sud)
        public RectInt neutralRoom, acquisitionRoom;
        public readonly bool[,] fixedCell; // Neutral Room et Acquisition Room : hors du labyrinthe tiré au hasard
        public readonly HashSet<Vector2Int> keepClear = new HashSet<Vector2Int>(); // cellules sans décor ni piège
        public readonly bool[,] eastWall, northWall; // mur entre (i, j) et (i + 1, j) / entre (i, j) et (i, j + 1)
        public readonly int[,] room;                 // -1 = couloir
        public readonly List<Room> rooms = new List<Room>();
        // Hauteur du mur nord de chaque colonne (la zone suivante complète au-dessus si son plafond est plus haut).
        public float[] northHeights;
        public float[] southHeights;                 // mur nord de la zone précédente (null pour la Zone 1)

        public Maze(int zone, int n, Vector3 origin, bool hub, Vector2Int start)
        {
            this.zone = zone;
            N = n;
            this.origin = origin;
            this.hub = hub;
            this.start = start;
            eastWall = new bool[n, n];
            northWall = new bool[n, n];
            room = new int[n, n];
            fixedCell = new bool[n, n];
        }

        public bool InGrid(int i, int j) => i >= 0 && j >= 0 && i < N && j < N;
        public bool Reserved(int i, int j) => hub && i >= HubMin && i <= HubMax && j >= 0 && j < HubRows;
        public bool Fixed(int i, int j) => InGrid(i, j) && fixedCell[i, j];
        public Vector3 Center(int i, int j) => origin + new Vector3((i + 0.5f) * Cell, 0f, (j + 0.5f) * Cell);

        public bool CellAt(Vector3 p, out int i, out int j)
        {
            i = Mathf.FloorToInt((p.x - origin.x) / Cell);
            j = Mathf.FloorToInt((p.z - origin.z) / Cell);
            return InGrid(i, j);
        }

        public bool HasWallHole(int i, int j, int dir)
        {
            switch (dir)
            {
                case 0: return wallHoles.ContainsKey((i, j, false));
                case 1: return wallHoles.ContainsKey((i - 1, j, false));
                case 2: return wallHoles.ContainsKey((i, j, true));
                default: return wallHoles.ContainsKey((i, j - 1, true));
            }
        }

        public Room RoomAt(int i, int j) => InGrid(i, j) && room[i, j] >= 0 ? rooms[room[i, j]] : null;
        public float Floor(int i, int j) => RoomAt(i, j)?.floor ?? 0f;
        public bool IsPit(int i, int j) => RoomAt(i, j)?.style == RoomStyle.Pit;
        // Cellule sans sol d'un seul tenant (fosse, ou trémie d'une liaison souterraine).
        public bool NoPlainFloor(int i, int j) => IsPit(i, j) || shafts.ContainsKey(new Vector2Int(i, j));

        // Hauteur du plafond ; hors de la grille, celle des couloirs.
        public float Ceil(int i, int j)
        {
            if (!InGrid(i, j)) return WallHeight;
            return Reserved(i, j) ? WallHeight : RoomAt(i, j)?.ceil ?? WallHeight;
        }
    }

    // Paramètres de génération d'une zone (chapitre 9 du GDD).
    class ZoneStyle
    {
        public int maxRooms, minRoomSize, maxRoomSize;
        // Part des cellules occupées par des salles (0 : seulement 'maxRooms' salles, espacées d'une cellule de couloir).
        // Avec une part visée, les salles peuvent se toucher.
        public float roomCoverage;
        public float pitChance, minPitDepth, maxPitDepth;
        public float maxLayerDepth;     // profondeur d'une couche (plusieurs couches si la zone est assez profonde)
        public float bridgeChance;      // chance qu'une fosse soit enjambée par une passerelle ou une poutre
        public float maxHeight;         // plafond le plus haut, au-dessus du niveau du Hub
        public float[] floors;          // plancher des salles (au-dessus du niveau du Hub)
        public float[] ceilings;        // hauteur sous plafond des salles
        public float[] chaoticCeilings; // salles chaotiques (plateformes)
        public float[] pitCeilings;     // hauteur au-dessus du pourtour des salles à profondeurs
    }

    // V0.6 : chaque salle a sa propre profondeur et sa propre hauteur, tirées au hasard ; la profondeur maximale vaut
    // nZone x 4 et la hauteur maximale nZone x 10 (par rapport au niveau du Hub). Environ 25 % de couloirs pour 75 % de salles.
    static ZoneStyle StyleFor(int zone)
    {
        float maxHeight = 10f * zone, maxDepth = 4f * zone;
        float H(float k) => Mathf.Max(4f, Mathf.Round(4f + (maxHeight - 4f) * k));
        bool first = zone == 1;
        return new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 2, maxRoomSize = first ? 4 : 5, roomCoverage = 0.75f,
            pitChance = first ? 0.2f : 0.3f, minPitDepth = 1.5f, maxPitDepth = maxDepth, maxLayerDepth = first ? 3f : 4f,
            bridgeChance = first ? 0f : 0.6f, maxHeight = maxHeight,
            floors = first ? new[] { 0f, 0f, 0.8f, 1.6f } : new[] { 0f, 0f, 0.8f, 1.6f, 2.4f },
            ceilings = new[] { H(0f), H(0.15f), H(0.3f), H(0.5f) },
            chaoticCeilings = new[] { H(0.5f), H(0.75f), H(1f) },
            pitCeilings = new[] { H(0.1f) + 1f, H(0.25f), H(0.4f) },
        };
    }

    const float ZoneDoorWidth = 3.4f, ZoneDoorHeight = 3.6f;

    // Profondeurs (chapitre 9.1) : grille fine de 0,5 m, pourtour praticable de 1,5 m le long des murs de la salle,
    // escaliers un peu plus raides que ceux des salles surélevées.
    const float Sub = 0.5f;
    const int SubPerCell = 10;
    const int DepthMargin = 3;
    const float DepthStepHeight = 0.25f;

    // Mur sur le côté 'dir' (0 = +X, 1 = -X, 2 = +Z, 3 = -Z) de la cellule.
    static bool HasWall(Maze m, int i, int j, int dir)
    {
        switch (dir)
        {
            case 0: return i == m.N - 1 || m.eastWall[i, j];
            case 1: return i == 0 || m.eastWall[i - 1, j];
            case 2: return j == m.N - 1 || m.northWall[i, j];
            default: return j == 0 || m.northWall[i, j - 1];
        }
    }

    static void SetWall(Maze m, int i, int j, int dir, bool value)
    {
        switch (dir)
        {
            case 0: if (i < m.N - 1) m.eastWall[i, j] = value; break;
            case 1: if (i > 0) m.eastWall[i - 1, j] = value; break;
            case 2: if (j < m.N - 1) m.northWall[i, j] = value; break;
            default: if (j > 0) m.northWall[i, j - 1] = value; break;
        }
    }

    static readonly int[] DI = { 1, -1, 0, 0 }, DJ = { 0, 0, 1, -1 };

    static T Pick<T>(System.Random rnd, params T[] values) => values[rnd.Next(values.Length)];

    static void Generate(Maze m, System.Random rnd, ZoneStyle style)
    {
        int n = m.N;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                m.eastWall[i, j] = m.northWall[i, j] = true;
                m.room[i, j] = -1;
            }

        // Labyrinthe parfait (exploration en profondeur) à partir de l'entrée de la zone ; la Neutral Room et l'Acquisition
        // Room n'en font pas partie (elles s'y ouvrent ensuite).
        Vector2Int seedCell = m.Fixed(m.start.x, m.start.y) ? new Vector2Int(m.entryCol + 2, 1) : m.start;
        var visited = new bool[n, n];
        var stack = new Stack<Vector2Int>();
        stack.Push(seedCell);
        visited[seedCell.x, seedCell.y] = true;
        var dirs = new List<int>(4);
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Peek();
            dirs.Clear();
            for (int d = 0; d < 4; d++)
            {
                int ni = c.x + DI[d], nj = c.y + DJ[d];
                if (m.InGrid(ni, nj) && !m.Reserved(ni, nj) && !m.Fixed(ni, nj) && !visited[ni, nj]) dirs.Add(d);
            }
            if (dirs.Count == 0) { stack.Pop(); continue; }
            int dir = dirs[rnd.Next(dirs.Count)];
            SetWall(m, c.x, c.y, dir, false);
            visited[c.x + DI[dir], c.y + DJ[dir]] = true;
            stack.Push(new Vector2Int(c.x + DI[dir], c.y + DJ[dir]));
        }

        // Quelques boucles pour éviter un labyrinthe trop linéaire.
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (m.Reserved(i, j) || m.Fixed(i, j)) continue;
                if (i < n - 1 && !m.Reserved(i + 1, j) && !m.Fixed(i + 1, j) && rnd.NextDouble() < 0.12) m.eastWall[i, j] = false;
                if (j < n - 1 && !m.Reserved(i, j + 1) && !m.Fixed(i, j + 1) && rnd.NextDouble() < 0.12) m.northWall[i, j] = false;
            }

        // Salles : blocs de cellules ouverts, chacun avec sa hauteur de plancher et de plafond.
        bool coverage = style.roomCoverage > 0f;
        int free = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (!m.Reserved(i, j) && !m.Fixed(i, j)) free++;
        int target = coverage ? Mathf.RoundToInt(free * style.roomCoverage) : int.MaxValue, covered = 0;
        int attempts = coverage ? 12000 : 700;
        for (int attempt = 0; attempt < attempts && m.rooms.Count < style.maxRooms && covered < target; attempt++)
        {
            int w, h;
            if (coverage)
            {
                // Tailles variées ; une fois la place devenue rare, de petites salles bouchent les trous.
                int min = attempt < attempts / 2 ? style.minRoomSize : 1;
                w = rnd.Next(min, style.maxRoomSize + 1);
                h = rnd.Next(min, style.maxRoomSize + 1);
                if (w * h < 2) continue;
            }
            else if (style.maxRoomSize <= 4)
            {
                if (rnd.NextDouble() < 0.15) { w = 4; h = 3; }
                else { w = rnd.NextDouble() < 0.4 ? 3 : 2; h = 2; }
            }
            else
            {
                w = rnd.Next(style.minRoomSize, style.maxRoomSize + 1);
                h = rnd.Next(style.minRoomSize, Mathf.Min(w + 1, style.maxRoomSize) + 1);
            }
            if (rnd.NextDouble() < 0.5) { int t = w; w = h; h = t; }
            int i0 = rnd.Next(0, n - w + 1), j0 = rnd.Next(0, n - h + 1);
            bool ok = true;
            for (int i = i0 - 1; i <= i0 + w && ok; i++)
                for (int j = j0 - 1; j <= j0 + h && ok; j++)
                {
                    var cell = new Vector2Int(i, j);
                    bool inside = i >= i0 && i < i0 + w && j >= j0 && j < j0 + h;
                    // Une cellule d'écart avec le Hub et les portes ; avec les autres salles aussi, sauf si elles peuvent se toucher.
                    if (m.InGrid(i, j) && (m.Reserved(i, j) || m.Fixed(i, j) || cell == m.start || m.keepClear.Contains(cell)
                        || (m.room[i, j] >= 0 && (inside || !coverage)))) ok = false;
                }
            if (!ok) continue;

            var room = new Room { rect = new RectInt(i0, j0, w, h) };
            // Salle à profondeurs : pourtour au niveau du Hub, fosses de formes variées et escaliers pour y descendre.
            if (w >= 2 && h >= 2 && rnd.NextDouble() < style.pitChance && GenerateDepths(room, style, rnd))
            {
                room.style = RoomStyle.Pit;
                room.floor = 0f;
                room.ceil = Pick(rnd, style.pitCeilings);
            }
            else if (m.zone >= 2 && w >= 3 && h >= 3 && rnd.NextDouble() < TemplateChance)
            {
                // Chapitre 10.3 (V0.6.3) : grande salle à plateformes, escaliers, passerelles et îlots.
                room.style = RoomStyle.Template;
                room.floor = 0f;
                room.ceil = Mathf.Min(style.maxHeight, Mathf.Max(TemplateMinCeil, Pick(rnd, style.ceilings)));
            }
            else
            {
                room.style = (RoomStyle)rnd.Next(4);
                // Les salles chaotiques restent de plain-pied : leurs plateformes font le relief.
                room.floor = room.style == RoomStyle.Chaotic ? 0f : Pick(rnd, style.floors);
                room.ceil = Mathf.Min(style.maxHeight, room.floor + Pick(rnd, room.style == RoomStyle.Chaotic ? style.chaoticCeilings : style.ceilings));
                room.chess = rnd.NextDouble() < 0.2;
            }
            int index = m.rooms.Count;
            m.rooms.Add(room);
            covered += w * h;
            for (int i = i0; i < i0 + w; i++)
                for (int j = j0; j < j0 + h; j++)
                {
                    m.room[i, j] = index;
                    room.cells.Add(new Vector2Int(i, j));
                    if (i < i0 + w - 1) m.eastWall[i, j] = false;
                    if (j < j0 + h - 1) m.northWall[i, j] = false;
                }
        }
    }

    // ---------- Profondeurs (chapitre 9.1) ----------

    // Creuse les profondeurs d'une salle : une fosse en rectangle, en L, deux fosses séparées par une passerelle, ou une
    // grande fosse à deux escaliers. Chaque fosse a au moins un escalier (les entités y descendent) ; si la zone est assez
    // profonde, une fosse peut en contenir une autre, plus bas, avec son propre escalier. Retourne false si rien n'a tenu.
    static bool GenerateDepths(Room room, ZoneStyle style, System.Random rnd)
    {
        int w = room.rect.width * SubPerCell, h = room.rect.height * SubPerCell;
        room.levels = new float[w, h];
        room.stairCell = new bool[w, h];
        var inner = new RectInt(DepthMargin, DepthMargin, w - 2 * DepthMargin, h - 2 * DepthMargin);

        var shapes = new List<(List<Vector2Int> cells, int stairs)>();
        switch (rnd.Next(4))
        {
            case 0:
                shapes.Add((CellsOf(RandomSubRect(inner, 0.55f, 1f, rnd), default), 1));
                break;
            case 1:
            {
                RectInt r = RandomSubRect(inner, 0.8f, 1f, rnd);
                int cw = Mathf.RoundToInt(r.width * Rand(rnd, 0.35f, 0.55f)), chh = Mathf.RoundToInt(r.height * Rand(rnd, 0.35f, 0.55f));
                var cut = new RectInt(rnd.Next(2) == 0 ? r.xMin : r.xMax - cw, rnd.Next(2) == 0 ? r.yMin : r.yMax - chh, cw, chh);
                shapes.Add((CellsOf(r, cut), 1));
                break;
            }
            case 2:
            {
                // Passerelle de 1,5 à 2,5 m entre les deux fosses, reliée au pourtour.
                bool alongX = inner.width >= inner.height;
                int length = alongX ? inner.width : inner.height, gap = 3 + rnd.Next(3);
                int first = (length - gap) / 2 + rnd.Next(-2, 3);
                int second = length - gap - first;
                if (first < 8 || second < 8) goto case 0;
                RectInt a = alongX ? new RectInt(inner.xMin, inner.yMin, first, inner.height) : new RectInt(inner.xMin, inner.yMin, inner.width, first);
                RectInt b = alongX ? new RectInt(inner.xMin + first + gap, inner.yMin, second, inner.height) : new RectInt(inner.xMin, inner.yMin + first + gap, inner.width, second);
                shapes.Add((CellsOf(RandomSubRect(a, 0.75f, 1f, rnd), default), 1));
                shapes.Add((CellsOf(RandomSubRect(b, 0.75f, 1f, rnd), default), 1));
                break;
            }
            default:
                shapes.Add((CellsOf(RandomSubRect(inner, 0.85f, 1f, rnd), default), 2));
                break;
        }

        bool any = false;
        room.bridgeCell = new bool[w, h];
        foreach (var (cells, stairs) in shapes)
        {
            if (cells.Count == 0 || !Dig(room, cells, 0f, style, rnd, stairs, 0)) continue;
            any = true;
            if (rnd.NextDouble() < style.bridgeChance) PlaceBridge(room, new HashSet<Vector2Int>(cells), rnd);
        }
        if (!any)
        {
            room.levels = null;
            room.stairCell = null;
            return false;
        }
        float lowest = 0f;
        foreach (var (_, level) in room.depths) lowest = Mathf.Min(lowest, level);
        room.pitDepth = -lowest;
        return true;
    }

    // Sous-rectangle au hasard de 'r' (chaque côté entre 'min' et 'max' fois celui de 'r').
    static RectInt RandomSubRect(RectInt r, float min, float max, System.Random rnd)
    {
        int w = Mathf.Clamp(Mathf.RoundToInt(r.width * Rand(rnd, min, max)), 1, r.width);
        int h = Mathf.Clamp(Mathf.RoundToInt(r.height * Rand(rnd, min, max)), 1, r.height);
        return new RectInt(r.xMin + rnd.Next(r.width - w + 1), r.yMin + rnd.Next(r.height - h + 1), w, h);
    }

    // Cases de 'r', moins celles de 'cut'.
    static List<Vector2Int> CellsOf(RectInt r, RectInt cut)
    {
        var cells = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
                if (!cut.Contains(new Vector2Int(x, z))) cells.Add(new Vector2Int(x, z));
        return cells;
    }

    // Creuse les cases 'cells' (toutes au niveau 'parent') d'une couche, avec 'stairs' escaliers ; moins profond si
    // l'escalier ne tient pas. Puis, s'il reste de la profondeur, une couche plus bas au milieu de celle-ci.
    static bool Dig(Room room, List<Vector2Int> cells, float parent, ZoneStyle style, System.Random rnd, int stairs, int layer)
    {
        float available = style.maxPitDepth + parent;
        if (available < style.minPitDepth - 0.01f) return false;
        float wanted = Rand(rnd, style.minPitDepth, Mathf.Min(style.maxLayerDepth, available));
        var region = new HashSet<Vector2Int>(cells);
        for (float depth = Mathf.Round(wanted / DepthStepHeight) * DepthStepHeight; depth >= style.minPitDepth - 0.01f; depth -= 0.5f)
        {
            float level = parent - depth;
            foreach (var c in cells) room.levels[c.x, c.y] = level;
            int placed = 0;
            for (int s = 0; s < stairs; s++) if (PlaceStair(room, region, parent, level, rnd)) placed++;
            if (placed == 0)
            {
                foreach (var c in cells) room.levels[c.x, c.y] = parent;
                continue;
            }
            room.depths.Add((cells, level));
            if (layer < 2 && rnd.NextDouble() < 0.7) DigInside(room, region, level, style, rnd, layer + 1);
            return true;
        }
        return false;
    }

    // Couche plus profonde : un rectangle au milieu de 'region', à 1,5 m de ses bords et loin de ses escaliers.
    static void DigInside(Room room, HashSet<Vector2Int> region, float level, ZoneStyle style, System.Random rnd, int layer)
    {
        if (style.maxPitDepth + level < style.minPitDepth - 0.01f) return;
        var core = new HashSet<Vector2Int>();
        int xMin = int.MaxValue, xMax = int.MinValue, zMin = int.MaxValue, zMax = int.MinValue;
        foreach (var c in region)
        {
            bool ok = true;
            for (int dx = -DepthMargin; dx <= DepthMargin && ok; dx++)
                for (int dz = -DepthMargin; dz <= DepthMargin && ok; dz++)
                {
                    var q = new Vector2Int(c.x + dx, c.y + dz);
                    if (!region.Contains(q) || room.stairCell[q.x, q.y]) ok = false;
                }
            if (!ok) continue;
            core.Add(c);
            xMin = Mathf.Min(xMin, c.x); xMax = Mathf.Max(xMax, c.x);
            zMin = Mathf.Min(zMin, c.y); zMax = Mathf.Max(zMax, c.y);
        }
        if (core.Count < 36) return;
        var bounds = new RectInt(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            RectInt r = RandomSubRect(bounds, 0.5f, 1f, rnd);
            if (r.width < 6 || r.height < 6) continue;
            var cells = CellsOf(r, default);
            if (cells.TrueForAll(core.Contains))
            {
                Dig(room, cells, level, style, rnd, 1, layer);
                return;
            }
        }
    }

    // Escalier dans 'region' (au niveau 'bottom'), collé à son bord : le haut donne sur le niveau 'top', un palier
    // de 1 m au pied.
    static bool PlaceStair(Room room, HashSet<Vector2Int> region, float top, float bottom, System.Random rnd)
    {
        int steps = Mathf.RoundToInt((top - bottom) / DepthStepHeight);
        int length = Mathf.CeilToInt(steps * StepDepth / Sub);
        int width = 3 + rnd.Next(2);
        int w = room.levels.GetLength(0), h = room.levels.GetLength(1);
        var list = new List<Vector2Int>(region);
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int dir = rnd.Next(4);
            var fwd = new Vector2Int(DI[dir], DJ[dir]);
            var side = new Vector2Int(Mathf.Abs(DJ[dir]), Mathf.Abs(DI[dir]));
            Vector2Int start = list[rnd.Next(list.Count)];
            bool ok = true;
            for (int a = 0; a < length + 2 && ok; a++)
                for (int b = 0; b < width && ok; b++)
                {
                    var c = start + fwd * a + side * b;
                    if (!region.Contains(c) || room.stairCell[c.x, c.y]) ok = false;
                }
            for (int b = 0; b < width && ok; b++)
            {
                var c = start - fwd + side * b;
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h || region.Contains(c) || room.stairCell[c.x, c.y]
                    || Mathf.Abs(room.levels[c.x, c.y] - top) > 0.01f) ok = false;
            }
            if (!ok) continue;

            Vector2Int far = start + fwd * (length - 1) + side * (width - 1);
            var strip = new RectInt(Mathf.Min(start.x, far.x), Mathf.Min(start.y, far.y), Mathf.Abs(far.x - start.x) + 1, Mathf.Abs(far.y - start.y) + 1);
            for (int x = strip.xMin; x < strip.xMax; x++)
                for (int z = strip.yMin; z < strip.yMax; z++)
                    room.stairCell[x, z] = true;
            for (int b = 0; b < width; b++) room.openEdges.Add(EdgeKey(start + side * b, start - fwd + side * b));
            room.stairs.Add(new Stair { strip = strip, dir = dir, steps = steps, top = top, bottom = bottom });
            return true;
        }
        return false;
    }

    // Passerelle (1 à 1,5 m de large) ou poutre (35 cm) d'un bord à l'autre d'une fosse, au niveau du Hub, sur une ligne
    // droite qui ne croise aucun escalier et qui s'appuie de chaque côté sur le pourtour.
    static void PlaceBridge(Room room, HashSet<Vector2Int> region, System.Random rnd)
    {
        int w = room.levels.GetLength(0), h = room.levels.GetLength(1);
        bool beam = rnd.NextDouble() < 0.4;
        int width = beam ? 1 : 2 + rnd.Next(2);
        bool Rim(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < w && c.y < h && !region.Contains(c) && !room.stairCell[c.x, c.y]
            && Mathf.Abs(room.levels[c.x, c.y]) < 0.01f;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            bool alongX = rnd.Next(2) == 0;
            var fwd = alongX ? Vector2Int.right : Vector2Int.up;
            var side = alongX ? Vector2Int.up : Vector2Int.right;
            Vector2Int start = new List<Vector2Int>(region)[rnd.Next(region.Count)];
            // Recule jusqu'au bord de la fosse, puis avance jusqu'à l'autre bord.
            while (region.Contains(start - fwd)) start -= fwd;
            int length = 0;
            while (region.Contains(start + fwd * length)) length++;
            if (length < 8) continue;
            bool ok = true;
            for (int b = 0; b < width && ok; b++)
            {
                if (!Rim(start - fwd + side * b) || !Rim(start + fwd * length + side * b)) ok = false;
                for (int a = 0; a < length && ok; a++)
                {
                    var c = start + fwd * a + side * b;
                    if (!region.Contains(c) || room.stairCell[c.x, c.y]) ok = false;
                }
            }
            if (!ok) continue;
            var far = start + fwd * (length - 1) + side * (width - 1);
            var cells = new RectInt(start.x, start.y, far.x - start.x + 1, far.y - start.y + 1);
            for (int x = cells.xMin; x < cells.xMax; x++)
                for (int z = cells.yMin; z < cells.yMax; z++)
                    room.bridgeCell[x, z] = true;
            room.bridges.Add((cells, beam));
            return;
        }
    }

    static (Vector2Int, Vector2Int) EdgeKey(Vector2Int a, Vector2Int b) =>
        a.x < b.x || (a.x == b.x && a.y < b.y) ? (a, b) : (b, a);

    // Colonne de la porte à orbes de la zone impaire 'zone' (dans son mur nord, du côté opposé à l'ouverture du Hub,
    // note 01 du chapitre 9). Tirée d'après la graine seule : la zone paire suivante place sa Neutral Room derrière.
    static int DoorColumn(int seed, int zone) => new System.Random(seed * 7349 + zone * 104729).Next(3, ZoneCells - 3);

    // Grille de la zone, avant le tirage du labyrinthe : cellule d'arrivée, porte, Neutral Room et Acquisition Room.
    static Maze CreateMaze(int seed, int zone)
    {
        bool odd = zone % 2 == 1;
        int n = ZoneCells;
        var start = odd ? new Vector2Int(HubCol, HubRows) : new Vector2Int(DoorColumn(seed, zone - 1), 0);
        var m = new Maze(zone, n, ZoneOrigin(zone), odd, start);
        m.keepClear.Add(start);
        if (odd)
        {
            // Devant la porte à orbes : trois cellules sans salle ni décor, réunies ensuite en antichambre.
            m.doorCol = DoorColumn(seed, zone);
            for (int di = -1; di <= 1; di++) m.keepClear.Add(new Vector2Int(m.doorCol + di, n - 1));
        }
        else
        {
            m.entryCol = start.x;
            m.neutralRoom = new RectInt(m.entryCol - 1, 0, 3, 3);
            m.acquisitionRoom = new RectInt(HubCol - 2, n - 3, 5, 3);
            foreach (var r in new[] { m.neutralRoom, m.acquisitionRoom })
                for (int i = r.xMin; i < r.xMax; i++)
                    for (int j = r.yMin; j < r.yMax; j++)
                    {
                        m.fixedCell[i, j] = true;
                        m.keepClear.Add(new Vector2Int(i, j));
                    }
        }
        return m;
    }

    // Après le labyrinthe : antichambre de la porte à orbes (zones impaires) ; Neutral Room et Acquisition Room de plain-pied
    // (zones paires), fermées sauf leurs ouvertures vers le labyrinthe. Le mur du fond de la Neutral Room porte les glyphes.
    static void StampFixedRooms(Maze m)
    {
        if (m.hub)
        {
            m.eastWall[m.doorCol - 1, m.N - 1] = m.eastWall[m.doorCol, m.N - 1] = false;
            return;
        }
        AddFixedRoom(m, m.neutralRoom, 6f);
        AddFixedRoom(m, m.acquisitionRoom, 7f);
        SetWall(m, m.entryCol - 1, 1, 1, false);
        SetWall(m, m.entryCol + 1, 1, 0, false);
        RectInt ar = m.acquisitionRoom;
        SetWall(m, HubCol, ar.yMin, 3, false);
        SetWall(m, ar.xMin, ar.yMin + 1, 1, false);
        SetWall(m, ar.xMax - 1, ar.yMin + 1, 0, false);
    }

    static void AddFixedRoom(Maze m, RectInt rect, float ceil)
    {
        var room = new Room { rect = rect, style = RoomStyle.Columns, floor = 0f, ceil = ceil, plain = true };
        int index = m.rooms.Count;
        m.rooms.Add(room);
        for (int i = rect.xMin; i < rect.xMax; i++)
            for (int j = rect.yMin; j < rect.yMax; j++)
            {
                m.room[i, j] = index;
                room.cells.Add(new Vector2Int(i, j));
                if (i < rect.xMax - 1) m.eastWall[i, j] = false;
                if (j < rect.yMax - 1) m.northWall[i, j] = false;
            }
    }

    class Chunk
    {
        public MeshBuilder floor = new MeshBuilder(), ceiling = new MeshBuilder();
        public MeshBuilder checker = new MeshBuilder();
    }

    // Génère et construit la zone 'zone' (1, 2, 3...). 'previous' : la zone précédente, déjà construite (sauf pour la
    // Zone 1), dont le mur nord sert de mur sud à celle-ci.
    public static ZoneRefs BuildZone(Transform parent, int seed, int zone, ZoneRefs previous)
    {
        var rnd = new System.Random(seed * 31 + zone * 7919);
        Maze m = CreateMaze(seed, zone);
        if (previous != null) m.southHeights = previous.maze.northHeights;
        Generate(m, rnd, StyleFor(zone));
        StampFixedRooms(m);
        ChooseStatueRoom(m, new System.Random(seed * 83 + zone * 457));
        ChooseLinks(m, new System.Random(seed * 89 + zone * 281));

        var refs = new ZoneRefs { index = zone, maze = m, root = new GameObject("Zone " + zone) };
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;
        refs.underground = m.undergroundBounds;
        refs.entrance = m.Center(m.start.x, m.start.y);

        BuildGeometry(m, root, colliders);
        if (m.hub) BuildZoneDoor(m, root, refs);
        else BuildGlyphRooms(m, root, refs, new System.Random(seed * 59 + zone * 613));
        BuildTemplates(m, root, colliders.transform, new System.Random(seed * 71 + zone * 733));
        BuildArcades(m, root, refs, new System.Random(seed * 67 + zone * 389));

        // Les collisions doivent être à jour pour poser le décor sur les sols (dalles, escaliers, fosses, plateformes).
        Physics.SyncTransforms();
        Decorate(m, root, colliders.transform, rnd, refs.itemSpots);
        MarkIntangibleSpheres(root);
        refs.mist = BuildMist(m, root);
        refs.nav = new MazeNav();
        refs.nav.AddZone(m.origin, m.N, Cell, (i, j) => !m.Reserved(i, j), (i, j) => m.Ceil(i, j));
        refs.nav.Prepare(colliders.transform, refs.door != null ? refs.door.Blocker : null);
        AddReflectionProbes(m, root);
        return refs;
    }

    static void BuildGeometry(Maze m, Transform root, GameObject colliders)
    {
        int n = m.N;
        int chunks = (n + ChunkSize - 1) / ChunkSize;
        var chunkData = new Chunk[chunks, chunks];
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
                chunkData[x, y] = new Chunk();
        Chunk ChunkOf(int i, int j) => chunkData[Mathf.Clamp(i, 0, n - 1) / ChunkSize, Mathf.Clamp(j, 0, n - 1) / ChunkSize];
        // Murs découpés plus finement que les sols : chaque morceau reçoit assez peu de lampes pour qu'elles soient toutes
        // calculées précisément, sans rupture de lumière d'un morceau à l'autre.
        int wallChunks = (n + WallChunkSize - 1) / WallChunkSize;
        var wallData = new MeshBuilder[wallChunks, wallChunks];
        MeshBuilder WallsOf(int i, int j) => wallData[Mathf.Clamp(i, 0, n - 1) / WallChunkSize, Mathf.Clamp(j, 0, n - 1) / WallChunkSize] ??= new MeshBuilder();

        void AddCollider(Vector3 center, Vector3 size)
        {
            var bc = colliders.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        float x0 = m.origin.x, z0 = m.origin.z;

        // Sols et plafonds (les fosses ont leur propre sol).
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (m.Reserved(i, j)) continue;
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f;
                Chunk ch = ChunkOf(i, j);
                if (!m.NoPlainFloor(i, j))
                    ch.floor.WorldQuad(c + new Vector3(-e, 0, -e), c + new Vector3(e, 0, -e), c + new Vector3(e, 0, e), c + new Vector3(-e, 0, e), Vector3.up, 0.5f);
                Vector3 top = c + Vector3.up * m.Ceil(i, j);
                ch.ceiling.WorldQuad(top + new Vector3(-e, 0, -e), top + new Vector3(e, 0, -e), top + new Vector3(e, 0, e), top + new Vector3(-e, 0, e), Vector3.down, 0.25f);
            }
        // Sol : une collision par suite de cellules sans fosse sur chaque rangée.
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n;)
            {
                if (m.NoPlainFloor(i, j)) { i++; continue; }
                int start = i;
                while (i < n && !m.NoPlainFloor(i, j)) i++;
                float a = x0 + start * Cell, b = x0 + i * Cell;
                AddCollider(new Vector3((a + b) * 0.5f, -0.5f, m.Center(0, j).z), new Vector3(b - a, 1f, Cell));
            }
        // Cellules à trémie : quatre bandes de sol autour de l'ouverture.
        foreach (var kv in m.shafts)
        {
            Vector3 c = m.Center(kv.Key.x, kv.Key.y);
            Rect hole = ShaftRect(m, kv.Key, kv.Value);
            float e = Cell * 0.5f;
            var strips = new[]
            {
                Rect.MinMaxRect(c.x - e, c.z - e, c.x + e, hole.yMin), Rect.MinMaxRect(c.x - e, hole.yMax, c.x + e, c.z + e),
                Rect.MinMaxRect(c.x - e, hole.yMin, hole.xMin, hole.yMax), Rect.MinMaxRect(hole.xMax, hole.yMin, c.x + e, hole.yMax),
            };
            foreach (Rect r in strips)
            {
                ChunkOf(kv.Key.x, kv.Key.y).floor.WorldQuad(new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMax), new Vector3(r.xMin, 0f, r.yMax), Vector3.up, 0.5f);
                AddCollider(new Vector3(r.center.x, -0.5f, r.center.y), new Vector3(r.width, 1f, r.height));
            }
        }
        // Plafonds : une collision par suite de cellules de même hauteur sur chaque rangée (rien au-dessus du Hub).
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n;)
            {
                if (m.Reserved(i, j)) { i++; continue; }
                float ceil = m.Ceil(i, j);
                int start = i;
                while (i < n && !m.Reserved(i, j) && Mathf.Approximately(m.Ceil(i, j), ceil)) i++;
                float a = x0 + start * Cell, b = x0 + i * Cell;
                AddCollider(new Vector3((a + b) * 0.5f, ceil + 0.5f, m.Center(0, j).z), new Vector3(b - a, 1f, Cell));
            }

        // Ouverture dans un mur (porte) : deux piliers et un linteau.
        void Doorway(MeshBuilder mb, Vector3 edge, bool alongX, float h, float width, float height, float tex)
        {
            float side = (Cell - Wall - width) * 0.5f;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 Size(float length, float hh) => alongX ? new Vector3(length, hh, Wall) : new Vector3(Wall, hh, length);
            foreach (float s in new[] { -1f, 1f })
            {
                var center = edge + axis * (s * (width * 0.5f + side * 0.5f)) + Vector3.up * (h * 0.5f);
                // Le pan s'engage sous le poteau (voir JointOverlap).
                mb.Box(center + axis * (s * JointOverlap * 0.5f), Size(side + JointOverlap, h), tex, 0b0011_0011);
                AddCollider(center, Size(side, h));
            }
            var header = edge + Vector3.up * ((height + h) * 0.5f);
            mb.Box(header, Size(width, h - height), tex, alongX ? 0b0011_1000 : 0b0000_1011);
            AddCollider(header, Size(width, h - height));
        }

        // Linteau d'une arête ouverte, percé pour laisser passer un tube aérien.
        void PiercedLintel(MeshBuilder mb, float x, float z, bool alongX, float low, float h, (float low, float high) hole, float tex)
        {
            // Longueur visible limitée à celle d'un pan de mur (au-delà, elle se confondrait avec le pan voisin) ; collisions complètes.
            float side = (WallRun - AerialHoleWidth) * 0.5f, solidSide = (Cell + Wall - AerialHoleWidth) * 0.5f;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            Vector3 Size(float length, float height) => alongX ? new Vector3(length, height, Wall) : new Vector3(Wall, height, length);
            void Piece(Vector3 center, float length, float height)
            {
                if (height <= 0.001f) return;
                mb.Box(center, Size(length, height), tex);
                AddCollider(center, Size(length, height));
            }
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 mid = new Vector3(x, (low + h) * 0.5f, z);
                mb.Box(mid + axis * (s * (AerialHoleWidth * 0.5f + side * 0.5f)), Size(side, h - low), tex);
                AddCollider(mid + axis * (s * (AerialHoleWidth * 0.5f + solidSide * 0.5f)), Size(solidSide, h - low));
            }
            float y0 = Mathf.Clamp(hole.low, low, h), y1 = Mathf.Clamp(hole.high, low, h);
            Piece(new Vector3(x, (low + y0) * 0.5f, z), AerialHoleWidth, y0 - low);
            Piece(new Vector3(x, (y1 + h) * 0.5f, z), AerialHoleWidth, h - y1);
        }

        // Murs : un segment par arête, entre deux poteaux, aussi haut que le plus haut des deux côtés.
        // Entre une salle haute et un couloir ouvert, un linteau ferme le haut de l'ouverture.
        var posts = new HashSet<Vector2Int>();
        for (int i = -1; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                // Arête verticale (le long de Z) à droite de la cellule (i, j) ; les bords est et ouest sont des murs pleins.
                bool leftIn = i >= 0, rightIn = i + 1 < n;
                bool boundary = !leftIn || !rightIn;
                bool wall;
                if (boundary) wall = true;
                else if (m.Reserved(i, j) && m.Reserved(i + 1, j)) wall = false;
                else wall = m.eastWall[i, j] || m.Reserved(i, j) || m.Reserved(i + 1, j);
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i + 1, j));
                float x = x0 + (i + 1) * Cell;
                float z = z0 + j * Cell + Cell * 0.5f;
                MeshBuilder mb = WallsOf(leftIn ? i : i + 1, j);
                const float tex = 0.5f;
                if (wall && m.wallHoles.TryGetValue((i, j, false), out var eastHole))
                {
                    // Liaison aérienne : ouverture dans le mur, avec son allège en dessous.
                    Doorway(mb, new Vector3(x, 0f, z), false, h, AerialHoleWidth, eastHole.high, tex);
                    var sill = new Vector3(x, eastHole.low * 0.5f, z);
                    mb.Box(sill, new Vector3(Wall, eastHole.low, AerialHoleWidth), tex);
                    AddCollider(sill, new Vector3(Wall, eastHole.low, AerialHoleWidth));
                    posts.Add(new Vector2Int(i + 1, j)); posts.Add(new Vector2Int(i + 1, j + 1));
                    continue;
                }
                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i + 1, j));
                    if (h - low < 0.01f) continue;
                    if (m.wallHoles.TryGetValue((i, j, false), out var eastLintelHole))
                    {
                        PiercedLintel(mb, x, z, false, low, h, eastLintelHole, tex);
                        continue;
                    }
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Wall, h - low, WallRun), tex, 0b0000_1011);
                    AddCollider(lintel, new Vector3(Wall, h - low, Cell + Wall));
                    continue;
                }
                mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, WallRun), tex, 0b0000_0011);
                AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, Cell + Wall));
                posts.Add(new Vector2Int(i + 1, j)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        m.northHeights = new float[n];
        for (int i = 0; i < n; i++)
            for (int j = -1; j < n; j++)
            {
                // Arête horizontale (le long de X) au-dessus de la cellule (i, j).
                bool lowIn = j >= 0, highIn = j + 1 < n;
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i, j + 1));
                float x = x0 + i * Cell + Cell * 0.5f;
                float z = z0 + (j + 1) * Cell;
                MeshBuilder mb = WallsOf(i, lowIn ? j : j + 1);
                const float tex = 0.5f;
                if (!lowIn && m.southHeights != null)
                {
                    // Mur sud : déjà construit par la zone précédente ; on le prolonge vers le haut si le plafond d'ici est
                    // plus haut que lui.
                    float built = m.southHeights[i];
                    if (h - built < 0.01f) continue;
                    var lintel = new Vector3(x, (built + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Cell + Wall, h - built, Wall), tex, 0b0011_1000);
                    AddCollider(lintel, new Vector3(Cell + Wall, h - built, Wall));
                    continue;
                }
                bool wall;
                if (!lowIn || !highIn) wall = true;
                else if (m.Reserved(i, j) && m.Reserved(i, j + 1)) wall = false;
                else wall = m.northWall[i, j] || m.Reserved(i, j) || m.Reserved(i, j + 1);
                if (!highIn) m.northHeights[i] = h;

                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i, j + 1));
                    if (h - low < 0.01f) continue;
                    if (m.wallHoles.TryGetValue((i, j, true), out var northLintelHole))
                    {
                        PiercedLintel(mb, x, z, true, low, h, northLintelHole, tex);
                        continue;
                    }
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(WallRun, h - low, Wall), tex, 0b0011_1000);
                    AddCollider(lintel, new Vector3(Cell + Wall, h - low, Wall));
                    continue;
                }
                if (m.wallHoles.TryGetValue((i, j, true), out var northHole))
                {
                    // Liaison aérienne : ouverture dans le mur, avec son allège en dessous.
                    Doorway(mb, new Vector3(x, 0f, z), true, h, AerialHoleWidth, northHole.high, tex);
                    var sill = new Vector3(x, northHole.low * 0.5f, z);
                    mb.Box(sill, new Vector3(AerialHoleWidth, northHole.low, Wall), tex);
                    AddCollider(sill, new Vector3(AerialHoleWidth, northHole.low, Wall));
                }
                else if (m.hub && i == HubCol && j == HubRows - 1)
                    Doorway(mb, new Vector3(x, 0f, z), true, h, DoorWidth, DoorHeight, tex); // porte du Hub
                else if (!highIn && m.hub && i == m.doorCol)
                    Doorway(mb, new Vector3(x, 0f, z), true, h, ZoneDoorWidth, ZoneDoorHeight, tex); // porte à orbes
                else if (!highIn && !m.hub && i == HubCol)
                    Doorway(mb, new Vector3(x, 0f, z), true, h, DoorWidth, DoorHeight, tex); // porte du chapiteau suivant
                else
                {
                    mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(WallRun, h, Wall), tex, 0b0011_0000);
                    AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Cell + Wall, h, Wall));
                }
                posts.Add(new Vector2Int(i, j + 1)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        foreach (var p in posts)
        {
            // Les poteaux du mur sud sont ceux de la zone précédente.
            if (p.y == 0 && m.southHeights != null) continue;
            float h = Mathf.Max(Mathf.Max(m.Ceil(p.x - 1, p.y - 1), m.Ceil(p.x, p.y - 1)), Mathf.Max(m.Ceil(p.x - 1, p.y), m.Ceil(p.x, p.y)));
            var c = new Vector3(x0 + p.x * Cell, h * 0.5f, z0 + p.y * Cell);
            WallsOf(p.x, p.y).Box(c, new Vector3(Wall, h, Wall), 0.5f, 0b0011_0011);
        }

        foreach (Room room in m.rooms)
        {
            if (room.style == RoomStyle.Pit)
            {
                BuildDepths(m, room, ChunkOf(room.rect.xMin, room.rect.yMin), colliders.transform, AddCollider);
                continue;
            }
            // Salles surélevées : dalle au niveau du plancher et escalier à chaque entrée, depuis le sol du voisin
            // (couloir ou salle plus basse ; une salle voisine plus haute construit elle-même l'escalier).
            if (room.floor <= 0f) continue;
            foreach (var cell in room.cells)
            {
                Vector3 c = m.Center(cell.x, cell.y);
                float minX = c.x - Cell * 0.5f, maxX = c.x + Cell * 0.5f, minZ = c.z - Cell * 0.5f, maxZ = c.z + Cell * 0.5f;
                Chunk ch = ChunkOf(cell.x, cell.y);
                for (int d = 0; d < 4; d++)
                {
                    int ni = cell.x + DI[d], nj = cell.y + DJ[d];
                    if (HasWall(m, cell.x, cell.y, d) || m.RoomAt(ni, nj) == room) continue;
                    float below = m.Floor(ni, nj);
                    if (below >= room.floor - 0.01f) continue;
                    int steps = Mathf.RoundToInt((room.floor - below) / StepHeight);
                    float depth = steps * StepDepth;
                    switch (d)
                    {
                        case 0: maxX -= depth; break;
                        case 1: minX += depth; break;
                        case 2: maxZ -= depth; break;
                        default: minZ += depth; break;
                    }
                    AddStairs(ch.floor, colliders.transform, c, d, steps, below, room.floor);
                }
                // Escaliers de part et d'autre d'une même cellule : ils se rejoignent, sans dalle entre eux.
                if (maxX - minX < 0.05f || maxZ - minZ < 0.05f) continue;
                var slab = new Vector3((minX + maxX) * 0.5f, room.floor * 0.5f, (minZ + maxZ) * 0.5f);
                var size = new Vector3(maxX - minX, room.floor, maxZ - minZ);
                ch.floor.Box(slab, size, 0.5f, 0b0011_0111);
                AddCollider(slab, size);
            }
        }

        // Meshes par bloc de cellules (l'éclairage par objet reste local).
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
            {
                Chunk ch = chunkData[x, y];
                string name = "Z" + m.zone + "_Bloc_" + x + "_" + y;
                if (!ch.floor.IsEmpty) GameArt.MeshObject(name + "_Sol", root, ch.floor.Build(name), GameArt.CheckerFloor);
                if (!ch.ceiling.IsEmpty) GameArt.MeshObject(name + "_Plafond", root, ch.ceiling.Build(name), GameArt.DarkConcrete);
                if (!ch.checker.IsEmpty) GameArt.MeshObject(name + "_Damier", root, ch.checker.Build(name), GameArt.CheckerWall);
            }
        for (int x = 0; x < wallChunks; x++)
            for (int y = 0; y < wallChunks; y++)
                if (wallData[x, y] != null && !wallData[x, y].IsEmpty)
                {
                    string name = "Z" + m.zone + "_Murs_" + x + "_" + y;
                    GameArt.MeshObject(name, root, wallData[x, y].Build(name), GameArt.CheckerWall);
                }
    }

    // Coin (x et z minimaux) de la grille fine d'une salle.
    static Vector3 DepthOrigin(Maze m, Room room) => m.origin + new Vector3(room.rect.xMin * Cell, 0f, room.rect.yMin * Cell);

    // Position (au sol) d'une case de la grille fine d'une salle.
    static Vector3 SubCenter(Maze m, Room room, Vector2Int c) =>
        DepthOrigin(m, room) + new Vector3((c.x + 0.5f) * Sub, room.levels[c.x, c.y], (c.y + 0.5f) * Sub);

    // Salle à profondeurs : sols de chaque niveau (rectangles fusionnés), parois infestées entre deux niveaux et escaliers.
    static void BuildDepths(Maze m, Room room, Chunk ch, Transform colliders, System.Action<Vector3, Vector3> addCollider)
    {
        float[,] lv = room.levels;
        int w = lv.GetLength(0), h = lv.GetLength(1);
        Vector3 o = DepthOrigin(m, room);
        bool Same(float a, float b) => Mathf.Abs(a - b) < 0.01f;

        // Sols : rectangles de cases au même niveau (les escaliers reposent sur le sol du bas).
        var done = new bool[w, h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                if (done[x, z]) continue;
                float level = lv[x, z];
                int x1 = x;
                while (x1 + 1 < w && !done[x1 + 1, z] && Same(lv[x1 + 1, z], level)) x1++;
                int z1 = z;
                while (z1 + 1 < h)
                {
                    bool row = true;
                    for (int k = x; k <= x1 && row; k++) if (done[k, z1 + 1] || !Same(lv[k, z1 + 1], level)) row = false;
                    if (!row) break;
                    z1++;
                }
                for (int a = x; a <= x1; a++)
                    for (int b = z; b <= z1; b++)
                        done[a, b] = true;
                float ax = o.x + x * Sub, bx = o.x + (x1 + 1) * Sub, az = o.z + z * Sub, bz = o.z + (z1 + 1) * Sub;
                ch.floor.WorldQuad(new Vector3(ax, level, az), new Vector3(bx, level, az), new Vector3(bx, level, bz), new Vector3(ax, level, bz), Vector3.up, 0.5f);
                addCollider(new Vector3((ax + bx) * 0.5f, level - 0.25f, (az + bz) * 0.5f), new Vector3(bx - ax, 0.5f, bz - az));
            }

        // Paroi entre deux cases voisines de niveaux différents (sauf en haut d'un escalier) ; 'aLow' : 'a' est en bas.
        bool WallBetween(Vector2Int a, Vector2Int b, out float low, out float high, out bool aLow)
        {
            float la = lv[a.x, a.y], lb = lv[b.x, b.y];
            low = Mathf.Min(la, lb);
            high = Mathf.Max(la, lb);
            aLow = la < lb;
            return !Same(la, lb) && !room.openEdges.Contains(EdgeKey(a, b));
        }
        // Parois le long de X (entre x et x + 1) puis le long de Z, fusionnées en bandes ; face tournée vers le bas,
        // collision du côté haut.
        for (int axis = 0; axis < 2; axis++)
        {
            int lines = axis == 0 ? w - 1 : h - 1, length = axis == 0 ? h : w;
            for (int line = 0; line < lines; line++)
                for (int t = 0; t < length;)
                {
                    Vector2Int A(int k) => axis == 0 ? new Vector2Int(line, k) : new Vector2Int(k, line);
                    Vector2Int B(int k) => axis == 0 ? new Vector2Int(line + 1, k) : new Vector2Int(k, line + 1);
                    if (!WallBetween(A(t), B(t), out float low, out float high, out bool aLow)) { t++; continue; }
                    int t1 = t + 1;
                    while (t1 < length && WallBetween(A(t1), B(t1), out float l2, out float h2, out bool a2) && Same(l2, low) && Same(h2, high) && a2 == aLow) t1++;
                    float edge = (axis == 0 ? o.x : o.z) + (line + 1) * Sub;
                    float s0 = (axis == 0 ? o.z : o.x) + t * Sub, s1 = (axis == 0 ? o.z : o.x) + t1 * Sub;
                    Vector3 P(float along, float y) => axis == 0 ? new Vector3(edge, y, along) : new Vector3(along, y, edge);
                    Vector3 toLow = (axis == 0 ? Vector3.right : Vector3.forward) * (aLow ? -1f : 1f);
                    ch.checker.WorldQuad(P(s0, low), P(s1, low), P(s1, high), P(s0, high), toLow, 0.5f);
                    Vector3 center = P((s0 + s1) * 0.5f, (low + high) * 0.5f) - toLow * 0.15f;
                    addCollider(center, axis == 0 ? new Vector3(0.3f, high - low, s1 - s0) : new Vector3(s1 - s0, high - low, 0.3f));
                    t = t1;
                }
        }

        foreach (var s in room.stairs) AddDepthStairs(ch.floor, colliders, o, s);

        // Passerelles : dalles de 30 cm au niveau du Hub, d'un bord à l'autre (les poutres sont posées avec le décor).
        foreach (var (cells, beam) in room.bridges)
        {
            if (beam) continue;
            var center = new Vector3(o.x + cells.center.x * Sub, -0.15f, o.z + cells.center.y * Sub);
            var size = new Vector3(cells.width * Sub, 0.3f, cells.height * Sub);
            ch.floor.Box(center, size, 0.5f);
            addCollider(center, size);
        }
    }

    // Escalier d'une profondeur : marches du haut de la bande (niveau 'top') vers le fond. Comme pour AddStairs,
    // la collision est une rampe pleine.
    static void AddDepthStairs(MeshBuilder mb, Transform colliders, Vector3 o, Stair s)
    {
        Vector3 fwd = new Vector3(DI[s.dir], 0f, DJ[s.dir]);
        Vector3 min = o + new Vector3(s.strip.xMin * Sub, 0f, s.strip.yMin * Sub), max = o + new Vector3(s.strip.xMax * Sub, 0f, s.strip.yMax * Sub);
        Vector3 center = (min + max) * 0.5f;
        float width = s.dir < 2 ? max.z - min.z : max.x - min.x;
        float stripLength = s.dir < 2 ? max.x - min.x : max.z - min.z;
        Vector3 topEdge = center - fwd * (stripLength * 0.5f);
        float rise = (s.top - s.bottom) / s.steps, length = s.steps * StepDepth;
        for (int k = 1; k <= s.steps; k++)
        {
            float surface = s.bottom + k * rise;
            Vector3 c = topEdge + fwd * (length - (k - 0.5f) * StepDepth) + Vector3.up * ((s.bottom + surface) * 0.5f);
            Vector3 size = s.dir < 2 ? new Vector3(StepDepth, surface - s.bottom, width) : new Vector3(width, surface - s.bottom, StepDepth);
            mb.Box(c, size, 0.5f, 0b0011_0111);
        }

        AddRamp(colliders, topEdge + fwd * length + Vector3.up * s.bottom, topEdge + Vector3.up * s.top, width);
    }

    // Zones impaires : porte massive dans le mur nord (fermée au départ), à moitié dans chaque zone, et ses orbes devant
    // elle, côté zone impaire (3 pour la première porte, 5 ensuite).
    static void BuildZoneDoor(Maze m, Transform root, ZoneRefs refs)
    {
        Vector3 toOdd = Vector3.back;
        Vector3 edge = m.Center(m.doorCol, m.N - 1) + Vector3.forward * (Cell * 0.5f);
        Quaternion facing = Quaternion.LookRotation(toOdd);
        refs.doorNumber = (m.zone + 1) / 2;
        refs.door = ZoneDoor.Create(root, edge, facing, ZoneDoorWidth, ZoneDoorHeight);
        int count = GameRules.OrbsOfDoor(refs.doorNumber), first = GameRules.FirstOrb(refs.doorNumber);
        refs.orbs = new ZoneOrb[count];
        Vector3 right = Vector3.Cross(Vector3.up, toOdd);
        for (int k = 0; k < count; k++)
            refs.orbs[k] = ZoneOrb.Create(root, first + k, edge + toOdd * 1.9f + right * ((k - (count - 1) * 0.5f) * 1.35f), facing);
    }

    // Zones paires : Neutral Room (3 glyphes neutres sur le mur du fond, face à l'entrée), glyphes d'acquisition contre des
    // murs pris au hasard (plus grands), et Acquisition Room avec la porte du chapiteau suivant, qui porte tous ces glyphes.
    static void BuildGlyphRooms(Maze m, Transform root, ZoneRefs refs, System.Random rnd)
    {
        var props = new GameObject("Glyphes").transform;
        props.SetParent(root, false);

        RectInt nr = m.neutralRoom;
        float back = m.Center(0, nr.yMax - 1).z + Cell * 0.5f - Wall * 0.5f;
        for (int t = 0; t < GameRules.GlyphTypes; t++)
        {
            Vector3 p = new Vector3(m.Center(nr.xMin + t, 0).x, 1.7f, back - 0.03f);
            refs.neutralGlyphs.Add(Glyph.Create(props, m.zone * 100 + 50 + t, t, true, m.zone, p, Vector3.back, Glyph.NeutralSize));
        }
        // Enseigne non traductible au-dessus des glyphes, lue depuis l'entrée.
        NeonSign(props, "EnseigneNeutralRoom", "Neutral Room", new Vector3(m.Center(m.entryCol, 0).x, 3.7f, back), NeutralSignWidth);
        Vector3 nrMin = m.origin + new Vector3(nr.xMin * Cell, -1f, nr.yMin * Cell), nrMax = m.origin + new Vector3(nr.xMax * Cell, 7f, nr.yMax * Cell);
        refs.neutralRoom = new Bounds((nrMin + nrMax) * 0.5f, nrMax - nrMin);

        // Glyphes d'acquisition : Zone 2, un de chaque ; ensuite, 2 + nZone / 2 tirés au hasard. Loin des deux salles et
        // les uns des autres (V0.6.1 : au moins 50 m entre eux), contre le mur d'une cellule de plain-pied.
        int count = GameRules.AcquisitionGlyphs(m.zone);
        var types = new List<int>();
        for (int k = 0; k < count; k++) types.Add(m.zone == 2 ? k % GameRules.GlyphTypes : rnd.Next(GameRules.GlyphTypes));
        for (int k = types.Count - 1; k > 0; k--) { int r = rnd.Next(k + 1); (types[k], types[r]) = (types[r], types[k]); }
        var cells = new List<Vector2Int>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                Room room = m.RoomAt(i, j);
                if (m.Fixed(i, j) || m.keepClear.Contains(new Vector2Int(i, j)) || room != null && (room.style == RoomStyle.Pit || room.floor > 0f)) continue;
                cells.Add(new Vector2Int(i, j));
            }
        var placed = new List<Vector3>();
        Vector3 nrCenter = refs.neutralRoom.center, arCenter = m.Center(HubCol, m.N - 2);
        // Zones 4 et plus (non précisé dans le GDD) : 50 m si possible, sinon de moins en moins.
        float[] spacings = m.zone == 2 ? new[] { 60f, GlyphMinSpacing } : new[] { 60f, GlyphMinSpacing, 30f, 15f };
        foreach (float spacing in spacings)
            for (int attempt = 0; attempt < 4000 && placed.Count < count; attempt++)
            {
                Vector2Int c = cells[rnd.Next(cells.Count)];
                int dir = rnd.Next(4);
                if (!HasWall(m, c.x, c.y, dir)) continue;
                Vector3 center = m.Center(c.x, c.y);
                if (Flat(center - nrCenter) < 25f || Flat(center - arCenter) < 25f) continue;
                if (placed.Exists(q => Flat(q - center) < spacing)) continue;
                // Pas de glyphe devant l'accès d'une liaison.
                if (InBlocks(m, center + new Vector3(DI[dir], 0f, DJ[dir]) * 2f, 0.5f)) continue;
                Vector3 toWall = new Vector3(DI[dir], 0f, DJ[dir]);
                Vector3 p = center + toWall * (Cell * 0.5f - Wall * 0.5f - 0.04f) + Vector3.up * 2.1f;
                int k = placed.Count;
                placed.Add(center);
                refs.acquisitionGlyphs.Add(Glyph.Create(props, m.zone * 100 + k, types[k], false, m.zone, p, -toWall, Glyph.AcquisitionSize));
            }

        // Porte du chapiteau suivant, dans le mur nord de l'Acquisition Room (au milieu de la colonne du Hub).
        Vector3 edge = m.Center(HubCol, m.N - 1) + Vector3.forward * (Cell * 0.5f);
        var glyphTypes = refs.acquisitionGlyphs.ConvertAll(g => g.Type);
        refs.acquisitionDoor = AcquisitionDoor.Create(root, edge, Quaternion.LookRotation(Vector3.back), DoorWidth, DoorHeight, glyphTypes);
        NeonSign(props, "EnseigneAcquisitionRoom", "Acquisition Room", edge + new Vector3(0f, 4.6f, -Wall * 0.5f), AcquisitionSignWidth);
    }

    // Distance minimale entre deux glyphes d'acquisition de la Zone 2 (V0.6.1).
    const float GlyphMinSpacing = 50f;

    // ---------- Bornes d'arcade (chapitre 10.2, V0.6.1) ----------

    // nZone - 1 bornes à partir de la Zone 3, de type tiré au hasard, contre le mur d'une cellule de plain-pied (au niveau du
    // Hub, assez haute), loin des salles fixes, des glyphes et des autres bornes ; la cellule reste sans décor (les plaques
    // de l'Arcade Dance s'étendent devant la borne).
    const float ArcadeSpacing = 25f;
    static int ArcadeId(int zone, int k) => zone * 100 + 70 + k;

    static void BuildArcades(Maze m, Transform root, ZoneRefs refs, System.Random rnd)
    {
        int count = GameRules.ArcadesInZone(m.zone);
        if (count <= 0) return;
        Physics.SyncTransforms();
        var props = new GameObject("Arcades").transform;
        props.SetParent(root, false);
        var avoid = new List<Vector3>();
        foreach (var g in refs.acquisitionGlyphs) avoid.Add(g.transform.position);
        if (refs.acquisitionDoor != null) avoid.Add(refs.acquisitionDoor.transform.position);
        if (refs.door != null) avoid.Add(refs.door.transform.position);
        var placed = new List<Vector3>();
        // Note 01 du chapitre 10.2 (V0.6.3) : jamais plus d'une borne dans une même salle.
        var usedRooms = new HashSet<Room>();
        // Chapitre 10.3 : les étoiles des plateformes d'abord (celles qui restent recevront un monticule).
        for (int k = m.stars.Count - 1; k > 0; k--)
        {
            int other = rnd.Next(k + 1);
            (m.stars[k], m.stars[other]) = (m.stars[other], m.stars[k]);
        }
        foreach (Star star in m.stars)
        {
            if (placed.Count >= count) break;
            if (usedRooms.Contains(star.room) || placed.Exists(q => Flat(q - star.pos) < ArcadeSpacing)) continue;
            // Face aux étages inférieurs : vers le centre de la salle, reculée pour garder la place des plaques de danse.
            Vector3 roomCenter = m.Center(star.room.rect.xMin, star.room.rect.yMin) + new Vector3((star.room.rect.width - 1) * Cell * 0.5f, 0f, (star.room.rect.height - 1) * Cell * 0.5f);
            Vector3 forward = roomCenter - star.pos;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Vector3 starPos = star.pos - forward * 1.2f;
            if (!FlatGround(starPos, forward, star.pos.y)) continue;
            star.used = true;
            usedRooms.Add(star.room);
            int rank = placed.Count;
            placed.Add(star.pos);
            refs.arcades.Add(ArcadeMachine.Create(props, ArcadeId(m.zone, rank), (ArcadeKind)rnd.Next(3), m.zone, starPos, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg));
        }
        for (int attempt = 0; attempt < 3000 && placed.Count < count; attempt++)
        {
            int i = rnd.Next(m.N), j = rnd.Next(m.N), dir = rnd.Next(4);
            Room room = m.RoomAt(i, j);
            if (room != null && (usedRooms.Contains(room) || room.style == RoomStyle.Template)) continue;
            var cell = new Vector2Int(i, j);
            if (m.Reserved(i, j) || m.Fixed(i, j) || m.keepClear.Contains(cell) || cell == m.start) continue;
            if (room != null && (room.style == RoomStyle.Pit || room.floor > 0f || room.levels != null)) continue;
            if (m.Ceil(i, j) < 3f || !HasWall(m, i, j, dir)) continue;
            Vector3 center = m.Center(i, j);
            if (avoid.Exists(q => Flat(q - center) < 12f) || placed.Exists(q => Flat(q - center) < ArcadeSpacing)) continue;
            if (m.neutralRoom.width > 0 && m.neutralRoom.Contains(cell)) continue;
            Vector3 toWall = new Vector3(DI[dir], 0f, DJ[dir]);
            // Dos au mur (la borne fait moins d'un mètre de profondeur), face à la cellule.
            Vector3 pos = center + toWall * (Cell * 0.5f - Wall * 0.5f - 0.62f);
            if (!FlatGround(pos, -toWall)) continue;
            var kind = (ArcadeKind)rnd.Next(3);
            int k = placed.Count;
            placed.Add(center);
            if (room != null) usedRooms.Add(room);
            m.keepClear.Add(cell);
            float yaw = Mathf.Atan2(-toWall.x, -toWall.z) * Mathf.Rad2Deg;
            refs.arcades.Add(ArcadeMachine.Create(props, ArcadeId(m.zone, k), kind, m.zone, pos, yaw));
        }
    }

    // Sol plat au niveau du Hub sous la borne et devant elle (jusqu'aux plaques de l'Arcade Dance).
    static bool FlatGround(Vector3 pos, Vector3 forward, float level = 0f)
    {
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        foreach (float ahead in new[] { 0f, 1f, 2.6f })
            foreach (float across in new[] { -0.8f, 0f, 0.8f })
            {
                Vector3 p = pos + forward * ahead + side * across + Vector3.up * 2f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(hit.point.y - level) > 0.05f || hit.normal.y < 0.95f) return false;
            }
        return true;
    }

    // Godmod (V0.6.1) : bornes du Hub 1 (Roulette, Machine à pince, Arcade Dance), entre les Chenils et le Bouton Rouge,
    // tournées vers le centre, sans masquer l'Écran des Logs.
    static readonly float[] HubArcadeAngles = { 75f, -75f, 122f };

    public static (Vector3 pos, float yaw) HubArcadeSpot(HubRefs h, int k)
    {
        float angle = HubArcadeAngles[Mathf.Clamp(k, 0, HubArcadeAngles.Length - 1)];
        return (h.center + Polar(angle, k == 2 ? 6.6f : 7.4f, 0f), angle + 180f);
    }

    // Enseignes en lettres de néon blanc (mots 3D générés par Meshy, police soviétique, préparés par "FunGame/Préparer les
    // enseignes néon") : 1,3 fois la largeur des anciens textes (2,65 m et 3,32 m).
    const float NeutralSignWidth = 2.65f * 1.3f, AcquisitionSignWidth = 3.32f * 1.3f;

    // Centrée en 'center', collée au mur nord dont la face est en center.z, tournée vers le sud. Sans le maillage, un texte.
    static void NeonSign(Transform parent, string mesh, string text, Vector3 center, float width)
    {
        var shape = Resources.Load<Mesh>("Art/Decor/" + mesh);
        if (shape == null)
        {
            GameArt.CreateText(parent, text, center + Vector3.back * 0.03f, 0.06f * 1.3f, new Color(0.85f, 0.85f, 0.82f)).text = text;
            return;
        }
        float scale = width / Mathf.Max(0.01f, shape.bounds.size.x);
        var go = GameArt.MeshObject(text, parent, shape, GameArt.Emissive("NeonBlanc", new Color(0.95f, 0.97f, 1f), new Color(0.55f, 0.56f, 0.6f)));
        go.transform.localScale = Vector3.one * scale;
        go.transform.localPosition = new Vector3(center.x, center.y - scale * 0.5f, center.z - shape.bounds.max.z * scale - 0.01f);
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        // Halo blanc du néon sur le mur et la salle.
        AddLight(parent, new Vector3(center.x, center.y, center.z - 0.8f), new Color(0.9f, 0.93f, 1f), 1.2f, 7f);
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // Brume du Couvre Feu : nappes au ras du sol (et dans les fosses), invisibles tant qu'il n'est pas là.
    static GameObject BuildMist(Maze m, Transform root)
    {
        var mist = new GameObject("CouvreFeu");
        mist.transform.SetParent(root, false);
        float[] layers = { 0.06f, 0.26f, 0.46f };
        int chunks = (m.N + ChunkSize - 1) / ChunkSize;
        var builders = new MeshBuilder[chunks, chunks];
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Reserved(i, j)) continue;
                Room room = m.RoomAt(i, j);
                // Les salles surélevées dépassent de la brume.
                if (room != null && room.floor >= GameRules.MistTop) continue;
                var mb = builders[i / ChunkSize, j / ChunkSize] ??= new MeshBuilder();
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f;
                void Layer(float y) => mb.WorldQuad(c + new Vector3(-e, y, -e), c + new Vector3(e, y, -e), c + new Vector3(e, y, e), c + new Vector3(-e, y, e), Vector3.up, 0.2f);
                foreach (float y in layers) Layer(y);
                if (room != null && room.style == RoomStyle.Pit)
                    for (float y = -room.pitDepth + 0.3f; y < -0.2f; y += 0.9f) Layer(y);
            }
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
                if (builders[x, y] != null)
                    GameArt.MeshObject("Brume_Z" + m.zone + "_" + x + "_" + y, mist.transform, builders[x, y].Build("Brume"), GameArt.Mist);
        mist.SetActive(false);
        return mist;
    }

    // Escalier montant depuis le côté 'dir' de la cellule (sol du voisin, à la hauteur 'from') vers le plancher de la salle.
    // Les marches sont visuelles ; la collision est une rampe pleine (le personnage monte sans buter sur les marches).
    static void AddStairs(MeshBuilder mb, Transform colliders, Vector3 cellCenter, int dir, int steps, float from, float floor)
    {
        Vector3 outward = new Vector3(DI[dir], 0f, DJ[dir]);
        Vector3 inward = -outward;
        Vector3 edge = cellCenter + outward * (Cell * 0.5f);
        float width = Cell - Wall;
        float rise = (floor - from) / steps;
        for (int k = 1; k <= steps; k++)
        {
            float h = from + k * rise;
            Vector3 center = edge + inward * ((k - 0.5f) * StepDepth) + Vector3.up * (h * 0.5f);
            Vector3 size = dir < 2 ? new Vector3(StepDepth, h, width) : new Vector3(width, h, StepDepth);
            mb.Box(center, size, 0.5f, 0b0011_0111);
        }

        AddRamp(colliders, edge + Vector3.up * from, edge + inward * (steps * StepDepth) + Vector3.up * floor, width);
    }

    // Collision d'un escalier : coin plein (prisme convexe) du pied 'foot' (au sol du bas) au haut 'head' (au niveau du
    // haut), large de 'width'. Plein jusqu'au sol du bas : on ne peut ni passer sous les marches ni y entrer par le côté.
    static void AddRamp(Transform colliders, Vector3 foot, Vector3 head, float width)
    {
        Vector3 along = head - foot;
        along.y = 0f;
        Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * (width * 0.5f);
        Vector3 footTop = foot + Vector3.up * 0.05f, headBase = new Vector3(head.x, foot.y, head.z);
        var vertices = new[]
        {
            foot - side, foot + side, footTop - side, footTop + side,
            headBase - side, headBase + side, head - side, head + side,
        };
        var mesh = new Mesh { name = "Rampe", vertices = vertices };
        mesh.triangles = new[]
        {
            0, 1, 3, 0, 3, 2,   // pied
            4, 6, 7, 4, 7, 5,   // dos, sous le haut
            0, 4, 5, 0, 5, 1,   // dessous
            2, 3, 7, 2, 7, 6,   // pente
            0, 2, 6, 0, 6, 4,   // côtés
            1, 5, 7, 1, 7, 3,
        };
        mesh.RecalculateBounds();
        var ramp = new GameObject("Rampe");
        ramp.transform.SetParent(colliders, false);
        var collider = ramp.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
    }

    // Sondes de réflexion (une par bloc), rendues une seule fois une fois le décor posé :
    // le damier astiqué y reflète les murs, les lampes et les yeux.
    static void AddReflectionProbes(Maze m, Transform root)
    {
        int chunks = (m.N + ChunkSize - 1) / ChunkSize;
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
            {
                float low = 0f, high = WallHeight;
                for (int i = x * ChunkSize; i < Mathf.Min(m.N, x * ChunkSize + ChunkSize); i++)
                    for (int j = y * ChunkSize; j < Mathf.Min(m.N, y * ChunkSize + ChunkSize); j++)
                    {
                        Room r = m.RoomAt(i, j);
                        if (r != null && r.style == RoomStyle.Pit) low = Mathf.Min(low, -r.pitDepth);
                        high = Mathf.Max(high, m.Ceil(i, j));
                    }
                high = Mathf.Min(high, 16f);
                Vector3 min = m.Center(x * ChunkSize, y * ChunkSize) - new Vector3(Cell, 0f, Cell) * 0.5f + Vector3.up * low;
                Vector3 max = m.Center(Mathf.Min(x * ChunkSize + ChunkSize - 1, m.N - 1), Mathf.Min(y * ChunkSize + ChunkSize - 1, m.N - 1))
                    + new Vector3(Cell, 0f, Cell) * 0.5f + Vector3.up * high;
                var probe = new GameObject("Reflets_Z" + m.zone + "_" + x + "_" + y).AddComponent<ReflectionProbe>();
                probe.transform.SetParent(root, false);
                probe.transform.localPosition = new Vector3((min.x + max.x) * 0.5f, 1.5f, (min.z + max.z) * 0.5f);
                probe.size = max - min + Vector3.one * 0.2f;
                probe.center = new Vector3(0f, (min.y + max.y) * 0.5f - 1.5f, 0f);
                probe.boxProjection = true;
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
                probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.resolution = 64;
                probe.hdr = false;
                probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = Color.black;
                probe.cullingMask = ~((1 << 2) | (1 << 5)); // sans les joueurs ni l'UI
                probe.farClipPlane = 30f;
                WorldStreamer.EnqueueProbe(probe);
            }
    }

    // ================= Entités et décor =================

    static readonly string[] ChessPieces = { "Pawn", "Rook", "Knight", "Bishop", "Queen", "King" };

    static void Decorate(Maze m, Transform root, Transform structure, System.Random rnd, List<Vector3> spots)
    {
        var props = new GameObject("Decor_Z" + m.zone).transform;
        props.SetParent(root, false);
        // Faisceaux des Lasers posés : aucun monticule à leur portée de déclenchement (filtré à la fin).
        var lasers = new List<(Vector3 a, Vector3 b)>();

        // Hauteur du sol (dalle, marche, passerelle ou fond de fosse) sous un point, sans compter le décor.
        float FloorAt(Vector3 p, float from)
        {
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, from + 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(structure) && hit.point.y > best) best = hit.point.y;
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Reserved(i, j)) continue;
                Vector3 c = m.Center(i, j);
                Room room = m.RoomAt(i, j);
                float floor = m.Floor(i, j), ceil = m.Ceil(i, j);

                bool nearDoor = Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 1;
                bool clear = m.keepClear.Contains(new Vector2Int(i, j));
                if (!nearDoor && !clear)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.2f, 1.2f), 0f, Rand(rnd, -1.2f, 1.2f));
                    spots.Add(new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z));
                }
                if (room != null && !clear)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.8f, 1.8f), 0f, Rand(rnd, -1.8f, 1.8f));
                    spots.Add(new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z));
                }
                if (clear) continue;

                // Lasers dans les couloirs droits (murs parallèles de part et d'autre).
                if (room == null && !nearDoor && rnd.NextDouble() < 0.45)
                {
                    bool alongZ = HasWall(m, i, j, 0) && HasWall(m, i, j, 1) && !HasWall(m, i, j, 2) && !HasWall(m, i, j, 3);
                    bool alongX = HasWall(m, i, j, 2) && HasWall(m, i, j, 3) && !HasWall(m, i, j, 0) && !HasWall(m, i, j, 1);
                    if (alongZ || alongX) lasers.Add(AddLaser(props, c, alongZ, rnd));
                }

                // Caisses contre les murs des couloirs : de quoi se percher au-dessus de la brume du Couvre Feu.
                if (room == null && !nearDoor && rnd.NextDouble() < 0.14)
                {
                    int dir = rnd.Next(4);
                    if (HasWall(m, i, j, dir)) AddCrate(props, c, dir, rnd);
                }

                // Éclairage : salles, et quelques néons dans les couloirs.
                if (room == null && rnd.NextDouble() < 0.18) AddLamp(props, c + Vector3.up * ceil, 6.5f, 1.1f, rnd);

                // Yeux dans les murs, le sol et le plafond.
                if (rnd.NextDouble() < 0.28)
                {
                    int dir = rnd.Next(4);
                    if (HasWall(m, i, j, dir) && !m.HasWallHole(i, j, dir))
                    {
                        Vector3 n = -new Vector3(DI[dir], 0, DJ[dir]);
                        Vector3 along = new Vector3(DJ[dir], 0, DI[dir]);
                        Vector3 p = c - n * (Cell * 0.5f - Wall * 0.5f) + along * Rand(rnd, -1.6f, 1.6f) + Vector3.up * Rand(rnd, floor + 1f, ceil - 0.8f);
                        AddEye(props, p, n, Rand(rnd, 0.5f, 1.1f));
                    }
                }
                if (rnd.NextDouble() < 0.06) AddEye(props, c + new Vector3(Rand(rnd, -1.5f, 1.5f), ceil, Rand(rnd, -1.5f, 1.5f)), Vector3.down, Rand(rnd, 0.7f, 1.3f));
                if (rnd.NextDouble() < 0.04)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f));
                    AddEye(props, new Vector3(p.x, FloorAt(p, ceil - 0.1f) - 0.15f, p.z), Vector3.up, Rand(rnd, 0.6f, 1f));
                }

                // Note 05 de la DA (V0.6) : dans les zones 1 à 3, des têtes de chiens (d'après les skins), sans yeux, déchirées et
                // rapiécées, sortent des murs ou gisent au sol. (Note 06 du chapitre 14 : plus de membres humains.)
                if (m.zone <= 3 && rnd.NextDouble() < (room != null ? 0.06 : 0.02))
                {
                    int dir = rnd.Next(4);
                    float size = Rand(rnd, 0.7f, 1.5f);
                    if (rnd.NextDouble() < 0.6 && HasWall(m, i, j, dir))
                    {
                        Vector3 n = -new Vector3(DI[dir], 0, DJ[dir]);
                        Vector3 along = new Vector3(DJ[dir], 0, DI[dir]);
                        Vector3 p = c - n * (Cell * 0.5f - Wall * 0.5f) + along * Rand(rnd, -1.2f, 1.2f)
                            + Vector3.up * Rand(rnd, floor + 1.2f, Mathf.Max(floor + 1.3f, Mathf.Min(ceil - 1f, floor + 3f)));
                        DogHeads.Spawn(props, p, n, size, rnd, false);
                    }
                    else
                    {
                        Vector3 p = c + new Vector3(Rand(rnd, -1.2f, 1.2f), 0f, Rand(rnd, -1.2f, 1.2f));
                        // Ni dans une trémie, ni sur un escalier ou un palier de liaison.
                        if (!InBlocks(m, p, 1f)) DogHeads.Spawn(props, new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z), Vector3.up, size, rnd, true);
                    }
                }
            }

        foreach (Room room in m.rooms)
        {
            Vector3 center = Vector3.zero;
            foreach (var c in room.cells) center += m.Center(c.x, c.y);
            center /= room.cells.Count;
            float height = Mathf.Min(16f, room.ceil - room.floor + room.pitDepth);
            AddLamp(props, new Vector3(center.x, room.ceil, center.z), 9f + (height - 4f) * 1.5f, 1.4f + (height - 4f) * 0.15f, rnd);
            // Neutral Room et Acquisition Room : vides (rien ne doit gêner le passage ni masquer les glyphes).
            if (room.plain) continue;

            // Intérieur de la salle (marge pour les murs et les escaliers).
            float minX = m.Center(room.rect.xMin, 0).x - Cell * 0.5f + 1.2f, maxX = m.Center(room.rect.xMax - 1, 0).x + Cell * 0.5f - 1.2f;
            float minZ = m.Center(0, room.rect.yMin).z - Cell * 0.5f + 1.2f, maxZ = m.Center(0, room.rect.yMax - 1).z + Cell * 0.5f - 1.2f;
            if (room.floor > 0f)
            {
                float stairs = Mathf.RoundToInt(room.floor / StepHeight) * StepDepth;
                minX += stairs; maxX -= stairs; minZ += stairs; maxZ -= stairs;
            }
            Vector3 RandomInside(float y) => new Vector3(Rand(rnd, minX, maxX), y, Rand(rnd, minZ, maxZ));

            if (room.statueHeight > 0f) AddStatue(props, center, room.statueHeight, spots);
            else switch (room.style)
            {
                case RoomStyle.Template:
                    // Les éléments de la Zone 1 restent présents, sur le pourtour libre.
                    for (int k = 0; k < 2 + room.cells.Count / 3; k++)
                    {
                        Vector3 p = RandomInside(room.floor);
                        if (InBlocks(m, p, 1.5f) || m.airLines.Exists(r => r.Contains(new Vector2(p.x, p.z)))) continue;
                        if (rnd.NextDouble() < 0.5) AddColumn(props, p, Rand(rnd, 0.35f, 0.6f), room.ceil - room.floor, rnd);
                        else AddBigSphere(props, p, Rand(rnd, 0.6f, 1.2f), room.floor, room.ceil, rnd);
                    }
                    break;
                case RoomStyle.Columns:
                    for (int k = 0; k < room.cells.Count; k++)
                    {
                        Vector3 p = RandomInside(room.floor);
                        // Une colonne monte jusqu'au plafond : elle couperait un tube aérien.
                        if (m.airLines.Exists(r => r.Contains(new Vector2(p.x, p.z)))) continue;
                        AddColumn(props, p, Rand(rnd, 0.35f, 0.6f), room.ceil - room.floor, rnd);
                    }
                    break;
                case RoomStyle.Spheres:
                    for (int k = 0; k < 2 + room.cells.Count / 2; k++)
                        AddBigSphere(props, RandomInside(room.floor), Rand(rnd, 0.6f, Mathf.Min(1.8f, (room.ceil - room.floor) * 0.25f)), room.floor, room.ceil, rnd);
                    break;
                case RoomStyle.Chaotic:
                    AddPlatforms(props, room, minX, maxX, minZ, maxZ, rnd, spots);
                    break;
                case RoomStyle.Pit:
                    AddDepthDecor(props, m, room, rnd, spots);
                    break;
                default:
                    foreach (var c in room.cells)
                    {
                        Vector3 p = m.Center(c.x, c.y) + new Vector3(Rand(rnd, -1.3f, 1.3f), room.floor, Rand(rnd, -1.3f, 1.3f));
                        if (rnd.NextDouble() < 0.6) AddBlock(props, p, room.floor, room.ceil, rnd);
                        else AddEye(props, p + Vector3.down * 0.2f, Vector3.up, Rand(rnd, 0.9f, 1.4f));
                    }
                    break;
            }

            // Pièces d'échecs, rares.
            if (room.chess)
            {
                int count = 1 + rnd.Next(3);
                for (int k = 0; k < count; k++)
                    AddChessPiece(props, Pick(rnd, ChessPieces), RandomInside(room.floor), rnd);
            }

            for (int e = 0; e < 2; e++)
            {
                Vector2Int c = room.cells[rnd.Next(room.cells.Count)];
                AddEye(props, m.Center(c.x, c.y) + new Vector3(Rand(rnd, -1f, 1f), room.ceil, Rand(rnd, -1f, 1f)), Vector3.down, Rand(rnd, 0.8f, 1.5f));
            }
        }

        // Chapitre 10.3 : rien dans les plateformes pleines ni leurs escaliers ; une étoile sans borne reçoit un monticule.
        spots.RemoveAll(s => InBlocks(m, s, 0.7f));
        foreach (Star star in m.stars) if (!star.used) spots.Add(star.pos);

        // Pas de monticule près d'un Laser : creuser ne doit jamais l'allumer.
        spots.RemoveAll(s => lasers.Exists(l => Laser.DistancePointSegment(s, l.a, l.b) <= GameRules.LaserTriggerDistance));
        // Ni sur un escalier, ni sous une plateforme (décor tout juste posé : on synchronise la physique avant de sonder).
        Physics.SyncTransforms();
        spots.RemoveAll(s => OnStairs(s) || UnderPlatform(s));
    }

    // Emprise d'un monticule (1,1 x 0,9 m) : un peu plus large, pour qu'il ne déborde pas sur l'obstacle.
    const float MoundClearance = 0.6f;

    // Escalier sous ou à côté du monticule : sa collision (rampe) touche l'emprise.
    static bool OnStairs(Vector3 spot)
    {
        foreach (var c in Physics.OverlapSphere(spot + Vector3.up * 0.3f, MoundClearance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (c.name == "Rampe") return true;
        return false;
    }

    // Plateforme au-dessus du monticule, quelle que soit sa hauteur. La sphère part juste au-dessus du sol : la plateforme
    // qui porte un monticule (objet au sommet) n'est pas comptée.
    static bool UnderPlatform(Vector3 spot)
    {
        foreach (var hit in Physics.SphereCastAll(spot + Vector3.up * (MoundClearance + 0.05f), MoundClearance, Vector3.up, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.collider.name == "Plateforme") return true;
        return false;
    }

    // Caisse contre le mur 'dir' d'une cellule de couloir, assez haute pour sortir de la brume, assez basse pour y sauter.
    static void AddCrate(Transform parent, Vector3 cellCenter, int dir, System.Random rnd)
    {
        Vector3 n = new Vector3(DI[dir], 0f, DJ[dir]);
        Vector3 along = new Vector3(DJ[dir], 0f, DI[dir]);
        var size = new Vector3(Rand(rnd, 0.9f, 1.3f), Rand(rnd, 0.65f, 0.8f), Rand(rnd, 0.9f, 1.3f));
        Vector3 p = cellCenter + n * (Cell * 0.5f - Wall * 0.5f - 0.7f) + along * Rand(rnd, -1.4f, 1.4f) + Vector3.up * (size.y * 0.5f);
        var crate = GameArt.Prim(PrimitiveType.Cube, parent, p, size, GameArt.Lit("Crate", new Color(0.32f, 0.27f, 0.22f), GameArt.Noise("wood", 128, 0.7f, 0.25f, 3), 0.1f), true);
        crate.name = "Caisse";
        crate.transform.localRotation = Quaternion.Euler(0f, Rand(rnd, -12f, 12f), 0f);
    }

    // Profondeurs : un objet au fond de chacune, et quelques obstacles (blocs, colonnes, plateformes sur pilier) là où
    // il reste assez de place autour pour circuler, loin des escaliers.
    static void AddDepthDecor(Transform parent, Maze m, Room room, System.Random rnd, List<Vector3> spots)
    {
        int w = room.levels.GetLength(0), h = room.levels.GetLength(1);
        bool Roomy(Vector2Int c, float level)
        {
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                {
                    int x = c.x + dx, z = c.y + dz;
                    if (x < 0 || z < 0 || x >= w || z >= h || room.stairCell[x, z] || room.bridgeCell[x, z] || Mathf.Abs(room.levels[x, z] - level) > 0.01f) return false;
                }
            return true;
        }

        // Poutres d'acier (décor : les entités ne s'y engagent pas) ; un objet au milieu d'une passerelle ou d'une poutre
        // sur deux, à aller chercher au-dessus du vide.
        Vector3 o = DepthOrigin(m, room);
        foreach (var (cells, beam) in room.bridges)
        {
            var center = new Vector3(o.x + cells.center.x * Sub, 0f, o.z + cells.center.y * Sub);
            if (beam)
            {
                var size = cells.width > cells.height ? new Vector3(cells.width * Sub, 0.4f, 0.35f) : new Vector3(0.35f, 0.4f, cells.height * Sub);
                GameArt.Prim(PrimitiveType.Cube, parent, center + Vector3.down * 0.2f, size, GameArt.Metal, true).name = "Poutre";
            }
            if (rnd.NextDouble() < 0.5) spots.Add(center);
        }
        foreach (var (cells, level) in room.depths)
        {
            var floor = cells.FindAll(c => Mathf.Abs(room.levels[c.x, c.y] - level) < 0.01f && !room.stairCell[c.x, c.y]);
            if (floor.Count == 0) continue;
            spots.Add(SubCenter(m, room, floor[rnd.Next(floor.Count)]));

            var roomy = floor.FindAll(c => Roomy(c, level));
            int count = Mathf.Min(roomy.Count / 30, 1 + rnd.Next(3));
            for (int k = 0; k < count; k++)
            {
                Vector3 p = SubCenter(m, room, roomy[rnd.Next(roomy.Count)]);
                double kind = rnd.NextDouble();
                if (kind < 0.4) AddBlock(parent, p, level, room.ceil, rnd);
                else if (kind < 0.7 && !m.airLines.Exists(r => r.Contains(new Vector2(p.x, p.z)))) AddColumn(parent, p, Rand(rnd, 0.3f, 0.5f), room.ceil - level, rnd);
                else
                {
                    // Plateforme sur pilier, à portée de saut depuis le fond, avec un objet dessus.
                    float top = level + Rand(rnd, 0.6f, 0.8f);
                    var size = new Vector3(Rand(rnd, 1.2f, 1.8f), 0.25f, Rand(rnd, 1.2f, 1.8f));
                    var plat = GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(p.x, top - size.y * 0.5f, p.z), size, GameArt.CheckerWall, true);
                    plat.name = "Plateforme";
                    plat.transform.localRotation = Quaternion.Euler(0f, Rand(rnd, -35f, 35f), 0f);
                    float hgt = top - size.y - level;
                    GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(p.x, level + hgt * 0.5f, p.z), new Vector3(0.35f, hgt, 0.35f), GameArt.Concrete, true).name = "Pilier";
                    spots.Add(new Vector3(p.x, top, p.z));
                }
            }
        }
    }

    static float Rand(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

    // Renvoie les extrémités du faisceau.
    static (Vector3 a, Vector3 b) AddLaser(Transform parent, Vector3 cellCenter, bool corridorAlongZ, System.Random rnd)
    {
        var go = new GameObject("Laser");
        go.transform.SetParent(parent, false);
        float inner = Cell * 0.5f - Wall * 0.5f;
        Vector3 across = corridorAlongZ ? Vector3.right : Vector3.forward;
        Vector3 offset = (corridorAlongZ ? Vector3.forward : Vector3.right) * Rand(rnd, -1.5f, 1.5f);
        float y = rnd.NextDouble() < 0.6 ? Rand(rnd, 0.25f, 0.45f) : Rand(rnd, 0.9f, 1.4f);
        Vector3 a = cellCenter + offset - across * inner + Vector3.up * y;
        Vector3 b = cellCenter + offset + across * inner + Vector3.up * y;

        Material metal = GameArt.Metal;
        Material tip = GameArt.Unlit("LaserTip", new Color(1f, 0.1f, 0.05f));
        var sparks = new ParticleSystem[2];
        int i = 0;
        foreach (var (p, dir) in new[] { (a, across), (b, -across) })
        {
            var emitter = GameArt.Prim(PrimitiveType.Cylinder, go.transform, p + dir * 0.06f, new Vector3(0.18f, 0.06f, 0.18f), metal);
            emitter.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            var t = GameArt.Prim(PrimitiveType.Sphere, go.transform, p + dir * 0.13f, Vector3.one * 0.07f, tip);
            t.name = "Embout";
            sparks[i++] = AddSparks(go.transform, p + dir * 0.15f, dir, rnd);
        }

        // Faisceau : cylindre (axe Y, 2 m de haut) entouré d'un halo plus large et diffus.
        float length = Vector3.Distance(a, b) - 0.2f;
        var beam = GameArt.Prim(PrimitiveType.Cylinder, go.transform, (a + b) * 0.5f, Vector3.one, GameArt.LaserBeam("LaserBeam", 2.2f, 2.5f));
        beam.name = "Faisceau";
        beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, across);
        beam.transform.localScale = new Vector3(0.035f, length * 0.5f, 0.035f);
        beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var glow = GameArt.Prim(PrimitiveType.Cylinder, beam.transform, Vector3.zero, new Vector3(3.2f, 1f, 3.2f), GameArt.LaserBeam("LaserGlow", 0.7f, 1.2f));
        glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<Laser>().Init(a, b, beam, sparks);
        return (a, b);
    }

    // Gerbe d'étincelles d'un embout ('dir' : vers le faisceau). Ne s'active que quand le laser s'allume.
    static ParticleSystem AddSparks(Transform parent, Vector3 pos, Vector3 dir, System.Random rnd)
    {
        var go = new GameObject("Etincelles");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.LookRotation(dir);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.08f));
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        main.flipRotation = 0f;

        // Crépitement irrégulier : un filet continu plus des petites salves.
        var emission = ps.emission;
        emission.rateOverTime = 14f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.1f, 3, 7, 3, 0.25f + (float)rnd.NextDouble() * 0.2f),
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.02f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 0.6f), new GradientColorKey(new Color(0.6f, 0.05f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        // Rebond sur le sol et les murs.
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.bounce = 0.35f;
        collision.dampen = 0.4f;
        collision.lifetimeLoss = 0.3f;
        collision.radiusScale = 0.5f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1.5f;
        renderer.sharedMaterial = GameArt.Spark;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // Labyrinthe plus sombre, plus terrifiant : néons affaiblis et de portée réduite.
    const float LampDim = 0.55f, LampReach = 0.8f;

    // Néon fixé au plafond ('ceilingPos' : point du plafond).
    static void AddLamp(Transform parent, Vector3 ceilingPos, float range, float intensity, System.Random rnd)
    {
        intensity *= LampDim;
        range *= LampReach;
        var go = new GameObject("Lampe");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = ceilingPos + Vector3.down * 0.05f;
        var bulb = GameArt.Prim(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.9f, 0.05f, 0.25f), GameArt.Unlit("Neon", new Color(0.7f, 0.7f, 0.66f)));
        var l = AddLight(go.transform, Vector3.down * 0.4f, new Color(0.85f, 0.87f, 0.9f), intensity, range);
        go.AddComponent<FlickerLight>().Init(l, bulb.GetComponent<Renderer>(), rnd.NextDouble() < 0.45);
    }

    // Très nombreux et purement visuels : construits sur les images suivantes, pour ne pas figer le jeu à la création d'une zone.
    static void AddEye(Transform parent, Vector3 pos, Vector3 normal, float size)
    {
        WorldStreamer.Enqueue(() =>
        {
            if (parent != null) BuildEye(parent, pos, normal, size);
        });
    }

    static void BuildEye(Transform parent, Vector3 pos, Vector3 normal, float size)
    {
        var go = new GameObject("Oeil");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.LookRotation(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
        go.transform.localScale = Vector3.one * size;

        Mesh sphere = GameArt.LowSphere;
        // Orbite sombre et paupières veinées autour du globe.
        var socket = GameArt.MeshObject("Orbite", go.transform, sphere, GameArt.Lit("EyeSocket", new Color(0.12f, 0.12f, 0.12f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f));
        socket.transform.localScale = new Vector3(1.35f, 1.15f, 0.55f);
        socket.transform.localPosition = Vector3.back * 0.05f;

        var ball = new GameObject("Globe").transform;
        ball.SetParent(go.transform, false);
        ball.localPosition = Vector3.forward * 0.05f;
        GameArt.MeshObject("Blanc", ball, sphere, GameArt.Lit("EyeWhite", Color.white, GameArt.EyeSclera(), 0.85f));
        var iris = GameArt.MeshObject("Iris", ball, GameArt.TinySphere, GameArt.Lit("EyeIris", new Color(0.45f, 0.47f, 0.45f), GameArt.Noise("iris", 64, 0.6f, 0.4f, 21), 0.9f));
        iris.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        iris.transform.localScale = new Vector3(0.5f, 0.5f, 0.22f);
        var pupil = GameArt.MeshObject("Pupille", ball, GameArt.TinySphere, GameArt.Lit("EyePupil", new Color(0.01f, 0.01f, 0.01f), null, 0.95f));
        pupil.transform.localPosition = new Vector3(0f, 0f, 0.47f);
        pupil.transform.localScale = new Vector3(0.24f, 0.24f, 0.1f);

        var lid = GameArt.MeshObject("Paupiere", go.transform, GameArt.TinySphere, GameArt.Lit("EyeLid", new Color(0.2f, 0.19f, 0.19f), null, 0.1f));
        lid.transform.localPosition = ball.localPosition;
        lid.SetActive(false);

        go.AddComponent<Eye>().Init(ball, lid.transform);
    }

    // Pièce d'échecs (modèle Meshy) de la taille d'un joueur, en noir ou blanc astiqué.
    static void AddChessPiece(Transform parent, string model, Vector3 pos, System.Random rnd)
    {
        var holder = GameArt.SpawnModel("Decor/" + model, parent, 1.8f);
        if (holder == null) return;
        holder.name = "Echecs_" + model;
        holder.transform.localPosition = pos;
        holder.transform.localRotation = Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f);
        bool white = rnd.NextDouble() < 0.5;
        Material mat = white ? GameArt.Lit("ChessWhite", new Color(0.9f, 0.9f, 0.88f), null, 0.85f)
                             : GameArt.Lit("ChessBlack", new Color(0.04f, 0.04f, 0.045f), null, 0.85f);
        foreach (var r in holder.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = mat;
            r.sharedMaterials = mats;
        }
        var col = holder.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.9f, 0f);
        col.height = 1.8f;
        col.radius = 0.32f;
    }

    // Colonne noir et blanc du sol au plafond.
    static void AddColumn(Transform parent, Vector3 pos, float radius, float height, System.Random rnd)
    {
        var go = new GameObject("Colonne");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        bool dark = rnd.NextDouble() < 0.5;
        Material shaft = dark ? GameArt.Lit("ColumnBlack", new Color(0.05f, 0.05f, 0.05f), null, 0.85f) : GameArt.Lit("ColumnWhite", new Color(0.88f, 0.88f, 0.86f), null, 0.8f);
        Material ring = dark ? GameArt.Lit("ColumnWhite", new Color(0.88f, 0.88f, 0.86f), null, 0.8f) : GameArt.Lit("ColumnBlack", new Color(0.05f, 0.05f, 0.05f), null, 0.85f);
        GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, height * 0.5f, 0f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), shaft, true);
        GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.15f, 0f), new Vector3(radius * 2.6f, 0.15f, radius * 2.6f), ring);
        GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, height - 0.15f, 0f), new Vector3(radius * 2.6f, 0.15f, radius * 2.6f), ring);
    }

    // Note 05 de la Direction Artistique (V0.4.7) : une sphère du terrain sans collision reçoit une matière fantomatique,
    // légèrement transparente (noire ou blanche selon sa teinte), pour qu'on devine qu'on peut la traverser. Les petites sphères
    // (embouts des Lasers) et les objets interactifs gardent la leur.
    static void MarkIntangibleSpheres(Transform root)
    {
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh != sphere || mf.transform.lossyScale.x < 0.3f || mf.GetComponentInParent<Interactable>() != null) continue;
            var col = mf.GetComponent<Collider>();
            if (col != null && col.enabled && !col.isTrigger) continue;
            var r = mf.GetComponent<Renderer>();
            if (r == null) continue;
            bool dark = r.sharedMaterial == null || r.sharedMaterial.color.grayscale < 0.5f;
            r.sharedMaterial = GameArt.Translucent(dark);
        }
    }

    // Grande sphère posée (à moitié enfoncée dans le sol) ou suspendue au plafond : fantomatique, noire ou blanche, sans
    // collision (on la traverse, et les Balises se voient à travers).
    static void AddBigSphere(Transform parent, Vector3 pos, float radius, float floor, float ceil, System.Random rnd)
    {
        Material mat = GameArt.Translucent(rnd.NextDouble() < 0.5);
        bool hanging = ceil - floor > 5f && rnd.NextDouble() < 0.5;
        float y = hanging ? Rand(rnd, floor + 2.6f + radius, ceil - radius - 0.3f) : floor + radius * Rand(rnd, 0.3f, 0.9f);
        var sphere = GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(pos.x, y, pos.z), Vector3.one * radius * 2f, mat);
        sphere.name = "Sphere";
        if (hanging)
        {
            float cable = ceil - (y + radius);
            GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(pos.x, y + radius + cable * 0.5f, pos.z), new Vector3(0.04f, cable * 0.5f, 0.04f), GameArt.Metal);
        }
    }

    // Salle chaotique : plateformes en escalier, de travers, à atteindre en sautant. L'objet le plus haut est au sommet.
    static void AddPlatforms(Transform parent, Room room, float minX, float maxX, float minZ, float maxZ, System.Random rnd, List<Vector3> spots)
    {
        Material mat = GameArt.CheckerWall;
        Material pillar = GameArt.Concrete;
        int count = 4 + rnd.Next(3) + room.cells.Count / 3;
        Vector3 p = new Vector3(Rand(rnd, minX, maxX), room.floor, Rand(rnd, minZ, maxZ));
        float top = room.floor;
        float maxTop = room.ceil - 2.4f;
        for (int k = 0; k < count; k++)
        {
            top = Mathf.Min(top + Rand(rnd, 0.6f, 0.85f), maxTop);
            var size = new Vector3(Rand(rnd, 1.3f, 2f), 0.25f, Rand(rnd, 1.3f, 2f));
            var plat = GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(p.x, top - size.y * 0.5f, p.z), size, mat, true);
            plat.name = "Plateforme";
            plat.transform.localRotation = Quaternion.Euler(0f, Rand(rnd, -35f, 35f), 0f);
            // Certaines reposent sur un pilier, les autres flottent.
            if (rnd.NextDouble() < 0.5)
            {
                float hgt = top - size.y - room.floor;
                if (hgt > 0.1f) GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(p.x, room.floor + hgt * 0.5f, p.z), new Vector3(0.35f, hgt, 0.35f), pillar, true).name = "Pilier";
            }

            // Prochaine plateforme : un saut plus loin, en restant dans la salle.
            float angle = Rand(rnd, 0f, Mathf.PI * 2f);
            float dist = Rand(rnd, 1.9f, 2.5f);
            Vector3 next = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
            if (next.x < minX || next.x > maxX) next.x = p.x - (next.x - p.x);
            if (next.z < minZ || next.z > maxZ) next.z = p.z - (next.z - p.z);
            next.x = Mathf.Clamp(next.x, minX, maxX);
            next.z = Mathf.Clamp(next.z, minZ, maxZ);
            if (k < count - 1) p = next;
        }
        // Objet à ramasser au sommet (accès difficile).
        spots.Add(new Vector3(p.x, top, p.z));
        // Quelques blocs renversés au sol.
        for (int k = 0; k < 2; k++)
        {
            var size = new Vector3(Rand(rnd, 0.8f, 1.6f), Rand(rnd, 0.6f, 1.4f), Rand(rnd, 0.8f, 1.6f));
            var block = GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(Rand(rnd, minX, maxX), room.floor + size.y * 0.4f, Rand(rnd, minZ, maxZ)), size, pillar, true);
            block.name = "Bloc";
            block.transform.localRotation = Quaternion.Euler(Rand(rnd, -25f, 25f), Rand(rnd, 0f, 360f), Rand(rnd, -25f, 25f));
        }
    }

    // ================= Architectures évolutives (chapitre 10.3, V0.6.3) =================
    // Interprétation des quatre schémas du GDD. Plateformes pleines jusqu'au sol, chacune avec son escalier : elles font partie
    // du bâti, les créatures y montent (la grille des entités ne connaît qu'un sol par point). Passerelles et îlots, qui
    // passent au-dessus du sol, restent du décor solide, réservés aux joueurs. Un pourtour de 3 m reste libre : aucune entrée
    // de la salle n'est jamais bouchée.

    const float TemplateChance = 0.5f, TemplateMinCeil = 6f, TemplateMargin = 3f, LevelLow = 1.6f, LevelHigh = 3f;
    const float StairWidth = 2.4f, StarRadius = 3.2f, IslandRadius = 0.9f, IslandSpacing = 2.1f, IslandDepth = 0.5f;
    const float BridgeWidth = 1.6f, BridgeDepth = 0.6f;
    const int PrismSides = 24;
    const float StatueChance = 0.5f, StatueMinHeight = 4f, StatueMaxHeight = 16f, StatueHeadroom = 2f, StatueFootprint = 0.36f, StatueAisle = 5f;

    internal class Star { public Vector3 pos; public Room room; public bool used; }

    struct TemplatePlatform { public float u, v, a, b, level; public bool round, star; }

    static TemplatePlatform P(float u, float v, float a, float b, float level, bool round = false, bool star = false) =>
        new TemplatePlatform { u = u, v = v, a = a, b = b, level = level, round = round, star = star };

    static readonly TemplatePlatform[][] Templates =
    {
        new[] { P(0.2f, 0.78f, 0.18f, 0.18f, LevelHigh, true, true), P(0.82f, 0.8f, 0.14f, 0.16f, LevelHigh), P(0.5f, 0.22f, 0.22f, 0.16f, LevelLow) },
        new[] { P(0.24f, 0.76f, 0.2f, 0.2f, LevelHigh), P(0.76f, 0.76f, 0.14f, 0.14f, LevelHigh, true, true), P(0.76f, 0.24f, 0.16f, 0.18f, LevelHigh) },
        new[] { P(0.2f, 0.24f, 0.16f, 0.2f, LevelHigh, true), P(0.56f, 0.76f, 0.2f, 0.16f, LevelHigh), P(0.86f, 0.26f, 0.12f, 0.12f, LevelHigh, true, true) },
        new[] { P(0.28f, 0.28f, 0.16f, 0.16f, LevelLow), P(0.72f, 0.28f, 0.16f, 0.16f, LevelHigh, true), P(0.28f, 0.72f, 0.16f, 0.16f, LevelHigh), P(0.72f, 0.72f, 0.16f, 0.16f, LevelLow, true, true) },
    };

    // Plateformes reliées : passerelle, ou suite d'îlots à franchir en sautant (les pointillés bleus du GDD).
    static readonly (int a, int b, bool jump)[][] TemplateLinks =
    {
        new[] { (0, 1, true) },
        new[] { (0, 1, false), (1, 2, false) },
        new[] { (1, 2, false), (0, 1, true) },
        new[] { (1, 2, false) },
    };

    class BuiltPlatform { public Vector3 center; public float reach, level; }

    static bool InBlocks(Maze m, Vector3 p, float padding)
    {
        foreach (Rect r in m.blocks)
            if (p.x > r.xMin - padding && p.x < r.xMax + padding && p.z > r.yMin - padding && p.z < r.yMax + padding) return true;
        return false;
    }

    static void BuildTemplates(Maze m, Transform root, Transform colliders, System.Random rnd)
    {
        var props = new GameObject("Architectures").transform;
        props.SetParent(root, false);
        BuildLinks(m, props);
        foreach (Room room in m.rooms)
            if (room.style == RoomStyle.Template) BuildTemplateRoom(m, room, props, colliders, rnd);
    }

    static void BuildTemplateRoom(Maze m, Room room, Transform props, Transform colliders, System.Random rnd)
    {
        float wallX0 = m.Center(room.rect.xMin, 0).x - Cell * 0.5f, wallX1 = m.Center(room.rect.xMax - 1, 0).x + Cell * 0.5f;
        float wallZ0 = m.Center(0, room.rect.yMin).z - Cell * 0.5f, wallZ1 = m.Center(0, room.rect.yMax - 1).z + Cell * 0.5f;
        float x0 = wallX0 + TemplateMargin, x1 = wallX1 - TemplateMargin, z0 = wallZ0 + TemplateMargin, z1 = wallZ1 - TemplateMargin;
        float width = x1 - x0, depth = z1 - z0;
        Rect stairLimit = Rect.MinMaxRect(wallX0 + 1.5f, wallZ0 + 1.5f, wallX1 - 1.5f, wallZ1 - 1.5f);
        Vector3 middle = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);

        int index = rnd.Next(Templates.Length);
        bool flipU = rnd.NextDouble() < 0.5, flipV = rnd.NextDouble() < 0.5, swap = room.rect.height > room.rect.width;
        var layout = Templates[index];
        var built = new BuiltPlatform[layout.Length];
        // Trémies, paliers et escaliers des liaisons sont déjà réservés.
        var taken = m.blocks.ConvertAll(r => Rect.MinMaxRect(r.xMin - 1.2f, r.yMin - 1.2f, r.xMax + 1.2f, r.yMax + 1.2f));
        var mb = new MeshBuilder();

        for (int n = 0; n < layout.Length; n++)
        {
            TemplatePlatform p = layout[n];
            float u = flipU ? 1f - p.u : p.u, v = flipV ? 1f - p.v : p.v, a = p.a, b = p.b;
            if (swap) { (u, v) = (v, u); (a, b) = (b, a); }
            float halfX = a * width, halfZ = b * depth;
            bool star = false;
            if (p.round)
            {
                float radius = Mathf.Max(1.4f, Mathf.Min(halfX, halfZ));
                star = p.star && StarRadius * 2f <= Mathf.Min(width, depth) * 0.6f;
                if (star) radius = StarRadius;
                halfX = halfZ = radius;
            }
            float cx = Mathf.Clamp(x0 + u * width, x0 + halfX, Mathf.Max(x0 + halfX, x1 - halfX));
            float cz = Mathf.Clamp(z0 + v * depth, z0 + halfZ, Mathf.Max(z0 + halfZ, z1 - halfZ));
            var center = new Vector3(cx, 0f, cz);
            Rect footprint = Rect.MinMaxRect(cx - halfX, cz - halfZ, cx + halfX, cz + halfZ);
            Rect padded = Rect.MinMaxRect(footprint.xMin - 1f, footprint.yMin - 1f, footprint.xMax + 1f, footprint.yMax + 1f);
            if (taken.Exists(r => r.Overlaps(padded))) continue;

            // Escalier du côté qui regarde le plus le centre de la salle et où il tient.
            int steps = Mathf.RoundToInt(p.level / StepHeight);
            float length = steps * StepDepth;
            Vector3 toMiddle = middle - center;
            var dirs = new List<int> { 0, 1, 2, 3 };
            dirs.Sort((d, e) => (toMiddle.x * DI[e] + toMiddle.z * DJ[e]).CompareTo(toMiddle.x * DI[d] + toMiddle.z * DJ[d]));
            Vector3 head = Vector3.zero, outward = Vector3.zero;
            Rect stair = default;
            bool found = false;
            foreach (int d in dirs)
            {
                Vector3 o = new Vector3(DI[d], 0f, DJ[d]);
                float half = StairWidth * 0.5f;
                // Sur un disque, l'escalier mord dans le bord : ses angles ne restent pas dans le vide.
                float edge = p.round ? Mathf.Sqrt(halfX * halfX - half * half) - 0.05f : (d < 2 ? halfX : halfZ) - 0.02f;
                Vector3 h = center + o * edge, f = h + o * length;
                Vector3 side = new Vector3(o.z, 0f, o.x) * half;
                Vector3 c0 = h - side, c1 = f + side;
                Rect r = Rect.MinMaxRect(Mathf.Min(c0.x, c1.x), Mathf.Min(c0.z, c1.z), Mathf.Max(c0.x, c1.x), Mathf.Max(c0.z, c1.z));
                if (r.xMin < stairLimit.xMin || r.xMax > stairLimit.xMax || r.yMin < stairLimit.yMin || r.yMax > stairLimit.yMax) continue;
                if (taken.Exists(q => q.Overlaps(r))) continue;
                head = h; outward = o; stair = r; found = true;
                break;
            }
            if (!found) continue;

            taken.Add(padded);
            taken.Add(stair);
            m.blocks.Add(footprint);
            m.blocks.Add(stair);
            if (p.round)
            {
                AddPrism(mb, center, halfX, p.level, 0f);
                AddPrismCollider(colliders, center, halfX, p.level, 0f);
            }
            else
            {
                var boxCenter = new Vector3(cx, p.level * 0.5f, cz);
                var boxSize = new Vector3(halfX * 2f, p.level, halfZ * 2f);
                mb.Box(boxCenter, boxSize, 0.5f, 0b0011_0111);
                var solid = new GameObject("Plateforme");
                solid.transform.SetParent(colliders, false);
                var box = solid.AddComponent<BoxCollider>();
                box.center = boxCenter;
                box.size = boxSize;
            }
            AddRoomStairs(mb, colliders, head, outward, steps, p.level);
            built[n] = new BuiltPlatform { center = center, reach = Mathf.Max(halfX, halfZ), level = p.level };
            if (star) m.stars.Add(new Star { pos = new Vector3(cx, p.level, cz), room = room });
        }

        foreach (var (ia, ib, jump) in TemplateLinks[index])
        {
            BuiltPlatform a = built[ia], c = built[ib];
            if (a == null || c == null || !Mathf.Approximately(a.level, c.level)) continue;
            Vector3 along = c.center - a.center;
            float distance = along.magnitude;
            along /= distance;
            if (jump) AddIslands(mb, props, a, c, along, distance);
            else AddBridge(props, a, c, along, distance);
        }

        if (!mb.IsEmpty) GameArt.MeshObject("Architecture", props, mb.Build("Architecture"), GameArt.CheckerFloor);
    }

    // Passerelle fine au ras des deux plateformes ; ses extrémités disparaissent dans leur masse.
    static void AddBridge(Transform props, BuiltPlatform a, BuiltPlatform c, Vector3 along, float distance)
    {
        // Un peu sous le sommet des plateformes : ses extrémités, noyées dedans, ne se confondent pas avec lui.
        Vector3 mid = (a.center + c.center) * 0.5f + Vector3.up * (a.level - BridgeDepth * 0.5f - 0.04f);
        var bridge = GameArt.Prim(PrimitiveType.Cube, props, mid, new Vector3(BridgeWidth, BridgeDepth, distance), GameArt.CheckerWall, true);
        bridge.name = "Plateforme";
        bridge.transform.localRotation = Quaternion.LookRotation(along);
    }

    // Îlots ronds entre deux plateformes, à franchir en sautant.
    static void AddIslands(MeshBuilder mb, Transform props, BuiltPlatform a, BuiltPlatform c, Vector3 along, float distance)
    {
        float gap = distance - a.reach - c.reach;
        // Entre deux îlots, au moins 2,1 m de centre à centre : ils ne se chevauchent jamais (diamètre 1,8 m).
        int count = Mathf.Max(1, Mathf.FloorToInt(gap / IslandSpacing) - 1);
        for (int k = 1; k <= count; k++)
        {
            Vector3 p = a.center + along * (a.reach + gap * k / (count + 1f));
            AddPrism(mb, p, IslandRadius, a.level, a.level - IslandDepth);
            AddPrismCollider(props, p, IslandRadius, a.level, a.level - IslandDepth);
        }
    }

    static void AddPrism(MeshBuilder mb, Vector3 center, float radius, float top, float bottom)
    {
        const float texScale = 0.5f;
        mb.Disc(new Vector3(center.x, top, center.z), radius, PrismSides, texScale);
        for (int i = 0; i < PrismSides; i++)
        {
            float a0 = i * Mathf.PI * 2f / PrismSides, a1 = (i + 1) * Mathf.PI * 2f / PrismSides;
            Vector3 d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
            Vector3 b0 = center + d0 * radius + Vector3.up * (bottom - center.y), b1 = center + d1 * radius + Vector3.up * (bottom - center.y);
            Vector3 t0 = b0 + Vector3.up * (top - bottom), t1 = b1 + Vector3.up * (top - bottom);
            float u0 = a0 * radius * texScale, u1 = a1 * radius * texScale;
            mb.Quad(b0, b1, t1, t0, (d0 + d1).normalized, new Vector2(u0, bottom * texScale), new Vector2(u1, bottom * texScale), new Vector2(u1, top * texScale), new Vector2(u0, top * texScale));
            // Dessous fermé pour les îlots, qu'on voit depuis le sol.
            if (bottom > 0f)
            {
                Vector3 below = new Vector3(center.x, bottom, center.z);
                mb.WorldQuad(below, b0, b1, b1, Vector3.down, texScale);
            }
        }
    }

    // Disque plein : prisme convexe, au sommet parfaitement plat (la capsule d'un cylindre Unity serait bombée).
    static void AddPrismCollider(Transform parent, Vector3 center, float radius, float top, float bottom)
    {
        var vertices = new Vector3[PrismSides * 2];
        var triangles = new List<int>();
        for (int i = 0; i < PrismSides; i++)
        {
            float a = i * Mathf.PI * 2f / PrismSides;
            Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            vertices[i] = new Vector3(center.x + d.x, top, center.z + d.z);
            vertices[i + PrismSides] = new Vector3(center.x + d.x, bottom, center.z + d.z);
            int next = (i + 1) % PrismSides;
            triangles.AddRange(new[] { i, i + PrismSides, next + PrismSides, i, next + PrismSides, next });
            if (i > 0 && next != 0)
            {
                triangles.AddRange(new[] { 0, i, next });
                triangles.AddRange(new[] { PrismSides, next + PrismSides, i + PrismSides });
            }
        }
        var mesh = new Mesh { name = "Plateforme", vertices = vertices, triangles = triangles.ToArray() };
        mesh.RecalculateBounds();
        var go = new GameObject("Plateforme");
        go.transform.SetParent(parent, false);
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
    }

    // Escalier du sol au sommet d'une plateforme : 'head' au bord du sommet, descente vers 'outward'.
    static void AddRoomStairs(MeshBuilder mb, Transform colliders, Vector3 head, Vector3 outward, int steps, float top)
    {
        float rise = top / steps;
        for (int k = 0; k < steps; k++)
        {
            // Le sommet de la plateforme tient lieu de marche haute : une marche à sa hauteur se confondrait avec lui.
            float h = top - (k + 1) * rise;
            if (h < 0.01f) continue;
            Vector3 center = head + outward * ((k + 0.5f) * StepDepth) + Vector3.up * (h * 0.5f);
            Vector3 size = Mathf.Abs(outward.x) > 0.5f ? new Vector3(StepDepth, h, StairWidth) : new Vector3(StairWidth, h, StepDepth);
            mb.Box(center, size, 0.5f, 0b0011_0111);
        }
        AddRamp(colliders, head + outward * (steps * StepDepth), head + Vector3.up * top, StairWidth);
    }

    // ================= Liaisons entre salles (flèches bleues du chapitre 10.3, V0.6.3) =================
    // Deux salles à plateformes séparées par au moins une cellule sont reliées par un tube (modèle Meshy) en ligne droite.
    // Souterraine : une trémie et son escalier s'enfoncent dans le sol de chaque salle, le tube passe sous le labyrinthe.
    // Aérienne : un palier et son escalier montent jusqu'à une ouverture percée dans le mur, le tube passe au-dessus des
    // plafonds des cellules intermédiaires. Réservées aux joueurs : rien de tout cela n'appartient au bâti des entités.

    const int ShaftSteps = 15, MaxLinksPerKind = 2, MaxLinkGap = 10;
    const float ShaftRise = 0.25f, ShaftHalfLength = ShaftSteps * StepDepth * 0.5f, ShaftHalfWidth = 1.5f, ShaftDepth = ShaftSteps * ShaftRise;
    // Le tube Meshy est en tonneau (rayon intérieur de 1,16 à 1,47 m) : le niveau de marche est calé sur son rayon moyen.
    const float TubeDiameter = 3f, TubeCenterHeight = 1.29f, TubeSegment = 10f, TubeWalkHalfWidth = 0.7f, TubeRoofHeight = 2.6f;
    const float CollarHalf = 1.62f, CollarInnerRadius = 1.4f, CollarOffset = 0.02f;
    const int CollarSides = 24;
    const int NoTop = 0b0011_1011;   // toutes les faces d'une boîte sauf le dessus
    const float DeckDepth = 2.4f, DeckHalfWidth = 2.2f, AerialClearance = 1.2f, AerialHeadroom = 3.7f, AerialHoleHeight = 2.9f, AerialHoleWidth = 3f, AerialSillDrop = 0.3f;
    const float AerialMinLevel = 5.2f, AerialMaxLevel = 8f;

    internal class RoomLink { public Vector2Int a, b; public int dir; public bool aerial; public float level; public readonly List<Vector2Int> holes = new List<Vector2Int>(); }

    // Cellules d'une liaison, extrémités comprises (dir : 0 = +X, 2 = +Z ; 'a' est toujours avant 'b').
    static IEnumerable<Vector2Int> LinkCells(Vector2Int a, Vector2Int b, int dir)
    {
        for (var c = a; ; c += new Vector2Int(DI[dir], DJ[dir]))
        {
            yield return c;
            if (c == b) break;
        }
    }

    // La cellule ne doit ouvrir sur l'extérieur de sa salle par aucun de ses côtés (sinon la trémie ou le palier boucherait une entrée).
    static bool ClosedCell(Maze m, Vector2Int c, Room room)
    {
        for (int d = 0; d < 4; d++)
            if (m.RoomAt(c.x + DI[d], c.y + DJ[d]) != room && !HasWall(m, c.x, c.y, d)) return false;
        return true;
    }

    static void ChooseLinks(Maze m, System.Random rnd)
    {
        if (m.zone < 2) return;
        var rooms = m.rooms.FindAll(r => r.style == RoomStyle.Template);
        // Toutes les rangées (ou colonnes) communes à deux salles séparées par au moins une cellule.
        var candidates = new List<(Vector2Int a, Vector2Int b, int dir, Room ra, Room rb)>();
        foreach (Room ra in rooms)
            foreach (Room rb in rooms)
            {
                if (ra == rb) continue;
                int gapX = rb.rect.xMin - ra.rect.xMax, gapZ = rb.rect.yMin - ra.rect.yMax;
                if (gapX >= 1 && gapX <= MaxLinkGap)
                    for (int j = Mathf.Max(ra.rect.yMin, rb.rect.yMin); j < Mathf.Min(ra.rect.yMax, rb.rect.yMax); j++)
                        candidates.Add((new Vector2Int(ra.rect.xMax - 1, j), new Vector2Int(rb.rect.xMin, j), 0, ra, rb));
                if (gapZ >= 1 && gapZ <= MaxLinkGap)
                    for (int i = Mathf.Max(ra.rect.xMin, rb.rect.xMin); i < Mathf.Min(ra.rect.xMax, rb.rect.xMax); i++)
                        candidates.Add((new Vector2Int(i, ra.rect.yMax - 1), new Vector2Int(i, rb.rect.yMin), 2, ra, rb));
            }
        for (int k = candidates.Count - 1; k > 0; k--)
        {
            int other = rnd.Next(k + 1);
            (candidates[k], candidates[other]) = (candidates[other], candidates[k]);
        }

        var usedUnderground = new HashSet<Vector2Int>();
        var endsUnderground = new HashSet<Room>();
        int count = 0;
        foreach (var (a, b, dir, ra, rb) in candidates)
        {
            if (count >= MaxLinksPerKind) break;
            if (TryUnderground(m, a, b, dir, ra, rb, usedUnderground, endsUnderground)) count++;
        }
        var usedAerial = new HashSet<Vector2Int>();
        var endsAerial = new HashSet<Room>();
        count = 0;
        foreach (var (a, b, dir, ra, rb) in candidates)
        {
            if (count >= MaxLinksPerKind) break;
            if (TryAerial(m, a, b, dir, ra, rb, usedAerial, endsAerial)) count++;
        }
    }

    static bool LineFree(Vector2Int a, Vector2Int b, int dir, HashSet<Vector2Int> used)
    {
        foreach (var c in LinkCells(a, b, dir)) if (used.Contains(c)) return false;
        return true;
    }

    // Première cellule de la rangée, depuis le bord de la salle vers son intérieur, sans ouverture sur l'extérieur.
    static bool FindClosedCell(Maze m, Vector2Int edge, int inward, Room room, out Vector2Int cell)
    {
        for (cell = edge; m.RoomAt(cell.x, cell.y) == room; cell += new Vector2Int(DI[inward], DJ[inward]))
            if (ClosedCell(m, cell, room)) return true;
        return false;
    }

    static bool TryUnderground(Maze m, Vector2Int a, Vector2Int b, int dir, Room ra, Room rb, HashSet<Vector2Int> used, HashSet<Room> endRooms)
    {
        if (endRooms.Contains(ra) || endRooms.Contains(rb)) return false;
        // La trémie peut être au milieu de la salle : le tube passe aussi sous ses dalles.
        if (!FindClosedCell(m, a, dir + 1, ra, out a) || !FindClosedCell(m, b, dir, rb, out b) || !LineFree(a, b, dir, used)) return false;
        foreach (var c in LinkCells(a, b, dir))
            if (m.IsPit(c.x, c.y) || m.Fixed(c.x, c.y) || m.Reserved(c.x, c.y)) return false;
        m.links.Add(new RoomLink { a = a, b = b, dir = dir, aerial = false, level = -ShaftDepth });
        foreach (var c in LinkCells(a, b, dir)) used.Add(c);
        endRooms.Add(ra); endRooms.Add(rb);
        foreach (var end in new[] { a, b })
        {
            m.shafts[end] = dir;
            Rect hole = ShaftRect(m, end, dir);
            m.blocks.Add(hole);
            m.undergroundBounds.Add(new Bounds(new Vector3(hole.center.x, -3.25f, hole.center.y), new Vector3(hole.width, 5.5f, hole.height)));
        }
        Vector3 ca = m.Center(a.x, a.y), cb = m.Center(b.x, b.y);
        var mid = (ca + cb) * 0.5f;
        float length = Vector3.Distance(ca, cb);
        m.undergroundBounds.Add(new Bounds(new Vector3(mid.x, -3.25f, mid.z), dir < 2 ? new Vector3(length, 5.5f, TubeDiameter) : new Vector3(TubeDiameter, 5.5f, length)));
        return true;
    }

    static bool TryAerial(Maze m, Vector2Int a, Vector2Int b, int dir, Room ra, Room rb, HashSet<Vector2Int> used, HashSet<Room> endRooms)
    {
        if (endRooms.Contains(ra) || endRooms.Contains(rb) || !LineFree(a, b, dir, used)) return false;
        if (!HasWall(m, a.x, a.y, dir) || !HasWall(m, b.x, b.y, dir + 1)) return false;
        // Le palier occupe la largeur de la cellule : il ne doit pas masquer une entrée sur ses côtés.
        if (!DeckFits(m, a, dir, ra) || !DeckFits(m, b, dir, rb)) return false;

        // Le tube passe au-dessus des plafonds bas (leur collision fait 1 m d'épaisseur) ; il traverse à découvert les salles
        // dégagées en hauteur, dont les murs sont percés et le plafond rehaussé. Fosses, salles chaotiques et statues l'arrêtent.
        float highest = 0f;
        var crossed = new List<Room>();
        foreach (var c in LinkCells(a, b, dir))
        {
            if (m.Fixed(c.x, c.y) || m.Reserved(c.x, c.y)) return false;
            Room r = m.RoomAt(c.x, c.y);
            if (r != null && CanCross(r))
            {
                if (!crossed.Contains(r)) crossed.Add(r);
                continue;
            }
            highest = Mathf.Max(highest, m.Ceil(c.x, c.y));
        }
        float level = Mathf.Max(AerialMinLevel, highest + AerialClearance);
        if (level > AerialMaxLevel) return false;

        // Palier et escalier doivent tenir dans la longueur des deux salles.
        float need = DeckDepth + AerialSteps(level) * StepDepth + 1f;
        float lengthA = (dir < 2 ? ra.rect.width : ra.rect.height) * Cell, lengthB = (dir < 2 ? rb.rect.width : rb.rect.height) * Cell;
        if (need > lengthA || need > lengthB) return false;

        // Une ouverture à chaque limite d'une salle haute, dans le mur ou dans le linteau d'une arête ouverte.
        var holes = new List<(int, int, bool)>();
        Vector2Int step = new Vector2Int(DI[dir], DJ[dir]);
        for (var c = a; c != b; c += step)
        {
            Room here = m.RoomAt(c.x, c.y), next = m.RoomAt(c.x + step.x, c.y + step.y);
            if (here == next || !crossed.Contains(here) && !crossed.Contains(next)) continue;
            holes.Add((c.x, c.y, dir == 2));
        }

        // Ni palier ni escalier sur l'accès d'une autre liaison, ni sous un autre tube ; et ce tube ne coupe aucun palier existant.
        var (deckA, stairsA) = DeckRects(m, a, dir + 1, level);
        var (deckB, stairsB) = DeckRects(m, b, dir, level);
        var footprints = new[] { deckA, stairsA, deckB, stairsB };
        foreach (Rect f in footprints)
        {
            Rect padded = Rect.MinMaxRect(f.xMin - 0.5f, f.yMin - 0.5f, f.xMax + 0.5f, f.yMax + 0.5f);
            if (m.blocks.Exists(q => q.Overlaps(padded)) || m.airLines.Exists(q => q.Overlaps(padded))) return false;
        }
        Vector3 ca = m.Center(a.x, a.y), cb = m.Center(b.x, b.y);
        Vector3 half = dir < 2 ? new Vector3(0f, 0f, TubeDiameter * 0.5f) : new Vector3(TubeDiameter * 0.5f, 0f, 0f);
        Rect airLine = MinMax(ca - half, cb + half);
        if (m.deckBlocks.Exists(q => q.Overlaps(airLine))) return false;

        foreach (Room r in crossed)
        {
            r.ceil = Mathf.Max(r.ceil, level + AerialHeadroom);
            r.chess = false;
        }
        var link = new RoomLink { a = a, b = b, dir = dir, aerial = true, level = level };
        m.links.Add(link);
        foreach (var c in LinkCells(a, b, dir)) used.Add(c);
        endRooms.Add(ra); endRooms.Add(rb);
        foreach (var key in holes)
        {
            // Allège abaissée : le bas évasé du tube ne la traverse pas.
            m.wallHoles[key] = (level - AerialSillDrop, level + AerialHoleHeight);
            link.holes.Add(new Vector2Int(key.Item1, key.Item2));
        }
        foreach (Rect f in footprints)
        {
            m.blocks.Add(f);
            m.deckBlocks.Add(f);
        }
        // Couloir d'air du tube : pas de colonne dessous dans les salles qu'il traverse.
        m.airLines.Add(airLine);
        return true;
    }

    // Salle qu'un tube aérien peut traverser : plancher assez bas, sans statue ni plateformes empilées jusqu'au plafond.
    static bool CanCross(Room r) => r.statueHeight <= 0f && r.floor <= AerialMinLevel - TubeRoofHeight && !r.plain && r.style != RoomStyle.Chaotic;

    static bool DeckFits(Maze m, Vector2Int cell, int dir, Room room)
    {
        // Côtés perpendiculaires à la liaison.
        int side = dir < 2 ? 2 : 0;
        foreach (int d in new[] { side, side + 1 })
            if (m.RoomAt(cell.x + DI[d], cell.y + DJ[d]) != room && !HasWall(m, cell.x, cell.y, d)) return false;
        return true;
    }

    static int AerialSteps(float level) => Mathf.CeilToInt(level / ShaftRise);

    // Emprises du palier (contre le mur) et de son escalier, qui descend vers l'intérieur de la salle ('inward' : direction depuis le mur).
    static (Rect deck, Rect stairs) DeckRects(Maze m, Vector2Int cell, int inward, float level)
    {
        Vector3 c = m.Center(cell.x, cell.y), o = new Vector3(DI[inward], 0f, DJ[inward]);
        Vector3 wall = c - o * (Cell * 0.5f);
        Vector3 side = new Vector3(o.z, 0f, o.x);
        Vector3 d0 = wall - side * DeckHalfWidth, d1 = wall + o * DeckDepth + side * DeckHalfWidth;
        Vector3 s0 = wall + o * DeckDepth - side * (StairWidth * 0.5f), s1 = wall + o * (DeckDepth + AerialSteps(level) * StepDepth) + side * (StairWidth * 0.5f);
        return (MinMax(d0, d1), MinMax(s0, s1));
    }

    static Rect MinMax(Vector3 a, Vector3 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));

    static Rect ShaftRect(Maze m, Vector2Int cell, int dir)
    {
        Vector3 c = m.Center(cell.x, cell.y);
        float halfX = dir < 2 ? ShaftHalfLength : ShaftHalfWidth, halfZ = dir < 2 ? ShaftHalfWidth : ShaftHalfLength;
        return Rect.MinMaxRect(c.x - halfX, c.z - halfZ, c.x + halfX, c.z + halfZ);
    }

    // Damier légèrement lumineux : il n'y a aucune lampe dans les tubes.
    static Material TubeMaterial
    {
        get
        {
            Material m = GameArt.Emissive("TubeDamier", Color.white, new Color(0.3f, 0.3f, 0.3f));
            Texture checker = GameArt.CheckerWall.mainTexture;
            if (m.mainTexture != checker)
            {
                m.mainTexture = checker;
                m.SetTexture("_EmissionMap", checker);
            }
            return m;
        }
    }

    static void BuildLinks(Maze m, Transform props)
    {
        Mesh tube = Resources.Load<Mesh>("Art/Decor/TunnelCanin");
        if (tube == null) return;
        foreach (RoomLink link in m.links)
        {
            var mb = new MeshBuilder();
            Vector3 along = new Vector3(DI[link.dir], 0f, DJ[link.dir]);
            Vector3 ca = m.Center(link.a.x, link.a.y), cb = m.Center(link.b.x, link.b.y);
            Vector3 from, to;
            if (link.aerial)
            {
                // Du nu intérieur d'un mur à l'autre : le tube ne déborde pas sur les paliers.
                from = ca + along * (Cell * 0.5f - Wall * 0.5f);
                to = cb - along * (Cell * 0.5f - Wall * 0.5f);
                AddDeck(mb, props, m, link.a, link.dir + 1, link.level);
                AddDeck(mb, props, m, link.b, link.dir, link.level);
                foreach (Vector2Int hole in link.holes)
                {
                    Vector3 edge = m.Center(hole.x, hole.y) + along * (Cell * 0.5f) + Vector3.up * (link.level + TubeCenterHeight);
                    AddCollar(mb, edge - along * (Wall * 0.5f + CollarOffset), -along);
                    AddCollar(mb, edge + along * (Wall * 0.5f + CollarOffset), along);
                }
            }
            else
            {
                float foot = ShaftSteps * StepDepth * 0.5f;
                from = ca + along * foot;
                to = cb - along * foot;
                AddShaft(mb, props, ca, along);
                AddShaft(mb, props, cb, -along);
            }
            AddTube(props, tube, from, to, link.level);
            GameArt.MeshObject("Liaison", props, mb.Build("Liaison"), TubeMaterial);
        }
    }

    // Trémie : escalier qui s'enfonce vers 'forward' jusqu'à l'embouchure du tube, parois de chaque côté, linteau au-dessus du tube.
    static void AddShaft(MeshBuilder mb, Transform props, Vector3 center, Vector3 forward)
    {
        float half = ShaftSteps * StepDepth * 0.5f;
        Vector3 head = center - forward * half, foot = center + forward * half;
        Vector3 side = new Vector3(forward.z, 0f, forward.x);
        bool alongX = Mathf.Abs(forward.x) > 0.5f;
        Vector3 Oriented(float length, float height, float width) => alongX ? new Vector3(length, height, width) : new Vector3(width, height, length);
        // Contremarche du haut, sous le bord du sol.
        mb.Box(head - forward * 0.05f + Vector3.down * (ShaftRise * 0.5f), Oriented(0.1f, ShaftRise, ShaftHalfWidth * 2f), 0.5f, NoTop);
        for (int k = 0; k < ShaftSteps; k++)
        {
            float top = -(k + 1) * ShaftRise;
            float h = top + ShaftDepth;
            if (h < 0.01f) continue;
            mb.Box(head + forward * ((k + 0.5f) * StepDepth) + Vector3.up * (top - h * 0.5f), Oriented(StepDepth, h, ShaftHalfWidth * 2f), 0.5f, 0b0011_0111);
        }
        AddRamp(props, foot + Vector3.down * ShaftDepth, head, ShaftHalfWidth * 2f);
        foreach (float s in new[] { -1f, 1f })
            AddSolid(mb, props, center + side * (s * (ShaftHalfWidth + 0.1f)) + Vector3.down * (ShaftDepth * 0.5f), Oriented(ShaftHalfLength * 2f, ShaftDepth, 0.2f), NoTop);
        float lintel = ShaftDepth - TubeCenterHeight - TubeDiameter * 0.5f;
        AddSolid(mb, props, center + forward * (ShaftHalfLength + 0.1f) + Vector3.down * (lintel * 0.5f), Oriented(0.2f, lintel, ShaftHalfWidth * 2f + 0.4f), NoTop);
        AddCollar(mb, foot + Vector3.down * (ShaftDepth - TubeCenterHeight) - forward * CollarOffset, -forward);
    }

    // Collerette autour d'une embouchure : un carré percé en rond, qui ferme les coins laissés entre l'ouverture et le tube rond.
    static void AddCollar(MeshBuilder mb, Vector3 center, Vector3 facing)
    {
        Vector3 across = new Vector3(facing.z, 0f, facing.x);
        for (int i = 0; i < CollarSides; i++)
        {
            float a0 = i * Mathf.PI * 2f / CollarSides, a1 = (i + 1) * Mathf.PI * 2f / CollarSides;
            Vector3 Inner(float a) => center + (across * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * CollarInnerRadius;
            Vector3 Outer(float a)
            {
                float c = Mathf.Cos(a), si = Mathf.Sin(a);
                return center + (across * c + Vector3.up * si) * (CollarHalf / Mathf.Max(Mathf.Abs(c), Mathf.Abs(si)));
            }
            mb.WorldQuad(Inner(a0), Outer(a0), Outer(a1), Inner(a1), facing, 0.5f);
        }
    }

    // Palier contre le mur, plein jusqu'au sol, et son escalier vers le centre de la salle.
    static void AddDeck(MeshBuilder mb, Transform props, Maze m, Vector2Int cell, int inward, float level)
    {
        Vector3 c = m.Center(cell.x, cell.y), o = new Vector3(DI[inward], 0f, DJ[inward]);
        Vector3 wall = c - o * (Cell * 0.5f - Wall * 0.5f);
        Vector3 side = new Vector3(o.z, 0f, o.x);
        bool alongX = Mathf.Abs(o.x) > 0.5f;
        float depth = DeckDepth - Wall * 0.5f;
        Vector3 size = alongX ? new Vector3(depth, level, DeckHalfWidth * 2f) : new Vector3(DeckHalfWidth * 2f, level, depth);
        AddSolid(mb, props, wall + o * (depth * 0.5f) + Vector3.up * (level * 0.5f), size);
        AddRoomStairs(mb, props, wall + o * depth + Vector3.up * 0f, o, AerialSteps(level), level);
    }

    static void AddSolid(MeshBuilder mb, Transform props, Vector3 center, Vector3 size, int faces = 63)
    {
        mb.Box(center, size, 0.5f, faces);
        var go = new GameObject("Plateforme");
        go.transform.SetParent(props, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    // Tube de 'from' à 'to' : morceaux du modèle Meshy et cage de collisions qui garde le joueur dedans. Pas de sol plat visible :
    // son intersection avec les nervures du tube dessinait des bords déchirés.
    static void AddTube(Transform props, Mesh tube, Vector3 from, Vector3 to, float floorY)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        Vector3 along = delta / length;
        bool alongX = Mathf.Abs(along.x) > 0.5f;
        int pieces = Mathf.Max(1, Mathf.CeilToInt(length / TubeSegment));
        float piece = length / pieces;
        Quaternion rotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
        for (int k = 0; k < pieces; k++)
        {
            var part = GameArt.MeshObject("Tube", props, tube, TubeMaterial);
            part.transform.position = new Vector3(from.x, floorY + TubeCenterHeight, from.z) + along * ((k + 0.5f) * piece);
            part.transform.rotation = rotation;
            part.transform.localScale = new Vector3(piece, TubeDiameter, TubeDiameter);
        }
        Vector3 mid = new Vector3((from.x + to.x) * 0.5f, floorY, (from.z + to.z) * 0.5f);
        Vector3 Oriented(float len, float height, float width) => alongX ? new Vector3(len, height, width) : new Vector3(width, height, len);
        var cage = new GameObject("Tube");
        cage.transform.SetParent(props, false);
        void Add(Vector3 center, Vector3 size)
        {
            var box = cage.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
        Vector3 side = new Vector3(along.z, 0f, along.x);
        Add(mid + Vector3.down * 0.15f, Oriented(length + 0.4f, 0.3f, TubeDiameter));
        Add(mid + Vector3.up * (TubeRoofHeight + 0.1f), Oriented(length + 0.4f, 0.2f, TubeDiameter));
        foreach (float s in new[] { -1f, 1f })
            Add(mid + side * (s * (TubeWalkHalfWidth + 0.1f)) + Vector3.up * (TubeRoofHeight * 0.5f), Oriented(length + 0.4f, TubeRoofHeight, 0.2f));
    }

    // Une zone sur deux, à partir de la Zone 2 : la statue de Pavlov se dresse dans la plus grande salle de plain-pied.
    // La salle est rehaussée s'il le faut, pour laisser au moins 2 m entre le sommet de la tête et le plafond.
    static void ChooseStatueRoom(Maze m, System.Random rnd)
    {
        if (m.zone < 2 || rnd.NextDouble() >= StatueChance) return;
        Room best = null;
        foreach (Room room in m.rooms)
        {
            if (room.plain || room.floor > 0f || room.levels != null || room.rect.width < 2 || room.rect.height < 2) continue;
            if (room.style == RoomStyle.Pit || room.style == RoomStyle.Chaotic || room.style == RoomStyle.Template) continue;
            if (best == null || room.rect.width * room.rect.height > best.rect.width * best.rect.height) best = room;
        }
        if (best == null) return;
        float side = Mathf.Min(best.rect.width, best.rect.height) * Cell - Wall;
        float tallest = Mathf.Min(StatueMaxHeight, 10f * m.zone - StatueHeadroom, (side - StatueAisle) / StatueFootprint);
        if (tallest < StatueMinHeight) return;
        best.statueHeight = Rand(rnd, StatueMinHeight, tallest);
        best.ceil = Mathf.Max(best.ceil, best.statueHeight + StatueHeadroom);
        best.chess = false;
    }

    static void AddStatue(Transform parent, Vector3 center, float height, List<Vector3> spots)
    {
        var statue = GameArt.SpawnModel("Decor/StatuePavlov", parent, height);
        if (statue == null) return;
        statue.name = "Statue de Pavlov";
        // Légère lueur de sa propre texture : sous les hauts plafonds, la lampe seule la laisserait presque noire.
        Texture2D granite = Resources.Load<Texture2D>("Art/Decor/StatuePavlov_base_color");
        Material stone = GameArt.Emissive("StatuePavlov", Color.white, new Color(0.42f, 0.42f, 0.42f));
        stone.mainTexture = granite;
        stone.SetTexture("_EmissionMap", granite);
        foreach (var r in statue.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = stone;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        statue.transform.position = new Vector3(center.x, 0f, center.z);
        // Elle regarde vers le sud, d'où arrivent les joueurs.
        statue.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Bounds b = GameArt.LocalBounds(statue.transform);
        var solid = statue.AddComponent<BoxCollider>();
        solid.center = b.center;
        solid.size = b.size;
        float clearance = Mathf.Max(b.size.x, b.size.z) * 0.75f + MoundClearance;
        spots.RemoveAll(s => Flat(s - center) < clearance);
    }

    static void AddBlock(Transform parent, Vector3 pos, float floor, float ceil, System.Random rnd)
    {
        var size = new Vector3(Rand(rnd, 0.8f, 1.8f), Rand(rnd, 0.5f, 1.6f), Rand(rnd, 0.8f, 1.8f));
        bool hanging = rnd.NextDouble() < 0.4;
        Vector3 p = new Vector3(pos.x, hanging ? ceil - size.y * 0.5f : floor + size.y * 0.5f, pos.z);
        GameArt.Prim(PrimitiveType.Cube, parent, p, size, GameArt.Concrete, true).name = "Bloc";
    }
}
