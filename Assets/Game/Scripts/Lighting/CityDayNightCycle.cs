using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class CityDayNightCycle : MonoBehaviour
{
    [Header("Time")]
    [Range(0f, 24f)] public float timeOfDay = 14f;
    [Tooltip("Real minutes for one complete 24-hour cycle.")]
    [Min(0.1f)] public float fullDayMinutes = 20f;
    public bool paused;
    public bool useUnscaledTime = true;
    public bool previewInEditor = true;
    [Header("Lighting")]
    public Light sun;
    public Material skyMaterial;
    [Range(0f, 360f)] public float sunAzimuth = 140f;
    [Min(0f)] public float daylightIntensity = 1.35f;
    [Min(0f)] public float moonlightIntensity = 0.1f;
    public bool controlFog = true;
    [Header("Display")]
    public bool showClock = true;
    public bool showTimeControls = true;

    public string CurrentTime => string.Format("{0:00}:{1:00}", Hour, Minute);
    public int Hour => Mathf.FloorToInt(Mathf.Repeat(timeOfDay, 24f));
    public int Minute => Mathf.FloorToInt(Mathf.Repeat(timeOfDay * 60f, 60f));
    public float Daylight { get; private set; }
    public float NightAmount => 1f - Daylight;

    private static readonly int NightId = Shader.PropertyToID("_CityNight");
    private static readonly int DayId = Shader.PropertyToID("_CityDaylight");
    private static readonly int HourId = Shader.PropertyToID("_CityHour");
    private static readonly int SunId = Shader.PropertyToID("_CitySunDirection");
    private GUIStyle clockStyle;
    private Material previousSky;
    private AmbientMode previousAmbientMode;
    private Color previousAmbientSky, previousAmbientEquator, previousAmbientGround, previousFog;
    private float previousFogDensity, previousSunIntensity;
    private Color previousSunColor;
    private Quaternion previousSunRotation;
    private bool previousFogEnabled;
    private FogMode previousFogMode;
    private float previousReflectionIntensity;
    private SphericalHarmonicsL2 previousAmbientProbe;
    private bool captured;

    private void OnEnable()
    {
        if (sun == null)
        {
            foreach (Light light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional) { sun = light; break; }
        }
        previousSky = RenderSettings.skybox;
        previousAmbientMode = RenderSettings.ambientMode;
        previousAmbientSky = RenderSettings.ambientSkyColor;
        previousAmbientEquator = RenderSettings.ambientEquatorColor;
        previousAmbientGround = RenderSettings.ambientGroundColor;
        previousFog = RenderSettings.fogColor;
        previousFogDensity = RenderSettings.fogDensity;
        previousFogEnabled = RenderSettings.fog;
        previousFogMode = RenderSettings.fogMode;
        previousReflectionIntensity = RenderSettings.reflectionIntensity;
        previousAmbientProbe = RenderSettings.ambientProbe;
        if (sun != null)
        {
            previousSunIntensity = sun.intensity;
            previousSunColor = sun.color;
            previousSunRotation = sun.transform.rotation;
        }
        captured = true;
        ApplyLighting();
    }

    private void Update()
    {
        if (Application.isPlaying && !paused)
            AdvanceTime(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        if (Application.isPlaying || previewInEditor) ApplyLighting();
    }

    public void SetTime(float hour)
    {
        timeOfDay = Mathf.Repeat(hour, 24f);
        ApplyLighting();
    }

    public void AdvanceTime(float realSeconds)
    {
        if (paused) return;
        timeOfDay = Mathf.Repeat(timeOfDay + Mathf.Max(0f, realSeconds) * 24f /
            (Mathf.Max(0.1f, fullDayMinutes) * 60f), 24f);
    }

    public void ApplyLighting()
    {
        timeOfDay = Mathf.Repeat(timeOfDay, 24f);
        fullDayMinutes = Mathf.Max(0.1f, fullDayMinutes);
        float elevation = Mathf.Sin((timeOfDay - 6f) / 24f * Mathf.PI * 2f);
        Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.25f, elevation));
        float warmth = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.45f, elevation));
        Quaternion rotation = Quaternion.Euler(timeOfDay / 24f * 360f - 90f, sunAzimuth, 0f);
        if (sun != null)
        {
            bool moon = elevation < -0.08f;
            sun.transform.rotation = moon ? Quaternion.LookRotation(-(rotation * Vector3.forward), rotation * Vector3.up) : rotation;
            sun.intensity = daylightIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.4f, elevation)) +
                (moon ? moonlightIntensity * NightAmount : 0f);
            sun.color = moon ? new Color(0.48f, 0.62f, 1f) :
                Color.Lerp(new Color(1f, 0.96f, 0.86f), new Color(1f, 0.43f, 0.2f), warmth);
            RenderSettings.sun = sun;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.06f, 0.08f, 0.14f), new Color(0.52f, 0.64f, 0.78f), Daylight);
        RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.04f, 0.05f, 0.08f), new Color(0.4f, 0.42f, 0.44f), Daylight);
        RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.02f, 0.025f, 0.04f), new Color(0.25f, 0.23f, 0.2f), Daylight);
        // Update ambient lighting directly; no per-frame environment rebake.
        var ambient = new SphericalHarmonicsL2();
        ambient.AddAmbientLight(RenderSettings.ambientEquatorColor);
        ambient.AddDirectionalLight(Vector3.up, RenderSettings.ambientSkyColor, 0.5f);
        RenderSettings.ambientProbe = ambient;
        RenderSettings.reflectionIntensity = Mathf.Lerp(0.08f, 1f, Daylight);
        if (skyMaterial != null && RenderSettings.skybox != skyMaterial) RenderSettings.skybox = skyMaterial;
        if (controlFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(new Color(0.018f, 0.025f, 0.055f), new Color(0.57f, 0.65f, 0.7f), Daylight);
            RenderSettings.fogDensity = Mathf.Lerp(0.0025f, 0.0011f, Daylight);
        }
        Shader.SetGlobalFloat(NightId, NightAmount);
        Shader.SetGlobalFloat(DayId, Daylight);
        Shader.SetGlobalFloat(HourId, timeOfDay);
        Shader.SetGlobalVector(SunId, -(rotation * Vector3.forward));
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || (!showClock && !showTimeControls)) return;
        if (clockStyle == null) clockStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        float panelHeight = showTimeControls ? 180f : 55f;
        GUILayout.BeginArea(new Rect(Mathf.Max(8f, Screen.width - 260f), showClock ? 16f : 64f, 244f, panelHeight), GUI.skin.box);
        if (showClock) GUILayout.Label(CurrentTime, clockStyle);
        if (showTimeControls)
        {
            paused = GUILayout.Toggle(paused, "Saati duraklat");
            GUILayout.Label("24 saat = " + fullDayMinutes.ToString("0.0") + " dakika");
            fullDayMinutes = GUILayout.HorizontalSlider(fullDayMinutes, 0.5f, 120f);
            GUILayout.Label("Saati ayarla");
            float selected = GUILayout.HorizontalSlider(timeOfDay, 0f, 23.999f);
            if (!Mathf.Approximately(selected, timeOfDay)) SetTime(selected);
        }
        GUILayout.EndArea();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(NightId, 0f);
        Shader.SetGlobalFloat(DayId, 1f);
        if (!captured) return;
        RenderSettings.skybox = previousSky;
        RenderSettings.ambientMode = previousAmbientMode;
        RenderSettings.ambientSkyColor = previousAmbientSky;
        RenderSettings.ambientEquatorColor = previousAmbientEquator;
        RenderSettings.ambientGroundColor = previousAmbientGround;
        RenderSettings.fogColor = previousFog;
        RenderSettings.fogDensity = previousFogDensity;
        RenderSettings.fog = previousFogEnabled;
        RenderSettings.fogMode = previousFogMode;
        RenderSettings.reflectionIntensity = previousReflectionIntensity;
        RenderSettings.ambientProbe = previousAmbientProbe;
        if (sun != null) { sun.intensity = previousSunIntensity; sun.color = previousSunColor; sun.transform.rotation = previousSunRotation; }
        captured = false;
    }
}
