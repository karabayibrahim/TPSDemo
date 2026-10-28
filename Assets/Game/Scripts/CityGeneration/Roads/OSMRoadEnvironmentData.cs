using System.Collections.Generic;
using UnityEngine;

public struct OSMRoadSidePlan
{
    public bool generateSidewalk;

    public float sidewalkWidth;

    public float bufferWidth;

    public float buildingScore;
}

public class OSMRoadEnvironmentData
{
    private struct RoadEnvironment
    {
        public float leftBuildingScore;
        public float rightBuildingScore;
    }

    private readonly Dictionary<long, RoadEnvironment>
        roadEnvironments =
            new Dictionary<long, RoadEnvironment>();

    private readonly Dictionary<Vector2Int, List<Vector3>>
        buildingGrid =
            new Dictionary<Vector2Int, List<Vector3>>();

    private const float GridSize =
        40f;

    private const float BuildingSearchRadius =
        32f;

    private const float RoadSampleSpacing =
        12f;

    // =========================================================
    // BUILD
    // =========================================================

    public static OSMRoadEnvironmentData Build(
        OSMMapData map)
    {
        OSMRoadEnvironmentData data =
            new OSMRoadEnvironmentData();

        if (map == null)
            return data;

        data.BuildBuildingIndex(
            map
        );

        foreach (OSMWay way in map.ways)
        {
            if (!OSMRoadMetrics.IsRoad(
                    way))
            {
                continue;
            }

            data.AnalyseRoad(
                map,
                way
            );
        }

        return data;
    }

    // =========================================================
    // BUILDING INDEX
    // =========================================================

    private void BuildBuildingIndex(
        OSMMapData map)
    {
        foreach (OSMWay way in map.ways)
        {
            if (way == null ||
                !way.HasTag("building") ||
                way.nodeIds == null ||
                way.nodeIds.Count < 4)
            {
                continue;
            }

            Vector3 center =
                Vector3.zero;

            int count =
                0;

            int end =
                way.nodeIds.Count;

            if (way.nodeIds[0] ==
                way.nodeIds[
                    way.nodeIds.Count - 1
                ])
            {
                end--;
            }

            for (int i = 0;
                 i < end;
                 i++)
            {
                if (!map.nodes.TryGetValue(
                        way.nodeIds[i],
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

                center += point;

                count++;
            }

            if (count == 0)
                continue;

            center /=
                count;

            AddBuildingToGrid(
                center
            );
        }
    }

    private void AddBuildingToGrid(
        Vector3 position)
    {
        Vector2Int cell =
            GetGridCell(
                position
            );

        if (!buildingGrid.TryGetValue(
                cell,
                out List<Vector3> list))
        {
            list =
                new List<Vector3>();

            buildingGrid.Add(
                cell,
                list
            );
        }

        list.Add(
            position
        );
    }

    // =========================================================
    // ANALYSE ROAD
    // =========================================================

    private void AnalyseRoad(
        OSMMapData map,
        OSMWay way)
    {
        List<Vector3> points =
            GetRoadPoints(
                map,
                way
            );

        if (points.Count < 2)
            return;

        float leftTotal =
            0f;

        float rightTotal =
            0f;

        int sampleCount =
            0;

        for (int segment = 0;
             segment < points.Count - 1;
             segment++)
        {
            Vector3 a =
                points[segment];

            Vector3 b =
                points[segment + 1];

            Vector3 direction =
                b -
                a;

            direction.y = 0f;

            float length =
                direction.magnitude;

            if (length < 0.1f)
                continue;

            direction.Normalize();

            int steps =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        length /
                        RoadSampleSpacing
                    )
                );

            for (int step = 0;
                 step < steps;
                 step++)
            {
                float t =
                    (
                        step +
                        0.5f
                    ) /
                    steps;

                Vector3 sample =
                    Vector3.Lerp(
                        a,
                        b,
                        t
                    );

                GetBuildingInfluence(
                    sample,
                    direction,
                    out float left,
                    out float right
                );

                leftTotal +=
                    left;

                rightTotal +=
                    right;

                sampleCount++;
            }
        }

        if (sampleCount == 0)
            sampleCount = 1;

        RoadEnvironment environment =
            new RoadEnvironment
            {
                leftBuildingScore =
                    Mathf.Clamp01(
                        leftTotal /
                        sampleCount
                    ),

                rightBuildingScore =
                    Mathf.Clamp01(
                        rightTotal /
                        sampleCount
                    )
            };

        roadEnvironments[
            way.id
        ] =
            environment;
    }

    // =========================================================
    // BUILDING INFLUENCE
    // =========================================================

    private void GetBuildingInfluence(
        Vector3 sample,
        Vector3 roadDirection,
        out float leftInfluence,
        out float rightInfluence)
    {
        leftInfluence =
            0f;

        rightInfluence =
            0f;

        Vector2Int centerCell =
            GetGridCell(
                sample
            );

        int cellRadius =
            Mathf.CeilToInt(
                BuildingSearchRadius /
                GridSize
            );

        for (int z = -cellRadius;
             z <= cellRadius;
             z++)
        {
            for (int x = -cellRadius;
                 x <= cellRadius;
                 x++)
            {
                Vector2Int cell =
                    new Vector2Int(
                        centerCell.x + x,
                        centerCell.y + z
                    );

                if (!buildingGrid.TryGetValue(
                        cell,
                        out List<Vector3> buildings))
                {
                    continue;
                }

                foreach (Vector3 building
                         in buildings)
                {
                    Vector3 toBuilding =
                        building -
                        sample;

                    toBuilding.y = 0f;

                    float distance =
                        toBuilding.magnitude;

                    if (distance >
                        BuildingSearchRadius ||
                        distance < 0.1f)
                    {
                        continue;
                    }

                    float influence =
                        1f -
                        distance /
                        BuildingSearchRadius;

                    influence *=
                        influence;

                    float cross =
                        roadDirection.x *
                        toBuilding.z -
                        roadDirection.z *
                        toBuilding.x;

                    if (cross > 0f)
                    {
                        leftInfluence =
                            Mathf.Max(
                                leftInfluence,
                                influence
                            );
                    }
                    else
                    {
                        rightInfluence =
                            Mathf.Max(
                                rightInfluence,
                                influence
                            );
                    }
                }
            }
        }
    }

    // =========================================================
    // SIDE PLAN
    // =========================================================

    public OSMRoadSidePlan GetSidePlan(
        OSMWay way,
        bool leftSide)
    {
        OSMRoadSidePlan plan =
            new OSMRoadSidePlan();

        if (!OSMRoadMetrics.IsSidewalkEligibleRoad(
                way))
        {
            return plan;
        }

        RoadEnvironment environment =
            GetEnvironment(
                way.id
            );

        float score =
            leftSide
                ? environment.leftBuildingScore
                : environment.rightBuildingScore;

        plan.buildingScore =
            score;

        // =====================================================
        // WIDTH
        // =====================================================

        float baseWidth =
            OSMRoadMetrics.GetSidewalkWidth(
                way
            );

        float widthRandom =
            Stable01(
                way.id,
                leftSide
                    ? 711
                    : 919
            );

        plan.sidewalkWidth =
            baseWidth *
            Mathf.Lerp(
                0.85f,
                1.2f,
                widthRandom
            );

        // =====================================================
        // EXPLICIT OSM
        // =====================================================

        if (OSMRoadMetrics.TryGetExplicitSidewalkDecision(
                way,
                leftSide,
                out bool explicitDecision))
        {
            plan.generateSidewalk =
                explicitDecision;

            if (plan.generateSidewalk)
            {
                plan.bufferWidth =
                    CalculateBufferWidth(
                        way,
                        leftSide,
                        score
                    );
            }

            return plan;
        }

        // =====================================================
        // PROCEDURAL
        // =====================================================

        OSMRoadProfileType profile =
            OSMRoadMetrics.GetRoadProfileType(
                way
            );

        float random =
            Stable01(
                way.id,
                leftSide
                    ? 1777
                    : 2777
            );

        switch (profile)
        {
            // =================================================
            // WIDE ROAD
            //
            // Building side gets priority.
            // Empty side usually gets no sidewalk.
            // =================================================

            case OSMRoadProfileType.Boulevard:

                if (score >= 0.15f)
                {
                    plan.generateSidewalk =
                        true;
                }
                else if (score >= 0.07f)
                {
                    plan.generateSidewalk =
                        random < 0.3f;
                }
                else
                {
                    plan.generateSidewalk =
                        false;
                }

                break;

            // =================================================

            case OSMRoadProfileType.Avenue:

                if (score >= 0.13f)
                {
                    plan.generateSidewalk =
                        true;
                }
                else if (score >= 0.06f)
                {
                    plan.generateSidewalk =
                        random < 0.45f;
                }
                else
                {
                    plan.generateSidewalk =
                        random < 0.08f;
                }

                break;

            // =================================================

            case OSMRoadProfileType.Residential:

                if (score >= 0.12f)
                {
                    plan.generateSidewalk =
                        true;
                }
                else if (score >= 0.045f)
                {
                    plan.generateSidewalk =
                        random < 0.72f;
                }
                else
                {
                    plan.generateSidewalk =
                        random < 0.28f;
                }

                break;

            // =================================================

            case OSMRoadProfileType.Service:

                if (score >= 0.16f)
                {
                    plan.generateSidewalk =
                        random < 0.75f;
                }
                else
                {
                    plan.generateSidewalk =
                        random < 0.08f;
                }

                break;

            // =================================================

            case OSMRoadProfileType.Shared:

                /*
                 * Living street çoðu zaman
                 * kaldýrýmsýz/paylaþýmlý olabilir.
                 */

                plan.generateSidewalk =
                    score >= 0.18f &&
                    random < 0.4f;

                break;

            default:

                plan.generateSidewalk =
                    score >= 0.1f ||
                    random < 0.25f;

                break;
        }

        if (plan.generateSidewalk)
        {
            plan.bufferWidth =
                CalculateBufferWidth(
                    way,
                    leftSide,
                    score
                );
        }

        return plan;
    }

    // =========================================================
    // BUFFER / GREEN STRIP
    // =========================================================

    private float CalculateBufferWidth(
        OSMWay way,
        bool leftSide,
        float buildingScore)
    {
        OSMRoadProfileType profile =
            OSMRoadMetrics.GetRoadProfileType(
                way
            );

        if (profile !=
                OSMRoadProfileType.Boulevard &&
            profile !=
                OSMRoadProfileType.Avenue)
        {
            return 0f;
        }

        float roadWidth =
            OSMRoadMetrics.GetRoadWidth(
                way
            );

        if (roadWidth < 7.5f)
            return 0f;

        float random =
            Stable01(
                way.id,
                leftSide
                    ? 3611
                    : 4711
            );

        /*
         * Çok yoðun bina cephesinde kaldýrým genellikle
         * doðrudan yola daha yakýn.
         */

        float chance =
            buildingScore > 0.4f
                ? 0.22f
                : 0.55f;

        if (random >
            chance)
        {
            return 0f;
        }

        float widthRandom =
            Stable01(
                way.id,
                leftSide
                    ? 5101
                    : 6101
            );

        return Mathf.Lerp(
            0.65f,
            2.1f,
            widthRandom
        );
    }

    // =========================================================
    // SCORE
    // =========================================================

    public float GetBuildingScore(
        long wayId,
        bool leftSide)
    {
        RoadEnvironment environment =
            GetEnvironment(
                wayId
            );

        return
            leftSide
                ? environment.leftBuildingScore
                : environment.rightBuildingScore;
    }

    private RoadEnvironment GetEnvironment(
        long wayId)
    {
        if (roadEnvironments.TryGetValue(
                wayId,
                out RoadEnvironment environment))
        {
            return environment;
        }

        return new RoadEnvironment();
    }

    // =========================================================
    // ROAD POINTS
    // =========================================================

    private List<Vector3> GetRoadPoints(
        OSMMapData map,
        OSMWay way)
    {
        List<Vector3> result =
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

            result.Add(
                point
            );
        }

        return result;
    }

    // =========================================================
    // GRID
    // =========================================================

    private Vector2Int GetGridCell(
        Vector3 point)
    {
        return new Vector2Int(
            Mathf.FloorToInt(
                point.x /
                GridSize
            ),
            Mathf.FloorToInt(
                point.z /
                GridSize
            )
        );
    }

    // =========================================================
    // STABLE RANDOM
    // =========================================================

    private float Stable01(
        long id,
        int salt)
    {
        long value =
            id +
            salt;

        int seed =
            unchecked(
                (int)(
                    value ^
                    (value >> 32)
                )
            );

        System.Random random =
            new System.Random(
                seed
            );

        return
            (float)random.NextDouble();
    }
}