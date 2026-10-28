using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CityPlayerValidation
{
    public const string PendingKey = "CityPlayerValidation.Pending";
    static CityPlayerValidation() { EditorApplication.playModeStateChanged += OnPlayMode; }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        if (!Application.isPlaying) return;
        var fixture = new GameObject("Temporary controller verification");
        int failures = 0;
        try
        {
            GameObject source = GameObject.Find("Player");
            GameObject floor = Surface(fixture.transform, new Vector3(5000f, 99.8f, 5000f), new Vector3(20f, 0.4f, 20f), Vector3.zero);
            GameObject clone = Clone(source, fixture.transform, new Vector3(5000f, 100.04f, 4997f));
            var motor = clone.GetComponent<CityThirdPersonController>();
            var animator = clone.GetComponent<Animator>();
            var ik = clone.GetComponent<HumanoidSlopeFootIK>();
            Tick(motor, animator, Vector2.zero, 45);
            Vector3 start = clone.transform.position;
            Tick(motor, animator, Vector2.up, 120);
            float travel = (clone.transform.position - start).magnitude;
            if (travel < 3f || !motor.IsGrounded) failures++;
            if (ik.EvaluationCount < 10) failures++;
            Debug.Log($"[Player verification] Flat walk: {travel:F2}m, grounded={motor.IsGrounded}, IK callbacks={ik.EvaluationCount}");
            Tick(motor, animator, Vector2.zero, 35);
            float initialY = clone.transform.position.y;
            motor.Simulate(Vector2.zero, false, true, 1f / 60f); animator.Update(1f / 60f);
            float peak = initialY, airborneWeight = 1f;
            for (int i = 0; i < 100; i++)
            {
                Tick(motor, animator, Vector2.zero, 1); peak = Mathf.Max(peak, clone.transform.position.y);
                if (i == 15) airborneWeight = Mathf.Max(ik.LeftWeight, ik.RightWeight);
            }
            if (peak - initialY < 0.7f || !motor.IsGrounded || airborneWeight > 0.15f) failures++;
            Debug.Log($"[Player verification] Jump height={peak - initialY:F2}m, airborne IK={airborneWeight:F3}, landed={motor.IsGrounded}");
            clone.SetActive(false);

            Surface(fixture.transform, new Vector3(5030f, 100f, 5000f), new Vector3(8f, 0.3f, 14f), new Vector3(-20f, 0f, 0f));
            Physics.SyncTransforms();
            Physics.Raycast(new Vector3(5030f, 105f, 4997f), Vector3.down, out RaycastHit hit, 20f);
            clone = Clone(source, fixture.transform, hit.point + Vector3.up * 0.035f);
            motor = clone.GetComponent<CityThirdPersonController>(); animator = clone.GetComponent<Animator>(); ik = clone.GetComponent<HumanoidSlopeFootIK>();
            Tick(motor, animator, Vector2.zero, 45); initialY = clone.transform.position.y;
            Tick(motor, animator, Vector2.up, 120);
            float rise = clone.transform.position.y - initialY;
            if (rise < 0.7f || !motor.IsGrounded || (!ik.LeftFootSupported && !ik.RightFootSupported)) failures++;
            Debug.Log($"[Player verification] 20-degree uphill: rise={rise:F2}m, grounded={motor.IsGrounded}, foot support={ik.LeftFootSupported}/{ik.RightFootSupported}");
            clone.SetActive(false);

            Surface(fixture.transform, new Vector3(5060f, 100f, 5000f), new Vector3(8f, 0.3f, 10f), new Vector3(0f, 0f, 18f));
            Physics.SyncTransforms();
            Physics.Raycast(new Vector3(5060f, 105f, 5000f), Vector3.down, out hit, 20f);
            clone = Clone(source, fixture.transform, hit.point + Vector3.up * 0.035f);
            motor = clone.GetComponent<CityThirdPersonController>(); animator = clone.GetComponent<Animator>(); ik = clone.GetComponent<HumanoidSlopeFootIK>();
            Tick(motor, animator, Vector2.zero, 100);
            float footDifference = Mathf.Abs(ik.LeftTarget.y - ik.RightTarget.y);
            if (!ik.LeftFootSupported || !ik.RightFootSupported || footDifference < 0.025f) failures++;
            Debug.Log($"[Player verification] Cross-slope: foot height difference={footDifference:F3}m, weights={ik.LeftWeight:F2}/{ik.RightWeight:F2}, pelvis={ik.PelvisOffset:F3}m");
            SessionState.SetInt("CityPlayerValidation.Failures", failures);
            Debug.Log("[Player verification] TOTAL FAILURES: " + failures);
        }
        catch (System.Exception error) { SessionState.SetInt("CityPlayerValidation.Failures", -1); Debug.LogException(error); }
        finally { Object.DestroyImmediate(fixture); }
    }

    private static GameObject Surface(Transform parent, Vector3 position, Vector3 scale, Vector3 angles)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Verification ground";
        cube.transform.SetParent(parent, false); cube.transform.SetPositionAndRotation(position, Quaternion.Euler(angles)); cube.transform.localScale = scale;
        return cube;
    }

    private static GameObject Clone(GameObject source, Transform parent, Vector3 position)
    {
        GameObject clone = Object.Instantiate(source, position, Quaternion.identity, parent); clone.name = "Verification humanoid";
        var motor = clone.GetComponent<CityThirdPersonController>(); motor.enabled = false;
        var orientation = new GameObject("Verification movement orientation"); orientation.transform.SetParent(parent, false); motor.view = orientation.transform;
        var animator = clone.GetComponent<Animator>(); animator.Rebind(); animator.Update(0f);
        Physics.SyncTransforms(); return clone;
    }

    private static void Tick(CityThirdPersonController motor, Animator animator, Vector2 move, int count)
    {
        for (int i = 0; i < count; i++) { motor.Simulate(move, false, false, 1f / 60f); Physics.SyncTransforms(); animator.Update(1f / 60f); }
    }
}
