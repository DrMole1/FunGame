using UnityEngine;

// Déplacement du joueur en ZQSD (relatif à la caméra), course (Shift), marche discrète (Ctrl) et saut (Espace).
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    enum Gait { Normal, Run, Sneak }

    public float moveSpeed = 4f;
    public float jumpForce = 4.6f;
    public Transform cameraTransform;

    // Bloque les commandes (lobby, Terminal, game over).
    public static bool InputLocked;

    Rigidbody rb;
    CapsuleCollider col;
    CharacterAvatar avatar;
    PlayerStatus status;
    // PlayerStatus est ajouté par NetworkGame après l'Awake de ce composant.
    PlayerStatus Status => status != null ? status : (status = GetComponent<PlayerStatus>());
    Vector3 inputDir;
    Gait gait;
    bool jumpRequested, grounded;
    Vector3 groundNormal = Vector3.up;
    float airborneLock;
    // Recul (coup de La Dame) : vitesse horizontale imposée qui s'amortit.
    Vector3 knockVelocity;
    float knockTimer;
    const float KnockDuration = 0.6f;

    // Immobilisé (attaque de La Dame) jusqu'à cet instant : plus de déplacement ni de saut.
    float lockedUntil;
    public void Immobilize(float seconds) => lockedUntil = Mathf.Max(lockedUntil, Time.time + seconds);
    bool Immobilized => Time.time < lockedUntil;

    // Multiplicateur de vitesse imposé par le jeu (brume de l'Abjuration).
    public float SpeedFactor { get; set; } = 1f;

    public AnimState CurrentAnimState => avatar != null ? avatar.State : AnimState.Idle;
    public bool Sneaking => gait == Gait.Sneak;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();
        avatar = GetComponent<CharacterAvatar>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Physics.gravity = new Vector3(0f, GameRules.Gravity, 0f);
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    bool CanControl => !InputLocked && (Status == null || !Status.Dead);

    void Update()
    {
        float v = 0f, h = 0f;
        bool runHeld = false, sneakHeld = false;
        if (CanControl && !Immobilized)
        {
            // Z/W accepté quel que soit le clavier (AZERTY/QWERTY) ; gauche = la touche Q (voir KeyLayout), A sert à aboyer.
            if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
            if (Input.GetKey(KeyLayout.Left) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
            runHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            // C en secours : certains navigateurs réservent Ctrl + touche.
            sneakHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.C);
        }

        // Course et marche discrète sont exclusives : la première touche enfoncée fait foi.
        if (gait == Gait.Run && !runHeld) gait = Gait.Normal;
        if (gait == Gait.Sneak && !sneakHeld) gait = Gait.Normal;
        if (gait == Gait.Normal)
        {
            if (runHeld) gait = Gait.Run;
            else if (sneakHeld) gait = Gait.Sneak;
        }

        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        inputDir = Vector3.ClampMagnitude(forward * v + right * h, 1f);

        grounded = IsGrounded();
        if (CanControl && !Immobilized && Input.GetKeyDown(KeyCode.Space) && grounded && airborneLock <= 0f)
        {
            jumpRequested = true;
            // Juste après l'impulsion, le sol est encore détecté : on reste en état "saut".
            airborneLock = 0.2f;
        }
        airborneLock -= Time.deltaTime;

        if (avatar != null && (Status == null || !Status.Dead))
            avatar.SetState(ComputeAnimState(grounded && airborneLock <= 0f));
    }

    float CurrentSpeed()
    {
        int items = Status != null ? Status.ItemCount : 0;
        switch (gait)
        {
            case Gait.Run: return moveSpeed * GameRules.RunMultiplier(items) * SpeedFactor;
            case Gait.Sneak: return moveSpeed * GameRules.SneakMultiplier * SpeedFactor;
            default: return moveSpeed * SpeedFactor;
        }
    }

    void FixedUpdate()
    {
        if (rb.isKinematic) return;
        Vector3 move = inputDir * CurrentSpeed();

        // En marche discrète, impossible de tomber d'une surface en hauteur.
        if (gait == Gait.Sneak && grounded && move.sqrMagnitude > 0f)
        {
            if (!HasGroundAhead(new Vector3(move.x, 0f, 0f))) move.x = 0f;
            if (!HasGroundAhead(new Vector3(0f, 0f, move.z))) move.z = 0f;
        }

        if (knockTimer > 0f)
        {
            knockTimer -= Time.fixedDeltaTime;
            move = knockVelocity * Mathf.Clamp01(knockTimer / KnockDuration);
        }

        Vector3 vel = rb.velocity;
        vel.x = move.x;
        vel.z = move.z;
        if (jumpRequested)
        {
            vel.y = jumpForce;
            jumpRequested = false;
        }
        else if (grounded && airborneLock <= 0f && groundNormal.y > 0.5f && groundNormal.y < 0.995f && vel.y <= 0.5f)
        {
            // Rampes et escaliers : on suit la pente sans glisser à l'arrêt.
            vel = Vector3.ProjectOnPlane(move, groundNormal);
        }
        rb.velocity = vel;

        // Le personnage s'oriente dans la direction du mouvement.
        if (inputDir.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(inputDir), 15f * Time.fixedDeltaTime));
    }

    AnimState ComputeAnimState(bool onGround)
    {
        if (!onGround) return AnimState.Jump;
        if (inputDir.sqrMagnitude < 0.01f) return AnimState.Idle;
        switch (gait)
        {
            case Gait.Run: return AnimState.Run;
            case Gait.Sneak: return AnimState.Sneak;
            default: return AnimState.Walk;
        }
    }

    float HalfHeight => col.height * 0.5f;

    // Sphère sous le personnage : les murs ne comptent pas comme sol (pas de saut le long des murs).
    bool IsGrounded()
    {
        float r = col.radius * 0.85f;
        bool hit = Physics.SphereCast(transform.position, r, Vector3.down, out RaycastHit info, HalfHeight - r + 0.08f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        groundNormal = hit ? info.normal : Vector3.up;
        return hit;
    }

    bool HasGroundAhead(Vector3 move)
    {
        Vector3 probe = rb.position + move.normalized * (col.radius + 0.15f);
        return Physics.Raycast(probe, Vector3.down, HalfHeight + 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    // Téléporte le personnage (apparition, réanimation).
    public void Teleport(Vector3 position, float yaw)
    {
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        rb.position = position;
        rb.rotation = transform.rotation;
        if (!rb.isKinematic) rb.velocity = Vector3.zero;
    }

    // Repousse le personnage dans la direction 'away' (horizontale), avec un petit bond.
    public void Knockback(Vector3 away, float speed)
    {
        if (rb.isKinematic) return;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f) away = -transform.forward;
        knockVelocity = away.normalized * speed;
        knockTimer = KnockDuration;
        rb.velocity = knockVelocity + Vector3.up * 4f;
        airborneLock = 0.25f;
    }

    public void SetFrozen(bool frozen)
    {
        rb.isKinematic = frozen;
        col.enabled = !frozen;
        if (!frozen) rb.velocity = Vector3.zero;
    }
}
