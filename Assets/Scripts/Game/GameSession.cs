using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Boucle de jeu : Hubs, manches, quota, cagnotte, objets, Terminal, Chenils, logs, morts et réanimations,
// sérénité et Couvre Feu, entités (La Dame, La Tour, Le Pion, La Cloche, L'Araignée...), orbes et portes,
// Post-It, Boussole, Balises, Téléporteurs et Stations Curatives. Zones, glyphes et Sanction : GameSession.Zones.cs.
// L'hôte fait autorité sur l'état de la partie ; chaque client gère les PV, la sérénité et l'inventaire de son personnage.
// Messages (en plus de ceux de NetworkGame) :
//   G|graine|manche|quota|progression|cagnotte|phase|temps|boussole|orbes|balise|bonus|tests|zones|hubs|acquisitions
//                                                               hôte -> tous : état de la partie (orbes brisés, Hubs ouverts et
//                                                               portes de chapiteau ouvertes : numéros séparés par des points)
//   IL|manche|id,id...   IR|manche|id                          objets déjà ramassés / ramassé
//   PK|id -> GV|type (0 = refusé)                              ramassage
//   SL|type                                                     vente au Terminal
//   BY|type -> BO|ok|type|cagnotte[|raison]                     achat au Terminal
//   TU|1/0 -> TG|1/0, TS|id                                     prise / libération du Terminal
//   RV|cible|hub -> RA|cible|sauveteur|hub                      réanimation depuis un Chenil
//   DE|cause                                                    mort d'un joueur
//   LG|texte                                                    ligne de l'Écran de Logs
//   SC|manche|id,golds,morts;id,golds,morts...                  Tableau de Manche
//   PP|x|y|z|nx|ny|nz|texte -> PA|id|x|y|z|nx|ny|nz|texte       Post-It accroché
//   PD|id -> PX|id                                              Post-It retiré
//   OB|orbe|case -> OU|case (-1 = refusé)                       Clé utilisée sur un orbe (case -1 : godmod, sans Clé)
//   HB|hub                                                      Bouton Rouge d'un Hub 2+ : ouvrir sa porte vers la zone suivante
//   EN|t|id,type,x,y,z,lacet,état,cible,événements,données;...  hôte -> tous : entités, t = heure d'envoi (canal rapide)
//   LK|id|1/0                                                   le joueur chassé regarde (ou non) La Dame
//   EH|dégâts|x|z|recul|type                                    hôte -> joueur : une entité vous frappe depuis (x, z)
//   TA                                                          hôte -> joueur : La Tour vous a repéré (alerte)
//   PS|entité -> PT|entité|objet                                le Pion vole un objet ramassable (0 : rien à voler)
//   NZ                                                          joueur -> hôte : saut (action bruyante, pour L'Araignée)
//   DR|objet|x|y|z -> IA|id|objet|x|y|z                         objet laissé à terre (mort, Pion), ramassable comme les autres
//   BN|x|y|z|appareil / BG|id / BC                              joueur -> hôte : poser / ramasser un appareil, utiliser un Chargeur
//   BW|id|x|y|z|appareil|cap|joueur / BZ|id                     hôte -> tous : appareil posé / retiré (Balise, Balise Améliorée,
//                                                               Téléporteur, Station Curative, Canon, objets uniques)
//   JO / JS|objet / JE                                          joueur -> hôte : ouvrir Pogo / lui vendre un objet / cliquer les mains vides
//   JX|état|sourire|frappé|victimes / JK / JN|raison                    hôte -> tous : état de Pogo ; -> joueur : coup mortel / refus
//   DF|durée                                                    hôte -> joueur : La Dame vous attaque, immobilisé
//   SK|hub -> SR|bonus (-1 = refusé), SX|hub                    /skip round ou Bouton Skip (confettis à côté du bouton)
//   BK|pitch -> BA|id|pitch                                     aboiement d'un joueur
//   GE|geste -> GS|id|geste                                     geste d'un joueur (ramasser, poser, utiliser, manger)
//   MD|monticule -> MS|manche|monticule / MN|monticule          creuser un monticule : début (tous) ou refus (joueur)
//   MO|manche|monticule   ML|manche|id,id...                   hôte -> tous : monticule creusé (objet libéré) / déjà creusés
//   (Télévision, sujets de test, appareils déjà posés : voir GameSession.Objects.cs)
public partial class GameSession : MonoBehaviour, IEntityHost
{
    enum Phase { Waiting = 0, Running = 1, GameOver = 2 }

    static readonly Color HubAmbient = new Color(0.42f, 0.36f, 0.32f);
    // Hors du Hub : presque le noir complet (plus terrifiant).
    static readonly Color MazeAmbient = new Color(0.016f, 0.016f, 0.02f);
    static readonly Color MistFog = new Color(0.02f, 0.05f, 0.12f);
    // Voile lointain hors du Hub (rendu local) : les couloirs éloignés se fondent dans un gris à peine visible.
    static readonly Color FarFog = new Color(0.03f, 0.032f, 0.038f);
    const float HubFog = 0.01f, MazeFog = 0.11f;
    // Lampe du personnage, hors du Hub : un halo court autour de lui.
    const float PlayerLightIntensity = 0.75f;
    const float EntitySyncInterval = 1f / 20f;

    public static GameSession Instance { get; private set; }

    NetworkGame net;
    PlayerController player;
    PlayerStatus status;
    PlayerPresence presence;
    CharacterAvatar avatar;
    OrbitCamera orbit;
    Light playerLight;
    GameHud hud;
    TerminalUI terminal;
    PostItUI postItUI;

    Transform worldRoot, itemsRoot, notesRoot, entitiesRoot;
    // Hub 1 (celui du départ) ; tous les Hubs générés, dans l'ordre, avec leurs boutons et leur Pogo.
    WorldBuilder.HubRefs hub;
    readonly List<WorldBuilder.HubRefs> hubs = new List<WorldBuilder.HubRefs>();
    readonly List<RedButton> buttons = new List<RedButton>();
    readonly List<SkipButton> skipButtons = new List<SkipButton>();
    readonly List<Pogo> pogos = new List<Pogo>();
    RedButton button;
    Pogo pogo;
    BeaconNetwork beacons;
    // Chenils de chaque joueur, un par Hub (index : numéro du Hub - 1).
    readonly Dictionary<string, List<Kennel>> kennels = new Dictionary<string, List<Kennel>>();
    readonly Dictionary<int, ItemPickup> items = new Dictionary<int, ItemPickup>();
    readonly Dictionary<int, PostItNote> notes = new Dictionary<int, PostItNote>();
    readonly Dictionary<int, Entity> entities = new Dictionary<int, Entity>();
    // Objets laissés à terre (mort d'un joueur, Pion) : ids à partir de DropIdBase, objet, position et Pion voleur.
    const int DropIdBase = 100000;
    class Dropped { public ItemType type; public Vector3 pos; public int stolenBy = -1; }
    readonly Dictionary<int, Dropped> dropped = new Dictionary<int, Dropped>();
    int nextDropId;

    // État de la partie (autorité : hôte).
    int seed, builtSeed = -1, itemsRound = -1;
    // Génération d'objets de valeur multipliée selon le nombre de joueurs (x3 seul, x2 à deux, x1,5 à trois, x1,2 à quatre),
    // fixée par l'hôte.
    float valueBoost = 1f, itemsBoost = -1f;
    int round = 1, quota, progress, cash;
    Phase phase = Phase.Waiting;
    float timeLeft = GameRules.RoundDuration;
    bool compass;
    // Orbes brisés (numéros globaux), Hubs dont la porte vers la zone suivante est ouverte, zones paires dont la porte du
    // chapiteau est ouverte (V0.6).
    readonly HashSet<int> brokenOrbs = new HashSet<int>();
    readonly HashSet<int> openedHubs = new HashSet<int>();
    readonly HashSet<int> openedAcquisitions = new HashSet<int>();
    readonly HashSet<int> picked = new HashSet<int>();
    // Monticules de la manche (V0.4.6) : id = celui de l'objet qu'ils contiennent ; déjà creusés ; creusages en cours (hôte).
    readonly Dictionary<int, Mound> mounds = new Dictionary<int, Mound>();
    readonly Dictionary<int, (ItemType type, Vector3 pos)> moundLoot = new Dictionary<int, (ItemType, Vector3)>();
    readonly HashSet<int> dugMounds = new HashSet<int>();
    readonly Dictionary<int, float> digEnds = new Dictionary<int, float>();
    // Monticule creusé par le joueur local (-1 : aucun) et fin du creusage.
    int localDig = -1;
    float localDigEnd;
    readonly List<string> logs = new List<string>();
    string terminalUser = "";
    // Moment où l'hôte a confié le Terminal : le temps que l'état « Terminal ouvert » du joueur lui parvienne.
    float terminalGrantedAt = -100f;
    const float TerminalGrantGrace = 1.5f;
    // Tableau de Manche : golds gagnés et morts de chaque joueur pendant la manche en cours.
    class RoundStats { public int golds, deaths; }
    readonly Dictionary<string, RoundStats> roundStats = new Dictionary<string, RoundStats>();
    int statsRound = 1;
    // Post-It accrochés (hôte : message complet, pour les joueurs qui arrivent).
    readonly Dictionary<int, string> noteMessages = new Dictionary<int, string>();
    readonly Dictionary<int, float> notePlacedAt = new Dictionary<int, float>();
    int nextNoteId, nextEntityId;
    // Balises posées (hôte : message complet, pour les joueurs qui arrivent).
    readonly Dictionary<int, string> beaconMessages = new Dictionary<int, string>();
    int nextBeaconId;
    // Pogo (hôte) : humeur, dernière activité (achat ou ouverture), fin du sourire et de la pénitence.
    Pogo.Mood pogoMood;
    float pogoActiveAt, pogoSmileUntil, pogoCooldownUntil;
    int pogoKills;
    bool hostCurfew;

    float syncTimer, regenAcc, serenityRegenAcc, mistTimer, screenTimer, entityTimer;
    int pendingPickups;
    string spectateId;
    float mistAmount;
    float invincibleUntil; // fin de l'invincibilité qui suit un /tp
    float deathAt = -100f;  // mort du joueur local : la caméra s'élève au-dessus du corps avant de suivre un autre joueur
    readonly Queue<float> barkTimes = new Queue<float>();
    readonly Dictionary<int, bool> watching = new Dictionary<int, bool>();
    // Hôte : dernière action bruyante de chaque joueur (L'Araignée), vols du Pion en attente de réponse.
    readonly Dictionary<string, float> noiseAt = new Dictionary<string, float>();
    readonly Dictionary<int, System.Action<ItemType>> pendingSteals = new Dictionary<int, System.Action<ItemType>>();
    // Téléportation (joueur local) : canalisation près d'un Téléporteur chargé, puis voyage en boule d'énergie.
    Beacon channelPad;
    AudioSource channelSound;
    float channelTime, stationAcc;
    bool healing;
    List<Vector3> teleportPath;
    int teleportLeg;
    GameObject energyBall;
    public static bool Teleporting { get; private set; }

    public PlayerPresence LocalPresence => net.InGame ? presence : null;
    public bool LocalDead => status.Dead;
    public int LocalHp => status.Hp;
    public int Cash => cash;
    public bool IsHost => net.IsHost;
    public bool IsGameOver => phase == Phase.GameOver;
    public bool HasFreeSlot => status.FreeSlots - pendingPickups > 0;
    public bool CompassUnlocked => compass;
    public ItemType SelectedItem => status.Selected >= 0 ? status[status.Selected] : ItemType.None;
    public IEnumerable<Entity> Entities => entities.Values;
    bool CurfewActive => phase == Phase.Running && timeLeft <= GameRules.CurfewStart;

    public void Init(NetworkGame network, PlayerController localPlayer)
    {
        Instance = this;
        net = network;
        player = localPlayer;
        status = player.GetComponent<PlayerStatus>();
        presence = player.GetComponent<PlayerPresence>();
        avatar = player.GetComponent<CharacterAvatar>();
        orbit = FindObjectOfType<OrbitCamera>();
        status.Changed += RefreshHud;
        player.Jumped += () => net.ToHost("NZ");

        ConfigureRendering();
        Entity.Host = this;
        GameAudio.Init();
        player.gameObject.AddComponent<Footsteps>();
        worldRoot = new GameObject("Monde").transform;
        hub = WorldBuilder.BuildHub(worldRoot);
        beacons = BeaconNetwork.Create(worldRoot, hub.beaconPosition, hub.beaconYaw);
        AddHubFixtures(hub);
        button = buttons[0];
        pogo = pogos[0];
        itemsRoot = new GameObject("Objets").transform;
        itemsRoot.SetParent(worldRoot, false);
        notesRoot = new GameObject("PostIts").transform;
        notesRoot.SetParent(worldRoot, false);
        entitiesRoot = new GameObject("Entites").transform;
        entitiesRoot.SetParent(worldRoot, false);

        // Lampe portée par le joueur local, allumée hors du Hub.
        playerLight = new GameObject("LampeJoueur").AddComponent<Light>();
        playerLight.transform.SetParent(player.transform, false);
        playerLight.transform.localPosition = new Vector3(0f, 0.7f, 0.2f);
        playerLight.type = LightType.Point;
        playerLight.range = 6f;
        playerLight.intensity = PlayerLightIntensity;
        playerLight.color = new Color(0.95f, 0.92f, 0.85f);
        playerLight.shadows = LightShadows.None;
        playerLight.renderMode = LightRenderMode.ForcePixel;

        hud = gameObject.AddComponent<GameHud>();
        hud.Init(RestartGame, () => net.LeaveGame());
        terminal = gameObject.AddComponent<TerminalUI>();
        terminal.Init(this);
        postItUI = gameObject.AddComponent<PostItUI>();
        postItUI.Init(OnPostItWritten);
        Loc.Changed += OnLanguageChanged;
        Shader.SetGlobalFloat("_CouvreFeu", 0f);
        // V0.4.7 : plus de brume autour du Roi.
        Shader.SetGlobalVectorArray("_RoiMist", new Vector4[4]);
        PasteReceiver.Ensure();
        UpdateScreens();
    }

    static void ConfigureRendering()
    {
        // Assez pour presque tous les morceaux de murs (jusqu'à 8 lampes) : pas de ruptures de lumière entre eux.
        QualitySettings.pixelLightCount = 6;
        QualitySettings.shadows = ShadowQuality.Disable;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.black;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.skybox = null;
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            // Au-delà, le brouillard du labyrinthe masque tout : inutile de le dessiner.
            cam.farClipPlane = 45f;
        }
    }

    // ================= Cycle de vie =================

    public void StartAsHost()
    {
        seed = Random.Range(1, int.MaxValue);
        round = 1;
        quota = GameRules.QuotaForRound(1);
        progress = cash = 0;
        phase = Phase.Waiting;
        timeLeft = GameRules.RoundDuration;
        compass = false;
        ClearProgress();
        picked.Clear();
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        noteMessages.Clear();
        notePlacedAt.Clear();
        nextNoteId = nextEntityId = nextBeaconId = nextDropId = 0;
        dropped.Clear();
        hostCurfew = false;
        testFlags = 0;
        ResetHubFixtures();
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        ApplyWorld();
        PlaceUniqueObjects();
    }

    // Balises et Pogo au début d'une partie.
    void ResetHubFixtures()
    {
        beaconMessages.Clear();
        beacons.Clear();
        beacons.Charge = GameRules.BeaconCharge;
        tvVideos.Clear();
        myTvs.Clear();
        tvAsked.Clear();
        bellObsession = -1;
        pogoMood = Pogo.Mood.Closed;
        pogoSmileUntil = pogoCooldownUntil = 0f;
        pogoKills = 0;
        foreach (var p in pogos) if (p != null)
        {
            p.SetState(Pogo.Mood.Closed, false, "");
            p.SetKills(0);
        }
    }

    public void OnLocalEnter()
    {
        audioMuteUntil = Time.unscaledTime + 2f;
        presence.PlayerId = net.MyId;
        hud.Show(true);
        hud.HideGameOver();
        status.ClearInventory();
        GiveStartingItems();
        pendingPickups = 0;
        ReviveLocal(SpawnPoint(), GameRules.MaxHp);
        if (phase == Phase.GameOver) OnGameOver();
        OnRosterChanged();
        RefreshHud();
    }

    // Note 04 du chapitre 7 (V0.5) : l'hôte commence la partie avec une Balise dans la case 1.
    void GiveStartingItems()
    {
        if (net.IsHost) status.Add(ItemType.Balise);
    }

    public void ResetSession()
    {
        terminal.Close(false);
        postItUI.Close();
        hud.Show(false);
        WipedOut = false;
        allDeadSince = -1f;
        GameAudio.CancelDeathSequence();
        ClearEntities();
        DestroyZones();
        builtSeed = itemsRound = -1;
        ClearItems();
        ClearMounds();
        dugMounds.Clear();
        digEnds.Clear();
        EndLocalDig();
        ResetCannon();
        ClearNotes();
        LeaveKennel(false);
        foreach (var list in kennels.Values) foreach (var k in list) if (k != null) Destroy(k.gameObject);
        kennels.Clear();
        ResetGlyphs();
        ClearArcadeState();
        SetHubArcades(false);
        phase = Phase.Waiting;
        round = 1;
        quota = GameRules.QuotaForRound(1);
        progress = cash = 0;
        timeLeft = GameRules.RoundDuration;
        compass = false;
        ClearProgress();
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        noteMessages.Clear();
        notePlacedAt.Clear();
        hostCurfew = false;
        dropped.Clear();
        testFlags = sentFlags = 0;
        Godmode = false;
        EndTeleport(false);
        ResetHubFixtures();
        status.ClearInventory();
        status.SetDead(false);
        status.SetHp(GameRules.MaxHp);
        status.SetSerenity(GameRules.MaxSerenity);
        player.SpeedFactor = 1f;
        presence.Alive = true;
        spectateId = null;
        if (orbit != null) orbit.target = player.transform;
        hub.door.SetOpen(false, true);
        button.SetRaised(true, false);
        pogoKills = 0;
        foreach (var p in pogos) if (p != null)
        {
            p.SetState(Pogo.Mood.Closed, false, "");
            p.SetKills(0);
        }
        mistAmount = 0f;
        UpdateScreens();
    }

    // Construit les zones (nouvelle graine, puis celles que l'hôte a fait générer) et répartit les objets de la manche.
    void ApplyWorld()
    {
        bool rebuilt = false;
        if (seed != builtSeed)
        {
            ClearEntities();
            DestroyZones();
            builtSeed = seed;
            itemsRound = -1;
            rebuilt = true;
        }
        while (zones.Count < zoneCount) GenerateNextZone();
        if (itemsRound != round || itemsBoost != valueBoost) SpawnItems();
        hub.door.SetOpen(phase != Phase.Waiting);
        bool animate = !rebuilt && Time.unscaledTime > audioMuteUntil;
        button.SetRaised(phase == Phase.Waiting, animate);
        UpdateDoors(animate);
        UpdateScreens();
    }

    // Objets de valeur de la manche dans chaque zone (chapitre 10 : pourcentage du quota). V0.4.6 : ils sont enfouis dans
    // des monticules de terre ; le contenu de chacun (V0.4.7 : Os 85 %, Canard 10 %, Le Réveil 5 %) est tiré d'après la
    // graine, chez tous les joueurs.
    void SpawnItems()
    {
        bool newRound = itemsRound != round;
        // Passage à la manche suivante : les objets déterrés disparaissent dans une petite animation, sans son.
        ClearItems(newRound);
        ClearMounds();
        if (newRound)
        {
            dugMounds.Clear();
            digEnds.Clear();
            EndLocalDig();
        }
        itemsRound = round;
        itemsBoost = valueBoost;
        moundLoot.Clear();
        foreach (var z in zones) SpawnZoneItems(z);
        // Les objets laissés à terre restent d'une manche à l'autre.
        foreach (var kv in dropped) items[kv.Key] = ItemPickup.Create(itemsRoot, kv.Key, kv.Value.type, kv.Value.pos);
    }

    // Monticules d'une zone pour la manche (chapitre 10, V0.6 : 120 + 30 x nZone - 10 x nManche % du quota), tirés d'après
    // la graine chez tous les joueurs. Identifiants : numéro de zone x 10000 + rang.
    void SpawnZoneItems(WorldBuilder.ZoneRefs zone)
    {
        var rnd = new System.Random(seed * 31 + round * 7919 + zone.index * 104729);
        var spots = new List<Vector3>(zone.itemSpots);
        for (int i = spots.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }
        int count = Mathf.Min(GameRules.MoundsForRound(round, zone.index, valueBoost), spots.Count, MoundIdsPerZone);
        for (int k = 0; k < count; k++)
        {
            int id = zone.index * MoundIdsPerZone + k;
            ItemType loot = Items.RollMound(rnd);
            float yaw = (float)rnd.NextDouble() * 360f;
            moundLoot[id] = (loot, spots[k]);
            if (!dugMounds.Contains(id)) mounds[id] = Mound.Create(itemsRoot, id, loot, spots[k], yaw);
            else if (!picked.Contains(id) && loot != ItemType.Reveil) items[id] = ItemPickup.Create(itemsRoot, id, loot, spots[k]);
        }
    }

    const int MoundIdsPerZone = 10000;

    void ClearItems(bool vanishDug = false)
    {
        foreach (var kv in items)
            if (kv.Value == null) continue;
            else if (vanishDug && kv.Key < DropIdBase) Fx.Vanish(kv.Value.gameObject);
            else Destroy(kv.Value.gameObject);
        items.Clear();
    }

    void ClearMounds()
    {
        foreach (var m in mounds.Values) if (m != null) Destroy(m.gameObject);
        mounds.Clear();
    }

    // ================= Monticules =================

    // Le joueur local creuse : il court d'abord jusqu'à 0,4 m du monticule (s'il en est plus loin), puis creuse
    // 1,2 seconde sans bouger, sauter ni interagir ; il aboie en commençant.
    public void RequestDig(Mound mound)
    {
        if (localDig >= 0 || status.Dead || mound.Digging) return;
        localDig = mound.Id;
        Vector3 away = player.transform.position - mound.transform.position;
        away.y = 0f;
        if (away.magnitude > GameRules.DigApproach + 0.05f)
        {
            Vector3 spot = mound.transform.position + away.normalized * GameRules.DigApproach;
            spot.y = player.transform.position.y;
            player.WalkTarget = spot;
            // Bloqué en chemin (mur, autre joueur) : il creuse quand même, là où il est.
            localDigEnd = Time.time + 0.5f + away.magnitude / GameRules.BaseSpeed;
            digApproaching = true;
            return;
        }
        StartLocalDig(mound);
    }

    bool digApproaching;

    void UpdateDigApproach()
    {
        if (!mounds.TryGetValue(localDig, out var mound) || mound == null || mound.Digging) { EndLocalDig(); return; }
        Vector3 to = player.WalkTarget.HasValue ? player.WalkTarget.Value - player.transform.position : Vector3.zero;
        to.y = 0f;
        if (to.magnitude > 0.05f && Time.time < localDigEnd) return;
        player.WalkTarget = null;
        digApproaching = false;
        StartLocalDig(mound);
    }

    void StartLocalDig(Mound mound)
    {
        localDigEnd = Time.time + GameRules.DigDuration;
        player.Digging = true;
        Bark();
        mound.StartDigging();
        net.ToHost("MD|" + mound.Id);
    }

    void EndLocalDig()
    {
        localDig = -1;
        digApproaching = false;
        if (player == null) return;
        player.Digging = false;
        player.WalkTarget = null;
    }

    void UpdateDigs()
    {
        if (localDig >= 0 && status.Dead) EndLocalDig();
        else if (digApproaching) UpdateDigApproach();
        else if (localDig >= 0 && Time.time >= localDigEnd) EndLocalDig();
        if (!net.IsHost || digEnds.Count == 0) return;
        var done = new List<int>();
        foreach (var kv in digEnds) if (Time.time >= kv.Value) done.Add(kv.Key);
        foreach (int id in done)
        {
            digEnds.Remove(id);
            net.ToAll("MO|" + round + "|" + id);
        }
    }

    // Monticule creusé : il disparaît (dernière gerbe de terre si 'animate') et libère son objet, à ramasser ; ou bien
    // Le Réveil en sort (créé par l'hôte).
    void OpenMound(int id, bool animate)
    {
        if (!dugMounds.Add(id)) return;
        if (mounds.TryGetValue(id, out var m) && m != null)
        {
            if (animate) m.Open();
            else Destroy(m.gameObject);
        }
        mounds.Remove(id);
        if (!moundLoot.TryGetValue(id, out var loot)) return;
        if (loot.type == ItemType.Reveil)
        {
            if (net.IsHost && animate) SpawnReveil(loot.pos);
            return;
        }
        if (!picked.Contains(id) && !items.ContainsKey(id)) items[id] = ItemPickup.Create(itemsRoot, id, loot.type, loot.pos);
    }

    void RemoveItem(int id)
    {
        dropped.Remove(id);
        if (items.TryGetValue(id, out var it))
        {
            if (it != null) Destroy(it.gameObject);
            items.Remove(id);
        }
    }

    void ClearNotes()
    {
        foreach (var n in notes.Values) if (n != null) Destroy(n.gameObject);
        notes.Clear();
    }

    // ================= Mise à jour =================

    void Update()
    {
        bool inGame = net.InGame;
        PlayerController.InputLocked = !inGame || TerminalUI.IsOpen || PostItUI.IsOpen || ControlsUI.IsOpen || phase == Phase.GameOver || Teleporting || localDig >= 0 || CannonFlying || InKennel || ClawActive;
        OrbitCamera.CursorLockBlocked = TerminalUI.IsOpen || PostItUI.IsOpen || GameHud.GameOverVisible;

        if (phase == Phase.Running && !WipedOut)
        {
            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            if (net.IsHost && inGame && timeLeft <= 0f) EndRound();
            // La Balise du Hub se décharge pendant la manche.
            beacons.Charge = Mathf.Max(0f, beacons.Charge - Time.deltaTime);
        }
        if (net.IsHost && inGame) UpdateHostPogo();
        if (net.IsHost && inGame)
        {
            syncTimer += Time.unscaledDeltaTime;
            if (syncTimer >= 1f) BroadcastState();
            UpdateHostCurfew();
            UpdateHostWipeOut();
            UpdateHostZones();
            UpdateHostGlyphs();
            UpdateHostArcades();
            SimulateEntities();
        }

        UpdateEntities();
        UpdateAtmosphere();
        UpdateGameSounds(inGame);
        if (!inGame)
        {
            OrbitCamera.Dread = 0f;
            return;
        }

        presence.Alive = !status.Dead;
        presence.Sneaking = player.Sneaking;

        // Régénération dans le Hub.
        bool inHub = WorldBuilder.IsInHub(player.transform.position);
        if (!status.Dead && status.Hp < GameRules.MaxHp && inHub)
        {
            regenAcc += GameRules.HubRegenPerSecond * Time.deltaTime;
            int add = Mathf.FloorToInt(regenAcc);
            if (add > 0)
            {
                regenAcc -= add;
                status.SetHp(status.Hp + add);
            }
        }
        else regenAcc = 0f;
        // Pendant le Couvre Feu, hors de la brume (Hub compris) : +10 par seconde ; sinon, +2 par seconde partout.
        float serenityRate = CurfewActive ? (inMist ? 0f : GameRules.CurfewRegenPerSecond)
                           : GameRules.CalmSerenityRegenPerSecond;
        if (!status.Dead && status.Serenity < GameRules.MaxSerenity && serenityRate > 0f)
        {
            serenityRegenAcc += serenityRate * Time.deltaTime;
            int add = Mathf.FloorToInt(serenityRegenAcc);
            if (add > 0)
            {
                serenityRegenAcc -= add;
                status.SetSerenity(status.Serenity + add);
            }
        }
        else serenityRegenAcc = 0f;

        // Dans sa niche, on peut encore aboyer (ordre "Aboie !").
        if ((!PlayerController.InputLocked || InKennel) && !status.Dead && Controls.Down(GameAction.Bark)) TryBark();
        UpdateDigs();
        UpdateKennel();
        UpdateCannon();
        UpdateTeleport();
        UpdateStations();
        player.Dancing = channelPad != null || healing;
        UpdateObjects();
        UpdateCloches();
        UpdateRoi();
        UpdateMistContact();
        UpdateDespair();
        UpdateGlyphOrders();
        UpdateArcades();
        UpdateWatching();
        UpdateDread();
        UpdateSpectate();

        screenTimer -= Time.unscaledDeltaTime;
        if (screenTimer <= 0f)
        {
            screenTimer = 0.2f;
            UpdateScreens();
            UpdateKennels();
        }

        var target = player.GetComponent<PlayerInteractor>().Current;
        string prompt = target != null ? target.Prompt : "";
        hud.SetPrompt(string.IsNullOrEmpty(prompt) ? "" : "[" + Controls.Label(GameAction.Interact) + "] " + prompt);
        // Cap de la caméra (là où le joueur regarde), pas celui du personnage.
        hud.SetCompass(compass && !status.Dead, (orbit != null ? orbit.transform : player.transform).eulerAngles.y);
    }

    float farFogAmount;

    void UpdateAtmosphere()
    {
        Camera cam = Camera.main;
        bool inHub = cam == null || WorldBuilder.IsInHub(cam.transform.position);
        float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
        // Hors du Hub, selon la luminosité (base +50 %, réglage ±10 %).
        float bright = Brightness.Factor;
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, inHub ? HubAmbient : MazeAmbient * bright, k);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, inHub ? HubFog : MazeFog / bright, k);
        // Pendant la mort, la lampe éclaire encore le corps puis faiblit, jusqu'au noir.
        bool dying = status.Dead && OrbitCamera.DeathView >= 0f;
        playerLight.enabled = net.InGame && (!status.Dead || dying) && !WorldBuilder.IsInHub(player.transform.position);
        float lamp = PlayerLightIntensity * bright;
        playerLight.intensity = dying ? lamp * (1f - Mathf.Clamp01(OrbitCamera.DeathView / OrbitCamera.DeathViewDuration)) : lamp;

        // Brume du Couvre Feu : apparaît et se dissipe en douceur ; le brouillard bleuit quand on y est plongé.
        bool curfew = net.InGame && CurfewActive;
        mistAmount = Mathf.MoveTowards(mistAmount, curfew ? 1f : 0f, Time.deltaTime / 4f);
        Shader.SetGlobalFloat("_CouvreFeu", mistAmount);
        UpdateMistHoles();
        bool mistShown = mistAmount > 0.001f;
        foreach (var z in zones) if (z.mist.activeSelf != mistShown) z.mist.SetActive(mistShown);
        float low = cam != null && !inHub ? Mathf.Clamp01((2.2f - cam.transform.position.y) / 1.5f) : 0f;
        farFogAmount = Mathf.Lerp(farFogAmount, inHub ? 0f : 1f, k);
        RenderSettings.fogColor = Color.Lerp(FarFog * farFogAmount, MistFog, mistAmount * low);
        GameAudio.CurfewLevel = curfew ? 1f : 0f;
    }

    // Sons liés à l'état de la partie, détectés chez tous les clients (hôte compris) par changement d'état :
    // Bouton Rouge, quota validé, game over, glas du Couvre Feu, tic-tac de la dernière minute.
    Phase soundPhase;
    int soundRound = 1, lastTickSecond = -1;
    float audioMuteUntil;
    bool soundCurfew;

    void UpdateGameSounds(bool inGame)
    {
        // Pas de fanfare ni de buzzer en rejoignant une partie déjà commencée : on se cale sur l'état reçu.
        bool mute = !inGame || Time.unscaledTime < audioMuteUntil;
        bool curfew = CurfewActive;
        if (!mute)
        {
            if (soundPhase == Phase.Waiting && phase == Phase.Running)
                GameAudio.Play3D("button", hub.buttonAnchor.position + Vector3.up, 0.8f, 40f, 0f);
            if (phase == Phase.Running && round > soundRound) GameAudio.Play2D("fanfare", 0.8f);
            if (soundPhase != Phase.GameOver && phase == Phase.GameOver) GameAudio.Play2D("gameover", 0.9f);
            if (curfew && !soundCurfew) GameAudio.Play2D("bell", 1f);
        }
        soundPhase = phase;
        soundRound = round;
        soundCurfew = curfew;

        int second = Mathf.CeilToInt(timeLeft);
        if (!mute && phase == Phase.Running && second <= 60 && second > 0 && second != lastTickSecond)
        {
            bool last = second <= 10;
            GameAudio.Play3D(last ? "tock" : "tick", NearestHub(player.transform.position).timerText.transform.position, last ? 1f : 0.6f, 30f, 0f, second % 2 == 0 ? 1f : 0.85f);
        }
        lastTickSecond = second;
    }

    // ---------- Le Roi : flash, lumières affolées (GameSession.Objects.cs), chaîne ----------

    int chainedBy = -1, chainPresses;
    float chainedAt;

    void UpdateRoi()
    {
        UpdateRoiEffects();
        Vector3 me = player.transform.position;

        // Enchaîné : immobilisé (la caméra reste libre) et traîné vers le Roi ; 3 appuis sur Espace rompent la chaîne,
        // et une barre d'espace s'agite à l'écran.
        hud.SetChainPrompt(chainedBy >= 0);
        if (chainedBy < 0) return;
        bool held = entities.TryGetValue(chainedBy, out var holder) && holder is Roi chainer
            && chainer.Current == Roi.State.Pull && chainer.TargetId == net.MyId;
        // (L'état du Roi arrive un peu après le message de la chaîne.)
        if (status.Dead || holder == null || !held && Time.time - chainedAt > 0.6f)
        {
            ReleaseChain();
            return;
        }
        player.Immobilize(0.2f);
        Vector3 to = holder.transform.position - me;
        to.y = 0f;
        player.Pull = to.magnitude > 0.4f ? to.normalized * GameRules.BaseSpeed * GameRules.RoiPullMultiplier : Vector3.zero;
        if (!PlayerController.InputLocked && Controls.Down(GameAction.Jump) && ++chainPresses >= GameRules.RoiBreakPresses)
        {
            net.ToHost("RB|" + chainedBy);
            ReleaseChain();
        }
    }

    void ReleaseChain()
    {
        chainedBy = -1;
        chainPresses = 0;
        player.Pull = null;
        player.ReleaseImmobilize();
    }

    // Au contact de la brume du Couvre Feu : -3 de sérénité par seconde (V0.5), et le déplacement y est ralenti de 20 %.
    bool inMist;

    void UpdateMistContact()
    {
        Vector3 p = player.transform.position;
        var capsule = player.GetComponent<CapsuleCollider>();
        float feet = p.y + capsule.center.y - capsule.height * 0.5f;
        inMist = !status.Dead && !Teleporting && !CannonFlying && !InKennel && CurfewActive && mistAmount > 0.5f && zones.Count > 0
            && WorldBuilder.ZoneAt(p) > 0 && !WorldBuilder.IsInHub(p) && feet < GameRules.MistTop && !NearEnhancedBeacon(p);
        // Dans la brume, le personnage avance 20 % moins vite ; immergé jusqu'au cou (Note 03, V0.4.11), 32 % moins vite.
        player.SpeedFactor = !inMist ? 1f : NeckHeight() < GameRules.MistTop ? GameRules.MistDeepSpeedMultiplier : GameRules.MistSpeedMultiplier;
        if (!inMist || Godmode)
        {
            mistTimer = 0f;
            return;
        }
        // mistTimer : points de sérénité perdus pas encore retirés (fraction).
        mistTimer += GameRules.CurfewLossPerSecond * Time.deltaTime;
        int loss = Mathf.FloorToInt(mistTimer);
        if (loss <= 0) return;
        mistTimer -= loss;
        status.SetSerenity(status.Serenity - loss);
    }

    // Note 02 du chapitre 3.4 (V0.4.10) : à 0 de sérénité, le personnage ne meurt plus d'un coup ; il perd 15 PV par
    // seconde, d'un bloc (non lissé), avec l'animation de dégâts, jusqu'à ce qu'il regagne de la sérénité.
    float despairTimer;

    void UpdateDespair()
    {
        if (status.Dead || status.Serenity > 0 || Teleporting || CannonFlying || InKennel)
        {
            despairTimer = 0f;
            return;
        }
        despairTimer += Time.deltaTime;
        if (despairTimer < 1f) return;
        despairTimer -= 1f;
        DamageLocal(GameRules.DespairDamagePerSecond, "le Couvre Feu");
    }

    // Hauteur du cou du personnage local (os du modèle ; à défaut, 1,45 m au-dessus des pieds) : il baisse quand le
    // personnage s'accroupit en marche discrète.
    float NeckHeight()
    {
        var animator = player.GetComponentInChildren<Animator>();
        Transform neck = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        if (neck != null) return neck.position.y;
        var capsule = player.GetComponent<CapsuleCollider>();
        return player.transform.position.y + capsule.center.y - capsule.height * 0.5f + 1.45f;
    }

    // Un joueur mort voit la caméra d'un autre joueur (clic pour changer).
    void UpdateSpectate()
    {
        if (deathFxAt >= 0f && status.Dead && Time.time >= deathFxAt) PlayDeathFx(deathFxSound);
        if (!status.Dead || orbit == null)
        {
            OrbitCamera.DeathView = -1f;
            hud.SetSpectate("");
            return;
        }
        // D'abord, la caméra s'élève lentement au-dessus de son propre corps.
        float sinceDeath = Time.time - deathAt;
        if (sinceDeath < OrbitCamera.DeathViewDuration && phase != Phase.GameOver)
        {
            orbit.target = player.transform;
            // Avant (Pogo), la caméra garde sa place habituelle.
            OrbitCamera.DeathView = sinceDeath >= 0f ? sinceDeath : -1f;
            hud.SetSpectate("");
            return;
        }
        OrbitCamera.DeathView = -1f;
        var alive = new List<RemotePlayer>();
        foreach (var rp in net.Remotes.Values) if (rp != null && !rp.Dead) alive.Add(rp);
        RemotePlayer current = alive.Find(r => r.PlayerId == spectateId);
        if (current == null || (Input.GetMouseButtonDown(0) && OrbitCamera.Looking && Time.frameCount != OrbitCamera.LockFrame))
        {
            int idx = current == null ? 0 : (alive.IndexOf(current) + 1) % alive.Count;
            current = alive.Count > 0 ? alive[idx] : null;
            spectateId = current != null ? current.PlayerId : null;
        }
        orbit.target = current != null ? current.transform : player.transform;
        string end = phase == Phase.GameOver ? "" : "\n" + Loc.T("Un autre joueur peut vous réanimer depuis votre Chenil, dans le Hub.");
        hud.SetSpectate(current != null
            ? Loc.F("Vous êtes mort. Vous regardez {0} (clic : changer de joueur).", current.PlayerName) + end
            : Loc.T("Vous êtes mort.") + end);
    }

    // Nouvelle langue : HUD et écrans du chapiteau retraduits tout de suite.
    void OnLanguageChanged()
    {
        if (hud != null && status != null) RefreshHud();
        if (hub != null && skipButtons.Count > 0) UpdateScreens();
    }

    // Écrans de tous les Hubs (V0.6 : l'Écran des Logs n'existe que dans le Hub 1).
    void UpdateScreens()
    {
        int seconds = Mathf.CeilToInt(timeLeft);
        string time = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        string quotaLine = Loc.F("Quota : {0} / {1}", progress, quota), cashLine = Loc.F("Cagnotte : {0}", Items.Price(cash));
        string board = RoundBoardText();
        foreach (var h in hubs)
        {
            h.timerText.text = time;
            h.quotaText.text = quotaLine;
            h.cashText.text = cashLine;
            h.roundBoardText.text = board;
            if (h.logText != null) h.logText.text = string.Join("\n", logs.ConvertAll(Loc.Line));
        }
        foreach (var b in skipButtons) if (b != null && b.gameObject.activeSelf != CanSkipRound) b.gameObject.SetActive(CanSkipRound);
    }

    // Classement des joueurs connectés : croquettes gagnées pendant la manche, puis morts.
    string RoundBoardText()
    {
        var rows = new List<(string name, int golds, int deaths)>();
        foreach (var kv in net.Roster)
        {
            roundStats.TryGetValue(kv.Key, out var s);
            rows.Add((kv.Value.name, s?.golds ?? 0, s?.deaths ?? 0));
        }
        rows.Sort((a, b) => a.golds != b.golds ? b.golds.CompareTo(a.golds) : a.deaths.CompareTo(b.deaths));
        var sb = new StringBuilder("<size=110>" + Loc.F("Manche {0}", statsRound) + "</size>\n");
        for (int i = 0; i < rows.Count; i++)
            sb.Append('\n').Append(i + 1).Append(". ").Append(rows[i].name).Append(" : ")
              .Append(Items.Price(rows[i].golds)).Append(" · ").Append(Loc.N(rows[i].deaths, "{0} mort|{0} morts", rows[i].deaths));
        return sb.ToString();
    }

    RoundStats StatsOf(string id)
    {
        if (!roundStats.TryGetValue(id, out var s)) roundStats[id] = s = new RoundStats();
        return s;
    }

    string StatsMsg()
    {
        var sb = new StringBuilder("SC|" + statsRound + "|");
        bool first = true;
        foreach (var kv in roundStats)
        {
            if (!first) sb.Append(';');
            first = false;
            sb.Append(kv.Key).Append(',').Append(kv.Value.golds).Append(',').Append(kv.Value.deaths);
        }
        return sb.ToString();
    }

    void ResetRoundStats(int newRound)
    {
        roundStats.Clear();
        statsRound = newRound;
        net.ToAll(StatsMsg());
    }

    void RefreshHud()
    {
        hud.SetHealth(status.Hp, status.Dead);
        hud.SetSerenity(status.Serenity, status.Dead);
        hud.SetInventory(status);
    }

    // ================= Chenils =================

    public void OnRosterChanged()
    {
        var present = new HashSet<string>(net.Roster.Keys);
        var toRemove = new List<string>();
        foreach (var id in kennels.Keys) if (!present.Contains(id)) toRemove.Add(id);
        foreach (var id in toRemove)
        {
            foreach (var k in kennels[id]) if (k != null) Destroy(k.gameObject);
            kennels.Remove(id);
        }
        if (!net.InGame && !net.IsHost) return;
        foreach (var kv in net.Roster)
        {
            if (!kennels.TryGetValue(kv.Key, out var list)) kennels[kv.Key] = list = new List<Kennel>();
            // Un Chenil par joueur dans chaque Hub.
            while (list.Count < hubs.Count)
            {
                var h = hubs[list.Count];
                int slot = Mathf.Clamp(kv.Value.slot, 0, h.kennelPositions.Length - 1);
                list.Add(Kennel.Create(worldRoot, h.kennelPositions[slot], h.kennelRotations[slot], kv.Key, h.index));
            }
        }
        UpdateKennels();
    }

    void UpdateKennels()
    {
        foreach (var kv in kennels)
            foreach (var kennel in kv.Value)
                UpdateKennelSign(kv.Key, kennel);
    }

    // Panneau du Chenil : pseudo, PV, distance (depuis ce Chenil) et état du joueur.
    void UpdateKennelSign(string id, Kennel kennel)
    {
        if (kennel == null || !net.Roster.TryGetValue(id, out var info)) return;
        Vector3 pos;
        int hp;
        bool dead;
        if (id == net.MyId)
        {
            pos = player.transform.position; hp = status.Hp; dead = status.Dead;
        }
        else if (net.Remotes.TryGetValue(id, out var rp) && rp != null)
        {
            pos = rp.transform.position; hp = rp.Dead ? 0 : rp.Hp; dead = rp.Dead;
        }
        else return;
        Vector3 d = pos - kennel.transform.position;
        d.y = 0f;
        kennel.SetInfo(info.name, hp, d.magnitude, dead);
        // Un joueur ne peut pas se réanimer lui-même.
        if (id == net.MyId) kennel.TargetDead = false;
    }

    Kennel KennelOf(string id, int hubIndex) =>
        kennels.TryGetValue(id, out var list) && hubIndex >= 1 && hubIndex <= list.Count ? list[hubIndex - 1] : null;

    // Devant le Chenil du joueur dans le Hub donné.
    Vector3 KennelSpawn(string id, int hubIndex)
    {
        var k = KennelOf(id, hubIndex);
        if (k != null)
        {
            Vector3 p = k.transform.position + k.transform.forward * 1.5f;
            return new Vector3(p.x, k.transform.position.y + 0.95f, p.z);
        }
        return SpawnPoint();
    }

    // ================= Joueur local =================

    Vector3 SpawnPoint()
    {
        Vector2 c = Random.insideUnitCircle.normalized * Random.Range(2.5f, 5f);
        return new Vector3(c.x, 0.95f, c.y);
    }

    public void RespawnInHub()
    {
        player.Teleport(SpawnPoint(), Random.Range(0f, 360f));
    }

    // Non précisé dans le GDD : un personnage réanimé retrouve toute sa sérénité.
    void ReviveLocal(Vector3 position, int hp)
    {
        deathAt = -100f;
        deathFxAt = -1f;
        OrbitCamera.DeathView = -1f;
        hud.ClearDeath();
        GameAudio.CancelDeathSequence();
        status.SetDead(false);
        status.SetHp(hp);
        status.SetSerenity(GameRules.MaxSerenity);
        presence.Alive = true;
        avatar.SetDead(false);
        player.SetFrozen(false);
        player.Teleport(position, Mathf.Atan2(-position.x, -position.z) * Mathf.Rad2Deg);
        spectateId = null;
        if (orbit != null) orbit.target = player.transform;
        RefreshHud();
    }

    // /godmod (hôte) : achats gratuits au Terminal, aucun dégât, aucune perte de sérénité, jamais immobilisé.
    public bool Godmode { get; private set; }

    public string SetGodmode(bool on)
    {
        if (!net.IsHost) return null;
        Godmode = on;
        if (on) player.ReleaseImmobilize();
        // V0.6.1 : en godmod, une borne d'arcade de chaque type apparaît dans le Hub 1 (chez tous les joueurs).
        SetHubArcades(on);
        net.ToAll("AG|" + (on ? 1 : 0));
        return Loc.T(on ? "Godmod activé : achats gratuits, invincible, sérénité intacte, jamais immobilisé." : "Godmod désactivé.");
    }

    bool Invincible => Time.time < invincibleUntil || Godmode || CannonFlying || InCannon || InKennel;

    // Dégâts reçus par le personnage local. Le Hub est un endroit sûr (sauf face au Réveil, 'inHub') ; juste après un /tp,
    // aucun dégât.
    public void DamageLocal(int amount, string cause, Entity by = null, bool inHub = false)
    {
        if (status.Dead || phase == Phase.GameOver || !net.InGame) return;
        if (WorldBuilder.IsInHub(player.transform.position) && !inHub || Invincible || Teleporting) return;
        status.SetHp(status.Hp - amount);
        RaiseTestFlag(TestHurt);
        hud.FlashDamage();
        GameAudio.Play2D("hurt", 0.9f, 0.08f);
        // Son de l'entité qui blesse sans tuer (Pion, Araignée). GDD 12 (V0.5.2) : entendu par tous (l'hôte le relaie)
        // ou seulement par le joueur touché, selon l'entité.
        if (status.Hp > 0)
        {
            if (by != null && by.HurtSound != null)
            {
                if (by.SoundsShared) net.ToHost("HL|" + by.Id);
                else by.PlayHurtSound();
            }
            return;
        }
        // Son de mise à mort de l'entité (GDD 12), même règle.
        if (by != null)
        {
            if (by.SoundsShared) net.ToHost("KL|" + by.Id);
            else by.PlayKillSound();
        }
        // Note 03 (V0.4.3) : tué par un Cauchemar (ou par la Tête de Cloche, "horriblement"), la caméra plonge environ
        // 1,5 seconde dans ses yeux, puis le corps s'effondre.
        if (by != null && (by.Nightmare || by.Kind == EntityKind.TeteDeCloche))
        {
            float stare = by.Kind == EntityKind.TeteDeCloche ? GameRules.TeteStareDuration : GameRules.NightmareStareDuration;
            OrbitCamera.StartJumpscare(by.transform, by.EyesLocal, stare, true, 0.6f);
            KillLocal(cause, stare);
        }
        else KillLocal(cause);
    }

    // fxDelay : la mort est immédiate, mais le râle, l'écran rouge et l'animation du corps attendent la fin d'un face-à-face
    // (Cauchemar, Note 03 du chapitre 3.4).
    void KillLocal(string cause, float fxDelay = 0f)
    {
        if (status.Dead) return;
        status.SetDead(true);
        zeroHpAt = Time.time;
        EndTeleport(false);
        channelPad = null;
        StopChannelSound();
        // Les objets du Terminal sont détruits ; les autres (avec un modèle 3D) restent à terre près du corps.
        var left = new List<ItemType>();
        var unique = new List<ItemType>();
        if (cause != null)
            for (int i = 0; i < GameRules.InventorySize; i++)
                if (Items.DropsOnDeath(status[i])) left.Add(status[i]);
                else if (Items.IsPlaceable(status[i])) unique.Add(status[i]);
        status.ClearInventory();
        pendingPickups = 0;
        presence.Alive = false;
        player.SetFrozen(true);
        // Capsule désactivée : les objets tombent jusqu'au sol.
        foreach (var type in left) DropNear(type, player.transform.position);
        // Les objets uniques (Télévision, Trampoline, Cloche Décorative), peluches et boules à facettes se retrouvent posés
        // près du corps.
        foreach (var type in unique) PlaceNear(type, player.transform.position);
        nearNightmareSince = -1f;
        terminal.Close();
        postItUI.Close();
        if (fxDelay > 0f)
        {
            deathFxAt = Time.time + fxDelay;
            deathAt = deathFxAt;
            deathFxSound = cause != null;
        }
        else PlayDeathFx(cause != null);
        if (cause != null) net.ToHost("DE|" + cause);
        RefreshHud();
    }

    float deathFxAt = -1f, zeroHpAt;
    bool deathFxSound;

    void PlayDeathFx(bool sound)
    {
        deathFxAt = -1f;
        avatar.SetDead(true);
        // Au game over, l'accord lugubre remplace les râles.
        if (!sound) return;
        // GDD 12 (V0.4.10) : le râle (Perso_Mort) se joue 1 s après que le personnage est tombé à 0 PV (après le
        // face-à-face d'un Cauchemar, plus long, il suit aussitôt).
        float delay = Mathf.Max(0f, zeroHpAt + GameRules.DeathSoundDelay - Time.time);
        GameAudio.Play3DDelayed("death", player.transform.position, 1f, 30f, 0.06f, delay);
        // Le râle, un silence total, puis un cœur qui s'arrête ; l'écran rougit et s'assombrit, la caméra quitte le corps.
        GameAudio.PlayDeathSequence(delay);
        hud.PlayDeath();
        deathAt = Time.time;
    }

    public void UseSelectedItem()
    {
        int slot = status.Selected;
        if (slot < 0) return;
        switch (status[slot])
        {
            case ItemType.CuisseDePoulet:
                if (status.Hp >= GameRules.MaxHp && status.Serenity >= GameRules.MaxSerenity)
                {
                    hud.Toast(Loc.T("Votre vie et votre sérénité sont déjà au maximum."));
                    return;
                }
                status.RemoveAt(slot);
                status.SetHp(status.Hp + Items.CuisseHeal);
                status.SetSerenity(status.Serenity + Items.CuisseSerenity);
                GameAudio.Play2D("eat", 0.8f, 0.1f);
                Gesture(PlayerGesture.Eat);
                hud.Toast(Loc.F("Miam ! +{0} PV, +{1} de sérénité", Items.CuisseHeal, Items.CuisseSerenity));
                break;
            case ItemType.Balise:
                TryPlaceBeacon(slot, DeviceKind.Beacon);
                break;
            case ItemType.BaliseAmelioree:
                TryPlaceBeacon(slot, DeviceKind.Enhanced);
                break;
            case ItemType.Teleporteur:
                TryPlaceBeacon(slot, DeviceKind.Teleporter);
                break;
            case ItemType.StationCurative:
                TryPlaceBeacon(slot, DeviceKind.Station);
                break;
            case ItemType.Canon:
                TryPlaceBeacon(slot, DeviceKind.Canon);
                break;
            case ItemType.Television:
            case ItemType.Trampoline:
            case ItemType.ClocheDecorative:
            case ItemType.PelucheLapin:
            case ItemType.PeluchePingouin:
            case ItemType.PelucheSinge:
            case ItemType.PelucheChat:
            case ItemType.BouleArgent:
            case ItemType.BouleOr:
            case ItemType.BouleDiamant:
                TryPlaceBeacon(slot, Beacon.KindOf(status[slot]));
                break;
            case ItemType.Chargeur:
                hud.Toast(Loc.T("Visez la Balise d'un Hub, près de la porte, pour la recharger."));
                break;
            case ItemType.PostIt:
                TryPlacePostIt(slot);
                break;
            case ItemType.Cle:
                hud.Toast(Loc.T("Visez un orbe activé, devant une porte à orbes, pour le briser avec la Clé."));
                break;
        }
    }

    // ================= Post-It =================

    int postItSlot = -1;
    Vector3 postItPos, postItNormal;

    // Viser un mur (assez proche) ouvre la saisie du message.
    void TryPlacePostIt(int slot)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool wall = Physics.Raycast(ray, out RaycastHit hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.normal.y) < 0.5f
            && hit.collider.GetComponentInParent<Interactable>() == null
            && Vector3.Distance(hit.point, player.transform.position) <= GameRules.InteractRange + 1f;
        if (!wall)
        {
            hud.Toast(Loc.T("Visez un mur tout proche pour y accrocher le Post-It."));
            return;
        }
        postItSlot = slot;
        postItPos = hit.point;
        postItNormal = hit.normal;
        postItUI.Open();
    }

    void OnPostItWritten(string text)
    {
        text = SanitizeNote(text);
        if (postItSlot < 0 || status.Dead || status[postItSlot] != ItemType.PostIt || text.Length == 0) return;
        status.RemoveAt(postItSlot);
        postItSlot = -1;
        Gesture(PlayerGesture.Use);
        net.ToHost("PP|" + NetworkGame.F(postItPos.x) + "|" + NetworkGame.F(postItPos.y) + "|" + NetworkGame.F(postItPos.z)
            + "|" + NetworkGame.F(postItNormal.x) + "|" + NetworkGame.F(postItNormal.y) + "|" + NetworkGame.F(postItNormal.z) + "|" + text);
    }

    static string SanitizeNote(string text)
    {
        text = (text ?? "").Replace("|", "").Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", "").Trim();
        return text.Length > Items.PostItMaxChars ? text.Substring(0, Items.PostItMaxChars) : text;
    }

    public void RemovePostIt(int id) => net.ToHost("PD|" + id);

    // ================= Balises =================

    // Viser le sol (assez près) pose la Balise (ou la Balise Améliorée, le Téléporteur, la Station Curative, le Canon).
    // Le Téléporteur, la Station Curative et le Canon se posent hors du Hub ; les Balises, les Balises Améliorées et les
    // objets uniques, partout (et se ramassent de la même façon). La Balise du Hub initiale ne change pas.
    void TryPlaceBeacon(int slot, DeviceKind kind)
    {
        string what = Items.Get(status[slot]).name;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // V0.6.1 : une boule à facettes se pose au sol ou au plafond (elle y pend au bout d'une chaînette).
        bool disco = Beacon.IsDiscoKind(kind);
        bool aimed = Physics.Raycast(ray, out RaycastHit hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Interactable>() == null;
        bool ceiling = disco && aimed && hit.normal.y < -0.7f && Vector3.Distance(hit.point, player.transform.position) <= GameRules.InteractRange + 4f;
        bool ground = aimed && hit.normal.y > 0.7f && Vector3.Distance(hit.point, player.transform.position) <= GameRules.InteractRange + 1.5f;
        if (!ground && !ceiling)
        {
            hud.Toast(disco ? Loc.F("Visez le sol ou le plafond, tout près de vous, pour y poser : {0}.", what)
                            : Loc.F("Visez le sol, tout près de vous, pour y poser : {0}.", what));
            return;
        }
        if (WorldBuilder.IsInHub(hit.point) && !Items.IsPlaceable(status[slot]) && kind != DeviceKind.Beacon && kind != DeviceKind.Enhanced)
        {
            hud.Toast(Loc.F("{0} : à poser hors du Hub.", what));
            return;
        }
        if (kind == DeviceKind.Canon && NearWall(hit.point, hit.collider, 1f))
        {
            hud.Toast(Loc.F("{0} : à poser à plus d'1 mètre d'un mur.", what));
            return;
        }
        status.RemoveAt(slot);
        Gesture(PlayerGesture.Place);
        // Tourné vers le joueur qui le pose (la Télévision lui présente son écran).
        Vector3 back = player.transform.position - hit.point;
        float yaw = back.sqrMagnitude > 0.01f ? Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg : 0f;
        net.ToHost("BN|" + NetworkGame.F(hit.point.x) + "|" + NetworkGame.F(hit.point.y) + "|" + NetworkGame.F(hit.point.z) + "|" + (int)kind
            + "|" + NetworkGame.F(yaw) + "|" + (ceiling ? 1 : 0));
    }

    // Un mur (ni le sol visé, ni un joueur, une entité ou un objet) à moins de 'distance' du point, à l'horizontale.
    static bool NearWall(Vector3 point, Collider ground, float distance)
    {
        foreach (float height in new[] { 0.3f, 1.2f })
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                foreach (var h in Physics.RaycastAll(point + Vector3.up * height, dir, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var c = h.collider;
                    if (c == ground || c.GetComponentInParent<Interactable>() != null || c.GetComponentInParent<PlayerController>() != null
                        || c.GetComponentInParent<RemotePlayer>() != null || c.GetComponentInParent<Entity>() != null) continue;
                    return true;
                }
            }
        return false;
    }

    public void InteractBeacon(Beacon beacon)
    {
        if (beacon.IsHub)
        {
            int slot = status.Selected;
            if (slot < 0 || status[slot] != ItemType.Chargeur)
            {
                hud.Toast(Loc.T(beacons.HubCharged ? "Les Balises des Hubs sont chargées. Un Chargeur prolonge leur charge."
                                             : "Les Balises des Hubs sont déchargées : sélectionnez un Chargeur pour les recharger."));
                return;
            }
            status.RemoveAt(slot);
            Gesture(PlayerGesture.Use);
            net.ToHost("BC");
            return;
        }
        if (!HasFreeSlot)
        {
            hud.Toast(Loc.T("Votre inventaire est plein !"));
            return;
        }
        pendingPickups++;
        Gesture(PlayerGesture.PickUp);
        net.ToHost("BG|" + beacon.Id);
    }

    // ================= Pogo =================

    string PogoMsg(string attacked) => "JX|" + (int)pogoMood + "|" + (Time.time < pogoSmileUntil ? 1 : 0) + "|" + attacked + "|" + pogoKills;

    void SetPogo(Pogo.Mood mood, string attacked = "")
    {
        pogoMood = mood;
        net.ToAll(PogoMsg(attacked));
    }

    // Sans achat pendant 10 secondes (ou à la fin de la partie), Pogo rentre dans sa boîte.
    void UpdateHostPogo()
    {
        if (pogoMood == Pogo.Mood.Closed) return;
        if (phase != Phase.Running || Time.time - pogoActiveAt > GameRules.PogoIdleClose) SetPogo(Pogo.Mood.Closed);
    }

    public void InteractPogo()
    {
        if (pogos[0].State == Pogo.Mood.Closed)
        {
            net.ToHost("JO");
            return;
        }
        int slot = status.Selected;
        ItemType type = slot >= 0 ? status[slot] : ItemType.None;
        if (type == ItemType.None)
        {
            net.ToHost("JE");
            return;
        }
        // Comme /sell : l'objet quitte l'inventaire, l'hôte crédite quota et cagnotte.
        status.RemoveAt(slot);
        Gesture(PlayerGesture.Use);
        net.ToHost("JS|" + (int)type);
        GameAudio.Play2D("register", 0.8f);
        GameAudio.Play2D("sell_bell", 0.7f);
    }

    // ================= Orbes et portes à orbes =================

    // Une Clé brise l'orbe ; en godmod (hôte), un clic suffit, même sur un orbe éteint (V0.6).
    public void UseKeyOnOrb(int orb)
    {
        int slot = status.IndexOf(ItemType.Cle);
        if (slot < 0 && !Godmode) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("OB|" + orb + "|" + slot);
    }

    // Geste du personnage local, joué tout de suite et relayé aux autres joueurs par l'hôte.
    void Gesture(PlayerGesture g)
    {
        if (avatar != null) avatar.PlayGesture(g);
        net.ToHost("GE|" + (int)g);
    }

    // ================= Entités =================

    // /spawn : fait apparaître l'entité 'id' dans la zone de l'hôte (la Zone 1 depuis un Hub). Retourne le message du Terminal.
    public string SpawnEntity(int id)
    {
        if (!net.IsHost) return Loc.T("Commande Incorrecte.");
        if (zones.Count == 0 || phase == Phase.GameOver) return Loc.T("Impossible pour le moment.");
        var rnd = new System.Random(seed + nextEntityId * 131 + Mathf.RoundToInt(Time.time * 10f));
        int zone = Mathf.Clamp(WorldBuilder.ZoneAt(player.transform.position), 1, zones.Count);
        string z = zone.ToString();
        switch (id)
        {
            case 1: return Loc.T("Les Lasers ne peuvent plus apparaître une fois le terrain généré.");
            case 2: return SpawnAt<Dame>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("La Dame rôde désormais dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 3: return SpawnAt<Tour>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("La Tour arpente désormais la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 4: return SpawnPions(zone, rnd) > 0 ? Loc.F("Des Pions s'installent dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 5: return SpawnAt<Cloche>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("La Cloche attend dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 6: return SpawnAt<Spider>(zone, rnd, GameRules.SpiderSpawnClearance) != null ? Loc.F("Une Araignée rôde dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 7: return SpawnAt<Fou>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("Le Fou sourit désormais dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 8: return SpawnAt<Roi>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("Le Roi arpente désormais la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 9: return SpawnAt<Cavalier>(zone, rnd, GameRules.EntitySpawnClearance) != null ? Loc.F("Le Cavalier galope désormais dans la Zone {0}...", z) : Loc.T("Impossible pour le moment.");
            case 10: return phase == Phase.Running ? SpawnReveilNearHost(rnd) : Loc.T("Impossible pour le moment.");
            case 11: return SpawnTete(net.MyId) ? Loc.T("La Tête de Cloche vous traque...") : Loc.T("Impossible pour le moment.");
            default: return Loc.F("Aucune entité n'a l'ID {0}.", id);
        }
    }

    // Entités de type Cauchemar (La Dame, La Tour, Le Fou, Le Roi, Le Cavalier) : l'une d'elles, au hasard.
    void SpawnNightmare(int zone, System.Random rnd)
    {
        switch (rnd.Next(5))
        {
            case 0: SpawnAt<Dame>(zone, rnd, GameRules.EntitySpawnClearance); break;
            case 1: SpawnAt<Tour>(zone, rnd, GameRules.EntitySpawnClearance); break;
            case 2: SpawnAt<Fou>(zone, rnd, GameRules.EntitySpawnClearance); break;
            case 3: SpawnAt<Cavalier>(zone, rnd, GameRules.EntitySpawnClearance); break;
            default: SpawnAt<Roi>(zone, rnd, GameRules.EntitySpawnClearance); break;
        }
    }

    // Entités de type Normal (un groupe de Pions, La Cloche) : l'une d'elles, au hasard.
    void SpawnNormal(int zone, System.Random rnd)
    {
        if (rnd.Next(2) == 0) SpawnPions(zone, rnd);
        else SpawnAt<Cloche>(zone, rnd, GameRules.EntitySpawnClearance);
    }

    // "Faire apparaître un Pion", c'est faire apparaître un groupe de 1 à 3 Pions, côte à côte.
    int SpawnPions(int zone, System.Random rnd)
    {
        var first = SpawnAt<Pion>(zone, rnd, GameRules.EntitySpawnClearance);
        if (first == null) return 0;
        MazeNav nav = zones[zone - 1].nav;
        int count = 1 + rnd.Next(3);
        for (int k = 1; k < count; k++)
        {
            Vector3 offset = Quaternion.Euler(0f, k * 120f, 0f) * Vector3.forward * 1.2f;
            int node = nav.NodeAt(first.transform.position + offset);
            Vector3 pos = node >= 0 ? nav.Position(node) : first.transform.position;
            var pion = Entity.Create<Pion>(entitiesRoot, nextEntityId++, pos, (float)rnd.NextDouble() * 360f, nav);
            pion.Zone = zone;
            pion.Home = first.Home;
            entities[pion.Id] = pion;
        }
        return count;
    }

    // Le Couvre Feu commence (hôte) : 3 à 5 Araignées par zone déverrouillée (et générée, à portée des joueurs) selon le
    // nombre de joueurs, loin des joueurs.
    void SpawnSpiders()
    {
        if (zones.Count == 0 || phase != Phase.Running) return;
        var rnd = new System.Random(seed * 41 + round * 977);
        int perZone = GameRules.SpidersPerZone(net.Roster.Count);
        foreach (var z in zones)
        {
            if (!ZoneUnlocked(z.index) || !ZoneActive(z.index)) continue;
            for (int k = 0; k < perZone; k++)
                SpawnAt<Spider>(z.index, rnd, GameRules.SpiderSpawnClearance);
        }
    }

    // Sur la grille de navigation de la zone (1, 2, 3...), loin du Hub, des joueurs (au moins 20 m, note 01 du chapitre 8)
    // et des Balises Améliorées ; sans emplacement valable, l'entité n'apparaît pas.
    T SpawnAt<T>(int zone, System.Random rnd, float playerClearance) where T : Entity
    {
        if (zone < 1 || zone > zones.Count) return null;
        MazeNav nav = zones[zone - 1].nav;
        if (!nav.Ready) nav.BuildAll();
        int node = -1;
        for (int attempt = 0; attempt < 60 && node < 0; attempt++)
        {
            int candidate = nav.RandomNode(0, rnd, Vector3.zero, 0f);
            if (candidate < 0) continue;
            Vector3 p = nav.Position(candidate);
            bool blocked = NearEnhancedBeacon(p) || WorldBuilder.IsInHub(p);
            foreach (var pl in PlayerPresence.All)
                if (pl.Alive && Vector3.Distance(pl.Position, p) < playerClearance) blocked = true;
            if (!blocked) node = candidate;
        }
        if (node < 0) return null;
        var e = Entity.Create<T>(entitiesRoot, nextEntityId++, nav.Position(node), (float)rnd.NextDouble() * 360f, nav);
        e.Zone = zone;
        entities[e.Id] = e;
        return e;
    }

    // Clients : l'entité ne se déplace pas elle-même (l'hôte la simule), elle n'a besoin que de la grille de sa zone.
    Entity CreateEntity(EntityKind kind, int id, Vector3 pos, float yaw)
    {
        int zone = Mathf.Clamp(WorldBuilder.ZoneAt(pos), 1, Mathf.Max(1, zones.Count));
        MazeNav nav = zones.Count > 0 ? zones[zone - 1].nav : null;
        switch (kind)
        {
            case EntityKind.Tour: return Entity.Create<Tour>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Pion: return Entity.Create<Pion>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Cloche: return Entity.Create<Cloche>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Spider: return Entity.Create<Spider>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Fou: return Entity.Create<Fou>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Roi: return Entity.Create<Roi>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Cavalier: return Entity.Create<Cavalier>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.Reveil: return Entity.Create<Reveil>(entitiesRoot, id, pos, yaw, nav);
            case EntityKind.TeteDeCloche: return WatchTete(Entity.Create<TeteDeCloche>(entitiesRoot, id, pos, yaw, nav));
            default: return Entity.Create<Dame>(entitiesRoot, id, pos, yaw, nav);
        }
    }

    static string EntityName(EntityKind kind)
    {
        switch (kind)
        {
            case EntityKind.Tour: return "La Tour";
            case EntityKind.Pion: return "Le Pion";
            case EntityKind.Cloche: return "La Cloche";
            case EntityKind.Spider: return "L'Araignée";
            case EntityKind.Fou: return "Le Fou";
            case EntityKind.Roi: return "Le Roi";
            case EntityKind.Cavalier: return "Le Cavalier";
            case EntityKind.Reveil: return "Le Réveil";
            case EntityKind.TeteDeCloche: return "La Tête de Cloche";
            default: return "La Dame";
        }
    }

    float GroundBelow(Vector3 p)
    {
        float best = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 3f, p.z), Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (IsStructure(hit.transform) && hit.point.y > best) best = hit.point.y;
        return float.IsNegativeInfinity(best) ? 0f : best;
    }

    void ClearEntities()
    {
        foreach (var d in entities.Values) if (d != null) Destroy(d.gameObject);
        entities.Clear();
        watching.Clear();
        pendingSteals.Clear();
        lastEntityTime = double.NegativeInfinity;
    }

    // Copies réutilisées d'une image à l'autre (les entités peuvent apparaître ou disparaître pendant le parcours).
    readonly List<Entity> entityBuffer = new List<Entity>();

    void SimulateEntities()
    {
        if (phase == Phase.Running)
        {
            entityBuffer.Clear();
            entityBuffer.AddRange(entities.Values);
            foreach (var d in entityBuffer) if (d != null) d.Simulate(Time.deltaTime, PlayerPresence.All);
        }
        // Le Réveil a explosé : il quitte la partie (et disparaît chez les autres avec l'envoi suivant).
        entityBuffer.Clear();
        entityBuffer.AddRange(entities.Values);
        foreach (var d in entityBuffer)
            if (d != null && d.Expired)
            {
                entities.Remove(d.Id);
                watching.Remove(d.Id);
                Destroy(d.gameObject);
            }
        entityTimer += Time.unscaledDeltaTime;
        if (entityTimer < EntitySyncInterval) return;
        // Rythme régulier : le temps en trop est reporté sur l'envoi suivant.
        entityTimer = Mathf.Min(entityTimer - EntitySyncInterval, EntitySyncInterval);
        var sb = new StringBuilder("EN|").Append(NetworkGame.Clock()).Append('|');
        bool first = true;
        foreach (var d in entities.Values)
        {
            if (d == null) continue;
            if (!first) sb.Append(';');
            first = false;
            Vector3 p = d.transform.position;
            sb.Append(d.Id).Append(',').Append((int)d.Kind).Append(',').Append(NetworkGame.F(p.x)).Append(',').Append(NetworkGame.F(p.y))
              .Append(',').Append(NetworkGame.F(p.z)).Append(',').Append(NetworkGame.F(d.transform.eulerAngles.y)).Append(',').Append(d.NetState)
              .Append(',').Append(d.TargetId).Append(',').Append(d.Pulse).Append(',').Append(d.Extra);
        }
        PeerNet.BroadcastFast(sb.ToString());
    }

    void UpdateEntities()
    {
        foreach (var d in entities.Values) if (d != null) d.Present(net.IsHost, Time.deltaTime);
    }

    // ---------- Demandes des entités (hôte) ----------

    public void Hit(Entity entity, string targetId, int damage, float knockback)
    {
        Vector3 p = entity.transform.position;
        net.ToPeer(targetId, "EH|" + damage + "|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.z) + "|" + NetworkGame.F(knockback) + "|" + (int)entity.Kind
            + "|" + entity.Id);
    }

    public void Immobilize(Entity entity, string targetId, float seconds) => net.ToPeer(targetId, "DF|" + NetworkGame.F(seconds));

    public void Alert(Entity entity, string targetId) => net.ToPeer(targetId, "TA");

    public void Chain(Entity entity, string targetId, bool on) => net.ToPeer(targetId, "RC|" + entity.Id + "|" + (on ? 1 : 0));

    public IEnumerable<Vector3> BeaconPositions()
    {
        foreach (var b in beacons.Placed)
            if (b != null && (b.Kind == DeviceKind.Beacon || b.Kind == DeviceKind.Enhanced)) yield return b.transform.position;
    }

    public void Swap(Entity entity, string targetId, Vector3 position) =>
        net.ToPeer(targetId, "FS|" + NetworkGame.F(position.x) + "|" + NetworkGame.F(position.y) + "|" + NetworkGame.F(position.z));

    public void Steal(Entity entity, string targetId, System.Action<ItemType> done)
    {
        pendingSteals[entity.Id] = done;
        net.ToPeer(targetId, "PS|" + entity.Id);
    }

    public void DropStolen(Entity entity, ItemType type, Vector3 position)
    {
        if (type == ItemType.None) return;
        AddDropped(type, new Vector3(position.x, position.y + 0.05f, position.z), entity.Id);
    }

    public float NoiseAge(string playerId) => noiseAt.TryGetValue(playerId, out float t) ? Time.time - t : float.MaxValue;

    // ---------- Objets laissés à terre ----------

    string DropMsg(int id, Dropped d) => "IA|" + id + "|" + (int)d.type + "|" + NetworkGame.F(d.pos.x) + "|" + NetworkGame.F(d.pos.y)
        + "|" + NetworkGame.F(d.pos.z) + "|" + d.stolenBy;

    // Hôte : nouvel objet à terre, annoncé à tous.
    void AddDropped(ItemType type, Vector3 pos, int stolenBy)
    {
        int id = DropIdBase + nextDropId++;
        net.ToAll(DropMsg(id, new Dropped { type = type, pos = pos, stolenBy = stolenBy }));
    }

    // Joueur local : l'objet tombe au sol, à moins d'un mètre du personnage.
    void DropNear(ItemType type, Vector3 center)
    {
        Vector2 o = Random.insideUnitCircle * 0.9f;
        Vector3 probe = center + new Vector3(o.x, 0.5f, o.y);
        Vector3 pos = Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point : center + Vector3.down * 0.9f;
        net.ToHost("DR|" + (int)type + "|" + NetworkGame.F(pos.x) + "|" + NetworkGame.F(pos.y) + "|" + NetworkGame.F(pos.z));
    }

    // ---------- La Dame regardée, La Cloche qui obnubile ----------

    // Le joueur chassé signale à l'hôte s'il regarde La Dame (elle accélère alors) ou Le Cavalier (il se fige alors) ;
    // chacun signale si sa caméra voit Le Fou.
    void UpdateWatching()
    {
        Camera cam = Camera.main;
        foreach (var e in entities.Values)
        {
            bool looking = false;
            if (e is Dame d)
            {
                bool chased = d.Current != Dame.State.Wander && d.TargetId == net.MyId && !status.Dead;
                if (chased && cam != null)
                {
                    Vector3 chest = d.transform.position + Vector3.up * d.ChestHeight;
                    Vector3 to = chest - cam.transform.position;
                    looking = Vector3.Angle(cam.transform.forward, to) < 32f && to.magnitude < 40f
                        && !Physics.Linecast(cam.transform.position, chest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                }
            }
            else if (e is Fou fou)
                looking = !status.Dead && !Teleporting && cam != null && CameraSees(cam, fou, GameRules.FouSightRange);
            else if (e is Cavalier cav)
                looking = cav.Current == Cavalier.State.Chase && cav.TargetId == net.MyId && !status.Dead && cam != null
                    && CameraSees(cam, cav, GameRules.CavalierChaseDistance) && FacesCavalier(cav);
            else continue;
            watching.TryGetValue(e.Id, out bool before);
            if (looking == before) continue;
            watching[e.Id] = looking;
            net.ToHost("LK|" + e.Id + "|" + (looking ? 1 : 0));
        }
    }

    // V0.4.10 : à moins de 3 m du Cavalier (avant : 7 m), le personnage doit en plus lui faire face (pas seulement la caméra).
    bool FacesCavalier(Cavalier cav)
    {
        Vector3 to = cav.transform.position - player.transform.position;
        to.y = 0f;
        if (to.magnitude >= GameRules.CavalierFaceDistance) return true;
        Vector3 facing = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
        return to.sqrMagnitude < 0.0001f || Vector3.Angle(facing, to) < GameRules.CavalierFaceAngle;
    }

    // Pulsations cardiaques (GDD 8, Note 02, V0.4.8) : un Cauchemar à moins de 10 m que la caméra voit (marche discrète
    // sans effet sur la distance), ou un Cauchemar qui traque le personnage ; plus il est proche, plus le cœur bat vite.
    const float DreadSightRange = 10f, DreadChaseRange = 30f;

    void UpdateDread()
    {
        Camera cam = Camera.main;
        float dread = 0f;
        if (!status.Dead && !Teleporting && cam != null && !WorldBuilder.IsInHub(player.transform.position))
            foreach (var e in entities.Values)
            {
                if (e == null || !e.Nightmare) continue;
                float d = Vector3.Distance(player.transform.position, e.transform.position);
                // Traqué : la tension ne descend pas sous un plancher, même de loin.
                if (e.TargetId == net.MyId)
                    dread = Mathf.Max(dread, Mathf.Lerp(0.35f, 1f, 1f - Mathf.Clamp01(d / DreadChaseRange)));
                if (d < DreadSightRange && CameraSees(cam, e, DreadSightRange + 6f))
                    dread = Mathf.Max(dread, Mathf.Lerp(0.25f, 1f, 1f - d / DreadSightRange));
            }
        OrbitCamera.Dread = dread;
        // À la mort, le battement de cœur s'estompe progressivement (GDD 12, Battement_Coeur).
        OrbitCamera.Dying = status.Dead;
    }

    // L'entité (Le Fou, Le Cavalier) est à l'écran, à portée, et un point de son corps (tête, buste ou jambes) n'est caché
    // par aucun mur.
    static bool CameraSees(Camera cam, Entity e, float range)
    {
        Vector3 from = cam.transform.position;
        foreach (float h in new[] { e.EyesLocal.y, e.ChestHeight, 0.6f })
        {
            Vector3 point = e.transform.position + Vector3.up * h;
            Vector3 v = cam.WorldToViewportPoint(point);
            if (v.z <= 0f || v.z > range || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
            if (!Physics.Linecast(from, point, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
        }
        return false;
    }

    // Obnubilé par une Cloche ou une Tête de Cloche : la caméra est forcée de la regarder. Une Cloche Décorative qui vient de
    // tinter fixe aussi le regard. V0.6 (note 07 du chapitre 14) : chaque fois qu'un son de cloche obnubile le joueur, il
    // perd 20 de sérénité (plus de dégâts toutes les 2 secondes).
    readonly HashSet<int> obsessedBy = new HashSet<int>();
    readonly List<int> obsessedNow = new List<int>();

    void UpdateCloches()
    {
        Entity nearest = null;
        float best = float.MaxValue;
        obsessedNow.Clear();
        if (!status.Dead && !Teleporting && !InKennel)
            foreach (var e in entities.Values)
                if (e is Cloche c && c.Obsesses(net.MyId) || e is TeteDeCloche t && t.Obsesses(net.MyId))
                {
                    obsessedNow.Add(e.Id);
                    float d = (e.transform.position - player.transform.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = e; }
                }
        // Une Cloche qui vient de l'obnubiler (la Tête de Cloche, elle, à chacun de ses coups : voir WatchTete).
        foreach (int id in obsessedNow)
            if (!obsessedBy.Contains(id) && entities.TryGetValue(id, out var e) && e is Cloche) LoseBellSerenity();
        obsessedBy.Clear();
        foreach (int id in obsessedNow) obsessedBy.Add(id);
        Vector3? bell = BellLook();
        OrbitCamera.ForcedLook = nearest != null ? nearest.transform.position + Vector3.up * (nearest is TeteDeCloche ? nearest.EyesLocal.y : nearest.ChestHeight) : bell;
    }

    void LoseBellSerenity()
    {
        if (status.Dead || Godmode || InKennel) return;
        status.SetSerenity(status.Serenity - GameRules.BellObsessionSerenity);
    }

    // ---------- Balises Améliorées : pas de Couvre Feu à moins de 5 m ----------

    readonly Vector4[] mistHoles = new Vector4[8];

    bool NearEnhancedBeacon(Vector3 p)
    {
        foreach (var b in beacons.Placed)
            if (b != null && b.Kind == DeviceKind.Enhanced)
            {
                Vector3 d = b.transform.position - p;
                d.y = 0f;
                if (d.magnitude < GameRules.EnhancedBeaconRadius) return true;
            }
        return false;
    }

    void UpdateMistHoles()
    {
        int n = 0;
        foreach (var b in beacons.Placed)
            if (b != null && b.Kind == DeviceKind.Enhanced && n < mistHoles.Length)
            {
                Vector3 p = b.transform.position;
                mistHoles[n++] = new Vector4(p.x, p.y, p.z, GameRules.EnhancedBeaconRadius);
            }
        for (int i = n; i < mistHoles.Length; i++) mistHoles[i] = Vector4.zero;
        Shader.SetGlobalVectorArray("_MistHoles", mistHoles);
    }

    // ---------- Station Curative : +10 PV par seconde à moins d'1 m ----------

    void UpdateStations()
    {
        bool near = false;
        if (!status.Dead && !Teleporting && status.Hp < GameRules.MaxHp)
            foreach (var b in beacons.Placed)
                if (b != null && b.Kind == DeviceKind.Station && b.Charged && NearDevice(b)) near = true;
        if (near && !healing) GameAudio.Play2D("heal", 0.5f);
        healing = near;
        if (!near)
        {
            stationAcc = 0f;
            return;
        }
        stationAcc += GameRules.StationHealPerSecond * Time.deltaTime;
        int add = Mathf.FloorToInt(stationAcc);
        if (add <= 0) return;
        stationAcc -= add;
        status.SetHp(status.Hp + add);
    }

    bool NearDevice(Beacon b)
    {
        Vector3 feet = player.transform.position + Vector3.down * 0.9f;
        Vector3 d = b.transform.position - feet;
        return Mathf.Abs(d.y) < 1.2f && new Vector2(d.x, d.z).magnitude < GameRules.DeviceRange;
    }

    // ---------- Téléporteur ----------

    // Moins d'1 m d'un Téléporteur chargé : 3 secondes de canalisation, puis voyage en boule d'énergie le long des liens
    // jusqu'à la Balise principale de son Hub. Pendant le voyage : aucune action (la caméra reste libre), ni dégâts ni perte
    // de sérénité.
    void UpdateTeleport()
    {
        if (Teleporting)
        {
            // La Balise du Hub tombe à 0 : la téléportation se rompt.
            if (!beacons.HubCharged || status.Dead || phase == Phase.GameOver) { EndTeleport(true); return; }
            TravelStep(Time.deltaTime);
            return;
        }
        Beacon pad = null;
        if (!status.Dead && phase != Phase.GameOver && !PlayerController.InputLocked)
            foreach (var b in beacons.Placed)
                if (b != null && b.Kind == DeviceKind.Teleporter && b.Charged && NearDevice(b)) { pad = b; break; }
        if (pad != channelPad)
        {
            channelPad = pad;
            channelTime = 0f;
            // Le son de canalisation dure toute l'attente ; il est coupé si le joueur s'éloigne.
            StopChannelSound();
            if (pad != null) channelSound = GameAudio.Play2D("teleport_charge", 0.6f);
        }
        if (pad == null) return;
        channelTime += Time.deltaTime;
        if (channelTime < GameRules.TeleportChannel) return;
        channelPad = null;
        var path = beacons.PathToHub(pad);
        if (path == null) return;
        StartTeleport(pad, path);
    }

    void StopChannelSound()
    {
        // La source a pu être reprise par un autre son entre-temps : on ne coupe que la canalisation.
        if (channelSound != null && channelSound.isPlaying && channelSound.clip == SoundBank.Get("teleport_charge")) channelSound.Stop();
        channelSound = null;
    }

    void StartTeleport(Beacon pad, List<Vector3> path)
    {
        Teleporting = true;
        presence.Teleporting = true;
        // Avalé par le Téléporteur : la boule part du disque.
        path.Insert(0, pad.transform.position + Vector3.up * 0.9f);
        teleportPath = path;
        teleportLeg = 0;
        // Le trajet est envoyé une fois : les autres joueurs le rejouent eux-mêmes, sans dépendre des positions reçues
        // (à 70 m/s, le moindre paquet en retard faisait saccader la boule).
        var sb = new System.Text.StringBuilder("TS");
        foreach (var p in path) sb.Append('|').Append(NetworkGame.F(p.x)).Append('|').Append(NetworkGame.F(p.y)).Append('|').Append(NetworkGame.F(p.z));
        net.ToHost(sb.ToString());
        player.SetFrozen(true);
        avatar.SetHidden(true);
        if (energyBall == null) energyBall = BuildEnergyBall(player.transform);
        energyBall.SetActive(true);
        terminal.Close();
        postItUI.Close();
        GameAudio.Play3D("teleport", pad.transform.position, 1f, 25f, 0f);
    }

    void TravelStep(float dt)
    {
        float move = GameRules.TeleportSpeed * dt;
        Vector3 pos = player.transform.position;
        while (move > 0f && teleportLeg < teleportPath.Count)
        {
            Vector3 goal = teleportPath[teleportLeg];
            float d = Vector3.Distance(pos, goal);
            if (d <= move) { pos = goal; move -= d; teleportLeg++; }
            else { pos += (goal - pos) / d * move; move = 0f; }
        }
        player.transform.position = pos;
        if (teleportLeg >= teleportPath.Count) EndTeleport(true, true);
    }

    // Fin du voyage : à côté de la Balise du Hub (arrivée) ou là où il s'est rompu.
    void EndTeleport(bool restore, bool arrived = false)
    {
        if (!Teleporting) return;
        Teleporting = false;
        presence.Teleporting = false;
        if (energyBall != null) energyBall.SetActive(false);
        avatar.SetHidden(false);
        if (!restore) return;
        Vector3 pos = player.transform.position;
        if (arrived && teleportPath != null && teleportPath.Count > 0)
        {
            // À côté de la Balise principale du Hub où mène le trajet, vers le centre de ce Hub.
            Beacon root = beacons.NearestHub(teleportPath[teleportPath.Count - 1]);
            Vector3 hubPos = root.transform.position, center = WorldBuilder.HubCenter(root.HubIndex);
            Vector3 toCenter = new Vector3(center.x - hubPos.x, 0f, center.z - hubPos.z).normalized;
            pos = new Vector3(hubPos.x, hubPos.y + 0.95f, hubPos.z) + toCenter * 1.3f;
        }
        player.SetFrozen(false);
        player.Teleport(pos, player.transform.eulerAngles.y);
        if (arrived) GameAudio.Play3D("revive", pos, 0.7f, 20f, 0f, 1.3f);
    }

    // Boule d'énergie : sphère lumineuse et halo bleu, à la place du personnage.
    public static GameObject BuildEnergyBall(Transform parent)
    {
        var ball = new GameObject("BouleEnergie");
        ball.transform.SetParent(parent, false);
        var core = GameArt.Prim(PrimitiveType.Sphere, ball.transform, Vector3.zero, Vector3.one * 0.35f,
            GameArt.Emissive("EnergyCore", new Color(0.6f, 0.85f, 1f), new Color(1.2f, 1.8f, 3f)));
        core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var aura = GameArt.Prim(PrimitiveType.Sphere, ball.transform, Vector3.zero, Vector3.one * 0.8f, GameArt.GhostAura);
        aura.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var light = new GameObject("Lueur").AddComponent<Light>();
        light.transform.SetParent(ball.transform, false);
        light.type = LightType.Point;
        light.color = new Color(0.4f, 0.7f, 1f);
        light.range = 5f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;
        ball.SetActive(false);
        return ball;
    }

    // ================= Actions (interactions) =================

    public void RequestPickup(ItemPickup item)
    {
        if (!HasFreeSlot)
        {
            hud.Toast(Loc.T("Votre inventaire est plein !"));
            return;
        }
        pendingPickups++;
        Gesture(PlayerGesture.PickUp);
        net.ToHost("PK|" + item.Id);
    }

    public void PressButton(int hubIndex = 1)
    {
        // Hubs 2 et suivants : le Bouton Rouge ouvre seulement la porte vers la zone suivante (n'importe quel joueur).
        if (hubIndex > 1)
        {
            net.ToHost("HB|" + hubIndex);
            return;
        }
        if (!net.IsHost)
        {
            hud.Toast(Loc.T("Seul l'hôte de la partie peut appuyer sur le Bouton Rouge."));
            return;
        }
        if (phase != Phase.Waiting) return;
        phase = Phase.Running;
        timeLeft = GameRules.RoundDuration;
        // Le nombre de joueurs au départ fixe la quantité d'objets de valeur (personne n'a encore pu en ramasser).
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        ApplyWorld();
        StartRoundEntities();
        BroadcastState();
    }

    public void RequestTerminal()
    {
        // L'hôte vérifie au moment de la demande si un autre joueur a réellement le Terminal ouvert.
        net.ToHost("TU|1");
    }

    // Côté hôte : le Terminal n'est occupé que si son utilisateur est encore là et l'a vraiment ouvert,
    // ou vient de le recevoir (son état « Terminal ouvert » n'est peut-être pas encore arrivé).
    bool TerminalBusyFor(string requester)
    {
        if (terminalUser.Length == 0 || terminalUser == requester || !net.Roster.ContainsKey(terminalUser)) return false;
        if (Time.time - terminalGrantedAt < TerminalGrantGrace) return true;
        if (terminalUser == net.MyId) return TerminalUI.IsOpen;
        return net.Remotes.TryGetValue(terminalUser, out var rp) && rp.InTerminal && !rp.Dead;
    }

    public void ReleaseTerminal() => net.ToHost("TU|0");

    public void RequestRevive(string playerId, int hubIndex = 1)
    {
        if (status.Dead || playerId == net.MyId) return;
        net.ToHost("RV|" + playerId + "|" + hubIndex);
    }

    // Vente depuis le Terminal : l'objet quitte l'inventaire, l'hôte crédite quota et cagnotte.
    public ItemDef SellSlot(int slot)
    {
        ItemType type = status[slot];
        if (type == ItemType.None) return null;
        status.RemoveAt(slot);
        net.ToHost("SL|" + (int)type);
        GameAudio.Play2D("register", 0.8f);
        return Items.Get(type);
    }

    public void Buy(ItemDef def) => net.ToHost("BY|" + (int)def.type);

    public string Kick(string playerName) => net.Kick(playerName);

    public void Toast(string message) => hud.Toast(message);

    // Aboiement : son spatialisé entendu par tous les joueurs, 3 fois au plus toutes les 10 secondes.
    // Le pitch varie au hasard entre -1 et +1 demi-ton.
    // V0.6 : pendant l'ordre "Aboie !", plus de limite de 3 aboiements.
    void TryBark()
    {
        while (barkTimes.Count > 0 && Time.time - barkTimes.Peek() > GameRules.BarkWindow) barkTimes.Dequeue();
        if (barkTimes.Count >= GameRules.BarkLimit && !OrderActive(2)) return;
        Bark();
    }

    // Aboiement sans limite (il compte tout de même dans les 3 aboiements des 10 dernières secondes).
    void Bark()
    {
        barkTimes.Enqueue(Time.time);
        lastBarkAt = Time.time;
        float pitch = Mathf.Pow(2f, Random.Range(-1f, 1f) / 12f);
        PlayBark(player.transform, pitch);
        net.ToHost("BK|" + NetworkGame.F(pitch));
    }

    static void PlayBark(Transform who, float pitch)
    {
        GameAudio.Play3DSmooth("bark", who.position + Vector3.up * 0.8f, 1f, GameRules.BarkRange, pitch);
        if (who.TryGetComponent(out CharacterAvatar avatar)) avatar.PlayGesture(PlayerGesture.Bark);
    }

    // /skip round : si le quota est atteint, l'hôte passe à la manche suivante. Retourne un refus, ou null si envoyé.
    public bool CanSkipRound => phase == Phase.Running && progress >= quota;

    // 'hubIndex' : Bouton Skip pressé (confettis à côté de lui), 0 depuis le Terminal.
    public string RequestSkipRound(int hubIndex = 0)
    {
        if (phase != Phase.Running) return Loc.T("Aucune manche n'est en cours.");
        if (!CanSkipRound) return Loc.T("Le quota de la manche n'est pas encore atteint.");
        net.ToHost("SK|" + hubIndex);
        return null;
    }

    // /tp (hôte) : téléporte le personnage à 3 mètres de l'ennemi le plus proche ("enemy"), de la porte entre la Zone 1 et la
    // Zone 2 ("door"), d'un joueur (son pseudo), ou dans la zone N ("/tp N", V0.6 : générée d'abord si besoin). Retourne le
    // message à afficher, ou null si la commande est invalide.
    // Le personnage est ensuite invincible pendant quelques secondes.
    public string Teleport(string target)
    {
        string result = TeleportTo(target);
        if (result != null)
        {
            // Pendant 5 secondes : ni dégâts, ni immobilisation (y compris par La Dame).
            invincibleUntil = Time.time + GameRules.TeleportInvincibility;
            player.ReleaseImmobilize();
        }
        return result;
    }

    string TeleportTo(string target)
    {
        if (!net.IsHost || status.Dead) return null;
        Vector3 here = player.transform.position;
        string lower = target.ToLowerInvariant();
        if (lower == "enemy")
        {
            Entity nearest = null;
            foreach (var d in entities.Values)
                if (d != null && (nearest == null || (d.transform.position - here).sqrMagnitude < (nearest.transform.position - here).sqrMagnitude))
                    nearest = d;
            if (nearest == null) return null;
            TeleportNear(nearest.transform.position);
            return Loc.F("Téléporté près de {0}.", nearest.DisplayName);
        }
        if (int.TryParse(lower, out int zoneNumber))
        {
            if (zoneNumber < 1 || zoneNumber > MaxTeleportZone) return null;
            if (zoneNumber > zoneCount)
            {
                zoneCount = zoneNumber;
                ApplyWorld();
                BroadcastState();
            }
            Vector3 at = zones[zoneNumber - 1].entrance;
            player.Teleport(new Vector3(at.x, GroundBelow(at) + 0.95f, at.z), 0f);
            return Loc.F("Téléporté dans la Zone {0}.", zoneNumber.ToString());
        }
        if (lower == "door")
        {
            if (zones.Count == 0 || zones[0].door == null) return null;
            Transform door = zones[0].door.transform;
            // Côté Zone 1, face à la porte.
            Vector3 p = door.position + door.forward * 3f;
            player.Teleport(new Vector3(p.x, GroundBelow(p) + 0.95f, p.z), door.eulerAngles.y + 180f);
            return Loc.T("Téléporté devant la porte de la Zone 2.");
        }
        foreach (var rp in net.Remotes.Values)
        {
            if (rp == null || !string.Equals(rp.PlayerName, target, System.StringComparison.OrdinalIgnoreCase)) continue;
            TeleportNear(rp.transform.position);
            return Loc.F("Téléporté près de {0}.", rp.PlayerName);
        }
        return null;
    }

    // Point libre à 3 m de 'target' (sans mur entre les deux), posé au sol, personnage tourné vers la cible.
    void TeleportNear(Vector3 target)
    {
        Vector3 from = target + Vector3.up * 1f;
        Vector3 spot = target;
        for (int k = 0; k < 8; k++)
        {
            float a = k * 45f * Mathf.Deg2Rad;
            Vector3 candidate = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3f;
            if (Physics.Linecast(from, candidate + Vector3.up * 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            spot = candidate;
            break;
        }
        float ground = Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point.y : target.y;
        Vector3 look = target - spot;
        player.Teleport(new Vector3(spot.x, ground + 0.95f, spot.z), look.sqrMagnitude > 0.01f ? Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg : 0f);
    }

    // ================= Hôte =================

    string StateMsg() => "G|" + seed + "|" + round + "|" + quota + "|" + progress + "|" + cash + "|" + (int)phase + "|" + NetworkGame.F(timeLeft)
        + "|" + (compass ? 1 : 0) + "|" + JoinIds(brokenOrbs) + "|" + NetworkGame.F(beacons.Charge) + "|" + NetworkGame.F(valueBoost) + "|" + testFlags
        + "|" + zoneCount + "|" + JoinIds(openedHubs) + "|" + JoinIds(openedAcquisitions);

    static string JoinIds(HashSet<int> ids) => string.Join(".", ids);

    static void ReadIds(string field, HashSet<int> ids)
    {
        ids.Clear();
        foreach (string id in field.Split('.')) if (id.Length > 0) ids.Add(NetworkGame.I(id));
    }

    void BroadcastState()
    {
        syncTimer = 0f;
        PeerNet.Broadcast(StateMsg());
        UpdateScreens();
    }

    // Tout ce qu'un joueur qui arrive en cours de partie doit connaître.
    public void SendSnapshot(string to)
    {
        net.ToPeer(to, StateMsg());
        var ids = new StringBuilder();
        foreach (int id in picked) { if (ids.Length > 0) ids.Append(','); ids.Append(id); }
        var dug = new StringBuilder();
        foreach (int id in dugMounds) { if (dug.Length > 0) dug.Append(','); dug.Append(id); }
        net.ToPeer(to, "ML|" + round + "|" + dug);
        net.ToPeer(to, "IL|" + round + "|" + ids);
        foreach (string line in logs) net.ToPeer(to, "LG|" + line);
        net.ToPeer(to, "TS|" + terminalUser);
        net.ToPeer(to, StatsMsg());
        foreach (string note in noteMessages.Values) net.ToPeer(to, note);
        // Appareils déjà posés : sans animation de pose ni tintement.
        foreach (string beacon in beaconMessages.Values) net.ToPeer(to, "BV" + beacon.Substring(2));
        SendObjectSnapshot(to);
        foreach (var kv in dropped) net.ToPeer(to, DropMsg(kv.Key, kv.Value));
        net.ToPeer(to, PogoMsg(""));
        SendGlyphSnapshot(to);
        SendArcadeSnapshot(to);
    }

    // Journal : modèle français et arguments, traduits à l'affichage chez chaque joueur (Loc.Line).
    void Log(string fr, params string[] args) => LogN(-1, fr, args);

    void LogN(int n, string fr, params string[] args)
    {
        string line = PeerNet.LocalNow.ToString("HH:mm") + " : " + Loc.Encode(fr, n, args);
        net.ToAll("LG|" + line);
    }

    // Le Couvre Feu se répand quand le Compteur de Temps affiche 3 minutes (V0.6).
    void UpdateHostCurfew()
    {
        bool active = CurfewActive;
        if (active && !hostCurfew)
        {
            Log("Le Couvre Feu se répand ... Le désespoir attend les plus faibles.");
            SpawnSpiders();
        }
        hostCurfew = active;
    }

    public void OnPlayerJoined(string id)
    {
        if (net.Roster.TryGetValue(id, out var info)) Log("Le joueur {0} est connecté.", info.name);
        OnRosterChanged();
    }

    public void OnPlayerLeft(string id, string playerName)
    {
        if (net.IsHost)
        {
            Log("Le joueur {0} est déconnecté.", playerName);
            if (terminalUser == id) net.ToAll("TS|");
        }
        OnRosterChanged();
    }

    void EndRound()
    {
        if (WipedOut) return;
        if (progress >= quota)
        {
            int done = round;
            round++;
            quota = GameRules.QuotaForRound(round);
            progress = 0;
            timeLeft = GameRules.RoundDuration;
            picked.Clear();
            valueBoost = GameRules.ValueBoost(net.Roster.Count);
            Log("Le quota de la manche {0} est validé ! Nouveau quota : {1}", done.ToString(), quota.ToString());
            if (hostCurfew) Log("Le Couvre Feu se retire ... Pour le moment.");
            hostCurfew = false;
            ResetRoundStats(round);
            ApplyWorld();
            StartRoundEntities();
            BroadcastState();
        }
        else
        {
            phase = Phase.GameOver;
            timeLeft = 0f;
            hostCurfew = false;
            ClearEntities();
            // Le Terminal, fermé sans être libéré, redevient libre pour tout le monde.
            net.ToAll("TS|");
            BroadcastState();
            OnGameOver();
        }
    }

    void RestartGame()
    {
        if (!net.IsHost) return;
        seed = Random.Range(1, int.MaxValue);
        round = 1;
        quota = GameRules.QuotaForRound(1);
        progress = cash = 0;
        phase = Phase.Waiting;
        timeLeft = GameRules.RoundDuration;
        compass = false;
        ClearProgress();
        picked.Clear();
        noteMessages.Clear();
        notePlacedAt.Clear();
        dropped.Clear();
        testFlags = 0;
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        ResetRoundStats(1);
        ResetHubFixtures();
        net.ToAll("TS|");
        net.ToAll(PogoMsg(""));
        ApplyWorld();
        BroadcastState();
        ResetGlyphs();
        ClearArcadeState();
        OnRestart();
        // Après OnRestart (qui retire les appareils posés) : les objets uniques de la nouvelle partie.
        PlaceUniqueObjects();
    }

    // Tous les joueurs connectés sont morts : fondu au noir, puis GAME OVER et retour au lobby (décidé par l'hôte, message WO).
    public bool WipedOut { get; private set; }
    float allDeadSince = -1f;
    // Laisse la dernière mort se jouer avant le fondu.
    const float WipeOutDelay = 2f;

    void UpdateHostWipeOut()
    {
        bool allDead = !WipedOut && phase != Phase.GameOver && net.Roster.Count > 0;
        if (allDead)
            foreach (var id in net.Roster.Keys)
                if (!IsDeadForHost(id)) { allDead = false; break; }
        if (!allDead)
        {
            allDeadSince = -1f;
            return;
        }
        if (allDeadSince < 0f) allDeadSince = Time.time;
        if (Time.time - allDeadSince >= WipeOutDelay)
        {
            net.ToAll("TS|");
            net.ToAll("WO");
        }
    }

    void OnWipeOut()
    {
        if (WipedOut) return;
        WipedOut = true;
        terminal.Close(false);
        postItUI.Close();
        OrbitCamera.LockCursor(false);
        hud.PlayWipeOut();
        GameAudio.PlayWipeOutSequence(GameHud.WipeFadeDuration, GameHud.WipeSilence);
    }

    // Quota non atteint : tous les personnages meurent.
    void OnGameOver()
    {
        if (WipedOut) return;
        terminal.Close(false);
        postItUI.Close();
        ClearEntities();
        if (!status.Dead) KillLocal(null);
        OrbitCamera.LockCursor(false);
        // Manche, temps, quota et cagnotte ne s'affichent que sur les écrans du chapiteau.
        hud.ShowGameOver(Loc.T("Le quota n'a pas été atteint.") + (net.IsHost ? "" : "\n" + Loc.T("En attente de l'hôte pour une nouvelle partie…")),
            net.IsHost);
    }

    void OnRestart()
    {
        hud.HideGameOver();
        dropped.Clear();
        status.ClearInventory();
        GiveStartingItems();
        pendingPickups = 0;
        ClearNotes();
        LeaveKennel(false);
        // Nouvelle partie : les Balises posées disparaissent, celles des Hubs sont rechargées.
        beacons.Clear();
        beacons.Charge = GameRules.BeaconCharge;
        myTvs.Clear();
        tvAsked.Clear();
        bellObsession = -1;
        ReviveLocal(SpawnPoint(), GameRules.MaxHp);
    }

    // ================= Réception =================

    public void HandleMessage(string from, string[] f, string raw)
    {
        bool host = net.IsHost;
        if (HandleZoneMessage(from, f, host)) return;
        if (HandleCannonMessage(from, f, host)) return;
        if (HandleObjectMessage(from, f, host)) return;
        if (HandleArcadeMessage(from, f, host)) return;
        switch (f[0])
        {
            case "G" when !host && f.Length >= 10:
            {
                Phase before = phase;
                seed = NetworkGame.I(f[1]);
                round = NetworkGame.I(f[2]);
                quota = NetworkGame.I(f[3]);
                progress = NetworkGame.I(f[4]);
                cash = NetworkGame.I(f[5]);
                phase = (Phase)Mathf.Clamp(NetworkGame.I(f[6]), 0, 2);
                timeLeft = NetworkGame.P(f[7]);
                compass = f[8] == "1";
                ReadIds(f[9], brokenOrbs);
                if (f.Length >= 11) beacons.Charge = NetworkGame.P(f[10]);
                if (f.Length >= 12) valueBoost = Mathf.Clamp(NetworkGame.P(f[11]), 1f, 3f);
                if (f.Length >= 13) testFlags = NetworkGame.I(f[12]);
                if (f.Length >= 14) zoneCount = Mathf.Max(1, NetworkGame.I(f[13]));
                if (f.Length >= 15) ReadIds(f[14], openedHubs);
                if (f.Length >= 16) ReadIds(f[15], openedAcquisitions);
                // Nouvelle partie : les critères remplis de notre côté repartent de zéro.
                if (testFlags == 0) sentFlags = 0;
                ApplyWorld();
                if (net.InGame)
                {
                    if (before != Phase.GameOver && phase == Phase.GameOver) OnGameOver();
                    else if (before == Phase.GameOver && phase != Phase.GameOver) OnRestart();
                }
                break;
            }
            case "WO":
                if (net.InGame) OnWipeOut();
                break;
            // ---------- Monticules ----------
            case "MD" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                if (mounds.ContainsKey(id) && !dugMounds.Contains(id) && !digEnds.ContainsKey(id))
                {
                    digEnds[id] = Time.time + GameRules.DigDuration;
                    noiseAt[from] = Time.time;
                    net.ToAll("MS|" + round + "|" + id);
                }
                else net.ToPeer(from, "MN|" + id);
                break;
            }
            case "MS" when f.Length >= 3:
                if (NetworkGame.I(f[1]) == round && mounds.TryGetValue(NetworkGame.I(f[2]), out var digMound) && digMound != null)
                    digMound.StartDigging();
                break;
            case "MN" when f.Length >= 2:
                if (localDig == NetworkGame.I(f[1])) EndLocalDig();
                break;
            case "MO" when f.Length >= 3:
                if (NetworkGame.I(f[1]) == round) OpenMound(NetworkGame.I(f[2]), true);
                break;
            case "ML" when !host && f.Length >= 3:
                if (NetworkGame.I(f[1]) != round) break;
                foreach (string s in f[2].Split(','))
                    if (s.Length > 0) OpenMound(NetworkGame.I(s), false);
                break;
            case "IL" when !host && f.Length >= 3:
                if (NetworkGame.I(f[1]) != round) break;
                foreach (string s in f[2].Split(','))
                    if (s.Length > 0) RemoveItem(NetworkGame.I(s));
                break;
            case "IR" when f.Length >= 3:
                if (NetworkGame.I(f[1]) != round) break;
                if (items.TryGetValue(NetworkGame.I(f[2]), out var taken) && taken != null)
                    GameAudio.Play3D(taken.Type == ItemType.Canard ? "duck" : "pop", taken.transform.position, 0.9f, 15f, 0.1f);
                RemoveItem(NetworkGame.I(f[2]));
                break;
            case "PK" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                if (items.TryGetValue(id, out var it) && it != null && !picked.Contains(id))
                {
                    picked.Add(id);
                    noiseAt[from] = Time.time;
                    // Ramasser un objet volé par un Pion : il attaque sur-le-champ.
                    if (dropped.TryGetValue(id, out var drop) && drop.stolenBy >= 0
                        && entities.TryGetValue(drop.stolenBy, out var thief) && thief is Pion pion)
                        pion.Provoke(from);
                    net.ToAll("IR|" + round + "|" + id);
                    net.ToPeer(from, "GV|" + (int)it.Type);
                }
                else net.ToPeer(from, "GV|0");
                break;
            }
            case "GV" when f.Length >= 2:
            {
                pendingPickups = Mathf.Max(0, pendingPickups - 1);
                var type = (ItemType)NetworkGame.I(f[1]);
                if (type != ItemType.None && !status.Dead) status.Add(type);
                break;
            }
            case "SL" when host && f.Length >= 2:
            {
                ItemDef def = Items.Get(NetworkGame.I(f[1]));
                if (def == null) break;
                progress += def.sellPrice;
                cash += def.sellPrice;
                StatsOf(from).golds += def.sellPrice;
                net.ToAll(StatsMsg());
                Log("Le joueur {0} a vendu {1} pour {2}.", NameOf(from), Loc.Tr(def.frName), Loc.Pr(def.sellPrice));
                BroadcastState();
                break;
            }
            case "BY" when host && f.Length >= 2:
            {
                ItemDef def = Items.Get(NetworkGame.I(f[1]));
                if (def == null || def.buyPrice <= 0) break;
                // La Boussole ne s'achète qu'une fois, pour toute l'équipe.
                if (def.type == ItemType.Boussole && compass)
                {
                    net.ToPeer(from, "BO|0|" + (int)def.type + "|" + cash + "|owned");
                    break;
                }
                // En godmod, l'hôte achète pour 0 gold.
                int price = from == net.MyId && Godmode ? 0 : def.buyPrice;
                if (cash >= price)
                {
                    cash -= price;
                    if (def.type == ItemType.Boussole) compass = true;
                    Log("Le joueur {0} a acheté {1} pour {2}.", NameOf(from), Loc.Tr(def.frName), Loc.Pr(price));
                    BroadcastState();
                    net.ToPeer(from, "BO|1|" + (int)def.type + "|" + cash);
                }
                else net.ToPeer(from, "BO|0|" + (int)def.type + "|" + cash);
                break;
            }
            case "BO" when f.Length >= 4:
            {
                bool ok = f[1] == "1";
                ItemDef def = Items.Get(NetworkGame.I(f[2]));
                if (ok && def != null && def.inventory) status.Add(def.type);
                if (ok && def != null && def.type == ItemType.Boussole) compass = true;
                GameAudio.Play2D(ok ? "register" : "error", 0.7f, 0f, ok ? 1.15f : 1f);
                terminal.OnBuyResult(ok, def, NetworkGame.I(f[3]), f.Length >= 5 && f[4] == "owned");
                break;
            }
            case "TU" when host && f.Length >= 2:
                if (f[1] == "1")
                {
                    if (!TerminalBusyFor(from))
                    {
                        terminalGrantedAt = Time.time;
                        net.ToAll("TS|" + from);
                        net.ToPeer(from, "TG|1");
                    }
                    else net.ToPeer(from, "TG|0");
                }
                else if (terminalUser == from) net.ToAll("TS|");
                break;
            case "TG" when f.Length >= 2:
                if (f[1] == "1" && !status.Dead && phase != Phase.GameOver) terminal.Open();
                else if (f[1] == "1") ReleaseTerminal();
                else hud.Toast(Loc.T("Un autre joueur utilise déjà le Terminal"));
                break;
            case "TS" when f.Length >= 2:
                terminalUser = f[1];
                UpdateScreens();
                break;
            case "RV" when host && f.Length >= 2:
            {
                string target = f[1];
                if (target == from || !net.Roster.ContainsKey(target) || IsDeadForHost(from) || !IsDeadForHost(target)) break;
                net.ToAll("RA|" + target + "|" + from + "|" + (f.Length >= 3 ? f[2] : "1"));
                Log("Le joueur {0} a été réanimé par {1}.", NameOf(target), NameOf(from));
                if (net.Remotes.TryGetValue(target, out var rp)) rp.SetStatus(AnimState.Idle, 1, 0);
                break;
            }
            case "RA" when f.Length >= 3:
            {
                int at = f.Length >= 4 ? NetworkGame.I(f[3]) : 1;
                GameAudio.Play3D("revive", KennelSpawn(f[1], at), 0.9f, 30f, 0f);
                if (f[1] == net.MyId && status.Dead && phase != Phase.GameOver)
                {
                    ReviveLocal(KennelSpawn(net.MyId, at), 1);
                    hud.Toast(Loc.F("{0} vous a réanimé !", NameOf(f[2])));
                }
                break;
            }
            case "DE" when host && f.Length >= 2:
                StatsOf(from).deaths++;
                net.ToAll(StatsMsg());
                Log("Le joueur {0} est mort par {1}.", NameOf(from), Loc.Tr(f[1]));
                break;
            case "SC" when f.Length >= 3:
                // L'hôte tient déjà ses statistiques à jour.
                if (host) break;
                statsRound = NetworkGame.I(f[1]);
                roundStats.Clear();
                foreach (string entry in f[2].Split(';'))
                {
                    string[] p = entry.Split(',');
                    if (p.Length >= 3) roundStats[p[0]] = new RoundStats { golds = NetworkGame.I(p[1]), deaths = NetworkGame.I(p[2]) };
                }
                UpdateScreens();
                break;
            case "LG" when raw.Length > 3:
                logs.Add(raw.Substring(3));
                if (logs.Count > GameRules.LogLines) logs.RemoveRange(0, logs.Count - GameRules.LogLines);
                UpdateScreens();
                break;

            // ---------- Post-It ----------
            case "PP" when host && f.Length >= 8:
            {
                noiseAt[from] = Time.time;
                string text = SanitizeNote(f[7]);
                if (text.Length == 0) break;
                int id = nextNoteId++;
                string msg = "PA|" + id + "|" + f[1] + "|" + f[2] + "|" + f[3] + "|" + f[4] + "|" + f[5] + "|" + f[6] + "|" + text;
                noteMessages[id] = msg;
                notePlacedAt[id] = Time.time;
                net.ToAll(msg);
                break;
            }
            case "PA" when f.Length >= 9:
            {
                int id = NetworkGame.I(f[1]);
                if (notes.ContainsKey(id)) break;
                var pos = new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4]));
                var normal = new Vector3(NetworkGame.P(f[5]), NetworkGame.P(f[6]), NetworkGame.P(f[7])).normalized;
                notes[id] = PostItNote.Create(notesRoot, id, pos, normal, SanitizeNote(f[8]));
                GameAudio.Play3D("postit", pos, 0.7f, 12f, 0.1f);
                break;
            }
            case "PD" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                // Pas de retrait pendant les 2 secondes qui suivent la pose.
                if (notePlacedAt.TryGetValue(id, out float placed) && Time.time - placed < Items.PostItLockSeconds) break;
                notePlacedAt.Remove(id);
                if (noteMessages.Remove(id)) net.ToAll("PX|" + id);
                break;
            }
            case "PX" when f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                if (notes.TryGetValue(id, out var note) && note != null)
                {
                    GameAudio.Play3D("postit", note.transform.position, 0.6f, 12f, 0.1f, 0.8f);
                    Destroy(note.gameObject);
                }
                notes.Remove(id);
                break;
            }

            // ---------- Balises ----------
            case "BN" when host && f.Length >= 4:
            {
                noiseAt[from] = Time.time;
                var kind = f.Length >= 5 ? (DeviceKind)Mathf.Clamp(NetworkGame.I(f[4]), 0, (int)Beacon.LastKind) : DeviceKind.Beacon;
                var pos = new Vector3(NetworkGame.P(f[1]), NetworkGame.P(f[2]), NetworkGame.P(f[3]));
                PlaceDevice(kind, pos, f.Length >= 6 ? NetworkGame.P(f[5]) : Random.Range(0f, 360f), from, f.Length >= 7 && f[6] == "1");
                break;
            }
            // BV : appareil déjà posé (joueur qui arrive), sans animation.
            case "BW" when f.Length >= 5:
            case "BV" when f.Length >= 5:
            {
                bool live = f[0] == "BW";
                var pos = new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4]));
                var kind = f.Length >= 6 ? (DeviceKind)Mathf.Clamp(NetworkGame.I(f[5]), 0, (int)Beacon.LastKind) : DeviceKind.Beacon;
                float yaw = f.Length >= 7 ? NetworkGame.P(f[6]) : float.NaN;
                string placer = f.Length >= 8 ? f[7] : "";
                bool hanging = f.Length >= 9 && f[8] == "1";
                Beacon placedDevice = beacons.Add(NetworkGame.I(f[1]), pos, kind, yaw, hanging);
                if (live && placer.Length > 0) GameAudio.Play3D("pop", pos, 0.8f, 15f, 0.1f, 0.8f);
                OnDevicePlaced(placedDevice, placer, live);
                break;
            }
            case "BG" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                Beacon device = beacons.Get(id);
                // Un Canon occupé ne se ramasse pas.
                if (device != null && !CannonOccupied(id) && beaconMessages.Remove(id))
                {
                    noiseAt[from] = Time.time;
                    tvVideos.Remove(id);
                    net.ToAll("BZ|" + id);
                    net.ToPeer(from, "GV|" + (int)device.Item);
                }
                else net.ToPeer(from, "GV|0");
                break;
            }
            case "BZ" when f.Length >= 2:
            {
                Beacon b = beacons.Get(NetworkGame.I(f[1]));
                if (b != null) GameAudio.Play3D("pop", b.transform.position, 0.8f, 15f, 0.1f);
                beacons.Remove(NetworkGame.I(f[1]));
                myTvs.Remove(NetworkGame.I(f[1]));
                break;
            }
            case "BC" when host:
                beacons.Charge = GameRules.BeaconCharge;
                net.ToAll("BR");
                BroadcastState();
                break;
            case "BR":
                foreach (var h in beacons.Hubs) if (h != null) GameAudio.Play3D("orb_on", h.Top, 1f, 30f, 0f, 1.2f);
                break;

            // ---------- Pogo ----------
            case "JO" when host:
                if (pogoMood != Pogo.Mood.Closed) break;
                if (phase != Phase.Running) net.ToPeer(from, "JN|0");
                else if (Time.time < pogoCooldownUntil) net.ToPeer(from, "JN|1");
                else
                {
                    pogoActiveAt = Time.time;
                    SetPogo(Pogo.Mood.Open);
                }
                break;
            case "JS" when host && f.Length >= 2:
            {
                ItemDef def = Items.Get(NetworkGame.I(f[1]));
                if (def == null) break;
                progress += def.sellPrice;
                cash += def.sellPrice;
                StatsOf(from).golds += def.sellPrice;
                net.ToAll(StatsMsg());
                Log("Le joueur {0} a vendu {1} pour {2}.", NameOf(from), Loc.Tr(def.frName), Loc.Pr(def.sellPrice));
                // Sujet de test : un objet vendu à Pogo.
                testFlags |= TestPogoSale;
                // V0.6.1 : dès que la Zone 3 est accessible, un objet des monticules vendu rapporte un Jeton WOOF une fois sur dix.
                if (Items.FromMound(def.type) && ZoneUnlocked(3) && Random.value < GameRules.TokenChance) net.ToPeer(from, "JT");
                BroadcastState();
                // Un achat le rend heureux (3 s de sourire) et le calme.
                pogoActiveAt = Time.time;
                pogoSmileUntil = Time.time + GameRules.PogoSmile;
                SetPogo(Pogo.Mood.Open);
                break;
            }
            case "JE" when host:
                if (pogoMood == Pogo.Mood.Open) SetPogo(Pogo.Mood.Angry);
                else if (pogoMood == Pogo.Mood.Angry)
                {
                    // Coup de tête mortel, puis il rentre dans sa boîte : 5 s avant de pouvoir le rappeler.
                    pogoCooldownUntil = Time.time + GameRules.PogoCooldown;
                    pogoSmileUntil = 0f;
                    pogoKills++;
                    SetPogo(Pogo.Mood.Closed, from);
                    net.ToPeer(from, "JK");
                }
                break;
            case "JX" when f.Length >= 5:
                foreach (var p in pogos) if (p != null)
                {
                    p.SetState((Pogo.Mood)Mathf.Clamp(NetworkGame.I(f[1]), 0, 2), f[2] == "1", f[3]);
                    p.SetKills(NetworkGame.I(f[4]));
                }
                break;
            case "JK":
                if (!status.Dead && !Invincible)
                {
                    // Droit dans les yeux de Pogo, puis la mort (la caméra ne quitte le corps qu'après).
                    OrbitCamera.StartJumpscare(NearestPogo().Head, Pogo.EyesLocal);
                    KillLocal("Pogo");
                    deathAt = Time.time + 2f;
                }
                break;
            case "JN" when f.Length >= 2:
                GameAudio.Play2D("error", 0.35f);
                break;

            // ---------- Orbes ----------
            case "OB" when host && f.Length >= 3:
            {
                int orb = NetworkGame.I(f[1]);
                int door = DoorOfOrb(orb);
                // En godmod, l'hôte brise aussi un orbe éteint, sans Clé.
                bool god = from == net.MyId && Godmode;
                bool ok = door > 0 && phase != Phase.GameOver && (god || phase == Phase.Running && GameRules.OrbActive(orb, round) && NetworkGame.I(f[2]) >= 0)
                    && !brokenOrbs.Contains(orb);
                net.ToPeer(from, "OU|" + (ok ? f[2] : "-1"));
                if (!ok) break;
                brokenOrbs.Add(orb);
                Log("Le joueur {0} a brisé l'orbe n°{1}.", NameOf(from), (orb - GameRules.FirstOrb(door) + 1).ToString());
                if (DoorOpen(door)) Log("Les orbes sont brisés : la porte de la Zone {0} s'ouvre !", (door * 2).ToString());
                UpdateDoors(true);
                BroadcastState();
                break;
            }
            case "HB" when host && f.Length >= 2:
            {
                int h = NetworkGame.I(f[1]);
                if (h < 2 || h > hubs.Count || openedHubs.Contains(h) || phase == Phase.GameOver) break;
                openedHubs.Add(h);
                UpdateDoors(true);
                BroadcastState();
                break;
            }
            case "OU" when f.Length >= 2:
            {
                int slot = NetworkGame.I(f[1]);
                if (slot >= 0 && slot < GameRules.InventorySize && status[slot] == ItemType.Cle)
                    status.RemoveAt(slot);
                else if (slot < 0) hud.Toast(Loc.T("Cet orbe ne peut pas être brisé."));
                break;
            }

            // ---------- Aboiements ----------
            case "BK" when host && f.Length >= 2:
            {
                noiseAt[from] = Time.time;
                float pitch = Mathf.Clamp(NetworkGame.P(f[1]), 0.9f, 1.1f);
                PeerNet.Broadcast("BA|" + from + "|" + NetworkGame.F(pitch));
                // L'hôte l'entend aussi (son propre aboiement est déjà joué).
                if (from != net.MyId && net.Remotes.TryGetValue(from, out var hostHeard) && hostHeard != null)
                    PlayBark(hostHeard.transform, pitch);
                break;
            }
            case "BA" when f.Length >= 3:
                if (f[1] != net.MyId && net.Remotes.TryGetValue(f[1], out var barker) && barker != null)
                    PlayBark(barker.transform, NetworkGame.P(f[2]));
                break;

            // ---------- Gestes (ramasser, poser, utiliser, manger) ----------
            case "GE" when host && f.Length >= 2:
            {
                int g = NetworkGame.I(f[1]);
                if (g <= (int)PlayerGesture.Bark || g > (int)PlayerGesture.Eat) break;
                PeerNet.Broadcast("GS|" + from + "|" + g);
                if (from != net.MyId && net.Remotes.TryGetValue(from, out var gesturer) && gesturer != null
                    && gesturer.TryGetComponent(out CharacterAvatar ga))
                    ga.PlayGesture((PlayerGesture)g);
                break;
            }
            case "GS" when f.Length >= 3:
                if (f[1] != net.MyId && net.Remotes.TryGetValue(f[1], out var other) && other != null
                    && other.TryGetComponent(out CharacterAvatar oa))
                    oa.PlayGesture((PlayerGesture)NetworkGame.I(f[2]));
                break;

            // ---------- Passer la manche ----------
            case "SK" when host:
            {
                if (phase != Phase.Running || progress < quota)
                {
                    net.ToPeer(from, "SR|-1");
                    break;
                }
                // Bouton Skip (V0.6) : des confettis explosent joyeusement à côté de lui, chez tous.
                if (f.Length >= 2 && NetworkGame.I(f[1]) > 0) net.ToAll("SX|" + f[1]);
                // 10 croquettes par tranche de 10 secondes restant au Compteur de Temps.
                int bonus = Mathf.FloorToInt(timeLeft / 10f) * 10;
                cash += bonus;
                LogN(bonus, "Le joueur {0} passe la manche {1} : {2} ajoutée à la cagnotte.|Le joueur {0} passe la manche {1} : {2} ajoutées à la cagnotte.",
                    NameOf(from), round.ToString(), Loc.Pr(bonus));
                net.ToPeer(from, "SR|" + bonus);
                EndRound();
                break;
            }
            case "SR" when f.Length >= 2:
                terminal.OnSkipResult(NetworkGame.I(f[1]));
                break;
            case "SX" when f.Length >= 2:
            {
                int h = NetworkGame.I(f[1]);
                if (h >= 1 && h <= skipButtons.Count && skipButtons[h - 1] != null)
                {
                    Transform at = skipButtons[h - 1].transform;
                    foreach (float side in new[] { -1f, 1f })
                        Fx.CircusConfetti(at.position + at.right * side * 0.9f + Vector3.down * 0.8f);
                }
                break;
            }

            // ---------- Entités ----------
            case "EN" when !host && f.Length >= 3:
                ReceiveEntities(NetworkGame.D(f[1]), f[2]);
                break;
            case "LK" when host && f.Length >= 3:
                if (entities.TryGetValue(NetworkGame.I(f[1]), out var watched) && watched is Dame watchedDame && watched.TargetId == from)
                    watchedDame.Watched = f[2] == "1";
                else if (watched is Fou watchedFou) watchedFou.SetLooking(from, f[2] == "1");
                else if (watched is Cavalier watchedCav && watched.TargetId == from) watchedCav.Watched = f[2] == "1";
                break;
            case "RC" when f.Length >= 3:
            {
                int roiId = NetworkGame.I(f[1]);
                if (f[2] != "1") { if (chainedBy == roiId) ReleaseChain(); break; }
                // Invincible (godmod, /tp, boule d'énergie) : la chaîne se rompt aussitôt.
                if (status.Dead || Invincible || Teleporting) { net.ToHost("RB|" + roiId); break; }
                chainedBy = roiId;
                chainedAt = Time.time;
                chainPresses = 0;
                break;
            }
            case "RB" when host && f.Length >= 2:
                if (entities.TryGetValue(NetworkGame.I(f[1]), out var chainer) && chainer is Roi chainRoi) chainRoi.BreakFree(from);
                break;
            case "FS" when f.Length >= 4:
                // Le Fou prend votre place : vous voilà à la sienne, la vue secouée en zoom, sous une pluie de confettis.
                if (!status.Dead && !Invincible && !Teleporting)
                {
                    var feet = new Vector3(NetworkGame.P(f[1]), NetworkGame.P(f[2]), NetworkGame.P(f[3]));
                    player.Teleport(feet + Vector3.up * 0.95f, player.transform.eulerAngles.y);
                    OrbitCamera.ZoomPunch();
                    Fx.CircusConfetti(feet);
                }
                break;
            case "DF" when f.Length >= 2:
                if (!status.Dead && !Invincible && !Teleporting) player.Immobilize(NetworkGame.P(f[1]));
                break;
            case "EH" when f.Length >= 6:
            {
                if (status.Dead || Invincible || Teleporting) break;
                var from2 = new Vector3(NetworkGame.P(f[2]), player.transform.position.y, NetworkGame.P(f[3]));
                entities.TryGetValue(f.Length >= 7 ? NetworkGame.I(f[6]) : -1, out var hitter);
                var hitKind = (EntityKind)NetworkGame.I(f[5]);
                // Le Réveil blesse aussi dans le Hub.
                DamageLocal(NetworkGame.I(f[1]), EntityName(hitKind), hitter, hitKind == EntityKind.Reveil);
                if (!status.Dead && NetworkGame.P(f[4]) > 0f) player.Knockback(player.transform.position - from2, NetworkGame.P(f[4]));
                break;
            }
            case "TA":
                if (!status.Dead) GameAudio.Play2D("tour_alert", 0.8f);
                break;
            case "KL" when host && f.Length >= 2:
                net.ToAll("EK|" + f[1]);
                break;
            case "EK" when f.Length >= 2:
                if (entities.TryGetValue(NetworkGame.I(f[1]), out var killer) && killer != null) killer.PlayKillSound();
                break;
            // Trajet d'une téléportation : l'hôte le relaie à tous avec l'identifiant de son auteur.
            case "TS" when host && f.Length >= 4:
                net.ToAll("TP|" + from + raw.Substring(2));
                break;
            case "TP" when f.Length >= 5:
                if (f[1] != net.MyId && net.Remotes.TryGetValue(f[1], out var traveller) && traveller != null)
                {
                    var tpPath = new List<Vector3>();
                    for (int i = 2; i + 2 < f.Length; i += 3)
                        tpPath.Add(new Vector3(NetworkGame.P(f[i]), NetworkGame.P(f[i + 1]), NetworkGame.P(f[i + 2])));
                    traveller.PlayTeleport(tpPath);
                }
                break;
            case "HL" when host && f.Length >= 2:
                net.ToAll("EW|" + f[1]);
                break;
            case "EW" when f.Length >= 2:
                if (entities.TryGetValue(NetworkGame.I(f[1]), out var hurter) && hurter != null) hurter.PlayHurtSound();
                break;
            case "NZ" when host:
                noiseAt[from] = Time.time;
                break;
            case "PS" when f.Length >= 2:
            {
                // Le Pion vous vole un objet ramassable (le premier trouvé dans l'inventaire).
                int slot = -1;
                for (int i = 0; i < GameRules.InventorySize && slot < 0; i++)
                    if (Items.DropsOnDeath(status[i])) slot = i;
                ItemType stolen = slot >= 0 && !status.Dead ? status.RemoveAt(slot) : ItemType.None;
                net.ToHost("PT|" + f[1] + "|" + (int)stolen);
                break;
            }
            case "PT" when host && f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]);
                if (pendingSteals.TryGetValue(id, out var done))
                {
                    pendingSteals.Remove(id);
                    done((ItemType)NetworkGame.I(f[2]));
                }
                break;
            }
            case "DR" when host && f.Length >= 5:
            {
                var type = (ItemType)NetworkGame.I(f[1]);
                if (!Items.DropsOnDeath(type)) break;
                AddDropped(type, new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4])), -1);
                break;
            }
            case "IA" when f.Length >= 6:
            {
                int id = NetworkGame.I(f[1]);
                var drop = new Dropped { type = (ItemType)NetworkGame.I(f[2]), pos = new Vector3(NetworkGame.P(f[3]), NetworkGame.P(f[4]), NetworkGame.P(f[5])) };
                if (f.Length >= 7) drop.stolenBy = NetworkGame.I(f[6]);
                if (items.ContainsKey(id) || drop.type == ItemType.None) break;
                dropped[id] = drop;
                items[id] = ItemPickup.Create(itemsRoot, id, drop.type, drop.pos);
                break;
            }
        }
    }

    // Clients : création, mise à jour et disparition des entités d'après l'état de l'hôte.
    readonly HashSet<int> seenEntities = new HashSet<int>();
    double lastEntityTime = double.NegativeInfinity;

    void ReceiveEntities(double time, string data)
    {
        if (zones.Count == 0) return;
        // Canal non ordonné : un état plus ancien que le dernier reçu ne doit pas recréer ni reculer une entité.
        if (time <= lastEntityTime) return;
        lastEntityTime = time;
        seenEntities.Clear();
        foreach (string entry in data.Split(';'))
        {
            string[] p = entry.Split(',');
            if (p.Length < 10) continue;
            int id = NetworkGame.I(p[0]);
            var kind = (EntityKind)Mathf.Clamp(NetworkGame.I(p[1]), (int)EntityKind.Dame, (int)EntityKind.TeteDeCloche);
            var pos = new Vector3(NetworkGame.P(p[2]), NetworkGame.P(p[3]), NetworkGame.P(p[4]));
            float yaw = NetworkGame.P(p[5]);
            seenEntities.Add(id);
            if (!entities.TryGetValue(id, out var e) || e == null || e.Kind != kind)
            {
                if (e != null) Destroy(e.gameObject);
                entities[id] = e = CreateEntity(kind, id, pos, yaw);
            }
            e.SetNetState(time, pos, yaw, (byte)Mathf.Clamp(NetworkGame.I(p[6]), 0, 255), p[7], NetworkGame.I(p[8]), p[9]);
        }
        var gone = new List<int>();
        foreach (int id in entities.Keys) if (!seenEntities.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            if (entities[id] != null) Destroy(entities[id].gameObject);
            entities.Remove(id);
            watching.Remove(id);
        }
    }

    bool IsDeadForHost(string id)
    {
        if (id == net.MyId) return status.Dead;
        return net.Remotes.TryGetValue(id, out var rp) && rp != null && rp.Dead;
    }

    string NameOf(string id) => net.Roster.TryGetValue(id, out var info) ? info.name : "?";
}
