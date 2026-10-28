using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HumanoidSlopeFootIK : MonoBehaviour
{
    [Header("Ground detection")]
    public LayerMask groundLayers = ~0;
    [Min(0.1f)] public float probeHeight = 0.55f;
    [Min(0.1f)] public float probeDistance = 1.25f;
    [Range(0.01f, 0.1f)] public float probeRadius = 0.035f;
    [Range(0.02f, 0.2f)] public float minimumSoleHeight = 0.07f;
    [Header("Blending")]
    [Range(0f, 1f)] public float weight = 1f;
    [Min(1f)] public float footResponse = 16f;
    [Min(1f)] public float pelvisResponse = 10f;
    [Min(0f)] public float maximumStepCorrection = 0.42f;
    [Min(0f)] public float maximumPelvisDrop = 0.28f;
    [Min(0f)] public float maximumPelvisRise = 0.08f;
    public bool drawDebug;

    public bool LeftFootSupported { get; private set; }
    public bool RightFootSupported { get; private set; }
    public Vector3 LeftTarget => left.position;
    public Vector3 RightTarget => right.position;
    public Vector3 LeftNormal => left.normal;
    public Vector3 RightNormal => right.normal;
    public float LeftWeight => left.weight;
    public float RightWeight => right.weight;
    public float PelvisOffset => pelvisOffset;
    public int EvaluationCount { get; private set; }

    private struct Foot
    {
        public Vector3 position, normal;
        public Quaternion rotation;
        public float weight, offset;
        public bool initialized, supported;
    }
    private Animator animator;
    private CityThirdPersonController motor;
    private Transform leftBone, rightBone;
    private Foot left, right;
    private float pelvisOffset;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    private void Awake() { Initialize(); }
    private bool Initialize()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (!animator.isHuman || animator.avatar == null) return false;
        if (motor == null) motor = GetComponentInParent<CityThirdPersonController>();
        if (leftBone == null) leftBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (rightBone == null) rightBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        return leftBone != null && rightBone != null;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !Initialize()) return;
        EvaluationCount++;
        float dt = Mathf.Max(Time.deltaTime, 0.001f);
        bool grounded = motor == null || (motor.IsGrounded && motor.VerticalSpeed <= 0.1f);
        Vector3 animatedLeft = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
        Vector3 animatedRight = animator.GetIKPosition(AvatarIKGoal.RightFoot);
        Quaternion rotationLeft = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
        Quaternion rotationRight = animator.GetIKRotation(AvatarIKGoal.RightFoot);
        float scale = Mathf.Abs(transform.lossyScale.y);
        Solve(ref left, animatedLeft, rotationLeft, Mathf.Max(minimumSoleHeight, animator.leftFeetBottomHeight * scale), grounded, dt);
        Solve(ref right, animatedRight, rotationRight, Mathf.Max(minimumSoleHeight, animator.rightFeetBottomHeight * scale), grounded, dt);
        LeftFootSupported = left.supported; RightFootSupported = right.supported;
        float desiredPelvis = 0f;
        if (grounded)
        {
            float a = left.supported && left.weight > 0.25f ? left.offset : 0f;
            float b = right.supported && right.weight > 0.25f ? right.offset : 0f;
            desiredPelvis = Mathf.Clamp(Mathf.Min(a, b), -maximumPelvisDrop, maximumPelvisRise);
        }
        pelvisOffset = Mathf.Lerp(pelvisOffset, desiredPelvis, 1f - Mathf.Exp(-pelvisResponse * dt));
        animator.bodyPosition += Vector3.up * pelvisOffset * weight;
        Apply(AvatarIKGoal.LeftFoot, left);
        Apply(AvatarIKGoal.RightFoot, right);
    }

    private void Solve(ref Foot foot, Vector3 animated, Quaternion rotation, float sole, bool grounded, float dt)
    {
        Vector3 origin = animated + Vector3.up * probeHeight;
        int count = grounded ? Physics.SphereCastNonAlloc(origin, probeRadius, Vector3.down, hits, probeDistance, groundLayers, QueryTriggerInteraction.Ignore) : 0;
        bool found = false; float nearest = float.PositiveInfinity; RaycastHit surface = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            Transform characterRoot = motor != null ? motor.transform : transform;
            if (hit.collider == null || hit.transform.IsChildOf(characterRoot) || hit.distance >= nearest || hit.normal.y < 0.5f) continue;
            float offset = hit.point.y + hit.normal.y * sole - animated.y;
            if (Mathf.Abs(offset) > maximumStepCorrection) continue;
            nearest = hit.distance; surface = hit; found = true;
        }
        foot.supported = found;
        float lift = animated.y - (transform.position.y + sole);
        float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.25f, lift));
        float targetWeight = found && grounded ? weight * (1f - swing) : 0f;
        float response = 1f - Mathf.Exp(-footResponse * dt);
        foot.weight = Mathf.Lerp(foot.weight, targetWeight, response);
        Vector3 target = animated; Quaternion targetRotation = rotation;
        foot.offset = 0f;
        if (found)
        {
            target.y = surface.point.y + surface.normal.y * sole;
            targetRotation = Quaternion.FromToRotation(Vector3.up, surface.normal) * rotation;
            foot.normal = surface.normal;
            foot.offset = target.y - animated.y;
        }
        else foot.normal = Vector3.up;
        if (!foot.initialized || foot.weight < 0.03f) { foot.position = target; foot.rotation = targetRotation; foot.initialized = true; }
        else
        {
            // XZ follows the animation, so swinging legs are never pinned behind the body.
            foot.position = new Vector3(target.x, Mathf.Lerp(foot.position.y, target.y, response), target.z);
            foot.rotation = Quaternion.Slerp(foot.rotation, targetRotation, response);
        }
    }

    private void Apply(AvatarIKGoal goal, Foot foot)
    {
        animator.SetIKPositionWeight(goal, foot.weight);
        animator.SetIKRotationWeight(goal, foot.weight);
        animator.SetIKPosition(goal, foot.position);
        animator.SetIKRotation(goal, foot.rotation);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;
        Gizmos.color = Color.cyan; Gizmos.DrawSphere(left.position, 0.045f); Gizmos.DrawRay(left.position, left.normal * 0.3f);
        Gizmos.color = Color.yellow; Gizmos.DrawSphere(right.position, 0.045f); Gizmos.DrawRay(right.position, right.normal * 0.3f);
    }
}
