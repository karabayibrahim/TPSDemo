using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class OSMSidewalkGenerator
{
    // =========================================================
    // REFERENCES
    // =========================================================

    private readonly Transform parent;

    private readonly Material[] sidewalkMaterials;
    private readonly Material[] curbMaterials;
    private readonly Material[] bufferMaterials;

    private readonly OSMTerrainElevation elevation;

    private readonly OSMRoadNetworkData roadNetwork;

    private readonly OSMRoadEnvironmentData environment;


    // =========================================================
    // SETTINGS
    // =========================================================

    private const float MaximumSegmentLength = 1f;

    /*
     * Kavşak yanında çok küçük üçgen/parça kaldırım
     * oluşmasını engeller.
     */
    private const float MinimumChunkLength = 3.5f;

    /*
     * Yol ile bordür arasında çok küçük güvenli boşluk.
     */
    private const float RoadToCurbGap = 0.005f;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public OSMSidewalkGenerator(
        Transform parent,
        Material[] sidewalkMaterials,
        Material[] curbMaterials,
        Material[] bufferMaterials,
        OSMTerrainElevation elevation,
        OSMRoadNetworkData roadNetwork,
        OSMRoadEnvironmentData environment)
    {
        this.parent =
            parent;

        this.sidewalkMaterials =
            sidewalkMaterials;

        this.curbMaterials =
            curbMaterials;

        this.bufferMaterials =
            bufferMaterials;

        this.elevation =
            elevation;

        this.roadNetwork =
            roadNetwork;

        this.environment =
            environment;
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

        // =====================================================
        // SIDE DECISIONS
        // =====================================================

        OSMRoadSidePlan leftPlan =
            GetPlan(
                way,
                true
            );

        OSMRoadSidePlan rightPlan =
            GetPlan(
                way,
                false
            );

        if (!leftPlan.generateSidewalk &&
            !rightPlan.generateSidewalk)
        {
            return;
        }

        // =====================================================
        // ROAD POINTS
        // =====================================================

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

        float curbHeight =
            OSMRoadMetrics.GetCurbHeight(
                way
            );

        SidewalkMeshBuilder builder =
            new SidewalkMeshBuilder();


        // =====================================================
        // LEFT SIDE
        // =====================================================

        if (leftPlan.generateSidewalk)
        {
            GenerateSide(
                builder,
                originalPoints,
                nodeIds,
                roadWidth,
                curbHeight,
                leftPlan,
                -1f
            );
        }


        // =====================================================
        // RIGHT SIDE
        // =====================================================

        if (rightPlan.generateSidewalk)
        {
            GenerateSide(
                builder,
                originalPoints,
                nodeIds,
                roadWidth,
                curbHeight,
                rightPlan,
                1f
            );
        }


        // =====================================================
        // NOTHING GENERATED
        // =====================================================

        if (builder.vertices.Count == 0)
            return;


        // =====================================================
        // CREATE MESH
        // =====================================================

        Mesh mesh =
            new Mesh();

        mesh.name =
            $"OSM_Sidewalk_{way.id}";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            builder.vertices
        );

        mesh.SetUVs(
            0,
            builder.uvs
        );

        /*
         * 0 = sidewalk
         * 1 = curb
         * 2 = grass/buffer
         */

        mesh.subMeshCount =
            3;

        mesh.SetTriangles(
            builder.sidewalkTriangles,
            0,
            false
        );

        mesh.SetTriangles(
            builder.curbTriangles,
            1,
            false
        );

        mesh.SetTriangles(
            builder.bufferTriangles,
            2,
            false
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();


        // =====================================================
        // CREATE OBJECT
        // =====================================================

        GameObject sidewalkObject =
            new GameObject(
                $"Sidewalk_{way.id}"
            );

        sidewalkObject.transform.SetParent(
            parent,
            false
        );

        MeshFilter filter =
            sidewalkObject
                .AddComponent<MeshFilter>();

        MeshRenderer renderer =
            sidewalkObject
                .AddComponent<MeshRenderer>();

        filter.sharedMesh =
            mesh;


        // =====================================================
        // MATERIALS
        // =====================================================

        Material sidewalkMaterial =
            GetStableMaterial(
                sidewalkMaterials,
                way.id,
                1001
            );

        Material curbMaterial =
            GetStableMaterial(
                curbMaterials,
                way.id,
                2001
            );

        Material bufferMaterial =
            GetStableMaterial(
                bufferMaterials,
                way.id,
                3001
            );

        if (curbMaterial == null)
        {
            curbMaterial =
                sidewalkMaterial;
        }

        if (bufferMaterial == null)
        {
            bufferMaterial =
                sidewalkMaterial;
        }

        renderer.sharedMaterials =
            new[]
            {
                sidewalkMaterial,
                curbMaterial,
                bufferMaterial
            };
    }


    // =========================================================
    // GENERATE SINGLE SIDE
    // =========================================================

    private void GenerateSide(
        SidewalkMeshBuilder builder,
        List<Vector3> originalPoints,
        List<long> nodeIds,
        float roadWidth,
        float curbHeight,
        OSMRoadSidePlan plan,
        float sideSign)
    {
        float sidewalkWidth =
            Mathf.Max(
                0.8f,
                plan.sidewalkWidth
            );

        float bufferWidth =
            Mathf.Max(
                0f,
                plan.bufferWidth
            );

        float totalSideWidth =
            sidewalkWidth +
            bufferWidth;

        // =====================================================
        // CUT AT JUNCTIONS
        // =====================================================

        List<List<Vector3>> chunks =
            BuildSidewalkChunks(
                originalPoints,
                nodeIds,
                totalSideWidth
            );

        foreach (List<Vector3> chunk
                 in chunks)
        {
            if (chunk == null ||
                chunk.Count < 2)
            {
                continue;
            }

            float originalLength =
                GetPolylineLength(
                    chunk
                );

            if (originalLength <
                GetMinimumChunkLength(
                    totalSideWidth
                ))
            {
                continue;
            }

            // =================================================
            // DENSIFY
            //
            // Terrain'i daha iyi takip eder.
            // =================================================

            List<Vector3> dense =
                OSMRoadMetrics
                    .DensifyPolyline(
                        chunk,
                        MaximumSegmentLength
                    );

            if (dense.Count < 2)
                continue;

            if (GetPolylineLength(dense) <
                GetMinimumChunkLength(
                    totalSideWidth
                ))
            {
                continue;
            }

            BuildSideMesh(
                builder,
                dense,
                roadWidth,
                sidewalkWidth,
                bufferWidth,
                curbHeight,
                sideSign
            );
        }
    }


    // =========================================================
    // SIDE PLAN
    // =========================================================

    private OSMRoadSidePlan GetPlan(
        OSMWay way,
        bool leftSide)
    {
        if (environment != null)
        {
            return
                environment.GetSidePlan(
                    way,
                    leftSide
                );
        }

        return new OSMRoadSidePlan
        {
            generateSidewalk =
                OSMRoadMetrics
                    .ShouldGenerateSidewalk(
                        way,
                        leftSide
                    ),

            sidewalkWidth =
                OSMRoadMetrics
                    .GetSidewalkWidth(
                        way
                    ),

            bufferWidth =
                0f,

            buildingScore =
                0f
        };
    }


    // =========================================================
    // BUILD SIDE MESH
    // =========================================================

    private void BuildSideMesh(
        SidewalkMeshBuilder builder,
        List<Vector3> points,
        float roadWidth,
        float sidewalkWidth,
        float bufferWidth,
        float curbHeight,
        float sideSign)
    {
        if (points == null ||
            points.Count < 2)
        {
            return;
        }

        int count =
            points.Count;

        float roadHalfWidth =
            roadWidth *
            0.5f;

        /*
         * Road
         *
         * | asphalt |
         *            <- curbOffset
         *
         * [curb]
         * [buffer optional]
         * [sidewalk]
         */

        float curbOffset =
            roadHalfWidth +
            RoadToCurbGap;

        float sidewalkInnerOffset =
            curbOffset +
            bufferWidth;

        float sidewalkOuterOffset =
            sidewalkInnerOffset +
            sidewalkWidth;


        // =====================================================
        // ARRAYS
        // =====================================================

        Vector3[] curbPoints =
            new Vector3[count];

        Vector3[] sidewalkInnerPoints =
            new Vector3[count];

        Vector3[] sidewalkOuterPoints =
            new Vector3[count];


        // =====================================================
        // CREATE XZ POSITIONS
        // =====================================================

        for (int i = 0;
             i < count;
             i++)
        {
            Vector3 right =
                OSMRoadMetrics.GetJoinRight(
                    points,
                    i
                );

            Vector3 curb =
                points[i] +
                right *
                curbOffset *
                sideSign;

            Vector3 sidewalkInner =
                points[i] +
                right *
                sidewalkInnerOffset *
                sideSign;

            Vector3 sidewalkOuter =
                points[i] +
                right *
                sidewalkOuterOffset *
                sideSign;

            curb.y =
                0f;

            sidewalkInner.y =
                0f;

            sidewalkOuter.y =
                0f;

            curbPoints[i] =
                curb;

            sidewalkInnerPoints[i] =
                sidewalkInner;

            sidewalkOuterPoints[i] =
                sidewalkOuter;
        }


        // =====================================================
        // GENERATE SEGMENTS
        // =====================================================

        float accumulatedDistance =
            0f;

        for (int i = 0;
             i < count - 1;
             i++)
        {
            float segmentLength =
                Vector3.Distance(
                    points[i],
                    points[i + 1]
                );

            if (segmentLength < 0.05f)
                continue;


            // =================================================
            // TERRAIN HEIGHTS
            //
            // Kritik fark:
            // kaldırım tek düz profile göre değil,
            // her kenarda Terrain'i ayrı takip ediyor.
            // =================================================

            float roadEdgeTerrain0 =
                SampleTerrain(
                    curbPoints[i]
                );

            float roadEdgeTerrain1 =
                SampleTerrain(
                    curbPoints[i + 1]
                );

            float innerTerrain0 =
                SampleTerrain(
                    sidewalkInnerPoints[i]
                );

            float innerTerrain1 =
                SampleTerrain(
                    sidewalkInnerPoints[i + 1]
                );

            float outerTerrain0 =
                SampleTerrain(
                    sidewalkOuterPoints[i]
                );

            float outerTerrain1 =
                SampleTerrain(
                    sidewalkOuterPoints[i + 1]
                );


            // =================================================
            // CURB HEIGHT
            //
            // Yol terrain conformer tarafından Terrain'e
            // oturtulduğu için burada Terrain bazlı gidiyoruz.
            // =================================================

            float roadSurfaceOffset =
                elevation != null
                    ? Mathf.Max(
                        0.025f,
                        elevation.RoadSurfaceOffset
                    )
                    : 0.025f;


            float curbBaseY0 =
    elevation != null
        ? elevation.SampleRoadSurfaceHeight(
            curbPoints[i]
        )
        : 0.06f;

            float curbBaseY1 =
                elevation != null
                    ? elevation.SampleRoadSurfaceHeight(
                        curbPoints[i + 1]
                    )
                    : 0.06f;


            float curbTopY0 =
                curbBaseY0 +
                curbHeight;

            float curbTopY1 =
                curbBaseY1 +
                curbHeight;


            // =================================================
            // WALK HEIGHTS
            //
            // Terrain'i takip etsin ama bordür üstünden
            // aşağıya düşmesin.
            // =================================================

            float innerTopY0 =
                Mathf.Max(
                    innerTerrain0 +
                    curbHeight,
                    curbTopY0
                );

            float innerTopY1 =
                Mathf.Max(
                    innerTerrain1 +
                    curbHeight,
                    curbTopY1
                );


            float outerTopY0 =
                outerTerrain0 +
                curbHeight;

            float outerTopY1 =
                outerTerrain1 +
                curbHeight;


            /*
             * Çok kısa mesafede büyük yan eğim oluşmasın.
             * Kaldırımın yol tarafına yakın yüksekliğe
             * biraz yaklaşmasını sağlıyor.
             */

            // Limit crossfall to a comfortable drainage slope.
            outerTopY0 = Mathf.Clamp(outerTopY0, innerTopY0 - sidewalkWidth * 0.02f,
                innerTopY0 + sidewalkWidth * 0.025f);
            outerTopY1 = Mathf.Clamp(outerTopY1, innerTopY1 - sidewalkWidth * 0.02f,
                innerTopY1 + sidewalkWidth * 0.025f);

            // =================================================
            // SIDEWALK TOP
            // =================================================

            Vector3 walkInner0 =
                sidewalkInnerPoints[i];

            Vector3 walkInner1 =
                sidewalkInnerPoints[i + 1];

            Vector3 walkOuter0 =
                sidewalkOuterPoints[i];

            Vector3 walkOuter1 =
                sidewalkOuterPoints[i + 1];

            walkInner0.y =
                innerTopY0;

            walkInner1.y =
                innerTopY1;

            walkOuter0.y =
                outerTopY0;

            walkOuter1.y =
                outerTopY1;


            AddQuadWithUV(
                builder,
                builder.sidewalkTriangles,

                walkInner0,
                walkInner1,
                walkOuter1,
                walkOuter0,

                Vector3.up,

                accumulatedDistance,
                accumulatedDistance +
                segmentLength,

                sidewalkWidth
            );


            // =================================================
            // BUFFER / GRASS STRIP
            // =================================================

            if (bufferWidth >
                0.05f)
            {
                Vector3 bufferRoad0 =
                    curbPoints[i];

                Vector3 bufferRoad1 =
                    curbPoints[i + 1];

                Vector3 bufferWalk0 =
                    sidewalkInnerPoints[i];

                Vector3 bufferWalk1 =
                    sidewalkInnerPoints[i + 1];

                bufferRoad0.y =
                    curbTopY0;

                bufferRoad1.y =
                    curbTopY1;

                bufferWalk0.y =
                    innerTopY0;

                bufferWalk1.y =
                    innerTopY1;

                AddQuadWithUV(
                    builder,
                    builder.bufferTriangles,

                    bufferRoad0,
                    bufferRoad1,
                    bufferWalk1,
                    bufferWalk0,

                    Vector3.up,

                    accumulatedDistance,
                    accumulatedDistance +
                    segmentLength,

                    bufferWidth
                );
            }


            // =================================================
            // INNER CURB FACE
            // =================================================

            Vector3 curbBottom0 =
                curbPoints[i];

            Vector3 curbBottom1 =
                curbPoints[i + 1];

            Vector3 curbTop0 =
                curbPoints[i];

            Vector3 curbTop1 =
                curbPoints[i + 1];

            curbBottom0.y =
                curbBaseY0;

            curbBottom1.y =
                curbBaseY1;

            curbTop0.y =
                curbTopY0;

            curbTop1.y =
                curbTopY1;


            Vector3 segmentDirection =
                points[i + 1] -
                points[i];

            segmentDirection.y =
                0f;

            Vector3 segmentRight =
                OSMRoadMetrics.GetRight(
                    segmentDirection
                );

            Vector3 towardRoad =
                -segmentRight *
                sideSign;


            AddQuadSimple(
                builder,
                builder.curbTriangles,

                curbBottom0,
                curbTop0,
                curbTop1,
                curbBottom1,

                towardRoad
            );


            // =================================================
            // OUTER SIDE FACE
            //
            // Terrain ile kaldırım arasındaki boşluğu kapatır.
            // =================================================

            Vector3 outerBottom0 =
                sidewalkOuterPoints[i];

            Vector3 outerBottom1 =
                sidewalkOuterPoints[i + 1];

            outerBottom0.y =
                outerTerrain0;

            outerBottom1.y =
                outerTerrain1;


            Vector3 awayFromRoad =
                segmentRight *
                sideSign;


            AddQuadSimple(
                builder,
                builder.curbTriangles,

                outerBottom1,
                walkOuter1,
                walkOuter0,
                outerBottom0,

                awayFromRoad
            );


            // Close the underside and both exposed ends of every sidewalk chunk.
            AddQuadSimple(builder, builder.curbTriangles,
                curbBottom0, curbBottom1, outerBottom1, outerBottom0, Vector3.down);
            if (i == 0)
                AddSidewalkEndCap(builder, curbTop0, walkInner0, walkOuter0,
                    curbBottom0, outerBottom0, -segmentDirection.normalized);
            if (i == count - 2)
                AddSidewalkEndCap(builder, curbTop1, walkInner1, walkOuter1,
                    curbBottom1, outerBottom1, segmentDirection.normalized);

            accumulatedDistance +=
                segmentLength;
        }
    }


    // =========================================================
    // JUNCTION CHUNKS
    // =========================================================

    private void AddSidewalkEndCap(SidewalkMeshBuilder builder,
        Vector3 curbTop, Vector3 innerTop, Vector3 outerTop,
        Vector3 curbBottom, Vector3 outerBottom, Vector3 normal)
    {
        float totalWidth = Vector3.Distance(new Vector3(curbTop.x, 0f, curbTop.z),
            new Vector3(outerTop.x, 0f, outerTop.z));
        float bufferWidth = Vector3.Distance(new Vector3(curbTop.x, 0f, curbTop.z),
            new Vector3(innerTop.x, 0f, innerTop.z));
        Vector3 innerBottom = innerTop;
        innerBottom.y = Mathf.Lerp(curbBottom.y, outerBottom.y,
            totalWidth > 0.001f ? bufferWidth / totalWidth : 0f);
        if (bufferWidth > 0.001f)
            AddQuadWithUV(builder, builder.curbTriangles,
                curbTop, curbBottom, innerBottom, innerTop, normal,
                0f, Mathf.Max(0.05f, curbTop.y - curbBottom.y), bufferWidth);
        AddQuadWithUV(builder, builder.curbTriangles,
            innerTop, innerBottom, outerBottom, outerTop, normal,
            0f, Mathf.Max(0.05f, innerTop.y - innerBottom.y),
            Mathf.Max(0.01f, totalWidth - bufferWidth));
    }

    private List<List<Vector3>> BuildSidewalkChunks(
        List<Vector3> points,
        List<long> nodeIds,
        float totalSideWidth)
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
        // FIRST POINT
        // =====================================================

        Vector3 first =
            points[0];

        if (IsJunction(
                nodeIds[0]))
        {
            Vector3 direction =
                points[1] -
                points[0];

            direction.y =
                0f;

            float length =
                direction.magnitude;

            if (length > 0.01f)
            {
                direction.Normalize();

                first +=
                    direction *
                    GetSafeTrim(
                        nodeIds[0],
                        totalSideWidth,
                        length
                    );
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
            long nodeId =
                nodeIds[i];

            if (!IsJunction(
                    nodeId))
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

            incoming.y =
                0f;

            float incomingLength =
                incoming.magnitude;

            if (incomingLength >
                0.01f)
            {
                incoming.Normalize();

                float trim =
                    GetSafeTrim(
                        nodeId,
                        totalSideWidth,
                        incomingLength
                    );

                Vector3 endPoint =
                    points[i] -
                    incoming *
                    trim;

                if (current.Count == 0 ||
                    Vector3.Distance(
                        current[
                            current.Count - 1
                        ],
                        endPoint) >
                    0.1f)
                {
                    current.Add(
                        endPoint
                    );
                }
            }


            AddChunkIfValid(
                result,
                current,
                totalSideWidth
            );


            // =================================================
            // NEW CHUNK
            // =================================================

            current =
                new List<Vector3>();


            // =================================================
            // START AFTER JUNCTION
            // =================================================

            Vector3 outgoing =
                points[i + 1] -
                points[i];

            outgoing.y =
                0f;

            float outgoingLength =
                outgoing.magnitude;

            if (outgoingLength >
                0.01f)
            {
                outgoing.Normalize();

                float trim =
                    GetSafeTrim(
                        nodeId,
                        totalSideWidth,
                        outgoingLength
                    );

                Vector3 startPoint =
                    points[i] +
                    outgoing *
                    trim;

                current.Add(
                    startPoint
                );
            }
        }


        // =====================================================
        // LAST POINT
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

            incoming.y =
                0f;

            float length =
                incoming.magnitude;

            if (length >
                0.01f)
            {
                incoming.Normalize();

                lastPoint -=
                    incoming *
                    GetSafeTrim(
                        nodeIds[last],
                        totalSideWidth,
                        length
                    );
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


        AddChunkIfValid(
            result,
            current,
            totalSideWidth
        );


        return result;
    }


    // =========================================================
    // ADD VALID CHUNK
    // =========================================================

    private void AddChunkIfValid(
        List<List<Vector3>> result,
        List<Vector3> candidate,
        float totalSideWidth)
    {
        if (candidate == null ||
            candidate.Count < 2)
        {
            return;
        }

        float length =
            GetPolylineLength(
                candidate
            );

        if (length <
            GetMinimumChunkLength(
                totalSideWidth
            ))
        {
            return;
        }

        result.Add(
            new List<Vector3>(
                candidate
            )
        );
    }


    // =========================================================
    // JUNCTION TRIM
    // =========================================================

    private float GetSafeTrim(
        long nodeId,
        float sideWidth,
        float segmentLength)
    {
        if (roadNetwork == null)
            return 0f;

        float trim =
            roadNetwork
                .GetJunctionTrimDistance(
                    nodeId,
                    sideWidth
                );

        /*
         * Segment çok kısaysa iki taraftan kesilip
         * ters dönmesin.
         */

        return
            Mathf.Min(
                trim,
                segmentLength *
                0.47f
            );
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
    // MINIMUM CHUNK LENGTH
    // =========================================================

    private float GetMinimumChunkLength(
        float totalSideWidth)
    {
        return
            Mathf.Max(
                MinimumChunkLength,
                totalSideWidth *
                1.35f
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

            point.y =
                0f;

            /*
             * Aynı / çok yakın node'ları temizle.
             */

            if (points.Count >
                    0 &&
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


    // =========================================================
    // POLYLINE LENGTH
    // =========================================================

    private float GetPolylineLength(
        List<Vector3> points)
    {
        if (points == null ||
            points.Count < 2)
        {
            return 0f;
        }

        float total =
            0f;

        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            total +=
                Vector3.Distance(
                    points[i],
                    points[i + 1]
                );
        }

        return total;
    }


    // =========================================================
    // TERRAIN
    // =========================================================

    private float SampleTerrain(
        Vector3 point)
    {
        if (elevation == null)
        {
            return 0f;
        }

        return
            elevation.SampleLocalHeight(
                point
            );
    }


    // =========================================================
    // QUAD WITH UV
    // =========================================================

    private void AddQuadWithUV(
        SidewalkMeshBuilder builder,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 expectedNormal,
        float distanceStart,
        float distanceEnd,
        float width)
    {
        int start =
            builder.vertices.Count;

        builder.vertices.Add(a);
        builder.vertices.Add(b);
        builder.vertices.Add(c);
        builder.vertices.Add(d);


        float uvScale =
            2f;

        float v0 =
            distanceStart /
            uvScale;

        float v1 =
            distanceEnd /
            uvScale;

        float u =
            Mathf.Max(
                0.1f,
                width /
                uvScale
            );


        builder.uvs.Add(
            new Vector2(
                0f,
                v0
            )
        );

        builder.uvs.Add(
            new Vector2(
                0f,
                v1
            )
        );

        builder.uvs.Add(
            new Vector2(
                u,
                v1
            )
        );

        builder.uvs.Add(
            new Vector2(
                u,
                v0
            )
        );


        AddQuadTriangles(
            builder,
            triangles,
            start,
            a,
            b,
            c,
            expectedNormal
        );
    }


    // =========================================================
    // SIMPLE QUAD
    // =========================================================

    private void AddQuadSimple(
        SidewalkMeshBuilder builder,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 expectedNormal)
    {
        int start =
            builder.vertices.Count;

        builder.vertices.Add(a);
        builder.vertices.Add(b);
        builder.vertices.Add(c);
        builder.vertices.Add(d);

        builder.uvs.Add(
            new Vector2(0f, 0f)
        );

        builder.uvs.Add(
            new Vector2(0f, 1f)
        );

        builder.uvs.Add(
            new Vector2(1f, 1f)
        );

        builder.uvs.Add(
            new Vector2(1f, 0f)
        );


        AddQuadTriangles(
            builder,
            triangles,
            start,
            a,
            b,
            c,
            expectedNormal
        );
    }


    // =========================================================
    // TRIANGLES
    // =========================================================

    private void AddQuadTriangles(
        SidewalkMeshBuilder builder,
        List<int> triangles,
        int start,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 expectedNormal)
    {
        Vector3 normal =
            Vector3.Cross(
                b - a,
                c - a
            );


        if (Vector3.Dot(
                normal,
                expectedNormal) >= 0f)
        {
            triangles.Add(
                start
            );

            triangles.Add(
                start + 1
            );

            triangles.Add(
                start + 2
            );


            triangles.Add(
                start
            );

            triangles.Add(
                start + 2
            );

            triangles.Add(
                start + 3
            );
        }
        else
        {
            triangles.Add(
                start
            );

            triangles.Add(
                start + 2
            );

            triangles.Add(
                start + 1
            );


            triangles.Add(
                start
            );

            triangles.Add(
                start + 3
            );

            triangles.Add(
                start + 2
            );
        }
    }


    // =========================================================
    // MATERIAL
    // =========================================================

    private Material GetStableMaterial(
        Material[] materials,
        long id,
        int salt)
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
            valid[
                random.Next(
                    valid.Count
                )
            ];
    }


    // =========================================================
    // BUILDER
    // =========================================================

    private class SidewalkMeshBuilder
    {
        public readonly List<Vector3> vertices =
            new List<Vector3>();

        public readonly List<Vector2> uvs =
            new List<Vector2>();

        public readonly List<int> sidewalkTriangles =
            new List<int>();

        public readonly List<int> curbTriangles =
            new List<int>();

        public readonly List<int> bufferTriangles =
            new List<int>();
    }
}
