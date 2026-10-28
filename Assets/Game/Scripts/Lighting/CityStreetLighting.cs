using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class CityStreetLighting : MonoBehaviour
{
    [Range(0, 24)] public int maximumActiveLights = 10;
    [Min(5f)] public float activationDistance = 65f;
    public Transform viewer;
    private Light[] lamps;
    private readonly List<Light> nearby = new List<Light>();
    private float nextUpdate;

    private void OnEnable() { Refresh(); }
    public void Refresh() { lamps = GetComponentsInChildren<Light>(true); nextUpdate = 0f; }

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextUpdate) return;
        nextUpdate = Time.realtimeSinceStartup + 0.5f;
        RefreshLights();
    }

    public void RefreshLights()
    {
        if (lamps == null) Refresh();
        float night = Shader.GetGlobalFloat("_CityNight");
        Transform camera = viewer;
        if (camera == null && Camera.main != null) camera = Camera.main.transform;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
            camera = UnityEditor.SceneView.lastActiveSceneView.camera.transform;
#endif
        nearby.Clear();
        foreach (Light lamp in lamps)
        {
            if (lamp == null) continue;
            lamp.enabled = false;
            if (camera != null && night > 0.2f && (lamp.transform.position - camera.position).sqrMagnitude <= activationDistance * activationDistance)
                nearby.Add(lamp);
        }
        if (camera == null) return;
        Vector3 position = camera.position;
        nearby.Sort((a, b) => (a.transform.position - position).sqrMagnitude.CompareTo((b.transform.position - position).sqrMagnitude));
        for (int i = 0; i < Mathf.Min(maximumActiveLights, nearby.Count); i++)
        {
            nearby[i].intensity = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.75f, night)) * 3.5f;
            nearby[i].enabled = true;
        }
    }

    private void OnDisable()
    {
        if (lamps == null) return;
        foreach (Light lamp in lamps) if (lamp != null) lamp.enabled = false;
    }
}
