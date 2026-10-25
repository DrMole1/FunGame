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

    readonly NetSmoother smoother = new NetSmoother();
    bool hasTarget;
    CharacterAvatar avatar;
    PlayerPresence presence;
    NameTag nameTag;
    CapsuleCollider body;
    CharacterLibrary library;
    GameObject energyBall, cannonBall;
    bool hiddenInCannon, flyingBall;
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
        if (!dead) avatar.SetState(anim);
    }

    // Téléportation : le trajet complet est reçu au départ et rejoué ici à vitesse constante, sans dépendre des
    // positions reçues (à 70 m/s, un paquet en retard de 50 ms faisait sauter la boule de 3,5 m).
    const float TeleportRelease = 0.2f;
    System.Collections.Generic.List<Vector3> tpPath;
    int tpLeg;
    float tpReleaseAt;

    public void PlayTeleport(System.Collections.Generic.List<Vector3> path)
    {
        if (path.Count == 0) return;
        tpPath = path;
        tpLeg = 0;
        transform.position = path[0];
        // Si l'état « en téléportation » n'arrive jamais, le rejeu s'arrête de lui-même.
        tpReleaseAt = teleporting ? float.MaxValue : Time.time + 1f;
    }

    void Update()
    {
        if (tpPath != null)
        {
            if (Time.time < tpReleaseAt)
            {
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
}
