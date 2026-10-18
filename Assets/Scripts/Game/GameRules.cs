using UnityEngine;

// Variables numériques du document de Game Design (V0.3.3).
public static class GameRules
{
    public const int MaxPlayers = 8;
    public const int MaxHp = 100;
    public const int MaxSerenity = 100;
    public const float HubRegenPerSecond = 5f;
    // Non précisé dans le GDD : la sérénité remonte lentement dans le Hub (refuge des joueurs).
    public const float HubSerenityRegenPerSecond = 1f;
    public const float RoundDuration = 15f * 60f;
    public const int InventorySize = 5;
    public const int LogLines = 8;
    public const float Gravity = -9.81f;
    // Chute du personnage 30 % plus rapide (la montée du saut est inchangée).
    public const float FallGravityMultiplier = 1.3f;

    // Après un /tp, le personnage de l'hôte ne prend aucun dégât et ne peut pas être immobilisé pendant 5 secondes.
    public const float TeleportInvincibility = 5f;

    // Aboiement (touche A) : 3 aboiements au plus sur 10 secondes, entendus jusqu'à 50 m (décroissance douce).
    public const int BarkLimit = 3;
    public const float BarkWindow = 10f;
    public const float BarkRange = 50f;

    public const float CameraMinDistance = 2f;
    public const float CameraMaxDistance = 5f;

    // Distance maximale entre le personnage et l'objet visé pour interagir.
    public const float InteractRange = 2.2f;

    public const float LaserTriggerDistance = 2f;
    public const int LaserDamage = 20;

    // Course : multiplicateur de la vitesse de base selon le nombre d'objets portés (0 à 5).
    static readonly float[] RunMultipliers = { 1.4f, 1.3f, 1.2f, 1.15f, 1.1f, 1.05f };
    public static float RunMultiplier(int itemCount) => RunMultipliers[Mathf.Clamp(itemCount, 0, RunMultipliers.Length - 1)];
    public const float SneakMultiplier = 0.5f;
    public const float BaseSpeed = 4f;

    // Premier quota à 500 (exemple du document), +250 à chaque nouvelle manche.
    public static int QuotaForRound(int round) => 500 + (round - 1) * 250;

    // ---------- Évolution du terrain (chapitre 10) ----------

    // Génération d'objets de valeur, en pourcentage du quota de la manche (manche 8 et suivantes : dernière valeur).
    static readonly float[] Zone1ValuePercent = { 1.5f, 1.4f, 1.3f, 1.2f, 1.1f, 1f, 0.9f, 0.8f };
    static readonly float[] Zone2ValuePercent = { 0f, 0f, 0f, 1f, 1.1f, 1.2f, 1.2f, 1.2f };
    // Entités de type "Cauchemar" qui apparaissent au début de la manche.
    static readonly int[] Zone1Nightmares = { 0, 1, 2, 1, 2, 2, 2, 2 };
    static readonly int[] Zone2Nightmares = { 0, 0, 0, 2, 2, 2, 3, 3 };

    static int RoundIndex(int round) => Mathf.Clamp(round - 1, 0, Zone1ValuePercent.Length - 1);

    public static int DucksForRound(int round, int zone)
    {
        float percent = (zone == 0 ? Zone1ValuePercent : Zone2ValuePercent)[RoundIndex(round)];
        int duckValue = Items.Get(ItemType.Canard).sellPrice;
        return Mathf.CeilToInt(QuotaForRound(round) * percent / duckValue);
    }

    public static int NightmaresForRound(int round, int zone) => (zone == 0 ? Zone1Nightmares : Zone2Nightmares)[RoundIndex(round)];

    // Manche 1 : un Cauchemar choisi au hasard apparaît dans la Zone 1 après 2 minutes de jeu, sans prévenir les joueurs.
    public const int LateNightmareRound = 1;
    public const float LateNightmareDelay = 2f * 60f;

    // ---------- L'Abjuration (chapitre 10.1) ----------

    // Elle apparaît quand le Compteur de Temps affiche 7 minutes, jusqu'à la manche suivante.
    public const float AbjurationStart = 7f * 60f;
    public const float AbjurationTick = 3f;     // toutes les 3 secondes de contact...
    public const int AbjurationLoss = 1;        // ...1 point de sérénité en moins
    public const float MistSpeedMultiplier = 0.8f; // déplacement ralenti de 20 % dans la brume
    // Hauteur (absolue) du dessus de la brume : le ras du sol du Hub.
    public const float MistTop = 0.5f;

    // ---------- La Dame (entité ID 2) ----------

    public const int DameDamage = 100;
    public const float DameChaseMultiplier = 1.05f;
    public const float DameWatchedMultiplier = 2.2f;
    public const float DameChaseMin = 20f, DameChaseMax = 30f;
    public const float DameSightRange = 20f;
    public const float DameKnockback = 9f;

    // Zone 2 : les orbes s'activent à la fin des manches 1, 2 et 3.
    public const int OrbCount = 3;
    public static bool OrbActive(int orb, int round) => round >= orb + 2;

    // ---------- Balises (chapitre 7.1) ----------

    // Charge de la Balise du Hub (au début du jeu, et après chaque Chargeur).
    public const float BeaconCharge = 10f * 60f;

    // ---------- Pogo (chapitre 4) ----------

    public const float PogoIdleClose = 10f;    // sans achat pendant 10 s, il rentre dans sa boîte
    public const float PogoSmile = 3f;         // il sourit 3 s après un achat
    public const float PogoCooldown = 5f;      // après avoir frappé, impossible de l'appeler pendant 5 s
}

public enum ItemType { None = 0, Croquette = 1, Canard = 2, PostIt = 3, Cle = 4, Boussole = 5, Balise = 6, Chargeur = 7 }

public class ItemDef
{
    public ItemType type;
    public int terminalId; // ID affiché et tapé au Terminal (0 = pas en vente)
    public string name;
    public string shortName;
    public int buyPrice;   // 0 = non achetable au Terminal
    public int sellPrice;
    public Color color;
    public bool inventory = true; // la Boussole ne se garde pas dans l'inventaire
}

public static class Items
{
    public const int CroquetteHeal = 40;
    public const int CroquetteSerenity = 40;
    public const int PostItMaxChars = 20;
    public const float PostItLockSeconds = 2f; // un Post-It ne peut pas être retiré juste après avoir été accroché

    static readonly ItemDef[] All =
    {
        // Objets obtenables au Terminal (ID 1 à 6), dans l'ordre du document.
        new ItemDef { type = ItemType.Croquette, terminalId = 1, name = "Croquette", shortName = "Croquette", buyPrice = 20, sellPrice = 1, color = new Color(0.62f, 0.4f, 0.22f) },
        new ItemDef { type = ItemType.Balise, terminalId = 2, name = "Balise", shortName = "Balise", buyPrice = 30, sellPrice = 1, color = new Color(0.3f, 0.6f, 1f) },
        new ItemDef { type = ItemType.PostIt, terminalId = 3, name = "Post-It", shortName = "Post-It", buyPrice = 5, sellPrice = 1, color = new Color(1f, 0.93f, 0.35f) },
        new ItemDef { type = ItemType.Cle, terminalId = 4, name = "Clé", shortName = "Clé", buyPrice = 200, sellPrice = 1, color = new Color(0.55f, 0.8f, 1f) },
        new ItemDef { type = ItemType.Boussole, terminalId = 5, name = "Boussole", shortName = "Boussole", buyPrice = 100, sellPrice = 0, color = new Color(0.9f, 0.9f, 0.9f), inventory = false },
        new ItemDef { type = ItemType.Chargeur, terminalId = 6, name = "Chargeur", shortName = "Chargeur", buyPrice = 50, sellPrice = 1, color = new Color(0.35f, 0.8f, 1f) },
        // Autres objets.
        new ItemDef { type = ItemType.Canard, name = "Canard en Plastique", shortName = "Canard", buyPrice = 0, sellPrice = 20, color = new Color(1f, 0.84f, 0.15f) },
    };

    public static ItemDef Get(ItemType type)
    {
        foreach (var d in All) if (d.type == type) return d;
        return null;
    }

    public static ItemDef Get(int id) => Get((ItemType)id);

    public static ItemDef ByTerminalId(int id)
    {
        foreach (var d in All) if (d.terminalId > 0 && d.terminalId == id) return d;
        return null;
    }

    public static System.Collections.Generic.IEnumerable<ItemDef> Buyable()
    {
        foreach (var d in All) if (d.buyPrice > 0) yield return d;
    }
}
