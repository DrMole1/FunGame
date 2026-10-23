using UnityEngine;
using UnityEngine.EventSystems;

// Caméra à la troisième personne qui tourne sans limite autour de la cible avec la souris.
// Molette : distance entre 2 et 5 unités. Elle ne passe jamais sous le personnage ni à travers les murs.
// Zoomer encore à 2 unités passe à la première personne (yeux du personnage, corps masqué) ; dézoomer en revient.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 3.5f;
    public float height = 1f;
    public float mouseSensitivity = 3f;
    public float minPitch = -15f;
    public float maxPitch = 75f;
    // Première personne : hauteur des yeux au-dessus du centre de la capsule, et débattement vertical du regard.
    public float eyeHeight = 0.62f;
    public float firstPersonPitch = 80f;

    // Désactivé pendant le lobby : la caméra tourne lentement et la souris reste libre.
    public static bool ControlsEnabled = true;
    // Bloque le verrouillage de la souris (Terminal ouvert).
    public static bool CursorLockBlocked;
    // Image où la souris vient d'être verrouillée : ce clic ne doit pas aussi déclencher d'action.
    public static int LockFrame = -1;
    // Sortie du Terminal ou d'un Post-It par Échap : retour au jeu sans menu pause (le navigateur peut refuser de
    // reverrouiller la souris tout de suite ; un clic dans le jeu le fera).
    public static bool ResumePending;
    static int resumeFrame = -1;

    public static void ResumeGame()
    {
        ResumePending = true;
        resumeFrame = Time.frameCount;
        LockCursor(true);
    }

    public static bool Looking => ControlsEnabled && Cursor.lockState == CursorLockMode.Locked;
    // Vue à la première personne du personnage local (le PlayerController s'oriente alors selon le regard).
    public static bool FirstPerson { get; private set; }
    // Obnubilé par La Cloche : la caméra est forcée de regarder ce point.
    public static Vector3? ForcedLook;
    // Mort du joueur local : la caméra quitte la tête et s'élève lentement au-dessus du corps (secondes écoulées, -1 : inactif).
    public static float DeathView = -1f;
    public const float DeathViewDuration = 3.5f;
    public static float Yaw { get; private set; }
    // Dans le Canon (V0.4.6) : vue à la première personne depuis ce point, légèrement zoomée.
    public static Vector3? CannonEye;
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
    const float CannonZoom = 0.8f;
    float baseFov = -1f;

    // Pulsations cardiaques (GDD 8, Note 02, V0.4.8) : 0 = calme, 1 = Cauchemar tout proche. Fixé à chaque image par la
    // session ; la caméra bat au rythme du cœur (d'autant plus vite que l'entité est proche), avec un léger battement
    // sonore, et le balancement de tête à la première personne se fait plus insistant. L'effet monte vite et retombe
    // lentement.
    public static float Dread;
    static float dread, heartPhase, heartFov;
    const float DreadRise = 3f, DreadFall = 0.35f;
    const float HeartRateCalm = 60f, HeartRatePanic = 120f;   // battements par minute
    const float HeartFovPulse = 2.6f;                          // degrés de champ de vision au battement
    const float HeartSoundAt = 0.12f;                          // position du son dans le cycle

    // Enveloppe d'un battement « poum-poum » (0 à 1) selon la position dans le cycle.
    static float HeartEnvelope(float phase)
    {
        float lub = Mathf.Exp(-Mathf.Pow((phase - 0.14f) / 0.065f, 2f));
        float dub = 0.6f * Mathf.Exp(-Mathf.Pow((phase - 0.3f) / 0.075f, 2f));
        return lub + dub;
    }

    void UpdateHeart()
    {
        float goal = Mathf.Clamp01(Dread);
        dread = Mathf.MoveTowards(dread, goal, Time.deltaTime * (goal > dread ? DreadRise : DreadFall));
        if (dread <= 0.001f)
        {
            // Au calme : le premier battement partira dès que la tension revient.
            heartPhase = 1f;
            return;
        }
        if (heartPhase >= 1f) heartPhase -= 1f;
        float before = heartPhase;
        heartPhase += Time.deltaTime * Mathf.Lerp(HeartRateCalm, HeartRatePanic, dread) / 60f;
        // Le « poum » sonne au sommet du premier gonflement de l'image.
        if (before < HeartSoundAt && heartPhase >= HeartSoundAt) GameAudio.Play2D("heart_thump", 0.26f + 0.4f * dread);
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
    }

    void Update()
    {
        // Échap libère la souris, un clic la reverrouille.
        if (!ControlsEnabled || CursorLockBlocked)
        {
            // (Juste après la fermeture du Terminal par Échap, le blocage n'est pas encore levé : on garde le verrouillage.)
            if (Cursor.lockState != CursorLockMode.None && Time.frameCount - resumeFrame > 1) LockCursor(false);
            return;
        }
        // Échap ouvre le menu pause, et le referme (GDD 6.2 : "Echap pour reprendre le jeu").
        if (Cursor.lockState == CursorLockMode.Locked) ResumePending = false;
        if (Input.GetKeyDown(KeyCode.Escape) && Time.frameCount != resumeFrame)
        {
            // Souris restée libre après le Terminal : Échap affiche le menu pause.
            if (ResumePending) ResumePending = false;
            else LockCursor(Cursor.lockState != CursorLockMode.Locked);
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !IsPointerOverUI())
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

    // Zoom dans le Canon et secousses (tir, impact).
    void UpdateLens()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return;
        // Le battement de l'image précédente est retiré avant le lissage, puis celui de cette image ajouté (non lissé).
        cam.fieldOfView += heartFov;
        if (baseFov < 0f) baseFov = cam.fieldOfView;
        float wanted = CannonEye.HasValue ? baseFov * CannonZoom : baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wanted + shake * 12f, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (zoomPunch >= 0f)
        {
            zoomPunch += Time.deltaTime;
            float k = Mathf.Clamp01(zoomPunch / ZoomPunchDuration);
            // Immédiat (pas de lissage) : zoom avant brutal, puis oscillations qui s'amortissent.
            cam.fieldOfView = wanted * (1f - 0.45f * Mathf.Cos(k * Mathf.PI * 5f) * (1f - k));
            if (k >= 1f) zoomPunch = -1f;
        }
        UpdateHeart();
        heartFov = dread > 0.001f && ControlsEnabled && DeathView < 0f ? HeartEnvelope(Mathf.Repeat(heartPhase, 1f)) * HeartFovPulse * dread : 0f;
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

        // Première personne : seulement sur son propre personnage (pas en spectateur), hors du lobby. Forcée dans le Canon.
        bool fp = (firstPerson || CannonEye.HasValue) && ControlsEnabled && target.GetComponent<PlayerController>() != null;
        if (!ControlsEnabled)
            yaw += 6f * Time.deltaTime;
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
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }
        pitch = fp ? Mathf.Clamp(pitch, -firstPersonPitch, firstPersonPitch) : Mathf.Clamp(pitch, minPitch, maxPitch);
        yaw = Mathf.Repeat(yaw, 360f);
        Yaw = yaw;
        FirstPerson = fp;
        HideTarget(fp ? target : null);

        if (DeathView >= 0f)
        {
            HideTarget(null);
            FirstPerson = false;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(DeathView / DeathViewDuration));
            Vector3 corpse = target.position + Vector3.down * 0.7f;
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

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        if (CannonEye.HasValue && fp)
        {
            transform.SetPositionAndRotation(CannonEye.Value, rot);
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
            Vector3 bob = HeadBob(controller, rot, out float roll);
            transform.SetPositionAndRotation(target.position + Vector3.up * eye + bob, rot * Quaternion.Euler(0f, 0f, roll));
            return;
        }
        Vector3 focus = target.position + Vector3.up * height;
        float wanted = ControlsEnabled ? distance : 5f;

        // Les murs rapprochent la caméra au lieu d'être traversés.
        float d = wanted;
        if (Physics.SphereCast(focus, 0.2f, -(rot * Vector3.forward), out RaycastHit hit, wanted,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            d = Mathf.Max(0.3f, hit.distance);
        currentDistance = d < currentDistance ? d : Mathf.Lerp(currentDistance, d, 1f - Mathf.Exp(-8f * Time.deltaTime));

        Vector3 pos = focus - rot * Vector3.forward * currentDistance;
        // Jamais sous le personnage (le pivot est au centre de la capsule).
        pos.y = Mathf.Max(pos.y, target.position.y);
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(focus - pos);
    }

    // Première personne : la tête oscille légèrement au rythme des pas (vertical, latéral et un soupçon de roulis),
    // d'autant plus que le personnage va vite ; l'effet s'estompe à l'arrêt et en l'air.
    float bobPhase, bobWeight;

    Vector3 HeadBob(PlayerController controller, Quaternion rot, out float roll)
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
        roll = sway * 0.35f * bobWeight * tension;
        Vector3 right = rot * Vector3.right;
        right.y = 0f;
        return (Vector3.up * (Mathf.Abs(step) - 0.5f) * 0.045f + right.normalized * sway * 0.02f) * bobWeight * tension;
    }

    public float CurrentDistance => FirstPerson ? 0f : currentDistance;

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
