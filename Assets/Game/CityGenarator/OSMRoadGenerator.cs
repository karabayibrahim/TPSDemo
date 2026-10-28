using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class OSMRoadGenerator
{
    private readonly Transform parent;

    private readonly Material roadMaterial;

    private readonly Material[] medianMaterials;

    private readonly OSMTerrainElevation elevation;

    private readonly OSMRoadNetworkData roadNetwork;

    private const float MaximumSegmentLength =
        1f;

    private const float MedianHeight =
        0.12f;

    public OSMRoadGenerator(
        Transform parent,
        Material roadMaterial,
        Material[] medianMaterials,
        OSMTerrainElevation elevation,
        OSMRoadNetworkData roadNetwork)
    {
        this.parent =
            parent;

        this.roadMaterial =
            roadMaterial;

        this.medianMaterials =
            medianMaterials;

        this.elevation =
            elevation;

        this.roadNetwork =
            roadNetwork;
    }

    // =========================================================
    // GENERATE
    // =========================================================

    public void Generate(
        OSMMapData map,
        OSMWay way)
    {
        if (!OSMRoadMetrics.IsRoad(
                way))
        {
            return;
        }

        GetRoadData(
            map,
            way,
            out List<Vector3> originalPoints,
            out List<long> nodeIds
        );

        if (originalPoints.Count < 2)
            return;

        float roadWidth =
            OSMRoadMetrics.GetRoadWidth(
                way
            );

        List<List<Vector3>> chunks =
            BuildRoadChunks(
                originalPoints,
                nodeIds
            );

        int chunkIndex =
            0;

        foreach (List<Vector3> chunk
                 in chunks)
        {
            if (chunk == null ||
                chunk.Count < 2)
            {
                continue;
            }

            List<Vector3> dense =
                OSMRoadMetrics.DensifyPolyline(
                    chunk,
                    MaximumSegmentLength
                );

            if (dense.Count < 2)
                continue;

            bool median =
                ShouldGenerateMedian(
                    way,
                    roadWidth
                );

            Mesh mesh =
                BuildRoadMesh(
                    dense,
                    roadWidth,
                    median,
                    OSMRoadMetrics.GetMedianWidth(
                        way
                    )
                );

            if (mesh == null)
                continue;

            GameObject road =
                new GameObject(
                    $"Road_{way.id}_{chunkIndex}"
                );

            road.transform.SetParent(
                parent,
                false
            );

            MeshFilter filter =
                road.AddComponent<MeshFilter>();

            MeshRenderer renderer =
                road.AddComponent<MeshRenderer>();

            filter.sharedMesh =
                mesh;

            Material medianMaterial =
                GetStableMaterial(
                    medianMaterials,
                    way.id
                );

            if (medianMaterial == null)
            {
                medianMaterial =
                    roadMaterial;
            }

            renderer.sharedMaterials =
                new[]
                {
                    roadMaterial,
                    medianMaterial
                };

            chunkIndex++;
        }
    }

    // =========================================================
    // ROAD CHUNKS
    // =========================================================

    private List<List<Vector3>> BuildRoadChunks(
        List<Vector3> points,
        List<long> nodeIds)
    {
        List<List<Vector3>> result =
            new List<List<Vector3>>();

        if (points.Count < 2 ||
            points.Count != nodeIds.Count)
        {
            return result;
        }

        List<Vector3> current =
            new List<Vector3>();

        // =====================================================
        // START
        // =====================================================

        Vector3 first =
            points[0];

        if (IsJunction(
                nodeIds[0]))
        {
            Vector3 direction =
                points[1] -
                points[0];

            float length =
                direction.magnitude;

            if (length > 0.01f)
            {
                direction.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork
                            .GetRoadTrimDistance(
                                nodeIds[0]
                            ),
                        length * 0.46f
                    );

                first +=
                    direction *
                    trim;
            }
        }

        current.Add(
            first
        );

        // =====================================================
        // INTERNAL POINTS
        // =====================================================

        for (int i = 1;
             i <
             points.Count - 1;
             i++)
        {
            if (!IsJunction(
                    nodeIds[i]))
            {
                current.Add(
                    points[i]
                );

                continue;
            }

            // =================================================
            // END BEFORE JUNCTION
            // =================================================

            Vector3 incoming =
                points[i] -
                points[i - 1];

            float incomingLength =
                incoming.magnitude;

            if (incomingLength >
                0.01f)
            {
                incoming.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork
                            .GetRoadTrimDistance(
                                nodeIds[i]
                            ),
                        incomingLength *
                        0.46f
                    );

                Vector3 end =
                    points[i] -
                    incoming *
                    trim;

                if (Vector3.Distance(
                        current[
                            current.Count - 1
                        ],
                        end) >
                    0.1f)
                {
                    current.Add(
                        end
                    );
                }
            }

            AddChunk(
                result,
                current
            );

            current =
                new List<Vector3>();

            // =================================================
            // START AFTER JUNCTION
            // =================================================

            Vector3 outgoing =
                points[i + 1] -
                points[i];

            float outgoingLength =
                outgoing.magnitude;

            if (outgoingLength >
                0.01f)
            {
                outgoing.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork
                            .GetRoadTrimDistance(
                                nodeIds[i]
                            ),
                        outgoingLength *
                        0.46f
                    );

                current.Add(
                    points[i] +
                    outgoing *
                    trim
                );
            }
        }

        // =====================================================
        // LAST
        // =====================================================

        int last =
            points.Count - 1;

        Vector3 lastPoint =
            points[last];

        if (IsJunction(
                nodeIds[last]))
        {
            Vector3 incoming =
                points[last] -
                points[last - 1];

            float length =
                incoming.magnitude;

            if (length > 0.01f)
            {
                incoming.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork
                            .GetRoadTrimDistance(
                                nodeIds[last]
                            ),
                        length * 0.46f
                    );

                lastPoint -=
                    incoming *
                    trim;
            }
        }

        if (current.Count == 0 ||
            Vector3.Distance(
                current[
                    current.Count - 1
                ],
                lastPoint) >
            0.1f)
        {
            current.Add(
                lastPoint
            );
        }

        AddChunk(
            result,
            current
        );

        return result;
    }

    private void AddChunk(
        List<List<Vector3>> result,
        List<Vector3> chunk)
    {
        if (chunk == null ||
            chunk.Count < 2)
        {
            return;
        }

        float length =
            0f;

        for (int i = 0;
             i < chunk.Count - 1;
             i++)
        {
            length +=
                Vector3.Distance(
                    chunk[i],
                    chunk[i + 1]
                );
        }

        if (length < 0.5f)
            return;

        result.Add(
            new List<Vector3>(
                chunk
            )
        );
    }

    // =========================================================
    // BUILD ROAD MESH
    // =========================================================

    private Mesh BuildRoadMesh(
        List<Vector3> points,
        float roadWidth,
        bool createMedian,
        float medianWidth)
    {
        int count =
            points.Count;

        if (count < 2)
            return null;

        float halfWidth =
            roadWidth *
            0.5f;

        RoadMeshBuilder builder =
            new RoadMeshBuilder();

        float accumulatedDistance =
            0f;

        float[] centerHeights =
            new float[count];

        // =====================================================
        // ROAD VERTICES
        //
        // Önemli:
        // artýk MAX terrain height yok.
        // Sol ve sað vertex Terrain'den birebir okunuyor.
        // =====================================================

        for (int i = 0;
             i < count;
             i++)
        {
            if (i > 0)
            {
                accumulatedDistance +=
                    Vector3.Distance(
                        points[i - 1],
                        points[i]
                    );
            }

            Vector3 joinRight =
                OSMRoadMetrics.GetJoinRight(
                    points,
                    i
                );

            Vector3 left =
                points[i] -
                joinRight *
                halfWidth;

            Vector3 right =
                points[i] +
                joinRight *
                halfWidth;

            left.y =
                GetRoadSurfaceHeight(
                    left
                );

            right.y =
                GetRoadSurfaceHeight(
                    right
                );

            centerHeights[i] =
                GetRoadSurfaceHeight(
                    points[i]
                );

            builder.roadVertices.Add(
                left
            );

            builder.roadVertices.Add(
                right
            );

            builder.roadUVs.Add(
                new Vector2(
                    0f,
                    accumulatedDistance /
                    4f
                )
            );

            builder.roadUVs.Add(
                new Vector2(
                    1f,
                    accumulatedDistance /
                    4f
                )
            );
        }

        // =====================================================
        // ROAD TRIANGLES
        // =====================================================

        for (int i = 0;
             i < count - 1;
             i++)
        {
            int a =
                i * 2;

            int b =
                a + 1;

            int c =
                (i + 1) * 2;

            int d =
                c + 1;

            builder.roadTriangles.Add(a);
            builder.roadTriangles.Add(c);
            builder.roadTriangles.Add(b);

            builder.roadTriangles.Add(b);
            builder.roadTriangles.Add(c);
            builder.roadTriangles.Add(d);
        }

        // =====================================================
        // MEDIAN
        // =====================================================

        if (createMedian)
        {
            AddMedian(
                builder,
                points,
                centerHeights,
                medianWidth
            );
        }

        return
            CreateMesh(
                builder
            );
    }

    // =========================================================
    // HEIGHT
    // =========================================================

    private float GetRoadSurfaceHeight(
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
    // MEDIAN
    // =========================================================

    private void AddMedian(
        RoadMeshBuilder builder,
        List<Vector3> points,
        float[] centerHeights,
        float width)
    {
        float halfWidth =
            width *
            0.5f;

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            Vector3 right0 =
                OSMRoadMetrics.GetJoinRight(
                    points,
                    i
                );

            Vector3 right1 =
                OSMRoadMetrics.GetJoinRight(
                    points,
                    i + 1
                );

            Vector3 lb0 =
                points[i] -
                right0 *
                halfWidth;

            Vector3 rb0 =
                points[i] +
                right0 *
                halfWidth;

            Vector3 lb1 =
                points[i + 1] -
                right1 *
                halfWidth;

            Vector3 rb1 =
                points[i + 1] +
                right1 *
                halfWidth;

            lb0.y =
                centerHeights[i];

            rb0.y =
                centerHeights[i];

            lb1.y =
                centerHeights[i + 1];

            rb1.y =
                centerHeights[i + 1];

            Vector3 lt0 =
                lb0 +
                Vector3.up *
                MedianHeight;

            Vector3 rt0 =
                rb0 +
                Vector3.up *
                MedianHeight;

            Vector3 lt1 =
                lb1 +
                Vector3.up *
                MedianHeight;

            Vector3 rt1 =
                rb1 +
                Vector3.up *
                MedianHeight;

            AddMedianQuad(
                builder,
                lt0,
                lt1,
                rt1,
                rt0,
                Vector3.up
            );

            AddMedianQuad(
                builder,
                lb0,
                lb1,
                lt1,
                lt0,
                -right0
            );

            AddMedianQuad(
                builder,
                rb1,
                rb0,
                rt0,
                rt1,
                right0
            );
        }
    }

    private void AddMedianQuad(
        RoadMeshBuilder builder,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 expectedNormal)
    {
        int start =
            builder.medianVertices.Count;

        builder.medianVertices.Add(a);
        builder.medianVertices.Add(b);
        builder.medianVertices.Add(c);
        builder.medianVertices.Add(d);

        builder.medianUVs.Add(
            new Vector2(0f, 0f)
        );

        builder.medianUVs.Add(
            new Vector2(0f, 1f)
        );

        builder.medianUVs.Add(
            new Vector2(1f, 1f)
        );

        builder.medianUVs.Add(
            new Vector2(1f, 0f)
        );

        Vector3 normal =
            Vector3.Cross(
                b - a,
                c - a
            );

        if (Vector3.Dot(
                normal,
                expectedNormal) >= 0f)
        {
            builder.medianTriangles.Add(start);
            builder.medianTriangles.Add(start + 1);
            builder.medianTriangles.Add(start + 2);

            builder.medianTriangles.Add(start);
            builder.medianTriangles.Add(start + 2);
            builder.medianTriangles.Add(start + 3);
        }
        else
        {
            builder.medianTriangles.Add(start);
            builder.medianTriangles.Add(start + 2);
            builder.medianTriangles.Add(start + 1);

            builder.medianTriangles.Add(start);
            builder.medianTriangles.Add(start + 3);
            builder.medianTriangles.Add(start + 2);
        }
    }

    // =========================================================
    // FINAL MESH
    // =========================================================

    private Mesh CreateMesh(
        RoadMeshBuilder builder)
    {
        List<Vector3> vertices =
            new List<Vector3>();

        List<Vector2> uvs =
            new List<Vector2>();

        vertices.AddRange(
            builder.roadVertices
        );

        uvs.AddRange(
            builder.roadUVs
        );

        int medianOffset =
            vertices.Count;

        vertices.AddRange(
            builder.medianVertices
        );

        uvs.AddRange(
            builder.medianUVs
        );

        List<int> medianTriangles =
            new List<int>();

        foreach (int index
                 in builder.medianTriangles)
        {
            medianTriangles.Add(
                index +
                medianOffset
            );
        }

        Mesh mesh =
            new Mesh();

        mesh.name =
            "OSM_Road";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            vertices
        );

        mesh.SetUVs(
            0,
            uvs
        );

        mesh.subMeshCount =
            2;

        mesh.SetTriangles(
            builder.roadTriangles,
            0,
            false
        );

        mesh.SetTriangles(
            medianTriangles,
            1,
            false
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // =========================================================
    // ROAD DATA
    // =========================================================

    private void GetRoadData(
        OSMMapData map,
        OSMWay way,
        out List<Vector3> points,
        out List<long> nodeIds)
    {
        points =
            new List<Vector3>();

        nodeIds =
            new List<long>();

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

            if (points.Count > 0 &&
                Vector3.Distance(
                    points[
                        points.Count - 1
                    ],
                    point) <
                0.05f)
            {
                continue;
            }

            points.Add(
                point
            );

            nodeIds.Add(
                nodeId
            );
        }
    }

    private bool IsJunction(
        long nodeId)
    {
        return
            roadNetwork != null &&
            roadNetwork.IsJunction(
                nodeId
            );
    }

    // =========================================================
    // MEDIAN
    // =========================================================

    private bool ShouldGenerateMedian(
        OSMWay way,
        float roadWidth)
    {
        if (OSMRoadMetrics.IsOneWay(
                way))
        {
            return false;
        }

        OSMRoadProfileType type =
            OSMRoadMetrics.GetRoadProfileType(
                way
            );

        float random =
            Stable01(
                way.id +
                8181
            );

        if (type ==
                OSMRoadProfileType.Boulevard &&
            roadWidth >= 9f)
        {
            return
                random < 0.75f;
        }

        return false;
    }

    // =========================================================
    // MATERIAL
    // =========================================================

    private Material GetStableMaterial(
        Material[] materials,
        long id)
    {
        if (materials == null ||
            materials.Length == 0)
        {
            return null;
        }

        List<Material> valid =
            new List<Material>();

        foreach (Material material
                 in materials)
        {
            if (material != null)
            {
                valid.Add(
                    material
                );
            }
        }

        if (valid.Count == 0)
            return null;

        int seed =
            unchecked(
                (int)(
                    id ^
                    (id >> 32)
                )
            );

        System.Random random =
            new System.Random(
                seed
            );

        return
            valid[
                random.Next(
                    valid.Count
                )
            ];
    }

    private float Stable01(
        long value)
    {
        int seed =
            unchecked(
                (int)(
                    value ^
                    (value >> 32)
                )
            );

        return
            (float)new System.Random(
                seed
            ).NextDouble();
    }

    private class RoadMeshBuilder
    {
        public readonly List<Vector3>
            roadVertices =
                new List<Vector3>();

        public readonly List<Vector2>
            roadUVs =
                new List<Vector2>();

        public readonly List<int>
            roadTriangles =
                new List<int>();

        public readonly List<Vector3>
            medianVertices =
                new List<Vector3>();

        public readonly List<Vector2>
            medianUVs =
                new List<Vector2>();

        public readonly List<int>
            medianTriangles =
                new List<int>();
    }
}