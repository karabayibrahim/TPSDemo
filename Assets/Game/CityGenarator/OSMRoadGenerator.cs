using System.Collections.Generic;
using UnityEngine;

public class OSMRoadGenerator
{
    private Transform parent;
    private Material roadMaterial;

    public OSMRoadGenerator(
        Transform parent,
        Material material)
    {
        this.parent = parent;
        this.roadMaterial = material;
    }

    public void Generate(
        OSMMapData map,
        OSMWay way)
    {
        if (!way.HasTag("highway"))
            return;

        List<Vector3> points =
            new List<Vector3>();

        foreach (long nodeId in way.nodeIds)
        {
            if (!map.nodes.TryGetValue(
                    nodeId,
                    out OSMNode node))
                continue;

            points.Add(
                OSMGeoUtils.GeoToUnity(
                    node.latitude,
                    node.longitude,
                    map.centerLatitude,
                    map.centerLongitude
                )
            );
        }

        if (points.Count < 2)
            return;

        float width =
            GetRoadWidth(way);

        CreateRoadMesh(
            points,
            width,
            way.id
        );
    }

    private float GetRoadWidth(OSMWay way)
    {
        string highway =
            way.GetTag("highway");

        switch (highway)
        {
            case "motorway":
                return 12f;

            case "trunk":
                return 10f;

            case "primary":
                return 9f;

            case "secondary":
                return 8f;

            case "tertiary":
                return 7f;

            case "residential":
                return 6f;

            case "service":
                return 4f;

            case "living_street":
                return 5f;

            default:
                return 4f;
        }
    }

    private void CreateRoadMesh(
        List<Vector3> points,
        float width,
        long id)
    {
        GameObject road =
            new GameObject(
                "Road_" + id
            );

        road.transform.SetParent(
            parent,
            false
        );

        Mesh mesh = new Mesh();

        List<Vector3> vertices =
            new List<Vector3>();

        List<int> triangles =
            new List<int>();

        List<Vector2> uvs =
            new List<Vector2>();

        float uvDistance = 0f;

        for (int i = 0;
             i < points.Count;
             i++)
        {
            Vector3 forward;

            if (i == 0)
            {
                forward =
                    points[1] -
                    points[0];
            }
            else if (
                i == points.Count - 1)
            {
                forward =
                    points[i] -
                    points[i - 1];
            }
            else
            {
                forward =
                    points[i + 1] -
                    points[i - 1];
            }

            forward.Normalize();

            Vector3 right =
                Vector3.Cross(
                    Vector3.up,
                    forward
                ).normalized;

            Vector3 leftPoint =
                points[i]
                - right * width * 0.5f;

            Vector3 rightPoint =
                points[i]
                + right * width * 0.5f;

            // Yol zeminin biraz üzerinde.
            leftPoint.y = 0.02f;
            rightPoint.y = 0.02f;

            vertices.Add(leftPoint);
            vertices.Add(rightPoint);

            if (i > 0)
            {
                uvDistance +=
                    Vector3.Distance(
                        points[i - 1],
                        points[i]
                    );
            }

            uvs.Add(
                new Vector2(
                    0,
                    uvDistance / 5f
                )
            );

            uvs.Add(
                new Vector2(
                    1,
                    uvDistance / 5f
                )
            );
        }

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            int index = i * 2;

            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);

            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter =
            road.AddComponent<MeshFilter>();

        MeshRenderer renderer =
            road.AddComponent<MeshRenderer>();

        filter.sharedMesh = mesh;

        if (roadMaterial != null)
        {
            renderer.sharedMaterial =
                roadMaterial;
        }
    }
}