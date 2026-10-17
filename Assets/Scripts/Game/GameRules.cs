using UnityEngine;

// Variables numériques du document de Game Design (V0.1).
public static class GameRules
{
    public const int MaxPlayers = 8;
    public const int MaxHp = 100;
    public const float HubRegenPerSecond = 5f;
    public const float RoundDuration = 15f * 60f;
    public const int InventorySize = 5;
    public const int LogLines = 8;
    public const float Gravity = -9.81f;

    public const float CameraMinDistance = 2f;
    public const float CameraMaxDistance = 5f;

    // Distance maximale entre le personnage et l'objet visé pour interagir.
    public const float InteractRange = 2.2f;

    public const float LaserTriggerDistance = 2f;
    public const int LaserDamage = 10;

    // Course : multiplicateur de la vitesse de base selon le nombre d'objets portés (0 à 5).
    static readonly float[] RunMultipliers = { 2f, 2f, 1.8f, 1.6f, 1.4f, 1.2f };
    public static float RunMultiplier(int itemCount) => RunMultipliers[Mathf.Clamp(itemCount, 0, RunMultipliers.Length - 1)];
    public const float SneakMultiplier = 0.5f;

    // Non précisé dans le GDD : premier quota à 500 (exemple du document), +250 par manche.
    public static int QuotaForRound(int round) => 500 + (round - 1) * 250;

    // Assez de canards pour dépasser le quota de 40 %, à répartir dans le labyrinthe.
    public static int DucksForRound(int round)
    {
        int duckValue = Items.Get(ItemType.Canard).sellPrice;
        return Mathf.CeilToInt(QuotaForRound(round) * 1.4f / duckValue);
    }
}

public enum ItemType { None = 0, Croquette = 1, Canard = 2 }

public class ItemDef
{
    public ItemType type;
    public string name;
    public string shortName;
    public int buyPrice;   // 0 = non achetable au Terminal
    public int sellPrice;
    public Color color;
}

public static class Items
{
    public const int CroquetteHeal = 10;

    static readonly ItemDef[] All =
    {
        new ItemDef { type = ItemType.Croquette, name = "Croquette", shortName = "Croquette", buyPrice = 10, sellPrice = 1, color = new Color(0.62f, 0.4f, 0.22f) },
        new ItemDef { type = ItemType.Canard, name = "Canard en Plastique", shortName = "Canard", buyPrice = 0, sellPrice = 20, color = new Color(1f, 0.84f, 0.15f) },
    };

    public static ItemDef Get(ItemType type)
    {
        foreach (var d in All) if (d.type == type) return d;
        return null;
    }

    public static ItemDef Get(int id) => Get((ItemType)id);

    public static System.Collections.Generic.IEnumerable<ItemDef> Buyable()
    {
        foreach (var d in All) if (d.buyPrice > 0) yield return d;
    }
}
