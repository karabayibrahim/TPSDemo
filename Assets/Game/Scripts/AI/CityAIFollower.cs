using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-30)]
[RequireComponent(typeof(CityThirdPersonController))]
public class CityAIFollower : MonoBehaviour
{
    public Transform target;
    [Header("Follow distance")]
    [Tooltip("Distance from the player at which this character stops, in meters.")]
    [Min(0.6f)] public float stoppingDistance = 2f;
    [Min(0.05f)] public float resumeMargin = 0.25f;
    [Header("Movement")]
    [Min(2f)] public float runWhenFartherThan = 7f;
    public bool followJumps = true;
    public LayerMask obstacleLayers = ~0;
    [Min(0.5f)] public float obstacleLookAhead = 1.5f;

    public bool IsFollowing { get; private set; }
    public float DistanceToTarget { get; private set; }
    public Vector3 DesiredDirection { get; private set; }
    public bool WantsSprint { get; private set; }
    public bool RequestedJump { get; private set; }
    private CityThirdPersonController motor, targetMotor;
    private readonly List<Vector3> trail = new List<Vector3>(256);
    private readonly RaycastHit[] hits = new RaycastHit[20];
    private Transform trackedTarget;
    private float jumpCooldown;

    private void OnEnable()
    {
        motor = GetComponent<CityThirdPersonController>();
        motor.readPlayerInput = false;
        motor.SetWorldInputOverride(Vector3.zero);
    }
    private void Update() { Tick(Time.deltaTime); }

    public void Tick(float deltaTime)
    {
        if (motor == null) motor = GetComponent<CityThirdPersonController>();
        motor.readPlayerInput = false;
        stoppingDistance = Mathf.Max(0.6f, stoppingDistance);
        DesiredDirection = Vector3.zero; WantsSprint = RequestedJump = false;
        jumpCooldown = Mathf.Max(0f, jumpCooldown - deltaTime);
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player != gameObject) target = player.transform;
        }
        if (target == null || target == transform)
        {
            IsFollowing = false; motor.SetWorldInputOverride(Vector3.zero); return;
        }
        if (trackedTarget != target)
        {
            trail.Clear(); trackedTarget = target; targetMotor = target.GetComponent<CityThirdPersonController>();
            trail.Add(target.position);
        }
        Vector3 toTarget = Flat(target.position - transform.position);
        DistanceToTarget = toTarget.magnitude;
        // Remember the player's route, so a follower does not simply cut through buildings.
        if (trail.Count == 0 || Flat(target.position - trail[trail.Count - 1]).sqrMagnitude > 0.55f * 0.55f)
        {
            if (trail.Count > 0 && Flat(target.position - trail[trail.Count - 1]).sqrMagnitude > 35f * 35f) trail.Clear();
            trail.Add(target.position); if (trail.Count > 256) trail.RemoveAt(0);
        }
        if (IsFollowing && DistanceToTarget <= stoppingDistance) IsFollowing = false;
        else if (!IsFollowing && DistanceToTarget > stoppingDistance + resumeMargin) IsFollowing = true;
        if (!IsFollowing)
        {
            trail.Clear(); trail.Add(target.position); motor.SetWorldInputOverride(Vector3.zero); return;
        }
        while (trail.Count > 1 && Flat(trail[0] - transform.position).sqrMagnitude < 0.7f * 0.7f) trail.RemoveAt(0);
        Vector3 waypoint = trail.Count > 0 ? trail[0] : target.position;
        Vector3 desired = Flat(waypoint - transform.position);
        if (desired.sqrMagnitude < 0.01f) desired = toTarget;
        desired.Normalize();
        WantsSprint = DistanceToTarget > Mathf.Max(runWhenFartherThan, stoppingDistance + 2f);
        float available = Mathf.Max(0f, DistanceToTarget - stoppingDistance);
        float speed = WantsSprint ? motor.runSpeed : motor.walkSpeed;
        float braking = Mathf.Clamp01(Mathf.Sqrt(2f * motor.acceleration * available) / Mathf.Max(speed, 0.1f));
        DesiredDirection = AvoidObstacles(desired) * braking;
        if (followJumps && targetMotor != null && targetMotor.VerticalSpeed > 1f && target.position.y > transform.position.y + 0.25f && DistanceToTarget < 5f && motor.IsGrounded && jumpCooldown <= 0f)
        {
            RequestedJump = true; jumpCooldown = 0.8f;
        }
        motor.SetWorldInputOverride(DesiredDirection, WantsSprint, RequestedJump);
    }

    private Vector3 AvoidObstacles(Vector3 desired)
    {
        if (Clearance(desired) >= obstacleLookAhead) return desired;
        float best = float.NegativeInfinity; Vector3 direction = Vector3.zero;
        // Both sides are evaluated, preventing a permanent left/right steering bias.
        for (int i = 0; i < 6; i++)
        {
            float angle = (i / 2 + 1) * 35f * (i % 2 == 0 ? 1f : -1f);
            Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;
            float clearance = Clearance(candidate);
            if (clearance < 0.45f) continue;
            float score = clearance / obstacleLookAhead + Vector3.Dot(candidate, desired) * 0.65f;
            if (score > best) { best = score; direction = candidate; }
        }
        return direction;
    }

    private float Clearance(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * 0.85f;
        int count = Physics.SphereCastNonAlloc(origin, 0.3f, direction, hits, obstacleLookAhead, obstacleLayers, QueryTriggerInteraction.Ignore);
        float nearest = obstacleLookAhead;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider == null || hits[i].transform.IsChildOf(transform) || (target != null && hits[i].transform.IsChildOf(target))) continue;
            // Ignore walkable ground; it is handled by the shared slope-aware motor.
            if (hits[i].normal.y > 0.65f) continue;
            nearest = Mathf.Min(nearest, hits[i].distance);
        }
        return nearest;
    }

    private static Vector3 Flat(Vector3 value) { value.y = 0f; return value; }
    private void OnDisable()
    {
        if (motor != null) motor.SetWorldInputOverride(Vector3.zero);
        IsFollowing = false; trail.Clear(); trackedTarget = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.6f, stoppingDistance));
        if (target != null) Gizmos.DrawLine(transform.position, target.position);
    }
}
