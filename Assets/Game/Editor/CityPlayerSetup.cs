using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CityPlayerSetup
{
    [MenuItem("Tools/Player/Configure Scene Player")]
    public static void Configure()
    {
        GameObject player = GameObject.Find("Player");
        if (player == null) throw new System.InvalidOperationException("Scene object 'Player' was not found.");
        Animator animator = player.GetComponent<Animator>();
        if (animator == null || !animator.isHuman || animator.avatar == null)
            throw new System.InvalidOperationException("Player needs a valid humanoid Animator and Avatar.");
        Camera camera = Camera.main;
        if (camera == null) throw new System.InvalidOperationException("A MainCamera is required.");
        Undo.RecordObject(player.transform, "Configure third person player");
        Undo.RecordObject(animator, "Configure player animator");
        EnsureFolder("Assets/Game/Animations/Player");
        const string controllerPath = "Assets/Game/Animations/Player/CityPlayer.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            const string source = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
            if (!AssetDatabase.CopyAsset(source, controllerPath))
                throw new System.InvalidOperationException("Could not copy humanoid locomotion controller.");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        }
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        foreach (ChildAnimatorState child in layers[0].stateMachine.states) child.state.iKOnFeet = false;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.Rebind(); animator.Update(0f);

        CharacterController capsule = player.GetComponent<CharacterController>();
        if (capsule == null) capsule = Undo.AddComponent<CharacterController>(player);
        Undo.RecordObject(capsule, "Configure player capsule");
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        capsule.height = Mathf.Clamp(head.position.y - player.transform.position.y + 0.23f, 1.6f, 2.3f);
        capsule.radius = 0.28f;
        capsule.center = Vector3.up * (capsule.height * 0.5f);
        capsule.skinWidth = 0.035f;
        capsule.stepOffset = 0.28f;
        capsule.slopeLimit = 50f;
        capsule.minMoveDistance = 0f;
        player.tag = "Player";
        var motor = player.GetComponent<CityThirdPersonController>();
        if (motor == null) motor = Undo.AddComponent<CityThirdPersonController>(player);
        Undo.RecordObject(motor, "Configure player movement");
        motor.animator = animator; motor.view = camera.transform;
        var ik = player.GetComponent<HumanoidSlopeFootIK>();
        if (ik == null) ik = Undo.AddComponent<HumanoidSlopeFootIK>(player);

        var city = Object.FindAnyObjectByType<IstanbulCityGenerator>();
        if (city != null)
        {
            Undo.RecordObject(city, "Disable cinematic mode for player"); city.DisableCinematic();
            var collision = city.GetComponent<OSMCityCollision>();
            if (collision == null) collision = Undo.AddComponent<OSMCityCollision>(city.gameObject);
            collision.Refresh();
        }
        foreach (CinematicCityCamera cinematic in Object.FindObjectsByType<CinematicCityCamera>())
        {
            Undo.RecordObject(cinematic, "Disable cinematic camera"); cinematic.SetCinematicActive(false); cinematic.enabled = false;
        }
        var orbit = camera.GetComponent<CityThirdPersonCamera>();
        if (orbit == null) orbit = Undo.AddComponent<CityThirdPersonCamera>(camera.gameObject);
        Undo.RecordObject(orbit, "Configure third person camera"); orbit.target = player.transform;
        Undo.RecordObject(camera, "Configure player camera clipping"); camera.nearClipPlane = 0.08f;

        // Preserve the user's horizontal spawn position; only settle onto the ground.
        Physics.SyncTransforms();
        capsule.enabled = false;
        if (Physics.Raycast(player.transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            player.transform.position = new Vector3(player.transform.position.x, hit.point.y + 0.025f, player.transform.position.z);
        capsule.enabled = true;
        orbit.ResetView();
        EditorUtility.SetDirty(player);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        EnsureFolder(folder.Substring(0, slash));
        AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
    }
}
