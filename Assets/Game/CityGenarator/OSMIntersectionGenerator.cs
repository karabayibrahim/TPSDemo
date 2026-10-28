using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class OSMIntersectionGenerator
{
    private readonly Transform parent;

    private readonly Material material;

    private readonly OSMTerrainElevation elevation;

    private readonly OSMRoadNetworkData roadNetwork;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public OSMIntersectionGenerator(
        Transform parent,
        Material material,
        OSMTerrainElevation elevation,
        OSMRoadNetworkData roadNetwork)
    {
        this.parent =
            parent;

        this.material =
            material;

        this.elevation =
            elevation;

        this.roadNetwork =
            roadNetwork;
    }

    // =========================================================
    // GENERATE
    // =========================================================

    public void Generate(
        OSMMapData map)
    {
        if (map == null ||
            roadNetwork == null)
        {
            return;
        }

        foreach (long nodeId
                 in roadNetwork.GetJunctionNodeIds())
        {
            GenerateIntersection(
                map,
                nodeId
            );
        }
    }

    // =========================================================
    // ONE INTERSECTION
    // =========================================================

    private void GenerateIntersection(
        OSMMapData map,
        long nodeId)
    {
        if (!map.nodes.TryGetValue(
                nodeId,
                out OSMNode centerNode))
        {
            return;
        }

        IReadOnlyList<
            OSMRoadNetworkData.RoadConnection
        > connections =
            roadNetwork.GetConnections(
                nodeId
            );

        if (connections == null ||
            connections.Count < 3)
        {
            return;
        }

        Vector3 center =
            OSMGeoUtils.GeoToUnity(
                centerNode.latitude,
                centerNode.longitude,
                map.centerLatitude,
                map.centerLongitude
            );

        center.y = 0f;

        float radius =
            roadNetwork.GetRoadTrimDistance(
                nodeId
            );

        List<IntersectionPoint> boundary =
            new List<IntersectionPoint>();

        // =====================================================
        // EXACT ROAD MOUTHS
        // =====================================================

        foreach (
            OSMRoadNetworkData.RoadConnection connection
            in connections)
        {
            if (!map.nodes.TryGetValue(
                    connection.neighborNodeId,
                    out OSMNode neighborNode))
            {
                continue;
            }

            Vector3 neighbor =
                OSMGeoUtils.GeoToUnity(
                    neighborNode.latitude,
                    neighborNode.longitude,
                    map.centerLatitude,
                    map.centerLongitude
                );

            neighbor.y = 0f;

            Vector3 direction =
                neighbor -
                center;

            direction.y = 0f;

            if (direction.sqrMagnitude <
                0.0001f)
            {
                continue;
            }

            direction.Normalize();

            Vector3 right =
                OSMRoadMetrics.GetRight(
                    direction
                );

            Vector3 roadEndCenter =
                center +
                direction *
                radius;

            Vector3 left =
                roadEndCenter -
                right *
                connection.halfWidth;

            Vector3 rightPoint =
                roadEndCenter +
                right *
                connection.halfWidth;

            AddBoundaryPoint(
                boundary,
                center,
                left
            );

            AddBoundaryPoint(
                boundary,
                center,
                rightPoint
            );
        }

        if (boundary.Count < 3)
            return;

        // =====================================================
        // SORT AROUND CENTER
        //
        // Bütün road-mouth vertexlerini koruyoruz.
        // =====================================================

        boundary.Sort(
            delegate (
                IntersectionPoint a,
                IntersectionPoint b)
            {
                return
                    a.angle.CompareTo(
                        b.angle
                    );
            }
        );

        RemoveDuplicatePoints(
            boundary
        );

        if (boundary.Count < 3)
            return;

        Mesh mesh =
            BuildIntersectionMesh(
                center,
                boundary
            );

        if (mesh == null)
            return;

        GameObject obj =
            new GameObject(
                $"Intersection_{nodeId}"
            );

        obj.transform.SetParent(
            parent,
            false
        );

        MeshFilter filter =
            obj.AddComponent<MeshFilter>();

        MeshRenderer renderer =
            obj.AddComponent<MeshRenderer>();

        filter.sharedMesh =
            mesh;

        renderer.sharedMaterial =
            material;
    }

    // =========================================================
    // ADD BOUNDARY
    // =========================================================

    private void AddBoundaryPoint(
        List<IntersectionPoint> list,
        Vector3 center,
        Vector3 point)
    {
        Vector3 relative =
            point -
            center;

        float angle =
            Mathf.Atan2(
                relative.z,
                relative.x
            );

        list.Add(
            new IntersectionPoint
            {
                position =
                    point,

                angle =
                    angle
            }
        );
    }

    // =========================================================
    // REMOVE DUPLICATES
    // =========================================================

    private void RemoveDuplicatePoints(
        List<IntersectionPoint> points)
    {
        for (int i =
                 points.Count - 1;
             i >= 0;
             i--)
        {
            for (int j = 0;
                 j < i;
                 j++)
            {
                if (Vector3.Distance(
                        points[i].position,
                        points[j].position
                    ) <
                    0.04f)
                {
                    points.RemoveAt(i);

                    break;
                }
            }
        }
    }

    // =========================================================
    // BUILD MESH
    // =========================================================

    private Mesh BuildIntersectionMesh(
        Vector3 center,
        List<IntersectionPoint> boundary)
    {
        List<Vector3> vertices =
            new List<Vector3>();

        List<Vector2> uvs =
            new List<Vector2>();

        List<int> triangles =
            new List<int>();

        // =====================================================
        // CENTER
        // =====================================================

        Vector3 centerVertex =
            center;

        centerVertex.y =
            GetRoadHeight(
                center
            );

        vertices.Add(
            centerVertex
        );

        uvs.Add(
            GetRoadUV(
                centerVertex
            )
        );

        // =====================================================
        // BOUNDARY
        //
        // EXACT same Terrain road-surface function.
        // =====================================================

        foreach (IntersectionPoint point
                 in boundary)
        {
            Vector3 vertex =
                point.position;

            vertex.y =
                GetRoadHeight(
                    vertex
                );

            vertices.Add(
                vertex
            );

            uvs.Add(
                GetRoadUV(
                    vertex
                )
            );
        }

        // =====================================================
        // TRIANGLE FAN
        // =====================================================

        for (int i = 0;
             i < boundary.Count;
             i++)
        {
            int current =
                i + 1;

            int next =
                (
                    (i + 1) %
                    boundary.Count
                ) +
                1;

            AddTriangleUp(
                vertices,
                triangles,
                0,
                current,
                next
            );
        }

        Mesh mesh =
            new Mesh();

        mesh.name =
            "OSM_Intersection_Merged";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            vertices
        );

        mesh.SetUVs(
            0,
            uvs
        );

        mesh.SetTriangles(
            triangles,
            0
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // =========================================================
    // ROAD HEIGHT
    // =========================================================

    private float GetRoadHeight(
        Vector3 point)
    {
        if (elevation == null)
        {
            return 0.06f;
        }

        return
            elevation.SampleRoadSurfaceHeight(
                point
            );
    }

    // =========================================================
    // UV
    // =========================================================

    private Vector2 GetRoadUV(
        Vector3 point)
    {
        return new Vector2(
            point.x / 4f,
            point.z / 4f
        );
    }

    // =========================================================
    // TRIANGLE UP
    // =========================================================

    private void AddTriangleUp(
        List<Vector3> vertices,
        List<int> triangles,
        int a,
        int b,
        int c)
    {
        Vector3 normal =
            Vector3.Cross(
                vertices[b] -
                vertices[a],

                vertices[c] -
                vertices[a]
            );

        if (Vector3.Dot(
                normal,
                Vector3.up) >= 0f)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
        else
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }
    }

    // =========================================================
    // DATA
    // =========================================================

    private struct IntersectionPoint
    {
        public Vector3 position;

        public float angle;
    }
}