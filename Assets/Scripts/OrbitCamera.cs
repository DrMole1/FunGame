using UnityEngine;
using UnityEngine.EventSystems;

// Caméra à la troisième personne qui tourne sans limite autour de la cible avec la souris.
// Molette : distance entre 2 et 5 unités. Elle ne passe jamais sous les pieds du personnage ni à travers les murs.
// Zoomer encore à 2 unités passe à la première personne (yeux du personnage, corps masqué) ; dézoomer en revient.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 3.5f;
    public float height = 1f;
    public float mouseSensitivity = 3f;
    public float minPitch = -45f;
    public float maxPitch = 75f;
    // Regard vers le haut : la caméra peut descendre jusqu'à cette distance sous le centre de la capsule (le sol l'arrête avant).
    const float MaxDropBelowCenter = 0.7f;
    // Première personne : hauteur des yeux au-dessus du centre de la capsule, et débattement vertical du regard.
    public float eyeHeight = 0.62f;
    public float firstPersonPitch = 80f;

    // Désactivé pendant le lobby : la caméra tourne lentement et la souris reste libre.
    public static bool ControlsEnabled = true;
    // Note 02 du chapitre 6.1 (V0.7.10) : dans le lobby, la caméra part face au personnage (son visage, le Terminal en arrière-plan)
    // puis tourne comme avant ; au changement de skin, elle revient rapidement à cette position de départ.
    static float lobbyHomeYaw;
    static bool lobbySnap, lobbyReturn;
    const float LobbySpinSpeed = 6f, LobbyReturnSpeed = 360f;
    // Cadrage du lobby : inclinaison (celle du lancement du jeu) et distance de la caméra.
    const float LobbyPitch = 20f, LobbyDistance = 5f;

    public static void SetLobbyHome(float yaw)
    {
        lobbyHomeYaw = yaw;
        lobbySnap = true;
        lobbyReturn = false;
        // Plus rien de la partie ne pilote la caméra (mort, Canon, toboggan, niche, Cloche, secousses, battements du cœur).
        DeathView = -1f;
        DeathSpot = null;
        CannonEye = null;
        SlideLook = null;
        KennelEye = null;
        FlightLook = null;
        FixedView = null;
        ForcedLook = null;
        MetronomeTilt = 0f;
        Dread = 0f;
        Dying = false;
        shake = 0f;
        zoomPunch = bellStrike = -1f;
    }

    public static void ReturnToLobbyHome() => lobbyReturn = true;
    // Bloque le verrouillage de la souris (Terminal ouvert).
    public static bool CursorLockBlocked;
    // Image où la souris vient d'être verrouillée : ce clic ne doit pas aussi déclencher d'action.
    public static int LockFrame = -1;
    // Sortie du Terminal ou d'un Post-It par Échap : retour au jeu sans menu pause (le navigateur peut refuser de
    // reverrouiller la souris tout de suite ; un clic dans le jeu le fera).
    public static bool ResumePending;
    static int resumeFrame = -1;
    // Délai laissé au navigateur pour capturer la souris.
    const float LockGrace = 0.4f;
    static float lockMissing;
    // Après la sortie d'une fenêtre de saisie par Échap, cette même touche ne doit pas aussi ouvrir le menu pause : le navigateur peut
    // perdre la souris juste après l'avoir capturée (touche encore enfoncée).
    const float ResumeGuard = 1f;
    static float resumeGuardUntil;

    public static void ResumeGame()
    {
        ResumePending = true;
        resumeFrame = Time.frameCount;
        resumeGuardUntil = Time.unscaledTime + ResumeGuard;
        LockCursor(true);
    }

    public static bool Looking => ControlsEnabled && Cursor.lockState == CursorLockMode.Locked;
    // Vue à la première personne du personnage local (le PlayerController s'oriente alors selon le regard).
    public static bool FirstPerson { get; private set; }
    // Obnubilé par La Cloche : la caméra est forcée de regarder ce point.
    public static Vector3? ForcedLook;
    // Mort du joueur local : la caméra quitte la tête et s'élève lentement au-dessus du corps (secondes écoulées, -1 : inactif).
    public static float DeathView = -1f;
    // Tué par La Tête de Cloche : le corps est traîné, la caméra reste sur l'endroit de la mort.
    public static Vector3? DeathSpot;
    public const float DeathViewDuration = 3.5f;
    public static float Yaw { get; private set; }
    // Dans le Canon (V0.4.6) : vue à la première personne depuis ce point, légèrement zoomée.
    public static Vector3? CannonEye;
    // Dans un toboggan (V0.6.6) : le regard suit le tube.
    public static Vector3? SlideLook;
    // Dans sa niche (V0.6) : la caméra est à l'intérieur, tournée vers l'extérieur (cap KennelYaw) ; on peut la bouger un peu.
    public static Vector3? KennelEye;
    // Machine à pince (V0.6.1) : la caméra rejoint ce point de vue et s'y bloque jusqu'à la fin de la partie.
    public static Pose? FixedView;
    public static float KennelYaw;
    const float KennelYawRange = 40f, KennelPitchRange = 25f;
    // En boulet de canon : la caméra suit le boulet en fixant son point d'arrivée.
    public static Vector3? FlightLook;
    static float shake;
    // Secousse de la caméra (tir du Canon, impact), qui s'amortit d'elle-même.
    public static void Shake(float amount) => shake = Mathf.Max(shake, amount);
    // Vive perturbation en zoom (échange de position avec Le Fou, V0.4.7) : le champ de vision se contracte et se dilate
    // par à-coups pendant un peu moins d'une seconde.
    static float zoomPunch = -1f;
    const float ZoomPunchDuration = 0.8f;
    public static void ZoomPunch()
    {
        zoomPunch = 0f;
        Shake(0.5f);
    }
    // Obnubilation (chapitre 3.4, V0.6.9) : au son de cloche, un seul zoom avant brusque suivi du dézoom, en 0,8 seconde.
    static float bellStrike = -1f;
    const float BellStrikeDuration = 0.8f, BellStrikeAttack = 0.15f, BellStrikeZoom = 0.42f;
    public static void BellStrike() => bellStrike = 0f;
    const float CannonZoom = 0.8f;
    float baseFov = -1f;

    // Pulsations cardiaques (GDD 8, Note 02, V0.4.8) : 0 = calme, 1 = Cauchemar tout proche. Fixé à chaque image par la
    // session ; la caméra bat au rythme du cœur (d'autant plus vite que l'entité est proche), avec un battement sonore
    // bien audible, et le balancement de tête à la première personne se fait plus insistant. L'effet monte vite et retombe
    // en 1 seconde environ.
    public static float Dread;
    // Personnage mort : le battement s'estompe en 1,4 s environ (avant le silence de la mort).
    public static bool Dying;
    static float dread, heartPhase, heartFov, beatAt = -10f;
    // Tension lissée (0 à 1) et battement en cours (0 à 1,6), pour l'assombrissement de l'écran (GameHud).
    public static float HeartTension => dread;
    public static float HeartBeat => dread > 0.001f ? HeartEnvelope(Time.time - beatAt) : 0f;
    const float DreadRise = 3f, DreadFall = 1f, DreadFallDying = 0.7f;
    const float HeartRateCalm = 60f, HeartRatePanic = 120f;   // battements par minute
    const float HeartFovPulse = 2.6f;                          // degrés de champ de vision au battement
    // Son Battement_Coeur (GDD 12, V0.4.11) : un « poum-poum » par pulsation, dont les deux coups tombent 0,09 s et
    // 0,34 s après le début du son ; la caméra bat sur ces deux coups. Volume : de plus en plus fort à l'approche d'un
    // Cauchemar, et nul quand la tension retombe à zéro (le retour au calme se fait en fondu).
    const float HeartLub = 0.09f, HeartDub = 0.34f;
    const float HeartVolumeMax = 1f;

    // Enveloppe d'un battement « poum-poum » (0 à 1), 't' secondes après le début du son.
    static float HeartEnvelope(float t)
    {
        float lub = Mathf.Exp(-Mathf.Pow((t - HeartLub) / 0.06f, 2f));
        float dub = 0.6f * Mathf.Exp(-Mathf.Pow((t - HeartDub) / 0.07f, 2f));
        return lub + dub;
    }

    void UpdateHeart()
    {
        float goal = Mathf.Clamp01(Dread);
        dread = Mathf.MoveTowards(dread, goal, Time.deltaTime * (goal > dread ? DreadRise : Dying ? DreadFallDying : DreadFall));
        if (dread <= 0.001f)
        {
            // Au calme : le premier battement partira dès que la tension revient.
            heartPhase = 1f;
            return;
        }
        // Une pulsation : le son part, et la caméra battra sur ses deux coups.
        if (heartPhase >= 1f)
        {
            heartPhase -= 1f;
            beatAt = Time.time;
            GameAudio.Play2D("heart_thump", HeartVolumeMax * Mathf.Sqrt(dread));
        }
        heartPhase += Time.deltaTime * Mathf.Lerp(HeartRateCalm, HeartRatePanic, dread) / 60f;
    }
    Camera cam;

    float yaw;
    float pitch = 20f;
    float currentDistance;
    bool firstPerson;
    float crouchBlend;
    Transform hiddenTarget;
    Renderer[] hidden = new Renderer[0];
    float hiddenRefresh;

    void Start()
    {
        yaw = transform.eulerAngles.y;
        currentDistance = distance;
        if (GetComponent<FilmGrain>() == null) gameObject.AddComponent<FilmGrain>();
        // V0.6.13 : effets post-process du chapitre 12.
        if (GetComponent<PostFxCamera>() == null) gameObject.AddComponent<PostFxCamera>();
    }

    void Update()
    {
        // Échap libère la souris, un clic la reverrouille.
        // Fenêtre "Modifier Contrôles" ouverte (ou fermée par Échap à cette image) : le menu pause reste affiché.
        if (!ControlsEnabled || CursorLockBlocked || ControlsUI.IsOpen || ControlsUI.ClosedFrame == Time.frameCount)
        {
            // (Juste après la fermeture du Terminal par Échap, le blocage n'est pas encore levé : on garde le verrouillage.)
            if (Cursor.lockState != CursorLockMode.None && Time.frameCount - resumeFrame > 1) LockCursor(false);
            return;
        }
        // Le navigateur peut refuser de capturer la souris juste après Échap, alors qu'Unity la croit verrouillée : au bout d'un
        // instant, on la libère vraiment, pour que le prochain clic la reverrouille.
        if (Cursor.lockState == CursorLockMode.Locked && !PeerNet.PointerLocked)
        {
            lockMissing += Time.unscaledDeltaTime;
            if (lockMissing > LockGrace) LockCursor(false);
        }
        else lockMissing = 0f;
        // Échap ouvre le menu pause, et le referme (GDD 6.2 : "Echap pour reprendre le jeu").
        bool guarded = Time.unscaledTime < resumeGuardUntil;
        if (PeerNet.PointerLocked && !guarded) ResumePending = false;
        if (Input.GetKeyDown(KeyCode.Escape) && Time.frameCount != resumeFrame && !guarded)
        {
            // Souris restée libre après le Terminal : Échap affiche le menu pause.
            if (ResumePending) ResumePending = false;
            else LockCursor(Cursor.lockState != CursorLockMode.Locked);
        }
        // Au retour d'une fenêtre de saisie, aucun menu n'est ouvert : un clic reprend la main, même au-dessus de l'inventaire.
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && (ResumePending || !IsPointerOverUI()))
        {
            LockCursor(true);
            LockFrame = Time.frameCount;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f && !firstPerson && distance <= GameRules.CameraMinDistance + 0.001f)
                firstPerson = true;
            else if (wheel < -0.01f && firstPerson)
            {
                firstPerson = false;
                distance = GameRules.CameraMinDistance;
                currentDistance = 0.3f;
            }
            else if (Mathf.Abs(wheel) > 0.01f && !firstPerson)
                distance = Mathf.Clamp(distance - wheel * 0.5f, GameRules.CameraMinDistance, GameRules.CameraMaxDistance);
        }
    }

    // Pogo tue le joueur (Note 04 du chapitre 4, environ 2 secondes) ou un Cauchemar le tue (Note 03 du chapitre 3.4,
    // environ 1,5 seconde) : la caméra se colle à son visage, droit dans les yeux, secouée frénétiquement, puis revient.
    const float JumpscareIn = 0.18f, JumpscareOut = 0.35f;
    static Transform jumpscareHead;
    static Vector3 jumpscareEyes, jumpscareDir;
    static float jumpscareStart = -10f, jumpscareDuration = 2f, jumpscareDistance = 0.34f;
    static bool jumpscareFacing;

    // facing : la caméra reste devant le visage (l'entité continue de bouger) ; sinon, face à face du côté de la caméra
    // au moment du coup (Pogo peut ensuite tourner la tête).
    // distance : de la caméra aux yeux (au-delà du plan de coupe proche, pour voir le visage d'une grande entité).
    public static void StartJumpscare(Transform head, Vector3 eyesLocal, float duration = 2f, bool facing = false, float distance = 0.34f)
    {
        jumpscareDistance = distance;
        jumpscareHead = head;
        jumpscareEyes = eyesLocal;
        jumpscareStart = Time.time;
        jumpscareDuration = duration;
        jumpscareFacing = facing;
        Camera cam = Camera.main;
        Vector3 eyes = head.TransformPoint(eyesLocal);
        jumpscareDir = cam != null ? Vector3.ProjectOnPlane(cam.transform.position - eyes, Vector3.up).normalized : head.forward;
        if (jumpscareDir.sqrMagnitude < 0.01f) jumpscareDir = head.forward;
    }

    public static bool JumpscareActive => jumpscareHead != null && Time.time - jumpscareStart < jumpscareDuration;

    void LateUpdate()
    {
        UpdateView();
        UpdateLens();
        if (!JumpscareActive) return;
        float t = Time.time - jumpscareStart;
        float w = Mathf.SmoothStep(0f, 1f, Mathf.Min(t / JumpscareIn, (jumpscareDuration - t) / JumpscareOut));
        Vector3 eyes = jumpscareHead.TransformPoint(jumpscareEyes);
        if (jumpscareFacing) jumpscareDir = Vector3.ProjectOnPlane(jumpscareHead.forward, Vector3.up).normalized;
        // Saccades : tremblement de position et de rotation, à haute fréquence.
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 31f, 0f) - 0.5f, Mathf.PerlinNoise(0f, t * 29f) - 0.5f, Mathf.PerlinNoise(t * 23f, t * 17f) - 0.5f);
        Vector3 pos = eyes + jumpscareDir * jumpscareDistance + jitter * 0.08f;
        Quaternion rot = Quaternion.LookRotation(eyes - pos) * Quaternion.Euler(jitter.y * 14f, jitter.x * 14f, jitter.z * 22f);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, w), Quaternion.Slerp(transform.rotation, rot, w));
    }

    // Zoom du lobby (survol des flèches du skin) : champ de vision réduit à 65 %, atteint en 1,2 s, quitté en 0,9 s
    // (V0.7.14 : dézoom ralenti, 0,3 s avant).
    float lobbyZoom;
    const float LobbyZoomFov = 0.65f, LobbyZoomIn = 1.2f, LobbyZoomOut = 0.9f;
    // V0.7.14 (Note 02) : position horizontale de la tête à l'écran une fois zoomé (part de la largeur, depuis la gauche).
    const float LobbyZoomHeadX = 0.7f;

    // Zoom dans le Canon et secousses (tir, impact).
    void UpdateLens()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        // Le battement de l'image précédente est retiré avant le lissage, puis celui de cette image ajouté (non lissé).
        cam.fieldOfView += heartFov;
        if (baseFov < 0f) baseFov = cam.fieldOfView;
        float wanted = CannonEye.HasValue ? baseFov * CannonZoom : baseFov;
        // Lobby : flèches du skin survolées, zoom progressif sur le personnage ; retour rapide une fois le survol fini.
        bool skinZoom = !ControlsEnabled && LobbySkinZoom.Active;
        lobbyZoom = Mathf.MoveTowards(lobbyZoom, skinZoom ? 1f : 0f, Time.deltaTime / (skinZoom ? LobbyZoomIn : LobbyZoomOut));
        wanted *= Mathf.Lerp(1f, LobbyZoomFov, Mathf.SmoothStep(0f, 1f, lobbyZoom));
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wanted + shake * 12f, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (zoomPunch >= 0f)
        {
            zoomPunch += Time.deltaTime;
            float k = Mathf.Clamp01(zoomPunch / ZoomPunchDuration);
            // Immédiat (pas de lissage) : zoom avant brutal, puis oscillations qui s'amortissent.
            cam.fieldOfView = wanted * (1f - 0.45f * Mathf.Cos(k * Mathf.PI * 5f) * (1f - k));
            if (k >= 1f) zoomPunch = -1f;
        }
        else if (bellStrike >= 0f)
        {
            bellStrike += Time.deltaTime;
            float k = Mathf.Clamp01(bellStrike / BellStrikeDuration);
            float strike = k < BellStrikeAttack ? k / BellStrikeAttack : 1f - Mathf.SmoothStep(0f, 1f, (k - BellStrikeAttack) / (1f - BellStrikeAttack));
            cam.fieldOfView = wanted * (1f - BellStrikeZoom * strike);
            if (k >= 1f) bellStrike = -1f;
        }
        UpdateHeart();
        heartFov = dread > 0.001f && ControlsEnabled && DeathView < 0f ? HeartEnvelope(Time.time - beatAt) * HeartFovPulse * dread : 0f;
        cam.fieldOfView -= heartFov;
        if (shake <= 0f) return;
        float t = Time.time * 40f;
        Vector3 j = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
        transform.position += j * shake * 0.25f;
        transform.rotation *= Quaternion.Euler(j.x * shake * 6f, j.y * shake * 6f, j.z * shake * 8f);
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
    }

    void UpdateView()
    {
        if (target == null) return;

        // V0.6.14 : dans un tunnel (TunnelZones), première personne obligatoire. À la troisième personne, la caméra s'avance
        // progressivement jusqu'aux yeux ; en sortant, elle recule jusqu'à la distance choisie par le joueur (inchangée).
        bool own = ControlsEnabled && target.GetComponent<PlayerController>() != null;
        bool inTunnel = own && DeathView < 0f && TunnelZones.Contains(target.position);
        tunnelBlend = Mathf.MoveTowards(tunnelBlend, inTunnel ? 1f : 0f, Time.deltaTime / TunnelBlendTime);
        // Première personne : seulement sur son propre personnage (pas en spectateur), hors du lobby. Forcée dans le Canon.
        bool fp = (firstPerson || CannonEye.HasValue || KennelEye.HasValue || tunnelBlend >= 1f) && own;
        if (!ControlsEnabled)
        {
            if (lobbySnap)
            {
                yaw = lobbyHomeYaw;
                // V0.7.14 : retour au lobby (mort, partie quittée) : même cadrage qu'au lancement du jeu. L'inclinaison et la
                // distance choisies à la souris et à la molette pendant la partie (ou la première personne, un tunnel) restaient.
                pitch = LobbyPitch;
                currentDistance = LobbyDistance;
                tunnelBlend = crouchBlend = lobbyZoom = 0f;
                lobbySnap = lobbyReturn = false;
            }
            else if (lobbyReturn)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, lobbyHomeYaw, LobbyReturnSpeed * Time.deltaTime);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, lobbyHomeYaw)) < 0.1f) lobbyReturn = false;
            }
            else yaw += LobbySpinSpeed * Time.deltaTime;
        }
        else if (ForcedLook.HasValue)
        {
            // La souris n'y peut rien : le regard est tiré vers La Cloche.
            Vector3 from = fp ? target.position + Vector3.up * eyeHeight : target.position + Vector3.up * height;
            Vector3 look = ForcedLook.Value - from;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, k);
            float wantedPitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            // À la troisième personne, la caméra se place derrière et un peu au-dessus du personnage.
            pitch = Mathf.Lerp(pitch, fp ? wantedPitch : Mathf.Max(wantedPitch + 12f, 8f), k);
        }
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            // Réglage du menu pause (MouseSensitivity).
            float sensitivity = mouseSensitivity * MouseSensitivity.Value;
            yaw += Input.GetAxis("Mouse X") * sensitivity;
            pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        }
        pitch = fp ? Mathf.Clamp(pitch, -firstPersonPitch, firstPersonPitch) : Mathf.Clamp(pitch, minPitch, maxPitch);
        if (KennelEye.HasValue)
        {
            yaw = KennelYaw + Mathf.Clamp(Mathf.DeltaAngle(KennelYaw, yaw), -KennelYawRange, KennelYawRange);
            pitch = Mathf.Clamp(pitch, -KennelPitchRange, KennelPitchRange);
        }
        yaw = Mathf.Repeat(yaw, 360f);
        Yaw = yaw;
        FirstPerson = fp;
        HideTarget(fp ? target : null);

        if (DeathView >= 0f)
        {
            HideTarget(null);
            FirstPerson = false;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(DeathView / DeathViewDuration));
            Vector3 corpse = (DeathSpot ?? target.position) + Vector3.down * 0.7f;
            // Spirale lente vers le haut, le regard rivé sur le corps.
            float angle = (yaw + k * 70f) * Mathf.Deg2Rad;
            // Jamais à travers le plafond ou les murs.
            float ceiling = Physics.Raycast(corpse + Vector3.up * 0.3f, Vector3.up, out RaycastHit up, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? up.distance : 6f;
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * Mathf.Lerp(1.1f, 1.9f, k)
                + Vector3.up * Mathf.Min(Mathf.Lerp(1.6f, 4.2f, k), ceiling);
            Vector3 eye = corpse + Vector3.up * 0.3f;
            if (Physics.SphereCast(eye, 0.15f, (corpse + offset - eye).normalized, out RaycastHit wall, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                offset = (corpse + offset - eye).normalized * Mathf.Max(0.3f, wall.distance - 0.1f) + Vector3.up * 0.3f;
            transform.position = corpse + offset;
            transform.rotation = Quaternion.LookRotation(corpse - transform.position, Vector3.up);
            return;
        }

        if (FixedView.HasValue)
        {
            HideTarget(null);
            FirstPerson = false;
            float follow = 1f - Mathf.Exp(-7f * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, FixedView.Value.position, follow),
                Quaternion.Slerp(transform.rotation, FixedView.Value.rotation, follow));
            return;
        }
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        if (CannonEye.HasValue && fp)
        {
            transform.SetPositionAndRotation(CannonEye.Value, SlideLook.HasValue ? Quaternion.LookRotation(SlideLook.Value - CannonEye.Value) : rot);
            return;
        }
        if (KennelEye.HasValue && fp)
        {
            transform.SetPositionAndRotation(KennelEye.Value, rot);
            return;
        }
        if (FlightLook.HasValue)
        {
            // Derrière et au-dessus du boulet, le regard rivé sur le point d'arrivée.
            Vector3 toGoal = FlightLook.Value - target.position;
            Vector3 flat = new Vector3(toGoal.x, 0f, toGoal.z);
            Vector3 back = flat.sqrMagnitude > 0.01f ? -flat.normalized : -(rot * Vector3.forward);
            Vector3 camPos = target.position + back * 3.2f + Vector3.up * 1.3f;
            float follow = 1f - Mathf.Exp(-12f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, camPos, follow);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(FlightLook.Value - transform.position), follow);
            return;
        }
        // En marche discrète, les yeux descendent en douceur avec la tête.
        var controller = target.GetComponent<PlayerController>();
        bool crouched = controller != null && controller.Crouched;
        crouchBlend = Mathf.MoveTowards(crouchBlend, crouched ? 1f : 0f, Time.deltaTime / 0.25f);
        if (fp)
        {
            float eye = eyeHeight - GameRules.SneakEyeDrop * Mathf.SmoothStep(0f, 1f, crouchBlend);
            Vector3 bob = HeadBob(controller, rot, out float roll, out float nod);
            transform.SetPositionAndRotation(target.position + Vector3.up * eye + bob, rot * Quaternion.Euler(nod, 0f, roll));
            return;
        }
        // Vers un tunnel (ou en en sortant) : le point visé descend vers les yeux et la distance fond vers zéro.
        float tunnelK = Mathf.SmoothStep(0f, 1f, tunnelBlend);
        Vector3 focus = target.position + Vector3.up * Mathf.Lerp(height, eyeHeight, tunnelK);
        float wanted = Mathf.Lerp(ControlsEnabled ? distance : LobbyDistance, 0.05f, tunnelK);
        // Tout près des yeux, le corps masquerait la vue.
        if (tunnelK > 0.7f) HideTarget(target);

        // Les murs rapprochent la caméra au lieu d'être traversés.
        float d = wanted;
        if (Physics.SphereCast(focus, 0.2f, -(rot * Vector3.forward), out RaycastHit hit, wanted,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            d = Mathf.Max(0.3f, hit.distance);
        currentDistance = d < currentDistance ? d : Mathf.Lerp(currentDistance, d, 1f - Mathf.Exp(-8f * Time.deltaTime));

        Vector3 pos = focus - rot * Vector3.forward * currentDistance;
        // Jamais sous les pieds du personnage (le pivot est au centre de la capsule).
        pos.y = Mathf.Max(pos.y, target.position.y - MaxDropBelowCenter);
        Quaternion camRotation = Quaternion.LookRotation(focus - pos);
        // V0.7.14 (Note 02) : zoom du lobby : la caméra descend à la hauteur de la tête du skin et la regarde de face, à
        // l'horizontale : la tête est au centre de l'écran.
        if (!ControlsEnabled && lobbyZoom > 0f)
        {
            var avatar = target.GetComponentInChildren<CharacterAvatar>();
            Vector3? headCenter = avatar != null ? avatar.HeadCenter : null;
            Vector3 head = headCenter ?? target.position + Vector3.up * eyeHeight;
            Vector3 flat = head - pos;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f)
            {
                float zk = Mathf.SmoothStep(0f, 1f, lobbyZoom);
                Vector3 zoomedPos = new Vector3(pos.x, head.y, pos.z);
                pos = Vector3.Lerp(pos, zoomedPos, zk);
                // La tête à LobbyZoomHeadX de la largeur de l'écran (un peu à droite) : la caméra pivote d'autant vers la gauche.
                var view = GetComponent<Camera>();
                float side = 0f;
                if (view != null)
                {
                    float halfH = Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) * view.aspect;
                    side = Mathf.Atan((LobbyZoomHeadX - 0.5f) * 2f * halfH) * Mathf.Rad2Deg;
                }
                Quaternion zoomedRot = Quaternion.Euler(0f, -side, 0f) * Quaternion.LookRotation(head - zoomedPos);
                // Au dézoom, Slerp vers la rotation habituelle : la caméra retrouve sa trajectoire.
                camRotation = Quaternion.Slerp(camRotation, zoomedRot, zk);
            }
        }
        // V0.7.4 (Note 02) : à la troisième personne aussi, un très léger balancement au rythme des pas (le même qu'à la première
        // personne, en bien plus discret).
        if (controller != null && tunnelK < 0.01f)
        {
            Vector3 bob = HeadBob(controller, rot, out float roll, out float nod);
            pos += bob * ThirdPersonBob;
            camRotation *= Quaternion.Euler(nod * ThirdPersonBob, 0f, roll * ThirdPersonBob);
        }
        transform.position = pos;
        transform.rotation = camRotation;
    }

    // V0.7.8 (chapitre 17, Note 02) : secousses accentuées (avant : 0,4), puis légèrement réduites (0,6 -> 0,5).
    const float ThirdPersonBob = 0.5f;

    // Première personne : la tête oscille légèrement au rythme des pas (vertical, latéral et un soupçon de roulis),
    // d'autant plus que le personnage va vite ; l'effet s'estompe à l'arrêt et en l'air.
    float bobPhase, bobWeight;

    // V0.7.8 (chapitre 17, Note 02) : secousses accentuées à la première et à la troisième personne : environ 1,7 fois plus
    // amples (hauteur, balancement latéral, roulis), plus un léger hochement de tête à chaque pas ('nod', en degrés).
    // V0.7.11 (Note 06) : secousses haut-bas légèrement réduites (hauteur 0,075 -> 0,062 ; hochement 0,55 -> 0,45).
    const float BobLift = 0.062f, BobSway = 0.034f, BobRoll = 0.6f, BobNod = 0.45f;
    // V0.7.11 (7.12) : la caméra penche la tête à droite et à gauche au rythme d'un Métronome regardé (degrés de roulis).
    public static float MetronomeTilt;

    Vector3 HeadBob(PlayerController controller, Quaternion rot, out float roll, out float nod)
    {
        var body = controller.GetComponent<Rigidbody>();
        Vector3 v = body != null ? body.velocity : Vector3.zero;
        float speed = new Vector2(v.x, v.z).magnitude;
        bool walking = controller.Grounded && speed > 0.5f;
        bobWeight = Mathf.MoveTowards(bobWeight, walking ? Mathf.Clamp01(speed / 6f) + 0.35f : 0f, Time.deltaTime * 3f);
        // Un pas toutes les ~0,45 s en marche, plus rapide en course.
        if (walking) bobPhase += Time.deltaTime * (5f + speed * 0.9f);
        float step = Mathf.Sin(bobPhase), sway = Mathf.Sin(bobPhase * 0.5f);
        // Pulsations cardiaques : les saccades des pas se font plus insistantes (jusqu'à 2,3 fois plus amples).
        float tension = 1f + 1.3f * dread;
        roll = sway * BobRoll * bobWeight * tension + MetronomeTilt / (FirstPerson ? 1f : ThirdPersonBob);
        // La tête plonge un peu à l'impact de chaque pas (au creux du mouvement vertical).
        nod = (0.5f - Mathf.Abs(step)) * BobNod * bobWeight * tension;
        Vector3 right = rot * Vector3.right;
        right.y = 0f;
        return (Vector3.up * (Mathf.Abs(step) - 0.5f) * BobLift + right.normalized * sway * BobSway) * bobWeight * tension;
    }

    public float CurrentDistance => FirstPerson ? 0f : currentDistance;

    // Passage progressif vers la première personne dans un tunnel (0 : hors tunnel, 1 : première personne).
    float tunnelBlend;
    const float TunnelBlendTime = 0.6f;

    // À la première personne, le corps du personnage est masqué (la caméra est dans sa tête).
    void HideTarget(Transform t)
    {
        if (t != hiddenTarget)
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden = new Renderer[0];
            hiddenTarget = t;
            hiddenRefresh = 0f;
        }
        if (t == null) return;
        // Le modèle change avec le skin : la liste est rafraîchie régulièrement.
        hiddenRefresh -= Time.deltaTime;
        if (hiddenRefresh <= 0f)
        {
            hiddenRefresh = 0.5f;
            hidden = t.GetComponentsInChildren<Renderer>(true);
        }
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = true;
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
