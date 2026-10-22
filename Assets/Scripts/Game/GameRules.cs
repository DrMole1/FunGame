using UnityEngine;

// Variables numériques du document de Game Design (V0.4.7).
public static class GameRules
{
    // Version du jeu, affichée en bas à droite du lobby.
    public const string Version = "V0.4.7";

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

    // V0.4.6 : les objets de valeur sont dans des monticules ; leur nombre tient compte des chances d'obtention.
    public static int MoundsForRound(int round, int zone, int boost)
    {
        float percent = (zone == 0 ? Zone1ValuePercent : Zone2ValuePercent)[RoundIndex(round)] * boost;
        return Mathf.CeilToInt(QuotaForRound(round) * percent / Items.MoundValue());
    }

    // Monticules (note 02 du chapitre 7) : creuser prend 1,2 seconde.
    public const float DigDuration = 1.2f;
    // Avant de creuser, le personnage s'avance de lui-même jusqu'à 0,4 m du monticule (centre à centre, à l'horizontale).
    public const float DigApproach = 0.4f;

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

    // Note 01 du chapitre 8 (V0.4.6) : aucune entité (hors piège) n'apparaît à moins de 20 m d'un joueur.
    public const float EntitySpawnClearance = 20f;

    // Note 03 du chapitre 3.4 (V0.4.3) : tué par un Cauchemar, la caméra fixe ses yeux environ 1,5 seconde.
    public const float NightmareStareDuration = 1.5f;

    // ---------- La Dame (entité ID 2) ----------

    public const int DameDamage = 100;
    public const float DameChaseMultiplier = 1.05f;
    public const float DameWatchedMultiplier = 2.2f;
    public const float DameChaseMin = 12f, DameChaseMax = 15f;    // V0.4.6 (avant : 20 à 30 s)
    public const float DameSightRange = 20f;
    public const float DameKnockback = 9f;
    // V0.4.6 : cri (Dame_Cri) quand elle repère un joueur, audible à 40 m ; ses pas résonnent en écho jusqu'à 30 m.
    public const float DameScreamRange = 40f;
    public const float DameStepRange = 30f;
    // V0.4.7 : "en recherche", elle pousse parfois un cri de souffrance (geste de 1,5 s, sans s'arrêter), 12 s d'écart au moins.
    public const float DameWailMin = 12f, DameWailMax = 25f, DameWailDuration = 1.5f;

    // ---------- La Tour (entité ID 3, Cauchemar) ----------

    public const int TourDamage = 100;
    public const float TourWalkMultiplier = 0.7f;        // "modérément lente"
    public const float TourRunMultiplier = 1.4f;          // V0.4.6 : elle court (vers sa distance d'attaque, ou la localisation tampon)
    public const float TourAttackDistance = 20f;          // V0.4.6 : au-delà, elle court vers le joueur avant de canaliser
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
    // V0.4.6 : la caméra "le voit" s'il est à l'écran, sans mur entre eux, et à moins de 20 m du joueur.
    public const float FouSightRange = 20f;
    public const float FouStareTime = 1.5f;                 // regardé 1,5 s : il attaque (regard détourné avant : il danse)
    public const float FouSwapCooldown = 5f;                // après un échange, 5 s sans pouvoir lancer son attaque
    public const float FouChaseDuration = 5f;               // en chasse, il court vers le joueur, puis danse
    public const float FouKillDistance = 0.8f;
    public const float FouDanceDuration = 3f;
    public const float FouBellsTail = 1.4f;                 // les grelots ne se coupent pas net : écho planant

    // ---------- Le Roi (entité ID 8, Cauchemar, V0.4.4) ----------

    public const float RoiWalkMultiplier = 0.7f;            // "légèrement lente"
    public const float RoiWalkMin = 7f, RoiWalkMax = 15f;   // marche avant un arrêt
    public const float RoiPauseMin = 2f, RoiPauseMax = 5f;  // arrêt, totalement immobile
    public const float RoiTurnBackChance = 0.2f;            // V0.4.7 (avant : 10 %)
    public const float RoiBeaconCooldown = 40f;             // après avoir atteint une Balise
    // V0.4.7 : plus d'Abjuration ni d'aura blanche autour de lui ; les lumières à moins de 30 m clignotent frénétiquement.
    public const float RoiLightRange = 30f;
    public const float RoiSightRange = 20f;
    public const float RoiSenseRadius = 6f;                 // tout près, il sent le joueur même dans son dos
    public const float RoiAimFirst = 1.5f, RoiAimAgain = 1.5f; // V0.4.7 : il lève le bras 1,5 s (avant : 2,5 s la première fois)
    // "À grande vitesse" ; V0.4.6 : lancée seulement si le joueur est en ligne de vue, elle le suit jusqu'à lui.
    public const float RoiChainSpeed = 26f;                 // 20 m/s x 1,3
    public const float RoiChainRadius = 0.7f;               // la pointe touche le joueur à moins de 70 cm
    public const int RoiBreakPresses = 3;                   // appuis sur Espace pour rompre la chaîne (V0.4.6, avant : 5)
    // V0.4.7 : "vitesse normale x 2", soit le doublement déjà appliqué (avant : 2) : quatre fois sa vitesse de marche.
    public const float RoiPullMultiplier = 4f;
    public const float RoiChaseMultiplier = 0.5f;           // en chasse, il avance lentement vers sa proie
    public const float RoiRecover = 1f;
    public const float RoiKillDistance = 0.8f;

    // ---------- Le Cavalier (entité ID 9, Cauchemar, V0.4.7) ----------

    public const int CavalierDamage = 100;
    public const float CavalierWalkMultiplier = 1.2f;       // "modérément rapide" : moins vite que la course (x1,4)
    public const float CavalierRunMultiplier = 2.2f;        // quand le joueur traqué ne le regarde pas
    public const float CavalierSightRange = 20f;
    public const float CavalierSenseRadius = 6f;            // même dans son dos
    public const float CavalierGallopRange = 30f;
    public const float CavalierHitDistance = 0.8f;
    public const float CavalierChaseDistance = 40f;         // compteur de distance : au-delà, il abandonne
    public const float CavalierIgnore = 2f;                 // puis ne traque plus ce joueur pendant 2 s
    public const float CavalierStompMin = 6f, CavalierStompMax = 14f, CavalierStompDuration = 1.5f;
    public const float CavalierTurnBackChance = 0.2f;

    // ---------- Le Réveil (entité ID 10, Monticule de terre, V0.4.7) ----------

    public const int ReveilDamage = 50;
    public const float ReveilSpeedMultiplier = 1.05f;
    public const float ReveilWake = 0.8f;                   // bond sur place et mise en marche du mécanisme
    public const float ReveilChase = 5f;
    public const float ReveilBlastRadius = 2f;
    public const float ReveilKnockback = 9f;

    // ---------- Le Pion (entité ID 4, Normal) ----------

    public const int PionDamage = 30;
    public const float PionHabitat = 30f;
    public const float PionWatchDistance = 5f;
    public const float PionCuriousMin = 12f, PionCuriousMax = 15f;
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

    // ---------- Objets uniques (7.6 à 7.8, V0.4.7) ----------

    public const float TvHeight = 0.8f * 1.8f, TrampolineHeight = 0.3f * 1.8f, BellHeight = 0.8f * 1.8f;
    public const float TvSoundRange = 10f;
    public const float TvCountdown = 4f;              // décompte de 3 à 0, une seconde par chiffre
    // Trampoline : un peu moins que la hauteur entre le sol du Hub et le haut du Chapiteau.
    public const float TrampolineJumpFraction = 0.9f;
    // Cloche Décorative : son tintement s'entend (et obnubile) en ligne de vue, jusqu'à cette distance.
    public const float BellSightRange = 80f;

    // ---------- Pogo (chapitre 4) ----------

    public const float PogoIdleClose = 10f;    // sans achat pendant 10 s, il rentre dans sa boîte
    public const float PogoSmile = 3f;         // il sourit 3 s après un achat
    public const float PogoCooldown = 5f;      // après avoir frappé, impossible de l'appeler pendant 5 s
}

public enum ItemType
{
    None = 0, Croquette = 1, Canard = 2, PostIt = 3, Cle = 4, Boussole = 5, Balise = 6, Chargeur = 7, Teleporteur = 8, BaliseAmelioree = 9,
    StationCurative = 10, Os = 11, Canon = 12, Television = 13, Trampoline = 14, ClocheDecorative = 15,
    // Tirage d'un monticule seulement (pas un objet) : Le Réveil en sort.
    Reveil = 99,
}

public class ItemDef
{
    public ItemType type;
    public int terminalId; // ID affiché et tapé au Terminal (0 = pas en vente)
    public string otherId; // ID des "Autres objets" (O1 à O5), pour /give
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
        // Objets obtenables au Terminal (ID 1 à 10), dans l'ordre du document.
        new ItemDef { type = ItemType.Croquette, terminalId = 1, name = "Croquette", shortName = "Croquette", buyPrice = 20, sellPrice = 1, color = new Color(0.62f, 0.4f, 0.22f) },
        new ItemDef { type = ItemType.Balise, terminalId = 2, name = "Balise", shortName = "Balise", buyPrice = 30, sellPrice = 1, color = new Color(0.3f, 0.6f, 1f) },
        new ItemDef { type = ItemType.PostIt, terminalId = 3, name = "Post-It", shortName = "Post-It", buyPrice = 5, sellPrice = 1, color = new Color(1f, 0.93f, 0.35f) },
        new ItemDef { type = ItemType.Cle, terminalId = 4, name = "Clé", shortName = "Clé", buyPrice = 200, sellPrice = 1, color = new Color(0.55f, 0.8f, 1f) },
        new ItemDef { type = ItemType.Boussole, terminalId = 5, name = "Boussole", shortName = "Boussole", buyPrice = 100, sellPrice = 0, color = new Color(0.9f, 0.9f, 0.9f), inventory = false },
        new ItemDef { type = ItemType.Chargeur, terminalId = 6, name = "Chargeur", shortName = "Chargeur", buyPrice = 50, sellPrice = 1, color = new Color(0.35f, 0.8f, 1f) },
        new ItemDef { type = ItemType.Teleporteur, terminalId = 7, name = "Téléporteur", shortName = "Téléport.", buyPrice = 200, sellPrice = 1, color = new Color(1f, 0.7f, 0.15f) },
        new ItemDef { type = ItemType.BaliseAmelioree, terminalId = 8, name = "Balise Améliorée", shortName = "Balise +", buyPrice = 50, sellPrice = 1, color = new Color(1f, 0.3f, 0.3f) },
        new ItemDef { type = ItemType.StationCurative, terminalId = 9, name = "Station Curative", shortName = "Station", buyPrice = 150, sellPrice = 1, color = new Color(0.4f, 1f, 0.45f) },
        new ItemDef { type = ItemType.Canon, terminalId = 10, name = "Canon", shortName = "Canon", buyPrice = 200, sellPrice = 1, color = new Color(0.9f, 0.2f, 0.2f) },
        // Autres objets (V0.4.6 : trouvés dans les monticules ; V0.4.7 : objets uniques posés dans la Zone 1 à la génération).
        new ItemDef { type = ItemType.Canard, otherId = "O1", name = "Canard en Plastique", shortName = "Canard", buyPrice = 0, sellPrice = 30, color = new Color(1f, 0.84f, 0.15f) },
        new ItemDef { type = ItemType.Os, otherId = "O2", name = "Os", shortName = "Os", buyPrice = 0, sellPrice = 20, color = new Color(0.93f, 0.9f, 0.8f) },
        new ItemDef { type = ItemType.Television, otherId = "O3", name = "Télévision", shortName = "Télé", buyPrice = 0, sellPrice = 50, color = new Color(0.55f, 0.5f, 0.45f) },
        new ItemDef { type = ItemType.Trampoline, otherId = "O4", name = "Trampoline", shortName = "Trampo.", buyPrice = 0, sellPrice = 50, color = new Color(0.3f, 0.5f, 1f) },
        new ItemDef { type = ItemType.ClocheDecorative, otherId = "O5", name = "Cloche Décorative", shortName = "Cloche", buyPrice = 0, sellPrice = 50, color = new Color(0.85f, 0.7f, 0.3f) },
    };

    // Modèle 3D (Resources/Art) d'un objet posé au sol ou porté par un Pion.
    public static string ModelName(ItemType type) =>
        type == ItemType.Canard ? "DuckSolo" : type == ItemType.Os ? "Os" : "Croquette";

    // Tableaux des monticules (chapitre 9, V0.4.7) : objet trouvé et chance d'obtention, identiques dans les deux zones.
    // ItemType.Reveil n'est pas un objet : l'entité Le Réveil sort du monticule.
    public static readonly (ItemType type, float chance)[] MoundLoot = { (ItemType.Os, 0.85f), (ItemType.Canard, 0.1f), (ItemType.Reveil, 0.05f) };

    public static ItemType RollMound(System.Random rnd)
    {
        double roll = rnd.NextDouble();
        foreach (var (type, chance) in MoundLoot)
        {
            if (roll < chance) return type;
            roll -= chance;
        }
        return MoundLoot[MoundLoot.Length - 1].type;
    }

    // Valeur moyenne d'un monticule (sert à en générer assez pour le quota).
    public static float MoundValue()
    {
        float v = 0f;
        foreach (var (type, chance) in MoundLoot) v += (Get(type)?.sellPrice ?? 0) * chance;
        return v;
    }

    // Mort d'un joueur (3.4) : les objets du Terminal sont détruits, les autres (avec un modèle 3D) restent à terre.
    public static bool DropsOnDeath(ItemType type)
    {
        var def = Get(type);
        return def != null && def.buyPrice <= 0 && (type == ItemType.Canard || type == ItemType.Os);
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

    // ID du Terminal ("1" à "10") ou des autres objets ("O1" à "O5"), sans tenir compte de la casse. Un zéro tapé à la place
    // de la lettre O ("04") désigne aussi un autre objet : les ID du Terminal ne commencent jamais par 0.
    public static ItemDef ByAnyId(string id)
    {
        id = id.Trim();
        if (id.Length > 1 && id[0] == '0') id = "O" + id.Substring(1);
        if (int.TryParse(id, out int n)) return ByTerminalId(n);
        foreach (var d in All)
            if (d.otherId != null && string.Equals(d.otherId, id, System.StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    // Objets uniques (V0.4.7) : posés au sol à la manière d'une Balise, Hub compris.
    public static bool IsUnique(ItemType type) => type == ItemType.Television || type == ItemType.Trampoline || type == ItemType.ClocheDecorative;

    public static System.Collections.Generic.IEnumerable<ItemDef> Buyable()
    {
        foreach (var d in All) if (d.buyPrice > 0) yield return d;
    }
}
