using System.Collections.Generic;
using UnityEngine;

// Chapitre 15.3 (V0.6.14, V0.7) : la Control Room. Dans le Hub de départ, caché derrière l'Écran des Logs, un tunnel circulaire,
// noir et obscur, s'enfonce vers le sud (hors du labyrinthe). La musique s'y efface progressivement et revient dans le Hub.
// À 10 m de l'entrée, une porte métallique de laboratoire, qui ne s'ouvre qu'avec la Carte Magnétique 1 (l'objet est
// consommé). Derrière : la Control Room, salle de 16 x 24 m (16 m de la sortie du tunnel au mur du fond), sans musique,
// murs, plafond et sol noirs, l'enseigne
// "Control Room" au mur (comme la Neutral Room et l'Acquisition Room).
// Tout est construit dans le repère du Hub 1 (son centre à l'origine, le sud vers -Z).
public static class ControlWing
{
    // Tout le tunnel est surélevé : son ouverture reste cachée derrière l'Écran des Logs (de 0,7 m à 4,4 m de haut).
    // On y entre d'un saut (un saut atteint environ 1,08 m).
    public const float Elevation = 0.95f;
    const float TunnelRadius = 1.5f, TunnelAxis = 1f;            // tube de 3 m de diamètre, sol plat à 1 m sous son axe
    static float FloorHalf => Mathf.Sqrt(TunnelRadius * TunnelRadius - TunnelAxis * TunnelAxis);   // 1,12 m
    static float TunnelTop => TunnelAxis + TunnelRadius;          // 2,5 m
    const float WallInner = WorldBuilder.HubRadius, WallOuter = WorldBuilder.HubRadius + 0.3f, HubWallHeight = 5f;
    const float DoorWidth = 1.3f, DoorHeight = 2.15f, DoorThickness = 0.12f, PlateThickness = 0.2f;
    static float DoorZ => -(WallOuter + GameRules.TunnelDoorDistance);
    static float RoomNorth => DoorZ - 1f;
    static float RoomSouth => RoomNorth - GameRules.ControlRoomLength;

    // Facteur de silence de la musique (0 : rien, 1 : silence) selon la position du personnage : 0 dans le Hub, puis de plus
    // en plus fort sur les premiers mètres du tunnel ; 1 au-delà et dans la Control Room.
    public static float MusicSilence(Vector3 world)
    {
        Vector3 d = world - WorldBuilder.HubCenter(1) - Vector3.up * Elevation;
        if (d.z > -WallInner || d.z < RoomSouth - 2f || Mathf.Abs(d.x) > GameRules.ControlRoomWidth * 0.5f + 2f || d.y > 10f || d.y < -5f)
            return 0f;
        return Mathf.Clamp01((-d.z - WallInner) / GameRules.TunnelMusicFade);
    }

    public static void Build(Transform hub)
    {
        var root = new GameObject("ControlRoom").transform;
        root.SetParent(hub, false);
        root.localPosition = Vector3.up * Elevation;
        var colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root, false);
        void Box(Vector3 center, Vector3 size)
        {
            var bc = colliders.gameObject.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        Material tunnel = GameArt.Lit("TunnelBeton", new Color(0.075f, 0.075f, 0.08f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.08f);
        Material black = GameArt.Lit("ControlNoir", new Color(0.018f, 0.018f, 0.02f), null, 0.25f);

        // Bouche du tunnel dans le mur du chapiteau : un pan de toile percé d'un trou rond (le pan du mur est retiré). Même
        // toile que le pan qu'il remplace (rouge, au sud) : depuis le chapiteau, rien ne le distingue des autres.
        // Mêmes dimensions qu'un pan du mur (32 pans, 8 cm de recouvrement), face intérieure alignée sur celle des autres, et la
        // toile étirée sur toute la face, comme sur les autres pans.
        float halfSeg = (2f * Mathf.PI * (WallInner + 0.15f) / 32f + 0.08f) * 0.5f;
        var mouth = new Mesh2();
        mouth.UvOf = p => new Vector2((halfSeg - p.x) / (halfSeg * 2f), (p.y + Elevation) / HubWallHeight);
        mouth.PlateWithHole(new Vector3(0f, 0f, -WallInner), Vector3.right, Vector3.up, -halfSeg, halfSeg, -Elevation, HubWallHeight - Elevation,
            (x, y) => x * x + (y - TunnelAxis) * (y - TunnelAxis) < TunnelRadius * TunnelRadius && y > 0f, 0.025f, 0.3f);
        GameArt.MeshObject("BoucheTunnel", root, mouth.Build("BoucheTunnel"), GameArt.Lit("HubRed", new Color(0.72f, 0.08f, 0.1f), GameArt.Canvas(), 0.85f));
        Box(new Vector3(0f, (TunnelTop + HubWallHeight - Elevation) * 0.5f, -WallInner - 0.15f), new Vector3(halfSeg * 2f, HubWallHeight - Elevation - TunnelTop, 0.3f));
        // Sous l'ouverture, le mur reste plein jusqu'au sol du Hub.
        Box(new Vector3(0f, -Elevation * 0.5f, -WallInner - 0.15f), new Vector3(halfSeg * 2f, Elevation, 0.3f));

        // Tube, sol plat et collisions jusqu'à la Control Room.
        float z0 = -WallInner - 0.02f, z1 = RoomNorth - 0.05f;
        var tube = new Mesh2();
        tube.Tube(TunnelAxis, TunnelRadius, z0, z1, 32);
        GameArt.MeshObject("Tunnel", root, tube.Build("Tunnel"), tunnel);
        var floor = new Mesh2();
        floor.Quad(new Vector3(-FloorHalf, 0.005f, z0), new Vector3(FloorHalf, 0.005f, z0), new Vector3(FloorHalf, 0.005f, z1), new Vector3(-FloorHalf, 0.005f, z1), 0.5f);
        GameArt.MeshObject("SolTunnel", root, floor.Build("SolTunnel"), tunnel);
        float len = z0 - z1, mid = (z0 + z1) * 0.5f;
        // Sol du tunnel : un massif qui descend jusque sous le sol du Hub (sa face avant est le rebord sur lequel on saute).
        Box(new Vector3(0f, -(Elevation + 1f) * 0.5f, mid), new Vector3(FloorHalf * 2f + 1f, Elevation + 1f, len));
        // Caméra à la première personne dans le tube (pas dans la Control Room).
        TunnelZones.Register(root, root.TransformPoint(new Vector3(0f, TunnelAxis, z0)), root.TransformPoint(new Vector3(0f, TunnelAxis, z1)), TunnelRadius + 0.2f);
        Box(new Vector3(0f, TunnelTop + 0.15f, mid), new Vector3(FloorHalf * 2f + 1f, 0.3f, len));
        foreach (float s in new[] { -1f, 1f })
            Box(new Vector3(s * (FloorHalf + 0.15f), TunnelTop * 0.5f, mid), new Vector3(0.3f, TunnelTop, len));

        // Cloison de la porte (disque percé d'une ouverture rectangulaire) et sa porte.
        var plate = new Mesh2();
        plate.PlateWithHole(new Vector3(0f, 0f, DoorZ), Vector3.right, Vector3.up, -TunnelRadius, TunnelRadius, 0f, TunnelTop,
            (x, y) => x * x + (y - TunnelAxis) * (y - TunnelAxis) > TunnelRadius * TunnelRadius || (Mathf.Abs(x) < DoorWidth * 0.5f && y < DoorHeight), 0.025f, PlateThickness);
        GameArt.MeshObject("CloisonPorte", root, plate.Build("CloisonPorte"), GameArt.Metal);
        float side = (FloorHalf * 2f - DoorWidth) * 0.5f;
        foreach (float s in new[] { -1f, 1f })
            Box(new Vector3(s * (DoorWidth * 0.5f + side * 0.5f), TunnelTop * 0.5f, DoorZ), new Vector3(side, TunnelTop, PlateThickness));
        Box(new Vector3(0f, (DoorHeight + TunnelTop) * 0.5f, DoorZ), new Vector3(DoorWidth, TunnelTop - DoorHeight, PlateThickness));
        // Note 06 (V0.7) : encadrement noir au creux de l'ouverture, dans le plan de la porte (milieu de la cloison).
        DoorFrame.Inset(root, new Vector3(0f, 0f, DoorZ - PlateThickness * 0.5f), DoorWidth * 0.5f, DoorHeight, 0.1f, PlateThickness + 0.02f);
        // Au milieu de l'épaisseur de la cloison : ouverte, elle disparaît dedans.
        ControlDoor.Create(root, new Vector3(0f, DoorHeight * 0.5f, DoorZ - PlateThickness * 0.5f), new Vector3(DoorWidth, DoorHeight, DoorThickness));

        // La Control Room : boîte noire de 20 x 30 x 6 m.
        float hw = GameRules.ControlRoomWidth * 0.5f, h = GameRules.ControlRoomHeight, n = RoomNorth, s0 = RoomSouth;
        var room = new Mesh2();
        room.Quad(new Vector3(-hw, 0f, s0), new Vector3(hw, 0f, s0), new Vector3(hw, 0f, n), new Vector3(-hw, 0f, n), 0.25f);   // sol
        room.Quad(new Vector3(-hw, h, n), new Vector3(hw, h, n), new Vector3(hw, h, s0), new Vector3(-hw, h, s0), 0.25f);       // plafond
        room.Quad(new Vector3(-hw, 0f, s0), new Vector3(-hw, h, s0), new Vector3(hw, h, s0), new Vector3(hw, 0f, s0), 0.25f);   // sud
        room.Quad(new Vector3(hw, 0f, s0), new Vector3(hw, h, s0), new Vector3(hw, h, n), new Vector3(hw, 0f, n), 0.25f);       // est
        room.Quad(new Vector3(-hw, 0f, n), new Vector3(-hw, h, n), new Vector3(-hw, h, s0), new Vector3(-hw, 0f, s0), 0.25f);   // ouest
        // Nord : percé là où arrive le tunnel.
        room.PlateWithHole(new Vector3(0f, 0f, n), Vector3.right, Vector3.up, -hw, hw, 0f, h,
            (x, y) => x * x + (y - TunnelAxis) * (y - TunnelAxis) < TunnelRadius * TunnelRadius && y > 0f, 0.04f, 0f, 2.2f, 3f);
        GameArt.MeshObject("ControlRoom", root, room.Build("ControlRoom"), black);
        float cz = (n + s0) * 0.5f, L = n - s0;
        Box(new Vector3(0f, -0.5f, cz), new Vector3(hw * 2f + 1f, 1f, L + 1f));
        Box(new Vector3(0f, h + 0.5f, cz), new Vector3(hw * 2f + 1f, 1f, L + 1f));
        Box(new Vector3(0f, h * 0.5f, s0 - 0.5f), new Vector3(hw * 2f + 1f, h, 1f));
        foreach (float sx in new[] { -1f, 1f })
        {
            Box(new Vector3(sx * (hw + 0.5f), h * 0.5f, cz), new Vector3(1f, h, L + 1f));
            // Mur nord, de part et d'autre du tunnel, et au-dessus.
            float inner = FloorHalf + 0.3f;
            Box(new Vector3(sx * (inner + (hw - inner) * 0.5f), h * 0.5f, n + 0.5f), new Vector3(hw - inner, h, 1f));
        }
        Box(new Vector3(0f, (TunnelTop + h) * 0.5f, n + 0.5f), new Vector3((FloorHalf + 0.3f) * 2f, h - TunnelTop, 1f));

        // Enseigne "Control Room" sur le mur du fond, face à l'entrée (néon Meshy si présent, sinon texte).
        Sign(root, new Vector3(0f, 3.6f, s0 + 0.02f));
    }

    // Enseigne : le mot "Control" (Meshy, Resources/Art/Decor/EnseigneControlRoom) ; à défaut, un texte au néon.
    static void Sign(Transform parent, Vector3 center)
    {
        var shape = Resources.Load<Mesh>("Art/Decor/EnseigneControlRoom");
        // V0.7 : le modèle "Control Room" brille d'une lueur blanche (émission forte et halo lumineux sur le mur).
        Material neon = GameArt.Emissive("NeonBlancVif", Color.white, new Color(1.6f, 1.6f, 1.7f));
        var holder = new GameObject("EnseigneControlRoom").transform;
        holder.SetParent(parent, false);
        holder.localPosition = center;
        // L'enseigne regarde vers le nord (vers l'entrée).
        holder.localRotation = Quaternion.Euler(0f, 180f, 0f);
        if (shape != null)
        {
            float scale = 7f / Mathf.Max(0.01f, shape.bounds.size.x);
            var go = GameArt.MeshObject("Control Room", holder, shape, neon);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(0f, -scale * 0.5f, -shape.bounds.max.z * scale - 0.01f);
        }
        else
        {
            var text = GameArt.CreateText(holder, "Control Room", new Vector3(0f, 0f, -0.03f), 0.38f, new Color(0.9f, 0.92f, 0.95f));
            text.text = "Control Room";
            text.fontStyle = FontStyle.Bold;
        }
        var light = new GameObject("HaloEnseigne").AddComponent<Light>();
        light.transform.SetParent(holder, false);
        light.transform.localPosition = new Vector3(0f, 0f, -1.2f);
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.93f, 1f);
        light.intensity = 2.4f;
        light.range = 11f;
        light.shadows = LightShadows.None;
    }

    // Petit constructeur de maillage (faces visibles des deux côtés : tube, cloisons et salle se voient de partout).
    class Mesh2
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();
        // Coordonnées de texture imposées (sinon : à l'échelle du monde).
        public System.Func<Vector3, Vector2> UvOf;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float texScale)
        {
            Vector3 u = (b - a).normalized, w = (d - a).normalized;
            var q = new[] { a, b, c, d };
            var quv = new Vector2[4];
            for (int k = 0; k < 4; k++) quv[k] = UvOf != null ? UvOf(q[k]) : new Vector2(Vector3.Dot(q[k], u), Vector3.Dot(q[k], w)) * texScale;
            TwoSided(q, quv);
        }

        // Les deux faces ont chacune leurs sommets : une normale par face (sinon elles s'annulent et l'éclairage est faux).
        void TwoSided(Vector3[] q, Vector2[] quv)
        {
            int i = v.Count;
            v.AddRange(q); uv.AddRange(quv);
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            int j = v.Count;
            v.AddRange(q); uv.AddRange(quv);
            t.AddRange(new[] { j, j + 2, j + 1, j, j + 3, j + 2 });
        }

        // Tube horizontal le long de -Z (de z0 à z1), axe à la hauteur 'axis'.
        public void Tube(float axis, float radius, float z0, float z1, int sides)
        {
            float perimeter = 2f * Mathf.PI * radius;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, axis + Mathf.Sin(a0) * radius, 0f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, axis + Mathf.Sin(a1) * radius, 0f);
                float u0 = perimeter * k / sides * 0.5f, u1 = perimeter * (k + 1) / sides * 0.5f;
                TwoSided(new[] { p0 + Vector3.forward * z0, p1 + Vector3.forward * z0, p1 + Vector3.forward * z1, p0 + Vector3.forward * z1 },
                    new[] { new Vector2(u0, z0 * 0.5f), new Vector2(u1, z0 * 0.5f), new Vector2(u1, z1 * 0.5f), new Vector2(u0, z1 * 0.5f) });
            }
        }

        // Plaque dans le plan (origin, right, up) sur [x0, x1] x [y0, y1], percée là où 'hole' est vrai (cellules de 'cell' m).
        // 'thickness' > 0 : une seconde face décalée vers -Z. Hors du rectangle [-fineX, fineX] x [0, fineY] (s'il est donné),
        // la plaque est faite de grands panneaux (la zone fine entoure le trou).
        public void PlateWithHole(Vector3 origin, Vector3 right, Vector3 up, float x0, float x1, float y0, float y1,
            System.Func<float, float, bool> hole, float cell, float thickness, float fineX = 0f, float fineY = 0f)
        {
            Vector3 P(float x, float y, float dz) => origin + right * x + up * y + Vector3.back * dz;
            void Panel(float ax, float bx, float ay, float by)
            {
                if (bx - ax < 1e-4f || by - ay < 1e-4f) return;
                Quad(P(ax, ay, 0f), P(bx, ay, 0f), P(bx, by, 0f), P(ax, by, 0f), 0.25f);
                if (thickness > 0f) Quad(P(ax, ay, thickness), P(bx, ay, thickness), P(bx, by, thickness), P(ax, by, thickness), 0.25f);
            }
            if (fineX > 0f)
            {
                Panel(x0, -fineX, y0, y1);
                Panel(fineX, x1, y0, y1);
                Panel(-fineX, fineX, fineY, y1);
                x0 = -fineX; x1 = fineX; y1 = fineY;
            }
            int nx = Mathf.CeilToInt((x1 - x0) / cell), ny = Mathf.CeilToInt((y1 - y0) / cell);
            for (int j = 0; j < ny; j++)
            {
                // Suites de cellules pleines d'une même rangée : un seul panneau.
                int i = 0;
                while (i < nx)
                {
                    float cy = y0 + (j + 0.5f) * cell;
                    if (hole(x0 + (i + 0.5f) * cell, cy)) { i++; continue; }
                    int start = i;
                    while (i < nx && !hole(x0 + (i + 0.5f) * cell, cy)) i++;
                    Panel(x0 + start * cell, Mathf.Min(x1, x0 + i * cell), y0 + j * cell, Mathf.Min(y1, y0 + (j + 1) * cell));
                }
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}

// Porte métallique de laboratoire secret (bandes de danger jaunes et noires, lecteur de badge). Visée avec la Carte
// Magnétique 1 sélectionnée, elle devient un objet interactif (contour) : un clic l'ouvre et consomme la carte.
public class ControlDoor : Interactable
{
    public static ControlDoor Current { get; private set; }
    public bool Open { get; private set; }

    Transform slab;
    Renderer led;
    Vector3 closedPos, openPos;
    float progress;

    public static ControlDoor Create(Transform parent, Vector3 center, Vector3 size)
    {
        var go = new GameObject("PorteLaboratoire");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        var door = go.AddComponent<ControlDoor>();
        door.slab = new GameObject("Battant").transform;
        door.slab.SetParent(go.transform, false);
        Material steel = GameArt.Lit("PorteLabo", new Color(0.42f, 0.44f, 0.46f), GameArt.Noise("concrete", 128, 0.85f, 0.15f, 5), 0.7f);
        steel.SetFloat("_Metallic", 0.75f);
        Material yellow = GameArt.Lit("DangerJaune", new Color(0.95f, 0.75f, 0.1f), null, 0.4f);
        Material dark = GameArt.Lit("DangerNoir", new Color(0.04f, 0.04f, 0.04f), null, 0.4f);
        Prim(door.slab, Vector3.zero, size, steel);
        // Bandes de danger en bas, des deux côtés.
        for (int k = 0; k < 6; k++)
        {
            float x = -size.x * 0.5f + (k + 0.5f) * size.x / 6f;
            foreach (float s in new[] { -1f, 1f })
            {
                var stripe = Prim(door.slab, new Vector3(x, -size.y * 0.5f + 0.18f, s * (size.z * 0.5f + 0.004f)), new Vector3(size.x / 6f, 0.3f, 0.006f), k % 2 == 0 ? yellow : dark);
                stripe.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            }
        }
        // Hublot sombre en haut, et poignée.
        foreach (float s in new[] { -1f, 1f })
            Prim(door.slab, new Vector3(0f, size.y * 0.25f, s * (size.z * 0.5f + 0.004f)), new Vector3(0.45f, 0.3f, 0.006f), GameArt.WindowDark);
        Prim(door.slab, new Vector3(-size.x * 0.38f, 0f, size.z * 0.5f + 0.03f), new Vector3(0.04f, 0.35f, 0.04f), GameArt.Metal);
        // Lecteur de badge, à droite de l'ouverture (côté Hub), avec son voyant.
        var reader = Prim(go.transform, new Vector3(size.x * 0.5f + 0.2f, 0.15f, size.z * 0.5f + 0.12f), new Vector3(0.14f, 0.22f, 0.06f), dark);
        door.led = Prim(reader.transform.parent, reader.transform.localPosition + new Vector3(0f, 0.07f, 0.035f), new Vector3(0.05f, 0.025f, 0.01f), GameArt.Unlit("VoyantRouge", new Color(1f, 0.1f, 0.08f))).GetComponent<Renderer>();
        door.closedPos = Vector3.zero;
        // Elle coulisse dans la paroi.
        door.openPos = Vector3.right * (size.x + 0.05f);
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        Current = door;
        door.SetOpen(GameSession.ControlRoomOpen, false);
        return door;
    }

    static GameObject Prim(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, mat);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    public void SetOpen(bool open, bool animate)
    {
        if (open && !Open && animate) GameAudio.Play3D("lab_door", transform.position, 1f, 20f, 0.05f);
        Open = open;
        if (!animate) progress = open ? 1f : 0f;
        if (led != null) led.sharedMaterial = open ? GameArt.Unlit("VoyantVert", new Color(0.15f, 1f, 0.3f)) : GameArt.Unlit("VoyantRouge", new Color(1f, 0.1f, 0.08f));
        Apply();
    }

    void Update()
    {
        float target = Open ? 1f : 0f;
        if (Mathf.Approximately(progress, target)) return;
        progress = Mathf.MoveTowards(progress, target, Time.deltaTime / 1.2f);
        Apply();
    }

    void Apply()
    {
        slab.localPosition = Vector3.Lerp(closedPos, openPos, Mathf.SmoothStep(0f, 1f, progress));
        // Ouverte (même à moitié), on passe ; le collider suit le battant.
        var col = GetComponent<BoxCollider>();
        if (col != null)
        {
            col.center = slab.localPosition;
            col.enabled = progress < 0.6f;
        }
    }

    public override bool CanInteract => !Open && GameSession.Instance != null && GameSession.Instance.SelectedItem == ItemType.CarteMagnetique1;
    public override string Prompt => Loc.T("Utiliser la Carte Magnétique 1");
    public override void Interact() => GameSession.Instance.UseControlCard();

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
