using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Boucle de jeu : Hub, manches, quota, cagnotte, objets, Terminal, Chenils, logs, morts et réanimations,
// sérénité et Abjuration, entités (La Dame, La Tour, Le Pion, La Cloche, L'Araignée), orbes et porte de la Zone 2,
// Post-It, Boussole, Balises, Téléporteurs et Stations Curatives.
// L'hôte fait autorité sur l'état de la partie ; chaque client gère les PV, la sérénité et l'inventaire de son personnage.
// Messages (en plus de ceux de NetworkGame) :
//   G|graine|manche|quota|progression|cagnotte|phase|temps|boussole|orbes|balise|bonus   hôte -> tous : état de la partie
//   IL|manche|id,id...   IR|manche|id                          objets déjà ramassés / ramassé
//   PK|id -> GV|type (0 = refusé)                              ramassage
//   SL|type                                                     vente au Terminal
//   BY|type -> BO|ok|type|cagnotte[|raison]                     achat au Terminal
//   TU|1/0 -> TG|1/0, TS|id                                     prise / libération du Terminal
//   RV|cible -> RA|cible|sauveteur                              réanimation depuis un Chenil
//   DE|cause                                                    mort d'un joueur
//   LG|texte                                                    ligne de l'Écran de Logs
//   SC|manche|id,golds,morts;id,golds,morts...                  Tableau de Manche
//   PP|x|y|z|nx|ny|nz|texte -> PA|id|x|y|z|nx|ny|nz|texte       Post-It accroché
//   PD|id -> PX|id                                              Post-It retiré
//   OB|orbe|case -> OU|case (-1 = refusé)                       Clé utilisée sur un orbe
//   EN|t|id,type,x,y,z,lacet,état,cible,événements,données;...  hôte -> tous : entités, t = heure d'envoi (canal rapide)
//   LK|id|1/0                                                   le joueur chassé regarde (ou non) La Dame
//   EH|dégâts|x|z|recul|type                                    hôte -> joueur : une entité vous frappe depuis (x, z)
//   TA                                                          hôte -> joueur : La Tour vous a repéré (alerte)
//   PS|entité -> PT|entité|objet                                le Pion vole un objet ramassable (0 : rien à voler)
//   NZ                                                          joueur -> hôte : saut (action bruyante, pour L'Araignée)
//   DR|objet|x|y|z -> IA|id|objet|x|y|z                         objet laissé à terre (mort, Pion), ramassable comme les autres
//   BN|x|y|z|appareil / BG|id / BC                              joueur -> hôte : poser / ramasser un appareil, utiliser un Chargeur
//   BW|id|x|y|z|appareil / BZ|id                                hôte -> tous : appareil posé / retiré (Balise, Balise Améliorée,
//                                                               Téléporteur, Station Curative)
//   JO / JS|objet / JE                                          joueur -> hôte : ouvrir Pogo / lui vendre un objet / cliquer les mains vides
//   JX|état|sourire|frappé / JK / JN|raison                     hôte -> tous : état de Pogo ; -> joueur : coup mortel / refus
//   DF|durée                                                    hôte -> joueur : La Dame vous attaque, immobilisé
//   SK -> SR|bonus (-1 = refusé)                                /skip round : passer à la manche suivante
//   BK|pitch -> BA|id|pitch                                     aboiement d'un joueur
public class GameSession : MonoBehaviour, IEntityHost
{
    enum Phase { Waiting = 0, Running = 1, GameOver = 2 }

    static readonly Color HubAmbient = new Color(0.42f, 0.36f, 0.32f);
    static readonly Color MazeAmbient = new Color(0.035f, 0.035f, 0.04f);
    static readonly Color MistFog = new Color(0.02f, 0.05f, 0.12f);
    const float HubFog = 0.01f, MazeFog = 0.085f;
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
    WorldBuilder.HubRefs hub;
    WorldBuilder.MazeRefs maze;
    RedButton button;
    Pogo pogo;
    BeaconNetwork beacons;
    readonly Dictionary<string, Kennel> kennels = new Dictionary<string, Kennel>();
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
    // Génération d'objets de valeur multipliée selon le nombre de joueurs (x3 seul, x2 à deux), fixée par l'hôte.
    int valueBoost = 1, itemsBoost = -1;
    int round = 1, quota, progress, cash;
    Phase phase = Phase.Waiting;
    float timeLeft = GameRules.RoundDuration;
    bool compass;
    int orbsBroken;
    readonly HashSet<int> picked = new HashSet<int>();
    readonly List<string> logs = new List<string>();
    string terminalUser = "";
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
    bool hostAbjuration;

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
    float channelTime, clocheTimer, stationAcc;
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
    bool AbjurationActive => phase == Phase.Running && timeLeft <= GameRules.AbjurationStart;

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
        button = RedButton.Create(hub.buttonAnchor);
        TerminalStation.Create(hub.terminalAnchor);
        pogo = Pogo.Create(hub.pogoAnchor);
        beacons = BeaconNetwork.Create(worldRoot, hub.beaconPosition, hub.beaconYaw);
        hub.hubProbe.RenderProbe();
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
        playerLight.range = 7.5f;
        playerLight.intensity = 0.9f;
        playerLight.color = new Color(0.95f, 0.92f, 0.85f);
        playerLight.shadows = LightShadows.None;
        playerLight.renderMode = LightRenderMode.ForcePixel;

        hud = gameObject.AddComponent<GameHud>();
        hud.Init(RestartGame, () => net.LeaveGame());
        terminal = gameObject.AddComponent<TerminalUI>();
        terminal.Init(this);
        postItUI = gameObject.AddComponent<PostItUI>();
        postItUI.Init(OnPostItWritten);
        Shader.SetGlobalFloat("_Abjuration", 0f);
        UpdateScreens();
    }

    static void ConfigureRendering()
    {
        QualitySettings.pixelLightCount = 4;
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
        orbsBroken = 0;
        picked.Clear();
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        noteMessages.Clear();
        notePlacedAt.Clear();
        nextNoteId = nextEntityId = nextBeaconId = nextDropId = 0;
        dropped.Clear();
        hostAbjuration = false;
        ResetHubFixtures();
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        ApplyWorld();
    }

    // Balises et Pogo au début d'une partie.
    void ResetHubFixtures()
    {
        beaconMessages.Clear();
        beacons.Clear();
        beacons.Charge = GameRules.BeaconCharge;
        pogoMood = Pogo.Mood.Closed;
        pogoSmileUntil = pogoCooldownUntil = 0f;
        pogo.SetState(Pogo.Mood.Closed, false, "");
    }

    public void OnLocalEnter()
    {
        audioMuteUntil = Time.unscaledTime + 2f;
        presence.PlayerId = net.MyId;
        hud.Show(true);
        hud.HideGameOver();
        status.ClearInventory();
        pendingPickups = 0;
        ReviveLocal(SpawnPoint(), GameRules.MaxHp);
        if (phase == Phase.GameOver) OnGameOver();
        OnRosterChanged();
        RefreshHud();
    }

    public void ResetSession()
    {
        terminal.Close(false);
        postItUI.Close();
        hud.Show(false);
        ClearEntities();
        if (maze != null) Destroy(maze.root);
        maze = null;
        builtSeed = itemsRound = -1;
        ClearItems();
        ClearNotes();
        foreach (var k in kennels.Values) if (k != null) Destroy(k.gameObject);
        kennels.Clear();
        phase = Phase.Waiting;
        round = 1;
        quota = GameRules.QuotaForRound(1);
        progress = cash = 0;
        timeLeft = GameRules.RoundDuration;
        compass = false;
        orbsBroken = 0;
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        noteMessages.Clear();
        notePlacedAt.Clear();
        hostAbjuration = false;
        dropped.Clear();
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
        button.gameObject.SetActive(true);
        mistAmount = 0f;
        UpdateScreens();
    }

    // Construit le labyrinthe (nouvelle graine) et répartit les objets de la manche.
    void ApplyWorld()
    {
        bool rebuilt = false;
        if (seed != builtSeed)
        {
            ClearEntities();
            if (maze != null) Destroy(maze.root);
            maze = WorldBuilder.BuildMaze(worldRoot, seed);
            builtSeed = seed;
            itemsRound = -1;
            rebuilt = true;
        }
        if (itemsRound != round || itemsBoost != valueBoost) SpawnItems();
        hub.door.SetOpen(phase != Phase.Waiting);
        button.gameObject.SetActive(phase == Phase.Waiting);
        UpdateZone2(!rebuilt && Time.unscaledTime > audioMuteUntil);
        UpdateScreens();
    }

    // Orbes (activés à la fin des manches 1, 2 et 3, brisés par une Clé) et porte de la Zone 2.
    void UpdateZone2(bool animate)
    {
        if (maze == null) return;
        for (int k = 0; k < GameRules.OrbCount; k++)
            maze.orbs[k].SetState(phase != Phase.GameOver && GameRules.OrbActive(k, round), (orbsBroken & (1 << k)) != 0, animate);
        bool open = orbsBroken == (1 << GameRules.OrbCount) - 1;
        if (open != maze.zoneDoor.IsOpen || !animate) maze.zoneDoor.SetOpen(open, !animate);
        maze.nav.DoorOpen = open;
    }

    // Objets de valeur de la manche dans chaque zone (chapitre 10 : pourcentage du quota).
    void SpawnItems()
    {
        ClearItems();
        itemsRound = round;
        itemsBoost = valueBoost;
        int id = 0;
        for (int zone = 0; zone < 2; zone++)
        {
            var rnd = new System.Random(seed * 31 + round * 7919 + zone * 104729);
            var spots = new List<Vector3>(maze.itemSpots[zone]);
            for (int i = spots.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (spots[i], spots[j]) = (spots[j], spots[i]);
            }
            int count = Mathf.Min(GameRules.DucksForRound(round, zone, valueBoost), spots.Count);
            for (int k = 0; k < count; k++, id++)
                items[id] = ItemPickup.Create(itemsRoot, id, ItemType.Canard, spots[k]);
        }
        // Les objets laissés à terre restent d'une manche à l'autre.
        foreach (var kv in dropped) items[kv.Key] = ItemPickup.Create(itemsRoot, kv.Key, kv.Value.type, kv.Value.pos);
    }

    void ClearItems()
    {
        foreach (var it in items.Values) if (it != null) Destroy(it.gameObject);
        items.Clear();
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
        PlayerController.InputLocked = !inGame || TerminalUI.IsOpen || PostItUI.IsOpen || phase == Phase.GameOver || Teleporting;
        OrbitCamera.CursorLockBlocked = TerminalUI.IsOpen || PostItUI.IsOpen || GameHud.GameOverVisible;

        if (phase == Phase.Running)
        {
            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            if (net.IsHost && inGame && timeLeft <= 0f) EndRound();
            if (net.IsHost && inGame && phase == Phase.Running) SpawnLateEntities();
            // La Balise du Hub se décharge pendant la manche.
            beacons.Charge = Mathf.Max(0f, beacons.Charge - Time.deltaTime);
        }
        if (net.IsHost && inGame) UpdateHostPogo();
        if (net.IsHost && inGame)
        {
            syncTimer += Time.unscaledDeltaTime;
            if (syncTimer >= 1f) BroadcastState();
            UpdateHostAbjuration();
            // La grille de navigation de La Dame se calcule en arrière-plan, quelques colonnes par image.
            if (maze != null && !maze.nav.Ready) maze.nav.BuildStep(6);
            SimulateEntities();
        }

        UpdateEntities();
        UpdateAtmosphere();
        UpdateGameSounds(inGame);
        if (!inGame) return;

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
        if (!status.Dead && status.Serenity < GameRules.MaxSerenity && inHub)
        {
            serenityRegenAcc += GameRules.HubSerenityRegenPerSecond * Time.deltaTime;
            int add = Mathf.FloorToInt(serenityRegenAcc);
            if (add > 0)
            {
                serenityRegenAcc -= add;
                status.SetSerenity(status.Serenity + add);
            }
        }
        else serenityRegenAcc = 0f;

        if (!PlayerController.InputLocked && !status.Dead && Input.GetKeyDown(KeyLayout.Bark)) TryBark();
        UpdateTeleport();
        UpdateStations();
        UpdateCloches();
        UpdateMistContact();
        UpdateWatching();
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
        hud.SetPrompt(string.IsNullOrEmpty(prompt) ? "" : "[E] " + prompt);
        // Cap de la caméra (là où le joueur regarde), pas celui du personnage.
        hud.SetCompass(compass && !status.Dead, (orbit != null ? orbit.transform : player.transform).eulerAngles.y);
    }

    void UpdateAtmosphere()
    {
        Camera cam = Camera.main;
        bool inHub = cam == null || WorldBuilder.IsInHub(cam.transform.position);
        float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, inHub ? HubAmbient : MazeAmbient, k);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, inHub ? HubFog : MazeFog, k);
        // Pendant la mort, la lampe éclaire encore le corps puis faiblit, jusqu'au noir.
        bool dying = status.Dead && OrbitCamera.DeathView >= 0f;
        playerLight.enabled = net.InGame && (!status.Dead || dying) && !WorldBuilder.IsInHub(player.transform.position);
        playerLight.intensity = dying ? 0.9f * (1f - Mathf.Clamp01(OrbitCamera.DeathView / OrbitCamera.DeathViewDuration)) : 0.9f;

        // Brume de l'Abjuration : apparaît et se dissipe en douceur ; le brouillard bleuit quand on y est plongé.
        bool abjuration = net.InGame && AbjurationActive;
        mistAmount = Mathf.MoveTowards(mistAmount, abjuration ? 1f : 0f, Time.deltaTime / 4f);
        Shader.SetGlobalFloat("_Abjuration", mistAmount);
        UpdateMistHoles();
        if (maze != null && maze.mist.activeSelf != mistAmount > 0.001f) maze.mist.SetActive(mistAmount > 0.001f);
        float low = cam != null && !inHub ? Mathf.Clamp01((2.2f - cam.transform.position.y) / 1.5f) : 0f;
        RenderSettings.fogColor = Color.Lerp(Color.black, MistFog, mistAmount * low);
        GameAudio.AbjurationLevel = abjuration ? 1f : 0f;
        UpdateInsectFlee();
    }

    // Sons liés à l'état de la partie, détectés chez tous les clients (hôte compris) par changement d'état :
    // Bouton Rouge, quota validé, game over, glas de l'Abjuration, tic-tac de la dernière minute.
    Phase soundPhase;
    int soundRound = 1, lastTickSecond = -1;
    float audioMuteUntil;
    bool soundAbjuration;

    void UpdateGameSounds(bool inGame)
    {
        // Pas de fanfare ni de buzzer en rejoignant une partie déjà commencée : on se cale sur l'état reçu.
        bool mute = !inGame || Time.unscaledTime < audioMuteUntil;
        bool abjuration = AbjurationActive;
        if (!mute)
        {
            if (soundPhase == Phase.Waiting && phase == Phase.Running)
                GameAudio.Play3D("button", hub.buttonAnchor.position + Vector3.up, 0.8f, 40f, 0f);
            if (phase == Phase.Running && round > soundRound) GameAudio.Play2D("fanfare", 0.8f);
            if (soundPhase != Phase.GameOver && phase == Phase.GameOver) GameAudio.Play2D("gameover", 0.9f);
            if (abjuration && !soundAbjuration) GameAudio.Play2D("bell", 1f);
        }
        soundPhase = phase;
        soundRound = round;
        soundAbjuration = abjuration;

        int second = Mathf.CeilToInt(timeLeft);
        if (!mute && phase == Phase.Running && second <= 60 && second > 0 && second != lastTickSecond)
        {
            bool last = second <= 10;
            GameAudio.Play3D(last ? "tock" : "tick", hub.timerText.transform.position, last ? 1f : 0.6f, 30f, 0f, second % 2 == 0 ? 1f : 0.85f);
        }
        lastTickSecond = second;

        // Grouillement des insectes : plus fort contre un mur du labyrinthe.
        float level = 0f;
        if (inGame && !status.Dead && !WorldBuilder.IsInHub(player.transform.position))
        {
            float nearest = 3f;
            Vector3 origin = player.transform.position;
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    nearest = Mathf.Min(nearest, hit.distance);
            level = Mathf.Clamp01(1f - (nearest - 0.5f) / 2f);
        }
        GameAudio.InsectLevel = level;
    }

    // Les insectes des murs (shader InfestedWall) fuient les joueurs vivants du labyrinthe ;
    // un joueur qui marche discrètement les dérange moins. Le rayon varie en douceur.
    const float InsectFleeRadius = 3.2f, InsectFleeSneakRadius = 1.8f;
    readonly Vector4[] insectFlee = new Vector4[GameRules.MaxPlayers];
    readonly Dictionary<PlayerPresence, float> insectRadius = new Dictionary<PlayerPresence, float>();

    void UpdateInsectFlee()
    {
        int n = 0;
        float k = 1f - Mathf.Exp(-3f * Time.deltaTime);
        foreach (var p in PlayerPresence.All)
        {
            bool active = net.InGame && p.Alive && !WorldBuilder.IsInHub(p.Position);
            float goal = !active ? 0f : p.Sneaking ? InsectFleeSneakRadius : InsectFleeRadius;
            insectRadius.TryGetValue(p, out float radius);
            radius = Mathf.Lerp(radius, goal, k);
            insectRadius[p] = radius;
            if (radius > 0.05f && n < insectFlee.Length)
            {
                Vector3 pos = p.Position + Vector3.up * 0.3f;
                insectFlee[n++] = new Vector4(pos.x, pos.y, pos.z, radius);
            }
        }
        for (int i = n; i < insectFlee.Length; i++) insectFlee[i] = Vector4.zero;
        Shader.SetGlobalVectorArray("_InsectFlee", insectFlee);
    }

    // Toutes les 3 secondes passées dans la brume de l'Abjuration : -1 de sérénité (à 0, le personnage meurt),
    // et le déplacement y est ralenti de 20 %.
    void UpdateMistContact()
    {
        Vector3 p = player.transform.position;
        var capsule = player.GetComponent<CapsuleCollider>();
        float feet = p.y + capsule.center.y - capsule.height * 0.5f;
        bool inMist = !status.Dead && !Teleporting && AbjurationActive && mistAmount > 0.5f && maze != null
            && maze.nav.ZoneAt(p) >= 0 && feet < GameRules.MistTop && !NearEnhancedBeacon(p);
        // Dans la brume, le personnage avance 20 % moins vite.
        player.SpeedFactor = inMist ? GameRules.MistSpeedMultiplier : 1f;
        if (!inMist || Godmode)
        {
            mistTimer = 0f;
            return;
        }
        mistTimer += Time.deltaTime;
        if (mistTimer < GameRules.AbjurationTick) return;
        mistTimer -= GameRules.AbjurationTick;
        status.SetSerenity(status.Serenity - GameRules.AbjurationLoss);
        if (status.Serenity <= 0) KillLocal("l'Abjuration");
    }

    // Un joueur mort voit la caméra d'un autre joueur (clic pour changer).
    void UpdateSpectate()
    {
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
        string end = phase == Phase.GameOver ? "" : "\nUn autre joueur peut vous réanimer depuis votre Chenil, dans le Hub.";
        hud.SetSpectate(current != null
            ? "Vous êtes mort. Vous regardez " + current.PlayerName + " (clic : changer de joueur)." + end
            : "Vous êtes mort." + end);
    }

    void UpdateScreens()
    {
        int seconds = Mathf.CeilToInt(timeLeft);
        string time = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        hub.timerText.text = time;
        hub.quotaText.text = "Quota : " + progress + " / " + quota;
        hub.cashText.text = "Cagnotte : " + cash + " golds";
        hub.logText.text = string.Join("\n", logs);
        string user = terminalUser.Length > 0 && net.Roster.TryGetValue(terminalUser, out var u) ? u.name : null;
        hub.terminalText.text = TerminalUI.Menu(CanSkipRound) +(user != null ? "\n\n> Utilisé par " + user : "");
        hub.roundBoardText.text = RoundBoardText();
    }

    // Classement des joueurs connectés : golds gagnés pendant la manche, puis morts.
    string RoundBoardText()
    {
        var rows = new List<(string name, int golds, int deaths)>();
        foreach (var kv in net.Roster)
        {
            roundStats.TryGetValue(kv.Key, out var s);
            rows.Add((kv.Value.name, s?.golds ?? 0, s?.deaths ?? 0));
        }
        rows.Sort((a, b) => a.golds != b.golds ? b.golds.CompareTo(a.golds) : a.deaths.CompareTo(b.deaths));
        var sb = new StringBuilder("<size=110>Manche " + statsRound + "</size>\n");
        for (int i = 0; i < rows.Count; i++)
            sb.Append('\n').Append(i + 1).Append(". ").Append(rows[i].name).Append(" : ")
              .Append(rows[i].golds).Append(" golds · ").Append(rows[i].deaths).Append(rows[i].deaths > 1 ? " morts" : " mort");
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
            if (kennels[id] != null) Destroy(kennels[id].gameObject);
            kennels.Remove(id);
        }
        if (!net.InGame && !net.IsHost) return;
        foreach (var kv in net.Roster)
        {
            if (kennels.ContainsKey(kv.Key)) continue;
            int slot = Mathf.Clamp(kv.Value.slot, 0, hub.kennelPositions.Length - 1);
            kennels[kv.Key] = Kennel.Create(worldRoot, hub.kennelPositions[slot], hub.kennelRotations[slot], kv.Key);
        }
        UpdateKennels();
    }

    void UpdateKennels()
    {
        foreach (var kv in kennels)
        {
            if (kv.Value == null || !net.Roster.TryGetValue(kv.Key, out var info)) continue;
            Vector3 pos;
            int hp;
            bool dead;
            if (kv.Key == net.MyId)
            {
                pos = player.transform.position; hp = status.Hp; dead = status.Dead;
            }
            else if (net.Remotes.TryGetValue(kv.Key, out var rp) && rp != null)
            {
                pos = rp.transform.position; hp = rp.Dead ? 0 : rp.Hp; dead = rp.Dead;
            }
            else continue;
            Vector3 d = pos - kv.Value.transform.position;
            d.y = 0f;
            // Un joueur ne peut pas se réanimer lui-même.
            kv.Value.SetInfo(info.name, hp, d.magnitude, dead);
            if (kv.Key == net.MyId) kv.Value.TargetDead = false;
        }
    }

    Vector3 KennelSpawn(string id)
    {
        if (kennels.TryGetValue(id, out var k) && k != null)
        {
            Vector3 p = k.transform.position + k.transform.forward * 1.5f;
            return new Vector3(p.x, 0.95f, p.z);
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
        return on ? "Godmod activé : achats gratuits, invincible, sérénité intacte, jamais immobilisé." : "Godmod désactivé.";
    }

    bool Invincible => Time.time < invincibleUntil || Godmode;

    // Dégâts reçus par le personnage local. Le Hub est un endroit sûr ; juste après un /tp, aucun dégât.
    public void DamageLocal(int amount, string cause)
    {
        if (status.Dead || phase == Phase.GameOver || !net.InGame) return;
        if (WorldBuilder.IsInHub(player.transform.position) || Invincible || Teleporting) return;
        status.SetHp(status.Hp - amount);
        hud.FlashDamage();
        GameAudio.Play2D("hurt", 0.9f, 0.08f);
        if (status.Hp <= 0) KillLocal(cause);
    }

    void KillLocal(string cause)
    {
        if (status.Dead) return;
        status.SetDead(true);
        EndTeleport(false);
        channelPad = null;
        StopChannelSound();
        // Les objets du Terminal sont détruits ; les autres (avec un modèle 3D) restent à terre près du corps.
        var left = new List<ItemType>();
        if (cause != null)
            for (int i = 0; i < GameRules.InventorySize; i++)
                if (Items.DropsOnDeath(status[i])) left.Add(status[i]);
        status.ClearInventory();
        pendingPickups = 0;
        presence.Alive = false;
        avatar.SetDead(true);
        player.SetFrozen(true);
        // Capsule désactivée : les objets tombent jusqu'au sol.
        foreach (var type in left) DropNear(type, player.transform.position);
        terminal.Close();
        postItUI.Close();
        // Au game over, l'accord lugubre remplace les râles.
        if (cause != null)
        {
            GameAudio.Play3D("death", player.transform.position, 1f, 30f, 0.06f);
            // Le râle, un silence total, puis un cœur qui s'arrête ; l'écran rougit et s'assombrit, la caméra quitte le corps.
            GameAudio.PlayDeathSequence();
            hud.PlayDeath();
            deathAt = Time.time;
        }
        if (cause != null) net.ToHost("DE|" + cause);
        RefreshHud();
    }

    public void UseSelectedItem()
    {
        int slot = status.Selected;
        if (slot < 0) return;
        switch (status[slot])
        {
            case ItemType.Croquette:
                if (status.Hp >= GameRules.MaxHp && status.Serenity >= GameRules.MaxSerenity)
                {
                    hud.Toast("Votre vie et votre sérénité sont déjà au maximum.");
                    return;
                }
                status.RemoveAt(slot);
                status.SetHp(status.Hp + Items.CroquetteHeal);
                status.SetSerenity(status.Serenity + Items.CroquetteSerenity);
                GameAudio.Play2D("eat", 0.8f, 0.1f);
                hud.Toast("Miam ! +" + Items.CroquetteHeal + " PV, +" + Items.CroquetteSerenity + " de sérénité");
                break;
            case ItemType.Canard:
                hud.Toast("Le Canard en Plastique ne sert qu'à être vendu (à Pogo, dans le Hub).");
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
            case ItemType.Chargeur:
                hud.Toast("Visez la Balise du Hub, près de la porte, pour la recharger.");
                break;
            case ItemType.PostIt:
                TryPlacePostIt(slot);
                break;
            case ItemType.Cle:
                hud.Toast("Visez un orbe activé, devant la porte de la Zone 2, pour le briser avec la Clé.");
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
            hud.Toast("Visez un mur tout proche pour y accrocher le Post-It.");
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

    // Viser le sol (hors du Hub, assez près) pose la Balise (ou la Balise Améliorée, le Téléporteur, la Station Curative).
    void TryPlaceBeacon(int slot, DeviceKind kind)
    {
        string what = Items.Get(status[slot]).name;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool ground = Physics.Raycast(ray, out RaycastHit hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.normal.y > 0.7f
            && hit.collider.GetComponentInParent<Interactable>() == null
            && Vector3.Distance(hit.point, player.transform.position) <= GameRules.InteractRange + 1.5f;
        if (!ground)
        {
            hud.Toast("Visez le sol, tout près de vous, pour y poser : " + what + ".");
            return;
        }
        if (WorldBuilder.IsInHub(hit.point))
        {
            hud.Toast(what + " : à poser hors du Hub.");
            return;
        }
        status.RemoveAt(slot);
        net.ToHost("BN|" + NetworkGame.F(hit.point.x) + "|" + NetworkGame.F(hit.point.y) + "|" + NetworkGame.F(hit.point.z) + "|" + (int)kind);
    }

    public void InteractBeacon(Beacon beacon)
    {
        if (beacon.IsHub)
        {
            int slot = status.Selected;
            if (slot < 0 || status[slot] != ItemType.Chargeur)
            {
                hud.Toast(beacons.HubCharged ? "La Balise du Hub est chargée. Un Chargeur prolonge sa charge."
                                             : "La Balise du Hub est déchargée : sélectionnez un Chargeur pour la recharger.");
                return;
            }
            status.RemoveAt(slot);
            net.ToHost("BC");
            return;
        }
        if (!HasFreeSlot)
        {
            hud.Toast("Votre inventaire est plein !");
            return;
        }
        pendingPickups++;
        net.ToHost("BG|" + beacon.Id);
    }

    // ================= Pogo =================

    string PogoMsg(string attacked) => "JX|" + (int)pogoMood + "|" + (Time.time < pogoSmileUntil ? 1 : 0) + "|" + attacked;

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
        if (pogo.State == Pogo.Mood.Closed)
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
        status.Deselect();
        net.ToHost("JS|" + (int)type);
        GameAudio.Play2D("register", 0.8f);
    }

    // ================= Orbes et porte de la Zone 2 =================

    public void UseKeyOnOrb(int orb)
    {
        int slot = status.IndexOf(ItemType.Cle);
        if (slot < 0)
        {
            hud.Toast("Il faut une Clé (en vente au Terminal) pour briser cet orbe.");
            return;
        }
        net.ToHost("OB|" + orb + "|" + slot);
    }

    // ================= Entités =================

    // /spawn : fait apparaître l'entité 'id' dans la Zone 1. Retourne le message du Terminal.
    public string SpawnEntity(int id)
    {
        if (!net.IsHost) return "Commande Incorrecte.";
        if (maze == null || phase == Phase.GameOver) return "Impossible pour le moment.";
        var rnd = new System.Random(seed + nextEntityId * 131 + Mathf.RoundToInt(Time.time * 10f));
        switch (id)
        {
            case 1: return "Les Lasers ne peuvent plus apparaître une fois le terrain généré.";
            case 2: return SpawnAt<Dame>(0, rnd, 18f) != null ? "La Dame rôde désormais dans la Zone 1..." : "Impossible pour le moment.";
            case 3: return SpawnAt<Tour>(0, rnd, 18f) != null ? "La Tour arpente désormais la Zone 1..." : "Impossible pour le moment.";
            case 4: return SpawnPions(0, rnd) > 0 ? "Des Pions s'installent dans la Zone 1..." : "Impossible pour le moment.";
            case 5: return SpawnAt<Cloche>(0, rnd, 18f) != null ? "La Cloche attend dans la Zone 1..." : "Impossible pour le moment.";
            case 6: return SpawnAt<Spider>(0, rnd, GameRules.SpiderSpawnClearance) != null ? "Une Araignée rôde dans la Zone 1..." : "Impossible pour le moment.";
            default: return "Aucune entité n'a l'ID " + id + ".";
        }
    }

    // Début de manche (hôte) : les entités de la manche précédente disparaissent, celles du tableau apparaissent,
    // avec les entités de type Normal (Pion, Cloche).
    void SpawnRoundEntities()
    {
        ClearEntities();
        lateNightmareDone = lateNormalsDone = false;
        if (maze == null || phase != Phase.Running) return;
        var rnd = new System.Random(seed * 17 + round * 613);
        for (int zone = 0; zone < 2; zone++)
        {
            for (int k = 0; k < GameRules.NightmaresForRound(round, zone); k++) SpawnNightmare(zone, rnd);
            for (int k = 0; k < GameRules.NormalsForRound(round, zone); k++) SpawnNormal(zone, rnd);
        }
    }

    // Manche 1 (hôte), sans aucun message dans les logs : 2 entités Normal après 2 minutes, un Cauchemar après 3 minutes.
    bool lateNightmareDone, lateNormalsDone;

    void SpawnLateEntities()
    {
        if (round != GameRules.LateSpawnRound || maze == null) return;
        float played = GameRules.RoundDuration - timeLeft;
        if (!lateNormalsDone && played >= GameRules.LateNormalsDelay)
        {
            lateNormalsDone = true;
            var rnd = new System.Random(seed * 37 + 104729);
            for (int k = 0; k < GameRules.NormalsPerZone; k++) SpawnNormal(0, rnd);
        }
        if (!lateNightmareDone && played >= GameRules.LateNightmareDelay)
        {
            lateNightmareDone = true;
            SpawnNightmare(0, new System.Random(seed * 31 + 7919));
        }
    }

    // Entités de type Cauchemar (La Dame, La Tour) : l'une d'elles, au hasard.
    void SpawnNightmare(int zone, System.Random rnd)
    {
        if (rnd.Next(2) == 0) SpawnAt<Dame>(zone, rnd, 18f);
        else SpawnAt<Tour>(zone, rnd, 18f);
    }

    // Entités de type Normal (un groupe de Pions, La Cloche) : l'une d'elles, au hasard.
    void SpawnNormal(int zone, System.Random rnd)
    {
        if (rnd.Next(2) == 0) SpawnPions(zone, rnd);
        else SpawnAt<Cloche>(zone, rnd, 18f);
    }

    // "Faire apparaître un Pion", c'est faire apparaître un groupe de 1 à 3 Pions, côte à côte.
    int SpawnPions(int zone, System.Random rnd)
    {
        var first = SpawnAt<Pion>(zone, rnd, 18f);
        if (first == null) return 0;
        int count = 1 + rnd.Next(3);
        for (int k = 1; k < count; k++)
        {
            Vector3 offset = Quaternion.Euler(0f, k * 120f, 0f) * Vector3.forward * 1.2f;
            int node = maze.nav.NodeAt(first.transform.position + offset);
            Vector3 pos = node >= 0 ? maze.nav.Position(node) : first.transform.position;
            var pion = Entity.Create<Pion>(entitiesRoot, nextEntityId++, pos, (float)rnd.NextDouble() * 360f, maze.nav);
            pion.Home = first.Home;
            entities[pion.Id] = pion;
        }
        return count;
    }

    // L'Abjuration commence (hôte) : 5 Araignées par zone déverrouillée, loin des joueurs.
    void SpawnSpiders()
    {
        if (maze == null || phase != Phase.Running) return;
        var rnd = new System.Random(seed * 41 + round * 977);
        int zones = maze.zoneDoor.IsOpen ? 2 : 1;
        for (int zone = 0; zone < zones; zone++)
            for (int k = 0; k < GameRules.SpidersPerZone; k++)
                SpawnAt<Spider>(zone, rnd, GameRules.SpiderSpawnClearance);
    }

    // Sur la grille de navigation de la zone, loin du Hub, des joueurs et des Balises Améliorées.
    T SpawnAt<T>(int zone, System.Random rnd, float playerClearance) where T : Entity
    {
        if (!maze.nav.Ready) maze.nav.BuildAll();
        int node = -1;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int candidate = maze.nav.RandomNode(zone, rnd, Vector3.zero, 30f);
            if (candidate < 0) continue;
            node = candidate;
            Vector3 p = maze.nav.Position(candidate);
            bool blocked = NearEnhancedBeacon(p);
            foreach (var pl in PlayerPresence.All)
                if (pl.Alive && Vector3.Distance(pl.Position, p) < playerClearance) blocked = true;
            if (!blocked) break;
        }
        if (node < 0) return null;
        var e = Entity.Create<T>(entitiesRoot, nextEntityId++, maze.nav.Position(node), (float)rnd.NextDouble() * 360f, maze.nav);
        entities[e.Id] = e;
        return e;
    }

    Entity CreateEntity(EntityKind kind, int id, Vector3 pos, float yaw)
    {
        switch (kind)
        {
            case EntityKind.Tour: return Entity.Create<Tour>(entitiesRoot, id, pos, yaw, maze.nav);
            case EntityKind.Pion: return Entity.Create<Pion>(entitiesRoot, id, pos, yaw, maze.nav);
            case EntityKind.Cloche: return Entity.Create<Cloche>(entitiesRoot, id, pos, yaw, maze.nav);
            case EntityKind.Spider: return Entity.Create<Spider>(entitiesRoot, id, pos, yaw, maze.nav);
            default: return Entity.Create<Dame>(entitiesRoot, id, pos, yaw, maze.nav);
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
            default: return "La Dame";
        }
    }

    float GroundBelow(Vector3 p)
    {
        float best = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 3f, p.z), Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.transform.IsChildOf(maze.structure) && hit.point.y > best) best = hit.point.y;
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

    void SimulateEntities()
    {
        if (phase == Phase.Running)
            foreach (var d in new List<Entity>(entities.Values)) if (d != null) d.Simulate(Time.deltaTime, PlayerPresence.All);
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
        net.ToPeer(targetId, "EH|" + damage + "|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.z) + "|" + NetworkGame.F(knockback) + "|" + (int)entity.Kind);
    }

    public void Immobilize(Entity entity, string targetId, float seconds) => net.ToPeer(targetId, "DF|" + NetworkGame.F(seconds));

    public void Alert(Entity entity, string targetId) => net.ToPeer(targetId, "TA");

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

    // Le joueur chassé signale à l'hôte s'il regarde La Dame (elle accélère alors).
    void UpdateWatching()
    {
        Camera cam = Camera.main;
        foreach (var e in entities.Values)
        {
            if (!(e is Dame d)) continue;
            bool chased = d.Current != Dame.State.Wander && d.TargetId == net.MyId && !status.Dead;
            bool looking = false;
            if (chased && cam != null)
            {
                Vector3 chest = d.transform.position + Vector3.up * d.ChestHeight;
                Vector3 to = chest - cam.transform.position;
                looking = Vector3.Angle(cam.transform.forward, to) < 32f && to.magnitude < 40f
                    && !Physics.Linecast(cam.transform.position, chest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
            watching.TryGetValue(d.Id, out bool before);
            if (looking == before) continue;
            watching[d.Id] = looking;
            net.ToHost("LK|" + d.Id + "|" + (looking ? 1 : 0));
        }
    }

    // Obnubilé par une Cloche : la caméra est forcée de la regarder, et toutes les 2 secondes, des dégâts.
    void UpdateCloches()
    {
        Cloche nearest = null;
        float best = float.MaxValue;
        if (!status.Dead && !Teleporting)
            foreach (var e in entities.Values)
                if (e is Cloche c && c.Obsesses(net.MyId))
                {
                    float d = (c.transform.position - player.transform.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = c; }
                }
        OrbitCamera.ForcedLook = nearest != null ? nearest.transform.position + Vector3.up * nearest.ChestHeight : (Vector3?)null;
        if (nearest == null)
        {
            clocheTimer = 0f;
            return;
        }
        clocheTimer += Time.deltaTime;
        if (clocheTimer < GameRules.ClocheTick) return;
        clocheTimer -= GameRules.ClocheTick;
        DamageLocal(GameRules.ClocheDamage, "La Cloche");
    }

    // ---------- Balises Améliorées : pas d'Abjuration à moins de 5 m ----------

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
    // jusqu'à la Balise du Hub. Pendant le voyage : aucune action (la caméra reste libre), ni dégâts ni perte de sérénité.
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
        // Avalé par le Téléporteur : la boule part du disque.
        path.Insert(0, pad.transform.position + Vector3.up * 0.9f);
        teleportPath = path;
        teleportLeg = 0;
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
        if (energyBall != null) energyBall.SetActive(false);
        avatar.SetHidden(false);
        if (!restore) return;
        Vector3 pos = player.transform.position;
        if (arrived)
        {
            Vector3 hubPos = beacons.Hub.transform.position;
            Vector3 toCenter = new Vector3(-hubPos.x, 0f, -hubPos.z).normalized;
            pos = new Vector3(hubPos.x, 0.95f, hubPos.z) + toCenter * 1.3f;
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
            hud.Toast("Votre inventaire est plein !");
            return;
        }
        pendingPickups++;
        net.ToHost("PK|" + item.Id);
    }

    public void PressButton()
    {
        if (!net.IsHost)
        {
            hud.Toast("Seul l'hôte de la partie peut appuyer sur le Bouton Rouge.");
            return;
        }
        if (phase != Phase.Waiting) return;
        phase = Phase.Running;
        timeLeft = GameRules.RoundDuration;
        // Le nombre de joueurs au départ fixe la quantité d'objets de valeur (personne n'a encore pu en ramasser).
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        Log("Un orbe s'éveille devant la porte de la Zone 2...");
        ApplyWorld();
        SpawnRoundEntities();
        BroadcastState();
    }

    public void RequestTerminal()
    {
        if (terminalUser.Length > 0 && terminalUser != net.MyId)
        {
            hud.Toast("Un autre joueur utilise déjà le Terminal");
            return;
        }
        net.ToHost("TU|1");
    }

    public void ReleaseTerminal() => net.ToHost("TU|0");

    public void RequestRevive(string playerId)
    {
        if (status.Dead || playerId == net.MyId) return;
        net.ToHost("RV|" + playerId);
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
    void TryBark()
    {
        while (barkTimes.Count > 0 && Time.time - barkTimes.Peek() > GameRules.BarkWindow) barkTimes.Dequeue();
        if (barkTimes.Count >= GameRules.BarkLimit) return;
        barkTimes.Enqueue(Time.time);
        float pitch = Mathf.Pow(2f, Random.Range(-1f, 1f) / 12f);
        PlayBark(player.transform, pitch);
        net.ToHost("BK|" + NetworkGame.F(pitch));
    }

    static void PlayBark(Transform who, float pitch) =>
        GameAudio.Play3DSmooth("bark", who.position + Vector3.up * 0.8f, 1f, GameRules.BarkRange, pitch);

    // /skip round : si le quota est atteint, l'hôte passe à la manche suivante. Retourne un refus, ou null si envoyé.
    public bool CanSkipRound => phase == Phase.Running && progress >= quota;

    public string RequestSkipRound()
    {
        if (phase != Phase.Running) return "Aucune manche n'est en cours.";
        if (!CanSkipRound) return "Le quota de la manche n'est pas encore atteint.";
        net.ToHost("SK");
        return null;
    }

    // /tp (hôte) : téléporte le personnage à 3 mètres de l'ennemi le plus proche ("enemy"), de la porte de la
    // Zone 2 ("door") ou d'un joueur (son pseudo). Retourne le message à afficher, ou null si la commande est invalide.
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
            return "Téléporté près de " + nearest.DisplayName + ".";
        }
        if (lower == "door")
        {
            if (maze == null) return null;
            Transform door = maze.zoneDoor.transform;
            // Côté Zone 1, face à la porte.
            Vector3 p = door.position + door.forward * 3f;
            player.Teleport(new Vector3(p.x, GroundBelow(p) + 0.95f, p.z), door.eulerAngles.y + 180f);
            return "Téléporté devant la porte de la Zone 2.";
        }
        foreach (var rp in net.Remotes.Values)
        {
            if (rp == null || !string.Equals(rp.PlayerName, target, System.StringComparison.OrdinalIgnoreCase)) continue;
            TeleportNear(rp.transform.position);
            return "Téléporté près de " + rp.PlayerName + ".";
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
        + "|" + (compass ? 1 : 0) + "|" + orbsBroken + "|" + NetworkGame.F(beacons.Charge) + "|" + valueBoost;

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
        net.ToPeer(to, "IL|" + round + "|" + ids);
        foreach (string line in logs) net.ToPeer(to, "LG|" + line);
        net.ToPeer(to, "TS|" + terminalUser);
        net.ToPeer(to, StatsMsg());
        foreach (string note in noteMessages.Values) net.ToPeer(to, note);
        foreach (string beacon in beaconMessages.Values) net.ToPeer(to, beacon);
        foreach (var kv in dropped) net.ToPeer(to, DropMsg(kv.Key, kv.Value));
        net.ToPeer(to, PogoMsg(""));
    }

    void Log(string text)
    {
        string line = PeerNet.LocalNow.ToString("HH:mm") + " : " + text;
        net.ToAll("LG|" + line);
    }

    // L'Abjuration se répand quand le Compteur de Temps affiche 7 minutes.
    void UpdateHostAbjuration()
    {
        bool active = AbjurationActive;
        if (active && !hostAbjuration)
        {
            Log("L'Abjuration se répand ... Le désespoir attend les plus faibles.");
            SpawnSpiders();
        }
        hostAbjuration = active;
    }

    public void OnPlayerJoined(string id)
    {
        if (net.Roster.TryGetValue(id, out var info)) Log("Le joueur " + info.name + " est connecté.");
        OnRosterChanged();
    }

    public void OnPlayerLeft(string id, string playerName)
    {
        if (net.IsHost)
        {
            Log("Le joueur " + playerName + " est déconnecté.");
            if (terminalUser == id) net.ToAll("TS|");
        }
        OnRosterChanged();
    }

    void EndRound()
    {
        if (progress >= quota)
        {
            int done = round;
            round++;
            quota = GameRules.QuotaForRound(round);
            progress = 0;
            timeLeft = GameRules.RoundDuration;
            picked.Clear();
            valueBoost = GameRules.ValueBoost(net.Roster.Count);
            Log("Le quota de la manche " + done + " est validé ! Nouveau quota : " + quota);
            if (hostAbjuration) Log("L'Abjuration se retire ... Pour le moment.");
            hostAbjuration = false;
            if (round <= GameRules.OrbCount) Log("Un orbe s'éveille devant la porte de la Zone 2...");
            ResetRoundStats(round);
            ApplyWorld();
            SpawnRoundEntities();
            BroadcastState();
        }
        else
        {
            phase = Phase.GameOver;
            timeLeft = 0f;
            hostAbjuration = false;
            ClearEntities();
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
        orbsBroken = 0;
        picked.Clear();
        noteMessages.Clear();
        notePlacedAt.Clear();
        dropped.Clear();
        valueBoost = GameRules.ValueBoost(net.Roster.Count);
        ResetRoundStats(1);
        ResetHubFixtures();
        net.ToAll(PogoMsg(""));
        ApplyWorld();
        BroadcastState();
        OnRestart();
    }

    // Quota non atteint : tous les personnages meurent.
    void OnGameOver()
    {
        terminal.Close(false);
        postItUI.Close();
        ClearEntities();
        if (!status.Dead) KillLocal(null);
        OrbitCamera.LockCursor(false);
        // Manche, temps, quota et cagnotte ne s'affichent que sur les écrans du chapiteau.
        hud.ShowGameOver("Le quota n'a pas été atteint." + (net.IsHost ? "" : "\nEn attente de l'hôte pour une nouvelle partie…"),
            net.IsHost);
    }

    void OnRestart()
    {
        hud.HideGameOver();
        dropped.Clear();
        status.ClearInventory();
        pendingPickups = 0;
        ClearNotes();
        // Nouvelle partie : les Balises posées disparaissent, celle du Hub est rechargée.
        beacons.Clear();
        beacons.Charge = GameRules.BeaconCharge;
        ReviveLocal(SpawnPoint(), GameRules.MaxHp);
    }

    // ================= Réception =================

    public void HandleMessage(string from, string[] f, string raw)
    {
        bool host = net.IsHost;
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
                orbsBroken = NetworkGame.I(f[9]);
                if (f.Length >= 11) beacons.Charge = NetworkGame.P(f[10]);
                if (f.Length >= 12) valueBoost = Mathf.Clamp(NetworkGame.I(f[11]), 1, 3);
                ApplyWorld();
                if (net.InGame)
                {
                    if (before != Phase.GameOver && phase == Phase.GameOver) OnGameOver();
                    else if (before == Phase.GameOver && phase != Phase.GameOver) OnRestart();
                }
                break;
            }
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
                Log("Le joueur " + NameOf(from) + " a vendu " + def.name + " pour " + def.sellPrice + " golds.");
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
                    Log("Le joueur " + NameOf(from) + " a acheté " + def.name + " pour " + price + " golds.");
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
                    if (terminalUser.Length == 0 || terminalUser == from || !net.Roster.ContainsKey(terminalUser))
                    {
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
                else hud.Toast("Un autre joueur utilise déjà le Terminal");
                break;
            case "TS" when f.Length >= 2:
                terminalUser = f[1];
                UpdateScreens();
                break;
            case "RV" when host && f.Length >= 2:
            {
                string target = f[1];
                if (target == from || !net.Roster.ContainsKey(target) || IsDeadForHost(from) || !IsDeadForHost(target)) break;
                net.ToAll("RA|" + target + "|" + from);
                Log("Le joueur " + NameOf(target) + " a été réanimé par " + NameOf(from) + ".");
                if (net.Remotes.TryGetValue(target, out var rp)) rp.SetStatus(AnimState.Idle, 1, 0);
                break;
            }
            case "RA" when f.Length >= 3:
                GameAudio.Play3D("revive", KennelSpawn(f[1]), 0.9f, 30f, 0f);
                if (f[1] == net.MyId && status.Dead && phase != Phase.GameOver)
                {
                    ReviveLocal(KennelSpawn(net.MyId), 1);
                    hud.Toast(NameOf(f[2]) + " vous a réanimé !");
                }
                break;
            case "DE" when host && f.Length >= 2:
                StatsOf(from).deaths++;
                net.ToAll(StatsMsg());
                Log("Le joueur " + NameOf(from) + " est mort par " + f[1] + ".");
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
                int id = nextBeaconId++;
                int kind = f.Length >= 5 ? Mathf.Clamp(NetworkGame.I(f[4]), 0, 3) : 0;
                string msg = "BW|" + id + "|" + f[1] + "|" + f[2] + "|" + f[3] + "|" + kind;
                beaconMessages[id] = msg;
                net.ToAll(msg);
                break;
            }
            case "BW" when f.Length >= 5:
            {
                var pos = new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4]));
                var kind = f.Length >= 6 ? (DeviceKind)Mathf.Clamp(NetworkGame.I(f[5]), 0, 3) : DeviceKind.Beacon;
                beacons.Add(NetworkGame.I(f[1]), pos, kind);
                GameAudio.Play3D("pop", pos, 0.8f, 15f, 0.1f, 0.8f);
                break;
            }
            case "BG" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                Beacon device = beacons.Get(id);
                if (device != null && beaconMessages.Remove(id))
                {
                    noiseAt[from] = Time.time;
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
                break;
            }
            case "BC" when host:
                beacons.Charge = GameRules.BeaconCharge;
                net.ToAll("BR");
                BroadcastState();
                break;
            case "BR":
                if (beacons.Hub != null) GameAudio.Play3D("orb_on", beacons.Hub.Top, 1f, 30f, 0f, 1.2f);
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
                Log("Le joueur " + NameOf(from) + " a vendu " + def.name + " pour " + def.sellPrice + " golds.");
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
                    SetPogo(Pogo.Mood.Closed, from);
                    net.ToPeer(from, "JK");
                }
                break;
            case "JX" when f.Length >= 4:
                pogo.SetState((Pogo.Mood)Mathf.Clamp(NetworkGame.I(f[1]), 0, 2), f[2] == "1", f[3]);
                break;
            case "JK":
                if (!status.Dead && !Invincible)
                {
                    // Droit dans les yeux de Pogo, puis la mort (la caméra ne quitte le corps qu'après).
                    OrbitCamera.StartJumpscare(pogo.Head, Pogo.EyesLocal);
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
                bool ok = orb >= 0 && orb < GameRules.OrbCount && phase == Phase.Running && GameRules.OrbActive(orb, round) && (orbsBroken & (1 << orb)) == 0;
                net.ToPeer(from, "OU|" + (ok ? f[2] : "-1"));
                if (!ok) break;
                orbsBroken |= 1 << orb;
                Log("Le joueur " + NameOf(from) + " a brisé l'orbe n°" + (orb + 1) + ".");
                if (orbsBroken == (1 << GameRules.OrbCount) - 1) Log("Les trois orbes sont brisés : la porte de la Zone 2 s'ouvre !");
                UpdateZone2(true);
                BroadcastState();
                break;
            }
            case "OU" when f.Length >= 2:
            {
                int slot = NetworkGame.I(f[1]);
                if (slot >= 0 && slot < GameRules.InventorySize && status[slot] == ItemType.Cle)
                {
                    status.RemoveAt(slot);
                    hud.Toast("La Clé tourne dans la serrure... l'orbe se brise !");
                }
                else if (slot < 0) hud.Toast("Cet orbe ne peut pas être brisé.");
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

            // ---------- Passer la manche ----------
            case "SK" when host:
            {
                if (phase != Phase.Running || progress < quota)
                {
                    net.ToPeer(from, "SR|-1");
                    break;
                }
                // 10 golds par tranche de 10 secondes restant au Compteur de Temps.
                int bonus = Mathf.FloorToInt(timeLeft / 10f) * 10;
                cash += bonus;
                Log("Le joueur " + NameOf(from) + " passe la manche " + round + " : " + bonus + " golds ajoutés à la cagnotte.");
                net.ToPeer(from, "SR|" + bonus);
                EndRound();
                break;
            }
            case "SR" when f.Length >= 2:
                terminal.OnSkipResult(NetworkGame.I(f[1]));
                break;

            // ---------- Entités ----------
            case "EN" when !host && f.Length >= 3:
                ReceiveEntities(NetworkGame.D(f[1]), f[2]);
                break;
            case "LK" when host && f.Length >= 3:
                if (entities.TryGetValue(NetworkGame.I(f[1]), out var watched) && watched is Dame watchedDame && watched.TargetId == from)
                    watchedDame.Watched = f[2] == "1";
                break;
            case "DF" when f.Length >= 2:
                if (!status.Dead && !Invincible && !Teleporting) player.Immobilize(NetworkGame.P(f[1]));
                break;
            case "EH" when f.Length >= 6:
            {
                if (status.Dead || Invincible || Teleporting) break;
                var from2 = new Vector3(NetworkGame.P(f[2]), player.transform.position.y, NetworkGame.P(f[3]));
                DamageLocal(NetworkGame.I(f[1]), EntityName((EntityKind)NetworkGame.I(f[5])));
                if (!status.Dead) player.Knockback(player.transform.position - from2, NetworkGame.P(f[4]));
                break;
            }
            case "TA":
                if (!status.Dead) GameAudio.Play2D("tour_alert", 0.8f);
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
        if (maze == null) return;
        // Canal non ordonné : un état plus ancien que le dernier reçu ne doit pas recréer ni reculer une entité.
        if (time <= lastEntityTime) return;
        lastEntityTime = time;
        seenEntities.Clear();
        foreach (string entry in data.Split(';'))
        {
            string[] p = entry.Split(',');
            if (p.Length < 10) continue;
            int id = NetworkGame.I(p[0]);
            var kind = (EntityKind)Mathf.Clamp(NetworkGame.I(p[1]), (int)EntityKind.Dame, (int)EntityKind.Spider);
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
