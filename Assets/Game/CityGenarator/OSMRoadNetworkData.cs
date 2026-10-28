using System.Collections.Generic;
using UnityEngine;

public class OSMRoadNetworkData
{
    public class RoadConnection
    {
        public long wayId;
        public long neighborNodeId;

        public float halfWidth;

        public OSMWay way;
    }

    private readonly Dictionary<long, List<RoadConnection>>
        connections =
            new Dictionary<long, List<RoadConnection>>();

    private readonly Dictionary<long, float>
        maximumHalfRoadWidth =
            new Dictionary<long, float>();

    // =========================================================
    // BUILD
    // =========================================================

    public static OSMRoadNetworkData Build(
        OSMMapData map)
    {
        OSMRoadNetworkData result =
            new OSMRoadNetworkData();

        if (map == null)
            return result;

        foreach (OSMWay way in map.ways)
        {
            if (!OSMRoadMetrics.IsRoad(way))
                continue;

            if (way.nodeIds == null ||
                way.nodeIds.Count < 2)
            {
                continue;
            }

            float halfWidth =
                OSMRoadMetrics.GetRoadWidth(way) *
                0.5f;

            for (int i = 0;
                 i < way.nodeIds.Count - 1;
                 i++)
            {
                long a =
                    way.nodeIds[i];

                long b =
                    way.nodeIds[i + 1];

                if (a == b)
                    continue;

                result.AddConnection(
                    a,
                    b,
                    way,
                    halfWidth
                );

                result.AddConnection(
                    b,
                    a,
                    way,
                    halfWidth
                );
            }
        }

        return result;
    }

    private void AddConnection(
        long nodeId,
        long neighborNodeId,
        OSMWay way,
        float halfWidth)
    {
        if (!connections.TryGetValue(
                nodeId,
                out List<RoadConnection> list))
        {
            list =
                new List<RoadConnection>();

            connections.Add(
                nodeId,
                list
            );
        }

        list.Add(
            new RoadConnection
            {
                wayId = way.id,
                neighborNodeId = neighborNodeId,
                halfWidth = halfWidth,
                way = way
            }
        );

        if (!maximumHalfRoadWidth.TryGetValue(
                nodeId,
                out float oldWidth))
        {
            maximumHalfRoadWidth[nodeId] =
                halfWidth;
        }
        else
        {
            maximumHalfRoadWidth[nodeId] =
                Mathf.Max(
                    oldWidth,
                    halfWidth
                );
        }
    }

    // =========================================================
    // JUNCTION
    // =========================================================

    public bool IsJunction(
        long nodeId)
    {
        return
            connections.TryGetValue(
                nodeId,
                out List<RoadConnection> list
            ) &&
            list.Count > 2;
    }

    public IEnumerable<long> GetJunctionNodeIds()
    {
        foreach (
            KeyValuePair<
                long,
                List<RoadConnection>
            > pair
            in connections)
        {
            if (pair.Value.Count > 2)
            {
                yield return pair.Key;
            }
        }
    }

    public IReadOnlyList<RoadConnection>
        GetConnections(long nodeId)
    {
        if (connections.TryGetValue(
                nodeId,
                out List<RoadConnection> list))
        {
            return list;
        }

        return
            System.Array.Empty<RoadConnection>();
    }

    // =========================================================
    // WIDTH
    // =========================================================

    public float GetRoadHalfWidth(
        long nodeId)
    {
        if (maximumHalfRoadWidth.TryGetValue(
                nodeId,
                out float width))
        {
            return width;
        }

        return 3f;
    }

    // =========================================================
    // COMMON JUNCTION RADIUS
    //
    // Road, intersection ve sidewalk ayný deðeri kullanýyor.
    // =========================================================

    public float GetJunctionRadius(
        long nodeId)
    {
        float halfWidth =
            GetRoadHalfWidth(
                nodeId
            );

        return
            halfWidth * 1.45f +
            0.75f;
    }

    // =========================================================
    // ROAD TRIM
    // =========================================================

    public float GetRoadTrimDistance(
        long nodeId)
    {
        return
            GetJunctionRadius(
                nodeId
            );
    }

    // =========================================================
    // SIDEWALK TRIM
    // =========================================================

    public float GetJunctionTrimDistance(
        long nodeId,
        float totalSideWidth)
    {
        float radius =
            GetJunctionRadius(
                nodeId
            );

        /*
         * Sidewalk intersection asfaltýnýn
         * biraz dýþýnda biter.
         */

        return
            radius +
            Mathf.Clamp(
                totalSideWidth * 0.20f,
                0.20f,
                0.75f
            );
    }
}