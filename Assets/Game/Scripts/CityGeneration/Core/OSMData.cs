using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class OSMNode
{
    public long id;
    public double latitude;
    public double longitude;

    public OSMNode(long id, double latitude, double longitude)
    {
        this.id = id;
        this.latitude = latitude;
        this.longitude = longitude;
    }
}

[System.Serializable]
public class OSMWay
{
    public long id;

    public List<long> nodeIds = new List<long>();
    public Dictionary<string, string> tags =
        new Dictionary<string, string>();

    public bool HasTag(string key)
    {
        return tags.ContainsKey(key);
    }

    public string GetTag(string key, string defaultValue = "")
    {
        if (tags.TryGetValue(key, out string value))
            return value;

        return defaultValue;
    }
}

public class OSMMapData
{
    public Dictionary<long, OSMNode> nodes =
        new Dictionary<long, OSMNode>();

    public List<OSMWay> ways =
        new List<OSMWay>();

    public double minLatitude;
    public double maxLatitude;

    public double minLongitude;
    public double maxLongitude;

    public double centerLatitude;
    public double centerLongitude;
}