using System.Collections.Generic;
using UnityEngine;

public class OSMLandscapePainter : MonoBehaviour
{
    [Header("Terrain layers: grass, soil, paving")]
    public TerrainLayer grass;
    public TerrainLayer soil;
    public TerrainLayer paving;
    [Range(0f, 1f)] public float grassCoverage = 0.78f;
    [Min(2f)] public float patchSize = 18f;
    [Min(0f)] public float pavementShoulder = 0.4f;

    public void Paint(OSMMapData map, Terrain terrain, Transform cityRoot)
    {
        if (map == null || terrain == null || grass == null || soil == null || paving == null) return;
        TerrainData data = terrain.terrainData;
        data.terrainLayers = new[] { grass, soil, paving };
        int size = data.alphamapResolution;
        var weights = new float[size, size, 3];
        var pavement = new float[size, size];
        var park = new float[size, size];
        foreach (OSMWay way in map.ways)
        {
            if (!OSMRoadMetrics.IsRoad(way)) continue;
            var points = Points(map, way);
            float radius = OSMRoadMetrics.GetRoadWidth(way) * 0.5f + pavementShoulder;
            if (OSMRoadMetrics.ShouldGenerateSidewalk(way, true) || OSMRoadMetrics.ShouldGenerateSidewalk(way, false))
                radius += OSMRoadMetrics.GetSidewalkWidth(way);
            for (int i = 1; i < points.Count; i++) StampSegment(points[i - 1], points[i], radius, pavement, terrain, cityRoot);
        }
        // Preserve explicitly mapped parks, gardens and parking areas.
        foreach (OSMWay way in map.ways)
        {
            string leisure = way.GetTag("leisure"), landuse = way.GetTag("landuse");
            bool green = leisure == "park" || leisure == "garden" || landuse == "grass" || landuse == "forest" || way.GetTag("natural") == "wood";
            bool paved = way.GetTag("amenity") == "parking";
            if ((!green && !paved) || way.nodeIds.Count < 4 || way.nodeIds[0] != way.nodeIds[way.nodeIds.Count - 1]) continue;
            var polygon = Points(map, way);
            if (polygon.Count < 3) continue;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                Vector3 point = PixelToCity(x, z, size, terrain, cityRoot);
                if (!Contains(polygon, point)) continue;
                if (paved) pavement[z, x] = 1f; else park[z, x] = 1f;
            }
        }
        for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
        {
            Vector3 p = PixelToCity(x, z, size, terrain, cityRoot);
            float noise = Mathf.PerlinNoise(p.x / patchSize + 71f, p.z / patchSize + 43f);
            float greenery = Mathf.SmoothStep(0.1f, 0.98f, Mathf.Clamp01(noise + grassCoverage - 0.45f));
            greenery = Mathf.Lerp(greenery, 0.97f, park[z, x]);
            float paved = pavement[z, x];
            weights[z, x, 0] = greenery * (1f - paved);
            weights[z, x, 1] = (1f - greenery) * (1f - paved);
            weights[z, x, 2] = paved;
        }
        data.SetAlphamaps(0, 0, weights);
        terrain.Flush();
    }

    private static List<Vector3> Points(OSMMapData map, OSMWay way)
    {
        var points = new List<Vector3>();
        foreach (long id in way.nodeIds)
            if (map.nodes.TryGetValue(id, out OSMNode node)) points.Add(OSMGeoUtils.GeoToUnity(node.latitude, node.longitude, map.centerLatitude, map.centerLongitude));
        return points;
    }

    private static Vector3 PixelToCity(int x, int z, int size, Terrain terrain, Transform root)
    {
        Vector3 local = new Vector3((float)x / (size - 1) * terrain.terrainData.size.x, 0f, (float)z / (size - 1) * terrain.terrainData.size.z);
        return root.InverseTransformPoint(terrain.transform.TransformPoint(local));
    }

    private static void StampSegment(Vector3 a, Vector3 b, float radius, float[,] mask, Terrain terrain, Transform root)
    {
        int size = mask.GetLength(0);
        Vector3 ta = terrain.transform.InverseTransformPoint(root.TransformPoint(a));
        Vector3 tb = terrain.transform.InverseTransformPoint(root.TransformPoint(b));
        float sx = (size - 1) / terrain.terrainData.size.x, sz = (size - 1) / terrain.terrainData.size.z;
        int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(ta.x, tb.x) - radius - 1f) * sx), 0, size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(ta.x, tb.x) + radius + 1f) * sx), 0, size - 1);
        int minZ = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(ta.z, tb.z) - radius - 1f) * sz), 0, size - 1);
        int maxZ = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(ta.z, tb.z) + radius + 1f) * sz), 0, size - 1);
        Vector2 start = new Vector2(a.x, a.z), delta = new Vector2(b.x - a.x, b.z - a.z);
        for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
        {
            Vector3 city = PixelToCity(x, z, size, terrain, root);
            Vector2 point = new Vector2(city.x, city.z);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / Mathf.Max(delta.sqrMagnitude, 0.001f));
            float distance = Vector2.Distance(point, start + delta * t);
            float value = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, radius + 0.8f, distance));
            mask[z, x] = Mathf.Max(mask[z, x], value);
        }
    }

    private static bool Contains(List<Vector3> polygon, Vector3 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector3 a = polygon[i], b = polygon[j];
            if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
        }
        return inside;
    }
}
