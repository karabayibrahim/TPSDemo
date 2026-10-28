using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public class CityThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float pivotHeight = 1.55f;
    [Min(0.5f)] public float distance = 4f;
    public Vector2 distanceLimits = new Vector2(1.5f, 6.5f);
    public Vector2 pitchLimits = new Vector2(-25f, 70f);
    public float mouseSensitivity = 0.12f;
    public float gamepadLookSpeed = 135f;
    public float followSmoothTime = 0.06f;
    public float collisionRadius = 0.22f;
    public LayerMask collisionLayers = ~0;
    public bool lockCursor = true;
    private float yaw, pitch = 17f, currentDistance, distanceVelocity;
    private Vector3 pivot, pivotVelocity;
    private bool initialized;
    private readonly RaycastHit[] hits = new RaycastHit[24];

    private void OnEnable()
    {
        if (Application.isPlaying && lockCursor) CaptureCursor();
    }

    private void LateUpdate()
    {
        if (target == null) return;
        if (!initialized) ResetView();
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame && lockCursor) CaptureCursor();
        if (Cursor.lockState == CursorLockMode.Locked || (mouse != null && mouse.rightButton.isPressed))
        {
            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                yaw += look.x; pitch -= look.y;
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.0025f, distanceLimits.x, distanceLimits.y);
            }
        }
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 look = pad.rightStick.ReadValue() * (gamepadLookSpeed * Time.deltaTime);
            yaw += look.x; pitch -= look.y;
        }
        pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
        UpdateView(Time.deltaTime);
    }

    public void ResetView()
    {
        if (target == null) return;
        yaw = target.eulerAngles.y; pitch = 17f;
        pivot = target.position + Vector3.up * pivotHeight;
        currentDistance = distance; initialized = true;
        UpdateView(0.02f);
    }

    private void UpdateView(float deltaTime)
    {
        pivot = Vector3.SmoothDamp(pivot, target.position + Vector3.up * pivotHeight, ref pivotVelocity, followSmoothTime, Mathf.Infinity, Mathf.Max(deltaTime, 0.001f));
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 direction = rotation * Vector3.back;
        float permitted = distance;
        int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, hits, distance, collisionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == null || hits[i].transform.IsChildOf(target)) continue;
            permitted = Mathf.Min(permitted, Mathf.Max(0.35f, hits[i].distance - 0.08f));
        }
        // Pull inward immediately; only the return to the desired distance is damped.
        currentDistance = permitted < currentDistance ? permitted :
            Mathf.SmoothDamp(currentDistance, permitted, ref distanceVelocity, 0.14f, Mathf.Infinity, Mathf.Max(deltaTime, 0.001f));
        transform.SetPositionAndRotation(pivot + direction * currentDistance, rotation);
    }

    private static void CaptureCursor() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    private void OnDisable() { if (Application.isPlaying) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } initialized = false; }
}
