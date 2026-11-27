using System.Collections.Generic;
using UnityEngine;

// V0.7.22 (11.2) : salles uniques de l'Échiquier et Jetons Noirs (8.11).
// Dans chaque zone, à partir de la Zone 2, hors des structures et des ravins : 3 à 5 salles uniques différentes (6 si la zone n'a pas de
// structure), chacune selon l'un des 12 schémas du GDD, tournée au hasard. Salles carrées : 12 x 12 cases du damier (1 m), dans
// 3 x 3 cellules (15 m), de 8 m de haut, plafond ceinturé de néons le long des murs (ils ne clignotent qu'à l'approche du Roi). Seules
// les pièces d'échecs des schémas y sont posées (ni cube, ni œil, ni colonne, ni sphère...) : symbole retourné, pièce au plafond ;
// symbole deux (trois) fois plus gros, pièce deux (trois) fois plus volumineuse ; couleur du symbole. Les fous, dames et tours noirs
// sont des Jetons Noirs.
public static partial class WorldBuilder
{
    const int UniqueSquares = 12, UniqueCells = 3;

    // Schémas du GDD (case (colonne, ligne) du coin haut gauche de la pièce, la ligne 0 au nord) : pièce (K roi, Q dame, R tour,
    // B fou, N cavalier), couleur (w blanc, b noir), case ; "xN" : symbole N fois plus gros (N x N cases) ; "c" : retourné (au plafond).
    static readonly string[] UniqueTemplates =
    {
        "Qb11,0 Bb0,1 Nw3,1 Rb5,2 Rw8,3x3c Rb1,6 Bb8,7 Nw9,9 Kw3,9x2 Qb6,11",
        "Bb2,1 Nw8,2 Rb6,3 Rb11,4 Qb0,5 Rw2,6x3c Qb6,7 Kw9,9x2 Bb5,10",
        "Qb11,0 Rw1,1x3c Kw8,2x2 Bb4,5 Rb10,5 Rb0,7 Bb5,8 Nw9,9x2c Nw1,11 Qb6,11",
        "Nw4,0 Rb8,1 Qb1,2 Rw5,4x3c Bb11,4 Qb4,7 Rb11,8 Bb7,10 Nw10,10",
        "Qb3,0 Bb7,0 Nw1,1 Rb10,1 Kw5,4x3 Qb9,6 Bb3,8 Nw7,9 Rb1,10",
        "Rb7,0 Kw8,1x3 Nw2,2 Bb5,2 Rb4,5 Nw7,7x2c Bb1,10",
        "Bb3,0 Nw8,1x2c Bb4,3 Rb7,4 Qb11,6 Kw3,7x3 Qb1,8 Nw10,8 Rb2,11",
        "Rb11,0 Nw3,1 Qb5,2 Rw8,3x2c Rb6,5 Bb1,6 Bb4,7 Kw8,7x3 Nw3,9 Qb2,11",
        "Kw0,0x3 Qb7,0 Bb6,3 Nw9,3 Rb10,5 Rb1,6 Bb5,8 Nw3,9 Rw9,9x3 Qb6,11",
        "Qb7,0 Bb11,0 Rb0,1 Rw3,2x2 Nw8,3x2c Kw9,7 Qb2,7 Nw1,8x2c Bb5,8 Rb8,11",
        "Rw0,0x3 Nw9,1 Rb6,3 Bb3,4 Qb5,4 Nw5,7x2c Rb11,8 Bb2,9 Qb9,10 Kw11,11",
        "Rw9,0x3 Bb2,1 Kw6,2 Rb4,3 Rb0,5 Qb3,6 Bb3,8 Rw0,9x3 Nw9,9 Qb10,11",
    };

    struct UniquePiece
    {
        public string model;
        public bool white, ceiling;
        public int c, r, size;
    }

    static List<UniquePiece> ParseUniqueTemplate(int index)
    {
        var list = new List<UniquePiece>();
        foreach (string token in UniqueTemplates[index].Split(' '))
        {
            if (token.Length < 4) continue;
            var piece = new UniquePiece { size = 1 };
            switch (token[0])
            {
                case 'K': piece.model = "King"; break;
                case 'Q': piece.model = "Queen"; break;
                case 'R': piece.model = "Rook"; break;
                case 'B': piece.model = "Bishop"; break;
                case 'N': piece.model = "Knight"; break;
                default: piece.model = "Pawn"; break;
            }
            piece.white = token[1] == 'w';
            string rest = token.Substring(2);
            piece.ceiling = rest.EndsWith("c");
            if (piece.ceiling) rest = rest.Substring(0, rest.Length - 1);
            int x = rest.IndexOf('x');
            if (x >= 0)
            {
                piece.size = int.Parse(rest.Substring(x + 1));
                rest = rest.Substring(0, x);
            }
            string[] cr = rest.Split(',');
            piece.c = int.Parse(cr[0]);
            piece.r = int.Parse(cr[1]);
            list.Add(piece);
        }
        return list;
    }

    // Avant le tirage du labyrinthe : les salles uniques, posées avant les autres salles (formes imposées de 3 x 3 cellules).
    static void PlanUniqueRooms(ZoneStyle style, TerrainKind kind, int zone, System.Random rnd)
    {
        if (zone < 2) return;
        int count = kind == TerrainKind.Echiquier ? GameRules.UniqueRoomsWithoutStructure
            : GameRules.MinUniqueRooms + rnd.Next(GameRules.MaxUniqueRooms - GameRules.MinUniqueRooms + 1);
        count = Mathf.Min(count, UniqueTemplates.Length);
        style.bigShapes = new List<bool[,]>();
        for (int k = 0; k < count; k++)
        {
            var mask = new bool[UniqueCells, UniqueCells];
            for (int c = 0; c < UniqueCells; c++)
                for (int r = 0; r < UniqueCells; r++) mask[c, r] = true;
            style.bigShapes.Add(mask);
        }
        style.bigCount = count;
        style.bigPlain = true;
        style.bigCeil = GameRules.UniqueRoomHeight;
    }

    // Après le tirage : chaque salle unique reçoit un schéma différent et une orientation.
    static void AssignUniqueRooms(ZoneStyle style, System.Random rnd)
    {
        if (!style.bigPlain || style.bigPlaced.Count == 0) return;
        var layouts = new List<int>();
        for (int k = 0; k < UniqueTemplates.Length; k++) layouts.Add(k);
        foreach (var (room, _, _) in style.bigPlaced)
        {
            int pick = rnd.Next(layouts.Count);
            room.unique = layouts[pick];
            layouts.RemoveAt(pick);
            room.uniqueTurns = rnd.Next(4);
            room.chess = false;
        }
    }

    // Décor d'une salle unique : néons le long des murs au plafond, pièces d'échecs du schéma (sur les cases du damier).
    // 'pieces' reçoit l'emplacement des pièces posées au sol (aucun monticule dessus).
    static void BuildUniqueRoom(Maze m, Room room, Transform props, System.Random rnd, List<Vector3> pieces)
    {
        float minX = m.origin.x + room.rect.xMin * Cell, maxX = m.origin.x + room.rect.xMax * Cell;
        float minZ = m.origin.z + room.rect.yMin * Cell, maxZ = m.origin.z + room.rect.yMax * Cell;
        float inset = Wall * 0.5f + 0.35f;
        // Néons : un par mur, collé au plafond tout le long du mur (une seule lampe chacun : à huit, les murs voisins recevaient plus de
        // lampes que le rendu n'en calcule au pixel, et l'éclairage se coupait net d'un bloc à l'autre).
        float span = (maxX - minX) - 2f * inset;
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            Vector3 p = alongX ? new Vector3((minX + maxX) * 0.5f, room.ceil, side == 0 ? minZ + inset : maxZ - inset)
                               : new Vector3(side == 2 ? minX + inset : maxX - inset, room.ceil, (minZ + maxZ) * 0.5f);
            AddNeonStrip(props, p, span - 0.6f, alongX, rnd);
        }

        // Le damier de 12 x 12 cases dans la salle de 15 m : les cases du sol (1 m) sont bordées par les mètres entiers du monde ; la salle
        // en a 14 entières (un demi-mètre de part et d'autre, l'origine de la zone tombant sur un demi-mètre). Le schéma est décalé de 1
        // ou 2 cases, pour que ses cases noires tombent sur les cases noires du sol (case claire : somme de ses indices paire).
        int turns = room.uniqueTurns;
        Vector2 Turn(Vector2 p)
        {
            for (int k = 0; k < (turns & 3); k++) p = new Vector2(UniqueSquares - 1 - p.y, p.x);
            return p;
        }
        Vector2 corner = Turn(Vector2.zero);
        float tileX = Mathf.Ceil(minX - 0.001f), tileZ = Mathf.Floor(maxZ + 0.001f);
        int ox = 1, oz = 1;
        bool Dark(int dx, int dz)
        {
            int ix = Mathf.FloorToInt(tileX + dx + corner.x + 0.5f), iz = Mathf.FloorToInt(tileZ - dz - corner.y - 0.5f);
            return ((ix + iz) & 1) != 0;
        }
        if (!Dark(ox, oz)) ox = 2;
        Vector3 World(Vector2 p) => new Vector3(tileX + ox + p.x + 0.5f, room.floor, tileZ - oz - p.y - 0.5f);

        foreach (var piece in ParseUniqueTemplate(room.unique))
        {
            float mid = (piece.size - 1) * 0.5f;
            Vector3 at = World(Turn(new Vector2(piece.c + mid, piece.r + mid)));
            float scale = piece.size > 1 ? Mathf.Pow(piece.size, 1f / 3f) : 1f;
            if (piece.ceiling) at.y = room.ceil;
            var go = AddChessPiece(props, piece.model, at, rnd, scale, piece.ceiling, piece.white);
            if (go == null) continue;
            if (!piece.ceiling) pieces.Add(at);
            if (!piece.white && !piece.ceiling && piece.size == 1 && JetonModel(piece.model)) MakeJeton(m, go, piece.model);
        }
    }

    // Néon en long, collé au plafond, sans grésillement (il ne clignote qu'à l'approche d'un Roi).
    static void AddNeonStrip(Transform parent, Vector3 ceilingPos, float length, bool alongX, System.Random rnd)
    {
        var go = new GameObject("NeonSalleUnique");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = ceilingPos + Vector3.down * 0.05f;
        var bulb = GameArt.Prim(PrimitiveType.Cube, go.transform, Vector3.zero, alongX ? new Vector3(length, 0.05f, 0.2f) : new Vector3(0.2f, 0.05f, length),
            GameArt.Unlit("Neon", new Color(0.7f, 0.7f, 0.66f)));
        var l = AddLight(go.transform, Vector3.down * 0.4f, new Color(0.85f, 0.87f, 0.9f), 1.25f * LampDim, 11f * LampReach);
        go.AddComponent<FlickerLight>().Init(l, bulb.GetComponent<Renderer>(), false);
    }

    // Tours, fous et dames : les pièces noires de ces modèles sont des Jetons Noirs (Zone 2 et suivantes).
    static bool JetonModel(string model) => model == "Rook" || model == "Bishop" || model == "Queen";

    static void MakeJeton(Maze m, GameObject piece, string model)
    {
        piece.name = "JetonNoir_" + model;
        piece.AddComponent<JetonNoir>().Init(m.zone * 10000 + m.jetonCount++, m.zone, model);
    }

    // Centre de la case du damier (1 m) sous un point.
    static Vector3 SnapToSquare(Vector3 p) => new Vector3(Mathf.Floor(p.x) + 0.5f, p.y, Mathf.Floor(p.z) + 0.5f);
}
