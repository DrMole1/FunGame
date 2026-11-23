// Environnements de terrain (GDD V0.7.11, chapitre 11) : chaque zone est générée selon un environnement, avec ses règles de
// génération, son architecture, son ambiance et sa direction artistique.
//   - Échiquier : salles et couloirs en labyrinthe (le seul environnement jusqu'en V0.7.10) ;
//   - Château : remparts, Tours, Donjon, Maisons-Fou, salles à créneaux reliées par des corridors ;
//   - Blockhaus : appartements et couloirs sur 4 étages, coupés en deux par un ravin.
// La Zone 1 est toujours un Échiquier ; ensuite, l'environnement de chaque zone est tiré au hasard (d'après la graine), sans
// jamais reprendre celui de la zone précédente.
public enum TerrainKind : byte { Echiquier = 0, Chateau = 1, Blockhaus = 2 }

public static class Terrains
{
    const int Count = 3;

    public static TerrainKind KindOf(int seed, int zone)
    {
        TerrainKind kind = TerrainKind.Echiquier;
        for (int z = 2; z <= zone; z++)
        {
            // L'un des deux autres environnements, tiré avec une graine propre à la zone.
            int pick = new System.Random(seed * 7121 + z * 92821).Next(Count - 1);
            kind = (TerrainKind)(((int)kind + 1 + pick) % Count);
        }
        return kind;
    }

    public static string Name(TerrainKind kind) =>
        kind == TerrainKind.Chateau ? "Château" : kind == TerrainKind.Blockhaus ? "Blockhaus" : "Échiquier";
}
