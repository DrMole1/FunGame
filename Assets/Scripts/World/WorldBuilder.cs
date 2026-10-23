using System.Collections.Generic;
using UnityEngine;

// Construit le Hub (chapiteau circulaire), le labyrinthe procédural qui l'entoure (Zone 1) et la Zone 2.
// Le labyrinthe dépend uniquement de la graine : tous les clients génèrent le même.
public static class WorldBuilder
{
    public const float Cell = 5f;
    // 27 x 27 cellules de 5 m centrées sur le Hub : un coin du labyrinthe est à plus de 100 m du chenil opposé.
    public const int Grid = 27;
    public const int HubMin = Grid / 2 - 2, HubMax = Grid / 2 + 2; // 5 x 5 cellules réservées au Hub
    public const float WallHeight = 4f;      // hauteur des couloirs (les salles ont leur propre hauteur)
    public const float Wall = 0.3f;
    public const float HubRadius = 12f;
    const float HubWallHeight = 5f;
    // Sommet du toit du chapiteau (cône de 6 m posé sur les murs), au-dessus du sol du Hub.
    public const float ChapiteauTop = HubWallHeight + 6f;
    const float DoorWidth = 2.4f, DoorHeight = 2.8f;
    const int DoorI = Grid / 2, DoorJ = HubMax + 1; // première cellule du labyrinthe derrière la porte
    const int ChunkSize = 4;
    const float StepHeight = 0.2f, StepDepth = 0.3f;

    // Angles (degrés, 0 = porte au nord) des emplacements du Hub.
    public static readonly float[] KennelAngles = { 55f, 78f, 101f, 124f, -55f, -78f, -101f, -124f };
    const float TerminalAngle = 150f, LogAngle = 180f, QuotaAngle = -28f, CashAngle = 28f, RoundBoardAngle = -142f;
    const float PogoAngle = -41f, BeaconAngle = 14f;

    public class HubRefs
    {
        public HubDoor door;
        public Transform buttonAnchor, terminalAnchor, pogoAnchor, skipAnchor;
        public Vector3 beaconPosition;   // Balise du Hub, près de la porte
        public float beaconYaw;
        public TextMesh timerText, quotaText, cashText, logText, roundBoardText;
        public ReflectionProbe hubProbe;
        public Vector3[] kennelPositions;
        public Quaternion[] kennelRotations;
    }

    public class MazeRefs
    {
        public GameObject root;
        public Transform structure;  // collisions du bâti (sols, murs, rampes), sans le décor
        public readonly List<Vector3>[] itemSpots = { new List<Vector3>(), new List<Vector3>() }; // par zone
        public MazeNav nav;
        public ZoneDoor zoneDoor;
        public readonly ZoneOrb[] orbs = new ZoneOrb[GameRules.OrbCount];
        public GameObject mist;      // brume de l'Abjuration
    }

    public static bool IsInHub(Vector3 p) => new Vector2(p.x, p.z).sqrMagnitude < HubRadius * HubRadius && p.y < HubWallHeight + 6f;

    static Vector3 Polar(float angleDeg, float radius, float y)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
    }

    // Rotation dont l'avant (+Z) pointe vers le centre du Hub.
    static Quaternion FacingCenter(float angleDeg) => Quaternion.Euler(0f, angleDeg + 180f, 0f);

    // ================= Hub =================

    public static HubRefs BuildHub(Transform parent)
    {
        var refs = new HubRefs();
        var root = new GameObject("Hub").transform;
        root.SetParent(parent, false);

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
        GameArt.Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.005f, 0f), new Vector3(7f, 0.005f, 7f), redMat);
        GameArt.Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.01f, 0f), new Vector3(6.2f, 0.005f, 6.2f), creamMat);

        // Mur circulaire à rayures, ouvert au nord pour la porte.
        const int segments = 32;
        float segLen = 2f * Mathf.PI * (HubRadius + 0.15f) / segments + 0.08f;
        for (int i = 1; i < segments; i++)
        {
            float angle = i * 360f / segments;
            var seg = GameArt.Prim(PrimitiveType.Cube, root, Polar(angle, HubRadius + 0.15f, HubWallHeight * 0.5f),
                new Vector3(segLen, HubWallHeight, 0.3f), i % 2 == 0 ? redMat : creamMat, true);
            seg.name = "HubWall";
            seg.transform.localRotation = FacingCenter(angle);
        }
        // Liseré doré en haut et en bas.
        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;
            for (int k = 0; k < 2; k++)
            {
                var trim = GameArt.Prim(PrimitiveType.Cube, root, Polar(angle, HubRadius - 0.02f, k == 0 ? 0.1f : HubWallHeight - 0.1f),
                    new Vector3(segLen, 0.2f, 0.1f), gold);
                trim.transform.localRotation = FacingCenter(angle);
                if (i == 0 && k == 0) Object.Destroy(trim);
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
        float zIn = HubRadius - 0.4f, zOut = HubRadius + 0.5f + 0.15f;
        float side = DoorWidth * 0.5f + 0.1f;
        foreach (float sx in new[] { -side, side })
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(sx, DoorHeight * 0.5f, (zIn + zOut) * 0.5f), new Vector3(0.2f, DoorHeight, zOut - zIn), redMat, true);
        GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, (DoorHeight + HubWallHeight) * 0.5f, HubRadius + 0.15f),
            new Vector3(DoorWidth + 0.6f, HubWallHeight - DoorHeight, 0.6f), creamMat, true);
        // Entrée du chapiteau (modèle Meshy) : arche de cirque à colonnes, rideaux de velours, panneau à ampoules où
        // s'affiche le Compteur de Temps. Sans le modèle : simple liseré doré au-dessus de la porte.
        Transform arch = BuildEntrance(root);
        if (arch == null)
            GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, DoorHeight + 0.1f, HubRadius - 0.2f), new Vector3(DoorWidth + 0.6f, 0.2f, 0.12f), gold);

        var door = GameArt.Prim(PrimitiveType.Cube, root, new Vector3(0f, DoorHeight * 0.5f, HubRadius + 0.15f),
            new Vector3(DoorWidth + 0.1f, DoorHeight, 0.12f), GameArt.Lit("Door", new Color(0.25f, 0.05f, 0.06f), null, 0.5f), true);
        door.name = "HubDoor";
        refs.door = door.AddComponent<HubDoor>();
        refs.door.Init(DoorHeight - 0.05f);

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
        refs.logText = Screen(root, "Ecran de Logs", LogAngle, HubRadius - 0.9f, 2.6f, new Vector2(8f, 3.6f), 0.027f, TextAnchor.MiddleLeft);
        AddPavlov(refs.logText.transform.parent);
        refs.timerText.color = new Color(1f, 0.35f, 0.3f);
        refs.roundBoardText = Screen(root, "Tableau de Manche", RoundBoardAngle, HubRadius - 0.45f, 2.7f, new Vector2(4.4f, 2.8f), 0.026f);
        refs.roundBoardText.color = new Color(1f, 0.9f, 0.6f);

        // Terminal.
        refs.terminalAnchor = new GameObject("TerminalAnchor").transform;
        refs.terminalAnchor.SetParent(root, false);
        refs.terminalAnchor.localPosition = Polar(TerminalAngle, HubRadius - 1.8f, 0f);
        refs.terminalAnchor.localRotation = FacingCenter(TerminalAngle);

        // Balise du Hub, à côté du passage vers le labyrinthe (vision directe par la porte).
        refs.beaconPosition = Polar(BeaconAngle, 8.6f, 0f);
        refs.beaconYaw = BeaconAngle + 180f;

        // Bouton Rouge au centre.
        refs.buttonAnchor = new GameObject("ButtonAnchor").transform;
        refs.buttonAnchor.SetParent(root, false);

        // Emplacements des Chenils.
        refs.kennelPositions = new Vector3[KennelAngles.Length];
        refs.kennelRotations = new Quaternion[KennelAngles.Length];
        for (int i = 0; i < KennelAngles.Length; i++)
        {
            refs.kennelPositions[i] = Polar(KennelAngles[i], HubRadius - 1.6f, 0f);
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

    static TextMesh Screen(Transform root, string name, float angle, float radius, float y, Vector2 size, float charSize,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name).transform;
        go.SetParent(root, false);
        go.localPosition = Polar(angle, radius, y);
        // Le texte se lit quand on le regarde selon son axe +Z : l'avant pointe vers le mur.
        go.localRotation = Quaternion.Euler(0f, angle, 0f);

        GameArt.Prim(PrimitiveType.Cube, go, new Vector3(0f, 0f, 0.06f), new Vector3(size.x + 0.2f, size.y + 0.2f, 0.08f), GameArt.Lit("ScreenFrame", new Color(0.1f, 0.1f, 0.1f), null, 0.6f));
        GameArt.Prim(PrimitiveType.Cube, go, new Vector3(0f, 0f, 0.01f), new Vector3(size.x, size.y, 0.02f), GameArt.Unlit("ScreenBg", new Color(0.02f, 0.03f, 0.04f)));

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

    enum RoomStyle { Columns, Spheres, Chaotic, Blocks, Pit }

    class Room
    {
        public RectInt rect;
        public float floor, ceil; // hauteur du plancher et du plafond (absolues)
        public float pitDepth;    // style Pit : profondeur maximale sous le niveau du Hub
        public RoomStyle style;
        public bool chess;
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
    class Stair
    {
        public RectInt strip;
        public int dir, steps;
        public float top, bottom;
    }

    // Une zone du labyrinthe : grille de cellules de 5 m. La Zone 1 entoure le Hub ; la Zone 2 est collée à un côté
    // de la Zone 1, reliée par une porte massive. Coordonnées (i, j) : i le long de X, j le long de Z.
    class Maze
    {
        public readonly int zone, N;
        public readonly Vector3 origin;   // coin (x et z minimaux) de la cellule (0, 0)
        public readonly bool hub;
        public readonly Vector2Int start; // cellule d'entrée (derrière la porte du Hub ou de la Zone 2)
        public Maze neighbor;
        public int doorSide = -1;          // Zone 2 : côté collé à la Zone 1, où se trouve la porte
        // Zone 1 : murs extérieurs du côté de la Zone 2 construits par celle-ci (cellules skipFrom à skipTo).
        public int skipSide = -1, skipFrom, skipTo;
        public Vector2Int doorCell = new Vector2Int(-1, -1);
        public readonly HashSet<Vector2Int> keepClear = new HashSet<Vector2Int>(); // cellules sans décor ni piège
        public readonly bool[,] eastWall, northWall; // mur entre (i, j) et (i + 1, j) / entre (i, j) et (i, j + 1)
        public readonly int[,] room;                 // -1 = couloir
        public readonly List<Room> rooms = new List<Room>();

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
        }

        public bool InGrid(int i, int j) => i >= 0 && j >= 0 && i < N && j < N;
        public bool Reserved(int i, int j) => hub && i >= HubMin && i <= HubMax && j >= HubMin && j <= HubMax;
        public Vector3 Center(int i, int j) => origin + new Vector3((i + 0.5f) * Cell, 0f, (j + 0.5f) * Cell);

        public bool CellAt(Vector3 p, out int i, out int j)
        {
            i = Mathf.FloorToInt((p.x - origin.x) / Cell);
            j = Mathf.FloorToInt((p.z - origin.z) / Cell);
            return InGrid(i, j);
        }

        public Room RoomAt(int i, int j) => InGrid(i, j) && room[i, j] >= 0 ? rooms[room[i, j]] : null;
        public float Floor(int i, int j) => RoomAt(i, j)?.floor ?? 0f;
        public bool IsPit(int i, int j) => RoomAt(i, j)?.style == RoomStyle.Pit;

        // Hauteur du plafond ; hors de la grille : celle de la zone voisine, sinon celle des couloirs.
        public float Ceil(int i, int j)
        {
            if (!InGrid(i, j))
                return neighbor != null && neighbor.CellAt(Center(i, j), out int ni, out int nj) ? neighbor.Ceil(ni, nj) : WallHeight;
            return Reserved(i, j) ? WallHeight : RoomAt(i, j)?.ceil ?? WallHeight;
        }

        // Le mur extérieur de la cellule de bord 'index' sur 'side' est construit par la zone voisine.
        public bool SkipsEdge(int side, int index) => side == skipSide && index >= skipFrom && index <= skipTo;
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
        public float[] floors;          // plancher des salles (au-dessus du niveau du Hub)
        public float[] ceilings;        // hauteur sous plafond des salles
        public float[] chaoticCeilings; // salles chaotiques (plateformes)
        public float[] pitCeilings;     // hauteur au-dessus du pourtour des salles à profondeurs
    }

    // Zone 1 : le plancher descend jusqu'à 3 unités sous le Hub, le plafond monte jusqu'à 10 unités au-dessus du plancher.
    // V0.4.2 : environ 25 % de couloirs pour 75 % de salles.
    static readonly ZoneStyle Zone1Style = new ZoneStyle
    {
        maxRooms = 200, minRoomSize = 2, maxRoomSize = 4, roomCoverage = 0.75f,
        pitChance = 0.2f, minPitDepth = 1.5f, maxPitDepth = 3f, maxLayerDepth = 3f,
        floors = new[] { 0f, 0f, 0.8f, 1.6f },
        ceilings = new[] { 4f, 6f, 8f },
        chaoticCeilings = new[] { 7f, 9f, 10f },
        pitCeilings = new[] { 5f, 6f, 7f },
    };

    // Zone 2 (100 m x 100 m) : plancher jusqu'à 10 unités sous le Hub, plafond jusqu'à 30 unités au-dessus du plancher.
    public const int Zone2Size = 20;
    static readonly ZoneStyle Zone2Style = new ZoneStyle
    {
        maxRooms = 24, minRoomSize = 2, maxRoomSize = 5,
        pitChance = 0.3f, minPitDepth = 2f, maxPitDepth = 10f, maxLayerDepth = 4f, bridgeChance = 0.6f,
        floors = new[] { 0f, 0f, 0.8f, 1.6f, 2.4f },
        ceilings = new[] { 5f, 8f, 12f, 18f },
        chaoticCeilings = new[] { 14f, 20f, 26f },
        pitCeilings = new[] { 8f, 12f, 16f, 20f },
    };

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

    // Cellule de bord numéro 'index' sur le côté 'side' d'une grille de 'n' cellules.
    static Vector2Int BorderCell(int side, int index, int n) => side switch
    {
        0 => new Vector2Int(n - 1, index),
        1 => new Vector2Int(0, index),
        2 => new Vector2Int(index, n - 1),
        _ => new Vector2Int(index, 0),
    };

    static void Generate(Maze m, System.Random rnd, ZoneStyle style)
    {
        int n = m.N;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                m.eastWall[i, j] = m.northWall[i, j] = true;
                m.room[i, j] = -1;
            }

        // Labyrinthe parfait (exploration en profondeur) à partir de l'entrée de la zone.
        var visited = new bool[n, n];
        var stack = new Stack<Vector2Int>();
        stack.Push(m.start);
        visited[m.start.x, m.start.y] = true;
        var dirs = new List<int>(4);
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Peek();
            dirs.Clear();
            for (int d = 0; d < 4; d++)
            {
                int ni = c.x + DI[d], nj = c.y + DJ[d];
                if (m.InGrid(ni, nj) && !m.Reserved(ni, nj) && !visited[ni, nj]) dirs.Add(d);
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
                if (m.Reserved(i, j)) continue;
                if (i < n - 1 && !m.Reserved(i + 1, j) && rnd.NextDouble() < 0.12) m.eastWall[i, j] = false;
                if (j < n - 1 && !m.Reserved(i, j + 1) && rnd.NextDouble() < 0.12) m.northWall[i, j] = false;
            }

        // Salles : blocs de cellules ouverts, chacun avec sa hauteur de plancher et de plafond.
        bool coverage = style.roomCoverage > 0f;
        int free = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (!m.Reserved(i, j)) free++;
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
                    if (m.InGrid(i, j) && (m.Reserved(i, j) || cell == m.start || m.keepClear.Contains(cell)
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
            else
            {
                room.style = (RoomStyle)rnd.Next(4);
                // Les salles chaotiques restent de plain-pied : leurs plateformes font le relief.
                room.floor = room.style == RoomStyle.Chaotic ? 0f : Pick(rnd, style.floors);
                room.ceil = room.floor + Pick(rnd, room.style == RoomStyle.Chaotic ? style.chaoticCeilings : style.ceilings);
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

    // Place la Zone 2 contre un côté de la Zone 1 : la porte est sur une cellule de couloir du bord de la Zone 1.
    // V0.4.6 (note 01 du chapitre 9) : du côté opposé à l'ouverture du Hub (qui donne vers +Z) : tout le bord -Z, ou la
    // moitié des bords -X et +X la plus éloignée de l'ouverture.
    static Maze PlaceZone2(Maze z1, System.Random rnd)
    {
        int n2 = Zone2Size;
        var candidates = new List<(int side, int index)>();
        foreach (int s in new[] { 3, 0, 1 })
        {
            int last = s == 3 ? Grid - 3 : Grid / 2 - 1;
            for (int d = 2; d <= last; d++)
            {
                Vector2Int c = BorderCell(s, d, Grid);
                if (z1.room[c.x, c.y] < 0 && !z1.Reserved(c.x, c.y)) candidates.Add((s, d));
            }
        }
        var pick = candidates.Count > 0 ? candidates[rnd.Next(candidates.Count)] : (3, Grid / 2);
        int side = pick.Item1, door = pick.Item2;
        // Décalage de la Zone 2 le long du côté : la porte n'est pas sur un de ses coins.
        int kMin = Mathf.Max(0, door - (n2 - 3)), kMax = Mathf.Min(Grid - n2, door - 2);
        int k = kMin + rnd.Next(Mathf.Max(1, kMax - kMin + 1));
        int e = door - k;
        Vector3 o = z1.origin;
        Vector3 origin;
        Vector2Int entry;
        switch (side)
        {
            case 0: origin = o + new Vector3(Grid * Cell, 0f, k * Cell); entry = new Vector2Int(0, e); break;
            case 1: origin = o + new Vector3(-n2 * Cell, 0f, k * Cell); entry = new Vector2Int(n2 - 1, e); break;
            case 2: origin = o + new Vector3(k * Cell, 0f, Grid * Cell); entry = new Vector2Int(e, 0); break;
            default: origin = o + new Vector3(k * Cell, 0f, -n2 * Cell); entry = new Vector2Int(e, n2 - 1); break;
        }
        var z2 = new Maze(1, n2, origin, false, entry) { neighbor = z1, doorSide = side ^ 1 };
        z2.keepClear.Add(entry);
        z1.skipSide = side;
        z1.skipFrom = k;
        z1.skipTo = k + n2 - 1;
        z1.doorCell = BorderCell(side, door, Grid);
        z1.keepClear.Add(z1.doorCell);
        return z2;
    }

    class Chunk
    {
        public MeshBuilder floor = new MeshBuilder(), ceiling = new MeshBuilder();
        public MeshBuilder checker = new MeshBuilder(), insects = new MeshBuilder();
    }

    // Note 06 de la Direction Artistique (V0.4.10) : tous les murs sont en damier noir et blanc ; certains portent des
    // parcelles d'insectes grouillants (shader InfestedWall), du sol au plafond. Chaque mur : 20 % de chances d'en avoir
    // une, 5 % d'en avoir deux.
    const float InsectPatchChance = 0.2f, InsectPatchPairChance = 0.05f;
    const float InsectPatchMinWidth = 1.3f, InsectPatchMaxWidth = 2.1f;

    // Parcelles d'insectes du labyrinthe (extrémités au sol, en coordonnées monde) : le grouillement s'entend près d'elles.
    public static readonly List<(Vector3 a, Vector3 b)> InsectPatches = new List<(Vector3, Vector3)>();

    public static MazeRefs BuildMaze(Transform parent, int seed)
    {
        var rnd = new System.Random(seed);
        var z1 = new Maze(0, Grid, new Vector3(-Grid * Cell * 0.5f, 0f, -Grid * Cell * 0.5f), true, new Vector2Int(DoorI, DoorJ));
        z1.keepClear.Add(z1.start);
        Generate(z1, rnd, Zone1Style);
        Maze z2 = PlaceZone2(z1, rnd);
        Generate(z2, rnd, Zone2Style);
        var zones = new[] { z1, z2 };

        var refs = new MazeRefs { root = new GameObject("Labyrinthe") };
        InsectPatches.Clear();
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;

        foreach (Maze m in zones) BuildZone(m, root, colliders, new System.Random(seed * 31 + m.zone));
        BuildZoneDoor(z2, root, refs);

        // Les collisions doivent être à jour pour poser le décor sur les sols (dalles, escaliers, fosses, plateformes).
        Physics.SyncTransforms();
        foreach (Maze m in zones) Decorate(m, root, colliders.transform, rnd, refs);
        MarkIntangibleSpheres(root);
        refs.mist = BuildMist(zones, root);
        refs.nav = BuildNav(z1, z2);
        refs.nav.Prepare(colliders.transform, refs.zoneDoor.Blocker);
        foreach (Maze m in zones) AddReflectionProbes(m, root);
        return refs;
    }

    // 'patchRnd' : tirage des parcelles d'insectes (à part, pour ne pas changer le reste de la génération).
    static void BuildZone(Maze m, Transform root, GameObject colliders, System.Random patchRnd)
    {
        int n = m.N;
        int chunks = (n + ChunkSize - 1) / ChunkSize;
        var chunkData = new Chunk[chunks, chunks];
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
                chunkData[x, y] = new Chunk();
        Chunk ChunkOf(int i, int j) => chunkData[Mathf.Clamp(i, 0, n - 1) / ChunkSize, Mathf.Clamp(j, 0, n - 1) / ChunkSize];

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
                if (!m.IsPit(i, j))
                    ch.floor.WorldQuad(c + new Vector3(-e, 0, -e), c + new Vector3(e, 0, -e), c + new Vector3(e, 0, e), c + new Vector3(-e, 0, e), Vector3.up, 0.5f);
                Vector3 top = c + Vector3.up * m.Ceil(i, j);
                ch.ceiling.WorldQuad(top + new Vector3(-e, 0, -e), top + new Vector3(e, 0, -e), top + new Vector3(e, 0, e), top + new Vector3(-e, 0, e), Vector3.down, 0.25f);
            }
        // Sol : une collision par suite de cellules sans fosse sur chaque rangée.
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n;)
            {
                if (m.IsPit(i, j)) { i++; continue; }
                int start = i;
                while (i < n && !m.IsPit(i, j)) i++;
                float a = x0 + start * Cell, b = x0 + i * Cell;
                AddCollider(new Vector3((a + b) * 0.5f, -0.5f, m.Center(0, j).z), new Vector3(b - a, 1f, Cell));
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
                mb.Box(center, Size(side, h), tex, 0b0011_0011);
                AddCollider(center, Size(side, h));
            }
            var header = edge + Vector3.up * ((height + h) * 0.5f);
            mb.Box(header, Size(width, h - height), tex, alongX ? 0b0011_1000 : 0b0000_1011);
            AddCollider(header, Size(width, h - height));
        }

        // Parcelles d'insectes d'un mur plein (Note 06) : sur la face 'normal', de 0 à 'h' ; 'along' : axe du mur.
        // L'UV x porte un numéro de parcelle (partie entière, pour varier la silhouette) et la position en largeur
        // (partie fractionnaire) ; l'UV y, la hauteur relative.
        void InsectPatchesOn(Chunk ch, Vector3 center, Vector3 along, Vector3 normal, float h)
        {
            double roll = patchRnd.NextDouble();
            int count = roll < InsectPatchPairChance ? 2 : roll < InsectPatchPairChance + InsectPatchChance ? 1 : 0;
            float length = Cell - Wall;
            for (int k = 0; k < count; k++)
            {
                float width = Rand(patchRnd, InsectPatchMinWidth, InsectPatchMaxWidth);
                // Deux parcelles : une dans chaque moitié du mur.
                float from = count == 1 ? -length * 0.5f : k == 0 ? -length * 0.5f : 0f;
                float to = count == 1 ? length * 0.5f : k == 0 ? 0f : length * 0.5f;
                float mid = Rand(patchRnd, from + width * 0.5f, Mathf.Max(from + width * 0.5f, to - width * 0.5f));
                Vector3 c = center + along * mid + normal * (Wall * 0.5f + 0.015f);
                Vector3 a = c - along * (width * 0.5f), b = c + along * (width * 0.5f);
                float id = patchRnd.Next(0, 1000);
                ch.insects.Quad(a, b, b + Vector3.up * h, a + Vector3.up * h, normal,
                    new Vector2(id, 0f), new Vector2(id + 0.999f, 0f), new Vector2(id + 0.999f, 1f), new Vector2(id, 1f));
                InsectPatches.Add((a, b));
            }
        }

        // Murs : un segment par arête, entre deux poteaux, aussi haut que le plus haut des deux côtés.
        // Entre une salle haute et un couloir ouvert, un linteau ferme le haut de l'ouverture.
        var posts = new HashSet<Vector2Int>();
        for (int i = -1; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                // Arête verticale (le long de Z) à droite de la cellule (i, j).
                bool leftIn = i >= 0, rightIn = i + 1 < n;
                int boundarySide = !rightIn ? 0 : !leftIn ? 1 : -1;
                if (boundarySide >= 0 && m.SkipsEdge(boundarySide, j)) continue;
                bool wall;
                if (boundarySide >= 0) wall = true;
                else if (m.Reserved(i, j) && m.Reserved(i + 1, j)) wall = false;
                else wall = m.eastWall[i, j] || m.Reserved(i, j) || m.Reserved(i + 1, j);
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i + 1, j));
                float x = x0 + (i + 1) * Cell;
                float z = z0 + j * Cell + Cell * 0.5f;
                Chunk ch = ChunkOf(leftIn ? i : i + 1, j);
                MeshBuilder mb = ch.checker;
                const float tex = 0.5f;
                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i + 1, j));
                    if (h - low < 0.01f) continue;
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Wall, h - low, Cell - Wall), tex, 0b0000_1011);
                    AddCollider(lintel, new Vector3(Wall, h - low, Cell + Wall));
                    continue;
                }
                if (boundarySide >= 0 && boundarySide == m.doorSide && j == m.start.y)
                    Doorway(mb, new Vector3(x, 0f, z), false, h, ZoneDoorWidth, ZoneDoorHeight, tex);
                else
                {
                    mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, Cell - Wall), tex, 0b0000_0011);
                    AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, Cell + Wall));
                    // Parcelle du côté d'une cellule du labyrinthe (au hasard si les deux le sont).
                    bool towardRight = !(rightIn && !m.Reserved(i + 1, j)) ? false
                        : !(leftIn && !m.Reserved(i, j)) || patchRnd.NextDouble() < 0.5;
                    InsectPatchesOn(ch, new Vector3(x, 0f, z), Vector3.forward, towardRight ? Vector3.right : Vector3.left, h);
                }
                posts.Add(new Vector2Int(i + 1, j)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        for (int i = 0; i < n; i++)
            for (int j = -1; j < n; j++)
            {
                // Arête horizontale (le long de X) au-dessus de la cellule (i, j).
                bool lowIn = j >= 0, highIn = j + 1 < n;
                int boundarySide = !highIn ? 2 : !lowIn ? 3 : -1;
                if (boundarySide >= 0 && m.SkipsEdge(boundarySide, i)) continue;
                bool wall;
                if (boundarySide >= 0) wall = true;
                else if (m.Reserved(i, j) && m.Reserved(i, j + 1)) wall = false;
                else wall = m.northWall[i, j] || m.Reserved(i, j) || m.Reserved(i, j + 1);
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i, j + 1));
                float x = x0 + i * Cell + Cell * 0.5f;
                float z = z0 + (j + 1) * Cell;
                Chunk ch = ChunkOf(i, lowIn ? j : j + 1);
                MeshBuilder mb = ch.checker;
                const float tex = 0.5f;

                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i, j + 1));
                    if (h - low < 0.01f) continue;
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Cell - Wall, h - low, Wall), tex, 0b0011_1000);
                    AddCollider(lintel, new Vector3(Cell + Wall, h - low, Wall));
                    continue;
                }
                if (m.hub && i == DoorI && j == HubMax)
                    Doorway(mb, new Vector3(x, 0f, z), true, h, DoorWidth, DoorHeight, tex); // porte du Hub
                else if (boundarySide >= 0 && boundarySide == m.doorSide && i == m.start.x)
                    Doorway(mb, new Vector3(x, 0f, z), true, h, ZoneDoorWidth, ZoneDoorHeight, tex);
                else
                {
                    mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(Cell - Wall, h, Wall), tex, 0b0011_0000);
                    AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Cell + Wall, h, Wall));
                    bool towardHigh = !(highIn && !m.Reserved(i, j + 1)) ? false
                        : !(lowIn && !m.Reserved(i, j)) || patchRnd.NextDouble() < 0.5;
                    InsectPatchesOn(ch, new Vector3(x, 0f, z), Vector3.right, towardHigh ? Vector3.forward : Vector3.back, h);
                }
                posts.Add(new Vector2Int(i, j + 1)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        foreach (var p in posts)
        {
            // Les poteaux du côté partagé avec la Zone 2 sont construits par celle-ci.
            if (m.skipSide >= 0)
            {
                int line = m.skipSide switch { 0 => p.x == n ? p.y : -1, 1 => p.x == 0 ? p.y : -1, 2 => p.y == n ? p.x : -1, _ => p.y == 0 ? p.x : -1 };
                if (line >= m.skipFrom && line <= m.skipTo + 1) continue;
            }
            float h = Mathf.Max(Mathf.Max(m.Ceil(p.x - 1, p.y - 1), m.Ceil(p.x, p.y - 1)), Mathf.Max(m.Ceil(p.x - 1, p.y), m.Ceil(p.x, p.y)));
            var c = new Vector3(x0 + p.x * Cell, h * 0.5f, z0 + p.y * Cell);
            ChunkOf(p.x, p.y).checker.Box(c, new Vector3(Wall, h, Wall), 0.5f, 0b0011_0011);
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
                string name = "Z" + (m.zone + 1) + "_Bloc_" + x + "_" + y;
                if (!ch.floor.IsEmpty) GameArt.MeshObject(name + "_Sol", root, ch.floor.Build(name), GameArt.CheckerFloor);
                if (!ch.ceiling.IsEmpty) GameArt.MeshObject(name + "_Plafond", root, ch.ceiling.Build(name), GameArt.DarkConcrete);
                if (!ch.checker.IsEmpty) GameArt.MeshObject(name + "_Damier", root, ch.checker.Build(name), GameArt.CheckerWall);
                if (!ch.insects.IsEmpty)
                {
                    // Sans sonde de réflexion : les carapaces resteraient pâles en reflétant le damier.
                    var patches = GameArt.MeshObject(name + "_Insectes", root, ch.insects.Build(name), GameArt.InsectPatch);
                    patches.GetComponent<MeshRenderer>().reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
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

    // Porte massive vers la Zone 2 (fermée au départ) et ses trois orbes, côté Zone 1.
    static void BuildZoneDoor(Maze z2, Transform root, MazeRefs refs)
    {
        Vector3 toZone1 = new Vector3(DI[z2.doorSide], 0f, DJ[z2.doorSide]);
        Vector3 edge = z2.Center(z2.start.x, z2.start.y) + toZone1 * (Cell * 0.5f);
        Quaternion facing = Quaternion.LookRotation(toZone1);
        refs.zoneDoor = ZoneDoor.Create(root, edge, facing, ZoneDoorWidth, ZoneDoorHeight);
        Vector3 right = Vector3.Cross(Vector3.up, toZone1);
        for (int k = 0; k < GameRules.OrbCount; k++)
            refs.orbs[k] = ZoneOrb.Create(root, k, edge + toZone1 * 1.9f + right * ((k - 1) * 1.35f), facing);
    }

    // Brume de l'Abjuration : nappes au ras du sol (et dans les fosses), invisibles tant qu'elle n'est pas là.
    static GameObject BuildMist(Maze[] zones, Transform root)
    {
        var mist = new GameObject("Abjuration");
        mist.transform.SetParent(root, false);
        float[] layers = { 0.06f, 0.26f, 0.46f };
        foreach (Maze m in zones)
        {
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
                        GameArt.MeshObject("Brume_Z" + (m.zone + 1) + "_" + x + "_" + y, mist.transform, builders[x, y].Build("Brume"), GameArt.Mist);
        }
        mist.SetActive(false);
        return mist;
    }

    // Zones de la navigation ; la grille fine est calculée plus tard, chez l'hôte seulement (MazeNav.BuildStep).
    static MazeNav BuildNav(Maze z1, Maze z2)
    {
        var nav = new MazeNav();
        foreach (Maze m in new[] { z1, z2 })
        {
            Maze zone = m;
            nav.AddZone(m.origin, m.N, Cell, (i, j) => !zone.Reserved(i, j), (i, j) => zone.Ceil(i, j));
        }
        return nav;
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
                var probe = new GameObject("Reflets_Z" + (m.zone + 1) + "_" + x + "_" + y).AddComponent<ReflectionProbe>();
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
                probe.RenderProbe();
            }
    }

    // ================= Entités et décor =================

    static readonly string[] Limbs = { "Arm", "Hand", "Head", "Bust" };
    static readonly string[] ChessPieces = { "Pawn", "Rook", "Knight", "Bishop", "Queen", "King" };

    static void Decorate(Maze m, Transform root, Transform structure, System.Random rnd, MazeRefs refs)
    {
        var props = new GameObject("Decor_Z" + (m.zone + 1)).transform;
        props.SetParent(root, false);
        List<Vector3> spots = refs.itemSpots[m.zone];

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
                if (room != null)
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
                    if (alongZ || alongX) AddLaser(props, c, alongZ, rnd);
                }

                // Caisses contre les murs des couloirs : de quoi se percher au-dessus de la brume de l'Abjuration.
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
                    if (HasWall(m, i, j, dir))
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

                // Grappes de membres figés (plusieurs mains, bras... côte à côte) qui sortent des murs, du sol et du plafond.
                if (rnd.NextDouble() < (room != null ? 0.16 : 0.05))
                {
                    double kind = rnd.NextDouble();
                    if (kind < 0.6)
                    {
                        int dir = rnd.Next(4);
                        if (HasWall(m, i, j, dir))
                        {
                            Vector3 n = -new Vector3(DI[dir], 0, DJ[dir]);
                            Vector3 along = new Vector3(DJ[dir], 0, DI[dir]);
                            Vector3 p = c - n * (Cell * 0.5f - Wall * 0.5f) + along * Rand(rnd, -0.9f, 0.9f) + Vector3.up * Rand(rnd, floor + 1f, Mathf.Min(ceil - 0.8f, floor + 2.8f));
                            string model = Pick(rnd, Limbs);
                            foreach (var (offset, spin) in LimbCluster(model, along, Vector3.up, rnd))
                                AddLimb(props, model, p + offset, n, spin, rnd, false);
                        }
                    }
                    else if (kind < 0.8)
                    {
                        Vector3 p = c + new Vector3(Rand(rnd, -1f, 1f), ceil, Rand(rnd, -1f, 1f));
                        string model = Pick(rnd, "Arm", "Hand", "Head");
                        foreach (var (offset, spin) in LimbCluster(model, Vector3.right, Vector3.forward, rnd))
                            AddLimb(props, model, p + offset, Vector3.down, spin, rnd, false);
                    }
                    else
                    {
                        Vector3 p = c + new Vector3(Rand(rnd, -0.9f, 0.9f), 0f, Rand(rnd, -0.9f, 0.9f));
                        string model = Pick(rnd, Limbs);
                        foreach (var (offset, spin) in LimbCluster(model, Vector3.right, Vector3.forward, rnd))
                        {
                            Vector3 q = p + offset;
                            AddLimb(props, model, new Vector3(q.x, FloorAt(q, ceil - 0.1f), q.z), Vector3.up, spin, rnd, true);
                        }
                    }
                }
            }

        foreach (Room room in m.rooms)
        {
            Vector3 center = Vector3.zero;
            foreach (var c in room.cells) center += m.Center(c.x, c.y);
            center /= room.cells.Count;
            float height = room.ceil - room.floor + room.pitDepth;
            AddLamp(props, new Vector3(center.x, room.ceil, center.z), 9f + (height - 4f) * 1.5f, 1.4f + (height - 4f) * 0.15f, rnd);

            // Intérieur de la salle (marge pour les murs et les escaliers).
            float minX = m.Center(room.rect.xMin, 0).x - Cell * 0.5f + 1.2f, maxX = m.Center(room.rect.xMax - 1, 0).x + Cell * 0.5f - 1.2f;
            float minZ = m.Center(0, room.rect.yMin).z - Cell * 0.5f + 1.2f, maxZ = m.Center(0, room.rect.yMax - 1).z + Cell * 0.5f - 1.2f;
            if (room.floor > 0f)
            {
                float stairs = Mathf.RoundToInt(room.floor / StepHeight) * StepDepth;
                minX += stairs; maxX -= stairs; minZ += stairs; maxZ -= stairs;
            }
            Vector3 RandomInside(float y) => new Vector3(Rand(rnd, minX, maxX), y, Rand(rnd, minZ, maxZ));

            switch (room.style)
            {
                case RoomStyle.Columns:
                    for (int k = 0; k < room.cells.Count; k++)
                        AddColumn(props, RandomInside(room.floor), Rand(rnd, 0.35f, 0.6f), room.ceil - room.floor, rnd);
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
                else if (kind < 0.7) AddColumn(parent, p, Rand(rnd, 0.3f, 0.5f), room.ceil - level, rnd);
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

    static void AddLaser(Transform parent, Vector3 cellCenter, bool corridorAlongZ, System.Random rnd)
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

    static void AddEye(Transform parent, Vector3 pos, Vector3 normal, float size)
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
        var iris = GameArt.MeshObject("Iris", ball, sphere, GameArt.Lit("EyeIris", new Color(0.45f, 0.47f, 0.45f), GameArt.Noise("iris", 64, 0.6f, 0.4f, 21), 0.9f));
        iris.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        iris.transform.localScale = new Vector3(0.5f, 0.5f, 0.22f);
        var pupil = GameArt.MeshObject("Pupille", ball, sphere, GameArt.Lit("EyePupil", new Color(0.01f, 0.01f, 0.01f), null, 0.95f));
        pupil.transform.localPosition = new Vector3(0f, 0f, 0.47f);
        pupil.transform.localScale = new Vector3(0.24f, 0.24f, 0.1f);

        var lid = GameArt.MeshObject("Paupiere", go.transform, sphere, GameArt.Lit("EyeLid", new Color(0.2f, 0.19f, 0.19f), null, 0.1f));
        lid.transform.localPosition = ball.localPosition;
        lid.SetActive(false);

        go.AddComponent<Eye>().Init(ball, lid.transform);
    }

    // Grappe de membres identiques serrés les uns contre les autres, dans le plan (u, v) de la surface :
    // décalage de chaque membre et rotation autour de la normale (proche d'un membre à l'autre).
    static List<(Vector3 offset, float spin)> LimbCluster(string model, Vector3 u, Vector3 v, System.Random rnd)
    {
        var (min, max, spacing) = model switch
        {
            "Hand" => (3, 5, 0.38f),
            "Arm" => (2, 5, 0.42f),
            "Head" => (2, 4, 0.55f),
            _ => (2, 3, 0.85f),
        };
        int count = rnd.Next(min, max + 1);
        float baseSpin = Rand(rnd, 0f, 360f);
        var list = new List<(Vector3, float)>();
        for (int k = 0; k < count; k++)
        {
            // En rang, chacun un peu décalé et désaligné.
            float across = (k - (count - 1) * 0.5f) * spacing + Rand(rnd, -0.25f, 0.25f) * spacing;
            float depth = Rand(rnd, -0.45f, 0.45f) * spacing;
            list.Add((u * across + v * depth, baseSpin + Rand(rnd, -15f, 15f)));
        }
        return list;
    }

    // Membre (bras, main, tête, buste) en plâtre gris poussiéreux, figé, sortant d'une surface de normale 'normal'
    // et tourné de 'spin' degrés autour d'elle.
    static void AddLimb(Transform parent, string model, Vector3 surfacePoint, Vector3 normal, float spin, System.Random rnd, bool solid)
    {
        var prefab = Resources.Load<GameObject>("Art/Decor/" + model);
        if (prefab == null) return;
        // Axe qui sort de la surface dans le modèle Meshy (la main est couchée, poignet vers -Z).
        Vector3 outAxis = model == "Hand" ? Vector3.forward : Vector3.up;
        float length = model switch { "Arm" => Rand(rnd, 1.5f, 2.1f), "Hand" => Rand(rnd, 0.8f, 1.2f), "Head" => Rand(rnd, 0.6f, 0.9f), _ => Rand(rnd, 1.1f, 1.5f) };
        float sink = model == "Arm" || model == "Hand" ? 0.2f : 0.12f;

        var holder = new GameObject(model);
        holder.transform.SetParent(parent, false);
        var go = Object.Instantiate(prefab, holder.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
        Material dusty = GameArt.DustyLimb;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = dusty;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        Bounds b = GameArt.LocalBounds(holder.transform);
        float extent = Mathf.Max(0.001f, Vector3.Dot(b.size, outAxis));
        go.transform.localScale *= length / extent;
        b = GameArt.LocalBounds(holder.transform);
        float baseAlong = Vector3.Dot(b.min, outAxis) + Vector3.Dot(b.size, outAxis) * sink;
        Vector3 anchor = b.center - outAxis * Vector3.Dot(b.center, outAxis) + outAxis * baseAlong;
        go.transform.localPosition = -anchor;

        // Orienté selon la normale, tourné de 'spin' autour d'elle et un peu penché.
        Quaternion tilt = Quaternion.Euler(Rand(rnd, -20f, 20f), 0f, Rand(rnd, -20f, 20f));
        holder.transform.localRotation = Quaternion.AngleAxis(spin, normal) *Quaternion.FromToRotation(outAxis, normal) * tilt;
        holder.transform.localPosition = surfacePoint;

        if (solid)
        {
            Bounds local = GameArt.LocalBounds(holder.transform);
            var box = holder.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size * 0.75f;
        }
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

    // Grande sphère posée (à moitié enfoncée dans le sol) ou suspendue au plafond : fantomatique, noire ou blanche.
    static void AddBigSphere(Transform parent, Vector3 pos, float radius, float floor, float ceil, System.Random rnd)
    {
        Material mat = GameArt.Translucent(rnd.NextDouble() < 0.5);
        bool hanging = ceil - floor > 5f && rnd.NextDouble() < 0.5;
        float y = hanging ? Rand(rnd, floor + 2.6f + radius, ceil - radius - 0.3f) : floor + radius * Rand(rnd, 0.3f, 0.9f);
        var sphere = GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(pos.x, y, pos.z), Vector3.one * radius * 2f, mat, true);
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

    static void AddBlock(Transform parent, Vector3 pos, float floor, float ceil, System.Random rnd)
    {
        var size = new Vector3(Rand(rnd, 0.8f, 1.8f), Rand(rnd, 0.5f, 1.6f), Rand(rnd, 0.8f, 1.8f));
        bool hanging = rnd.NextDouble() < 0.4;
        Vector3 p = new Vector3(pos.x, hanging ? ceil - size.y * 0.5f : floor + size.y * 0.5f, pos.z);
        GameArt.Prim(PrimitiveType.Cube, parent, p, size, GameArt.Concrete, true).name = "Bloc";
    }
}
