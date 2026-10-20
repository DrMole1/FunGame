using UnityEngine;

// Variables numériques du document de Game Design (V0.4.5).
public static class GameRules
{
    // Version du jeu, affichée en bas à droite du lobby.
    public const string Version = "V0.4.5";

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
    // Chute du personnage plus rapide : +30 %, puis encore +30 % (1,3 × 1,3) ; la montée du saut est inchangée.
    public const float FallGravityMultiplier = 1.69f;
    // Saut 40 % plus court à hauteur égale : impulsion ÷ 0,6 et gravité du personnage en l'air ÷ 0,6².
    public const float JumpTimeScale = 0.6f;
    public static float AirGravityMultiplier(bool falling) =>
        (falling ? FallGravityMultiplier : 1f) / (JumpTimeScale * JumpTimeScale);

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

    public const float LaserTriggerDistance = 4f;
    public const int LaserDamage = 20;

    // Course : multiplicateur de la vitesse de base selon le nombre d'objets portés (0 à 5).
    static readonly float[] RunMultipliers = { 1.4f, 1.3f, 1.2f, 1.15f, 1.1f, 1.05f };
    public static float RunMultiplier(int itemCount) => RunMultipliers[Mathf.Clamp(itemCount, 0, RunMultipliers.Length - 1)];
    public const float SneakMultiplier = 0.5f;
    // Marche discrète : le personnage se baisse (tête environ 20 % plus bas dans l'animation) ; sa capsule
    // raccourcit d'autant (pieds au même endroit) et la vue à la première personne descend au niveau de sa tête.
    public const float SneakHeightFactor = 0.8f;
    public const float SneakEyeDrop = 0.3f;
    public const float BaseSpeed = 4f;

    // Premier quota à 500 (exemple du document), +250 à chaque nouvelle manche.
    public static int QuotaForRound(int round) => 500 + (round - 1) * 250;

    // ---------- Évolution du terrain (chapitre 10) ----------

    // Génération d'objets de valeur, en pourcentage du quota de la manche (manche 8 et suivantes : dernière valeur).
    static readonly float[] Zone1ValuePercent = { 1.5f, 1.4f, 1.3f, 1.2f, 1.1f, 1f, 0.9f, 0.8f };
    // V0.4.1 : la Zone 2 reçoit objets et entités dès la manche 3.
    static readonly float[] Zone2ValuePercent = { 0f, 0f, 1f, 1f, 1.1f, 1.2f, 1.2f, 1.2f };
    // Entités de type "Cauchemar" qui apparaissent au début de la manche.
    static readonly int[] Zone1Nightmares = { 0, 1, 2, 1, 2, 2, 2, 2 };
    static readonly int[] Zone2Nightmares = { 0, 0, 2, 2, 2, 2, 3, 3 };
    // Entités de type "Normal" (V0.4.2) : 3 par zone ouverte aux entités dès la manche 2 ; manche 1 : 2 après 2 minutes de jeu.
    public const int NormalsPerZone = 3;
    public const int LateNormals = 2;

    static int RoundIndex(int round) => Mathf.Clamp(round - 1, 0, Zone1ValuePercent.Length - 1);

    // Note 01 du chapitre 10 : à 2 joueurs connectés, la génération d'objets de valeur est doublée ; seul, triplée.
    public static int ValueBoost(int players) => players <= 1 ? 3 : players == 2 ? 2 : 1;

    public static int DucksForRound(int round, int zone, int boost)
    {
        float percent = (zone == 0 ? Zone1ValuePercent : Zone2ValuePercent)[RoundIndex(round)] * boost;
        int duckValue = Items.Get(ItemType.Canard).sellPrice;
        return Mathf.CeilToInt(QuotaForRound(round) * percent / duckValue);
    }

    public static int NightmaresForRound(int round, int zone) => (zone == 0 ? Zone1Nightmares : Zone2Nightmares)[RoundIndex(round)];

    // Manche 1 : un Cauchemar choisi au hasard apparaît dans la Zone 1 après 3 minutes de jeu, et 2 entités de type Normal
    // après 2 minutes, sans prévenir les joueurs.
    public const int LateSpawnRound = 1;
    public const float LateNightmareDelay = 3f * 60f;
    public const float LateNormalsDelay = 2f * 60f;

    // ---------- L'Abjuration (chapitre 10.1) ----------

    // Elle apparaît quand le Compteur de Temps affiche 7 minutes, jusqu'à la manche suivante.
    public const float AbjurationStart = 7f * 60f;
    public const float AbjurationLossPerSecond = 2f;   // sérénité perdue par seconde au contact de la brume (V0.4.2)
    // Note 02 (V0.4.2) : hors de la brume, Hub compris, la sérénité remonte de 10 points par seconde.
    public const float AbjurationRegenPerSecond = 10f;
    public const float MistSpeedMultiplier = 0.8f; // déplacement ralenti de 20 % dans la brume
    // Hauteur (absolue) du dessus de la brume : le ras du sol du Hub.
    public const float MistTop = 0.5f;

    // La Dame, La Tour et Le Roi sont 25 % plus grands que la taille de leur modèle (3,33 m) ; le labyrinthe garde au moins
    // 3,6 m sous plafond partout où ils passent (couloirs 4 m, porte de la Zone 2 3,6 m).
    public const float BigEntityScale = 1.25f;

    // Note 03 du chapitre 3.4 (V0.4.3) : tué par un Cauchemar, la caméra fixe ses yeux environ 1,5 seconde.
    public const float NightmareStareDuration = 1.5f;

    // ---------- La Dame (entité ID 2) ----------

    public const int DameDamage = 100;
    public const float DameChaseMultiplier = 1.05f;
    public const float DameWatchedMultiplier = 2.2f;
    public const float DameChaseMin = 20f, DameChaseMax = 30f;
    public const float DameSightRange = 20f;
    public const float DameKnockback = 9f;

    // Hurlement spatialisé toutes les 5 à 8 secondes, audible à 40 m (pitch au hasard entre -1 et +1 demi-ton).
    public const float DameScreamMin = 5f, DameScreamMax = 8f, DameScreamRange = 40f;

    // ---------- La Tour (entité ID 3, Cauchemar) ----------

    public const int TourDamage = 100;
    public const float TourWalkMultiplier = 0.7f;        // "modérément lente"
    public const float TourInvestigateMultiplier = 1.35f; // "modérément rapide", vers la localisation tampon
    public const float TourSightRange = 50f;
    public const float TourStepRange = 30f;
    public const float TourSurveyMin = 7f, TourSurveyMax = 15f, TourSurveyDuration = 2f;
    public const float TourTurnBackChance = 0.3f;
    public const float TourChannel = 3f;
    public const float TourChargeSpeed = 45f;            // "à la vitesse de la lumière" : quasi instantané
    public const float TourKnockback = 10f;

    // ---------- Le Fou (entité ID 7, Cauchemar, V0.4.5) ----------

    public const int FouDamage = 20;
    public const float FouRunMultiplier = 1.5f;
    public const float FouRunMin = 2f, FouRunMax = 5f;      // durée d'une course
    public const float FouWalkMin = 4f, FouWalkMax = 10f;   // marche avant la course suivante
    public const float FouTurnBackChance = 0.3f;
    public const float FouBellsRange = 30f;
    // Non précisé : la caméra "le voit" s'il est à l'écran, sans mur entre eux, à moins de 40 m.
    public const float FouSightRange = 40f;
    public const float FouStareTime = 2f;                   // regardé 2 s : il attaque
    public const float FouLookAgainWindow = 3f;             // regard détourné puis revenu dans les 3 s : il attaque aussi
    public const float FouSwapCooldown = 5f;                // après un échange, 5 s sans pouvoir lancer son attaque
    public const float FouChaseDuration = 5f;               // en chasse, il court vers le joueur, puis danse
    public const float FouKillDistance = 0.8f;
    public const float FouDanceChance = 0.15f, FouDanceDuration = 3f;

    // ---------- Le Roi (entité ID 8, Cauchemar, V0.4.4) ----------

    public const float RoiWalkMultiplier = 0.7f;            // "légèrement lente"
    public const float RoiWalkMin = 7f, RoiWalkMax = 15f;   // marche avant un arrêt
    public const float RoiPauseMin = 2f, RoiPauseMax = 5f;  // arrêt, totalement immobile
    public const float RoiTurnBackChance = 0.1f;
    public const float RoiBeaconCooldown = 40f;             // après avoir atteint une Balise
    public const float RoiAuraRadius = 10f;                 // Abjuration autour de lui, hors Abjuration
    public const float RoiSightRange = 20f;
    public const float RoiSenseRadius = 6f;                 // tout près, il sent le joueur même dans son dos
    public const float RoiSlowMultiplier = 0.8f;            // aura blanche : -20 %, sans course
    public const float RoiAimFirst = 2.5f, RoiAimAgain = 1.5f;
    // Non précisé : "à grande vitesse", mais esquivable (environ 0,8 s pour 20 m).
    public const float RoiChainSpeed = 26f;                 // 20 m/s x 1,3
    public const float RoiChainRadius = 0.7f;               // la pointe touche le joueur à moins de 70 cm
    public const int RoiBreakPresses = 5;
    public const float RoiPullMultiplier = 2f;              // le joueur enchaîné est traîné deux fois plus vite qu'il ne marche                   // appuis sur Espace pour rompre la chaîne
    public const float RoiRecover = 1f;
    public const float RoiKillDistance = 0.8f;

    // ---------- Le Pion (entité ID 4, Normal) ----------

    public const int PionDamage = 30;
    public const float PionHabitat = 30f;
    public const float PionWatchDistance = 5f;
    public const float PionCuriousMin = 4f, PionCuriousMax = 8f;
    public const float PionRushMultiplier = 1.4f;        // il "se met à courir" : vitesse de course d'un joueur
    public const float PionKnockback = 6f;

    // ---------- La Cloche (entité ID 5, Normal) ----------

    public const int ClocheDamage = 10;
    public const float ClocheSightRange = 50f;
    public const float ClocheTick = 2f;                  // dégâts toutes les 2 secondes passées à la regarder
    public const float ClocheStopDistance = 2f;
    public const float ClocheSearchMin = 50f, ClocheSearchMax = 70f;
    public const float ClocheSearchMultiplier = 1.5f;

    // ---------- L'Araignée (entité ID 6, Abjuration) ----------

    public const int SpiderDamage = 50;
    public const int SpidersPerZone = 5;
    public const float SpiderSpawnClearance = 25f;
    public const float SpiderSightRange = 12f;
    public const float SpiderProwlMultiplier = 0.5f;
    public const float SpiderRushMultiplier = 2.6f;      // "très rapidement"
    public const float SpiderAttackDistance = 0.8f;
    public const float SpiderPause = 1f, SpiderPursuit = 10f;
    public const float SpiderKnockback = 7f;
    // Une action bruyante (aboiement, saut, objet ramassé ou posé) trahit un joueur accroupi pendant ce délai.
    public const float NoiseMemory = 0.6f;

    // Entités de type Normal au début de la manche : 3 dans chaque zone qui reçoit des Cauchemars (manche 1 : plus tard).
    public static int NormalsForRound(int round, int zone) =>
        round > LateSpawnRound && NightmaresForRound(round, zone) > 0 ? NormalsPerZone : 0;

    // Zone 2 : les orbes s'activent au début des manches 1, 2 et 3 (V0.3.4).
    public const int OrbCount = 3;
    public static bool OrbActive(int orb, int round) => round >= orb + 1;

    // ---------- Balises (chapitre 7.1) ----------

    // Charge de la Balise du Hub (au début du jeu, et après chaque Chargeur).
    public const float BeaconCharge = 10f * 60f;
    // Balise Améliorée (7.3) : ni Abjuration ni apparition d'entité dans ce rayon.
    public const float EnhancedBeaconRadius = 5f;

    // ---------- Téléporteur (7.2) et Station Curative (7.4) ----------

    public const float DeviceRange = 1f;
    public const float TeleportChannel = 3f;
    public const float TeleportSpeed = 70f;   // boule d'énergie le long des liens
    public const float StationHealPerSecond = 10f;

    // ---------- Pogo (chapitre 4) ----------

    public const float PogoIdleClose = 10f;    // sans achat pendant 10 s, il rentre dans sa boîte
    public const float PogoSmile = 3f;         // il sourit 3 s après un achat
    public const float PogoCooldown = 5f;      // après avoir frappé, impossible de l'appeler pendant 5 s
}

public enum ItemType { None = 0, Croquette = 1, Canard = 2, PostIt = 3, Cle = 4, Boussole = 5, Balise = 6, Chargeur = 7, Teleporteur = 8, BaliseAmelioree = 9, StationCurative = 10 }

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
        // Objets obtenables au Terminal (ID 1 à 9), dans l'ordre du document.
        new ItemDef { type = ItemType.Croquette, terminalId = 1, name = "Croquette", shortName = "Croquette", buyPrice = 20, sellPrice = 1, color = new Color(0.62f, 0.4f, 0.22f) },
        new ItemDef { type = ItemType.Balise, terminalId = 2, name = "Balise", shortName = "Balise", buyPrice = 30, sellPrice = 1, color = new Color(0.3f, 0.6f, 1f) },
        new ItemDef { type = ItemType.PostIt, terminalId = 3, name = "Post-It", shortName = "Post-It", buyPrice = 5, sellPrice = 1, color = new Color(1f, 0.93f, 0.35f) },
        new ItemDef { type = ItemType.Cle, terminalId = 4, name = "Clé", shortName = "Clé", buyPrice = 200, sellPrice = 1, color = new Color(0.55f, 0.8f, 1f) },
        new ItemDef { type = ItemType.Boussole, terminalId = 5, name = "Boussole", shortName = "Boussole", buyPrice = 100, sellPrice = 0, color = new Color(0.9f, 0.9f, 0.9f), inventory = false },
        new ItemDef { type = ItemType.Chargeur, terminalId = 6, name = "Chargeur", shortName = "Chargeur", buyPrice = 50, sellPrice = 1, color = new Color(0.35f, 0.8f, 1f) },
        new ItemDef { type = ItemType.Teleporteur, terminalId = 7, name = "Téléporteur", shortName = "Téléport.", buyPrice = 200, sellPrice = 1, color = new Color(1f, 0.7f, 0.15f) },
        new ItemDef { type = ItemType.BaliseAmelioree, terminalId = 8, name = "Balise Améliorée", shortName = "Balise +", buyPrice = 50, sellPrice = 1, color = new Color(1f, 0.3f, 0.3f) },
        new ItemDef { type = ItemType.StationCurative, terminalId = 9, name = "Station Curative", shortName = "Station", buyPrice = 150, sellPrice = 1, color = new Color(0.4f, 1f, 0.45f) },
        // Autres objets.
        new ItemDef { type = ItemType.Canard, name = "Canard en Plastique", shortName = "Canard", buyPrice = 0, sellPrice = 20, color = new Color(1f, 0.84f, 0.15f) },
    };

    // Mort d'un joueur (3.4) : les objets du Terminal sont détruits, les autres (avec un modèle 3D) restent à terre.
    public static bool DropsOnDeath(ItemType type)
    {
        var def = Get(type);
        return def != null && def.buyPrice <= 0 && type == ItemType.Canard;
    }

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
