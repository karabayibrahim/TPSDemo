using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Text))]
public class CityClockDisplay : MonoBehaviour
{
    public CityDayNightCycle cycle;
    private Text label;
    private string displayed;
    private int displayedMinute = int.MinValue;

    private void OnEnable()
    {
        label = GetComponent<Text>();
        displayed = null;
        displayedMinute = int.MinValue;
        Refresh();
    }
    private void Update() { Refresh(); }
    public void Refresh()
    {
        if (label == null) label = GetComponent<Text>();
        if (cycle == null) cycle = FindAnyObjectByType<CityDayNightCycle>();
        int minute = cycle != null ? cycle.Hour * 60 + cycle.Minute : -1;
        if (displayedMinute == minute && label.text == displayed) return;
        string time = cycle != null ? cycle.CurrentTime : "--:--";
        displayedMinute = minute;
        displayed = time;
        label.text = time;
    }
}
