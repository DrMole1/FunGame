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

    public static bool Looking => ControlsEnabled && Cursor.lockState == CursorLockMode.Locked;
    // Vue à la première personne du personnage local (le PlayerController s'oriente alors selon le regard).
    public static bool FirstPerson { get; private set; }
    // Obnubilé par La Cloche : la caméra est forcée de regarder ce point.
    public static Vector3? ForcedLook;
    // Mort du joueur local : la caméra quitte la tête et s'élève lentement au-dessus du corps (secondes écoulées, -1 : inactif).
    public static float DeathView = -1f;
    public const float DeathViewDuration = 3.5f;
    public static float Yaw { get; private set; }

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
            if (Cursor.lockState != CursorLockMode.None) LockCursor(false);
            return;
        }
        // Échap ouvre le menu pause, et le referme (GDD 6.2 : "Echap pour reprendre le jeu").
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(Cursor.lockState != CursorLockMode.Locked);
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

    void LateUpdate()
    {
        if (target == null) return;

        // Première personne : seulement sur son propre personnage (pas en spectateur), hors du lobby.
        bool fp = firstPerson && ControlsEnabled && target.GetComponent<PlayerController>() != null;
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
        // En marche discrète, les yeux descendent en douceur avec la tête.
        var controller = target.GetComponent<PlayerController>();
        bool crouched = controller != null && controller.Crouched;
        crouchBlend = Mathf.MoveTowards(crouchBlend, crouched ? 1f : 0f, Time.deltaTime / 0.25f);
        if (fp)
        {
            float eye = eyeHeight - GameRules.SneakEyeDrop * Mathf.SmoothStep(0f, 1f, crouchBlend);
            transform.SetPositionAndRotation(target.position + Vector3.up * eye, rot);
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
