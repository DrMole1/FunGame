using UnityEngine;

// Déplacement du joueur en ZQSD (relatif à la caméra), course avec Shift et saut avec Espace.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float runSpeed = 8f;
    public float jumpForce = 6f;
    public Transform cameraTransform;

    Rigidbody rb;
    Collider col;
    CharacterAvatar avatar;
    Vector3 inputDir;
    bool running;
    bool jumpRequested;
    float airborneLock;

    public AnimState CurrentAnimState => avatar != null ? avatar.State : AnimState.Idle;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        avatar = GetComponent<CharacterAvatar>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        // Z/W et Q/A acceptés pour fonctionner quel que soit le mapping du clavier (AZERTY/QWERTY).
        float v = 0f, h = 0f;
        if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
        running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        inputDir = Vector3.ClampMagnitude(forward * v + right * h, 1f);

        bool grounded = IsGrounded();
        if (Input.GetKeyDown(KeyCode.Space) && grounded)
        {
            jumpRequested = true;
            // Juste après l'impulsion, le rayon touche encore le sol : on reste en état "saut".
            airborneLock = 0.2f;
        }
        airborneLock -= Time.deltaTime;

        if (avatar != null) avatar.SetState(ComputeAnimState(grounded && airborneLock <= 0f));
    }

    void FixedUpdate()
    {
        float speed = running ? runSpeed : moveSpeed;
        Vector3 vel = rb.velocity;
        vel.x = inputDir.x * speed;
        vel.z = inputDir.z * speed;
        if (jumpRequested)
        {
            vel.y = jumpForce;
            jumpRequested = false;
        }
        rb.velocity = vel;

        // Le personnage s'oriente dans la direction du mouvement.
        if (inputDir.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(inputDir), 15f * Time.fixedDeltaTime));
    }

    AnimState ComputeAnimState(bool grounded)
    {
        if (!grounded) return AnimState.Jump;
        if (inputDir.sqrMagnitude < 0.01f) return AnimState.Idle;
        return running ? AnimState.Run : AnimState.Walk;
    }

    bool IsGrounded()
    {
        float halfHeight = col.bounds.extents.y;
        return Physics.Raycast(transform.position, Vector3.down, halfHeight + 0.1f, ~0, QueryTriggerInteraction.Ignore);
    }
}
