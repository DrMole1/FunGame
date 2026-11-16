using System.Collections.Generic;
using UnityEngine;

// Chapitre 15.3 (V0.6.14, V0.7) : la Control Room. Dans le Hub de départ, caché derrière l'Écran des Logs, un tunnel circulaire,
// noir et obscur, s'enfonce vers le sud (hors du labyrinthe). La musique s'y efface progressivement et revient dans le Hub.
// À 10 m de l'entrée, une porte métallique de laboratoire, qui ne s'ouvre qu'avec la Carte Magnétique 1 (l'objet est
// consommé). Derrière : la Control Room, salle de 12 x 18 m (V0.7.8 ; 12 m de la sortie du tunnel au mur du fond), sans musique,
// murs, plafond et sol noirs, l'enseigne
// "Control Room" au mur (comme la Neutral Room et l'Acquisition Room).
// Tout est construit dans le repère du Hub 1 (son centre à l'origine, le sud vers -Z).
// V0.7.2 (Note 07) : le tube est fait de segments de tunnel en béton (modèle Meshy, Resources/Art/TunnelBeton), aux mêmes
// dimensions, toujours aussi sombre ; des pellicules de poussière en tombent. La porte, au fond, reprend le modèle de la porte
// de la Neutral Room (porte blindée).
// V0.7.4 : à gauche en entrant (à l'est), une seconde porte de laboratoire, ouverte par la Carte Magnétique 2, mène au Vestiaire
// (couloir éclairé par 3 néons, penderie à cintres sur sa longueur), qui donne sur l'Ascenseur (vieil ascenseur des années
// 1980, défectueux et saboté) et, en face, sur la porte entrouverte du Bureau L.I.V. (un néon).
// V0.7.5 : sous l'enseigne, le grand écran (ControlScreen), relié au promontoire de la Cloche, et son tableau de bord.
public static class ControlWing
{
    // Tout le tunnel est surélevé : son ouverture reste cachée derrière l'Écran des Logs (de 0,7 m à 4,4 m de haut).
    // On y entre d'un saut (un saut atteint environ 1,08 m).
    public const float Elevation = 0.95f;
    // V0.7.7 (Note 02) : calque du sol du tunnel. Les lampes du chapiteau (sans ombres) l'éclairaient à travers le mur et le
    // teintaient de brun : elles ignorent ce calque, et le sol garde le noir de la Control Room.
    public const int TunnelFloorLayer = 29;
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
        if (d.z > -WallInner || d.z < RoomSouth - 2f || d.x < -GameRules.ControlRoomWidth * 0.5f - 2f || d.x > WingEast + 2f || d.y > 10f || d.y < -5f)
            return 0f;
        return Mathf.Clamp01((-d.z - WallInner) / GameRules.TunnelMusicFade);
    }

    // V0.7.5 : le point (monde) est dans la Control Room.
    public static bool InControlRoom(Vector3 world)
    {
        Vector3 d = world - WorldBuilder.HubCenter(1) - Vector3.up * Elevation;
        return d.z <= RoomNorth && d.z >= RoomSouth && Mathf.Abs(d.x) <= GameRules.ControlRoomWidth * 0.5f
            && d.y >= -1f && d.y <= GameRules.ControlRoomHeight + 1f;
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
        System.Func<float, float, bool> mouthHole = (x, y) => x * x + (y - TunnelAxis) * (y - TunnelAxis) < TunnelRadius * TunnelRadius && y > 0f;
        // V0.7.5 (Note 03) : seule la face côté chapiteau est en toile ; la face arrière (vue depuis le tunnel) est construite
        // plus bas, dans la couleur du tunnel.
        mouth.PlateWithHole(new Vector3(0f, 0f, -WallInner), Vector3.right, Vector3.up, -halfSeg, halfSeg, -Elevation, HubWallHeight - Elevation,
            mouthHole, 0.025f, 0f);
        GameArt.MeshObject("BoucheTunnel", root, mouth.Build("BoucheTunnel"), GameArt.Lit("HubRed", new Color(0.72f, 0.08f, 0.1f), GameArt.Canvas(), 0.85f));
        Box(new Vector3(0f, (TunnelTop + HubWallHeight - Elevation) * 0.5f, -WallInner - 0.15f), new Vector3(halfSeg * 2f, HubWallHeight - Elevation - TunnelTop, 0.3f));
        // Sous l'ouverture, le mur reste plein jusqu'au sol du Hub.
        Box(new Vector3(0f, -Elevation * 0.5f, -WallInner - 0.15f), new Vector3(halfSeg * 2f, Elevation, 0.3f));

        // Tube, sol plat et collisions jusqu'à la Control Room.
        float z0 = -WallInner - 0.02f, z1 = RoomNorth - 0.05f;
        if (!BuildSegments(root, z0, z1))
        {
            var tube = new Mesh2();
            tube.Tube(TunnelAxis, TunnelRadius, z0, z1, 32);
            GameArt.MeshObject("Tunnel", root, tube.Build("Tunnel"), tunnel);
        }
        BuildDust(root, z0, z1);

        // V0.7.5 (Note 03) : jonction entre le tunnel et le chapiteau. La face arrière du pan percé et le bord du trou (sur
        // l'épaisseur du mur) étaient de la toile du chapiteau : deux bandes verticales rouges restaient visibles depuis le
        // tunnel, sur les côtés. Elles sont maintenant dans la couleur du tunnel.
        Material junction = tunnelMat != null ? tunnelMat : tunnel;
        var back = new Mesh2();
        back.PlateWithHole(new Vector3(0f, 0f, -WallInner - 0.3f), Vector3.right, Vector3.up, -halfSeg, halfSeg, -Elevation, HubWallHeight - Elevation,
            mouthHole, 0.025f, 0f);
        GameArt.MeshObject("BoucheTunnelArriere", root, back.Build("BoucheTunnelArriere"), junction);
        var rim = new Mesh2();
        rim.Tube(TunnelAxis, TunnelRadius - 0.01f, -WallInner - 0.005f, -WallInner - 0.31f, 48);
        GameArt.MeshObject("JonctionTunnel", root, rim.Build("JonctionTunnel"), junction);
        // V0.7.7 (Note 01) : les pans voisins du mur du chapiteau (tournés de 11,25°) dépassent dans la bouche du tunnel : leur
        // tranche crème se voyait depuis le tunnel, à droite et à gauche. Deux surfaces dans la couleur du tunnel la recouvrent,
        // sur toute l'épaisseur du mur.
        float neighbourEdge = WallInner * Mathf.Sin(Mathf.PI / 16f) - halfSeg * Mathf.Cos(Mathf.PI / 16f) - 0.008f;
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * neighbourEdge, za = -WallInner + 0.004f, zb = -WallInner - 0.33f;
            var cheek = new Mesh2();
            cheek.Quad(new Vector3(x, -0.05f, za), new Vector3(x, -0.05f, zb), new Vector3(x, TunnelTop + 0.05f, zb), new Vector3(x, TunnelTop + 0.05f, za), 0.5f);
            GameArt.MeshObject(sx < 0f ? "JonctionGauche" : "JonctionDroite", root, cheek.Build("JonctionCote"), junction);
        }
        // V0.7.7 (Note 02) : le sol du tunnel est de la couleur du sol et des murs de la Control Room.
        var floor = new Mesh2();
        floor.Quad(new Vector3(-FloorHalf, 0.005f, z0), new Vector3(FloorHalf, 0.005f, z0), new Vector3(FloorHalf, 0.005f, z1), new Vector3(-FloorHalf, 0.005f, z1), 0.5f);
        var floorGo = GameArt.MeshObject("SolTunnel", root, floor.Build("SolTunnel"), black);
        floorGo.layer = TunnelFloorLayer;
        // Il touche aussi la sonde de reflets du chapiteau, qui lui renvoyait la toile chaude : pas de reflets de sonde (comme
        // le sol de la Control Room, hors de sa portée).
        floorGo.GetComponent<Renderer>().reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
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

        // La Control Room : boîte noire de 18 x 12 x 6 m (V0.7.8 ; avant : 24 x 16 m).
        float hw = GameRules.ControlRoomWidth * 0.5f, h = GameRules.ControlRoomHeight, n = RoomNorth, s0 = RoomSouth;
        var room = new Mesh2();
        room.Quad(new Vector3(-hw, 0f, s0), new Vector3(hw, 0f, s0), new Vector3(hw, 0f, n), new Vector3(-hw, 0f, n), 0.25f);   // sol
        room.Quad(new Vector3(-hw, h, n), new Vector3(hw, h, n), new Vector3(hw, h, s0), new Vector3(-hw, h, s0), 0.25f);       // plafond
        room.Quad(new Vector3(-hw, 0f, s0), new Vector3(-hw, h, s0), new Vector3(hw, h, s0), new Vector3(hw, 0f, s0), 0.25f);   // sud
        // Est : percé de la porte du Vestiaire (V0.7.4).
        room.PlateWithHole(new Vector3(hw, 0f, VestiaireZ), Vector3.forward, Vector3.up, s0 - VestiaireZ, n - VestiaireZ, 0f, h,
            (x, y) => Mathf.Abs(x) < DoorWidth * 0.5f && y < DoorHeight, 0.05f, 0f, 1.2f, 2.5f);
        room.Quad(new Vector3(-hw, 0f, n), new Vector3(-hw, h, n), new Vector3(-hw, h, s0), new Vector3(-hw, 0f, s0), 0.25f);   // ouest
        // Nord : percé là où arrive le tunnel.
        room.PlateWithHole(new Vector3(0f, 0f, n), Vector3.right, Vector3.up, -hw, hw, 0f, h,
            (x, y) => x * x + (y - TunnelAxis) * (y - TunnelAxis) < TunnelRadius * TunnelRadius && y > 0f, 0.04f, 0f, 2.2f, 3f);
        GameArt.MeshObject("ControlRoom", root, room.Build("ControlRoom"), black);
        float cz = (n + s0) * 0.5f, L = n - s0;
        Box(new Vector3(0f, -0.5f, cz), new Vector3(hw * 2f + 1f, 1f, L + 1f));
        Box(new Vector3(0f, h + 0.5f, cz), new Vector3(hw * 2f + 1f, 1f, L + 1f));
        Box(new Vector3(0f, h * 0.5f, s0 - 0.5f), new Vector3(hw * 2f + 1f, h, 1f));
        // Ouest plein ; est percé de la porte du Vestiaire (mur de 0,3 m entre la salle et le Vestiaire).
        Box(new Vector3(-(hw + 0.5f), h * 0.5f, cz), new Vector3(1f, h, L + 1f));
        float doorN = VestiaireZ + DoorWidth * 0.5f, doorS = VestiaireZ - DoorWidth * 0.5f;
        Box(new Vector3(hw + 0.15f, h * 0.5f, (n + 0.5f + doorN) * 0.5f), new Vector3(0.3f, h, n + 0.5f - doorN));
        Box(new Vector3(hw + 0.15f, h * 0.5f, (doorS + s0 - 0.5f) * 0.5f), new Vector3(0.3f, h, doorS - s0 + 0.5f));
        Box(new Vector3(hw + 0.15f, (DoorHeight + h) * 0.5f, VestiaireZ), new Vector3(0.3f, h - DoorHeight, DoorWidth));
        foreach (float sx in new[] { -1f, 1f })
        {
            // Mur nord, de part et d'autre du tunnel, et au-dessus.
            float inner = FloorHalf + 0.3f;
            Box(new Vector3(sx * (inner + (hw - inner) * 0.5f), h * 0.5f, n + 0.5f), new Vector3(hw - inner, h, 1f));
        }
        Box(new Vector3(0f, (TunnelTop + h) * 0.5f, n + 0.5f), new Vector3((FloorHalf + 0.3f) * 2f, h - TunnelTop, 1f));

        // Enseigne "Control Room" sur le mur du fond, face à l'entrée (néon Meshy si présent, sinon texte).
        // V0.7.5 : remontée (avant : 3,6 m) pour laisser place, dessous, au grand écran.
        Sign(root, new Vector3(0f, 4.5f, s0 + 0.02f));
        // V0.7.5 : grand écran, câble, promontoire et tableau de bord.
        ControlScreen.Build(root, s0, hw);

        // V0.7.7 (Note 04) : deux néons muraux, à la même hauteur (1,2 m au-dessus des portes) : au-dessus du passage vers le
        // Tunnel Caché (mur nord) et au-dessus de la porte du Vestiaire (mur est).
        WallNeon(root, new Vector3(0f, DoorHeight + 1.2f, n), Vector3.back);
        WallNeon(root, new Vector3(hw, DoorHeight + 1.2f, VestiaireZ), Vector3.left);
        // V0.7.7 (Note 07) : la Tête-De-Cloche inanimée, assise contre le mur ouest (en face du Vestiaire).
        SeatedTete.Build(root, new Vector3(-hw, 0f, VestiaireZ), Vector3.right, Box);
        // V0.7.7 : un second jeu de casiers, contre le mur ouest, au nord de la Tête-De-Cloche (sous un bloc de ventilation).
        // V0.7.8 : salle réduite à 12 x 18 m : casiers entre le moteur-pompe et la Tête-De-Cloche.
        Lockers.Build(root, new Vector3(-hw + Lockers.Depth + 0.02f, 0f, ControlDecor.WestLockersZ(n)), Vector3.right, Box, 1);
        // V0.7.7 (Note 09) : tuyauteries, vannes, conduits d'aération et moteurs sur les murs.
        ControlDecor.Build(root, hw, n, s0, h, VestiaireZ, TunnelRadius, Box);

        BuildVestiaire(root, Box);
    }

    // Néon mural : réglette horizontale plaquée au mur, son tube et sa lumière, tournée vers 'inward'.
    static void WallNeon(Transform parent, Vector3 wallPoint, Vector3 inward)
    {
        var holder = new GameObject("NeonMural").transform;
        holder.SetParent(parent, false);
        holder.localPosition = wallPoint;
        holder.localRotation = Quaternion.LookRotation(inward);
        Material housing = GameArt.Lit("RegletteNeon", new Color(0.75f, 0.76f, 0.74f), null, 0.4f);
        Material tube = GameArt.Emissive("TubeNeon", Color.white, new Color(1.5f, 1.6f, 1.65f));
        Deco(holder, new Vector3(0f, 0f, 0.035f), new Vector3(1.3f, 0.12f, 0.07f), housing);
        var t = Deco(holder, new Vector3(0f, -0.015f, 0.09f), new Vector3(0.045f, 0.6f, 0.045f), tube, PrimitiveType.Cylinder);
        t.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var light = new GameObject("Neon").AddComponent<Light>();
        light.transform.SetParent(holder, false);
        light.transform.localPosition = new Vector3(0f, -0.15f, 0.45f);
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.96f, 1f);
        light.range = 7f;
        light.intensity = 1.4f;
        light.shadows = LightShadows.None;
    }

    // ---------- Le Vestiaire, l'Ascenseur et le Bureau L.I.V. (V0.7.4, chapitre 15.3) ----------
    // Vu depuis le Hub de départ (schéma du GDD) : le Vestiaire part vers la gauche de la Control Room (vers l'est), l'Ascenseur
    // s'ouvre sur son flanc nord (côté Hub), le Bureau L.I.V. est au bout, en face de la porte du Vestiaire.
    const float VestLength = 12f, VestWidth = 3.6f, VestHeight = 3f;
    const float LiftOpening = 1.2f, LiftWidth = 2f, LiftDepth = 2f, LiftHeight = 2.4f, LiftDoorHeight = 2.2f;
    const float OfficeLength = 6f, OfficeWidth = 6f, OfficeDoorWidth = 1f, OfficeDoorHeight = 2.15f;
    static float VestiaireZ => (RoomNorth + RoomSouth) * 0.5f - 2f;
    static float VestWest => GameRules.ControlRoomWidth * 0.5f + 0.3f;
    static float VestEast => VestWest + VestLength;
    static float WingEast => VestEast + 0.3f + OfficeLength;
    static float LiftX => VestEast - 3f;

    static void BuildVestiaire(Transform root, System.Action<Vector3, Vector3> box)
    {
        var wing = new GameObject("Vestiaire").transform;
        wing.SetParent(root, false);
        Material wallMat = GameArt.Lit("VestiaireMur", new Color(0.4f, 0.46f, 0.42f), GameArt.Noise("concrete", 128, 0.8f, 0.25f, 23), 0.15f);
        Material floorMat = GameArt.Lit("VestiaireSol", new Color(0.3f, 0.3f, 0.29f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f);
        Material ceilMat = GameArt.Lit("VestiairePlafond", new Color(0.33f, 0.33f, 0.32f), GameArt.Noise("concrete", 128, 0.8f, 0.2f, 7), 0.05f);
        Material officeMat = GameArt.Lit("BureauMur", new Color(0.46f, 0.41f, 0.33f), GameArt.Noise("concrete", 128, 0.85f, 0.2f, 31), 0.12f);
        Material woodPanel = GameArt.Lit("AscenseurBois", new Color(0.36f, 0.22f, 0.13f), GameArt.Noise("wood", 128, 0.7f, 0.25f, 3), 0.35f);
        float z = VestiaireZ, hw = VestWidth * 0.5f, x0 = VestWest, x1 = VestEast, h = VestHeight;
        float zn = z + hw, zs = z - hw;

        // Porte venant de la Control Room : même modèle que celle du tunnel, au milieu du mur de 0,3 m (Carte Magnétique 2).
        var entry = new GameObject("PorteVestiaire").transform;
        entry.SetParent(wing, false);
        entry.localPosition = new Vector3(x0 - 0.15f, 0f, z);
        entry.localRotation = Quaternion.Euler(0f, -90f, 0f);
        DoorFrame.Inset(entry, Vector3.zero, DoorWidth * 0.5f, DoorHeight, 0.1f, 0.32f);
        ControlDoor.Create(entry, new Vector3(0f, DoorHeight * 0.5f, 0f), new Vector3(DoorWidth, DoorHeight, DoorThickness), 2);

        // ----- Couloir -----
        var floor = new Mesh2();
        floor.Quad(new Vector3(x0, 0f, zs), new Vector3(x1, 0f, zs), new Vector3(x1, 0f, zn), new Vector3(x0, 0f, zn), 0.5f);
        var ceil = new Mesh2();
        ceil.Quad(new Vector3(x0, h, zn), new Vector3(x1, h, zn), new Vector3(x1, h, zs), new Vector3(x0, h, zs), 0.5f);
        var walls = new Mesh2();
        walls.PlateWithHole(new Vector3(x0, 0f, z), Vector3.forward, Vector3.up, -hw, hw, 0f, h,
            (x, y) => Mathf.Abs(x) < DoorWidth * 0.5f && y < DoorHeight, 0.05f, 0f, 1.2f, 2.5f);
        walls.Quad(new Vector3(x0, 0f, zs), new Vector3(x1, 0f, zs), new Vector3(x1, h, zs), new Vector3(x0, h, zs), 0.5f);
        walls.PlateWithHole(new Vector3(LiftX, 0f, zn), Vector3.right, Vector3.up, x0 - LiftX, x1 - LiftX, 0f, h,
            (x, y) => Mathf.Abs(x) < LiftOpening * 0.5f && y < LiftDoorHeight, 0.05f, 0f, 1.4f, 2.5f);
        walls.PlateWithHole(new Vector3(x1, 0f, z), Vector3.forward, Vector3.up, -hw, hw, 0f, h,
            (x, y) => Mathf.Abs(x) < OfficeDoorWidth * 0.5f && y < OfficeDoorHeight, 0.05f, 0f, 1f, 2.5f);
        GameArt.MeshObject("SolVestiaire", wing, floor.Build("SolVestiaire"), floorMat);
        GameArt.MeshObject("PlafondVestiaire", wing, ceil.Build("PlafondVestiaire"), ceilMat);
        GameArt.MeshObject("MursVestiaire", wing, walls.Build("MursVestiaire"), wallMat);
        float cx = (x0 + x1) * 0.5f;
        box(new Vector3(cx, -0.5f, z), new Vector3(VestLength + 0.6f, 1f, VestWidth + 0.6f));
        box(new Vector3(cx, h + 0.5f, z), new Vector3(VestLength + 0.6f, 1f, VestWidth + 0.6f));
        box(new Vector3(cx, h * 0.5f, zs - 0.15f), new Vector3(VestLength, h, 0.3f));
        // Nord, autour de l'Ascenseur ; est, autour de la porte du Bureau.
        float la = LiftX - LiftOpening * 0.5f, lb = LiftX + LiftOpening * 0.5f;
        box(new Vector3((x0 + la) * 0.5f, h * 0.5f, zn + 0.15f), new Vector3(la - x0, h, 0.3f));
        box(new Vector3((lb + x1) * 0.5f, h * 0.5f, zn + 0.15f), new Vector3(x1 - lb, h, 0.3f));
        box(new Vector3(LiftX, (LiftDoorHeight + h) * 0.5f, zn + 0.15f), new Vector3(LiftOpening, h - LiftDoorHeight, 0.3f));
        float da = z - OfficeDoorWidth * 0.5f, db = z + OfficeDoorWidth * 0.5f;
        box(new Vector3(x1 + 0.15f, h * 0.5f, (zs + da) * 0.5f), new Vector3(0.3f, h, da - zs));
        box(new Vector3(x1 + 0.15f, h * 0.5f, (db + zn) * 0.5f), new Vector3(0.3f, h, zn - db));
        box(new Vector3(x1 + 0.15f, (OfficeDoorHeight + h) * 0.5f, z), new Vector3(0.3f, h - OfficeDoorHeight, OfficeDoorWidth));

        // Trois néons au plafond, sur la longueur.
        for (int k = 0; k < 3; k++) Neon(wing, new Vector3(x0 + VestLength * (0.2f + 0.3f * k), h, z), 6.5f, 1.5f);

        BuildWardrobe(wing, x0 + 0.8f, x1 - 0.8f, zs + 0.38f);
        // V0.7.7 (Note 08) : casiers en métal contre le mur nord, entre la porte et l'Ascenseur.
        Lockers.Build(wing, new Vector3(x0 + 2.4f, 0f, zn - Lockers.Depth - 0.02f), Vector3.back, box);
        BuildLift(wing, box, zn, woodPanel);
        BuildOffice(wing, box, officeMat, floorMat, ceilMat);
    }

    // Tube de néon blanc froid sous sa réglette, et sa lumière.
    static void Neon(Transform parent, Vector3 ceilingPoint, float range, float intensity)
    {
        Material housing = GameArt.Lit("RegletteNeon", new Color(0.75f, 0.76f, 0.74f), null, 0.4f);
        Material tube = GameArt.Emissive("TubeNeon", Color.white, new Color(1.5f, 1.6f, 1.65f));
        Deco(parent, ceilingPoint + Vector3.down * 0.04f, new Vector3(1.3f, 0.06f, 0.18f), housing);
        var t = Deco(parent, ceilingPoint + Vector3.down * 0.1f, new Vector3(0.045f, 0.6f, 0.045f), tube, PrimitiveType.Cylinder);
        t.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        var light = new GameObject("Neon").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = ceilingPoint + Vector3.down * 0.3f;
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.96f, 1f);
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // Penderie sur la longueur du couloir : une tringle, ses supports, des cintres (quelques-uns de travers, d'autres manquent)
    // et trois blouses de laboratoire abandonnées.
    static void BuildWardrobe(Transform parent, float xa, float xb, float zRail)
    {
        var holder = new GameObject("Penderie").transform;
        holder.SetParent(parent, false);
        Material chrome = GameArt.Lit("Tringle", new Color(0.62f, 0.63f, 0.64f), null, 0.85f);
        Material wire = GameArt.Lit("Cintre", new Color(0.5f, 0.5f, 0.52f), null, 0.7f);
        Material coat = GameArt.Lit("Blouse", new Color(0.82f, 0.82f, 0.78f), GameArt.Noise("concrete", 128, 0.9f, 0.1f, 41), 0.1f);
        const float railY = 1.8f;
        var rail = Deco(holder, new Vector3((xa + xb) * 0.5f, railY, zRail), new Vector3(0.03f, (xb - xa) * 0.5f, 0.03f), chrome, PrimitiveType.Cylinder);
        rail.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        for (float x = xa; x <= xb + 0.01f; x += (xb - xa) / 4f)
            Deco(holder, new Vector3(x, railY + 0.02f, zRail - 0.19f), new Vector3(0.03f, 0.03f, 0.4f), chrome);
        var rnd = new System.Random(1980);
        int index = 0;
        for (float x = xa + 0.25f; x < xb - 0.15f; x += 0.24f, index++)
        {
            if (rnd.NextDouble() < 0.18) continue;
            var hanger = new GameObject("Cintre").transform;
            hanger.SetParent(holder, false);
            hanger.localPosition = new Vector3(x, railY, zRail);
            hanger.localRotation = Quaternion.Euler((float)(rnd.NextDouble() * 10 - 5), 0f, (float)(rnd.NextDouble() * 14 - 7));
            // Crochet sur la tringle, épaules en triangle, barre du bas.
            Deco(hanger, new Vector3(0f, -0.04f, 0f), new Vector3(0.008f, 0.08f, 0.008f), wire);
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = Deco(hanger, new Vector3(0f, -0.13f, s * 0.1f), new Vector3(0.008f, 0.009f, 0.22f), wire);
                arm.transform.localRotation = Quaternion.Euler(s * 22f, 0f, 0f);
            }
            Deco(hanger, new Vector3(0f, -0.17f, 0f), new Vector3(0.008f, 0.008f, 0.4f), wire);
            if (index % 9 == 4)
            {
                var c = Deco(hanger, new Vector3(0f, -0.62f, 0f), new Vector3(0.07f, 0.92f, 0.46f), coat);
                c.transform.localRotation = Quaternion.Euler(0f, (float)(rnd.NextDouble() * 16 - 8), 0f);
            }
        }
    }

    // L'Ascenseur : vieux (années 1980), défectueux et saboté. Les portes palières sont bloquées : l'une est rentrée, l'autre
    // est restée à mi-course, de travers. Cabine lambrissée, panneau de commande arraché aux fils coupés, plafonnier qui grésille.
    static void BuildLift(Transform parent, System.Action<Vector3, Vector3> box, float zn, Material wood)
    {
        var lift = new GameObject("Ascenseur").transform;
        lift.SetParent(parent, false);
        Material steel = GameArt.Lit("AscenseurAcier", new Color(0.55f, 0.52f, 0.46f), GameArt.Noise("concrete", 128, 0.85f, 0.2f, 5), 0.6f);
        Material brass = GameArt.Lit("AscenseurLaiton", new Color(0.7f, 0.55f, 0.28f), null, 0.75f);
        Material dark = GameArt.Lit("AscenseurNoir", new Color(0.03f, 0.03f, 0.03f), null, 0.3f);
        float x = LiftX, w = LiftWidth * 0.5f, z0 = zn, z1 = zn + LiftDepth, h = LiftHeight;

        var cabin = new Mesh2();
        cabin.Quad(new Vector3(x - w, 0f, z0), new Vector3(x + w, 0f, z0), new Vector3(x + w, 0f, z1), new Vector3(x - w, 0f, z1), 0.5f);
        cabin.Quad(new Vector3(x - w, h, z1), new Vector3(x + w, h, z1), new Vector3(x + w, h, z0), new Vector3(x - w, h, z0), 0.5f);
        cabin.Quad(new Vector3(x - w, 0f, z1), new Vector3(x + w, 0f, z1), new Vector3(x + w, h, z1), new Vector3(x - w, h, z1), 0.5f);
        cabin.Quad(new Vector3(x - w, 0f, z0), new Vector3(x - w, 0f, z1), new Vector3(x - w, h, z1), new Vector3(x - w, h, z0), 0.5f);
        cabin.Quad(new Vector3(x + w, 0f, z1), new Vector3(x + w, 0f, z0), new Vector3(x + w, h, z0), new Vector3(x + w, h, z1), 0.5f);
        GameArt.MeshObject("Cabine", lift, cabin.Build("Cabine"), wood);
        // Collisions de la cabine.
        box(new Vector3(x, -0.5f, (z0 + z1) * 0.5f), new Vector3(LiftWidth + 0.6f, 1f, LiftDepth));
        box(new Vector3(x, h + 0.5f, (z0 + z1) * 0.5f), new Vector3(LiftWidth + 0.6f, 1f, LiftDepth));
        box(new Vector3(x, h * 0.5f, z1 + 0.15f), new Vector3(LiftWidth + 0.6f, h, 0.3f));
        foreach (float s in new[] { -1f, 1f }) box(new Vector3(x + s * (w + 0.15f), h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.3f, h, LiftDepth));

        // Encadrement de laiton côté couloir, et indicateur d'étage mort au-dessus.
        float o = LiftOpening * 0.5f;
        foreach (float s in new[] { -1f, 1f })
            Deco(lift, new Vector3(x + s * (o + 0.04f), LiftDoorHeight * 0.5f, z0 - 0.02f), new Vector3(0.08f, LiftDoorHeight, 0.04f), brass);
        Deco(lift, new Vector3(x, LiftDoorHeight + 0.04f, z0 - 0.02f), new Vector3(LiftOpening + 0.16f, 0.08f, 0.04f), brass);
        var dial = Deco(lift, new Vector3(x, LiftDoorHeight + 0.3f, z0 - 0.025f), new Vector3(0.36f, 0.02f, 0.36f), brass, PrimitiveType.Cylinder);
        dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var face = Deco(lift, new Vector3(x, LiftDoorHeight + 0.3f, z0 - 0.04f), new Vector3(0.3f, 0.01f, 0.3f), GameArt.Lit("CadranCreme", new Color(0.78f, 0.72f, 0.58f), null, 0.3f), PrimitiveType.Cylinder);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var needle = Deco(lift, new Vector3(x - 0.04f, LiftDoorHeight + 0.27f, z0 - 0.05f), new Vector3(0.012f, 0.12f, 0.005f), dark);
        needle.transform.localRotation = Quaternion.Euler(0f, 0f, 55f);

        // Portes palières : la gauche rentrée, la droite bloquée à mi-course et de travers (passage d'environ 0,9 m).
        float doorW = LiftOpening * 0.5f;
        Deco(lift, new Vector3(x - o - doorW * 0.5f + 0.05f, LiftDoorHeight * 0.5f, z0 + 0.06f), new Vector3(doorW, LiftDoorHeight - 0.02f, 0.04f), steel);
        var stuck = Deco(lift, new Vector3(x + o - doorW * 0.5f + 0.3f, LiftDoorHeight * 0.5f - 0.03f, z0 + 0.07f), new Vector3(doorW, LiftDoorHeight - 0.06f, 0.04f), steel);
        stuck.transform.localRotation = Quaternion.Euler(0f, 6f, -3f);
        box(stuck.transform.localPosition, new Vector3(doorW, LiftDoorHeight - 0.06f, 0.06f));

        // Panneau de commande, capot arraché pendant au bout de ses fils, fils coupés.
        float px = x + w - 0.02f;
        Deco(lift, new Vector3(px, 1.2f, z0 + 0.45f), new Vector3(0.03f, 0.5f, 0.24f), brass);
        Deco(lift, new Vector3(px - 0.01f, 1.2f, z0 + 0.45f), new Vector3(0.03f, 0.42f, 0.17f), dark);
        var cover = Deco(lift, new Vector3(px - 0.12f, 0.78f, z0 + 0.5f), new Vector3(0.02f, 0.46f, 0.22f), brass);
        cover.transform.localRotation = Quaternion.Euler(18f, 0f, -24f);
        Material[] wires = { GameArt.Lit("FilRouge", new Color(0.6f, 0.05f, 0.05f), null, 0.3f), GameArt.Lit("FilJaune", new Color(0.75f, 0.6f, 0.05f), null, 0.3f), dark };
        for (int k = 0; k < 6; k++)
        {
            var wire = Deco(lift, new Vector3(px - 0.04f, 1.05f - k * 0.015f, z0 + 0.38f + k * 0.03f), new Vector3(0.008f, 0.12f + 0.03f * (k % 3), 0.008f), wires[k % 3], PrimitiveType.Cylinder);
            wire.transform.localRotation = Quaternion.Euler(k * 9f - 20f, 0f, 10f + k * 6f);
        }
        for (int k = 0; k < 8; k++)
        {
            var button = Deco(lift, new Vector3(px - 0.03f, 1.36f - (k / 2) * 0.08f, z0 + 0.41f + (k % 2) * 0.08f), new Vector3(0.035f, 0.01f, 0.035f), k == 5 ? dark : GameArt.Lit("BoutonIvoire", new Color(0.85f, 0.8f, 0.68f), null, 0.4f), PrimitiveType.Cylinder);
            button.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        // Plafonnier qui grésille (lumière jaunie, intermittente).
        Deco(lift, new Vector3(x, h - 0.03f, (z0 + z1) * 0.5f), new Vector3(0.5f, 0.04f, 0.5f), GameArt.Emissive("PlafonnierAscenseur", new Color(0.9f, 0.8f, 0.55f), new Color(0.5f, 0.42f, 0.25f)));
        var bulb = new GameObject("Plafonnier").AddComponent<Light>();
        bulb.transform.SetParent(lift, false);
        bulb.transform.localPosition = new Vector3(x, h - 0.25f, (z0 + z1) * 0.5f);
        bulb.type = LightType.Point;
        bulb.color = new Color(1f, 0.82f, 0.55f);
        bulb.range = 3.5f;
        bulb.intensity = 0.9f;
        bulb.shadows = LightShadows.None;
        bulb.gameObject.AddComponent<FlickeringLight>();
    }

    // Bureau L.I.V. : au bout du Vestiaire, derrière une porte entrouverte marquée "L.I.V." (lettres blanches lumineuses, en
    // relief), qu'un joueur peut pousser. Un seul néon, un vieux bureau, une chaise, un classeur, des papiers épars.
    static void BuildOffice(Transform parent, System.Action<Vector3, Vector3> box, Material wallMat, Material floorMat, Material ceilMat)
    {
        var office = new GameObject("BureauLIV").transform;
        office.SetParent(parent, false);
        float x0 = VestEast + 0.3f, x1 = WingEast, z = VestiaireZ, hw = OfficeWidth * 0.5f, h = VestHeight;
        float zs = z - hw, zn = z + hw, cx = (x0 + x1) * 0.5f;
        var floor = new Mesh2();
        floor.Quad(new Vector3(x0, 0f, zs), new Vector3(x1, 0f, zs), new Vector3(x1, 0f, zn), new Vector3(x0, 0f, zn), 0.5f);
        var ceil = new Mesh2();
        ceil.Quad(new Vector3(x0, h, zn), new Vector3(x1, h, zn), new Vector3(x1, h, zs), new Vector3(x0, h, zs), 0.5f);
        var walls = new Mesh2();
        walls.PlateWithHole(new Vector3(x0, 0f, z), Vector3.forward, Vector3.up, -hw, hw, 0f, h,
            (x, y) => Mathf.Abs(x) < OfficeDoorWidth * 0.5f && y < OfficeDoorHeight, 0.05f, 0f, 1f, 2.5f);
        walls.Quad(new Vector3(x1, 0f, zs), new Vector3(x1, h, zs), new Vector3(x1, h, zn), new Vector3(x1, 0f, zn), 0.5f);
        walls.Quad(new Vector3(x0, 0f, zs), new Vector3(x1, 0f, zs), new Vector3(x1, h, zs), new Vector3(x0, h, zs), 0.5f);
        walls.Quad(new Vector3(x0, 0f, zn), new Vector3(x0, h, zn), new Vector3(x1, h, zn), new Vector3(x1, 0f, zn), 0.5f);
        GameArt.MeshObject("SolBureau", office, floor.Build("SolBureau"), floorMat);
        GameArt.MeshObject("PlafondBureau", office, ceil.Build("PlafondBureau"), ceilMat);
        GameArt.MeshObject("MursBureau", office, walls.Build("MursBureau"), wallMat);
        box(new Vector3(cx, -0.5f, z), new Vector3(OfficeLength + 0.6f, 1f, OfficeWidth + 0.6f));
        box(new Vector3(cx, h + 0.5f, z), new Vector3(OfficeLength + 0.6f, 1f, OfficeWidth + 0.6f));
        box(new Vector3(x1 + 0.15f, h * 0.5f, z), new Vector3(0.3f, h, OfficeWidth));
        box(new Vector3(cx, h * 0.5f, zs - 0.15f), new Vector3(OfficeLength, h, 0.3f));
        box(new Vector3(cx, h * 0.5f, zn + 0.15f), new Vector3(OfficeLength, h, 0.3f));
        Neon(office, new Vector3(cx, h, z), 7f, 1.3f);

        // Encadrement noir dans l'épaisseur du mur, et la porte entrouverte (charnière côté nord, elle s'ouvre vers le Bureau).
        var frame = new GameObject("PorteBureau").transform;
        frame.SetParent(office, false);
        frame.localPosition = new Vector3(VestEast + 0.15f, 0f, z);
        frame.localRotation = Quaternion.Euler(0f, -90f, 0f);
        DoorFrame.Inset(frame, Vector3.zero, OfficeDoorWidth * 0.5f, OfficeDoorHeight, 0.06f, 0.32f);
        LivDoor.Create(office, new Vector3(x0 + 0.03f, 0f, z + OfficeDoorWidth * 0.5f - 0.04f), OfficeDoorWidth - 0.1f, OfficeDoorHeight - 0.04f);

        // Mobilier.
        Material wood = GameArt.Lit("BureauBois", new Color(0.32f, 0.2f, 0.11f), GameArt.Noise("wood", 128, 0.7f, 0.25f, 3), 0.3f);
        Material metal = GameArt.Lit("BureauMetal", new Color(0.36f, 0.39f, 0.38f), null, 0.5f);
        Material paper = GameArt.Lit("BureauPapier", new Color(0.86f, 0.84f, 0.76f), null, 0.1f);
        float dx = x1 - 1.4f;
        Deco(office, new Vector3(dx, 0.76f, z), new Vector3(0.85f, 0.05f, 1.7f), wood);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Deco(office, new Vector3(dx + sx * 0.37f, 0.37f, z + sz * 0.78f), new Vector3(0.05f, 0.74f, 0.05f), wood);
        Deco(office, new Vector3(dx + 0.05f, 0.5f, z - 0.55f), new Vector3(0.7f, 0.45f, 0.5f), wood);
        box(new Vector3(dx, 0.4f, z), new Vector3(0.85f, 0.8f, 1.7f));
        // Chaise, reculée et tournée de travers.
        var chair = new GameObject("Chaise").transform;
        chair.SetParent(office, false);
        chair.localPosition = new Vector3(dx - 0.85f, 0f, z + 0.2f);
        chair.localRotation = Quaternion.Euler(0f, 70f, 0f);
        Deco(chair, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.05f, 0.45f), wood);
        Deco(chair, new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.55f, 0.04f), wood);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Deco(chair, new Vector3(sx * 0.19f, 0.22f, sz * 0.19f), new Vector3(0.04f, 0.45f, 0.04f), metal);
        // Classeur métallique contre le mur nord, un tiroir entrouvert.
        float fx = x0 + 1.4f;
        Deco(office, new Vector3(fx, 0.66f, zn - 0.32f), new Vector3(0.5f, 1.32f, 0.6f), metal);
        Deco(office, new Vector3(fx, 0.95f, zn - 0.75f), new Vector3(0.44f, 0.26f, 0.3f), metal);
        box(new Vector3(fx, 0.66f, zn - 0.4f), new Vector3(0.5f, 1.32f, 0.8f));
        // Papiers sur le bureau et au sol.
        var rnd = new System.Random(1989);
        for (int k = 0; k < 9; k++)
        {
            bool onDesk = k < 4;
            Vector3 at = onDesk ? new Vector3(dx + (float)(rnd.NextDouble() - 0.5) * 0.5f, 0.79f, z + (float)(rnd.NextDouble() - 0.5) * 1.2f)
                                : new Vector3(x0 + 0.8f + (float)rnd.NextDouble() * (OfficeLength - 2.5f), 0.004f, z + (float)(rnd.NextDouble() - 0.5) * (OfficeWidth - 1.5f));
            var sheet = Deco(office, at, new Vector3(0.21f, 0.002f, 0.297f), paper);
            sheet.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        }
    }

    // Élément de décor sans collision ni ombre portée.
    static GameObject Deco(Transform parent, Vector3 pos, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameArt.Prim(type, parent, pos, size, mat);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }


    // Segments Meshy (anneau de béton, axe Z, rayon intérieur ~0,77 m pour 1,4 m de long) mis à l'échelle du tube (rayon
    // intérieur 1,5 m) et alignés bout à bout de z0 à z1. Assombris : le tunnel reste noir et obscur.
    const float SegmentInnerRadius = 0.77f, SegmentLength = 1.4f;

    // Matériau des segments de béton (pour la jonction avec le chapiteau, V0.7.5).
    static Material tunnelMat;

    static bool BuildSegments(Transform root, float z0, float z1)
    {
        var prefab = Resources.Load<GameObject>("Art/TunnelBeton");
        if (prefab == null) return false;
        float scale = TunnelRadius / SegmentInnerRadius;
        float length = z0 - z1;
        int count = Mathf.Max(1, Mathf.RoundToInt(length / (SegmentLength * scale)));
        float stretch = length / (count * SegmentLength * scale);
        var holder = new GameObject("Tunnel").transform;
        holder.SetParent(root, false);
        Material dark = null;
        tunnelMat = null;
        for (int k = 0; k < count; k++)
        {
            var seg = Object.Instantiate(prefab, holder);
            seg.name = "Segment" + (k + 1);
            foreach (var c in seg.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            seg.transform.localRotation = prefab.transform.localRotation * Quaternion.Euler(0f, 0f, k * 37f);
            seg.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(scale, scale, scale * stretch));
            seg.transform.localPosition = new Vector3(0f, TunnelAxis, z0 - (k + 0.5f) * length / count);
            foreach (var r in seg.GetComponentsInChildren<Renderer>())
            {
                if (dark == null)
                {
                    dark = new Material(r.sharedMaterial) { name = "TunnelBetonSombre", color = new Color(0.22f, 0.22f, 0.24f) };
                    if (dark.HasProperty("_Glossiness")) dark.SetFloat("_Glossiness", 0.08f);
                }
                r.sharedMaterial = dark;
                tunnelMat = dark;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        return true;
    }

    // Pellicules de poussière qui tombent lentement du haut du tube, sur toute sa longueur.
    static void BuildDust(Transform root, float z0, float z1)
    {
        var go = new GameObject("Poussiere");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, TunnelTop - 0.25f, (z0 + z1) * 0.5f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.022f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0.39f, 0.37f, 0.35f), new Color(0.58f, 0.56f, 0.53f, 0.5f));
        main.gravityModifier = 0.012f;
        main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 26f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(FloorHalf * 1.6f, 0.2f, Mathf.Abs(z0 - z1));
        // Elles flottent et virevoltent un peu en tombant.
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.4f;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GameArt.Spark;
        ps.Play();
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
            // V0.7.8 : salle réduite, la caméra à la troisième personne venait se coller au mur sud, dans les lettres : la
            // boîte de l'enseigne l'arrête devant (hors de portée des joueurs, à 4,5 m de haut).
            go.AddComponent<BoxCollider>();
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
// V0.7.4 : porte 1 (tunnel, Carte Magnétique 1) ou porte 2 (Vestiaire, Carte Magnétique 2).
public class ControlDoor : Interactable
{
    static readonly ControlDoor[] doors = new ControlDoor[3];
    public static ControlDoor Get(int index) => index >= 1 && index < doors.Length ? doors[index] : null;
    public int Index { get; private set; } = 1;
    public bool Open { get; private set; }
    ItemType Card => Index == 2 ? ItemType.CarteMagnetique2 : ItemType.CarteMagnetique1;

    Transform slab;
    Renderer led;
    Vector3 closedPos, openPos;
    float progress;

    public static ControlDoor Create(Transform parent, Vector3 center, Vector3 size, int index = 1)
    {
        var go = new GameObject("PorteLaboratoire");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        var door = go.AddComponent<ControlDoor>();
        door.Index = index;
        door.slab = new GameObject("Battant").transform;
        door.slab.SetParent(go.transform, false);
        Material steel = GameArt.Lit("PorteLabo", new Color(0.42f, 0.44f, 0.46f), GameArt.Noise("concrete", 128, 0.85f, 0.15f, 5), 0.7f);
        steel.SetFloat("_Metallic", 0.75f);
        Material yellow = GameArt.Lit("DangerJaune", new Color(0.95f, 0.75f, 0.1f), null, 0.4f);
        Material dark = GameArt.Lit("DangerNoir", new Color(0.04f, 0.04f, 0.04f), null, 0.4f);
        // V0.7.2 : même modèle que la porte de la Neutral Room (porte blindée Meshy), ajusté à l'ouverture ; à défaut, une
        // porte de laboratoire en primitives.
        var blinded = GameArt.SpawnModel("PorteBlindee", door.slab, size.y);
        if (blinded != null)
        {
            Bounds b = GameArt.LocalBounds(blinded.transform);
            blinded.transform.localScale = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), 1f, size.z * 1.6f / Mathf.Max(0.01f, b.size.z));
            b = GameArt.LocalBounds(blinded.transform);
            blinded.transform.localPosition = new Vector3(-b.center.x, -size.y * 0.5f - b.min.y, -b.center.z);
            foreach (var r in blinded.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
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
        }
        // Lecteur de badge, à droite de l'ouverture (côté Hub), avec son voyant.
        // V0.7.7 (Note 06) : boîtier en métal plus réaliste (Meshy, Resources/Art/Decor/LecteurCarte ; à défaut en primitives :
        // boîtier vissé, fente, plaque) ; son voyant (une led) luit faiblement en rouge, puis en vert, de la même intensité,
        // quand la porte s'ouvre.
        door.BuildReader(go.transform, new Vector3(size.x * 0.5f + 0.2f, 0.15f, size.z * 0.5f + 0.1f), dark);
        door.closedPos = Vector3.zero;
        // Elle coulisse dans la paroi.
        door.openPos = Vector3.right * (size.x + 0.05f);
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        doors[index] = door;
        door.SetOpen(GameSession.SecretDoorOpen(index), false);
        return door;
    }

    static GameObject Prim(Transform parent, Vector3 pos, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameArt.Prim(type, parent, pos, size, mat);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    Light ledGlow;
    static readonly Color LedRed = new Color(1f, 0.12f, 0.08f), LedGreen = new Color(0.15f, 1f, 0.25f);
    const float LedLight = 0.35f;

    // Boîtier (centre 'at', face vers +Z local) et sa led, en haut.
    void BuildReader(Transform parent, Vector3 at, Material dark)
    {
        const float W = 0.13f, H = 0.2f, D = 0.05f;
        var reader = new GameObject("LecteurCarte").transform;
        reader.SetParent(parent, false);
        reader.localPosition = at;
        Vector3 ledPos = new Vector3(0f, H * 0.36f, D * 0.5f + 0.004f);
        var model = GameArt.SpawnModel("Decor/LecteurCarte", reader, H);
        if (model != null)
        {
            Bounds b = GameArt.LocalBounds(model.transform);
            model.transform.localPosition += new Vector3(0f, -H * 0.5f, -b.max.z + D * 0.5f);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            Material steel = GameArt.Lit("LecteurAcier", new Color(0.46f, 0.47f, 0.48f), GameArt.Noise("rust", 64, 0.75f, 0.25f, 19), 0.55f);
            steel.SetFloat("_Metallic", 0.7f);
            Prim(reader, new Vector3(0f, 0f, -D * 0.15f), new Vector3(W + 0.03f, H + 0.03f, D * 0.3f), steel);   // plaque murale
            Prim(reader, Vector3.zero, new Vector3(W, H, D), steel);
            Prim(reader, new Vector3(0f, -H * 0.12f, D * 0.5f + 0.001f), new Vector3(W * 0.75f, 0.012f, 0.004f), dark);   // fente
            Prim(reader, new Vector3(0f, H * 0.12f, D * 0.5f + 0.001f), new Vector3(W * 0.6f, H * 0.22f, 0.003f), dark);  // plaque gravée
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                    Prim(reader, new Vector3(sx * W * 0.38f, sy * H * 0.42f, D * 0.5f + 0.002f), new Vector3(0.012f, 0.012f, 0.004f), GameArt.Metal, PrimitiveType.Cylinder)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        var bulb = Prim(reader, ledPos, new Vector3(0.018f, 0.018f, 0.012f), GameArt.Unlit("VoyantRouge", LedRed), PrimitiveType.Sphere);
        bulb.name = "Led";
        led = bulb.GetComponent<Renderer>();
        ledGlow = new GameObject("LueurLed").AddComponent<Light>();
        ledGlow.transform.SetParent(reader, false);
        ledGlow.transform.localPosition = ledPos + Vector3.forward * 0.06f;
        ledGlow.type = LightType.Point;
        ledGlow.range = 0.7f;
        ledGlow.intensity = LedLight;
        ledGlow.shadows = LightShadows.None;
    }

    public void SetOpen(bool open, bool animate)
    {
        if (open && !Open && animate) GameAudio.Play3D("lab_door", transform.position, 1f, 20f, 0.05f);
        Open = open;
        if (!animate) progress = open ? 1f : 0f;
        if (led != null) led.sharedMaterial = open ? GameArt.Unlit("VoyantVert", LedGreen) : GameArt.Unlit("VoyantRouge", LedRed);
        // Même intensité lumineuse, rouge ou verte.
        if (ledGlow != null) { ledGlow.color = open ? LedGreen : LedRed; ledGlow.intensity = LedLight; }
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

    // La led n'a pas de contour : sinon, la porte visée au moment de passer la carte gardait en mémoire la led rouge et la
    // remettait en retirant le contour (led rouge, lueur verte).
    protected override bool HasOutline(Renderer r) => r != led;

    public override bool CanInteract => !Open && GameSession.Instance != null && GameSession.Instance.SelectedItem == Card;
    public override string Prompt => Loc.T(Index == 2 ? "Utiliser la Carte Magnétique 2" : "Utiliser la Carte Magnétique 1");
    public override void Interact() => GameSession.Instance.UseControlCard(Index);

    void OnDestroy()
    {
        if (doors[Index] == this) doors[Index] = null;
    }
}

// Porte du Bureau L.I.V. (V0.7.4) : entrouverte, marquée "L.I.V." en blanc lumineux et en relief côté Vestiaire. Un joueur
// la pousse (objet interactif) : elle s'ouvre en grand, chez tous (état tenu par l'hôte, GameSession.LivDoorPushed).
public class LivDoor : Interactable
{
    public static LivDoor Current { get; private set; }
    const float AjarAngle = 38f, PushedAngle = 102f, SwingTime = 0.9f;
    bool pushed;
    float angle = AjarAngle;

    // 'hinge' : pied de la charnière (côté nord de l'ouverture) ; fermée, la porte s'étend vers le sud.
    public static LivDoor Create(Transform parent, Vector3 hinge, float width, float height)
    {
        var go = new GameObject("PorteLIV");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = hinge;
        var door = go.AddComponent<LivDoor>();
        Material wood = GameArt.Lit("PorteBureauBois", new Color(0.27f, 0.24f, 0.2f), GameArt.Noise("wood", 128, 0.75f, 0.2f, 13), 0.25f);
        Material metal = GameArt.Lit("PoigneeLaiton", new Color(0.7f, 0.56f, 0.3f), null, 0.8f);
        var leaf = GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, height * 0.5f, -width * 0.5f), new Vector3(0.05f, height, width), wood);
        leaf.AddComponent<BoxCollider>();
        foreach (float s in new[] { -1f, 1f })
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(s * 0.045f, 1.0f, -width + 0.1f), new Vector3(0.04f, 0.03f, 0.13f), metal);
        Letters(go.transform, new Vector3(-0.03f, 1.55f, -width * 0.5f));
        Current = door;
        door.SetPushed(GameSession.LivDoorPushed, false);
        return door;
    }

    // "L.I.V." : modèle Meshy s'il existe (Resources/Art/Decor/EnseigneLIV), sinon lettres en relief (couches de texte
    // superposées, la face blanche et lumineuse devant).
    static void Letters(Transform parent, Vector3 at)
    {
        var holder = new GameObject("LIV").transform;
        holder.SetParent(parent, false);
        holder.localPosition = at;
        // Face lisible depuis le Vestiaire (-X).
        holder.localRotation = Quaternion.Euler(0f, 90f, 0f);
        var shape = Resources.Load<Mesh>("Art/Decor/EnseigneLIV");
        if (shape != null)
        {
            float scale = 0.7f / Mathf.Max(0.01f, shape.bounds.size.x);
            var go = GameArt.MeshObject("L.I.V.", holder, shape, GameArt.Emissive("NeonBlancVif", Color.white, new Color(1.6f, 1.6f, 1.7f)));
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(-shape.bounds.center.x * scale, -shape.bounds.center.y * scale, -shape.bounds.max.z * scale);
        }
        else
        {
            const int layers = 6;
            for (int k = layers - 1; k >= 0; k--)
            {
                float t = k / (float)(layers - 1);
                var text = GameArt.CreateText(holder, k == 0 ? "Face" : "Relief" + k, new Vector3(0f, 0f, k * 0.004f), 0.05f,
                    k == 0 ? Color.white : Color.Lerp(new Color(0.75f, 0.75f, 0.78f), new Color(0.25f, 0.25f, 0.27f), t));
                text.text = "L.I.V.";
                text.fontStyle = FontStyle.Bold;
            }
        }
        var light = new GameObject("LueurLIV").AddComponent<Light>();
        light.transform.SetParent(holder, false);
        light.transform.localPosition = new Vector3(0f, 0f, -0.35f);
        light.type = LightType.Point;
        light.color = new Color(0.92f, 0.95f, 1f);
        light.range = 1.6f;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;
    }

    public void SetPushed(bool value, bool animate)
    {
        if (value && !pushed && animate) GameAudio.Play3D("door", transform.position + Vector3.up, 0.6f, 15f, 0.05f, 0.7f);
        pushed = value;
        if (!animate) angle = pushed ? PushedAngle : AjarAngle;
        Apply();
    }

    void Update()
    {
        float target = pushed ? PushedAngle : AjarAngle;
        if (Mathf.Approximately(angle, target)) return;
        angle = Mathf.MoveTowards(angle, target, (PushedAngle - AjarAngle) / SwingTime * Time.deltaTime);
        Apply();
    }

    // Elle pivote vers le Bureau (+X).
    void Apply() => transform.localRotation = Quaternion.Euler(0f, -angle, 0f);

    public override bool CanInteract => !pushed;
    public override string Prompt => Loc.T("Pousser la porte");
    public override void Interact() => GameSession.Instance.PushLivDoor();

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}

// Lumière qui grésille : allumée presque tout le temps, avec des coupures brèves et irrégulières (plafonnier de l'Ascenseur).
public class FlickeringLight : MonoBehaviour
{
    Light lamp;
    float baseIntensity, nextChange;

    void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
    }

    void Update()
    {
        if (Time.time < nextChange) return;
        bool off = lamp.intensity > 0.01f && Random.value < 0.35f;
        lamp.intensity = off ? 0f : baseIntensity * Random.Range(0.6f, 1f);
        nextChange = Time.time + (off ? Random.Range(0.03f, 0.18f) : Random.Range(0.08f, 1.6f));
    }
}
