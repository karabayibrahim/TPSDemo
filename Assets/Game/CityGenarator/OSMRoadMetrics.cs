using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum OSMRoadProfileType
{
    Local,
    Residential,
    Avenue,
    Boulevard,
    Service,
    Shared
}

public static class OSMRoadMetrics
{
    // =========================================================
    // ROAD CHECK
    // =========================================================

    public static bool IsRoad(OSMWay way)
    {
        return
            way != null &&
            way.HasTag("highway") &&
            !string.IsNullOrWhiteSpace(
                way.GetTag("highway")
            );
    }

    // =========================================================
    // PROFILE
    // =========================================================

    public static OSMRoadProfileType GetRoadProfileType(
        OSMWay way)
    {
        string highway =
            way.GetTag("highway")
                .ToLowerInvariant();

        switch (highway)
        {
            case "motorway":
            case "trunk":
            case "primary":
                return OSMRoadProfileType.Boulevard;

            case "secondary":
            case "tertiary":
                return OSMRoadProfileType.Avenue;

            case "residential":
            case "unclassified":
                return OSMRoadProfileType.Residential;

            case "service":
                return OSMRoadProfileType.Service;

            case "living_street":
                return OSMRoadProfileType.Shared;

            default:
                return OSMRoadProfileType.Local;
        }
    }

    // =========================================================
    // ROAD WIDTH
    // =========================================================

    public static float GetRoadWidth(OSMWay way)
    {
        string explicitWidth =
            way.GetTag("width");

        if (TryParseMeters(
                explicitWidth,
                out float width))
        {
            return Mathf.Clamp(
                width,
                2.5f,
                30f
            );
        }

        string lanes =
            way.GetTag("lanes");

        if (int.TryParse(
                lanes,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int laneCount))
        {
            if (laneCount > 0)
            {
                return Mathf.Clamp(
                    laneCount * 3.1f,
                    3.2f,
                    24f
                );
            }
        }

        string highway =
            way.GetTag("highway")
                .ToLowerInvariant();

        switch (highway)
        {
            case "motorway":
                return 12f;

            case "motorway_link":
                return 7f;

            case "trunk":
                return 10f;

            case "trunk_link":
                return 7f;

            case "primary":
                return 9f;

            case "primary_link":
                return 6.5f;

            case "secondary":
                return 8f;

            case "secondary_link":
                return 6f;

            case "tertiary":
                return 7f;

            case "tertiary_link":
                return 5.5f;

            case "residential":
                return 6f;

            case "living_street":
                return 5f;

            case "service":
                return 4f;

            case "unclassified":
                return 5.5f;

            default:
                return 4.5f;
        }
    }

    // =========================================================
    // SIDEWALK BASE WIDTH
    // =========================================================

    public static float GetSidewalkWidth(OSMWay way)
    {
        string highway =
            way.GetTag("highway")
                .ToLowerInvariant();

        switch (highway)
        {
            case "trunk":
                return 2.8f;

            case "primary":
                return 2.6f;

            case "secondary":
                return 2.2f;

            case "tertiary":
                return 1.9f;

            case "residential":
                return 1.65f;

            case "living_street":
                return 1.25f;

            case "service":
                return 1.2f;

            case "unclassified":
                return 1.5f;

            default:
                return 1.4f;
        }
    }

    // =========================================================
    // CURB
    // =========================================================

    public static float GetCurbHeight(OSMWay way)
    {
        string highway =
            way.GetTag("highway")
                .ToLowerInvariant();

        switch (highway)
        {
            case "living_street":
                return 0.07f;

            case "service":
                return 0.1f;

            default:
                return 0.15f;
        }
    }

    // =========================================================
    // SIDEWALK ELIGIBLE
    // =========================================================

    public static bool IsSidewalkEligibleRoad(
        OSMWay way)
    {
        if (!IsRoad(way))
            return false;

        string highway =
            way.GetTag("highway")
                .ToLowerInvariant();

        switch (highway)
        {
            case "motorway":
            case "motorway_link":
            case "trunk_link":
            case "footway":
            case "path":
            case "cycleway":
            case "steps":
            case "track":
            case "pedestrian":
                return false;
        }

        return true;
    }

    // =========================================================
    // EXPLICIT OSM SIDEWALK
    // =========================================================

    public static bool TryGetExplicitSidewalkDecision(
        OSMWay way,
        bool leftSide,
        out bool generate)
    {
        generate = false;

        string sideTag =
            way.GetTag(
                leftSide
                    ? "sidewalk:left"
                    : "sidewalk:right"
            );

        if (!string.IsNullOrWhiteSpace(sideTag))
        {
            sideTag =
                sideTag
                    .Trim()
                    .ToLowerInvariant();

            if (sideTag == "yes")
            {
                generate = true;
                return true;
            }

            if (sideTag == "no" ||
                sideTag == "none" ||
                sideTag == "separate")
            {
                generate = false;
                return true;
            }
        }

        string sidewalk =
            way.GetTag("sidewalk");

        if (string.IsNullOrWhiteSpace(sidewalk))
            return false;

        sidewalk =
            sidewalk
                .Trim()
                .ToLowerInvariant();

        switch (sidewalk)
        {
            case "yes":
            case "both":

                generate = true;
                return true;

            case "left":

                generate =
                    leftSide;

                return true;

            case "right":

                generate =
                    !leftSide;

                return true;

            case "no":
            case "none":
            case "separate":

                generate = false;
                return true;
        }

        return false;
    }

    // =========================================================
    // FALLBACK
    // =========================================================

    public static bool ShouldGenerateSidewalk(
        OSMWay way,
        bool leftSide)
    {
        if (!IsSidewalkEligibleRoad(way))
            return false;

        if (TryGetExplicitSidewalkDecision(
                way,
                leftSide,
                out bool explicitDecision))
        {
            return explicitDecision;
        }

        return true;
    }

    // =========================================================
    // ONEWAY
    // =========================================================

    public static bool IsOneWay(OSMWay way)
    {
        string value =
            way.GetTag("oneway")
                .Trim()
                .ToLowerInvariant();

        return
            value == "yes" ||
            value == "1" ||
            value == "true" ||
            value == "-1";
    }

    // =========================================================
    // MEDIAN
    // =========================================================

    public static float GetMedianWidth(OSMWay way)
    {
        float roadWidth =
            GetRoadWidth(way);

        return Mathf.Clamp(
            roadWidth * 0.14f,
            1f,
            2.4f
        );
    }

    // =========================================================
    // DENSIFY
    // =========================================================

    public static List<Vector3> DensifyPolyline(
        List<Vector3> source,
        float maximumSegmentLength = 1.5f)
    {
        List<Vector3> result =
            new List<Vector3>();

        if (source == null ||
            source.Count == 0)
        {
            return result;
        }

        maximumSegmentLength =
            Mathf.Max(
                0.25f,
                maximumSegmentLength
            );

        result.Add(
            source[0]
        );

        for (int i = 0;
             i < source.Count - 1;
             i++)
        {
            Vector3 start =
                source[i];

            Vector3 end =
                source[i + 1];

            float distance =
                Vector3.Distance(
                    start,
                    end
                );

            if (distance < 0.01f)
                continue;

            int pieces =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        distance /
                        maximumSegmentLength
                    )
                );

            for (int step = 1;
                 step <= pieces;
                 step++)
            {
                float t =
                    step /
                    (float)pieces;

                result.Add(
                    Vector3.Lerp(
                        start,
                        end,
                        t
                    )
                );
            }
        }

        return result;
    }

    // =========================================================
    // ROAD JOIN
    // =========================================================

    public static Vector3 GetJoinRight(
        List<Vector3> points,
        int index)
    {
        if (points == null ||
            points.Count < 2)
        {
            return Vector3.right;
        }

        if (index <= 0)
        {
            return GetRight(
                points[1] -
                points[0]
            );
        }

        if (index >=
            points.Count - 1)
        {
            return GetRight(
                points[
                    points.Count - 1
                ] -
                points[
                    points.Count - 2
                ]
            );
        }

        Vector3 previousDirection =
            points[index] -
            points[index - 1];

        Vector3 nextDirection =
            points[index + 1] -
            points[index];

        previousDirection.y = 0f;
        nextDirection.y = 0f;

        if (previousDirection.sqrMagnitude <
            0.0001f)
        {
            previousDirection =
                nextDirection;
        }

        if (nextDirection.sqrMagnitude <
            0.0001f)
        {
            nextDirection =
                previousDirection;
        }

        previousDirection.Normalize();
        nextDirection.Normalize();

        Vector3 previousRight =
            GetRight(
                previousDirection
            );

        Vector3 nextRight =
            GetRight(
                nextDirection
            );

        Vector3 miter =
            previousRight +
            nextRight;

        if (miter.sqrMagnitude <
            0.001f)
        {
            return nextRight;
        }

        miter.Normalize();

        float denominator =
            Mathf.Abs(
                Vector3.Dot(
                    miter,
                    nextRight
                )
            );

        denominator =
            Mathf.Max(
                denominator,
                0.35f
            );

        float multiplier =
            Mathf.Min(
                1f / denominator,
                2.5f
            );

        return
            miter *
            multiplier;
    }

    public static Vector3 GetRight(
        Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <
            0.0001f)
        {
            return Vector3.right;
        }

        direction.Normalize();

        return new Vector3(
            direction.z,
            0f,
            -direction.x
        );
    }

    private static bool TryParseMeters(
        string value,
        out float meters)
    {
        meters = 0f;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string cleaned =
            value
                .ToLowerInvariant()
                .Replace("meters", "")
                .Replace("meter", "")
                .Replace("metres", "")
                .Replace("metre", "")
                .Replace("m", "")
                .Trim();

        return float.TryParse(
            cleaned,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out meters
        );
    }
}