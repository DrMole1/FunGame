using UnityEngine;

// Variables numériques du document de Game Design (V0.6.1).
public static class GameRules
{
    // Version du jeu, affichée en bas à droite du lobby.
    public const string Version = "V0.6.5";

    public const int MaxPlayers = 8;
    public const int MaxHp = 100;
    public const int MaxSerenity = 100;
    public const float HubRegenPerSecond = 5f;
    // Non précisé dans le GDD (validé en V0.4.10) : hors Couvre Feu, la sérénité remonte de 2 points par seconde
    // partout (Hub compris), pour qu'un personnage tombé à 0 ne perde pas ses PV indéfiniment.
    public const float CalmSerenityRegenPerSecond = 2f;
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

    // Premier quota à 500 (exemple du document), +200 à chaque nouvelle manche.
    public static int QuotaForRound(int round) => 500 + (round - 1) * 200;

    // ---------- Évolution du terrain (chapitre 10, V0.6) ----------

    // Génération d'objets de valeur d'une zone, en pourcentage du quota : 120 + 30 x nZone - 10 x nManche (jamais négative).
    public static float ValuePercent(int round, int zone) => Mathf.Max(0f, 120f + 30f * zone - 10f * round) / 100f;
    // V0.6 : à chaque manche, 2 entités de type "Normal" et 2 de type "Cauchemar" apparaissent par zone (choisies au hasard),
    // et celles de la manche précédente disparaissent.
    public const int NormalsPerZone = 2;
    public const int NightmaresPerZone = 2;

    // Notes 01 et 02 du chapitre 10 : génération d'objets de valeur x3 seul, x2 à deux, x1,5 à trois, x1,2 à quatre.
    public static float ValueBoost(int players) => players <= 1 ? 3f : players == 2 ? 2f : players == 3 ? 1.5f : players == 4 ? 1.2f : 1f;

    // V0.4.6 : les objets de valeur sont dans des monticules ; leur nombre tient compte des chances d'obtention.
    public static int MoundsForRound(int round, int zone, float boost) =>
        Mathf.CeilToInt(QuotaForRound(round) * ValuePercent(round, zone) * boost / Items.MoundValue(zone));

    // Monticules (note 02 du chapitre 7) : creuser prend 1,2 seconde.
    public const float DigDuration = 1.2f;
    // Avant de creuser, le personnage s'avance de lui-même jusqu'à 0,4 m du monticule (centre à centre, à l'horizontale).
    public const float DigApproach = 0.4f;

    // Manche 1 : les entités de type Normal apparaissent après 2 minutes de jeu, et un seul Cauchemar, dans la Zone 1, après
    // 3 minutes, sans prévenir les joueurs.
    public const int LateSpawnRound = 1;
    public const float LateNightmareDelay = 3f * 60f;
    public const float LateNormalsDelay = 2f * 60f;

    // ---------- Le Couvre Feu (chapitre 10.1, ex-Abjuration) ----------

    // V0.6 : il apparaît quand le Compteur de Temps affiche 3 minutes (avant : 7), jusqu'à la manche suivante.
    public const float CurfewStart = 3f * 60f;
    public const float CurfewLossPerSecond = 3f;   // sérénité perdue par seconde au contact de la brume (V0.5, avant : 6)
    // Note 02 du chapitre 3.4 (V0.4.10) : à 0 de sérénité, le personnage perd 15 PV par seconde (au lieu de mourir).
    public const int DespairDamagePerSecond = 15;
    // Note 02 (V0.4.2) : hors de la brume, Hub compris, la sérénité remonte de 10 points par seconde.
    public const float CurfewRegenPerSecond = 10f;
    public const float MistSpeedMultiplier = 0.8f; // déplacement ralenti de 20 % dans la brume
    public const float MistDeepSpeedMultiplier = 0.68f; // Note 03 (V0.4.11) : immergé jusqu'au cou, ralenti de 32 %
    // Hauteur (absolue) du dessus de la brume : le ras du sol du Hub.
    public const float MistTop = 0.5f;
    // Note 07 du chapitre 14 (V0.6) : chaque fois qu'un joueur est obnubilé par un son de cloche, il perd 20 de sérénité.
    public const int BellObsessionSerenity = 20;

    // La Dame, La Tour et Le Roi sont 25 % plus grands que la taille de leur modèle (3,33 m) ; le labyrinthe garde au moins
    // 3,6 m sous plafond partout où ils passent (couloirs 4 m, porte de la Zone 2 3,6 m).
    public const float BigEntityScale = 1.25f;

    // Note 01 du chapitre 8 (V0.4.6) : aucune entité (hors piège) n'apparaît à moins de 20 m d'un joueur.
    public const float EntitySpawnClearance = 20f;

    // Note 03 du chapitre 3.4 (V0.4.3) : tué par un Cauchemar, la caméra fixe ses yeux environ 1,5 seconde.
    public const float NightmareStareDuration = 1.5f;
    // GDD 12 (V0.4.10) : le son de mort (Perso_Mort) se joue 1 s après la chute à 0 PV.
    public const float DeathSoundDelay = 1f;

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
    public const float TourRunLimit = 20f;                // temps de course cumulé sur un même joueur, puis elle abandonne
    public const float TourIgnore = 6f;                   // puis ne le prend plus pour cible pendant 6 s
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
    // V0.4.7 : plus de brume ni d'aura blanche autour de lui ; les lumières à moins de 30 m clignotent frénétiquement.
    public const float RoiLightRange = 30f;
    public const float RoiAmbienceRange = 30f;              // V0.4.9 : musique d'ambiance (Roi_Autour) autour de lui
    public const float RoiSightRange = 20f;
    public const float RoiSenseRadius = 6f;                 // tout près, il sent le joueur même dans son dos
    public const float RoiAimFirst = 1.5f, RoiAimAgain = 1f; // V0.4.7 : il lève le bras 1,5 s ; V0.4.9 : chaîne relancée 1 s après la rupture (avant : 1 + 1,5 s)
    // "À grande vitesse" ; V0.4.6 : lancée seulement si le joueur est en ligne de vue, elle le suit jusqu'à lui.
    public const float RoiChainSpeed = 26f;                 // 20 m/s x 1,3
    public const float RoiChainRadius = 0.7f;               // la pointe touche le joueur à moins de 70 cm
    public const int RoiBreakPresses = 3;                   // appuis sur Espace pour rompre la chaîne (V0.4.6, avant : 5)
    // V0.4.7 : "vitesse normale x 2", soit le doublement déjà appliqué (avant : 2) : quatre fois sa vitesse de marche.
    public const float RoiPullMultiplier = 4f;
    public const float RoiChaseMultiplier = 0.5f;           // en chasse, il avance lentement vers sa proie
    public const float RoiKillDistance = 0.8f;

    // ---------- Le Cavalier (entité ID 9, Cauchemar, V0.4.7) ----------

    public const int CavalierDamage = 100;
    public const float CavalierWalkMultiplier = 1.2f;       // "modérément rapide" : moins vite que la course (x1,4)
    public const float CavalierRunMultiplier = 3.5f;        // quand le joueur traqué ne le regarde pas (V0.4.8, avant : 2,2)
    public const float CavalierSightRange = 20f;
    public const float CavalierSenseRadius = 6f;            // même dans son dos
    public const float CavalierGallopRange = 30f;
    public const float CavalierHitDistance = 0.8f;
    public const float CavalierChaseDistance = 50f;         // compteur de distance : au-delà, il abandonne (V0.4.8, avant : 40)
    public const float CavalierFaceDistance = 3f;           // plus près, le personnage doit en plus lui faire face (V0.4.10, avant : 7)
    public const float CavalierFaceAngle = 60f;             // écart maximal entre l'orientation du personnage et le Cavalier
    public const float CavalierIgnore = 2f;                 // puis ne traque plus ce joueur pendant 2 s
    public const float CavalierStompMin = 6f, CavalierStompMax = 14f, CavalierStompDuration = 1.5f;
    public const float CavalierTurnBackChance = 0.2f;

    // ---------- Le Réveil (entité ID 10, Monticule de terre, V0.4.7) ----------

    public const int ReveilDamage = 50;
    public const float ReveilSpeedMultiplier = 1.2f;        // V0.6 (avant : 1,05)
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

    // V0.6 : plus de dégâts toutes les 2 secondes ; arrivée à 0,4 m de sa proie, elle explose et tue les joueurs à moins de 2 m.
    public const int ClocheDamage = 100;
    public const float ClocheSightRange = 25f;           // V0.4.9 (avant : 50 m)
    public const float ClocheStopDistance = 0.4f;
    public const float ClocheBlastRadius = 2f;
    public const float ClocheKnockback = 8f;
    public const float ClocheSearchMin = 50f, ClocheSearchMax = 70f;
    public const float ClocheSearchMultiplier = 1.5f;

    // ---------- L'Araignée (entité ID 6, Couvre Feu) ----------

    // Ajout V0.6.5 (règle prédominante) : aucune Araignée lors du Couvre-Feu, jusqu'à une prochaine version qui le mentionnera.
    public const bool SpidersEnabled = false;
    public const int SpiderDamage = 50;
    // V0.5 : Araignées par zone déverrouillée selon le nombre de joueurs (3 seul, 4 de 2 à 4 joueurs, 5 de 5 à 8).
    public static int SpidersPerZone(int players) => players <= 1 ? 3 : players <= 4 ? 4 : 5;
    public const float SpiderSpawnClearance = 25f;
    public const float SpiderSightRange = 12f;
    public const float SpiderProwlMultiplier = 0.5f;
    public const float SpiderRushMultiplier = 2.6f;      // "très rapidement"
    public const float SpiderAttackDistance = 0.8f;
    public const float SpiderPause = 1f, SpiderPursuit = 10f;
    public const float SpiderKnockback = 7f;
    // Une action bruyante (aboiement, saut, objet ramassé ou posé) trahit un joueur accroupi pendant ce délai.
    public const float NoiseMemory = 0.6f;

    // ---------- La Tête de Cloche (entité ID 11, Sanction, V0.6) ----------

    public const int TeteDamage = 100;
    public const float TeteSpawnDistance = 30f;            // elle apparaît à 30 m du joueur sanctionné
    public const float TeteSteadyTime = 7f, TeteFullSpeedTime = 20f, TeteMaxMultiplier = 2f;
    public const float TeteKillDistance = 0.8f;
    public const float TeteLeaveDistance = 20f;            // elle disparaît à 20 m du cadavre
    public const float TeteLeaveMultiplier = 0.4f;         // "en se déplaçant lentement"
    public const float TeteRingSlow = 3f, TeteRingFast = 0.5f, TeteRingNear = 5f;
    public const float TeteSightRange = 30f;               // obnubile les joueurs en vision directe
    public const float TeteKillHold = 0.63f;               // immobilisé, puis le coup mortel
    public const float TeteKillAnimSpeed = 1f / 0.7f;      // animation d'attaque accélérée pour tenir dans ce délai
    public const float TeteStareDuration = 1.05f;          // la caméra de la victime plonge dans ses yeux

    // Vitesse de la Tête de Cloche (multiple de la marche d'un joueur) après 't' secondes de traque : celle du joueur les
    // 7 premières secondes, puis elle accélère chaque seconde jusqu'à 2x au bout de 20 secondes.
    public static float TeteSpeed(float t) =>
        t < TeteSteadyTime ? 1f : Mathf.Lerp(1f, TeteMaxMultiplier, Mathf.Clamp01(Mathf.Floor(t - TeteSteadyTime + 1f) / (TeteFullSpeedTime - TeteSteadyTime)));

    // Délai entre deux coups de marteau sur sa tête : 3 s de loin, jusqu'à 0,5 s à moins de 5 m du joueur traqué.
    public static float TeteRingDelay(float distance) =>
        Mathf.Lerp(TeteRingFast, TeteRingSlow, Mathf.InverseLerp(TeteRingNear, TeteSpawnDistance, distance));

    // ---------- Terrain (chapitre 9, V0.6.5) ----------

    // Hauteur maximale d'une salle : nZone x 10, sans excéder 50 mètres.
    public static float MaxRoomHeight(int zone) => Mathf.Min(10f * zone, 50f);
    // Note 01 du chapitre 10.3 : au moins 2 tunnels en Zone 2, 3 en Zone 3, 4 à partir de la Zone 4.
    public static int MinTunnels(int zone) => zone < 2 ? 0 : Mathf.Min(zone, 4);
    // Salles de plus de 21 m : la moitié seulement ont les grandes plateformes (au moins 10 m²).
    public const float LargePlatformChance = 0.5f;
    // Chapitre 9.2 : à partir de cette zone impaire débloquée, les zones trop en arrière sont fermées puis désactivées.
    public const int FirstSealingZone = 7;

    // ---------- Glyphes (chapitre 9, V0.6) ----------

    public const int GlyphTypes = 3;
    // Glyphes neutres d'une zone impaire : un de chaque type, à au moins 40 m les uns des autres.
    public const float NeutralGlyphSpacing = 40f;
    public static int NeutralGlyphId(int zone, int type) => zone * 100 + 50 + type;
    // Durée de l'ordre de chaque glyphe (immobile et accroupi, aller à la niche, aboyer).
    public static readonly float[] GlyphDuration = { 10f, 30f, 10f };
    public const float GlyphSitGrace = 2.5f;     // "Assis !" : au plus 2,5 secondes de suite sans être immobile et accroupi (V0.6.3)
    public const float GlyphBarkGap = 1.5f;      // "Aboie !" : au plus 1,5 seconde sans aboyer
    // Glyphes d'acquisition d'une zone paire : 3 dans la Zone 2 (un de chaque), puis 2 + nZone / 2, 6 au plus (V0.6.5).
    public const int MaxAcquisitionGlyphs = 6;
    public static int AcquisitionGlyphs(int zone) => zone <= 2 ? 3 : Mathf.Min(MaxAcquisitionGlyphs, 2 + zone / 2);

    // ---------- Arcades (chapitre 10.2, V0.6.1) ----------

    // Dès que la Zone 3 est accessible, vendre à Pogo un objet des monticules peut rapporter un Jeton WOOF
    // (V0.6.3 : chance = zone la plus lointaine atteinte au cours de la partie x 3 %).
    public static float TokenChance(int lastZone) => lastZone * 0.03f;
    // Bornes d'arcade : à partir de la Zone 3, nZone - 1 par zone, 6 au plus (V0.6.5) ; aucune dans les zones 1 et 2.
    public const int MaxArcadesPerZone = 6;
    public static int ArcadesInZone(int zone) => zone < 3 ? 0 : Mathf.Min(MaxArcadesPerZone, zone - 1);
    public const float ArcadeMusicRange = 10f;    // petite musique de foire d'une borne chargée
    public const float DiscoMusicRange = 10f;     // musique disco d'une boule à facettes posée
    // Roulette : 20 sections égales ; 9 tickets 50, 5 tickets 100, 3 tickets 200, 1 ticket 500 et 2 Réveils.
    public const int RouletteSections = 20;
    public const float RouletteSpin = 7f;
    // Machine à pince : 5 peluches à chaque nouvelle manche (lapin 50 %, pingouin 25 %, singe 15 %, chat 10 %).
    public const int ClawPlushes = 5;
    public static readonly float[] PlushChances = { 0.5f, 0.25f, 0.15f, 0.1f };
    // Arcade Dance : 8 flèches, 2 secondes pour chacune ; 0 erreur : diamant, 1 : or, 2 : argent, 3 et plus : rien.
    public const int DanceArrows = 8;
    public const float DanceArrowTime = 2f;

    // ---------- Balises (chapitre 7.1) ----------

    // Charge de la Balise du Hub (au début du jeu, et après chaque Chargeur).
    public const float BeaconCharge = 10f * 60f;
    // Balise Améliorée (7.3) : ni Couvre Feu ni apparition d'entité dans ce rayon.
    public const float EnhancedBeaconRadius = 5f;

    // ---------- Téléporteur (7.2) et Station Curative (7.4) ----------

    public const float DeviceRange = 1f;
    public const float TeleportChannel = 3f;
    public const float TeleportSpeed = 70f;   // boule d'énergie le long des liens
    public const float StationHealPerSecond = 10f;

    // ---------- Objets uniques (7.6 à 7.8, V0.4.7) ----------

    public const float TvHeight = 0.8f * 1.8f, TrampolineHeight = 0.3f * 1.8f, BellHeight = 0.8f * 1.8f;
    public const float TvSoundRange = 10f;
    public const float TvMusicDuckRange = 7f;         // plus près d'une Télévision qui diffuse : les musiques d'ambiance s'effacent
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
    None = 0, CuisseDePoulet = 1, Canard = 2, PostIt = 3, Cle = 4, Boussole = 5, Balise = 6, Chargeur = 7, Teleporteur = 8, BaliseAmelioree = 9,
    StationCurative = 10, Os = 11, Canon = 12, Television = 13, Trampoline = 14, ClocheDecorative = 15,
    // V0.6.1 (chapitre 10.2, Arcades) : Jeton WOOF, lots de la Roulette, de la Machine à pince et de l'Arcade Dance.
    JetonWoof = 16, Ticket50 = 17, Ticket100 = 18, Ticket200 = 19, Ticket500 = 20,
    PelucheLapin = 21, PeluchePingouin = 22, PelucheSinge = 23, PelucheChat = 24,
    BouleArgent = 25, BouleOr = 26, BouleDiamant = 27,
    // V0.6.3 (O7) : Canard en Plastique Coloré. Le type réel est CanardColore + nZone / 2 (101 : Zone 2, 102 : Zone 4...),
    // ce qui lui donne sa couleur et sa valeur sans rien transmettre de plus sur le réseau.
    CanardColore = 100,
    // Tirage d'un monticule seulement (pas un objet) : Le Réveil en sort.
    Reveil = 99,
}

public class ItemDef
{
    public ItemType type;
    public int terminalId; // ID affiché et tapé au Terminal (0 = pas en vente)
    public string otherId; // ID des "Autres objets" (O1 à O5), pour /give
    // Noms en français (clés de traduction) ; name et shortName les donnent dans la langue choisie.
    public string frName, frShortName;
    public string name { get => Loc.T(frName); set => frName = value; }
    public string shortName { get => Loc.T(frShortName); set => frShortName = value; }
    public int buyPrice;   // 0 = non achetable au Terminal
    public int sellPrice;
    public Color color;
    public bool inventory = true; // la Boussole ne se garde pas dans l'inventaire
}

public static class Items
{
    // Note 05 du chapitre 7 (V0.5.1) : l'objet 1 est une Cuisse de poulet (ce n'est plus une croquette).
    public const int CuisseHeal = 40;
    public const int CuisseSerenity = 40;
    public const int PostItMaxChars = 20;
    public const float PostItLockSeconds = 2f; // un Post-It ne peut pas être retiré juste après avoir été accroché

    // V0.6.3 : un Canard Coloré par palier (nZone / 2) : valeur 25 + palier x 25, couleur de la Balise du Hub de la zone précédente.
    public const int MaxDuckTier = 120;
    static readonly System.Collections.Generic.Dictionary<int, ItemDef> coloredDucks = new System.Collections.Generic.Dictionary<int, ItemDef>();

    static ItemDef ColoredDuck(int tier)
    {
        if (!coloredDucks.TryGetValue(tier, out var def))
            coloredDucks[tier] = def = new ItemDef
            {
                type = ItemType.CanardColore + tier, otherId = tier == 1 ? "O7" : null, name = "Canard en Plastique Coloré", shortName = "Canard col.",
                buyPrice = 0, sellPrice = 25 + tier * 25, color = Beacon.HubColor(tier),
            };
        return def;
    }

    public static bool IsColoredDuck(ItemType type) => type > ItemType.CanardColore && type <= ItemType.CanardColore + MaxDuckTier;
    public static bool IsDuck(ItemType type) => type == ItemType.Canard || IsColoredDuck(type);
    public static int DuckTier(ItemType type) => type - ItemType.CanardColore;
    public static ItemType ColoredDuckOf(int zone) => ItemType.CanardColore + Mathf.Clamp(zone / 2, 1, MaxDuckTier);

    static readonly ItemDef[] All =
    {
        // Objets obtenables au Terminal (ID 1 à 10), dans l'ordre du document.
        new ItemDef { type = ItemType.CuisseDePoulet, terminalId = 1, name = "Cuisse de poulet", shortName = "Cuisse", buyPrice = 20, sellPrice = 1, color = new Color(0.62f, 0.4f, 0.22f) },
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
        // V0.6.1 : Jeton WOOF (donné par Pogo), seul lot de l'arcade à figurer au tableau des autres objets.
        new ItemDef { type = ItemType.JetonWoof, otherId = "O6", name = "Jeton WOOF", shortName = "Jeton", buyPrice = 0, sellPrice = 1, color = new Color(1f, 0.8f, 0.2f) },
        // V0.6.3 : O7, un exemplaire pour /give (celui de la Zone 2) ; les autres paliers sont créés à la demande.
        ColoredDuck(1),
        // Lots des bornes d'arcade (10.2), échangeables à Pogo.
        new ItemDef { type = ItemType.Ticket50, name = "Ticket 50", shortName = "Ticket 50", buyPrice = 0, sellPrice = 50, color = new Color(0.3f, 0.85f, 0.35f) },
        new ItemDef { type = ItemType.Ticket100, name = "Ticket 100", shortName = "Ticket 100", buyPrice = 0, sellPrice = 100, color = new Color(0.3f, 0.55f, 1f) },
        new ItemDef { type = ItemType.Ticket200, name = "Ticket 200", shortName = "Ticket 200", buyPrice = 0, sellPrice = 200, color = new Color(0.7f, 0.35f, 1f) },
        new ItemDef { type = ItemType.Ticket500, name = "Ticket 500", shortName = "Ticket 500", buyPrice = 0, sellPrice = 500, color = new Color(1f, 0.78f, 0.2f) },
        new ItemDef { type = ItemType.PelucheLapin, otherId = "O11", name = "Lapin en peluche", shortName = "Lapin", buyPrice = 0, sellPrice = 50, color = new Color(0.95f, 0.95f, 0.95f) },
        new ItemDef { type = ItemType.PeluchePingouin, otherId = "O12", name = "Pingouin en peluche", shortName = "Pingouin", buyPrice = 0, sellPrice = 100, color = new Color(0.4f, 0.5f, 0.65f) },
        new ItemDef { type = ItemType.PelucheSinge, otherId = "O13", name = "Singe en peluche", shortName = "Singe", buyPrice = 0, sellPrice = 200, color = new Color(0.55f, 0.35f, 0.2f) },
        new ItemDef { type = ItemType.PelucheChat, otherId = "O14", name = "Chat en peluche", shortName = "Chat", buyPrice = 0, sellPrice = 500, color = new Color(0.7f, 0.7f, 0.72f) },
        new ItemDef { type = ItemType.BouleArgent, otherId = "O8", name = "Boule à facettes argent", shortName = "Boule arg.", buyPrice = 0, sellPrice = 50, color = new Color(0.8f, 0.82f, 0.86f) },
        new ItemDef { type = ItemType.BouleOr, otherId = "O9", name = "Boule à facettes or", shortName = "Boule or", buyPrice = 0, sellPrice = 70, color = new Color(1f, 0.8f, 0.3f) },
        new ItemDef { type = ItemType.BouleDiamant, otherId = "O10", name = "Boule à facettes diamant", shortName = "Boule diam.", buyPrice = 0, sellPrice = 100, color = new Color(0.7f, 0.95f, 1f) },
    };

    // Teinte de chaque lot de l'arcade (un même modèle Meshy pour les quatre tickets et pour les trois boules à facettes).
    public static Color Tint(ItemType type)
    {
        switch (type)
        {
            case ItemType.Ticket50: return new Color(0.45f, 1f, 0.45f);
            case ItemType.Ticket100: return new Color(0.45f, 0.65f, 1f);
            case ItemType.Ticket200: return new Color(0.78f, 0.45f, 1f);
            case ItemType.Ticket500: return new Color(1f, 0.82f, 0.25f);
            case ItemType.BouleOr: return new Color(1f, 0.78f, 0.3f);
            case ItemType.BouleDiamant: return new Color(0.75f, 0.95f, 1.15f);
            default: return Color.white;
        }
    }

    // Apparence d'un objet (à terre, posé ou porté par un Pion) : teinte des lots, couleur des Canards Colorés.
    public static void ApplyLook(Transform visual, ItemType type)
    {
        if (IsColoredDuck(type))
        {
            Material duck = ColoredDuckMaterial(DuckTier(type));
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = duck;
            return;
        }
        Color tint = Tint(type);
        if (tint == Color.white) return;
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
            r.sharedMaterial = new Material(r.sharedMaterial) { color = tint * (IsDiscoBall(type) ? 1.7f : 1f) };
    }

    // Texture du canard désaturée (menu "FunGame/Préparer les modèles V0.6.3"), teintée par la couleur du Hub. Le doré est
    // métallique : il ne se confond pas avec le simple canard jaune.
    static Material ColoredDuckMaterial(int tier)
    {
        int colorIndex = (tier - 1) % 4;
        bool gold = colorIndex == 3;
        Material m = GameArt.Lit("CanardColore" + colorIndex, Color.Lerp(Beacon.HubColor(tier), Color.white, 0.08f) * 1.25f, Resources.Load<Texture2D>("Art/DuckGris"), gold ? 0.85f : 0.6f);
        if (gold) m.SetFloat("_Metallic", 0.9f);
        return m;
    }

    public static bool IsTicket(ItemType type) => type >= ItemType.Ticket50 && type <= ItemType.Ticket500;
    public static bool IsPlush(ItemType type) => type >= ItemType.PelucheLapin && type <= ItemType.PelucheChat;
    public static bool IsDiscoBall(ItemType type) => type >= ItemType.BouleArgent && type <= ItemType.BouleDiamant;
    // Lot gagné à une borne : il scintille de petites particules, posé à terre devant elle.
    public static bool IsPrize(ItemType type) => IsTicket(type) || IsPlush(type) || IsDiscoBall(type);

    // Modèle 3D (Resources/Art) d'un objet posé au sol ou porté par un Pion.
    public static string ModelName(ItemType type)
    {
        switch (type)
        {
            case ItemType.Canard: return "DuckSolo";
            case ItemType.Os: return "Os";
            case ItemType.CuisseDePoulet: return "CuisseDePoulet";
            case ItemType.JetonWoof: return "JetonWoof";
            case ItemType.PelucheLapin: return "PelucheLapin";
            case ItemType.PeluchePingouin: return "PeluchePingouin";
            case ItemType.PelucheSinge: return "PelucheSinge";
            case ItemType.PelucheChat: return "PelucheChat";
            default:
                if (IsTicket(type)) return "Ticket";
                if (IsDiscoBall(type)) return "BouleFacettes";
                if (IsColoredDuck(type)) return "DuckSolo";
                return "Croquette";
        }
    }

    // Note 05 du chapitre 14 (V0.5.1) : la monnaie est la croquette (ce ne sont plus des golds). "1 croquette", "30 croquettes".
    public static string Price(int amount) => Loc.Price(amount);

    // Tableaux des monticules (chapitre 9, V0.6.3) : objet trouvé et chance d'obtention, selon la parité de la zone.
    // ItemType.Reveil n'est pas un objet : l'entité Le Réveil sort du monticule.
    static readonly (ItemType type, float chance)[] OddMoundLoot = { (ItemType.Os, 0.85f), (ItemType.Canard, 0.1f), (ItemType.Reveil, 0.05f) };
    static readonly (ItemType type, float chance)[] EvenMoundLoot = { (ItemType.Os, 0.1f), (ItemType.Canard, 0.4f), (ItemType.CanardColore, 0.45f), (ItemType.Reveil, 0.05f) };

    static (ItemType type, float chance)[] MoundLoot(int zone) => zone % 2 == 0 ? EvenMoundLoot : OddMoundLoot;
    // Le Canard Coloré d'un tableau est celui de la zone du monticule.
    static ItemType InZone(ItemType type, int zone) => type == ItemType.CanardColore ? ColoredDuckOf(zone) : type;

    public static ItemType RollMound(System.Random rnd, int zone)
    {
        var loot = MoundLoot(zone);
        double roll = rnd.NextDouble();
        foreach (var (type, chance) in loot)
        {
            if (roll < chance) return InZone(type, zone);
            roll -= chance;
        }
        return loot[loot.Length - 1].type;
    }

    // Valeur moyenne d'un monticule de cette zone (sert à en générer assez pour le quota).
    public static float MoundValue(int zone)
    {
        float v = 0f;
        foreach (var (type, chance) in MoundLoot(zone)) v += (Get(InZone(type, zone))?.sellPrice ?? 0) * chance;
        return v;
    }

    // Mort d'un joueur (3.4) : les objets du Terminal sont détruits, les autres (avec un modèle 3D) restent à terre.
    // Non précisé dans le GDD (V0.6.1) : le Jeton WOOF et les tickets aussi (les peluches et boules à facettes, qui se
    // posent comme une Balise, sont posées près du corps, comme les objets uniques).
    public static bool DropsOnDeath(ItemType type)
    {
        var def = Get(type);
        return def != null && def.buyPrice <= 0 && (IsDuck(type) || type == ItemType.Os || type == ItemType.JetonWoof || IsTicket(type));
    }

    public static ItemDef Get(ItemType type)
    {
        if (IsColoredDuck(type)) return ColoredDuck(DuckTier(type));
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
    // Posés au sol comme une Balise, Hub compris : objets uniques, peluches et boules à facettes (V0.6.1), canards (V0.6.3).
    public static bool IsPlaceable(ItemType type) => IsUnique(type) || IsPlush(type) || IsDiscoBall(type) || IsDuck(type);
    // Note 01 du chapitre 10.2 (V0.6.3) : ils se balancent près d'une boule à facettes posée.
    public static bool Dances(ItemType type) => IsDuck(type) || IsPlush(type);
    // Objets ramassés dans les monticules (V0.6.1 : en vendre un à Pogo peut rapporter un Jeton WOOF).
    public static bool FromMound(ItemType type) => type == ItemType.Os || IsDuck(type);

    public static System.Collections.Generic.IEnumerable<ItemDef> Buyable()
    {
        foreach (var d in All) if (d.buyPrice > 0) yield return d;
    }
}
