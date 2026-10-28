using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class CityThirdPersonController : MonoBehaviour
{
    [Header("Movement")]
    [Min(0.1f)] public float walkSpeed = 2f;
    [Min(0.1f)] public float runSpeed = 5.5f;
    [Min(0.1f)] public float acceleration = 12f;
    [Min(0.01f)] public float turnSmoothTime = 0.12f;
    [Header("Ground and jump")]
    public LayerMask groundLayers = ~0;
    [Min(0f)] public float jumpHeight = 1.15f;
    public float gravity = -22f;
    [Min(0f)] public float coyoteTime = 0.12f;
    [Min(0f)] public float jumpBufferTime = 0.12f;
    [Header("References")]
    public Transform view;
    public Animator animator;
    [Tooltip("Disable for AI characters so keyboard/gamepad controls only the player.")]
    public bool readPlayerInput = true;

    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Vector3 Velocity { get; private set; }
    public float VerticalSpeed => verticalSpeed;
    public CharacterController Capsule => capsule;

    private CharacterController capsule;
    private Vector3 horizontalVelocity;
    private float verticalSpeed, turnVelocity, coyoteRemaining, bufferedJump, fallingTime;
    private bool initialized;
    private bool inputOverride;
    private Vector2 overrideMove;
    private bool overrideSprint, overrideJump;
    private bool worldSpaceOverride;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int MotionId = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int JumpId = Animator.StringToHash("Jump");
    private static readonly int FallId = Animator.StringToHash("FreeFall");

    private void Awake() { Initialize(); }

    private void Initialize()
    {
        if (initialized) return;
        capsule = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (view == null && Camera.main != null) view = Camera.main.transform;
        initialized = true;
    }

    private void Update()
    {
        Vector2 move = Vector2.zero; bool sprint = false, jump = false;
        if (inputOverride) { move = overrideMove; sprint = overrideSprint; jump = overrideJump; overrideJump = false; }
        else if (readPlayerInput)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move = new Vector2((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f));
                sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                jump = keyboard.spaceKey.wasPressedThisFrame;
            }
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue(); if (stick.sqrMagnitude > move.sqrMagnitude) move = stick;
                sprint |= pad.leftStickButton.isPressed || pad.rightShoulder.isPressed;
                jump |= pad.buttonSouth.wasPressedThisFrame;
            }
        }
        Simulate(move, sprint, jump, Time.deltaTime);
    }

    // Also supports scripted movement and repeatable controller verification.
    public void SetInputOverride(Vector2 move, bool sprint = false, bool jump = false)
    {
        inputOverride = true; worldSpaceOverride = false; overrideMove = move; overrideSprint = sprint; overrideJump |= jump;
    }
    public void SetWorldInputOverride(Vector3 direction, bool sprint = false, bool jump = false)
    {
        inputOverride = true; worldSpaceOverride = true;
        overrideMove = Vector2.ClampMagnitude(new Vector2(direction.x, direction.z), 1f);
        overrideSprint = sprint; overrideJump |= jump;
    }
    public void ClearInputOverride() { inputOverride = false; worldSpaceOverride = false; overrideJump = false; }

    public void Simulate(Vector2 input, bool sprint, bool jump, float deltaTime)
    {
        Initialize();
        if (!capsule.enabled || deltaTime <= 0f) return;
        deltaTime = Mathf.Min(deltaTime, 0.05f);
        Vector2 move = Vector2.ClampMagnitude(input, 1f);
        ProbeGround();
        coyoteRemaining = IsGrounded ? coyoteTime : Mathf.Max(0f, coyoteRemaining - deltaTime);
        bufferedJump = jump ? jumpBufferTime : Mathf.Max(0f, bufferedJump - deltaTime);
        bool jumped = (jump || bufferedJump > 0f) && (IsGrounded || coyoteRemaining > 0f);
        if (jumped)
        {
            verticalSpeed = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
            bufferedJump = coyoteRemaining = 0f; IsGrounded = false;
        }
        else if (IsGrounded && verticalSpeed < 0f) verticalSpeed = -2.5f;

        Vector3 forward = !worldSpaceOverride && view != null ? view.forward : Vector3.forward;
        forward.y = 0f; forward.Normalize();
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 desired = (forward * move.y + right * move.x) * (sprint ? runSpeed : walkSpeed);
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, acceleration * deltaTime);
        if (move.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, Mathf.SmoothDampAngle(transform.eulerAngles.y, angle, ref turnVelocity, turnSmoothTime, Mathf.Infinity, deltaTime), 0f);
        }
        Vector3 displacement = horizontalVelocity;
        if (IsGrounded) displacement = Vector3.ProjectOnPlane(horizontalVelocity, GroundNormal).normalized * horizontalVelocity.magnitude;
        verticalSpeed = Mathf.Max(verticalSpeed + gravity * deltaTime, -45f);
        displacement.y += verticalSpeed;
        Vector3 before = transform.position;
        CollisionFlags flags = capsule.Move(displacement * deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
        Velocity = (transform.position - before) / deltaTime;
        if (!jumped) ProbeGround();
        fallingTime = IsGrounded ? 0f : fallingTime + deltaTime;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            float actualSpeed = new Vector2(Velocity.x, Velocity.z).magnitude;
            animator.SetFloat(SpeedId, actualSpeed, 0.12f, deltaTime);
            animator.SetFloat(MotionId, Mathf.Lerp(0.75f, 1f, move.magnitude));
            animator.SetBool(GroundedId, IsGrounded);
            animator.SetBool(JumpId, !IsGrounded && verticalSpeed > 0f);
            animator.SetBool(FallId, !IsGrounded && fallingTime > 0.15f && verticalSpeed <= 0f);
        }
    }

    private void ProbeGround()
    {
        Vector3 center = transform.TransformPoint(capsule.center);
        float radius = capsule.radius * 0.9f;
        Vector3 origin = center - Vector3.up * (capsule.height * 0.5f - capsule.radius) + Vector3.up * 0.08f;
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits, 0.3f, groundLayers, QueryTriggerInteraction.Ignore);
        bool found = false; float nearest = float.PositiveInfinity; GroundNormal = Vector3.up;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            if (hit.normal.y < Mathf.Cos(capsule.slopeLimit * Mathf.Deg2Rad)) continue;
            nearest = hit.distance; GroundNormal = hit.normal; found = true;
        }
        IsGrounded = verticalSpeed <= 0.1f && (found || capsule.isGrounded);
    }

    private void OnDisable() { horizontalVelocity = Vector3.zero; Velocity = Vector3.zero; }
    // The reused humanoid clips contain these animation events.
    private void OnFootstep(AnimationEvent animationEvent) { }
    private void OnLand(AnimationEvent animationEvent) { }
}
