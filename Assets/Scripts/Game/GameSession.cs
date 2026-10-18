using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Boucle de jeu : Hub, manches, quota, cagnotte, objets, Terminal, Chenils, logs, morts et réanimations.
// L'hôte fait autorité sur l'état de la partie ; chaque client gère les PV et l'inventaire de son personnage.
// Messages (en plus de ceux de NetworkGame) :
//   G|graine|manche|quota|progression|cagnotte|phase|temps    hôte -> tous : état de la partie
//   IL|manche|id,id...   IR|manche|id                          objets déjà ramassés / ramassé
//   PK|id -> GV|type (0 = refusé)                              ramassage
//   SL|type                                                     vente au Terminal
//   BY|type -> BO|ok|type|cagnotte                              achat au Terminal
//   TU|1/0 -> TG|1/0, TS|id                                     prise / libération du Terminal
//   RV|cible -> RA|cible|sauveteur                              réanimation depuis un Chenil
//   DE|cause                                                    mort d'un joueur
//   LG|texte                                                    ligne de l'Écran de Logs
//   SC|manche|id,golds,morts;id,golds,morts...                  Tableau de Manche
public class GameSession : MonoBehaviour
{
    enum Phase { Waiting = 0, Running = 1, GameOver = 2 }

    static readonly Color HubAmbient = new Color(0.42f, 0.36f, 0.32f);
    static readonly Color MazeAmbient = new Color(0.035f, 0.035f, 0.04f);
    const float HubFog = 0.01f, MazeFog = 0.085f;

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

    Transform worldRoot, itemsRoot;
    WorldBuilder.HubRefs hub;
    WorldBuilder.MazeRefs maze;
    RedButton button;
    readonly Dictionary<string, Kennel> kennels = new Dictionary<string, Kennel>();
    readonly Dictionary<int, ItemPickup> items = new Dictionary<int, ItemPickup>();

    // État de la partie (autorité : hôte).
    int seed, builtSeed = -1, itemsRound = -1;
    int round = 1, quota, progress, cash;
    Phase phase = Phase.Waiting;
    float timeLeft = GameRules.RoundDuration;
    readonly HashSet<int> picked = new HashSet<int>();
    readonly List<string> logs = new List<string>();
    string terminalUser = "";
    // Tableau de Manche : golds gagnés et morts de chaque joueur pendant la manche en cours.
    class RoundStats { public int golds, deaths; }
    readonly Dictionary<string, RoundStats> roundStats = new Dictionary<string, RoundStats>();
    int statsRound = 1;

    float syncTimer, regenAcc, screenTimer;
    int pendingPickups;
    string spectateId;

    public PlayerPresence LocalPresence => net.InGame ? presence : null;
    public bool LocalDead => status.Dead;
    public int LocalHp => status.Hp;
    public int Cash => cash;
    public bool IsHost => net.IsHost;
    public bool IsGameOver => phase == Phase.GameOver;
    public bool HasFreeSlot => status.FreeSlots - pendingPickups > 0;

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
        itemsRoot = new GameObject("Objets").transform;
        itemsRoot.SetParent(worldRoot, false);

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
        picked.Clear();
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        ApplyWorld();
    }

    public void OnLocalEnter()
    {
        audioMuteUntil = Time.unscaledTime + 2f;
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
        hud.Show(false);
        if (maze != null) Destroy(maze.root);
        maze = null;
        builtSeed = itemsRound = -1;
        ClearItems();
        foreach (var k in kennels.Values) if (k != null) Destroy(k.gameObject);
        kennels.Clear();
        phase = Phase.Waiting;
        round = 1;
        quota = GameRules.QuotaForRound(1);
        progress = cash = 0;
        timeLeft = GameRules.RoundDuration;
        logs.Clear();
        terminalUser = "";
        roundStats.Clear();
        statsRound = 1;
        status.ClearInventory();
        status.SetDead(false);
        status.SetHp(GameRules.MaxHp);
        presence.Alive = true;
        spectateId = null;
        if (orbit != null) orbit.target = player.transform;
        hub.door.SetOpen(false, true);
        button.gameObject.SetActive(true);
        UpdateScreens();
    }

    // Construit le labyrinthe (nouvelle graine) et répartit les objets de la manche.
    void ApplyWorld()
    {
        if (seed != builtSeed)
        {
            if (maze != null) Destroy(maze.root);
            maze = WorldBuilder.BuildMaze(worldRoot, seed);
            builtSeed = seed;
            itemsRound = -1;
        }
        if (itemsRound != round) SpawnItems();
        hub.door.SetOpen(phase != Phase.Waiting);
        button.gameObject.SetActive(phase == Phase.Waiting);
        UpdateScreens();
    }

    void SpawnItems()
    {
        ClearItems();
        itemsRound = round;
        var rnd = new System.Random(seed * 31 + round * 7919);
        var spots = new List<Vector3>(maze.itemSpots);
        for (int i = spots.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }
        int count = Mathf.Min(GameRules.DucksForRound(round), spots.Count);
        for (int id = 0; id < count; id++)
            items[id] = ItemPickup.Create(itemsRoot, id, ItemType.Canard, spots[id]);
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

    // ================= Mise à jour =================

    void Update()
    {
        bool inGame = net.InGame;
        PlayerController.InputLocked = !inGame || TerminalUI.IsOpen || phase == Phase.GameOver;
        OrbitCamera.CursorLockBlocked = TerminalUI.IsOpen || GameHud.GameOverVisible;

        if (phase == Phase.Running)
        {
            timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);
            if (net.IsHost && inGame && timeLeft <= 0f) EndRound();
        }
        if (net.IsHost && inGame)
        {
            syncTimer += Time.unscaledDeltaTime;
            if (syncTimer >= 1f) BroadcastState();
        }

        UpdateAtmosphere();
        UpdateGameSounds(inGame);
        if (!inGame) return;

        presence.Alive = !status.Dead;
        presence.Sneaking = player.Sneaking;

        // Régénération dans le Hub.
        if (!status.Dead && status.Hp < GameRules.MaxHp && WorldBuilder.IsInHub(player.transform.position))
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
    }

    void UpdateAtmosphere()
    {
        Camera cam = Camera.main;
        bool inHub = cam == null || WorldBuilder.IsInHub(cam.transform.position);
        float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
        RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, inHub ? HubAmbient : MazeAmbient, k);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, inHub ? HubFog : MazeFog, k);
        playerLight.enabled = net.InGame && !status.Dead && !WorldBuilder.IsInHub(player.transform.position);
        UpdateInsectFlee();
    }

    // Sons liés à l'état de la partie, détectés chez tous les clients (hôte compris) par changement d'état :
    // Bouton Rouge, quota validé, game over, tic-tac de la dernière minute près du Compteur de Temps.
    Phase soundPhase;
    int soundRound = 1, lastTickSecond = -1;
    float audioMuteUntil;

    void UpdateGameSounds(bool inGame)
    {
        // Pas de fanfare ni de buzzer en rejoignant une partie déjà commencée : on se cale sur l'état reçu.
        bool mute = !inGame || Time.unscaledTime < audioMuteUntil;
        if (!mute)
        {
            if (soundPhase == Phase.Waiting && phase == Phase.Running)
                GameAudio.Play3D("button", hub.buttonAnchor.position + Vector3.up, 0.8f, 40f, 0f);
            if (phase == Phase.Running && round > soundRound) GameAudio.Play2D("fanfare", 0.8f);
            if (soundPhase != Phase.GameOver && phase == Phase.GameOver) GameAudio.Play2D("gameover", 0.9f);
        }
        soundPhase = phase;
        soundRound = round;

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

    void ReviveLocal(Vector3 position, int hp)
    {
        status.SetDead(false);
        status.SetHp(hp);
        presence.Alive = true;
        avatar.SetDead(false);
        player.SetFrozen(false);
        player.Teleport(position, Mathf.Atan2(-position.x, -position.z) * Mathf.Rad2Deg);
        spectateId = null;
        if (orbit != null) orbit.target = player.transform;
        RefreshHud();
    }

    // Dégâts reçus par le personnage local. Le Hub est un endroit sûr.
    public void DamageLocal(int amount, string cause)
    {
        if (status.Dead || phase == Phase.GameOver || !net.InGame) return;
        if (WorldBuilder.IsInHub(player.transform.position)) return;
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
                if (status.Hp >= GameRules.MaxHp)
                {
                    hud.Toast("Votre jauge de vie est déjà pleine.");
                    return;
                }
                status.RemoveAt(slot);
                status.SetHp(status.Hp + Items.CroquetteHeal);
                GameAudio.Play2D("eat", 0.8f, 0.1f);
                hud.Toast("Miam ! +" + Items.CroquetteHeal + " PV");
                break;
            case ItemType.Canard:
                hud.Toast("Le Canard en Plastique ne sert qu'à être vendu au Terminal.");
                break;
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

    // ================= Hôte =================

    string StateMsg() => "G|" + seed + "|" + round + "|" + quota + "|" + progress + "|" + cash + "|" + (int)phase + "|" + NetworkGame.F(timeLeft);

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
    }

    void Log(string text)
    {
        string line = PeerNet.LocalNow.ToString("HH:mm") + " : " + text;
        net.ToAll("LG|" + line);
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
            ResetRoundStats(round);
            ApplyWorld();
            BroadcastState();
        }
        else
        {
            phase = Phase.GameOver;
            timeLeft = 0f;
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
        picked.Clear();
        ResetRoundStats(1);
        ApplyWorld();
        BroadcastState();
        OnRestart();
    }

    // Quota non atteint : tous les personnages meurent.
    void OnGameOver()
    {
        terminal.Close(false);
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
        ReviveLocal(SpawnPoint(), GameRules.MaxHp);
    }

    // ================= Réception =================

    public void HandleMessage(string from, string[] f, string raw)
    {
        bool host = net.IsHost;
        switch (f[0])
        {
            case "G" when !host && f.Length >= 8:
            {
                Phase before = phase;
                seed = NetworkGame.I(f[1]);
                round = NetworkGame.I(f[2]);
                quota = NetworkGame.I(f[3]);
                progress = NetworkGame.I(f[4]);
                cash = NetworkGame.I(f[5]);
                phase = (Phase)Mathf.Clamp(NetworkGame.I(f[6]), 0, 2);
                timeLeft = NetworkGame.P(f[7]);
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
                if (cash >= def.buyPrice)
                {
                    cash -= def.buyPrice;
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
                if (ok && def != null) status.Add(def.type);
                GameAudio.Play2D(ok ? "register" : "error", 0.7f, 0f, ok ? 1.15f : 1f);
                terminal.OnBuyResult(ok, def, NetworkGame.I(f[3]));
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
        }
    }

    bool IsDeadForHost(string id)
    {
        if (id == net.MyId) return status.Dead;
        return net.Remotes.TryGetValue(id, out var rp) && rp != null && rp.Dead;
    }

    string NameOf(string id) => net.Roster.TryGetValue(id, out var info) ? info.name : "?";
}
