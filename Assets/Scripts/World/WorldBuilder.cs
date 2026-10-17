using System.Collections.Generic;
using UnityEngine;

// Construit le Hub (chapiteau circulaire) et le labyrinthe procédural qui l'entoure.
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
    const float DoorWidth = 2.4f, DoorHeight = 2.8f;
    const int DoorI = Grid / 2, DoorJ = HubMax + 1; // première cellule du labyrinthe derrière la porte
    const int ChunkSize = 4;
    const float StepHeight = 0.2f, StepDepth = 0.3f;

    // Angles (degrés, 0 = porte au nord) des emplacements du Hub.
    public static readonly float[] KennelAngles = { 55f, 78f, 101f, 124f, -55f, -78f, -101f, -124f };
    const float TerminalAngle = 150f, LogAngle = 180f, QuotaAngle = -28f, CashAngle = 28f, RoundBoardAngle = -142f;

    public class HubRefs
    {
        public HubDoor door;
        public Transform buttonAnchor, terminalAnchor;
        public TextMesh timerText, quotaText, cashText, logText, terminalText, roundBoardText;
        public Vector3[] kennelPositions;
        public Quaternion[] kennelRotations;
    }

    public class MazeRefs
    {
        public GameObject root;
        public List<Vector3> itemSpots = new List<Vector3>();
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
        Material redMat = GameArt.Lit("HubRed", red, null, 0.25f);
        Material creamMat = GameArt.Lit("HubCream", cream, null, 0.25f);
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
        GameArt.MeshObject("Roof", root, roof.Build("HubRoof"), GameArt.Lit("HubRoof", Color.white, GameArt.Stripes(red, cream), 0.1f));
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

        // Écrans.
        refs.timerText = Screen(root, "Compteur de Temps", 0f, HubRadius - 0.75f, 4.05f, new Vector2(3.2f, 1.1f), 0.075f);
        refs.quotaText = Screen(root, "Rendu de Quota", QuotaAngle, HubRadius - 0.45f, 2.5f, new Vector2(3.6f, 1.1f), 0.042f);
        refs.cashText = Screen(root, "Rendu de Cagnotte", CashAngle, HubRadius - 0.45f, 2.5f, new Vector2(3.6f, 1.1f), 0.042f);
        refs.logText = Screen(root, "Ecran de Logs", LogAngle, HubRadius - 0.9f, 2.6f, new Vector2(8f, 3.6f), 0.027f, TextAnchor.MiddleLeft);
        refs.timerText.color = new Color(1f, 0.35f, 0.3f);
        refs.roundBoardText = Screen(root, "Tableau de Manche", RoundBoardAngle, HubRadius - 0.45f, 2.7f, new Vector2(4.4f, 2.8f), 0.026f);
        refs.roundBoardText.color = new Color(1f, 0.9f, 0.6f);

        // Terminal.
        refs.terminalAnchor = new GameObject("TerminalAnchor").transform;
        refs.terminalAnchor.SetParent(root, false);
        refs.terminalAnchor.localPosition = Polar(TerminalAngle, HubRadius - 1.8f, 0f);
        refs.terminalAnchor.localRotation = FacingCenter(TerminalAngle);
        refs.terminalText = Screen(root, "Ecran du Terminal", TerminalAngle, HubRadius - 0.45f, 2.6f, new Vector2(3.4f, 2f), 0.02f, TextAnchor.MiddleLeft);
        refs.terminalText.color = new Color(0.45f, 1f, 0.5f);

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
            // Petit tapis pour marquer l'emplacement même vide.
            var mat = GameArt.Prim(PrimitiveType.Cube, root, refs.kennelPositions[i] + Vector3.up * 0.01f, new Vector3(2f, 0.02f, 2f), gold);
            mat.transform.localRotation = refs.kennelRotations[i];
        }

        // Décor : podium et ballons de cirque (entre le Rendu de Quota et les chenils, sans masquer d'écran).
        var podium = GameArt.Prim(PrimitiveType.Cylinder, root, Polar(-41f, 9.2f, 0.3f), new Vector3(2.2f, 0.3f, 2.2f), redMat, true);
        GameArt.Prim(PrimitiveType.Sphere, root, podium.transform.localPosition + Vector3.up * 1.05f, Vector3.one * 1.5f, GameArt.Lit("Ball", new Color(0.2f, 0.35f, 0.85f), GameArt.Stripes(new Color(0.2f, 0.35f, 0.85f), cream), 0.5f), true);
        GameArt.Prim(PrimitiveType.Sphere, root, Polar(-24f, 10.8f, 0.45f), Vector3.one * 0.9f, gold, true);
        GameArt.Prim(PrimitiveType.Sphere, root, Polar(-33f, 10.7f, 0.35f), Vector3.one * 0.7f, redMat, true);

        return refs;
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

    enum RoomStyle { Columns, Spheres, Chaotic, Blocks }

    class Room
    {
        public RectInt rect;
        public float floor, ceil; // hauteur du plancher et du plafond (absolues)
        public RoomStyle style;
        public bool chess;
        public readonly List<Vector2Int> cells = new List<Vector2Int>();
    }

    class Maze
    {
        public bool[,] eastWall = new bool[Grid, Grid];  // mur entre (i, j) et (i + 1, j)
        public bool[,] northWall = new bool[Grid, Grid]; // mur entre (i, j) et (i, j + 1)
        public int[,] room = new int[Grid, Grid];        // -1 = couloir
        public readonly List<Room> rooms = new List<Room>();

        public Room RoomAt(int i, int j) => InGrid(i, j) && room[i, j] >= 0 ? rooms[room[i, j]] : null;
        public float Floor(int i, int j) => RoomAt(i, j)?.floor ?? 0f;
        // Hauteur du plafond ; hors labyrinthe (bords, Hub) : hauteur des couloirs.
        public float Ceil(int i, int j) => !InGrid(i, j) || Reserved(i, j) ? WallHeight : RoomAt(i, j)?.ceil ?? WallHeight;
    }

    static bool Reserved(int i, int j) => i >= HubMin && i <= HubMax && j >= HubMin && j <= HubMax;
    static bool InGrid(int i, int j) => i >= 0 && j >= 0 && i < Grid && j < Grid;
    static Vector3 CellCenter(int i, int j) => new Vector3((i - Grid / 2) * Cell, 0f, (j - Grid / 2) * Cell);

    // Mur sur le côté 'dir' (0 = +X, 1 = -X, 2 = +Z, 3 = -Z) de la cellule.
    static bool HasWall(Maze m, int i, int j, int dir)
    {
        switch (dir)
        {
            case 0: return i == Grid - 1 || m.eastWall[i, j];
            case 1: return i == 0 || m.eastWall[i - 1, j];
            case 2: return j == Grid - 1 || m.northWall[i, j];
            default: return j == 0 || m.northWall[i, j - 1];
        }
    }

    static void SetWall(Maze m, int i, int j, int dir, bool value)
    {
        switch (dir)
        {
            case 0: if (i < Grid - 1) m.eastWall[i, j] = value; break;
            case 1: if (i > 0) m.eastWall[i - 1, j] = value; break;
            case 2: if (j < Grid - 1) m.northWall[i, j] = value; break;
            default: if (j > 0) m.northWall[i, j - 1] = value; break;
        }
    }

    static readonly int[] DI = { 1, -1, 0, 0 }, DJ = { 0, 0, 1, -1 };

    static T Pick<T>(System.Random rnd, params T[] values) => values[rnd.Next(values.Length)];

    static Maze Generate(System.Random rnd)
    {
        var m = new Maze();
        for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                m.eastWall[i, j] = m.northWall[i, j] = true;
                m.room[i, j] = -1;
            }

        // Labyrinthe parfait (exploration en profondeur) à partir de la porte du Hub.
        var visited = new bool[Grid, Grid];
        var stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(DoorI, DoorJ));
        visited[DoorI, DoorJ] = true;
        var dirs = new List<int>(4);
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Peek();
            dirs.Clear();
            for (int d = 0; d < 4; d++)
            {
                int ni = c.x + DI[d], nj = c.y + DJ[d];
                if (InGrid(ni, nj) && !Reserved(ni, nj) && !visited[ni, nj]) dirs.Add(d);
            }
            if (dirs.Count == 0) { stack.Pop(); continue; }
            int dir = dirs[rnd.Next(dirs.Count)];
            SetWall(m, c.x, c.y, dir, false);
            visited[c.x + DI[dir], c.y + DJ[dir]] = true;
            stack.Push(new Vector2Int(c.x + DI[dir], c.y + DJ[dir]));
        }

        // Quelques boucles pour éviter un labyrinthe trop linéaire.
        for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                if (Reserved(i, j)) continue;
                if (i < Grid - 1 && !Reserved(i + 1, j) && rnd.NextDouble() < 0.12) m.eastWall[i, j] = false;
                if (j < Grid - 1 && !Reserved(i, j + 1) && rnd.NextDouble() < 0.12) m.northWall[i, j] = false;
            }

        // Salles : blocs de 2x2 à 4x3 cellules ouverts, chacune avec sa hauteur de plancher et de plafond.
        for (int attempt = 0; attempt < 700 && m.rooms.Count < 34; attempt++)
        {
            int w, h;
            if (rnd.NextDouble() < 0.15) { w = 4; h = 3; }
            else { w = rnd.NextDouble() < 0.4 ? 3 : 2; h = 2; }
            if (rnd.NextDouble() < 0.5) { int t = w; w = h; h = t; }
            int i0 = rnd.Next(0, Grid - w + 1), j0 = rnd.Next(0, Grid - h + 1);
            bool ok = true;
            for (int i = i0 - 1; i <= i0 + w && ok; i++)
                for (int j = j0 - 1; j <= j0 + h && ok; j++)
                {
                    bool inside = i >= i0 && i < i0 + w && j >= j0 && j < j0 + h;
                    // Une cellule d'écart avec le Hub, la porte et les autres salles.
                    if (InGrid(i, j) && (Reserved(i, j) || (i == DoorI && j == DoorJ) || m.room[i, j] >= 0)) ok = false;
                    if (inside && !InGrid(i, j)) ok = false;
                }
            if (!ok) continue;

            var room = new Room { rect = new RectInt(i0, j0, w, h), style = (RoomStyle)rnd.Next(4) };
            // Les salles chaotiques restent de plain-pied : leurs plateformes font le relief.
            room.floor = room.style == RoomStyle.Chaotic ? 0f : Pick(rnd, 0f, 0f, 0.8f, 1.6f);
            room.ceil = room.floor + (room.style == RoomStyle.Chaotic ? Pick(rnd, 7f, 9f) : Pick(rnd, 4f, 6f, 8f));
            room.chess = rnd.NextDouble() < 0.2;
            int index = m.rooms.Count;
            m.rooms.Add(room);
            for (int i = i0; i < i0 + w; i++)
                for (int j = j0; j < j0 + h; j++)
                {
                    m.room[i, j] = index;
                    room.cells.Add(new Vector2Int(i, j));
                    if (i < i0 + w - 1) m.eastWall[i, j] = false;
                    if (j < j0 + h - 1) m.northWall[i, j] = false;
                }
        }
        return m;
    }

    class Chunk
    {
        public MeshBuilder floor = new MeshBuilder(), ceiling = new MeshBuilder();
        public MeshBuilder concrete = new MeshBuilder(), checker = new MeshBuilder();
    }

    public static MazeRefs BuildMaze(Transform parent, int seed)
    {
        var rnd = new System.Random(seed);
        Maze m = Generate(rnd);
        var refs = new MazeRefs { root = new GameObject("Labyrinthe") };
        Transform root = refs.root.transform;
        root.SetParent(parent, false);

        int chunks = (Grid + ChunkSize - 1) / ChunkSize;
        var chunkData = new Chunk[chunks, chunks];
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
                chunkData[x, y] = new Chunk();
        Chunk ChunkOf(int i, int j) => chunkData[Mathf.Clamp(i, 0, Grid - 1) / ChunkSize, Mathf.Clamp(j, 0, Grid - 1) / ChunkSize];

        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        void AddCollider(Vector3 center, Vector3 size)
        {
            var bc = colliders.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        float half = Grid * Cell * 0.5f;

        // Sols et plafonds.
        for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                if (Reserved(i, j)) continue;
                Vector3 c = CellCenter(i, j);
                float e = Cell * 0.5f;
                Chunk ch = ChunkOf(i, j);
                ch.floor.WorldQuad(c + new Vector3(-e, 0, -e), c + new Vector3(e, 0, -e), c + new Vector3(e, 0, e), c + new Vector3(-e, 0, e), Vector3.up, 0.5f);
                Vector3 top = c + Vector3.up * m.Ceil(i, j);
                ch.ceiling.WorldQuad(top + new Vector3(-e, 0, -e), top + new Vector3(e, 0, -e), top + new Vector3(e, 0, e), top + new Vector3(-e, 0, e), Vector3.down, 0.25f);
            }
        AddCollider(new Vector3(0f, -0.5f, 0f), new Vector3(Grid * Cell, 1f, Grid * Cell));
        // Plafonds : une collision par suite de cellules de même hauteur sur chaque rangée (rien au-dessus du Hub).
        for (int j = 0; j < Grid; j++)
            for (int i = 0; i < Grid;)
            {
                if (Reserved(i, j)) { i++; continue; }
                float ceil = m.Ceil(i, j);
                int start = i;
                while (i < Grid && !Reserved(i, j) && Mathf.Approximately(m.Ceil(i, j), ceil)) i++;
                float x0 = -half + start * Cell, x1 = -half + i * Cell;
                AddCollider(new Vector3((x0 + x1) * 0.5f, ceil + 0.5f, CellCenter(0, j).z), new Vector3(x1 - x0, 1f, Cell));
            }

        // Murs : un segment par arête, entre deux poteaux, aussi haut que le plus haut des deux côtés.
        // Entre une salle haute et un couloir ouvert, un linteau ferme le haut de l'ouverture.
        var posts = new HashSet<Vector2Int>();
        for (int i = -1; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                // Arête verticale (le long de Z) à droite de la cellule (i, j).
                bool leftIn = i >= 0, rightIn = i + 1 < Grid;
                bool wall;
                if (!leftIn || !rightIn) wall = true;
                else if (Reserved(i, j) && Reserved(i + 1, j)) wall = false;
                else wall = m.eastWall[i, j] || Reserved(i, j) || Reserved(i + 1, j);
                bool roomWall = (leftIn && m.room[i, j] >= 0) || (rightIn && m.room[i + 1, j] >= 0);
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i + 1, j));
                float x = -half + (i + 1) * Cell;
                float z = -half + j * Cell + Cell * 0.5f;
                Chunk ch = ChunkOf(leftIn ? i : i + 1, j);
                MeshBuilder mb = roomWall ? ch.checker : ch.concrete;
                float tex = roomWall ? 0.5f : 0.25f;
                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i + 1, j));
                    if (h - low < 0.01f) continue;
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Wall, h - low, Cell - Wall), tex, 0b0000_1011);
                    AddCollider(lintel, new Vector3(Wall, h - low, Cell + Wall));
                    continue;
                }
                mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, Cell - Wall), tex, 0b0000_0011);
                AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Wall, h, Cell + Wall));
                posts.Add(new Vector2Int(i + 1, j)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        for (int i = 0; i < Grid; i++)
            for (int j = -1; j < Grid; j++)
            {
                // Arête horizontale (le long de X) au-dessus de la cellule (i, j).
                bool lowIn = j >= 0, highIn = j + 1 < Grid;
                bool wall;
                if (!lowIn || !highIn) wall = true;
                else if (Reserved(i, j) && Reserved(i, j + 1)) wall = false;
                else wall = m.northWall[i, j] || Reserved(i, j) || Reserved(i, j + 1);
                bool roomWall = (lowIn && m.room[i, j] >= 0) || (highIn && m.room[i, j + 1] >= 0);
                float h = Mathf.Max(m.Ceil(i, j), m.Ceil(i, j + 1));
                float x = -half + i * Cell + Cell * 0.5f;
                float z = -half + (j + 1) * Cell;
                Chunk ch = ChunkOf(i, lowIn ? j : j + 1);
                MeshBuilder mb = roomWall ? ch.checker : ch.concrete;
                float tex = roomWall ? 0.5f : 0.25f;

                if (!wall)
                {
                    float low = Mathf.Min(m.Ceil(i, j), m.Ceil(i, j + 1));
                    if (h - low < 0.01f) continue;
                    var lintel = new Vector3(x, (low + h) * 0.5f, z);
                    mb.Box(lintel, new Vector3(Cell - Wall, h - low, Wall), tex, 0b0011_1000);
                    AddCollider(lintel, new Vector3(Cell + Wall, h - low, Wall));
                    continue;
                }
                if (i == DoorI && j == HubMax)
                {
                    // Mur du Hub côté labyrinthe : ouverture de la porte.
                    float side = (Cell - Wall - DoorWidth) * 0.5f;
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        var center = new Vector3(x + sx * (DoorWidth * 0.5f + side * 0.5f), h * 0.5f, z);
                        mb.Box(center, new Vector3(side, h, Wall), tex, 0b0011_0011);
                        AddCollider(center, new Vector3(side, h, Wall));
                    }
                    var header = new Vector3(x, (DoorHeight + h) * 0.5f, z);
                    mb.Box(header, new Vector3(DoorWidth, h - DoorHeight, Wall), tex, 0b0011_1000);
                    AddCollider(header, new Vector3(DoorWidth, h - DoorHeight, Wall));
                }
                else
                {
                    mb.Box(new Vector3(x, h * 0.5f, z), new Vector3(Cell - Wall, h, Wall), tex, 0b0011_0000);
                    AddCollider(new Vector3(x, h * 0.5f, z), new Vector3(Cell + Wall, h, Wall));
                }
                posts.Add(new Vector2Int(i, j + 1)); posts.Add(new Vector2Int(i + 1, j + 1));
            }
        foreach (var p in posts)
        {
            float h = Mathf.Max(Mathf.Max(m.Ceil(p.x - 1, p.y - 1), m.Ceil(p.x, p.y - 1)), Mathf.Max(m.Ceil(p.x - 1, p.y), m.Ceil(p.x, p.y)));
            var c = new Vector3(-half + p.x * Cell, h * 0.5f, -half + p.y * Cell);
            ChunkOf(p.x, p.y).concrete.Box(c, new Vector3(Wall, h, Wall), 0.25f, 0b0011_0011);
        }

        // Salles surélevées : dalle au niveau du plancher et escalier à chaque entrée.
        foreach (Room room in m.rooms)
        {
            if (room.floor <= 0f) continue;
            int steps = Mathf.RoundToInt(room.floor / StepHeight);
            float depth = steps * StepDepth;
            foreach (var cell in room.cells)
            {
                Vector3 c = CellCenter(cell.x, cell.y);
                float minX = c.x - Cell * 0.5f, maxX = c.x + Cell * 0.5f, minZ = c.z - Cell * 0.5f, maxZ = c.z + Cell * 0.5f;
                Chunk ch = ChunkOf(cell.x, cell.y);
                for (int d = 0; d < 4; d++)
                {
                    int ni = cell.x + DI[d], nj = cell.y + DJ[d];
                    if (HasWall(m, cell.x, cell.y, d) || m.RoomAt(ni, nj) == room) continue;
                    switch (d)
                    {
                        case 0: maxX -= depth; break;
                        case 1: minX += depth; break;
                        case 2: maxZ -= depth; break;
                        default: minZ += depth; break;
                    }
                    AddStairs(ch.floor, colliders.transform, c, d, steps, room.floor);
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
                string n = "Bloc_" + x + "_" + y;
                if (!ch.floor.IsEmpty) GameArt.MeshObject(n + "_Sol", root, ch.floor.Build(n), GameArt.CheckerFloor);
                if (!ch.ceiling.IsEmpty) GameArt.MeshObject(n + "_Plafond", root, ch.ceiling.Build(n), GameArt.DarkConcrete);
                if (!ch.concrete.IsEmpty)
                {
                    // Sans sonde de réflexion : les carapaces resteraient pâles en reflétant le damier.
                    var walls = GameArt.MeshObject(n + "_Beton", root, ch.concrete.Build(n), GameArt.InfestedWall);
                    walls.GetComponent<MeshRenderer>().reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                if (!ch.checker.IsEmpty) GameArt.MeshObject(n + "_Damier", root, ch.checker.Build(n), GameArt.CheckerWall);
            }

        // Les collisions doivent être à jour pour poser le décor sur les sols (dalles, escaliers, plateformes).
        Physics.SyncTransforms();
        Decorate(m, root, colliders.transform, rnd, refs);
        AddReflectionProbes(root, chunks);
        return refs;
    }

    // Escalier montant depuis le côté 'dir' de la cellule vers le plancher de la salle. Les marches sont visuelles ;
    // la collision est une rampe (le personnage monte sans buter sur les marches).
    static void AddStairs(MeshBuilder mb, Transform colliders, Vector3 cellCenter, int dir, int steps, float floor)
    {
        Vector3 outward = new Vector3(DI[dir], 0f, DJ[dir]);
        Vector3 inward = -outward;
        Vector3 edge = cellCenter + outward * (Cell * 0.5f);
        float width = Cell - Wall;
        float rise = floor / steps;
        for (int k = 1; k <= steps; k++)
        {
            float h = k * rise;
            Vector3 center = edge + inward * ((k - 0.5f) * StepDepth) + Vector3.up * (h * 0.5f);
            Vector3 size = dir < 2 ? new Vector3(StepDepth, h, width) : new Vector3(width, h, StepDepth);
            mb.Box(center, size, 0.5f, 0b0011_0111);
        }

        float depth = steps * StepDepth;
        Vector3 bottom = edge + Vector3.up * 0.05f, top = edge + inward * depth + Vector3.up * floor;
        Vector3 slope = top - bottom;
        var ramp = new GameObject("Rampe").transform;
        ramp.SetParent(colliders, false);
        ramp.localRotation = Quaternion.LookRotation(slope.normalized, Vector3.up);
        ramp.localPosition = (bottom + top) * 0.5f - ramp.localRotation * Vector3.up * 0.1f;
        var box = ramp.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(width, 0.2f, slope.magnitude);
    }

    // Sondes de réflexion (une par bloc), rendues une seule fois une fois le décor posé :
    // le damier astiqué y reflète les murs, les lampes et les yeux.
    static void AddReflectionProbes(Transform root, int chunks)
    {
        const float height = 10f;
        for (int x = 0; x < chunks; x++)
            for (int y = 0; y < chunks; y++)
            {
                Vector3 min = CellCenter(x * ChunkSize, y * ChunkSize) - new Vector3(Cell, 0f, Cell) * 0.5f;
                Vector3 max = CellCenter(Mathf.Min(x * ChunkSize + ChunkSize - 1, Grid - 1), Mathf.Min(y * ChunkSize + ChunkSize - 1, Grid - 1))
                    + new Vector3(Cell, 0f, Cell) * 0.5f + Vector3.up * height;
                var probe = new GameObject("Reflets_" + x + "_" + y).AddComponent<ReflectionProbe>();
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
        var props = new GameObject("Decor").transform;
        props.SetParent(root, false);

        // Hauteur du sol (dalle, marche ou plateforme) sous un point, sans compter le décor.
        float FloorAt(Vector3 p, float from)
        {
            float best = 0f;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, from + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(structure) && hit.point.y > best) best = hit.point.y;
            return best;
        }

        for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                if (Reserved(i, j)) continue;
                Vector3 c = CellCenter(i, j);
                Room room = m.RoomAt(i, j);
                float floor = m.Floor(i, j), ceil = m.Ceil(i, j);

                bool nearDoor = Mathf.Abs(i - DoorI) + Mathf.Abs(j - DoorJ) <= 1;
                if (!nearDoor)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.2f, 1.2f), 0f, Rand(rnd, -1.2f, 1.2f));
                    refs.itemSpots.Add(new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z));
                }
                if (room != null)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.8f, 1.8f), 0f, Rand(rnd, -1.8f, 1.8f));
                    refs.itemSpots.Add(new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z));
                }

                // Lasers dans les couloirs droits (murs parallèles de part et d'autre).
                if (room == null && !nearDoor && rnd.NextDouble() < 0.45)
                {
                    bool alongZ = HasWall(m, i, j, 0) && HasWall(m, i, j, 1) && !HasWall(m, i, j, 2) && !HasWall(m, i, j, 3);
                    bool alongX = HasWall(m, i, j, 2) && HasWall(m, i, j, 3) && !HasWall(m, i, j, 0) && !HasWall(m, i, j, 1);
                    if (alongZ || alongX) AddLaser(props, c, alongZ, rnd);
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

                // Membres noirs figés qui sortent des murs, du sol et du plafond.
                if (rnd.NextDouble() < (room != null ? 0.3 : 0.12))
                {
                    double kind = rnd.NextDouble();
                    if (kind < 0.6)
                    {
                        int dir = rnd.Next(4);
                        if (HasWall(m, i, j, dir))
                        {
                            Vector3 n = -new Vector3(DI[dir], 0, DJ[dir]);
                            Vector3 along = new Vector3(DJ[dir], 0, DI[dir]);
                            Vector3 p = c - n * (Cell * 0.5f - Wall * 0.5f) + along * Rand(rnd, -1.5f, 1.5f) + Vector3.up * Rand(rnd, floor + 0.8f, Mathf.Min(ceil - 0.6f, floor + 3f));
                            AddLimb(props, Pick(rnd, Limbs), p, n, rnd, false);
                        }
                    }
                    else if (kind < 0.8)
                        AddLimb(props, Pick(rnd, "Arm", "Hand", "Head"), c + new Vector3(Rand(rnd, -1.6f, 1.6f), ceil, Rand(rnd, -1.6f, 1.6f)), Vector3.down, rnd, false);
                    else
                    {
                        Vector3 p = c + new Vector3(Rand(rnd, -1.4f, 1.4f), 0f, Rand(rnd, -1.4f, 1.4f));
                        AddLimb(props, Pick(rnd, Limbs), new Vector3(p.x, FloorAt(p, ceil - 0.1f), p.z), Vector3.up, rnd, true);
                    }
                }
            }

        foreach (Room room in m.rooms)
        {
            Vector3 center = Vector3.zero;
            foreach (var c in room.cells) center += CellCenter(c.x, c.y);
            center /= room.cells.Count;
            float height = room.ceil - room.floor;
            AddLamp(props, new Vector3(center.x, room.ceil, center.z), 9f + (height - 4f) * 1.5f, 1.4f + (height - 4f) * 0.15f, rnd);

            // Intérieur de la salle (marge pour les murs et les escaliers).
            float minX = CellCenter(room.rect.xMin, 0).x - Cell * 0.5f + 1.2f, maxX = CellCenter(room.rect.xMax - 1, 0).x + Cell * 0.5f - 1.2f;
            float minZ = CellCenter(0, room.rect.yMin).z - Cell * 0.5f + 1.2f, maxZ = CellCenter(0, room.rect.yMax - 1).z + Cell * 0.5f - 1.2f;
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
                        AddColumn(props, RandomInside(room.floor), Rand(rnd, 0.35f, 0.6f), height, rnd);
                    break;
                case RoomStyle.Spheres:
                    for (int k = 0; k < 2 + room.cells.Count / 2; k++)
                        AddBigSphere(props, RandomInside(room.floor), Rand(rnd, 0.6f, Mathf.Min(1.8f, height * 0.25f)), room.floor, room.ceil, rnd);
                    break;
                case RoomStyle.Chaotic:
                    AddPlatforms(props, room, minX, maxX, minZ, maxZ, rnd, refs);
                    break;
                default:
                    foreach (var c in room.cells)
                    {
                        Vector3 p = CellCenter(c.x, c.y) + new Vector3(Rand(rnd, -1.3f, 1.3f), room.floor, Rand(rnd, -1.3f, 1.3f));
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
                AddEye(props, CellCenter(c.x, c.y) + new Vector3(Rand(rnd, -1f, 1f), room.ceil, Rand(rnd, -1f, 1f)), Vector3.down, Rand(rnd, 0.8f, 1.5f));
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
        foreach (var (p, dir) in new[] { (a, across), (b, -across) })
        {
            var emitter = GameArt.Prim(PrimitiveType.Cylinder, go.transform, p + dir * 0.06f, new Vector3(0.18f, 0.06f, 0.18f), metal);
            emitter.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            var t = GameArt.Prim(PrimitiveType.Sphere, go.transform, p + dir * 0.13f, Vector3.one * 0.07f, tip);
            t.name = "Embout";
        }

        var beam = GameArt.Prim(PrimitiveType.Cube, go.transform, (a + b) * 0.5f, Vector3.one, GameArt.Unlit("LaserBeam", new Color(1f, 0.05f, 0.05f)));
        beam.name = "Faisceau";
        beam.transform.localRotation = Quaternion.LookRotation(across);
        beam.transform.localScale = new Vector3(0.035f, 0.035f, Vector3.Distance(a, b) - 0.2f);
        var glow = GameArt.Prim(PrimitiveType.Cube, beam.transform, Vector3.zero, new Vector3(2.2f, 2.2f, 1f), GameArt.Unlit("LaserGlow", new Color(0.6f, 0f, 0f)));
        glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<Laser>().Init(a, b, beam);
    }

    // Néon fixé au plafond ('ceilingPos' : point du plafond).
    static void AddLamp(Transform parent, Vector3 ceilingPos, float range, float intensity, System.Random rnd)
    {
        var go = new GameObject("Lampe");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = ceilingPos + Vector3.down * 0.05f;
        var bulb = GameArt.Prim(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.9f, 0.05f, 0.25f), GameArt.Unlit("Neon", new Color(0.95f, 0.95f, 0.9f)));
        var l = AddLight(go.transform, Vector3.down * 0.4f, new Color(0.85f, 0.87f, 0.9f), intensity, range);
        if (rnd.NextDouble() < 0.45) go.AddComponent<FlickerLight>().Init(l, bulb.GetComponent<Renderer>());
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

    // Membre (bras, main, tête, buste) peint en noir uni, figé, sortant d'une surface de normale 'normal'.
    static void AddLimb(Transform parent, string model, Vector3 surfacePoint, Vector3 normal, System.Random rnd, bool solid)
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
        Material black = GameArt.Unlit("Limb", new Color(0.01f, 0.01f, 0.01f));
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int k = 0; k < mats.Length; k++) mats[k] = black;
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

        // Orienté selon la normale, tourné au hasard autour d'elle et un peu penché.
        Quaternion tilt = Quaternion.Euler(Rand(rnd, -20f, 20f), 0f, Rand(rnd, -20f, 20f));
        holder.transform.localRotation = Quaternion.AngleAxis(Rand(rnd, 0f, 360f), normal) * Quaternion.FromToRotation(outAxis, normal) * tilt;
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

    // Grande sphère posée (à moitié enfoncée dans le sol) ou suspendue au plafond.
    static void AddBigSphere(Transform parent, Vector3 pos, float radius, float floor, float ceil, System.Random rnd)
    {
        Material mat = rnd.NextDouble() < 0.5 ? GameArt.Lit("SphereBlack", new Color(0.03f, 0.03f, 0.03f), null, 0.9f)
                                              : GameArt.Lit("SphereWhite", new Color(0.9f, 0.9f, 0.9f), null, 0.9f);
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
    static void AddPlatforms(Transform parent, Room room, float minX, float maxX, float minZ, float maxZ, System.Random rnd, MazeRefs refs)
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
        refs.itemSpots.Add(new Vector3(p.x, top, p.z));
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
