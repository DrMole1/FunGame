using UnityEngine;

// Environnements de terrain (chapitre 11). V0.7.14 : chaque zone est générée selon l'environnement Échiquier ; certaines zones
// contiennent en plus une structure, Château ou Blockhaus, posée sur les parcelles d'un des 12 gabarits du GDD (la zone est
// découpée en 4 x 4 parcelles ; la Neutral Room est au nord, l'entrée au sud).
//   - Zone 1 : aucune structure ;
//   - Zone 2 : toujours une structure (Château 50 %, Blockhaus 50 %) ;
//   - à partir de la Zone 3 : 60 % de chance d'avoir une structure (Château 50 %, Blockhaus 50 %).
// Le tirage ne dépend que de la graine et du numéro de la zone : tous les joueurs ont le même.
public enum TerrainKind : byte { Echiquier = 0, Chateau = 1, Blockhaus = 2 }

public static class Terrains
{
    // Parcelles bleues de chaque gabarit : (colonne d'ouest en est, rangée du nord (Neutral Room) au sud (entrée)), de 0 à 3.
    static readonly (int col, int row)[][] Templates =
    {
        new[] { (3, 0), (0, 1), (1, 1), (2, 1), (3, 1) },                   // 1
        new[] { (0, 0), (0, 1), (1, 1), (2, 1), (3, 1) },                   // 2
        new[] { (0, 0), (3, 0), (0, 1), (1, 1), (2, 1), (3, 1) },           // 3
        new[] { (1, 0), (2, 0), (1, 1), (2, 1) },                           // 4
        new[] { (0, 0), (1, 0), (2, 0), (3, 0), (0, 1) },                   // 5
        new[] { (0, 0), (1, 0), (2, 0), (3, 0), (3, 1) },                   // 6
        new[] { (0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (3, 1) },           // 7
        new[] { (0, 0), (1, 0), (2, 0), (3, 0), (1, 1), (2, 1) },           // 8
        new[] { (0, 1), (1, 1), (2, 1), (3, 1), (3, 2) },                   // 9
        new[] { (0, 1), (1, 1), (2, 1), (3, 1), (0, 2) },                   // 10
        new[] { (0, 1), (1, 1), (2, 1), (3, 1), (3, 0), (3, 2) },           // 11
        new[] { (0, 1), (1, 1), (2, 1), (3, 1), (0, 0), (0, 2) },           // 12
    };

    public static int TemplateCount => Templates.Length;

    // Structure de la zone (Echiquier : aucune).
    public static TerrainKind StructureOf(int seed, int zone)
    {
        if (zone < 2) return TerrainKind.Echiquier;
        if (zone >= 3 && Hash01(seed, zone, 1) >= GameRules.StructureChance) return TerrainKind.Echiquier;
        return Hash01(seed, zone, 2) < GameRules.ChateauChance ? TerrainKind.Chateau : TerrainKind.Blockhaus;
    }

    // Gabarit (0 à 11) de la structure de la zone.
    public static int TemplateOf(int seed, int zone) => Mathf.Min(Templates.Length - 1, (int)(Hash01(seed, zone, 3) * Templates.Length));

    // Tirage dans [0, 1[ d'après la graine, la zone et un sel (hachage : deux zones voisines n'ont pas des tirages voisins, ce que
    // System.Random ne garantit pas avec des graines proches).
    static double Hash01(int seed, int zone, int salt)
    {
        ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)zone * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)salt * 0x165667B19E3779F9UL;
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    public static bool InTemplate(int template, int col, int row)
    {
        foreach (var (c, r) in Templates[template]) if (c == col && r == row) return true;
        return false;
    }

    // Parcelle d'une cellule de la grille de la zone (34 x 34 cellules : parcelles de 9, 8, 9 et 8 cellules).
    public static int ParcelCol(int i) => Mathf.Clamp(i * 4 / WorldBuilder.ZoneCells, 0, 3);
    public static int ParcelRow(int j) => 3 - Mathf.Clamp(j * 4 / WorldBuilder.ZoneCells, 0, 3);

    // Première zone (à partir de 'from', en s'éloignant de part et d'autre) qui contient une structure de ce type ; 0 si aucune
    // dans les 'range' zones autour.
    public static int NearestZoneWith(int seed, TerrainKind kind, int from, int range = 200)
    {
        for (int d = 0; d <= range; d++)
            foreach (int z in new[] { from + d, from - d })
                if (z >= 1 && StructureOf(seed, z) == kind) return z;
        return 0;
    }

    public static string Name(TerrainKind kind) =>
        kind == TerrainKind.Chateau ? "Château" : kind == TerrainKind.Blockhaus ? "Blockhaus" : "Échiquier";
}
