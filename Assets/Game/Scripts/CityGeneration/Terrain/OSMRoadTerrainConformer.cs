using System.Collections.Generic;
using UnityEngine;

public class OSMRoadTerrainConformer : MonoBehaviour
{
    [Header("Road Terrain Conforming")]

    [Tooltip("Yol dışında Terrain'in yumuşak geçiş mesafesi.")]
    [SerializeField]
    private float roadBlendMargin =
        4f;

    [Tooltip("Asfaltın iki kenarından Terrain'e ekstra düz alan.")]
    [SerializeField]
    private float roadCoreExtra =
        0.5f;

    [Tooltip("Yol boyu yükseklik profilinin yumuşatılması.")]
    [Range(0, 8)]
    [SerializeField]
    private int longitudinalSmoothIterations =
        4;

    [Tooltip("Terrain'e yol izi atarken örnekleme mesafesi.")]
    [SerializeField]
    private float roadSampleSpacing =
        2f;

    [Header("Junction")]

    [SerializeField]
    private float junctionExtraRadius =
        1.25f;

    [SerializeField]
    private float junctionBlendMargin =
        3f;

    // =========================================================
    // CONFORM
    // =========================================================

    public void Conform(
        OSMMapData map,
        OSMRoadNetworkData roadNetwork,
        Transform cityRoot,
        Terrain terrain)
    {
        if (map == null ||
            terrain == null ||
            terrain.terrainData == null ||
            cityRoot == null)
        {
            return;
        }

        TerrainData data =
            terrain.terrainData;

        int resolution =
            data.heightmapResolution;

        float[,] heights =
            data.GetHeights(
                0,
                0,
                resolution,
                resolution
            );

        // =====================================================
        // ROADS
        // =====================================================

        // Extend flat foundations beyond building footprints so narrow gaps
        // between facades and sidewalks cannot retain procedural terrain humps.
        foreach (OSMWay building in map.ways)
        {
            if (!building.HasTag("building")) continue;
            List<Vector3> footprint = GetWayPoints(map, building);
            if (footprint.Count < 3) continue;
            float baseHeight = float.MaxValue;
            foreach (Vector3 point in footprint)
                baseHeight = Mathf.Min(baseHeight, SampleHeightMeters(heights, point, cityRoot, terrain));
            List<Vector3> perimeter = OSMRoadMetrics.DensifyPolyline(footprint, 1.5f);
            foreach (Vector3 point in perimeter)
                StampHeight(heights, point, baseHeight, 2.5f, 4.5f, cityRoot, terrain);
        }

        foreach (OSMWay way
                 in map.ways)
        {
            if (!OSMRoadMetrics.IsRoad(
                    way))
            {
                continue;
            }

            List<Vector3> originalPoints =
                GetWayPoints(
                    map,
                    way
                );

            if (originalPoints.Count < 2)
                continue;

            List<Vector3> points =
                OSMRoadMetrics.DensifyPolyline(
                    originalPoints,
                    roadSampleSpacing
                );

            if (points.Count < 2)
                continue;

            float halfRoadWidth =
                OSMRoadMetrics.GetRoadWidth(
                    way
                ) *
                0.5f;

            float coreRadius =
                halfRoadWidth +
                Mathf.Max(roadCoreExtra,
                    OSMRoadMetrics.ShouldGenerateSidewalk(way, true) || OSMRoadMetrics.ShouldGenerateSidewalk(way, false)
                        ? OSMRoadMetrics.GetSidewalkWidth(way) + 0.75f : roadCoreExtra);

            float outerRadius =
                coreRadius +
                roadBlendMargin;

            // =================================================
            // GET ORIGINAL LONGITUDINAL PROFILE
            // =================================================

            float[] profile =
                new float[
                    points.Count
                ];

            for (int i = 0;
                 i < points.Count;
                 i++)
            {
                profile[i] =
                    SampleHeightMeters(
                        heights,
                        points[i],
                        cityRoot,
                        terrain
                    );
            }

            // =================================================
            // SMOOTH PROFILE
            // =================================================

            for (int iteration = 0;
                 iteration <
                 longitudinalSmoothIterations;
                 iteration++)
            {
                float[] next =
                    new float[
                        profile.Length
                    ];

                for (int i = 0;
                     i < profile.Length;
                     i++)
                {
                    float total =
                        profile[i];

                    float weight =
                        1f;

                    if (i > 0)
                    {
                        total +=
                            profile[i - 1];

                        weight +=
                            1f;
                    }

                    if (i <
                        profile.Length - 1)
                    {
                        total +=
                            profile[i + 1];

                        weight +=
                            1f;
                    }

                    if (i > 1)
                    {
                        total +=
                            profile[i - 2] *
                            0.5f;

                        weight +=
                            0.5f;
                    }

                    if (i <
                        profile.Length - 2)
                    {
                        total +=
                            profile[i + 2] *
                            0.5f;

                        weight +=
                            0.5f;
                    }

                    next[i] =
                        total /
                        weight;
                }

                profile =
                    next;
            }

            // =================================================
            // STAMP ROAD CORRIDOR
            // =================================================

            for (int i = 0;
                 i < points.Count;
                 i++)
            {
                StampHeight(
                    heights,
                    points[i],
                    profile[i],
                    coreRadius,
                    outerRadius,
                    cityRoot,
                    terrain
                );
            }
        }

        // =====================================================
        // JUNCTIONS
        // =====================================================

        if (roadNetwork != null)
        {
            foreach (long nodeId
                     in roadNetwork
                         .GetJunctionNodeIds())
            {
                if (!map.nodes.TryGetValue(
                        nodeId,
                        out OSMNode node))
                {
                    continue;
                }

                Vector3 localCenter =
                    OSMGeoUtils.GeoToUnity(
                        node.latitude,
                        node.longitude,
                        map.centerLatitude,
                        map.centerLongitude
                    );

                localCenter.y = 0f;

                float targetHeight =
                    SampleHeightMeters(
                        heights,
                        localCenter,
                        cityRoot,
                        terrain
                    );

                float coreRadius =
                    roadNetwork.GetJunctionRadius(
                        nodeId
                    ) +
                    junctionExtraRadius;

                float outerRadius =
                    coreRadius +
                    junctionBlendMargin;

                StampHeight(
                    heights,
                    localCenter,
                    targetHeight,
                    coreRadius,
                    outerRadius,
                    cityRoot,
                    terrain
                );
            }
        }

        // =====================================================
        // APPLY
        // =====================================================

        data.SetHeights(
            0,
            0,
            heights
        );

        terrain.Flush();
    }

    // =========================================================
    // STAMP
    // =========================================================

    private void StampHeight(
        float[,] heights,
        Vector3 cityLocalPoint,
        float targetMeters,
        float coreRadius,
        float outerRadius,
        Transform cityRoot,
        Terrain terrain)
    {
        TerrainData data =
            terrain.terrainData;

        int resolution =
            data.heightmapResolution;

        Vector3 worldPoint =
            cityRoot.TransformPoint(
                cityLocalPoint
            );

        Vector3 terrainLocal =
            terrain.transform
                .InverseTransformPoint(
                    worldPoint
                );

        float pixelX =
            terrainLocal.x /
            data.size.x *
            (
                resolution - 1
            );

        float pixelZ =
            terrainLocal.z /
            data.size.z *
            (
                resolution - 1
            );

        float metersPerPixelX =
            data.size.x /
            (
                resolution - 1
            );

        float metersPerPixelZ =
            data.size.z /
            (
                resolution - 1
            );

        int radiusX =
            Mathf.CeilToInt(
                outerRadius /
                metersPerPixelX
            );

        int radiusZ =
            Mathf.CeilToInt(
                outerRadius /
                metersPerPixelZ
            );

        int centerX =
            Mathf.RoundToInt(
                pixelX
            );

        int centerZ =
            Mathf.RoundToInt(
                pixelZ
            );

        float targetNormalized =
            Mathf.Clamp01(
                targetMeters /
                data.size.y
            );

        for (int z =
                 centerZ - radiusZ;
             z <=
                 centerZ + radiusZ;
             z++)
        {
            if (z < 0 ||
                z >= resolution)
            {
                continue;
            }

            for (int x =
                     centerX - radiusX;
                 x <=
                     centerX + radiusX;
                 x++)
            {
                if (x < 0 ||
                    x >= resolution)
                {
                    continue;
                }

                float worldDX =
                    (
                        x -
                        pixelX
                    ) *
                    metersPerPixelX;

                float worldDZ =
                    (
                        z -
                        pixelZ
                    ) *
                    metersPerPixelZ;

                float distance =
                    Mathf.Sqrt(
                        worldDX *
                        worldDX +
                        worldDZ *
                        worldDZ
                    );

                if (distance >
                    outerRadius)
                {
                    continue;
                }

                float strength;

                if (distance <=
                    coreRadius)
                {
                    strength =
                        1f;
                }
                else
                {
                    float t =
                        Mathf.InverseLerp(
                            outerRadius,
                            coreRadius,
                            distance
                        );

                    strength =
                        t *
                        t *
                        (
                            3f -
                            2f *
                            t
                        );
                }

                heights[z, x] =
                    Mathf.Lerp(
                        heights[z, x],
                        targetNormalized,
                        strength
                    );
            }
        }
    }

    // =========================================================
    // SAMPLE
    // =========================================================

    private float SampleHeightMeters(
        float[,] heights,
        Vector3 cityLocalPoint,
        Transform cityRoot,
        Terrain terrain)
    {
        TerrainData data =
            terrain.terrainData;

        int resolution =
            data.heightmapResolution;

        Vector3 worldPoint =
            cityRoot.TransformPoint(
                cityLocalPoint
            );

        Vector3 local =
            terrain.transform
                .InverseTransformPoint(
                    worldPoint
                );

        float px =
            Mathf.Clamp(
                local.x /
                data.size.x *
                (
                    resolution - 1
                ),
                0f,
                resolution - 1
            );

        float pz =
            Mathf.Clamp(
                local.z /
                data.size.z *
                (
                    resolution - 1
                ),
                0f,
                resolution - 1
            );

        int x0 =
            Mathf.FloorToInt(px);

        int z0 =
            Mathf.FloorToInt(pz);

        int x1 =
            Mathf.Min(
                x0 + 1,
                resolution - 1
            );

        int z1 =
            Mathf.Min(
                z0 + 1,
                resolution - 1
            );

        float tx =
            px - x0;

        float tz =
            pz - z0;

        float h00 =
            heights[z0, x0];

        float h10 =
            heights[z0, x1];

        float h01 =
            heights[z1, x0];

        float h11 =
            heights[z1, x1];

        float h0 =
            Mathf.Lerp(
                h00,
                h10,
                tx
            );

        float h1 =
            Mathf.Lerp(
                h01,
                h11,
                tx
            );

        float normalized =
            Mathf.Lerp(
                h0,
                h1,
                tz
            );

        return
            normalized *
            data.size.y;
    }

    // =========================================================
    // WAY POINTS
    // =========================================================

    private List<Vector3> GetWayPoints(
        OSMMapData map,
        OSMWay way)
    {
        List<Vector3> points =
            new List<Vector3>();

        foreach (long nodeId
                 in way.nodeIds)
        {
            if (!map.nodes.TryGetValue(
                    nodeId,
                    out OSMNode node))
            {
                continue;
            }

            Vector3 point =
                OSMGeoUtils.GeoToUnity(
                    node.latitude,
                    node.longitude,
                    map.centerLatitude,
                    map.centerLongitude
                );

            point.y = 0f;

            if (points.Count == 0 ||
                Vector3.Distance(
                    points[
                        points.Count - 1
                    ],
                    point) >
                0.05f)
            {
                points.Add(
                    point
                );
            }
        }

        return points;
    }
}