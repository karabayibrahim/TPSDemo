using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CityDayNightCycle))]
public class CityDayNightCycleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var cycle = (CityDayNightCycle)target;
        EditorGUILayout.LabelField("Current Time", cycle.CurrentTime, EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) cycle.ApplyLighting();
        EditorGUILayout.HelpBox("Full Day Minutes: gerçek zamanda kaç dakikada bir 24 saat geçeceği. Döngü Play modunda ilerler; editörde saati değiştirerek önizleyebilirsiniz.", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        Preset(cycle, "Sabah", 7f);
        Preset(cycle, "Gündüz", 14f);
        Preset(cycle, "Akşam", 18.5f);
        Preset(cycle, "Gece", 22f);
        EditorGUILayout.EndHorizontal();
    }

    private static void Preset(CityDayNightCycle cycle, string label, float hour)
    {
        if (!GUILayout.Button(label)) return;
        Undo.RecordObject(cycle, "Set city time");
        cycle.SetTime(hour);
        EditorUtility.SetDirty(cycle);
        SceneView.RepaintAll();
    }
}
