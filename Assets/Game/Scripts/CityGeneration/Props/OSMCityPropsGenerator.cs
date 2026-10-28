using System;
using System.Collections.Generic;
using UnityEngine;

public class OSMCityPropsGenerator : MonoBehaviour
{
    [Header("Matching street prefabs")]
    public GameObject[] vehicles;
    public GameObject[] streetLamps;
    public GameObject litterBin;
    public GameObject[] dumpsters;
    [Header("Distribution")]
    [Min(12f)] public float lampSpacing = 27f;
    [Min(8f)] public float parkedCarSpacing = 17f;
    [Range(0f, 1f)] public float parkingDensity = 0.58f;
    [Min(15f)] public float binSpacing = 46f;
    [Min(1f)] public float minimumPedestrianWidth = 1.1f;
    [SerializeField, HideInInspector] private Transform generatedRoot;
    public int LampCount { get; private set; }
    public int CarCount { get; private set; }
    public int BinCount { get; private set; }

    private struct Occupied { public Vector2 position; public float radius; }
    private struct Junction { public Vector2 position; public float radius; }
    private readonly List<Occupied> occupied = new List<Occupied>();
    private readonly List<Junction> junctions = new List<Junction>();
    private readonly List<List<Vector2>> buildings = new List<List<Vector2>>();
    private readonly List<Bounds> buildingBounds = new List<Bounds>();
    private readonly List<Vector2> entrances = new List<Vector2>();

    [ContextMenu("Regenerate Street Props")]
    private void Regenerate()
    {
        var generator = GetComponent<IstanbulCityGenerator>();
        if (generator == null) return;
#if UNITY_EDITOR
        var data = new UnityEditor.SerializedObject(generator);
        var file = data.FindProperty("osmFile").objectReferenceValue as TextAsset;
        if (file != null) Generate(OSMParser.Parse(file.text));
#endif
    }

    public void Generate(OSMMapData map)
    {
        if (map == null) return;
        occupied.Clear(); junctions.Clear(); buildings.Clear(); buildingBounds.Clear(); entrances.Clear();
        LampCount = CarCount = BinCount = 0;
        var network = OSMRoadNetworkData.Build(map);
        var environment = OSMRoadEnvironmentData.Build(map);
        var elevation = GetComponent<OSMTerrainElevation>();
        foreach (long id in network.GetJunctionNodeIds())
            if (map.nodes.TryGetValue(id, out OSMNode node))
            {
                Vector3 p = Geo(map, node);
                junctions.Add(new Junction { position = new Vector2(p.x, p.z), radius = network.GetRoadTrimDistance(id) });
            }
        BuildObstacles(map);
        var stage = new GameObject("StreetProps");
        stage.transform.SetParent(transform, false);
        foreach (OSMWay way in map.ways)
        {
            if (!OSMRoadMetrics.IsSidewalkEligibleRoad(way)) continue;
            var points = new List<Vector3>();
            foreach (long id in way.nodeIds) if (map.nodes.TryGetValue(id, out OSMNode node)) points.Add(Geo(map, node));
            if (points.Count < 2) continue;
            float length = 0f; for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
            float width = OSMRoadMetrics.GetRoadWidth(way);
            var random = new System.Random(unchecked((int)(way.id ^ (way.id >> 32))));
            for (int side = -1; side <= 1; side += 2)
            {
                OSMRoadSidePlan plan = environment.GetSidePlan(way, side < 0);
                if (!plan.generateSidewalk) continue;
                float walkWidth = Mathf.Max(0.8f, plan.sidewalkWidth);
                float curb = width * 0.5f + 0.005f;
                for (float distance = 9f + (float)random.NextDouble() * 10f; distance < length - 7f; distance += lampSpacing)
                {
                    Sample(points, distance, out Vector3 center, out Vector3 tangent);
                    Vector3 outward = OSMRoadMetrics.GetRight(tangent) * side;
                    Vector3 point = center + outward * (curb + 0.26f);
                    if (walkWidth < minimumPedestrianWidth + 0.3f || !Free(point, 0.3f, 1.5f, 1.6f)) continue;
                    GameObject prefab = Pick(streetLamps, random); if (prefab == null) continue;
                    Place(prefab, stage.transform, point, -outward, elevation, OSMRoadMetrics.GetCurbHeight(way), false);
                    Reserve(point, 0.35f); LampCount++;
                }
                for (float distance = 15f + (float)random.NextDouble() * 12f; distance < length - 8f; distance += binSpacing)
                {
                    Sample(points, distance, out Vector3 center, out Vector3 tangent);
                    Vector3 outward = OSMRoadMetrics.GetRight(tangent) * side;
                    bool large = dumpsters != null && dumpsters.Length > 0 && walkWidth + plan.bufferWidth > minimumPedestrianWidth + 1.15f && random.NextDouble() < 0.3;
                    float depth = large ? 1.05f : 0.45f;
                    if (walkWidth + plan.bufferWidth < minimumPedestrianWidth + depth) continue;
                    Vector3 point = center + outward * (curb + depth * 0.5f + 0.08f);
                    if (!Free(point, large ? 0.85f : 0.32f, 2f, 2f)) continue;
                    GameObject prefab = large ? Pick(dumpsters, random) : litterBin;
                    if (prefab == null) continue;
                    Place(prefab, stage.transform, point, -outward, elevation, OSMRoadMetrics.GetCurbHeight(way), false);
                    Reserve(point, large ? 0.9f : 0.38f); BinCount++;
                }
            }
            bool oneWay = OSMRoadMetrics.IsOneWay(way);
            bool noParking = way.GetTag("parking:lane:both") == "no" || way.GetTag("parking:both") == "no" || way.GetTag("parking") == "no";
            if (noParking || width < (oneWay ? 5.8f : 8.5f)) continue;
            int parkingSide = 1;
            if (!environment.GetSidePlan(way, false).generateSidewalk) parkingSide = -1;
            for (float distance = 12f + (float)random.NextDouble() * 7f; distance < length - 10f; distance += parkedCarSpacing)
            {
                if (random.NextDouble() > parkingDensity) continue;
                Sample(points, distance, out Vector3 center, out Vector3 tangent);
                Sample(points, distance - 2.5f, out _, out Vector3 approach);
                Sample(points, distance + 2.5f, out _, out Vector3 departure);
                if (Vector3.Angle(approach, departure) > 12f) continue;
                Vector3 point = center + OSMRoadMetrics.GetRight(tangent) * parkingSide * (width * 0.5f - 1.12f);
                if (!Free(point, 2.65f, 5f, 3.5f)) continue;
                GameObject prefab = Pick(vehicles, random); if (prefab == null) continue;
                Vector3 direction = way.GetTag("oneway") == "-1" ? -tangent : tangent;
                if (!oneWay && parkingSide < 0) direction = -direction;
                Place(prefab, stage.transform, point, direction, elevation, 0.02f, true);
                Reserve(point, 2.65f); CarCount++;
            }
        }
        Transform old = generatedRoot;
        if (old == null) foreach (Transform child in transform) if (child != stage.transform && child.name == "StreetProps") { old = child; break; }
        if (old != null)
        {
            if (Application.isPlaying) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            else
            {
#if UNITY_EDITOR
                UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);
#else
                DestroyImmediate(old.gameObject);
#endif
            }
        }
        generatedRoot = stage.transform;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(stage, "Generate street props");
#endif
        stage.AddComponent<CityStreetLighting>().Refresh();
        Debug.Log($"Street props: {LampCount} lamps, {CarCount} parked vehicles, {BinCount} bins/containers.", this);
    }

    private void BuildObstacles(OSMMapData map)
    {
        foreach (OSMWay way in map.ways)
        {
            if (!way.HasTag("building") || way.nodeIds.Count < 4) continue;
            var polygon = new List<Vector2>();
            foreach (long id in way.nodeIds) if (map.nodes.TryGetValue(id, out OSMNode node)) { Vector3 p = Geo(map, node); polygon.Add(new Vector2(p.x, p.z)); }
            if (polygon.Count < 3) continue;
            var bounds = new Bounds(new Vector3(polygon[0].x, 0f, polygon[0].y), Vector3.zero);
            float longest = 0f; Vector2 entrance = polygon[0];
            for (int i = 0; i < polygon.Count; i++)
            {
                bounds.Encapsulate(new Vector3(polygon[i].x, 0f, polygon[i].y));
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float length = (next - polygon[i]).sqrMagnitude;
                if (length > longest) { longest = length; entrance = (next + polygon[i]) * 0.5f; }
            }
            buildings.Add(polygon); buildingBounds.Add(bounds); entrances.Add(entrance);
        }
    }

    private bool Free(Vector3 position, float radius, float junctionMargin, float entranceMargin)
    {
        Vector2 p = new Vector2(position.x, position.z);
        foreach (Junction j in junctions) if ((p - j.position).sqrMagnitude < Mathf.Pow(j.radius + junctionMargin, 2f)) return false;
        foreach (Vector2 entrance in entrances) if ((p - entrance).sqrMagnitude < entranceMargin * entranceMargin) return false;
        foreach (Occupied item in occupied) if ((p - item.position).sqrMagnitude < Mathf.Pow(item.radius + radius + 0.2f, 2f)) return false;
        for (int i = 0; i < buildings.Count; i++)
        {
            // Reserve the generated facade's projecting architectural volumes too.
            float buildingClearance = radius + 0.8f;
            Bounds bounds = buildingBounds[i]; bounds.Expand(buildingClearance * 2f);
            if (!bounds.Contains(new Vector3(p.x, 0f, p.y))) continue;
            List<Vector2> polygon = buildings[i]; bool inside = false;
            for (int a = 0, b = polygon.Count - 1; a < polygon.Count; b = a++)
            {
                Vector2 from = polygon[b], delta = polygon[a] - from;
                float t = Mathf.Clamp01(Vector2.Dot(p - from, delta) / Mathf.Max(delta.sqrMagnitude, 0.0001f));
                if ((p - from - delta * t).sqrMagnitude < buildingClearance * buildingClearance) return false;
                if ((polygon[a].y > p.y) != (polygon[b].y > p.y) && p.x < (polygon[b].x - polygon[a].x) * (p.y - polygon[a].y) / (polygon[b].y - polygon[a].y) + polygon[a].x) inside = !inside;
            }
            if (inside) return false;
        }
        return true;
    }

    private void Reserve(Vector3 p, float radius) { occupied.Add(new Occupied { position = new Vector2(p.x, p.z), radius = radius }); }
    private static GameObject Pick(GameObject[] prefabs, System.Random random) { return prefabs != null && prefabs.Length > 0 ? prefabs[random.Next(prefabs.Length)] : null; }
    private static Vector3 Geo(OSMMapData map, OSMNode node) { return OSMGeoUtils.GeoToUnity(node.latitude, node.longitude, map.centerLatitude, map.centerLongitude); }

    private static void Sample(List<Vector3> points, float distance, out Vector3 point, out Vector3 tangent)
    {
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 delta = points[i] - points[i - 1]; float length = delta.magnitude;
            if (length < 0.001f) continue;
            if (distance <= length) { point = Vector3.Lerp(points[i - 1], points[i], distance / length); tangent = delta / length; return; }
            distance -= length;
        }
        point = points[points.Count - 1]; tangent = (point - points[points.Count - 2]).normalized;
    }

    private static void Place(GameObject prefab, Transform parent, Vector3 point, Vector3 forward, OSMTerrainElevation elevation, float lift, bool alignSlope)
    {
        point.y = (elevation != null ? elevation.SampleRoadSurfaceHeight(point) : 0.06f) + lift;
        Vector3 up = Vector3.up;
        if (alignSlope && elevation != null)
        {
            float dx = elevation.SampleLocalHeight(point + Vector3.right * 0.5f) - elevation.SampleLocalHeight(point - Vector3.right * 0.5f);
            float dz = elevation.SampleLocalHeight(point + Vector3.forward * 0.5f) - elevation.SampleLocalHeight(point - Vector3.forward * 0.5f);
            up = new Vector3(-dx, 1f, -dz).normalized;
        }
        var instance = Instantiate(prefab, parent, false);
        instance.transform.localPosition = point;
        instance.transform.localRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, up).normalized, up);
    }
}
