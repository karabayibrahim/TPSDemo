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

    // =========================================================
    // QUALITY
    // =========================================================

    /*
     * Yol boyunca vertex aralığı.
     *
     * 0.5 = çok detaylı
     * 0.75 = önerilen
     * 1.0 = daha performanslı
     */
    private const float MaximumSegmentLength =
        0.75f;

    /*
     * Yol genişliğinde kaç parça olacak.
     *
     * 6 =
     *
     * ●--●--●--●--●--●--●
     *
     * yani 7 vertex.
     */
    private const int WidthSegments =
        6;

    private const float MedianHeight =
        0.12f;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

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
        if (map == null ||
            way == null)
        {
            return;
        }

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

        // =====================================================
        // ROAD WIDTH
        // =====================================================

        float roadWidth =
            OSMRoadMetrics.GetRoadWidth(
                way
            );

        // =====================================================
        // SPLIT AT INTERSECTIONS
        // =====================================================

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

            // =================================================
            // DENSE CENTERLINE
            // =================================================

            List<Vector3> dense =
                OSMRoadMetrics.DensifyPolyline(
                    chunk,
                    MaximumSegmentLength
                );

            if (dense.Count < 2)
                continue;

            // =================================================
            // MEDIAN
            // =================================================

            bool median =
                ShouldGenerateMedian(
                    way,
                    roadWidth
                );

            // =================================================
            // MESH
            // =================================================

            Mesh mesh =
                BuildTerrainDrapedRoadMesh(
                    dense,
                    roadWidth,
                    median,
                    OSMRoadMetrics.GetMedianWidth(
                        way
                    )
                );

            if (mesh == null)
                continue;

            // =================================================
            // OBJECT
            // =================================================

            GameObject roadObject =
                new GameObject(
                    $"Road_{way.id}_{chunkIndex}"
                );

            roadObject.transform.SetParent(
                parent,
                false
            );

            MeshFilter filter =
                roadObject.AddComponent<MeshFilter>();

            MeshRenderer renderer =
                roadObject.AddComponent<MeshRenderer>();

            filter.sharedMesh =
                mesh;

            // =================================================
            // MATERIAL
            // =================================================

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

            ApplyRoadMarkingData(mesh, dense, way, roadWidth,
                median ? OSMRoadMetrics.GetMedianWidth(way) : 0f);
            chunkIndex++;
        }
    }

    // =========================================================
    // TERRAIN DRAPED ROAD
    // =========================================================

    private void ApplyRoadMarkingData(Mesh mesh, List<Vector3> points,
        OSMWay way, float width, float medianWidth)
    {
        string highway = way.GetTag("highway").ToLowerInvariant();
        int lanes = Mathf.Min(OSMRoadMetrics.GetLaneCount(way),
            Mathf.Max(1, Mathf.FloorToInt(width / 2.7f)));
        bool enabled = width >= 5.5f && lanes >= 2 && highway != "living_street" &&
            highway != "service" && highway != "footway" && highway != "pedestrian" && highway != "path";
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
        var layout = new List<Vector4>(mesh.vertexCount);
        var limits = new List<Vector4>(mesh.vertexCount);
        int roadVertexCount = points.Count * (WidthSegments + 1);
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            layout.Add(i < roadVertexCount ? new Vector4(width, lanes, medianWidth,
                OSMRoadMetrics.IsOneWay(way) ? 1f : 0f) : Vector4.zero);
            limits.Add(new Vector4(length, enabled && length >= 8f && i < roadVertexCount ? 1f : 0f, 0f, 0f));
        }
        mesh.SetUVs(1, layout);
        mesh.SetUVs(2, limits);
    }

    private Mesh BuildTerrainDrapedRoadMesh(
        List<Vector3> centerLine,
        float roadWidth,
        bool createMedian,
        float medianWidth)
    {
        if (centerLine == null ||
            centerLine.Count < 2)
        {
            return null;
        }

        RoadMeshBuilder builder =
            new RoadMeshBuilder();

        int rowCount =
            centerLine.Count;

        int columnCount =
            WidthSegments + 1;

        float halfWidth =
            roadWidth *
            0.5f;

        float accumulatedDistance =
            0f;

        // =====================================================
        // GRID
        // =====================================================

        for (int row = 0;
             row < rowCount;
             row++)
        {
            if (row > 0)
            {
                accumulatedDistance +=
                    Vector3.Distance(
                        centerLine[row - 1],
                        centerLine[row]
                    );
            }

            Vector3 joinRight =
                OSMRoadMetrics.GetJoinRight(
                    centerLine,
                    row
                );

            // =================================================
            // WIDTH
            // =================================================

            for (int column = 0;
                 column < columnCount;
                 column++)
            {
                float t =
                    column /
                    (float)WidthSegments;

                float lateralOffset =
                    Mathf.Lerp(
                        -halfWidth,
                        halfWidth,
                        t
                    );

                Vector3 vertex =
                    centerLine[row] +
                    joinRight *
                    lateralOffset;

                /*
                 * KRİTİK KISIM
                 *
                 * Her yol vertex'i Terrain'i
                 * bağımsız sample ediyor.
                 */

                vertex.y =
                    GetRoadSurfaceHeight(
                        vertex
                    );

                builder.roadVertices.Add(
                    vertex
                );

                builder.roadUVs.Add(
                    new Vector2(
                        t,
                        accumulatedDistance /
                        4f
                    )
                );
            }
        }

        // =====================================================
        // GRID TRIANGLES
        // =====================================================

        for (int row = 0;
             row < rowCount - 1;
             row++)
        {
            for (int column = 0;
                 column < WidthSegments;
                 column++)
            {
                int a =
                    row *
                    columnCount +
                    column;

                int b =
                    a + 1;

                int c =
                    (
                        row + 1
                    ) *
                    columnCount +
                    column;

                int d =
                    c + 1;

                AddRoadQuad(
                    builder,
                    a,
                    b,
                    d,
                    c
                );
            }
        }

        // =====================================================
        // MEDIAN
        // =====================================================

        if (createMedian)
        {
            AddTerrainDrapedMedian(
                builder,
                centerLine,
                medianWidth
            );
        }

        // =====================================================
        // FINAL MESH
        // =====================================================

        return
            CreateMesh(
                builder
            );
    }

    // =========================================================
    // ROAD QUAD
    // =========================================================

    private void AddRoadQuad(
        RoadMeshBuilder builder,
        int a,
        int b,
        int c,
        int d)
    {
        Vector3 normal =
            Vector3.Cross(
                builder.roadVertices[b] -
                builder.roadVertices[a],

                builder.roadVertices[c] -
                builder.roadVertices[a]
            );

        if (Vector3.Dot(
                normal,
                Vector3.up) >= 0f)
        {
            builder.roadTriangles.Add(a);
            builder.roadTriangles.Add(b);
            builder.roadTriangles.Add(c);

            builder.roadTriangles.Add(a);
            builder.roadTriangles.Add(c);
            builder.roadTriangles.Add(d);
        }
        else
        {
            builder.roadTriangles.Add(a);
            builder.roadTriangles.Add(c);
            builder.roadTriangles.Add(b);

            builder.roadTriangles.Add(a);
            builder.roadTriangles.Add(d);
            builder.roadTriangles.Add(c);
        }
    }

    // =========================================================
    // MEDIAN
    // =========================================================

    private void AddTerrainDrapedMedian(
        RoadMeshBuilder builder,
        List<Vector3> centerLine,
        float medianWidth)
    {
        float halfWidth =
            medianWidth *
            0.5f;

        float distance =
            0f;

        for (int i = 0;
             i < centerLine.Count - 1;
             i++)
        {
            if (i > 0)
            {
                distance +=
                    Vector3.Distance(
                        centerLine[i - 1],
                        centerLine[i]
                    );
            }

            Vector3 right0 =
                OSMRoadMetrics.GetJoinRight(
                    centerLine,
                    i
                );

            Vector3 right1 =
                OSMRoadMetrics.GetJoinRight(
                    centerLine,
                    i + 1
                );

            Vector3 left0 =
                centerLine[i] -
                right0 *
                halfWidth;

            Vector3 rightPoint0 =
                centerLine[i] +
                right0 *
                halfWidth;

            Vector3 left1 =
                centerLine[i + 1] -
                right1 *
                halfWidth;

            Vector3 rightPoint1 =
                centerLine[i + 1] +
                right1 *
                halfWidth;

            // =================================================
            // BOTTOM
            // =================================================

            left0.y =
                GetRoadSurfaceHeight(
                    left0
                );

            rightPoint0.y =
                GetRoadSurfaceHeight(
                    rightPoint0
                );

            left1.y =
                GetRoadSurfaceHeight(
                    left1
                );

            rightPoint1.y =
                GetRoadSurfaceHeight(
                    rightPoint1
                );

            // =================================================
            // TOP
            // =================================================

            Vector3 topLeft0 =
                left0 +
                Vector3.up *
                MedianHeight;

            Vector3 topRight0 =
                rightPoint0 +
                Vector3.up *
                MedianHeight;

            Vector3 topLeft1 =
                left1 +
                Vector3.up *
                MedianHeight;

            Vector3 topRight1 =
                rightPoint1 +
                Vector3.up *
                MedianHeight;

            // Top
            AddMedianQuad(
                builder,
                topLeft0,
                topLeft1,
                topRight1,
                topRight0,
                Vector3.up
            );

            // Left side
            AddMedianQuad(
                builder,
                left0,
                left1,
                topLeft1,
                topLeft0,
                -right0
            );

            // Right side
            AddMedianQuad(
                builder,
                rightPoint1,
                rightPoint0,
                topRight0,
                topRight1,
                right0
            );
        }
    }

    // =========================================================
    // MEDIAN QUAD
    // =========================================================

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
    // ROAD HEIGHT
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
    // BUILD ROAD CHUNKS
    // =========================================================

    private List<List<Vector3>> BuildRoadChunks(
        List<Vector3> points,
        List<long> nodeIds)
    {
        List<List<Vector3>> result =
            new List<List<Vector3>>();

        if (points == null ||
            nodeIds == null ||
            points.Count < 2 ||
            points.Count !=
            nodeIds.Count)
        {
            return result;
        }

        List<Vector3> current =
            new List<Vector3>();

        // =====================================================
        // FIRST
        // =====================================================

        Vector3 first =
            points[0];

        if (IsJunction(
                nodeIds[0]))
        {
            Vector3 direction =
                points[1] -
                points[0];

            direction.y = 0f;

            float length =
                direction.magnitude;

            if (length > 0.01f)
            {
                direction.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork.GetRoadTrimDistance(
                            nodeIds[0]
                        ),
                        length *
                        0.46f
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
        // INTERNAL
        // =====================================================

        for (int i = 1;
             i < points.Count - 1;
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
            // BEFORE JUNCTION
            // =================================================

            Vector3 incoming =
                points[i] -
                points[i - 1];

            incoming.y = 0f;

            float incomingLength =
                incoming.magnitude;

            if (incomingLength >
                0.01f)
            {
                incoming.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork.GetRoadTrimDistance(
                            nodeIds[i]
                        ),
                        incomingLength *
                        0.46f
                    );

                Vector3 endpoint =
                    points[i] -
                    incoming *
                    trim;

                if (current.Count == 0 ||
                    Vector3.Distance(
                        current[
                            current.Count - 1
                        ],
                        endpoint) >
                    0.05f)
                {
                    current.Add(
                        endpoint
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
            // AFTER JUNCTION
            // =================================================

            Vector3 outgoing =
                points[i + 1] -
                points[i];

            outgoing.y = 0f;

            float outgoingLength =
                outgoing.magnitude;

            if (outgoingLength >
                0.01f)
            {
                outgoing.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork.GetRoadTrimDistance(
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

            incoming.y = 0f;

            float length =
                incoming.magnitude;

            if (length > 0.01f)
            {
                incoming.Normalize();

                float trim =
                    Mathf.Min(
                        roadNetwork.GetRoadTrimDistance(
                            nodeIds[last]
                        ),
                        length *
                        0.46f
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
            0.05f)
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

    // =========================================================
    // ADD CHUNK
    // =========================================================

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

        if (way.nodeIds == null)
            return;

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
                    point) < 0.05f)
            {
                continue;
            }

            points.Add(point);
            nodeIds.Add(nodeId);
        }
    }

    // =========================================================
    // JUNCTION
    // =========================================================

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
    // MEDIAN DECISION
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

        if (type == OSMRoadProfileType.Boulevard && roadWidth >= 12f &&
            OSMRoadMetrics.GetLaneCount(way) >= 4)
            return true;

        return false;
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
            "OSM_TerrainDrapedRoad";

        mesh.indexFormat =
            IndexFormat.UInt32;

        OSMRoadSurfaceClearance.Apply(vertices, builder.roadTriangles, elevation);

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

    // =========================================================
    // BUILDER
    // =========================================================

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