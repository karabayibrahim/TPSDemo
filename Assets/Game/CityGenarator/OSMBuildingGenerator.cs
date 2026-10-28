using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

public class OSMBuildingGenerator
{
    private readonly Transform parent;

    private readonly Material[] facadeMaterials;
    private readonly Material[] glassMaterials;
    private readonly Material[] shopMaterials;
    private readonly Material[] balconyMaterials;

    // =========================================================
    // GLOBAL
    // =========================================================

    private const float FloorHeight = 3f;

    private const float GlassOffset = 0.015f;
    private const float ShopOffset = 0.02f;

    private const int MaxShopMaterialVariantsPerBuilding = 3;
    private const int MaxBalconyMaterialVariantsPerBuilding = 2;

    // =========================================================
    // BALCONY GEOMETRY
    // =========================================================

    private const float BalconyDepth = 0.85f;
    private const float BalconyThickness = 0.12f;

    private const float RailHeight = 1.0f;
    private const float RailThickness = 0.06f;

    // =========================================================
    // SURFACE
    // =========================================================

    private enum SurfaceType
    {
        Facade,
        Glass,
        Shop
    }

    // =========================================================
    // BUILDING ARCHETYPE
    // =========================================================

    private enum BuildingArchetype
    {
        ClassicResidential,
        ModernResidential,
        MixedUse,
        Office,
        GlassPlaza
    }

    // =========================================================
    // STYLE
    // =========================================================

    private struct BuildingStyle
    {
        public string styleName;

        public BuildingArchetype archetype;

        public float targetBayWidth;

        public float windowWidth;
        public float windowHeight;
        public float windowBottom;
        public float windowSideMargin;

        public float shopHeight;
        public float shopSideMargin;
        public float shopChance;

        public float entranceWidth;
        public float entranceHeight;

        public bool allowBalcony;

        public float balconyChance;
        public float balconyMaxWidth;

        public bool fullGlassUpperFloors;
        public bool fullGlassGroundFloor;
    }

    // =========================================================
    // BUILD RESULT
    // =========================================================

    private class BuildingMeshResult
    {
        public Mesh mesh;

        public List<int> usedShopMaterialIndices =
            new List<int>();

        public List<int> usedBalconyMaterialIndices =
            new List<int>();
    }

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public OSMBuildingGenerator(
        Transform parent,
        Material[] facadeMaterials,
        Material[] glassMaterials,
        Material[] shopMaterials,
        Material[] balconyMaterials)
    {
        this.parent = parent;

        this.facadeMaterials = facadeMaterials;
        this.glassMaterials = glassMaterials;
        this.shopMaterials = shopMaterials;
        this.balconyMaterials = balconyMaterials;
    }

    // =========================================================
    // GENERATE
    // =========================================================

    public void Generate(
        OSMMapData map,
        OSMWay way)
    {
        if (!way.HasTag("building"))
            return;

        if (way.nodeIds == null ||
            way.nodeIds.Count < 4)
        {
            return;
        }

        // Þimdilik sadece kapalý OSM building way.
        if (way.nodeIds[0] !=
            way.nodeIds[way.nodeIds.Count - 1])
        {
            return;
        }

        List<Vector3> footprint =
            new List<Vector3>();

        foreach (long nodeId in way.nodeIds)
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

            footprint.Add(point);
        }

        if (footprint.Count < 4)
            return;

        // Son node ilk node'un tekrarýysa kaldýr.
        if (Vector3.Distance(
                footprint[0],
                footprint[footprint.Count - 1])
            < 0.1f)
        {
            footprint.RemoveAt(
                footprint.Count - 1
            );
        }

        footprint =
            CleanPolygon(
                footprint
            );

        if (footprint.Count < 3)
            return;

        // Polygon CCW olsun.
        if (SignedArea(footprint) < 0f)
        {
            footprint.Reverse();
        }

        GetBuildingDimensions(
            way,
            out int floorCount,
            out float height
        );

        BuildingStyle style =
            SelectBuildingStyle(
                way,
                floorCount
            );

        CreateBuilding(
            footprint,
            height,
            floorCount,
            way.id,
            style
        );
    }

    // =========================================================
    // CREATE BUILDING
    // =========================================================

    private void CreateBuilding(
        List<Vector3> footprint,
        float height,
        int floorCount,
        long osmId,
        BuildingStyle style)
    {
        GameObject building =
            new GameObject(
                $"Building_{osmId}"
            );

        building.transform.SetParent(
            parent,
            false
        );

        // =====================================================
        // INFO
        // =====================================================

        BuildingInfo info =
            building.AddComponent<BuildingInfo>();

        info.osmId = osmId;
        info.floorCount = floorCount;
        info.height = height;
        info.styleName = style.styleName;

        info.footprint =
            footprint.ToArray();

        // =====================================================
        // MAIN MATERIALS
        // =====================================================

        int facadeMaterialIndex =
            GetStableMaterialIndex(
                facadeMaterials,
                osmId,
                101
            );

        int glassMaterialIndex =
            GetStableMaterialIndex(
                glassMaterials,
                osmId,
                307
            );

        int[] shopPalette =
            CreateMaterialPalette(
                shopMaterials,
                osmId + 7127,
                MaxShopMaterialVariantsPerBuilding
            );

        int[] balconyPalette =
            CreateMaterialPalette(
                balconyMaterials,
                osmId + 8123,
                MaxBalconyMaterialVariantsPerBuilding
            );

        info.facadeMaterialIndex =
            facadeMaterialIndex;

        info.glassMaterialIndex =
            glassMaterialIndex;

        // =====================================================
        // BUILD MESH
        // =====================================================

        BuildingMeshResult result =
            BuildBuildingMesh(
                footprint,
                height,
                floorCount,
                osmId,
                style,
                shopPalette,
                balconyPalette
            );

        if (result == null ||
            result.mesh == null)
        {
            DestroyObject(building);
            return;
        }

        // =====================================================
        // MESH OBJECT
        // =====================================================

        GameObject meshObject =
            new GameObject("Mesh");

        meshObject.transform.SetParent(
            building.transform,
            false
        );

        MeshFilter filter =
            meshObject.AddComponent<MeshFilter>();

        MeshRenderer renderer =
            meshObject.AddComponent<MeshRenderer>();

        filter.sharedMesh =
            result.mesh;

        // =====================================================
        // MATERIAL LIST
        //
        // 0 = facade
        // 1 = glass
        // 2... = used shops
        // sonra = used balconies
        // =====================================================

        Material facadeMaterial =
            GetMaterial(
                facadeMaterials,
                facadeMaterialIndex
            );

        Material glassMaterial =
            GetMaterial(
                glassMaterials,
                glassMaterialIndex
            );

        if (glassMaterial == null)
        {
            glassMaterial =
                facadeMaterial;
        }

        List<Material> rendererMaterials =
            new List<Material>();

        rendererMaterials.Add(
            facadeMaterial
        );

        rendererMaterials.Add(
            glassMaterial
        );

        // =====================================================
        // SHOP MATERIALS
        // =====================================================

        foreach (int shopMaterialIndex
                 in result.usedShopMaterialIndices)
        {
            Material material =
                GetMaterial(
                    shopMaterials,
                    shopMaterialIndex
                );

            if (material == null)
            {
                material =
                    glassMaterial;
            }

            rendererMaterials.Add(
                material
            );
        }

        // =====================================================
        // BALCONY MATERIALS
        // =====================================================

        foreach (int balconyMaterialIndex
                 in result.usedBalconyMaterialIndices)
        {
            Material material =
                GetMaterial(
                    balconyMaterials,
                    balconyMaterialIndex
                );

            if (material == null)
            {
                material =
                    facadeMaterial;
            }

            rendererMaterials.Add(
                material
            );
        }

        renderer.sharedMaterials =
            rendererMaterials.ToArray();
    }

    // =========================================================
    // BUILD MESH
    // =========================================================

    private BuildingMeshResult BuildBuildingMesh(
        List<Vector3> footprint,
        float height,
        int floorCount,
        long osmId,
        BuildingStyle style,
        int[] shopPalette,
        int[] balconyPalette)
    {
        MeshBuilder builder =
            new MeshBuilder();

        // Geometri random'ý
        System.Random geometryRandom =
            CreateStableRandom(
                osmId + 1009
            );

        // Shop materyal random'ý
        System.Random shopRandom =
            CreateStableRandom(
                osmId + 2003
            );

        // Balcony materyal random'ý
        System.Random balconyRandom =
            CreateStableRandom(
                osmId + 5003
            );

        int entranceEdge =
            FindLongestEdge(
                footprint
            );

        // =====================================================
        // FACADES
        // =====================================================

        for (int edgeIndex = 0;
             edgeIndex < footprint.Count;
             edgeIndex++)
        {
            Vector3 start =
                footprint[edgeIndex];

            Vector3 end =
                footprint[
                    (edgeIndex + 1)
                    % footprint.Count
                ];

            Vector3 edge =
                end - start;

            edge.y = 0f;

            float edgeLength =
                edge.magnitude;

            if (edgeLength < 0.40f)
                continue;

            Vector3 direction =
                edge.normalized;

            /*
             * CCW polygon.
             * Ýç taraf sol,
             * dýþ taraf sað.
             */
            Vector3 outward =
                new Vector3(
                    direction.z,
                    0f,
                    -direction.x
                ).normalized;

            GenerateFacade(
                builder,
                start,
                direction,
                outward,
                edgeLength,
                height,
                floorCount,
                edgeIndex == entranceEdge,
                geometryRandom,
                shopRandom,
                balconyRandom,
                style,
                shopPalette,
                balconyPalette
            );
        }

        // =====================================================
        // ROOF + BOTTOM
        // =====================================================

        AddRoof(
            builder,
            footprint,
            height
        );

        AddBottom(
            builder,
            footprint
        );

        if (builder.vertices.Count == 0)
            return null;

        // =====================================================
        // USED SHOP MATERIALS
        // =====================================================

        List<int> usedShopIndices =
            new List<int>(
                builder.shopTriangles.Keys
            );

        usedShopIndices.Sort();

        // =====================================================
        // USED BALCONY MATERIALS
        // =====================================================

        List<int> usedBalconyIndices =
            new List<int>(
                builder.balconyTriangles.Keys
            );

        usedBalconyIndices.Sort();

        // =====================================================
        // CREATE MESH
        // =====================================================

        Mesh mesh =
            new Mesh();

        mesh.name =
            $"ProceduralBuilding_{style.styleName}";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            builder.vertices
        );

        mesh.SetUVs(
            0,
            builder.uvs
        );

        // =====================================================
        // SUBMESH COUNT
        //
        // 0 facade
        // 1 glass
        // shops...
        // balconies...
        // =====================================================

        mesh.subMeshCount =
            2 +
            usedShopIndices.Count +
            usedBalconyIndices.Count;

        // =====================================================
        // FACADE
        // =====================================================

        mesh.SetTriangles(
            builder.facadeTriangles,
            0,
            false
        );

        // =====================================================
        // GLASS
        // =====================================================

        mesh.SetTriangles(
            builder.glassTriangles,
            1,
            false
        );

        // =====================================================
        // SHOP SUBMESHES
        // =====================================================

        for (int i = 0;
             i < usedShopIndices.Count;
             i++)
        {
            int materialIndex =
                usedShopIndices[i];

            mesh.SetTriangles(
                builder.shopTriangles[
                    materialIndex
                ],
                2 + i,
                false
            );
        }

        // =====================================================
        // BALCONY SUBMESHES
        // =====================================================

        int balconySubMeshStart =
            2 +
            usedShopIndices.Count;

        for (int i = 0;
             i < usedBalconyIndices.Count;
             i++)
        {
            int materialIndex =
                usedBalconyIndices[i];

            mesh.SetTriangles(
                builder.balconyTriangles[
                    materialIndex
                ],
                balconySubMeshStart + i,
                false
            );
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return new BuildingMeshResult
        {
            mesh =
                mesh,

            usedShopMaterialIndices =
                usedShopIndices,

            usedBalconyMaterialIndices =
                usedBalconyIndices
        };
    }

    // =========================================================
    // GENERATE FACADE
    // =========================================================

    private void GenerateFacade(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float edgeLength,
        float buildingHeight,
        int floorCount,
        bool entranceEdge,
        System.Random geometryRandom,
        System.Random shopRandom,
        System.Random balconyRandom,
        BuildingStyle style,
        int[] shopPalette,
        int[] balconyPalette)
    {
        int bayCount =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    edgeLength /
                    style.targetBayWidth
                )
            );

        float bayWidth =
            edgeLength /
            bayCount;

        int floors =
            Mathf.Max(
                1,
                floorCount
            );

        // =====================================================
        // FLOORS
        // =====================================================

        for (int floor = 0;
             floor < floors;
             floor++)
        {
            float floorBottom =
                floor *
                FloorHeight;

            float floorTop =
                Mathf.Min(
                    floorBottom +
                    FloorHeight,
                    buildingHeight
                );

            if (floorTop <=
                floorBottom + 0.10f)
            {
                continue;
            }

            bool previousBalcony =
                false;

            // =================================================
            // BAYS
            // =================================================

            for (int bay = 0;
                 bay < bayCount;
                 bay++)
            {
                float bayStart =
                    bay *
                    bayWidth;

                float bayEnd =
                    (bay + 1) *
                    bayWidth;

                // =================================================
                // GROUND FLOOR
                // =================================================

                if (floor == 0)
                {
                    bool entrance =
                        entranceEdge &&
                        bay ==
                        bayCount / 2;

                    if (entrance)
                    {
                        AddEntranceCell(
                            builder,
                            edgeStart,
                            direction,
                            outward,
                            bayStart,
                            bayEnd,
                            floorBottom,
                            floorTop,
                            style
                        );

                        continue;
                    }

                    // =============================================
                    // GLASS PLAZA GROUND
                    // =============================================

                    if (style.fullGlassGroundFloor &&
                        geometryRandom.NextDouble()
                        < 0.55)
                    {
                        AddCurtainGlassCell(
                            builder,
                            edgeStart,
                            direction,
                            outward,
                            bayStart,
                            bayEnd,
                            floorBottom,
                            floorTop,
                            style
                        );

                        continue;
                    }

                    // =============================================
                    // SHOP
                    // =============================================

                    bool canShop =
                        bayWidth >=
                        1.60f;

                    bool createShop =
                        canShop &&
                        geometryRandom.NextDouble()
                        <
                        style.shopChance;

                    if (createShop)
                    {
                        int shopMaterialIndex =
                            PickMaterialFromPalette(
                                shopPalette,
                                shopRandom
                            );

                        AddShopCell(
                            builder,
                            edgeStart,
                            direction,
                            outward,
                            bayStart,
                            bayEnd,
                            floorBottom,
                            floorTop,
                            style,
                            shopMaterialIndex
                        );
                    }
                    else
                    {
                        AddWindowCell(
                            builder,
                            edgeStart,
                            direction,
                            outward,
                            bayStart,
                            bayEnd,
                            floorBottom,
                            floorTop,
                            false,
                            style,
                            -1
                        );
                    }

                    continue;
                }

                // =================================================
                // GLASS PLAZA UPPER FLOOR
                // =================================================

                if (style.fullGlassUpperFloors)
                {
                    AddCurtainGlassCell(
                        builder,
                        edgeStart,
                        direction,
                        outward,
                        bayStart,
                        bayEnd,
                        floorBottom,
                        floorTop,
                        style
                    );

                    continue;
                }

                // =================================================
                // BALCONY
                // =================================================

                bool balcony =
                    style.allowBalcony &&
                    bayWidth >= 1.75f &&
                    !previousBalcony &&
                    geometryRandom.NextDouble()
                    <
                    style.balconyChance;

                int balconyMaterialIndex =
                    -1;

                if (balcony)
                {
                    balconyMaterialIndex =
                        PickMaterialFromPalette(
                            balconyPalette,
                            balconyRandom
                        );
                }

                AddWindowCell(
                    builder,
                    edgeStart,
                    direction,
                    outward,
                    bayStart,
                    bayEnd,
                    floorBottom,
                    floorTop,
                    balcony,
                    style,
                    balconyMaterialIndex
                );

                previousBalcony =
                    balcony;
            }
        }

        // =====================================================
        // EXTRA HEIGHT
        // =====================================================

        float generatedHeight =
            floors *
            FloorHeight;

        if (generatedHeight <
            buildingHeight - 0.05f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                0f,
                edgeLength,
                generatedHeight,
                buildingHeight,
                0f,
                SurfaceType.Facade
            );
        }
    }

    // =========================================================
    // WINDOW CELL
    // =========================================================

    private void AddWindowCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float floorBottom,
        float floorTop,
        bool balcony,
        BuildingStyle style,
        int balconyMaterialIndex)
    {
        float cellWidth =
            cellEnd -
            cellStart;

        if (cellWidth < 0.75f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        float openingWidth =
            Mathf.Min(
                style.windowWidth,
                cellWidth -
                style.windowSideMargin *
                2f
            );

        if (openingWidth < 0.50f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        float openingBottom;
        float openingHeight;

        // =====================================================
        // BALCONY DOOR
        // =====================================================

        if (balcony)
        {
            openingBottom =
                floorBottom +
                0.08f;

            openingHeight =
                Mathf.Min(
                    2.15f,
                    floorTop -
                    openingBottom -
                    0.15f
                );
        }
        else
        {
            openingBottom =
                floorBottom +
                style.windowBottom;

            openingHeight =
                Mathf.Min(
                    style.windowHeight,
                    floorTop -
                    openingBottom -
                    0.25f
                );
        }

        if (openingHeight < 0.50f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        // =====================================================
        // WINDOW / DOOR
        // =====================================================

        AddOpeningCell(
            builder,
            edgeStart,
            direction,
            outward,
            cellStart,
            cellEnd,
            floorBottom,
            floorTop,
            openingWidth,
            openingBottom,
            openingHeight,
            GlassOffset,
            SurfaceType.Glass
        );

        // =====================================================
        // BALCONY GEOMETRY
        // =====================================================

        if (balcony)
        {
            float balconyWidth =
                Mathf.Min(
                    style.balconyMaxWidth,
                    cellWidth -
                    0.25f
                );

            if (balconyWidth >= 0.90f)
            {
                float center =
                    (
                        cellStart +
                        cellEnd
                    ) *
                    0.5f;

                AddBalcony(
                    builder,
                    edgeStart,
                    direction,
                    outward,
                    center,
                    floorBottom,
                    balconyWidth,
                    balconyMaterialIndex
                );
            }
        }
    }

    // =========================================================
    // SHOP CELL
    // =========================================================

    private void AddShopCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float floorBottom,
        float floorTop,
        BuildingStyle style,
        int shopMaterialIndex)
    {
        float cellWidth =
            cellEnd -
            cellStart;

        float openingWidth =
            cellWidth -
            style.shopSideMargin *
            2f;

        if (openingWidth < 0.75f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        float openingBottom =
            floorBottom +
            0.08f;

        float openingHeight =
            Mathf.Min(
                style.shopHeight,
                floorTop -
                openingBottom -
                0.20f
            );

        if (openingHeight < 0.75f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        AddOpeningCell(
            builder,
            edgeStart,
            direction,
            outward,
            cellStart,
            cellEnd,
            floorBottom,
            floorTop,
            openingWidth,
            openingBottom,
            openingHeight,
            ShopOffset,
            SurfaceType.Shop,
            shopMaterialIndex
        );
    }

    // =========================================================
    // CURTAIN GLASS
    // =========================================================

    private void AddCurtainGlassCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float floorBottom,
        float floorTop,
        BuildingStyle style)
    {
        float cellWidth =
            cellEnd -
            cellStart;

        float openingWidth =
            cellWidth -
            style.windowSideMargin *
            2f;

        if (openingWidth < 0.80f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        float openingBottom =
            floorBottom +
            0.10f;

        float openingHeight =
            floorTop -
            openingBottom -
            0.12f;

        if (openingHeight < 1f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        AddOpeningCell(
            builder,
            edgeStart,
            direction,
            outward,
            cellStart,
            cellEnd,
            floorBottom,
            floorTop,
            openingWidth,
            openingBottom,
            openingHeight,
            GlassOffset,
            SurfaceType.Glass
        );
    }

    // =========================================================
    // ENTRANCE
    // =========================================================

    private void AddEntranceCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float floorBottom,
        float floorTop,
        BuildingStyle style)
    {
        float cellWidth =
            cellEnd -
            cellStart;

        float width =
            Mathf.Min(
                style.entranceWidth,
                cellWidth -
                0.30f
            );

        if (width < 0.65f)
        {
            AddSolidCell(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                cellEnd,
                floorBottom,
                floorTop
            );

            return;
        }

        float openingBottom =
            floorBottom +
            0.05f;

        float openingHeight =
            Mathf.Min(
                style.entranceHeight,
                floorTop -
                openingBottom -
                0.10f
            );

        AddOpeningCell(
            builder,
            edgeStart,
            direction,
            outward,
            cellStart,
            cellEnd,
            floorBottom,
            floorTop,
            width,
            openingBottom,
            openingHeight,
            GlassOffset,
            SurfaceType.Glass
        );
    }

    // =========================================================
    // OPENING CELL
    // =========================================================

    private void AddOpeningCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float cellBottom,
        float cellTop,
        float openingWidth,
        float openingBottom,
        float openingHeight,
        float surfaceOffset,
        SurfaceType surfaceType,
        int shopMaterialIndex = -1)
    {
        float center =
            (
                cellStart +
                cellEnd
            ) *
            0.5f;

        float openingLeft =
            center -
            openingWidth *
            0.5f;

        float openingRight =
            center +
            openingWidth *
            0.5f;

        openingLeft =
            Mathf.Clamp(
                openingLeft,
                cellStart,
                cellEnd
            );

        openingRight =
            Mathf.Clamp(
                openingRight,
                cellStart,
                cellEnd
            );

        float openingTop =
            Mathf.Min(
                openingBottom +
                openingHeight,
                cellTop
            );

        // =====================================================
        // LEFT
        // =====================================================

        if (openingLeft >
            cellStart + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                openingLeft,
                cellBottom,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        // =====================================================
        // RIGHT
        // =====================================================

        if (cellEnd >
            openingRight + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                openingRight,
                cellEnd,
                cellBottom,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        // =====================================================
        // BOTTOM
        // =====================================================

        if (openingBottom >
            cellBottom + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                openingLeft,
                openingRight,
                cellBottom,
                openingBottom,
                0f,
                SurfaceType.Facade
            );
        }

        // =====================================================
        // TOP
        // =====================================================

        if (cellTop >
            openingTop + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                openingLeft,
                openingRight,
                openingTop,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        // =====================================================
        // OPENING SURFACE
        // =====================================================

        AddFacadeQuad(
            builder,
            edgeStart,
            direction,
            outward,
            openingLeft,
            openingRight,
            openingBottom,
            openingTop,
            surfaceOffset,
            surfaceType,
            shopMaterialIndex
        );
    }

    // =========================================================
    // SOLID
    // =========================================================

    private void AddSolidCell(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float cellStart,
        float cellEnd,
        float floorBottom,
        float floorTop)
    {
        AddFacadeQuad(
            builder,
            edgeStart,
            direction,
            outward,
            cellStart,
            cellEnd,
            floorBottom,
            floorTop,
            0f,
            SurfaceType.Facade
        );
    }

    // =========================================================
    // FACADE QUAD
    // =========================================================

    private void AddFacadeQuad(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float horizontalStart,
        float horizontalEnd,
        float verticalStart,
        float verticalEnd,
        float outwardOffset,
        SurfaceType surfaceType,
        int shopMaterialIndex = -1)
    {
        if (horizontalEnd <=
            horizontalStart)
        {
            return;
        }

        if (verticalEnd <=
            verticalStart)
        {
            return;
        }

        Vector3 bottomLeft =
            edgeStart +
            direction *
            horizontalStart +
            Vector3.up *
            verticalStart +
            outward *
            outwardOffset;

        Vector3 bottomRight =
            edgeStart +
            direction *
            horizontalEnd +
            Vector3.up *
            verticalStart +
            outward *
            outwardOffset;

        Vector3 topLeft =
            edgeStart +
            direction *
            horizontalStart +
            Vector3.up *
            verticalEnd +
            outward *
            outwardOffset;

        Vector3 topRight =
            edgeStart +
            direction *
            horizontalEnd +
            Vector3.up *
            verticalEnd +
            outward *
            outwardOffset;

        int index =
            builder.vertices.Count;

        builder.vertices.Add(
            bottomLeft
        );

        builder.vertices.Add(
            bottomRight
        );

        builder.vertices.Add(
            topLeft
        );

        builder.vertices.Add(
            topRight
        );

        float width =
            horizontalEnd -
            horizontalStart;

        float height =
            verticalEnd -
            verticalStart;

        // =====================================================
        // UV
        // =====================================================

        if (surfaceType ==
            SurfaceType.Facade)
        {
            builder.uvs.Add(
                new Vector2(0f, 0f)
            );

            builder.uvs.Add(
                new Vector2(
                    width / 3f,
                    0f
                )
            );

            builder.uvs.Add(
                new Vector2(
                    0f,
                    height / 3f
                )
            );

            builder.uvs.Add(
                new Vector2(
                    width / 3f,
                    height / 3f
                )
            );
        }
        else
        {
            builder.uvs.Add(
                new Vector2(0f, 0f)
            );

            builder.uvs.Add(
                new Vector2(1f, 0f)
            );

            builder.uvs.Add(
                new Vector2(0f, 1f)
            );

            builder.uvs.Add(
                new Vector2(1f, 1f)
            );
        }

        List<int> triangles =
            GetTriangleList(
                builder,
                surfaceType,
                shopMaterialIndex
            );

        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);

        triangles.Add(index + 1);
        triangles.Add(index + 2);
        triangles.Add(index + 3);
    }

    // =========================================================
    // TRIANGLE LIST
    // =========================================================

    private List<int> GetTriangleList(
        MeshBuilder builder,
        SurfaceType surfaceType,
        int shopMaterialIndex)
    {
        if (surfaceType ==
            SurfaceType.Glass)
        {
            return
                builder.glassTriangles;
        }

        if (surfaceType ==
            SurfaceType.Shop)
        {
            if (shopMaterialIndex < 0)
            {
                return
                    builder.glassTriangles;
            }

            if (!builder.shopTriangles.TryGetValue(
                    shopMaterialIndex,
                    out List<int> triangles))
            {
                triangles =
                    new List<int>();

                builder.shopTriangles.Add(
                    shopMaterialIndex,
                    triangles
                );
            }

            return
                triangles;
        }

        return
            builder.facadeTriangles;
    }

    // =========================================================
    // BALCONY
    // =========================================================

    private void AddBalcony(
        MeshBuilder builder,
        Vector3 edgeStart,
        Vector3 direction,
        Vector3 outward,
        float centerDistance,
        float floorY,
        float width,
        int balconyMaterialIndex)
    {
        Vector3 wallCenter =
            edgeStart +
            direction *
            centerDistance;

        // =====================================================
        // SLAB
        // =====================================================

        Vector3 slabCenter =
            wallCenter +
            outward *
            (
                BalconyDepth *
                0.5f
            );

        slabCenter.y =
            floorY -
            BalconyThickness *
            0.5f;

        AddOrientedBox(
            builder,
            slabCenter,
            direction,
            outward,
            width,
            BalconyThickness,
            BalconyDepth,
            balconyMaterialIndex
        );

        // =====================================================
        // FRONT RAIL
        // =====================================================

        Vector3 frontRail =
            wallCenter +
            outward *
            (
                BalconyDepth -
                RailThickness *
                0.5f
            );

        frontRail.y =
            floorY +
            RailHeight *
            0.5f;

        AddOrientedBox(
            builder,
            frontRail,
            direction,
            outward,
            width,
            RailHeight,
            RailThickness,
            balconyMaterialIndex
        );

        // =====================================================
        // LEFT RAIL
        // =====================================================

        Vector3 leftRail =
            wallCenter -
            direction *
            (
                width *
                0.5f -
                RailThickness *
                0.5f
            ) +
            outward *
            (
                BalconyDepth *
                0.5f
            );

        leftRail.y =
            floorY +
            RailHeight *
            0.5f;

        AddOrientedBox(
            builder,
            leftRail,
            outward,
            -direction,
            BalconyDepth,
            RailHeight,
            RailThickness,
            balconyMaterialIndex
        );

        // =====================================================
        // RIGHT RAIL
        // =====================================================

        Vector3 rightRail =
            wallCenter +
            direction *
            (
                width *
                0.5f -
                RailThickness *
                0.5f
            ) +
            outward *
            (
                BalconyDepth *
                0.5f
            );

        rightRail.y =
            floorY +
            RailHeight *
            0.5f;

        AddOrientedBox(
            builder,
            rightRail,
            outward,
            direction,
            BalconyDepth,
            RailHeight,
            RailThickness,
            balconyMaterialIndex
        );
    }

    // =========================================================
    // ORIENTED BOX
    // =========================================================

    private void AddOrientedBox(
        MeshBuilder builder,
        Vector3 center,
        Vector3 right,
        Vector3 forward,
        float sizeX,
        float sizeY,
        float sizeZ,
        int balconyMaterialIndex)
    {
        right.Normalize();
        forward.Normalize();

        float hx =
            sizeX *
            0.5f;

        float hy =
            sizeY *
            0.5f;

        float hz =
            sizeZ *
            0.5f;

        Vector3 r =
            right *
            hx;

        Vector3 u =
            Vector3.up *
            hy;

        Vector3 f =
            forward *
            hz;

        int start =
            builder.vertices.Count;

        // 0
        builder.vertices.Add(
            center - r - u - f
        );

        // 1
        builder.vertices.Add(
            center + r - u - f
        );

        // 2
        builder.vertices.Add(
            center - r + u - f
        );

        // 3
        builder.vertices.Add(
            center + r + u - f
        );

        // 4
        builder.vertices.Add(
            center - r - u + f
        );

        // 5
        builder.vertices.Add(
            center + r - u + f
        );

        // 6
        builder.vertices.Add(
            center - r + u + f
        );

        // 7
        builder.vertices.Add(
            center + r + u + f
        );

        for (int i = 0;
             i < 8;
             i++)
        {
            builder.uvs.Add(
                Vector2.zero
            );
        }

        List<int> triangles =
            GetBalconyTriangleList(
                builder,
                balconyMaterialIndex
            );

        // BACK
        AddTriangle(
            triangles,
            start + 0,
            start + 2,
            start + 1
        );

        AddTriangle(
            triangles,
            start + 1,
            start + 2,
            start + 3
        );

        // FRONT
        AddTriangle(
            triangles,
            start + 4,
            start + 5,
            start + 6
        );

        AddTriangle(
            triangles,
            start + 5,
            start + 7,
            start + 6
        );

        // LEFT
        AddTriangle(
            triangles,
            start + 0,
            start + 4,
            start + 2
        );

        AddTriangle(
            triangles,
            start + 4,
            start + 6,
            start + 2
        );

        // RIGHT
        AddTriangle(
            triangles,
            start + 1,
            start + 3,
            start + 5
        );

        AddTriangle(
            triangles,
            start + 3,
            start + 7,
            start + 5
        );

        // BOTTOM
        AddTriangle(
            triangles,
            start + 0,
            start + 1,
            start + 4
        );

        AddTriangle(
            triangles,
            start + 1,
            start + 5,
            start + 4
        );

        // TOP
        AddTriangle(
            triangles,
            start + 2,
            start + 6,
            start + 3
        );

        AddTriangle(
            triangles,
            start + 3,
            start + 6,
            start + 7
        );
    }

    // =========================================================
    // BALCONY TRIANGLE LIST
    // =========================================================

    private List<int> GetBalconyTriangleList(
        MeshBuilder builder,
        int balconyMaterialIndex)
    {
        /*
         * Balcony material verilmediyse
         * facade materyalini kullan.
         */
        if (balconyMaterialIndex < 0)
        {
            return
                builder.facadeTriangles;
        }

        if (!builder.balconyTriangles.TryGetValue(
                balconyMaterialIndex,
                out List<int> triangles))
        {
            triangles =
                new List<int>();

            builder.balconyTriangles.Add(
                balconyMaterialIndex,
                triangles
            );
        }

        return
            triangles;
    }

    // =========================================================
    // MATERIAL PALETTE
    // =========================================================

    private int[] CreateMaterialPalette(
        Material[] materials,
        long seedId,
        int maximumVariants)
    {
        if (materials == null ||
            materials.Length == 0)
        {
            return
                Array.Empty<int>();
        }

        List<int> valid =
            new List<int>();

        for (int i = 0;
             i < materials.Length;
             i++)
        {
            if (materials[i] != null)
            {
                valid.Add(i);
            }
        }

        if (valid.Count == 0)
        {
            return
                Array.Empty<int>();
        }

        System.Random random =
            CreateStableRandom(
                seedId
            );

        // Fisher-Yates shuffle.
        for (int i =
                 valid.Count - 1;
             i > 0;
             i--)
        {
            int j =
                random.Next(
                    0,
                    i + 1
                );

            int temp =
                valid[i];

            valid[i] =
                valid[j];

            valid[j] =
                temp;
        }

        int count =
            Mathf.Min(
                maximumVariants,
                valid.Count
            );

        int[] palette =
            new int[count];

        for (int i = 0;
             i < count;
             i++)
        {
            palette[i] =
                valid[i];
        }

        return
            palette;
    }

    // =========================================================
    // PICK MATERIAL
    // =========================================================

    private int PickMaterialFromPalette(
        int[] palette,
        System.Random random)
    {
        if (palette == null ||
            palette.Length == 0)
        {
            return -1;
        }

        return
            palette[
                random.Next(
                    0,
                    palette.Length
                )
            ];
    }

    // =========================================================
    // ROOF
    // =========================================================

    private void AddRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float height)
    {
        List<int> triangles =
            TriangulatePolygon(
                footprint
            );

        if (triangles.Count < 3)
            return;

        int start =
            builder.vertices.Count;

        foreach (Vector3 point
                 in footprint)
        {
            builder.vertices.Add(
                new Vector3(
                    point.x,
                    height,
                    point.z
                )
            );

            builder.uvs.Add(
                new Vector2(
                    point.x *
                    0.1f,
                    point.z *
                    0.1f
                )
            );
        }

        for (int i = 0;
             i < triangles.Count;
             i += 3)
        {
            builder.facadeTriangles.Add(
                start +
                triangles[i]
            );

            builder.facadeTriangles.Add(
                start +
                triangles[i + 2]
            );

            builder.facadeTriangles.Add(
                start +
                triangles[i + 1]
            );
        }
    }

    // =========================================================
    // BOTTOM
    // =========================================================

    private void AddBottom(
        MeshBuilder builder,
        List<Vector3> footprint)
    {
        List<int> triangles =
            TriangulatePolygon(
                footprint
            );

        if (triangles.Count < 3)
            return;

        int start =
            builder.vertices.Count;

        foreach (Vector3 point
                 in footprint)
        {
            builder.vertices.Add(
                new Vector3(
                    point.x,
                    0f,
                    point.z
                )
            );

            builder.uvs.Add(
                Vector2.zero
            );
        }

        for (int i = 0;
             i < triangles.Count;
             i += 3)
        {
            builder.facadeTriangles.Add(
                start +
                triangles[i]
            );

            builder.facadeTriangles.Add(
                start +
                triangles[i + 1]
            );

            builder.facadeTriangles.Add(
                start +
                triangles[i + 2]
            );
        }
    }

    // =========================================================
    // STYLE SELECT
    // =========================================================

    private BuildingStyle SelectBuildingStyle(
        OSMWay way,
        int floorCount)
    {
        System.Random random =
            CreateStableRandom(
                way.id + 4001
            );

        string building =
            GetTagLower(
                way,
                "building"
            );

        string office =
            GetTagLower(
                way,
                "office"
            );

        string shop =
            GetTagLower(
                way,
                "shop"
            );

        bool officeLike =
            !string.IsNullOrEmpty(
                office
            ) ||
            building == "office" ||
            building == "commercial" ||
            building == "hotel";

        bool residential =
            building == "apartments" ||
            building == "residential" ||
            building == "house" ||
            building == "detached";

        bool retail =
            !string.IsNullOrEmpty(
                shop
            ) ||
            building == "retail";

        int roll =
            random.Next(
                0,
                100
            );

        // =====================================================
        // OFFICE
        // =====================================================

        if (officeLike)
        {
            if (floorCount >= 6 &&
                roll < 50)
            {
                return
                    GlassPlazaStyle();
            }

            return
                OfficeStyle();
        }

        // =====================================================
        // RETAIL
        // =====================================================

        if (retail)
        {
            if (floorCount >= 6 &&
                roll < 25)
            {
                return
                    GlassPlazaStyle();
            }

            return
                MixedUseStyle();
        }

        // =====================================================
        // RESIDENTIAL
        // =====================================================

        if (residential)
        {
            if (roll < 50)
            {
                return
                    ClassicResidentialStyle();
            }

            if (roll < 82)
            {
                return
                    ModernResidentialStyle();
            }

            return
                MixedUseStyle();
        }

        // =====================================================
        // GENERIC
        // =====================================================

        if (floorCount >= 7 &&
            roll < 18)
        {
            return
                GlassPlazaStyle();
        }

        if (roll < 38)
        {
            return
                ClassicResidentialStyle();
        }

        if (roll < 63)
        {
            return
                ModernResidentialStyle();
        }

        if (roll < 84)
        {
            return
                MixedUseStyle();
        }

        return
            OfficeStyle();
    }

    // =========================================================
    // CLASSIC
    // =========================================================

    private BuildingStyle ClassicResidentialStyle()
    {
        return new BuildingStyle
        {
            styleName =
                "ClassicResidential",

            archetype =
                BuildingArchetype.ClassicResidential,

            targetBayWidth =
                2.20f,

            windowWidth =
                1.00f,

            windowHeight =
                1.35f,

            windowBottom =
                0.95f,

            windowSideMargin =
                0.26f,

            shopHeight =
                2.05f,

            shopSideMargin =
                0.20f,

            shopChance =
                0.55f,

            entranceWidth =
                1.25f,

            entranceHeight =
                2.20f,

            allowBalcony =
                true,

            balconyChance =
                0.42f,

            balconyMaxWidth =
                2.00f,

            fullGlassUpperFloors =
                false,

            fullGlassGroundFloor =
                false
        };
    }

    // =========================================================
    // MODERN
    // =========================================================

    private BuildingStyle ModernResidentialStyle()
    {
        return new BuildingStyle
        {
            styleName =
                "ModernResidential",

            archetype =
                BuildingArchetype.ModernResidential,

            targetBayWidth =
                2.75f,

            windowWidth =
                1.45f,

            windowHeight =
                1.55f,

            windowBottom =
                0.78f,

            windowSideMargin =
                0.24f,

            shopHeight =
                2.15f,

            shopSideMargin =
                0.18f,

            shopChance =
                0.62f,

            entranceWidth =
                1.40f,

            entranceHeight =
                2.30f,

            allowBalcony =
                true,

            balconyChance =
                0.26f,

            balconyMaxWidth =
                2.20f,

            fullGlassUpperFloors =
                false,

            fullGlassGroundFloor =
                false
        };
    }

    // =========================================================
    // MIXED USE
    // =========================================================

    private BuildingStyle MixedUseStyle()
    {
        return new BuildingStyle
        {
            styleName =
                "MixedUse",

            archetype =
                BuildingArchetype.MixedUse,

            targetBayWidth =
                2.90f,

            windowWidth =
                1.35f,

            windowHeight =
                1.55f,

            windowBottom =
                0.75f,

            windowSideMargin =
                0.22f,

            shopHeight =
                2.30f,

            shopSideMargin =
                0.15f,

            shopChance =
                0.88f,

            entranceWidth =
                1.45f,

            entranceHeight =
                2.35f,

            allowBalcony =
                false,

            balconyChance =
                0f,

            balconyMaxWidth =
                0f,

            fullGlassUpperFloors =
                false,

            fullGlassGroundFloor =
                false
        };
    }

    // =========================================================
    // OFFICE
    // =========================================================

    private BuildingStyle OfficeStyle()
    {
        return new BuildingStyle
        {
            styleName =
                "Office",

            archetype =
                BuildingArchetype.Office,

            targetBayWidth =
                3.10f,

            windowWidth =
                1.80f,

            windowHeight =
                1.65f,

            windowBottom =
                0.70f,

            windowSideMargin =
                0.18f,

            shopHeight =
                2.35f,

            shopSideMargin =
                0.15f,

            shopChance =
                0.50f,

            entranceWidth =
                1.60f,

            entranceHeight =
                2.45f,

            allowBalcony =
                false,

            balconyChance =
                0f,

            balconyMaxWidth =
                0f,

            fullGlassUpperFloors =
                false,

            fullGlassGroundFloor =
                false
        };
    }

    // =========================================================
    // GLASS PLAZA
    // =========================================================

    private BuildingStyle GlassPlazaStyle()
    {
        return new BuildingStyle
        {
            styleName =
                "GlassPlaza",

            archetype =
                BuildingArchetype.GlassPlaza,

            targetBayWidth =
                3.30f,

            windowWidth =
                2.40f,

            windowHeight =
                2.50f,

            windowBottom =
                0.18f,

            windowSideMargin =
                0.10f,

            shopHeight =
                2.45f,

            shopSideMargin =
                0.08f,

            shopChance =
                0.45f,

            entranceWidth =
                1.90f,

            entranceHeight =
                2.55f,

            allowBalcony =
                false,

            balconyChance =
                0f,

            balconyMaxWidth =
                0f,

            fullGlassUpperFloors =
                true,

            fullGlassGroundFloor =
                true
        };
    }

    // =========================================================
    // BUILDING DIMENSIONS
    // =========================================================

    private void GetBuildingDimensions(
        OSMWay way,
        out int floorCount,
        out float height)
    {
        floorCount = 0;
        height = 0f;

        // =====================================================
        // LEVELS
        // =====================================================

        string levels =
            way.GetTag(
                "building:levels"
            );

        if (!string.IsNullOrWhiteSpace(
                levels))
        {
            if (int.TryParse(
                    levels,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                floorCount =
                    Mathf.Clamp(
                        value,
                        1,
                        50
                    );
            }
        }

        // =====================================================
        // HEIGHT
        // =====================================================

        string heightString =
            way.GetTag(
                "height"
            );

        if (!string.IsNullOrWhiteSpace(
                heightString))
        {
            heightString =
                heightString
                    .ToLowerInvariant()
                    .Replace("meters", "")
                    .Replace("meter", "")
                    .Replace("metres", "")
                    .Replace("metre", "")
                    .Replace("m", "")
                    .Trim();

            if (float.TryParse(
                    heightString,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value))
            {
                height =
                    Mathf.Clamp(
                        value,
                        2.5f,
                        300f
                    );
            }
        }

        if (floorCount > 0 &&
            height > 0f)
        {
            return;
        }

        if (floorCount > 0)
        {
            height =
                floorCount *
                FloorHeight;

            return;
        }

        if (height > 0f)
        {
            floorCount =
                Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        height /
                        FloorHeight
                    )
                );

            return;
        }

        // Stable random.
        System.Random random =
            CreateStableRandom(
                way.id + 9811
            );

        floorCount =
            random.Next(
                3,
                8
            );

        height =
            floorCount *
            FloorHeight;
    }

    // =========================================================
    // STABLE MATERIAL INDEX
    // =========================================================

    private int GetStableMaterialIndex(
        Material[] materials,
        long osmId,
        int salt)
    {
        if (materials == null ||
            materials.Length == 0)
        {
            return -1;
        }

        List<int> valid =
            new List<int>();

        for (int i = 0;
             i < materials.Length;
             i++)
        {
            if (materials[i] != null)
            {
                valid.Add(i);
            }
        }

        if (valid.Count == 0)
            return -1;

        System.Random random =
            CreateStableRandom(
                osmId +
                salt
            );

        return
            valid[
                random.Next(
                    0,
                    valid.Count
                )
            ];
    }

    // =========================================================
    // GET MATERIAL
    // =========================================================

    private Material GetMaterial(
        Material[] materials,
        int index)
    {
        if (materials == null)
            return null;

        if (index < 0 ||
            index >= materials.Length)
        {
            return null;
        }

        return
            materials[index];
    }

    // =========================================================
    // CLEAN POLYGON
    // =========================================================

    private List<Vector3> CleanPolygon(
        List<Vector3> source)
    {
        List<Vector3> result =
            new List<Vector3>();

        const float minimumDistance =
            0.08f;

        foreach (Vector3 point
                 in source)
        {
            if (result.Count == 0)
            {
                result.Add(point);
                continue;
            }

            if (Vector3.Distance(
                    result[
                        result.Count - 1
                    ],
                    point)
                >=
                minimumDistance)
            {
                result.Add(point);
            }
        }

        if (result.Count > 2 &&
            Vector3.Distance(
                result[0],
                result[
                    result.Count - 1
                ])
            <
            minimumDistance)
        {
            result.RemoveAt(
                result.Count - 1
            );
        }

        // =====================================================
        // COLLINEAR CLEAN
        // =====================================================

        bool removed = true;

        int safety = 100;

        while (removed &&
               result.Count > 3 &&
               safety-- > 0)
        {
            removed = false;

            for (int i = 0;
                 i < result.Count;
                 i++)
            {
                Vector3 previous =
                    result[
                        (
                            i - 1 +
                            result.Count
                        )
                        %
                        result.Count
                    ];

                Vector3 current =
                    result[i];

                Vector3 next =
                    result[
                        (i + 1)
                        %
                        result.Count
                    ];

                Vector2 a =
                    new Vector2(
                        current.x -
                        previous.x,
                        current.z -
                        previous.z
                    );

                Vector2 b =
                    new Vector2(
                        next.x -
                        current.x,
                        next.z -
                        current.z
                    );

                if (a.sqrMagnitude <
                    0.001f ||
                    b.sqrMagnitude <
                    0.001f)
                {
                    result.RemoveAt(i);

                    removed = true;

                    break;
                }

                a.Normalize();
                b.Normalize();

                float cross =
                    Mathf.Abs(
                        a.x *
                        b.y -
                        a.y *
                        b.x
                    );

                if (cross < 0.005f)
                {
                    result.RemoveAt(i);

                    removed = true;

                    break;
                }
            }
        }

        return
            result;
    }

    // =========================================================
    // TRIANGULATION
    // =========================================================

    private List<int> TriangulatePolygon(
        List<Vector3> points)
    {
        List<int> triangles =
            new List<int>();

        if (points == null ||
            points.Count < 3)
        {
            return triangles;
        }

        List<int> remaining =
            new List<int>();

        for (int i = 0;
             i < points.Count;
             i++)
        {
            remaining.Add(i);
        }

        if (SignedArea(points) < 0f)
        {
            remaining.Reverse();
        }

        int safety =
            points.Count *
            points.Count *
            4;

        while (remaining.Count > 3 &&
               safety-- > 0)
        {
            bool earFound =
                false;

            for (int i = 0;
                 i < remaining.Count;
                 i++)
            {
                int previous =
                    remaining[
                        (
                            i - 1 +
                            remaining.Count
                        )
                        %
                        remaining.Count
                    ];

                int current =
                    remaining[i];

                int next =
                    remaining[
                        (i + 1)
                        %
                        remaining.Count
                    ];

                Vector2 a =
                    ToXZ(
                        points[previous]
                    );

                Vector2 b =
                    ToXZ(
                        points[current]
                    );

                Vector2 c =
                    ToXZ(
                        points[next]
                    );

                if (!IsConvex(
                        a,
                        b,
                        c))
                {
                    continue;
                }

                bool containsPoint =
                    false;

                for (int j = 0;
                     j < remaining.Count;
                     j++)
                {
                    int test =
                        remaining[j];

                    if (test == previous ||
                        test == current ||
                        test == next)
                    {
                        continue;
                    }

                    if (PointInTriangle(
                            ToXZ(
                                points[test]
                            ),
                            a,
                            b,
                            c))
                    {
                        containsPoint = true;
                        break;
                    }
                }

                if (containsPoint)
                    continue;

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);

                remaining.RemoveAt(i);

                earFound = true;
                break;
            }

            if (!earFound)
            {
                return
                    new List<int>();
            }
        }

        if (remaining.Count == 3)
        {
            triangles.Add(
                remaining[0]
            );

            triangles.Add(
                remaining[1]
            );

            triangles.Add(
                remaining[2]
            );
        }

        return
            triangles;
    }

    // =========================================================
    // GEOMETRY HELPERS
    // =========================================================

    private bool IsConvex(
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        return
            Cross2D(
                b - a,
                c - b
            )
            >
            0.00001f;
    }

    private bool PointInTriangle(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        float d1 =
            Cross2D(
                b - a,
                point - a
            );

        float d2 =
            Cross2D(
                c - b,
                point - b
            );

        float d3 =
            Cross2D(
                a - c,
                point - c
            );

        const float epsilon =
            0.00001f;

        return
            d1 >= -epsilon &&
            d2 >= -epsilon &&
            d3 >= -epsilon;
    }

    private float Cross2D(
        Vector2 a,
        Vector2 b)
    {
        return
            a.x *
            b.y -
            a.y *
            b.x;
    }

    private Vector2 ToXZ(
        Vector3 point)
    {
        return
            new Vector2(
                point.x,
                point.z
            );
    }

    private float SignedArea(
        List<Vector3> points)
    {
        float area = 0f;

        for (int i = 0;
             i < points.Count;
             i++)
        {
            Vector3 a =
                points[i];

            Vector3 b =
                points[
                    (i + 1)
                    %
                    points.Count
                ];

            area +=
                a.x *
                b.z -
                b.x *
                a.z;
        }

        return
            area *
            0.5f;
    }

    // =========================================================
    // LONGEST EDGE
    // =========================================================

    private int FindLongestEdge(
        List<Vector3> footprint)
    {
        int result = 0;

        float longest = -1f;

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            Vector3 a =
                footprint[i];

            Vector3 b =
                footprint[
                    (i + 1)
                    %
                    footprint.Count
                ];

            float length =
                Vector3.Distance(
                    a,
                    b
                );

            if (length >
                longest)
            {
                longest = length;
                result = i;
            }
        }

        return
            result;
    }

    // =========================================================
    // RANDOM
    // =========================================================

    private System.Random CreateStableRandom(
        long id)
    {
        int seed =
            unchecked(
                (int)(
                    id ^
                    (id >> 32)
                )
            );

        return
            new System.Random(
                seed
            );
    }

    // =========================================================
    // TAG
    // =========================================================

    private string GetTagLower(
        OSMWay way,
        string key)
    {
        string value =
            way.GetTag(
                key
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return
                string.Empty;
        }

        return
            value
                .Trim()
                .ToLowerInvariant();
    }

    // =========================================================
    // TRIANGLE
    // =========================================================

    private void AddTriangle(
        List<int> list,
        int a,
        int b,
        int c)
    {
        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

    // =========================================================
    // DESTROY
    // =========================================================

    private void DestroyObject(
        GameObject obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(
                obj
            );
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(
                obj
            );
        }
    }

    // =========================================================
    // MESH BUILDER
    // =========================================================

    private class MeshBuilder
    {
        public readonly List<Vector3> vertices =
            new List<Vector3>();

        public readonly List<Vector2> uvs =
            new List<Vector2>();

        // Material 0
        public readonly List<int> facadeTriangles =
            new List<int>();

        // Material 1
        public readonly List<int> glassTriangles =
            new List<int>();

        // Shop material index -> triangles
        public readonly Dictionary<int, List<int>>
            shopTriangles =
                new Dictionary<int, List<int>>();

        // Balcony material index -> triangles
        public readonly Dictionary<int, List<int>>
            balconyTriangles =
                new Dictionary<int, List<int>>();
    }
}


// =============================================================
// BUILDING INFO
// =============================================================

public class BuildingInfo :
    MonoBehaviour
{
    public long osmId;

    public int floorCount;

    public float height;

    public string styleName;

    public int facadeMaterialIndex = -1;

    public int glassMaterialIndex = -1;

    [HideInInspector]
    public Vector3[] footprint;
}