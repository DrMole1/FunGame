using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Boucle de jeu : Hub, manches, quota, cagnotte, objets, Terminal, Chenils, logs, morts et réanimations,
// sérénité et Abjuration, La Dame, orbes et porte de la Zone 2, Post-It et Boussole.
// L'hôte fait autorité sur l'état de la partie ; chaque client gère les PV, la sérénité et l'inventaire de son personnage.
// Messages (en plus de ceux de NetworkGame) :
//   G|graine|manche|quota|progression|cagnotte|phase|temps|boussole|orbes   hôte -> tous : état de la partie
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
//   EN|id,x,y,z,lacet,état,cible;...                            hôte -> tous : entités (La Dame)
//   LK|id|1/0                                                   le joueur chassé regarde (ou non) La Dame
//   DH|x|z                                                      hôte -> joueur : La Dame vous frappe depuis (x, z)
//   SK -> SR|bonus (-1 = refusé)                                /skip round : passer à la manche suivante
//   BK|pitch -> BA|id|pitch                                     aboiement d'un joueur
public class GameSession : MonoBehaviour
{
    enum Phase { Waiting = 0, Running = 1, GameOver = 2 }

    static readonly Color HubAmbient = new Color(0.42f, 0.36f, 0.32f);
    static readonly Color MazeAmbient = new Color(0.035f, 0.035f, 0.04f);
    static readonly Color MistFog = new Color(0.02f, 0.05f, 0.12f);
    const float HubFog = 0.01f, MazeFog = 0.085f;
    const float EntitySyncInterval = 0.1f;

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
    readonly Dictionary<string, Kennel> kennels = new Dictionary<string, Kennel>();
    readonly Dictionary<int, ItemPickup> items = new Dictionary<int, ItemPickup>();
    readonly Dictionary<int, PostItNote> notes = new Dictionary<int, PostItNote>();
    readonly Dictionary<int, Dame> dames = new Dictionary<int, Dame>();

    // État de la partie (autorité : hôte).
    int seed, builtSeed = -1, itemsRound = -1;
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
    int nextNoteId, nextDameId;
    bool hostAbjuration;

    float syncTimer, regenAcc, serenityRegenAcc, mistTimer, screenTimer, entityTimer;
    int pendingPickups;
    string spectateId;
    float mistAmount;
    float invincibleUntil; // fin de l'invincibilité qui suit un /tp
    readonly Queue<float> barkTimes = new Queue<float>();
    readonly Dictionary<int, bool> watching = new Dictionary<int, bool>();

    public PlayerPresence LocalPresence => net.InGame ? presence : null;
    public bool LocalDead => status.Dead;
    public int LocalHp => status.Hp;
    public int Cash => cash;
    public bool IsHost => net.IsHost;
    public bool IsGameOver => phase == Phase.GameOver;
    public bool HasFreeSlot => status.FreeSlots - pendingPickups > 0;
    public bool CompassUnlocked => compass;
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

        ConfigureRendering();
        GameAudio.Init();
        player.gameObject.AddComponent<Footsteps>();
        worldRoot = new GameObject("Monde").transform;
        hub = WorldBuilder.BuildHub(worldRoot);
        button = RedButton.Create(hub.buttonAnchor);
        TerminalStation.Create(hub.terminalAnchor);
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
        nextNoteId = nextDameId = 0;
        hostAbjuration = false;
        ApplyWorld();
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
        if (itemsRound != round) SpawnItems();
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
            int count = Mathf.Min(GameRules.DucksForRound(round, zone), spots.Count);
            for (int k = 0; k < count; k++, id++)
                items[id] = ItemPickup.Create(itemsRoot, id, ItemType.Canard, spots[k]);
        }
    }

    void ClearItems()
    {
        foreach (var it in items.Values) if (it != null) Destroy(it.gameObject);
        items.Clear();
    }

    void RemoveItem(int id)
    {
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
        PlayerController.InputLocked = !inGame || TerminalUI.IsOpen || PostItUI.IsOpen || phase == Phase.GameOver;
        OrbitCamera.CursorLockBlocked = TerminalUI.IsOpen || PostItUI.IsOpen || GameHud.GameOverVisible;

        if (phase == Phase.Running)
        {
            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            if (net.IsHost && inGame && timeLeft <= 0f) EndRound();
        }
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
        hud.SetPrompt(target != null ? "[E] " + target.Prompt : "");
        hud.SetCompass(compass && !status.Dead, player.transform.eulerAngles.y);
    }

    void UpdateAtmosphere()
    {
        Camera cam = Camera.main;
        bool inHub = cam == null || WorldBuilder.IsInHub(cam.transform.position);
        float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, inHub ? HubAmbient : MazeAmbient, k);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, inHub ? HubFog : MazeFog, k);
        playerLight.enabled = net.InGame && !status.Dead && !WorldBuilder.IsInHub(player.transform.position);

        // Brume de l'Abjuration : apparaît et se dissipe en douceur ; le brouillard bleuit quand on y est plongé.
        bool abjuration = net.InGame && AbjurationActive;
        mistAmount = Mathf.MoveTowards(mistAmount, abjuration ? 1f : 0f, Time.deltaTime / 4f);
        Shader.SetGlobalFloat("_Abjuration", mistAmount);
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
        float feet = p.y - player.GetComponent<CapsuleCollider>().height * 0.5f;
        bool inMist = !status.Dead && AbjurationActive && mistAmount > 0.5f && maze != null
            && maze.nav.ZoneAt(p) >= 0 && feet < GameRules.MistTop;
        // Dans la brume, le personnage avance 20 % moins vite.
        player.SpeedFactor = inMist ? GameRules.MistSpeedMultiplier : 1f;
        if (!inMist)
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
            hud.SetSpectate("");
            return;
        }
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
        hub.terminalText.text = TerminalUI.Menu + (user != null ? "\n\n> Utilisé par " + user : "");
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

    bool Invincible => Time.time < invincibleUntil;

    // Dégâts reçus par le personnage local. Le Hub est un endroit sûr ; juste après un /tp, aucun dégât.
    public void DamageLocal(int amount, string cause)
    {
        if (status.Dead || phase == Phase.GameOver || !net.InGame) return;
        if (WorldBuilder.IsInHub(player.transform.position) || Invincible) return;
        status.SetHp(status.Hp - amount);
        hud.FlashDamage();
        GameAudio.Play2D("hurt", 0.9f, 0.08f);
        if (status.Hp <= 0) KillLocal(cause);
    }

    void KillLocal(string cause)
    {
        if (status.Dead) return;
        status.SetDead(true);
        status.ClearInventory(); // tous les objets portés sont perdus
        pendingPickups = 0;
        presence.Alive = false;
        avatar.SetDead(true);
        player.SetFrozen(true);
        terminal.Close();
        postItUI.Close();
        // Au game over, l'accord lugubre remplace les râles.
        if (cause != null) GameAudio.Play3D("death", player.transform.position, 1f, 30f, 0.06f);
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
                hud.Toast("Le Canard en Plastique ne sert qu'à être vendu au Terminal.");
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

    // ================= Entités (La Dame) =================

    // /spawn : fait apparaître l'entité 'id' dans la Zone 1. Retourne le message du Terminal.
    public string SpawnEntity(int id)
    {
        if (!net.IsHost) return "Commande Incorrecte.";
        if (maze == null || phase == Phase.GameOver) return "Impossible pour le moment.";
        switch (id)
        {
            case 1: return "Les Lasers ne peuvent plus apparaître une fois le terrain généré.";
            case 2:
                SpawnDame(0, new System.Random(seed + nextDameId * 131 + Mathf.RoundToInt(Time.time * 10f)));
                return "La Dame rôde désormais dans la Zone 1...";
            default: return "Aucune entité n'a l'ID " + id + ".";
        }
    }

    // Début de manche (hôte) : les entités de la manche précédente disparaissent, celles du tableau apparaissent.
    void SpawnRoundEntities()
    {
        ClearEntities();
        if (maze == null || phase != Phase.Running) return;
        var rnd = new System.Random(seed * 17 + round * 613);
        for (int zone = 0; zone < 2; zone++)
            for (int k = 0; k < GameRules.NightmaresForRound(round, zone); k++)
                SpawnDame(zone, rnd);
    }

    void SpawnDame(int zone, System.Random rnd)
    {
        // Sur la grille de navigation, loin du Hub et des joueurs.
        if (!maze.nav.Ready) maze.nav.BuildAll();
        Vector3 away = Vector3.zero;
        int node = maze.nav.RandomNode(zone, rnd, away, 30f);
        if (node < 0) return;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            bool near = false;
            foreach (var p in PlayerPresence.All)
                if (p.Alive && Vector3.Distance(p.Position, maze.nav.Position(node)) < 18f) near = true;
            if (!near) break;
            node = maze.nav.RandomNode(zone, rnd, away, 30f);
        }
        Vector3 pos = maze.nav.Position(node);
        int id = nextDameId++;
        dames[id] = Dame.Create(entitiesRoot, id, pos, (float)rnd.NextDouble() * 360f, maze.nav, OnDameHit);
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
        foreach (var d in dames.Values) if (d != null) Destroy(d.gameObject);
        dames.Clear();
        watching.Clear();
    }

    void SimulateEntities()
    {
        if (phase == Phase.Running)
            foreach (var d in dames.Values) d.Simulate(Time.deltaTime, PlayerPresence.All);
        entityTimer += Time.unscaledDeltaTime;
        if (entityTimer < EntitySyncInterval) return;
        entityTimer = 0f;
        var sb = new StringBuilder("EN|");
        bool first = true;
        foreach (var d in dames.Values)
        {
            if (!first) sb.Append(';');
            first = false;
            Vector3 p = d.transform.position;
            sb.Append(d.Id).Append(',').Append(NetworkGame.F(p.x)).Append(',').Append(NetworkGame.F(p.y)).Append(',').Append(NetworkGame.F(p.z))
              .Append(',').Append(NetworkGame.F(d.transform.eulerAngles.y)).Append(',').Append((int)d.Current).Append(',').Append(d.TargetId);
        }
        PeerNet.Broadcast(sb.ToString());
    }

    void UpdateEntities()
    {
        foreach (var d in dames.Values) d.Present(net.IsHost, Time.deltaTime);
    }

    // La Dame frappe : le joueur visé encaisse les dégâts chez lui (hôte : message direct).
    void OnDameHit(Dame dame, string targetId)
    {
        Vector3 p = dame.transform.position;
        net.ToPeer(targetId, "DH|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.z));
    }

    // Le joueur chassé signale à l'hôte s'il regarde La Dame (elle accélère alors).
    void UpdateWatching()
    {
        Camera cam = Camera.main;
        foreach (var d in dames.Values)
        {
            bool chased = d.Current != Dame.State.Wander && d.TargetId == net.MyId && !status.Dead;
            bool looking = false;
            if (chased && cam != null)
            {
                Vector3 chest = d.transform.position + Vector3.up * 1.4f;
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
    public string RequestSkipRound()
    {
        if (phase != Phase.Running) return "Aucune manche n'est en cours.";
        if (progress < quota) return "Le quota de la manche n'est pas encore atteint.";
        net.ToHost("SK");
        return null;
    }

    // /tp (hôte) : téléporte le personnage à 3 mètres de l'ennemi le plus proche ("enemy"), de la porte de la
    // Zone 2 ("door") ou d'un joueur (son pseudo). Retourne le message à afficher, ou null si la commande est invalide.
    // Le personnage est ensuite invincible pendant quelques secondes.
    public string Teleport(string target)
    {
        string result = TeleportTo(target);
        if (result != null) invincibleUntil = Time.time + GameRules.TeleportInvincibility;
        return result;
    }

    string TeleportTo(string target)
    {
        if (!net.IsHost || status.Dead) return null;
        Vector3 here = player.transform.position;
        string lower = target.ToLowerInvariant();
        if (lower == "enemy")
        {
            Dame nearest = null;
            foreach (var d in dames.Values)
                if (d != null && (nearest == null || (d.transform.position - here).sqrMagnitude < (nearest.transform.position - here).sqrMagnitude))
                    nearest = d;
            if (nearest == null) return null;
            TeleportNear(nearest.transform.position);
            return "Téléporté près de La Dame.";
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
        + "|" + (compass ? 1 : 0) + "|" + orbsBroken;

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
        if (active && !hostAbjuration) Log("L'Abjuration se répand ... Le désespoir attend les plus faibles.");
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
            Log("Le quota de la manche " + done + " est validé ! Nouveau quota : " + quota);
            if (hostAbjuration) Log("L'Abjuration se retire ... Pour le moment.");
            hostAbjuration = false;
            if (done <= GameRules.OrbCount) Log("Un orbe s'éveille devant la porte de la Zone 2...");
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
        ResetRoundStats(1);
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
        status.ClearInventory();
        pendingPickups = 0;
        ClearNotes();
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
                if (type != ItemType.None && !status.Dead && status.Add(type) >= 0)
                    hud.Toast(Items.Get(type).name + " ramassé.");
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
                if (cash >= def.buyPrice)
                {
                    cash -= def.buyPrice;
                    if (def.type == ItemType.Boussole) compass = true;
                    Log("Le joueur " + NameOf(from) + " a acheté " + def.name + " pour " + def.buyPrice + " golds.");
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
                if (net.Remotes.TryGetValue(target, out var rp)) rp.SetTarget(rp.transform.position, rp.transform.eulerAngles.y, AnimState.Idle, 1, 0);
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
            case "EN" when !host:
                ReceiveEntities(raw.Length > 3 ? raw.Substring(3) : "");
                break;
            case "LK" when host && f.Length >= 3:
                if (dames.TryGetValue(NetworkGame.I(f[1]), out var watched) && watched.TargetId == from)
                    watched.Watched = f[2] == "1";
                break;
            case "DH" when f.Length >= 3:
            {
                if (status.Dead || Invincible) break;
                var from2 = new Vector3(NetworkGame.P(f[1]), player.transform.position.y, NetworkGame.P(f[2]));
                DamageLocal(GameRules.DameDamage, "La Dame");
                if (!status.Dead) player.Knockback(player.transform.position - from2, GameRules.DameKnockback);
                break;
            }
        }
    }

    // Clients : création, mise à jour et disparition des entités d'après l'état de l'hôte.
    readonly HashSet<int> seenEntities = new HashSet<int>();

    void ReceiveEntities(string data)
    {
        if (maze == null) return;
        seenEntities.Clear();
        foreach (string entry in data.Split(';'))
        {
            string[] p = entry.Split(',');
            if (p.Length < 7) continue;
            int id = NetworkGame.I(p[0]);
            var pos = new Vector3(NetworkGame.P(p[1]), NetworkGame.P(p[2]), NetworkGame.P(p[3]));
            float yaw = NetworkGame.P(p[4]);
            seenEntities.Add(id);
            if (!dames.TryGetValue(id, out var dame) || dame == null)
                dames[id] = dame = Dame.Create(entitiesRoot, id, pos, yaw, maze.nav, null);
            dame.SetNetState(pos, yaw, (Dame.State)Mathf.Clamp(NetworkGame.I(p[5]), 0, 2), p[6]);
        }
        var gone = new List<int>();
        foreach (int id in dames.Keys) if (!seenEntities.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            if (dames[id] != null) Destroy(dames[id].gameObject);
            dames.Remove(id);
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
