using UnityEngine;

// Déplacement du cube en ZQSD (relatif à la caméra) + saut avec Espace.
[RequireComponent(typeof(Rigidbody))]
public class CubeController : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float jumpForce = 6f;
    public Transform cameraTransform;

    Rigidbody rb;
    Collider col;
    Vector3 inputDir;
    bool jumpRequested;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
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

        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        inputDir = Vector3.ClampMagnitude(forward * v + right * h, 1f);

        if (Input.GetKeyDown(KeyCode.Space) && IsGrounded())
            jumpRequested = true;
    }

    void FixedUpdate()
    {
        Vector3 vel = rb.velocity;
        vel.x = inputDir.x * moveSpeed;
        vel.z = inputDir.z * moveSpeed;
        if (jumpRequested)
        {
            vel.y = jumpForce;
            jumpRequested = false;
        }
        rb.velocity = vel;

        // Le cube s'oriente dans la direction du mouvement.
        if (inputDir.sqrMagnitude > 0.01f)
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(inputDir), 15f * Time.fixedDeltaTime));
    }

    bool IsGrounded()
    {
        float halfHeight = col.bounds.extents.y;
        return Physics.Raycast(transform.position, Vector3.down, halfHeight + 0.1f, ~0, QueryTriggerInteraction.Ignore);
    }
}
