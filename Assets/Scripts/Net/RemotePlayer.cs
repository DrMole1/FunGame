using UnityEngine;

// Personnage d'un autre joueur : rejoue en douceur les positions reçues du réseau (NetSmoother),
// applique son animation et son état.
public class RemotePlayer : MonoBehaviour
{
    public string PlayerId { get; private set; }
    public string PlayerName { get; private set; }
    public int Hp { get; private set; } = GameRules.MaxHp;
    public bool Dead => presence.Alive == false;
    // Terminal ouvert chez ce joueur (dernier état reçu).
    public bool InTerminal { get; private set; }
    // En téléportation (état reçu, ou trajet encore en cours de rejeu), et d'où il est parti.
    public bool Teleporting => teleporting || tpPath != null;
    public Vector3 TeleportFrom { get; private set; }
    // V0.7.11 (7.16) : Sac à Os dans le dos, et nombre d'Os qu'il contient (état reçu).
    public bool HasBoneBag { get; private set; }
    public int BagBones { get; private set; }
    public CharacterAvatar Avatar => avatar;

    readonly NetSmoother smoother = new NetSmoother();
    bool hasTarget;
    CharacterAvatar avatar;
    bool wasSitting;
    PlayerPresence presence;
    NameTag nameTag;
    CapsuleCollider body;
    CharacterLibrary library;
    GameObject energyBall, cannonBall;
    bool hiddenInCannon, flyingBall, corpseGone;
    bool teleporting;

    public static RemotePlayer Create(string id, string playerName, Color color, CharacterLibrary library, int character, Vector3 position)
    {
        var go = new GameObject("Remote_" + playerName);
        go.layer = 2; // Ignore Raycast : ne bloque ni la visée ni la caméra
        go.transform.position = position;

        // Cinématique : les autres joueurs sont des obstacles qui poussent le joueur local.
        var rp = go.AddComponent<RemotePlayer>();
        rp.body = CharacterAvatar.EnsureCapsule(go, library);
        rp.library = library;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        rp.PlayerId = id;
        rp.PlayerName = playerName;
        rp.avatar = go.AddComponent<CharacterAvatar>();
        rp.avatar.SetCharacter(library, character);
        rp.presence = go.AddComponent<PlayerPresence>();
        rp.presence.PlayerId = id;
        go.AddComponent<Footsteps>();

        rp.nameTag = go.AddComponent<NameTag>();
        rp.nameTag.height = library.height * 0.5f + 0.35f;
        rp.nameTag.Set(playerName, color);
        rp.tagColor = color;
        // V0.7.11 : cible d'interaction (Collier à Laisse, Os à glisser dans son Sac à Os), active seulement quand elle sert.
        PlayerTarget.Attach(rp, library.height);
        return rp;
    }

    Color tagColor;

    // Pseudo changé en cours de partie (pseudo tiré du skin, qui suit le skin).
    public void SetName(string playerName)
    {
        if (playerName == PlayerName) return;
        PlayerName = playerName;
        nameTag.Set(playerName, tagColor);
    }

    public void SetCharacter(CharacterLibrary library, int character) => avatar.SetCharacter(library, character);

    // Rebond sur le Trampoline : l'animation de saut repart du début, même s'il était déjà en l'air.
    public void PlayBounce() { if (!Dead) avatar.SetState(AnimState.Jump, true); }

    // État reçu, horodaté par l'émetteur ; ignoré s'il est plus ancien que le dernier (arrivé dans le désordre).
    public void SetTarget(double time, Vector3 pos, float yaw, AnimState anim, int hp, int flags)
    {
        if (!smoother.Push(time, pos, yaw)) return;
        // La logique de jeu (La Dame, détection...) utilise la position la plus récente, pas celle affichée.
        presence.HasNetPosition = true;
        presence.NetPosition = pos;
        SetStatus(anim, hp, flags);
        if (!hasTarget)
        {
            hasTarget = true;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
    }

    public void SetStatus(AnimState anim, int hp, int flags)
    {
        bool dead = (flags & NetworkGame.FlagDead) != 0;
        // V0.6.13 : Protocole en main, bras tendu vers un glyphe, assis à l'arrivée (il se relève).
        avatar.SetHolding((flags & NetworkGame.FlagProtocol) != 0 && !dead, (DocumentKind)((flags >> NetworkGame.FlagDocumentShift) & 7));
        // V0.7.11 : Lunettes PEACE, Sac à Os et Mégaphone, vus sur le personnage.
        avatar.SetEquipment((flags & NetworkGame.FlagGlasses) != 0, (flags & NetworkGame.FlagBoneBag) != 0, (flags & NetworkGame.FlagMegaphone) != 0 && !dead);
        HasBoneBag = (flags & NetworkGame.FlagBoneBag) != 0;
        BagBones = (flags >> NetworkGame.FlagBonesShift) & 15;

        avatar.SetArmRaised((flags & NetworkGame.FlagGlyphArm) != 0 && !dead);
        avatar.SetSeated((flags & NetworkGame.FlagSeated) != 0 && !dead);
        bool sitting = (flags & NetworkGame.FlagSitting) != 0 && !dead;
        if (sitting && !wasSitting) avatar.PlaySitUp();
        wasSitting = sitting;
        if (hasTarget && !dead && hp < Hp) GameAudio.Play3D("hurt", transform.position, 0.5f, 15f, 0.1f);
        Hp = hp;
        InTerminal = (flags & NetworkGame.FlagTerminal) != 0;
        bool sneaking = (flags & NetworkGame.FlagSneak) != 0;
        if (sneaking != presence.Sneaking)
        {
            // Accroupi : capsule plus basse et pseudo au-dessus de la tête baissée.
            CharacterAvatar.SetCrouched(body, library, sneaking);
            nameTag.height = library.height * (sneaking ? GameRules.SneakHeightFactor - 0.5f : 0.5f) + 0.35f;
        }
        presence.Sneaking = sneaking;
        // En téléportation : une boule d'énergie à la place du personnage.
        bool tele = (flags & NetworkGame.FlagTeleport) != 0 && !dead;
        if (tele != teleporting)
        {
            if (tele && tpPath == null) TeleportFrom = transform.position;
            teleporting = tele;
            presence.Teleporting = tele;
            // Fin du voyage : on garde le rejeu le temps que les positions reçues (affichées en léger retard) rattrapent.
            if (tpPath != null) tpReleaseAt = tele ? float.MaxValue : Time.time + TeleportRelease;
            if (energyBall == null) energyBall = GameSession.BuildEnergyBall(transform);
            energyBall.SetActive(tele);
            avatar.SetHidden(tele);
            nameTag.Show(!tele && !dead);
        }
        // Dans un Canon : invisible ; projeté : un boulet de canon à la place du personnage (V0.4.6).
        bool inCannon = (flags & NetworkGame.FlagHidden) != 0 && !dead;
        bool ball = (flags & NetworkGame.FlagCannonball) != 0 && !dead;
        if (inCannon != hiddenInCannon || ball != flyingBall)
        {
            hiddenInCannon = inCannon;
            presence.InCannon = inCannon;
            flyingBall = ball;
            if (cannonBall == null) cannonBall = GameSession.BuildCannonBall(transform);
            cannonBall.SetActive(ball);
            avatar.SetHidden(inCannon || ball || teleporting);
            nameTag.Show(!inCannon && !ball && !teleporting && !dead);
        }
        if (dead != Dead)
        {
            // GDD 12 (V0.5.2) : Perso_Mort n'est entendu que par le joueur qui meurt (pas chez les autres).
            presence.Alive = !dead;
            avatar.SetDead(dead);
            nameTag.Show(!dead);
            body.enabled = !dead;
        }
        // Son cadavre a disparu avec La Tête de Cloche.
        bool gone = (flags & NetworkGame.FlagCorpseGone) != 0 && dead;
        if (gone != corpseGone)
        {
            corpseGone = gone;
            avatar.SetHidden(gone || hiddenInCannon || flyingBall || teleporting);
        }
        if (!dead) avatar.SetState(anim);
    }

    // Quota non atteint : le corps réapparaît (plus de boule d'énergie ni de boulet) pour exploser.
    public void RevealBody()
    {
        if (Dead || corpseGone) return;
        tpPath = null;
        if (energyBall != null) energyBall.SetActive(false);
        if (cannonBall != null) cannonBall.SetActive(false);
        avatar.SetHidden(false);
    }

    // Téléportation : le trajet complet est reçu au départ et rejoué ici à vitesse constante, sans dépendre des
    // positions reçues (à 70 m/s, un paquet en retard de 50 ms faisait sauter la boule de 3,5 m).
    const float TeleportRelease = 0.2f;
    // V0.7.7 : le rejeu est calé sur l'heure de départ (horloge de l'émetteur) et suit la même ligne de temps que les positions
    // lissées. Chez un joueur non hôte, le trajet (canal fiable, relayé par l'hôte) arrive après les premières positions
    // (canal rapide) : avant, la boule repartait alors du disque et rejouait le trajet en retard, puis sautait à l'arrivée.
    // Désormais, elle reprend là où elle doit être à cet instant.
    System.Collections.Generic.List<Vector3> tpPath;
    double tpStart;
    bool tpTimed;
    int tpLeg;
    float tpReleaseAt;

    public void PlayTeleport(System.Collections.Generic.List<Vector3> path, int fluxColor, double senderStart = -1)
    {
        if (path.Count == 0) return;
        tpTimed = senderStart >= 0 && smoother.HasData;
        tpStart = senderStart;
        // V0.7.4 (Note 06) : la boule a la couleur du flux suivi.
        if (energyBall == null) energyBall = GameSession.BuildEnergyBall(transform);
        GameSession.TintEnergyBall(energyBall, fluxColor);
        tpPath = path;
        tpLeg = 0;
        TeleportFrom = path[0];
        if (tpTimed) transform.position = PointAlong(path, (float)(smoother.ShownSenderTime - tpStart) * GameRules.TeleportSpeed);
        else transform.position = path[0];
        // Si l'état « en téléportation » n'arrive jamais, le rejeu s'arrête de lui-même.
        tpReleaseAt = teleporting ? float.MaxValue : Time.time + 1f;
    }

    void Update()
    {
        if (tpPath != null)
        {
            if (Time.time < tpReleaseAt)
            {
                if (tpTimed)
                {
                    transform.position = PointAlong(tpPath, (float)(smoother.ShownSenderTime - tpStart) * GameRules.TeleportSpeed);
                    return;
                }
                float move = GameRules.TeleportSpeed * Time.deltaTime;
                Vector3 pos = transform.position;
                while (move > 0f && tpLeg < tpPath.Count)
                {
                    Vector3 goal = tpPath[tpLeg];
                    float d = Vector3.Distance(pos, goal);
                    if (d <= move) { pos = goal; move -= d; tpLeg++; }
                    else { pos += (goal - pos) / d * move; move = 0f; }
                }
                transform.position = pos;
                return;
            }
            tpPath = null;
        }
        if (!hasTarget) return;
        smoother.Sample(out Vector3 pos2, out float yaw);
        transform.SetPositionAndRotation(pos2, Quaternion.Euler(0f, yaw, 0f));
    }

    // Point du trajet à 'distance' mètres du départ (avant le départ : le disque ; au-delà : l'arrivée).
    static Vector3 PointAlong(System.Collections.Generic.List<Vector3> path, float distance)
    {
        if (distance <= 0f) return path[0];
        for (int i = 0; i + 1 < path.Count; i++)
        {
            float d = Vector3.Distance(path[i], path[i + 1]);
            if (distance <= d) return d > 1e-4f ? Vector3.Lerp(path[i], path[i + 1], distance / d) : path[i + 1];
            distance -= d;
        }
        return path[path.Count - 1];
    }
}
