using UnityEngine;

// Personnage d'un autre joueur : rejoue en douceur les positions reçues du réseau (NetSmoother),
// applique son animation et son état.
public class RemotePlayer : MonoBehaviour
{
    public string PlayerId { get; private set; }
    public string PlayerName { get; private set; }
    public int Hp { get; private set; } = GameRules.MaxHp;
    public bool Dead => presence.Alive == false;

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
        return rp;
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
            flyingBall = ball;
            if (cannonBall == null) cannonBall = GameSession.BuildCannonBall(transform);
            cannonBall.SetActive(ball);
            avatar.SetHidden(inCannon || ball || teleporting);
            nameTag.Show(!inCannon && !ball && !teleporting && !dead);
        }
        if (dead != Dead)
        {
            var session = GameSession.Instance;
            if (dead && hasTarget && (session == null || !session.IsGameOver))
                GameAudio.Play3D("death", transform.position, 1f, 30f, 0.06f);
            presence.Alive = !dead;
            avatar.SetDead(dead);
            nameTag.Show(!dead);
            body.enabled = !dead;
        }
        if (!dead) avatar.SetState(anim);
    }

    void Update()
    {
        if (!hasTarget) return;
        smoother.Sample(out Vector3 pos, out float yaw);
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }
}
