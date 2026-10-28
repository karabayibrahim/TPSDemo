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
    private readonly Material[] roofMaterials;

    // =========================================================
    // GENERAL
    // =========================================================

    private const float FloorHeight = 3f;

    private const float GlassOffset = -0.18f;
    private const float ShopOffset = -0.12f;

    private const int MaxShopMaterialVariantsPerBuilding = 3;
    private const int MaxBalconyMaterialVariantsPerBuilding = 2;

    // =========================================================
    // BALCONY
    // =========================================================

    private const float BalconyDepth = 0.85f;
    private const float BalconyThickness = 0.12f;

    private const float RailHeight = 1f;
    private const float RailThickness = 0.06f;

    // =========================================================
    // TYPES
    // =========================================================

    private enum SurfaceType
    {
        Facade,
        Glass,
        Shop
    }

    private enum BuildingArchetype
    {
        ClassicResidential,
        ModernResidential,
        MixedUse,
        Office,
        GlassPlaza
    }

    private enum RoofType
    {
        Flat,
        Gabled,
        Hipped,
        Pyramidal,
        Skillion,
        Mansard
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
        public float balconyDepth;

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
        Material[] balconyMaterials,
        Material[] roofMaterials)
    {
        this.parent = parent;

        this.facadeMaterials = facadeMaterials;
        this.glassMaterials = glassMaterials;
        this.shopMaterials = shopMaterials;
        this.balconyMaterials = balconyMaterials;
        this.roofMaterials = roofMaterials;
    }

    // =========================================================
    // GENERATE
    // =========================================================

    public GameObject Rebuild(BuildingInfo info, OSMWay sourceWay = null)
    {
        if (info == null || info.footprint == null || info.footprint.Length < 3 ||
            !Enum.TryParse(info.roofType, out RoofType roofType))
            return null;

        BuildingStyle style;
        switch (info.styleName)
        {
            case "ModernResidential": style = ModernResidentialStyle(); break;
            case "MixedUse": style = MixedUseStyle(); break;
            case "Office": style = OfficeStyle(); break;
            case "GlassPlaza": style = GlassPlazaStyle(); break;
            default: style = ClassicResidentialStyle(); break;
        }

        if (info.floorCount >= 5 && info.styleName == "Office" &&
            CreateStableRandom(info.osmId + 6511).NextDouble() < 0.65)
            style = GlassPlazaStyle();
        if (style.archetype == BuildingArchetype.GlassPlaza)
            roofType = RoofType.Flat;
        int childCount = parent.childCount;
        CreateBuilding(new List<Vector3>(info.footprint), info.height, info.floorCount,
            info.osmId, style, roofType, info.roofHeight,
            GetRoofMaterialIndex(sourceWay ?? new OSMWay { id = info.osmId }, info.osmId, roofType));
        if (parent.childCount <= childCount)
            return null;
        Transform replacement = parent.GetChild(parent.childCount - 1);
        replacement.localPosition = info.transform.localPosition;
        replacement.localRotation = info.transform.localRotation;
        replacement.localScale = info.transform.localScale;
        return replacement.gameObject;
    }

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

        // Şimdilik kapalı OSM way.
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

        // Kapalı way'in tekrarlanan son noktasını kaldır.
        if (Vector3.Distance(
                footprint[0],
                footprint[footprint.Count - 1]) < 0.1f)
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

        // CCW standardı.
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

        RoofType roofType =
            SelectRoofType(
                way,
                style,
                footprint
            );

        float roofHeight =
            GetRoofHeight(
                way,
                footprint,
                roofType
            );

        int roofMaterialIndex =
            GetRoofMaterialIndex(
                way,
                way.id,
                roofType
            );

        CreateBuilding(
            footprint,
            height,
            floorCount,
            way.id,
            style,
            roofType,
            roofHeight,
            roofMaterialIndex
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
        BuildingStyle style,
        RoofType roofType,
        float roofHeight,
        int roofMaterialIndex)
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

        info.styleName =
            style.styleName;

        info.roofType =
            roofType.ToString();

        info.roofHeight =
            roofHeight;

        info.roofMaterialIndex =
            roofMaterialIndex;

        info.footprint =
            footprint.ToArray();

        // =====================================================
        // MATERIAL SELECTION
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
                roofType,
                roofHeight,
                shopPalette,
                balconyPalette
            );

        if (result == null ||
            result.mesh == null)
        {
            DestroyObject(building);
            return;
        }

        GameObject meshObject =
            new GameObject("Mesh");

        meshObject.transform.SetParent(
            building.transform,
            false
        );

        MeshFilter meshFilter =
            meshObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer =
            meshObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh =
            result.mesh;

        // =====================================================
        // MATERIAL LIST
        //
        // 0 = facade
        // 1 = glass
        // shop...
        // balcony...
        // roof
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

        if (style.archetype == BuildingArchetype.GlassPlaza)
        {
            facadeMaterial = GetPlazaMaterial(osmId, false);
            glassMaterial = GetPlazaMaterial(osmId, true);
        }

        if (glassMaterial == null)
        {
            glassMaterial =
                facadeMaterial;
        }

        Material roofMaterial =
            GetMaterial(
                roofMaterials,
                roofMaterialIndex
            );

        if (roofMaterial == null)
        {
            roofMaterial =
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

        foreach (int index
                 in result.usedShopMaterialIndices)
        {
            Material material =
                GetMaterial(
                    shopMaterials,
                    index
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

        foreach (int index
                 in result.usedBalconyMaterialIndices)
        {
            Material material =
                GetMaterial(
                    balconyMaterials,
                    index
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

        // =====================================================
        // ROOF MATERIAL
        // =====================================================

        rendererMaterials.Add(
            style.archetype == BuildingArchetype.GlassPlaza ? facadeMaterial : roofMaterial
        );

        meshRenderer.sharedMaterials =
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
        RoofType roofType,
        float roofHeight,
        int[] shopPalette,
        int[] balconyPalette)
    {
        MeshBuilder builder =
            new MeshBuilder();

        System.Random geometryRandom =
            CreateStableRandom(
                osmId + 1009
            );

        System.Random shopRandom =
            CreateStableRandom(
                osmId + 2003
            );

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
                    %
                    footprint.Count
                ];

            Vector3 edge =
                end -
                start;

            edge.y = 0f;

            float edgeLength =
                edge.magnitude;

            if (edgeLength < 0.4f)
                continue;

            Vector3 direction =
                edge.normalized;

            // CCW polygon -> dış taraf sağ.
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
        // ROOF
        // =====================================================

        AddRoof(
            builder,
            footprint,
            height,
            roofHeight,
            roofType
        );

        // =====================================================
        // BOTTOM
        // =====================================================

        AddBottom(
            builder,
            footprint
        );

        if (builder.vertices.Count == 0)
            return null;

        // =====================================================
        // USED MATERIALS
        // =====================================================

        List<int> usedShopIndices =
            new List<int>(
                builder.shopTriangles.Keys
            );

        usedShopIndices.Sort();

        List<int> usedBalconyIndices =
            new List<int>(
                builder.balconyTriangles.Keys
            );

        usedBalconyIndices.Sort();

        // =====================================================
        // UNITY MESH
        // =====================================================

        Mesh mesh =
            new Mesh();

        mesh.name =
            $"Building_{style.styleName}_{roofType}";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(
            builder.vertices
        );

        mesh.SetUVs(
            0,
            builder.uvs
        );

        int shopStart =
            2;

        int balconyStart =
            shopStart +
            usedShopIndices.Count;

        int roofSubMesh =
            balconyStart +
            usedBalconyIndices.Count;

        mesh.subMeshCount =
            roofSubMesh + 1;

        // Facade
        mesh.SetTriangles(
            builder.facadeTriangles,
            0,
            false
        );

        // Glass
        mesh.SetTriangles(
            builder.glassTriangles,
            1,
            false
        );

        // Shops
        for (int i = 0;
             i < usedShopIndices.Count;
             i++)
        {
            mesh.SetTriangles(
                builder.shopTriangles[
                    usedShopIndices[i]
                ],
                shopStart + i,
                false
            );
        }

        // Balconies
        for (int i = 0;
             i < usedBalconyIndices.Count;
             i++)
        {
            mesh.SetTriangles(
                builder.balconyTriangles[
                    usedBalconyIndices[i]
                ],
                balconyStart + i,
                false
            );
        }

        // Roof
        mesh.SetTriangles(
            builder.roofTriangles,
            roofSubMesh,
            false
        );

        OSMInteriorMappingData.Prepare(mesh, usedShopIndices.Count);
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
    // FACADE
    // =========================================================

    private void GenerateFacade(
        MeshBuilder builder, Vector3 edgeStart, Vector3 direction, Vector3 outward,
        float edgeLength, float buildingHeight, int floorCount, bool entranceEdge,
        System.Random geometryRandom, System.Random shopRandom, System.Random balconyRandom,
        BuildingStyle style, int[] shopPalette, int[] balconyPalette)
    {
        int bayCount = Mathf.Max(1, Mathf.RoundToInt(edgeLength / style.targetBayWidth));
        int floors = Mathf.Max(1, floorCount);
        float floorHeight = buildingHeight / floors;
        int balconyPhase = geometryRandom.Next(0, 3);
        float[] upperBays = CreateFacadeBays(edgeLength, bayCount, geometryRandom, 0.24f);
        int groundCount = Mathf.Max(1, Mathf.RoundToInt(edgeLength / RandomRange(geometryRandom, 3.1f, 5.2f)));
        float[] groundBays = CreateFacadeBays(edgeLength, groundCount, geometryRandom, 0.38f);
        var bayStyles = new BuildingStyle[bayCount];
        var offsets = new float[bayCount];
        float projection = RandomRange(geometryRandom, 0.35f, 0.75f);
        float recess = RandomRange(geometryRandom, 0.25f, 0.65f);
        int massPhase = geometryRandom.Next(0, 4);
        for (int bay = 0; bay < bayCount; bay++)
        {
            BuildingStyle variant = style;
            variant.windowWidth *= RandomRange(geometryRandom, 0.78f, 1.28f);
            variant.windowHeight *= RandomRange(geometryRandom, 0.86f, 1.16f);
            variant.windowBottom *= RandomRange(geometryRandom, 0.85f, 1.12f);
            variant.balconyMaxWidth *= RandomRange(geometryRandom, 0.82f, 1.3f);
            variant.balconyDepth = RandomRange(geometryRandom, 0.85f, 1.4f);
            bayStyles[bay] = variant;
            // Keep corners on the OSM footprint; vary interior vertical volumes.
            if (bay > 0 && bay < bayCount - 1)
            {
                int rhythm = (bay + massPhase) % 4;
                offsets[bay] = rhythm == 0 ? projection : rhythm == 2 ? -recess : 0f;
            }
        }

        for (int floor = 0; floor < floors; floor++)
        {
            float bottom = floor * floorHeight;
            float top = (floor + 1) * floorHeight;
            if (top - bottom < 0.1f) continue;
            float[] divisions = floor == 0 ? groundBays : upperBays;
            int count = divisions.Length - 1;
            for (int bay = 0; bay < count; bay++)
            {
                float start = divisions[bay];
                float end = divisions[bay + 1];
                float width = end - start;
                BuildingStyle cellStyle = floor == 0 ? style : bayStyles[bay];
                float offset = floor == 0 ? 0f : offsets[bay];
                Vector3 origin = edgeStart + outward * offset;

                if (floor > 0)
                {
                    float previous = bay == 0 ? 0f : offsets[bay - 1];
                    if (Mathf.Abs(offset - previous) > 0.001f)
                    {
                        Vector3 boundary = edgeStart + direction * start;
                        Vector3 a = boundary + outward * previous + Vector3.up * bottom;
                        Vector3 b = boundary + outward * offset + Vector3.up * bottom;
                        AddDetailQuad(builder, a, b, b + Vector3.up * (top - bottom),
                            a + Vector3.up * (top - bottom), offset > previous ? -direction : direction);
                    }
                    if (Mathf.Abs(offset) > 0.001f)
                    {
                        // Close projecting soffits and recessed terraces at ground and roof.
                        Vector3 a = edgeStart + direction * start;
                        Vector3 b = edgeStart + direction * end;
                        if (floor == 1)
                            AddDetailQuad(builder, a + Vector3.up * bottom, b + Vector3.up * bottom,
                                b + outward * offset + Vector3.up * bottom,
                                a + outward * offset + Vector3.up * bottom,
                                offset > 0f ? Vector3.down : Vector3.up);
                        if (floor == floors - 1)
                            AddDetailQuad(builder, a + Vector3.up * top, b + Vector3.up * top,
                                b + outward * offset + Vector3.up * top,
                                a + outward * offset + Vector3.up * top,
                                offset > 0f ? Vector3.up : Vector3.down);
                    }
                }

                if (floor == 0 && cellStyle.fullGlassGroundFloor)
                {
                    AddCurtainGlassCell(builder, origin, direction, outward, start, end, bottom, top, cellStyle);
                    if (entranceEdge && bay == count / 2)
                    {
                        Vector3 door = origin + direction * ((start + end) * 0.5f);
                        AddOrientedBox(builder, door + Vector3.up * 2.5f + outward * 0.55f,
                            direction, outward, Mathf.Min(width, 3f), 0.12f, 1.25f, -1);
                        AddOrientedBox(builder, door + Vector3.up * 2.3f,
                            direction, outward, Mathf.Min(width, 2f), 0.06f, 0.15f, -1);
                    }
                }
                else if (floor == 0)
                {
                    cellStyle.shopHeight *= RandomRange(geometryRandom, 0.83f, 1.12f);
                    cellStyle.shopSideMargin *= RandomRange(geometryRandom, 0.7f, 1.6f);
                    if (entranceEdge && bay == count / 2)
                        AddEntranceCell(builder, origin, direction, outward, start, end, bottom, top, cellStyle);
                    else if (width >= 1.6f && geometryRandom.NextDouble() < cellStyle.shopChance)
                        AddShopCell(builder, origin, direction, outward, start, end, bottom, top,
                            cellStyle, PickMaterialFromPalette(shopPalette, shopRandom));
                    else if (cellStyle.fullGlassGroundFloor)
                        AddCurtainGlassCell(builder, origin, direction, outward, start, end, bottom, top, cellStyle);
                    else
                        AddWindowCell(builder, origin, direction, outward, start, end, bottom, top, false, cellStyle, -1);
                }
                else if (cellStyle.fullGlassUpperFloors)
                    AddCurtainGlassCell(builder, origin, direction, outward, start, end, bottom, top, cellStyle);
                else
                {
                    bool balcony = cellStyle.allowBalcony && width >= 1.75f &&
                        cellStyle.balconyChance > 0f && (bay + balconyPhase) % 3 == 1;
                    AddWindowCell(builder, origin, direction, outward, start, end, bottom, top,
                        balcony, cellStyle, balcony ? PickMaterialFromPalette(balconyPalette, balconyRandom) : -1);
                }
            }
        }
    }

    private float[] CreateFacadeBays(float length, int count, System.Random random, float variation)
    {
        float[] divisions = new float[count + 1];
        float total = 0f;
        for (int i = 1; i <= count; i++)
        {
            total += RandomRange(random, 1f - variation, 1f + variation);
            divisions[i] = total;
        }
        for (int i = 1; i < count; i++) divisions[i] = divisions[i] / total * length;
        divisions[count] = length;
        return divisions;
    }

    // =========================================================
    // WINDOW
    // =========================================================

    // A loggia is an opening in the facade with a recessed rear wall, not a box
    // attached to an otherwise closed wall. Keep its sides inside the bay.
    private void AddRecessedBalcony(MeshBuilder builder, Vector3 origin,
        Vector3 direction, Vector3 outward, float start, float end,
        float bottom, float top, BuildingStyle style, int materialIndex)
    {
        float width = Mathf.Min(style.balconyMaxWidth, end - start - 0.6f);
        if (width < 0.9f)
        {
            AddWindowCell(builder, origin, direction, outward, start, end,
                bottom, top, false, style, -1);
            return;
        }

        float center = (start + end) * 0.5f;
        float left = center - width * 0.5f;
        float right = center + width * 0.5f;
        float sill = bottom + 0.12f;
        float head = top - 0.28f;
        float depth = Mathf.Min(style.balconyDepth > 0f ? style.balconyDepth : 1.15f, (end - start) * 0.4f);
        AddSolidCell(builder, origin, direction, outward, start, left, bottom, top);
        AddSolidCell(builder, origin, direction, outward, right, end, bottom, top);
        AddSolidCell(builder, origin, direction, outward, left, right, bottom, sill);
        AddSolidCell(builder, origin, direction, outward, left, right, head, top);
        AddOpeningReveals(builder, origin, direction, outward, left, right, sill, head, depth);

        Vector3 rear = origin - outward * depth;
        AddOpeningCell(builder, rear, direction, outward, left, right, sill, head,
            Mathf.Min(width - 0.28f, 1.8f), sill + 0.04f,
            Mathf.Min(2.15f, head - sill - 0.12f), GlassOffset, SurfaceType.Glass);

        // Slim metal-style rails leave the recess visible and cast real shadows.
        Vector3 railOrigin = origin + direction * center - outward * 0.08f;
        AddOrientedBox(builder, railOrigin + Vector3.up * (sill + RailHeight),
            direction, outward, width, RailThickness, RailThickness, materialIndex);
        int posts = Mathf.Max(2, Mathf.CeilToInt(width / 0.22f));
        for (int i = 0; i <= posts; i++)
        {
            Vector3 post = railOrigin + direction * Mathf.Lerp(-width * 0.5f, width * 0.5f, (float)i / posts);
            post.y += sill + RailHeight * 0.5f;
            AddOrientedBox(builder, post, direction, outward,
                0.035f, RailHeight, 0.035f, materialIndex);
        }
    }

    private void AddOpeningReveals(MeshBuilder builder, Vector3 origin,
        Vector3 direction, Vector3 outward, float left, float right,
        float bottom, float top, float depth)
    {
        Vector3 a = origin + direction * left + Vector3.up * bottom;
        Vector3 b = origin + direction * right + Vector3.up * bottom;
        Vector3 c = origin + direction * right + Vector3.up * top;
        Vector3 d = origin + direction * left + Vector3.up * top;
        Vector3 inset = -outward * depth;
        AddDetailQuad(builder, a, a + inset, d + inset, d, direction);
        AddDetailQuad(builder, b, c, c + inset, b + inset, -direction);
        AddDetailQuad(builder, a, b, b + inset, a + inset, Vector3.up);
        AddDetailQuad(builder, d, d + inset, c + inset, c, Vector3.down);
    }

    private void AddDetailQuad(MeshBuilder builder, Vector3 a, Vector3 b,
        Vector3 c, Vector3 d, Vector3 normal)
    {
        int index = builder.vertices.Count;
        builder.vertices.AddRange(new[] { a, b, c, d });
        builder.uvs.AddRange(new[] { Vector2.zero,
            new Vector2(Vector3.Distance(a, b) / 3f, 0f),
            new Vector2(Vector3.Distance(a, b) / 3f, Vector3.Distance(b, c) / 3f),
            new Vector2(0f, Vector3.Distance(b, c) / 3f) });
        AddQuadExpectedNormal(builder, builder.facadeTriangles,
            index, index + 1, index + 2, index + 3, normal);
    }

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
        if (balcony && floorTop - floorBottom >= 2.4f)
        {
            AddRecessedBalcony(builder, edgeStart, direction, outward,
                cellStart, cellEnd, floorBottom, floorTop, style, balconyMaterialIndex);
            return;
        }

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

        if (openingWidth < 0.5f)
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

        if (balcony)
        {
            // Balkon kapısı.
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

        if (openingHeight < 0.5f)
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

        if (!balcony)
            return;

        float balconyWidth =
            Mathf.Min(
                style.balconyMaxWidth,
                cellWidth -
                0.25f
            );

        if (balconyWidth < 0.9f)
            return;

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

    // =========================================================
    // SHOP
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
                0.2f
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
        MeshBuilder builder, Vector3 origin, Vector3 direction, Vector3 outward,
        float start, float end, float bottom, float top, BuildingStyle style)
    {
        if (end - start < 0.1f || top - bottom < 0.1f) return;
        AddFacadeQuad(builder, origin, direction, outward, start, end,
            bottom, top, -0.045f, SurfaceType.Glass);
        float center = (start + end) * 0.5f;
        float midHeight = (bottom + top) * 0.5f;
        // Narrow aluminum mullions instead of plaster window surrounds.
        foreach (float x in new[] { start, center, end })
            AddOrientedBox(builder, origin + direction * x + Vector3.up * midHeight,
                direction, outward, 0.045f, top - bottom, 0.12f, -1);
        AddOrientedBox(builder, origin + direction * center + Vector3.up * top,
            direction, outward, end - start, 0.09f, 0.13f, -1);
        AddOrientedBox(builder, origin + direction * center + Vector3.up * (bottom + 0.16f),
            direction, outward, end - start, 0.32f, 0.08f, -1);
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
        float width =
            Mathf.Min(
                style.entranceWidth,
                (
                    cellEnd -
                    cellStart
                ) -
                0.3f
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
                0.1f
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
    // OPENING
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

        float left =
            center -
            openingWidth *
            0.5f;

        float right =
            center +
            openingWidth *
            0.5f;

        left =
            Mathf.Clamp(
                left,
                cellStart,
                cellEnd
            );

        right =
            Mathf.Clamp(
                right,
                cellStart,
                cellEnd
            );

        float top =
            Mathf.Min(
                openingBottom +
                openingHeight,
                cellTop
            );

        // Left wall
        if (left >
            cellStart + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                cellStart,
                left,
                cellBottom,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        // Right wall
        if (cellEnd >
            right + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                right,
                cellEnd,
                cellBottom,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        // Bottom
        if (openingBottom >
            cellBottom + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                left,
                right,
                cellBottom,
                openingBottom,
                0f,
                SurfaceType.Facade
            );
        }

        // Top
        if (cellTop >
            top + 0.005f)
        {
            AddFacadeQuad(
                builder,
                edgeStart,
                direction,
                outward,
                left,
                right,
                top,
                cellTop,
                0f,
                SurfaceType.Facade
            );
        }

        if (surfaceOffset < 0f)
        {
            AddOpeningReveals(builder, edgeStart, direction, outward,
                left, right, openingBottom, top, -surfaceOffset);
            float frame = 0.055f;
            Vector3 centerPoint = edgeStart + direction * center;
            AddOrientedBox(builder, centerPoint + Vector3.up * (openingBottom - frame * 0.5f),
                direction, outward, right - left + 0.16f, frame, 0.26f, -1);
            AddOrientedBox(builder, centerPoint + Vector3.up * (top + frame * 0.5f),
                direction, outward, right - left + frame * 2f, frame, 0.1f, -1);
            foreach (float x in new[] { left - frame * 0.5f, right + frame * 0.5f, center })
            {
                Vector3 position = edgeStart + direction * x + Vector3.up * ((openingBottom + top) * 0.5f);
                AddOrientedBox(builder, position, direction, outward,
                    frame, top - openingBottom, 0.1f, -1);
            }
        }

        // Window/shop surface
        AddFacadeQuad(
            builder,
            edgeStart,
            direction,
            outward,
            left,
            right,
            openingBottom,
            top,
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
        float start,
        float end,
        float bottom,
        float top)
    {
        AddFacadeQuad(
            builder,
            edgeStart,
            direction,
            outward,
            start,
            end,
            bottom,
            top,
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
            return;

        if (verticalEnd <=
            verticalStart)
            return;

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

        builder.vertices.Add(bottomLeft);
        builder.vertices.Add(bottomRight);
        builder.vertices.Add(topLeft);
        builder.vertices.Add(topRight);

        float width =
            horizontalEnd -
            horizontalStart;

        float height =
            verticalEnd -
            verticalStart;

        if (surfaceType == SurfaceType.Facade)
        {
            // Continuous coordinates keep plaster and brick courses aligned
            // across windows and wall pieces instead of restarting every quad.
            builder.uvs.Add(new Vector2(horizontalStart / 3f, verticalStart / 3f));
            builder.uvs.Add(new Vector2(horizontalEnd / 3f, verticalStart / 3f));
            builder.uvs.Add(new Vector2(horizontalStart / 3f, verticalEnd / 3f));
            builder.uvs.Add(new Vector2(horizontalEnd / 3f, verticalEnd / 3f));
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

    private List<int> GetTriangleList(
        MeshBuilder builder,
        SurfaceType type,
        int shopMaterialIndex)
    {
        if (type ==
            SurfaceType.Glass)
        {
            return
                builder.glassTriangles;
        }

        if (type ==
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

            return triangles;
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
        int materialIndex)
    {
        Vector3 wallCenter =
            edgeStart +
            direction *
            centerDistance;

        // Slab
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
            materialIndex
        );

        // Front rail
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
            materialIndex
        );

        // Left rail
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
            materialIndex
        );

        // Right rail
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
            materialIndex
        );
    }

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

        Vector3 r =
            right *
            sizeX *
            0.5f;

        Vector3 u =
            Vector3.up *
            sizeY *
            0.5f;

        Vector3 f =
            forward *
            sizeZ *
            0.5f;

        int start =
            builder.vertices.Count;

        builder.vertices.Add(
            center - r - u - f
        );

        builder.vertices.Add(
            center + r - u - f
        );

        builder.vertices.Add(
            center - r + u - f
        );

        builder.vertices.Add(
            center + r + u - f
        );

        builder.vertices.Add(
            center - r - u + f
        );

        builder.vertices.Add(
            center + r - u + f
        );

        builder.vertices.Add(
            center - r + u + f
        );

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

        // Back
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

        // Front
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

        // Left
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

        // Right
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

        // Bottom
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

        // Top
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

    private List<int> GetBalconyTriangleList(
        MeshBuilder builder,
        int materialIndex)
    {
        if (materialIndex < 0)
        {
            return
                builder.facadeTriangles;
        }

        if (!builder.balconyTriangles.TryGetValue(
                materialIndex,
                out List<int> triangles))
        {
            triangles =
                new List<int>();

            builder.balconyTriangles.Add(
                materialIndex,
                triangles
            );
        }

        return triangles;
    }

    // =========================================================
    // ROOF TYPE
    // =========================================================

    private RoofType SelectRoofType(
        OSMWay way,
        BuildingStyle style,
        List<Vector3> footprint)
    {
        if (style.archetype == BuildingArchetype.GlassPlaza)
            return RoofType.Flat;

        string osmShape =
            GetTagLower(
                way,
                "roof:shape"
            );

        // =====================================================
        // OSM FIRST
        // =====================================================

        if (!string.IsNullOrEmpty(
                osmShape))
        {
            RoofType? requested =
                null;

            switch (osmShape)
            {
                case "flat":

                    requested =
                        RoofType.Flat;

                    break;

                case "gabled":
                case "gable":

                    requested =
                        RoofType.Gabled;

                    break;

                case "hipped":
                case "half-hipped":

                    requested =
                        RoofType.Hipped;

                    break;

                case "pyramidal":

                    requested =
                        RoofType.Pyramidal;

                    break;

                case "skillion":
                case "shed":

                    requested =
                        RoofType.Skillion;

                    break;

                case "mansard":
                case "gambrel":

                    requested =
                        RoofType.Mansard;

                    break;
            }

            if (requested.HasValue)
            {
                if (CanUseRoofType(
                        footprint,
                        requested.Value))
                {
                    return
                        requested.Value;
                }

                // Önceden burada Flat'e dönüyordu.
                // Artık karmaşık footprint -> Skillion.
                if (requested.Value !=
                    RoofType.Flat)
                {
                    return
                        RoofType.Skillion;
                }

                return
                    RoofType.Flat;
            }
        }

        // =====================================================
        // PROCEDURAL DISTRIBUTION
        // =====================================================

        System.Random random =
            CreateStableRandom(
                way.id + 14111
            );

        int roll =
            random.Next(
                0,
                100
            );

        RoofType type;

        switch (style.archetype)
        {
            // =================================================
            // CLASSIC RESIDENTIAL
            //
            // Flat       %8
            // Gabled    %27
            // Hipped    %35
            // Pyramidal %12
            // Mansard   %13
            // Skillion   %5
            // =================================================

            case BuildingArchetype.ClassicResidential:

                if (roll < 8)
                    type = RoofType.Flat;

                else if (roll < 35)
                    type = RoofType.Gabled;

                else if (roll < 70)
                    type = RoofType.Hipped;

                else if (roll < 82)
                    type = RoofType.Pyramidal;

                else if (roll < 95)
                    type = RoofType.Mansard;

                else
                    type = RoofType.Skillion;

                break;

            // =================================================
            // MODERN RESIDENTIAL
            //
            // Flat       %18
            // Hipped     %28
            // Gabled     %18
            // Skillion   %20
            // Pyramidal  %10
            // Mansard     %6
            // =================================================

            case BuildingArchetype.ModernResidential:

                if (roll < 18)
                    type = RoofType.Flat;

                else if (roll < 46)
                    type = RoofType.Hipped;

                else if (roll < 64)
                    type = RoofType.Gabled;

                else if (roll < 84)
                    type = RoofType.Skillion;

                else if (roll < 94)
                    type = RoofType.Pyramidal;

                else
                    type = RoofType.Mansard;

                break;

            // =================================================
            // MIXED USE
            // =================================================

            case BuildingArchetype.MixedUse:

                if (roll < 25)
                    type = RoofType.Flat;

                else if (roll < 50)
                    type = RoofType.Hipped;

                else if (roll < 68)
                    type = RoofType.Gabled;

                else if (roll < 86)
                    type = RoofType.Skillion;

                else if (roll < 95)
                    type = RoofType.Pyramidal;

                else
                    type = RoofType.Mansard;

                break;

            // =================================================
            // OFFICE
            // =================================================

            case BuildingArchetype.Office:

                if (roll < 55)
                    type = RoofType.Flat;

                else if (roll < 80)
                    type = RoofType.Skillion;

                else if (roll < 90)
                    type = RoofType.Hipped;

                else
                    type = RoofType.Pyramidal;

                break;

            // =================================================
            // PLAZA
            // =================================================

            case BuildingArchetype.GlassPlaza:

                if (roll < 78)
                    type = RoofType.Flat;

                else
                    type = RoofType.Skillion;

                break;

            default:

                type =
                    RoofType.Hipped;

                break;
        }

        // =====================================================
        // FALLBACK
        // =====================================================

        if (!CanUseRoofType(
                footprint,
                type))
        {
            if (type !=
                RoofType.Flat)
            {
                return
                    RoofType.Skillion;
            }
        }

        return type;
    }

    // =========================================================
    // ROOF COMPATIBILITY
    // =========================================================

    private bool CanUseRoofType(
        List<Vector3> footprint,
        RoofType roofType)
    {
        if (footprint == null ||
            footprint.Count < 3)
        {
            return false;
        }

        switch (roofType)
        {
            case RoofType.Flat:

                return true;

            case RoofType.Skillion:

                // Basit polygonların çoğunda çalışır.
                return true;

            case RoofType.Pyramidal:

                return
                    IsConvexPolygon(
                        footprint
                    );

            case RoofType.Gabled:
            case RoofType.Hipped:
            case RoofType.Mansard:

                return
                    footprint.Count == 4 &&
                    IsConvexPolygon(
                        footprint
                    );

            default:

                return false;
        }
    }

    private bool IsConvexPolygon(
        List<Vector3> polygon)
    {
        if (polygon == null ||
            polygon.Count < 3)
        {
            return false;
        }

        float sign =
            0f;

        for (int i = 0;
             i < polygon.Count;
             i++)
        {
            Vector2 a =
                ToXZ(
                    polygon[i]
                );

            Vector2 b =
                ToXZ(
                    polygon[
                        (i + 1)
                        %
                        polygon.Count
                    ]
                );

            Vector2 c =
                ToXZ(
                    polygon[
                        (i + 2)
                        %
                        polygon.Count
                    ]
                );

            float cross =
                Cross2D(
                    b - a,
                    c - b
                );

            if (Mathf.Abs(cross) <
                0.0001f)
            {
                continue;
            }

            float currentSign =
                Mathf.Sign(
                    cross
                );

            if (sign == 0f)
            {
                sign =
                    currentSign;
            }
            else if (currentSign !=
                     sign)
            {
                return false;
            }
        }

        return true;
    }

    // =========================================================
    // ROOF HEIGHT
    // =========================================================

    private float GetRoofHeight(
        OSMWay way,
        List<Vector3> footprint,
        RoofType roofType)
    {
        if (roofType ==
            RoofType.Flat)
        {
            return 0f;
        }

        string roofHeightTag =
            way.GetTag(
                "roof:height"
            );

        if (TryParseMeters(
                roofHeightTag,
                out float osmHeight))
        {
            return
                Mathf.Clamp(
                    osmHeight,
                    0.4f,
                    8f
                );
        }

        float minX =
            float.MaxValue;

        float maxX =
            float.MinValue;

        float minZ =
            float.MaxValue;

        float maxZ =
            float.MinValue;

        foreach (Vector3 point
                 in footprint)
        {
            minX =
                Mathf.Min(
                    minX,
                    point.x
                );

            maxX =
                Mathf.Max(
                    maxX,
                    point.x
                );

            minZ =
                Mathf.Min(
                    minZ,
                    point.z
                );

            maxZ =
                Mathf.Max(
                    maxZ,
                    point.z
                );
        }

        float shortestDimension =
            Mathf.Max(
                1f,
                Mathf.Min(
                    maxX - minX,
                    maxZ - minZ
                )
            );

        System.Random random =
            CreateStableRandom(
                way.id + 17777
            );

        float multiplier;

        switch (roofType)
        {
            case RoofType.Gabled:

                multiplier =
                    RandomRange(
                        random,
                        0.25f,
                        0.42f
                    );

                break;

            case RoofType.Hipped:

                multiplier =
                    RandomRange(
                        random,
                        0.22f,
                        0.38f
                    );

                break;

            case RoofType.Pyramidal:

                multiplier =
                    RandomRange(
                        random,
                        0.24f,
                        0.40f
                    );

                break;

            case RoofType.Mansard:

                multiplier =
                    RandomRange(
                        random,
                        0.32f,
                        0.50f
                    );

                break;

            case RoofType.Skillion:

                multiplier =
                    RandomRange(
                        random,
                        0.12f,
                        0.25f
                    );

                break;

            default:

                multiplier =
                    0.25f;

                break;
        }

        return
            Mathf.Clamp(
                shortestDimension *
                multiplier,
                0.8f,
                5.5f
            );
    }

    // =========================================================
    // ADD ROOF
    // =========================================================

    private void AddRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float buildingHeight,
        float roofHeight,
        RoofType roofType)
    {
        switch (roofType)
        {
            case RoofType.Gabled:

                if (CanUseRoofType(
                        footprint,
                        RoofType.Gabled))
                {
                    AddGabledRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }
                else
                {
                    AddSkillionRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }

                break;

            case RoofType.Hipped:

                if (CanUseRoofType(
                        footprint,
                        RoofType.Hipped))
                {
                    AddHippedRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }
                else
                {
                    AddSkillionRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }

                break;

            case RoofType.Pyramidal:

                if (CanUseRoofType(
                        footprint,
                        RoofType.Pyramidal))
                {
                    AddPyramidalRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }
                else
                {
                    AddSkillionRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }

                break;

            case RoofType.Skillion:

                AddSkillionRoof(
                    builder,
                    footprint,
                    buildingHeight,
                    roofHeight
                );

                break;

            case RoofType.Mansard:

                if (CanUseRoofType(
                        footprint,
                        RoofType.Mansard))
                {
                    AddMansardRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }
                else
                {
                    AddSkillionRoof(
                        builder,
                        footprint,
                        buildingHeight,
                        roofHeight
                    );
                }

                break;

            case RoofType.Flat:
            default:

                AddFlatRoof(
                    builder,
                    footprint,
                    buildingHeight
                );

                break;
        }
    }

    // =========================================================
    // FLAT ROOF
    // =========================================================

    private void AddFlatRoof(
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
                    point.x * 0.2f,
                    point.z * 0.2f
                )
            );
        }

        for (int i = 0;
             i < triangles.Count;
             i += 3)
        {
            AddTriangleExpectedNormal(
                builder,
                builder.roofTriangles,
                start + triangles[i],
                start + triangles[i + 1],
                start + triangles[i + 2],
                Vector3.up
            );
        }
    }

    // =========================================================
    // GABLED ROOF
    // =========================================================

    private void AddGabledRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float baseHeight,
        float roofHeight)
    {
        GetCanonicalRoofQuad(
            footprint,
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3
        );

        p0.y = baseHeight;
        p1.y = baseHeight;
        p2.y = baseHeight;
        p3.y = baseHeight;

        Vector3 endA =
            (
                p3 +
                p0
            ) *
            0.5f;

        Vector3 endB =
            (
                p1 +
                p2
            ) *
            0.5f;

        Vector3 ridgeA =
            endA +
            Vector3.up *
            roofHeight;

        Vector3 ridgeB =
            endB +
            Vector3.up *
            roofHeight;

        int i0 =
            AddRoofVertex(
                builder,
                p0
            );

        int i1 =
            AddRoofVertex(
                builder,
                p1
            );

        int i2 =
            AddRoofVertex(
                builder,
                p2
            );

        int i3 =
            AddRoofVertex(
                builder,
                p3
            );

        int rA =
            AddRoofVertex(
                builder,
                ridgeA
            );

        int rB =
            AddRoofVertex(
                builder,
                ridgeB
            );

        Vector3 outward0 =
            GetEdgeOutward(
                p0,
                p1
            );

        Vector3 outward2 =
            GetEdgeOutward(
                p2,
                p3
            );

        // Roof side 1
        AddQuadExpectedNormal(
            builder,
            builder.roofTriangles,
            i0,
            i1,
            rB,
            rA,
            (
                outward0 +
                Vector3.up
            ).normalized
        );

        // Roof side 2
        AddQuadExpectedNormal(
            builder,
            builder.roofTriangles,
            i2,
            i3,
            rA,
            rB,
            (
                outward2 +
                Vector3.up
            ).normalized
        );

        // =====================================================
        // GABLE FACADE TRIANGLES
        // =====================================================

        Vector3 outward1 =
            GetEdgeOutward(
                p1,
                p2
            );

        Vector3 outward3 =
            GetEdgeOutward(
                p3,
                p0
            );

        int fP1 =
            AddFacadeVertex(
                builder,
                p1
            );

        int fP2 =
            AddFacadeVertex(
                builder,
                p2
            );

        int fRidgeB =
            AddFacadeVertex(
                builder,
                ridgeB
            );

        AddTriangleExpectedNormal(
            builder,
            builder.facadeTriangles,
            fP1,
            fP2,
            fRidgeB,
            outward1
        );

        int fP3 =
            AddFacadeVertex(
                builder,
                p3
            );

        int fP0 =
            AddFacadeVertex(
                builder,
                p0
            );

        int fRidgeA =
            AddFacadeVertex(
                builder,
                ridgeA
            );

        AddTriangleExpectedNormal(
            builder,
            builder.facadeTriangles,
            fP3,
            fP0,
            fRidgeA,
            outward3
        );
    }

    // =========================================================
    // HIPPED ROOF
    // =========================================================

    private void AddHippedRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float baseHeight,
        float roofHeight)
    {
        GetCanonicalRoofQuad(
            footprint,
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3
        );

        p0.y = baseHeight;
        p1.y = baseHeight;
        p2.y = baseHeight;
        p3.y = baseHeight;

        Vector3 endA =
            (
                p3 +
                p0
            ) *
            0.5f;

        Vector3 endB =
            (
                p1 +
                p2
            ) *
            0.5f;

        Vector3 ridgeAxis =
            endB -
            endA;

        ridgeAxis.y = 0f;

        float longLength =
            ridgeAxis.magnitude;

        if (longLength < 0.1f)
        {
            AddPyramidalRoof(
                builder,
                footprint,
                baseHeight,
                roofHeight
            );

            return;
        }

        ridgeAxis.Normalize();

        float shortWidth =
            (
                Vector3.Distance(
                    p1,
                    p2
                ) +
                Vector3.Distance(
                    p3,
                    p0
                )
            ) *
            0.5f;

        float ridgeLength =
            Mathf.Max(
                0f,
                longLength -
                shortWidth
            );

        // Kareye yakınsa gerçek kırma/piramit daha iyi görünür.
        if (ridgeLength <
            shortWidth *
            0.08f)
        {
            AddPyramidalRoof(
                builder,
                footprint,
                baseHeight,
                roofHeight
            );

            return;
        }

        Vector3 center =
            (
                endA +
                endB
            ) *
            0.5f;

        Vector3 ridgeA =
            center -
            ridgeAxis *
            ridgeLength *
            0.5f +
            Vector3.up *
            roofHeight;

        Vector3 ridgeB =
            center +
            ridgeAxis *
            ridgeLength *
            0.5f +
            Vector3.up *
            roofHeight;

        int i0 =
            AddRoofVertex(
                builder,
                p0
            );

        int i1 =
            AddRoofVertex(
                builder,
                p1
            );

        int i2 =
            AddRoofVertex(
                builder,
                p2
            );

        int i3 =
            AddRoofVertex(
                builder,
                p3
            );

        int rA =
            AddRoofVertex(
                builder,
                ridgeA
            );

        int rB =
            AddRoofVertex(
                builder,
                ridgeB
            );

        Vector3 outward0 =
            GetEdgeOutward(p0, p1);

        Vector3 outward1 =
            GetEdgeOutward(p1, p2);

        Vector3 outward2 =
            GetEdgeOutward(p2, p3);

        Vector3 outward3 =
            GetEdgeOutward(p3, p0);

        // Long side 1
        AddQuadExpectedNormal(
            builder,
            builder.roofTriangles,
            i0,
            i1,
            rB,
            rA,
            (
                outward0 +
                Vector3.up
            ).normalized
        );

        // End hip
        AddTriangleExpectedNormal(
            builder,
            builder.roofTriangles,
            i1,
            i2,
            rB,
            (
                outward1 +
                Vector3.up
            ).normalized
        );

        // Long side 2
        AddQuadExpectedNormal(
            builder,
            builder.roofTriangles,
            i2,
            i3,
            rA,
            rB,
            (
                outward2 +
                Vector3.up
            ).normalized
        );

        // End hip
        AddTriangleExpectedNormal(
            builder,
            builder.roofTriangles,
            i3,
            i0,
            rA,
            (
                outward3 +
                Vector3.up
            ).normalized
        );
    }

    // =========================================================
    // PYRAMIDAL ROOF
    // =========================================================

    private void AddPyramidalRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float baseHeight,
        float roofHeight)
    {
        if (!IsConvexPolygon(
                footprint))
        {
            AddSkillionRoof(
                builder,
                footprint,
                baseHeight,
                roofHeight
            );

            return;
        }

        Vector3 center =
            Vector3.zero;

        foreach (Vector3 point
                 in footprint)
        {
            center += point;
        }

        center /=
            footprint.Count;

        center.y =
            baseHeight +
            roofHeight;

        List<int> baseIndices =
            new List<int>();

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            Vector3 point =
                footprint[i];

            point.y =
                baseHeight;

            baseIndices.Add(
                AddRoofVertex(
                    builder,
                    point
                )
            );
        }

        int apex =
            AddRoofVertex(
                builder,
                center
            );

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            int next =
                (i + 1)
                %
                footprint.Count;

            Vector3 outward =
                GetEdgeOutward(
                    footprint[i],
                    footprint[next]
                );

            AddTriangleExpectedNormal(
                builder,
                builder.roofTriangles,
                baseIndices[i],
                baseIndices[next],
                apex,
                (
                    outward +
                    Vector3.up
                ).normalized
            );
        }
    }

    // =========================================================
    // SKILLION / SHED ROOF
    //
    // Karmaşık polygonlarda da çalışır.
    // =========================================================

    private void AddSkillionRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float baseHeight,
        float roofHeight)
    {
        if (footprint == null ||
            footprint.Count < 3)
        {
            return;
        }

        List<int> polygonTriangles =
            TriangulatePolygon(
                footprint
            );

        if (polygonTriangles.Count < 3)
        {
            AddFlatRoof(
                builder,
                footprint,
                baseHeight
            );

            return;
        }

        // En uzun kenara dik yönde eğim.
        int longestEdge =
            FindLongestEdge(
                footprint
            );

        Vector3 edgeA =
            footprint[
                longestEdge
            ];

        Vector3 edgeB =
            footprint[
                (longestEdge + 1)
                %
                footprint.Count
            ];

        Vector3 longDirection =
            edgeB -
            edgeA;

        longDirection.y = 0f;

        if (longDirection.sqrMagnitude <
            0.001f)
        {
            AddFlatRoof(
                builder,
                footprint,
                baseHeight
            );

            return;
        }

        longDirection.Normalize();

        Vector3 slopeDirection =
            new Vector3(
                -longDirection.z,
                0f,
                longDirection.x
            );

        float minProjection =
            float.MaxValue;

        float maxProjection =
            float.MinValue;

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            float projection =
                Vector3.Dot(
                    footprint[i],
                    slopeDirection
                );

            minProjection =
                Mathf.Min(
                    minProjection,
                    projection
                );

            maxProjection =
                Mathf.Max(
                    maxProjection,
                    projection
                );
        }

        float range =
            maxProjection -
            minProjection;

        if (range < 0.01f)
        {
            AddFlatRoof(
                builder,
                footprint,
                baseHeight
            );

            return;
        }

        List<int> roofIndices =
            new List<int>();

        List<float> vertexHeights =
            new List<float>();

        // =====================================================
        // TOP ROOF POINTS
        // =====================================================

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            Vector3 point =
                footprint[i];

            float projection =
                Vector3.Dot(
                    point,
                    slopeDirection
                );

            float normalized =
                Mathf.InverseLerp(
                    minProjection,
                    maxProjection,
                    projection
                );

            float y =
                baseHeight +
                normalized *
                roofHeight;

            Vector3 roofPoint =
                new Vector3(
                    point.x,
                    y,
                    point.z
                );

            roofIndices.Add(
                AddRoofVertex(
                    builder,
                    roofPoint
                )
            );

            vertexHeights.Add(
                y
            );
        }

        // =====================================================
        // TOP SURFACE
        // =====================================================

        for (int i = 0;
             i < polygonTriangles.Count;
             i += 3)
        {
            int a =
                roofIndices[
                    polygonTriangles[i]
                ];

            int b =
                roofIndices[
                    polygonTriangles[i + 1]
                ];

            int c =
                roofIndices[
                    polygonTriangles[i + 2]
                ];

            AddTriangleExpectedNormal(
                builder,
                builder.roofTriangles,
                a,
                b,
                c,
                Vector3.up
            );
        }

        // =====================================================
        // SIDE WALLS
        // =====================================================

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            int next =
                (i + 1)
                %
                footprint.Count;

            Vector3 a =
                footprint[i];

            Vector3 b =
                footprint[next];

            Vector3 baseA =
                new Vector3(
                    a.x,
                    baseHeight,
                    a.z
                );

            Vector3 baseB =
                new Vector3(
                    b.x,
                    baseHeight,
                    b.z
                );

            Vector3 topA =
                new Vector3(
                    a.x,
                    vertexHeights[i],
                    a.z
                );

            Vector3 topB =
                new Vector3(
                    b.x,
                    vertexHeights[next],
                    b.z
                );

            if (topA.y <=
                    baseHeight + 0.005f &&
                topB.y <=
                    baseHeight + 0.005f)
            {
                continue;
            }

            int baseAIndex =
                AddFacadeVertex(
                    builder,
                    baseA
                );

            int baseBIndex =
                AddFacadeVertex(
                    builder,
                    baseB
                );

            int topAIndex =
                AddFacadeVertex(
                    builder,
                    topA
                );

            int topBIndex =
                AddFacadeVertex(
                    builder,
                    topB
                );

            Vector3 outward =
                GetEdgeOutward(
                    a,
                    b
                );

            AddQuadExpectedNormal(
                builder,
                builder.facadeTriangles,
                baseAIndex,
                baseBIndex,
                topBIndex,
                topAIndex,
                outward
            );
        }
    }

    // =========================================================
    // MANSARD
    // =========================================================

    private void AddMansardRoof(
        MeshBuilder builder,
        List<Vector3> footprint,
        float baseHeight,
        float roofHeight)
    {
        if (footprint.Count != 4 ||
            !IsConvexPolygon(
                footprint))
        {
            AddSkillionRoof(
                builder,
                footprint,
                baseHeight,
                roofHeight
            );

            return;
        }

        Vector3 center =
            Vector3.zero;

        for (int i = 0;
             i < 4;
             i++)
        {
            center +=
                footprint[i];
        }

        center /=
            4f;

        center.y =
            baseHeight;

        Vector3[] outer =
            new Vector3[4];

        Vector3[] middle =
            new Vector3[4];

        Vector3[] top =
            new Vector3[4];

        for (int i = 0;
             i < 4;
             i++)
        {
            outer[i] =
                new Vector3(
                    footprint[i].x,
                    baseHeight,
                    footprint[i].z
                );

            // Dik alt eğim
            middle[i] =
                Vector3.Lerp(
                    outer[i],
                    center,
                    0.28f
                );

            middle[i].y =
                baseHeight +
                roofHeight *
                0.68f;

            // Daha yatık üst eğim
            top[i] =
                Vector3.Lerp(
                    outer[i],
                    center,
                    0.52f
                );

            top[i].y =
                baseHeight +
                roofHeight;
        }

        int[] outerIndex =
            new int[4];

        int[] middleIndex =
            new int[4];

        int[] topIndex =
            new int[4];

        for (int i = 0;
             i < 4;
             i++)
        {
            outerIndex[i] =
                AddRoofVertex(
                    builder,
                    outer[i]
                );

            middleIndex[i] =
                AddRoofVertex(
                    builder,
                    middle[i]
                );

            topIndex[i] =
                AddRoofVertex(
                    builder,
                    top[i]
                );
        }

        // Alt dik yüzey.
        for (int i = 0;
             i < 4;
             i++)
        {
            int next =
                (i + 1)
                %
                4;

            Vector3 outward =
                GetEdgeOutward(
                    outer[i],
                    outer[next]
                );

            AddQuadExpectedNormal(
                builder,
                builder.roofTriangles,
                outerIndex[i],
                outerIndex[next],
                middleIndex[next],
                middleIndex[i],
                (
                    outward +
                    Vector3.up *
                    0.25f
                ).normalized
            );
        }

        // Üst yatık yüzey.
        for (int i = 0;
             i < 4;
             i++)
        {
            int next =
                (i + 1)
                %
                4;

            Vector3 outward =
                GetEdgeOutward(
                    middle[i],
                    middle[next]
                );

            AddQuadExpectedNormal(
                builder,
                builder.roofTriangles,
                middleIndex[i],
                middleIndex[next],
                topIndex[next],
                topIndex[i],
                (
                    outward +
                    Vector3.up
                ).normalized
            );
        }

        // Küçük düz üst kapak.
        AddQuadExpectedNormal(
            builder,
            builder.roofTriangles,
            topIndex[0],
            topIndex[1],
            topIndex[2],
            topIndex[3],
            Vector3.up
        );
    }

    // =========================================================
    // ROOF HELPERS
    // =========================================================

    private void GetCanonicalRoofQuad(
        List<Vector3> footprint,
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2,
        out Vector3 p3)
    {
        Vector3 a = footprint[0];
        Vector3 b = footprint[1];
        Vector3 c = footprint[2];
        Vector3 d = footprint[3];

        float pairA =
            (
                Vector3.Distance(
                    a,
                    b
                ) +
                Vector3.Distance(
                    c,
                    d
                )
            ) *
            0.5f;

        float pairB =
            (
                Vector3.Distance(
                    b,
                    c
                ) +
                Vector3.Distance(
                    d,
                    a
                )
            ) *
            0.5f;

        // p0-p1 ve p2-p3 mümkün olduğunca uzun kenar.
        if (pairA >= pairB)
        {
            p0 = a;
            p1 = b;
            p2 = c;
            p3 = d;
        }
        else
        {
            p0 = b;
            p1 = c;
            p2 = d;
            p3 = a;
        }
    }

    private Vector3 GetEdgeOutward(
        Vector3 a,
        Vector3 b)
    {
        Vector3 direction =
            b -
            a;

        direction.y = 0f;

        if (direction.sqrMagnitude <
            0.0001f)
        {
            return
                Vector3.forward;
        }

        direction.Normalize();

        return new Vector3(
            direction.z,
            0f,
            -direction.x
        );
    }

    private int AddRoofVertex(
        MeshBuilder builder,
        Vector3 point)
    {
        int index =
            builder.vertices.Count;

        builder.vertices.Add(
            point
        );

        builder.uvs.Add(
            new Vector2(
                point.x *
                0.2f,
                point.z *
                0.2f
            )
        );

        return index;
    }

    private int AddFacadeVertex(
        MeshBuilder builder,
        Vector3 point)
    {
        int index =
            builder.vertices.Count;

        builder.vertices.Add(
            point
        );

        builder.uvs.Add(
            new Vector2(
                point.x *
                0.2f,
                point.y *
                0.2f
            )
        );

        return index;
    }

    private void AddQuadExpectedNormal(
        MeshBuilder builder,
        List<int> triangles,
        int a,
        int b,
        int c,
        int d,
        Vector3 expectedNormal)
    {
        AddTriangleExpectedNormal(
            builder,
            triangles,
            a,
            b,
            c,
            expectedNormal
        );

        AddTriangleExpectedNormal(
            builder,
            triangles,
            a,
            c,
            d,
            expectedNormal
        );
    }

    private void AddTriangleExpectedNormal(
        MeshBuilder builder,
        List<int> triangles,
        int a,
        int b,
        int c,
        Vector3 expectedNormal)
    {
        Vector3 pa =
            builder.vertices[a];

        Vector3 pb =
            builder.vertices[b];

        Vector3 pc =
            builder.vertices[c];

        Vector3 normal =
            Vector3.Cross(
                pb - pa,
                pc - pa
            );

        if (Vector3.Dot(
                normal,
                expectedNormal) < 0f)
        {
            int temp =
                b;

            b =
                c;

            c =
                temp;
        }

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    // =========================================================
    // ROOF MATERIAL
    // =========================================================

    private int GetRoofMaterialIndex(
        OSMWay way,
        long osmId,
        RoofType roofType)
    {
        if (roofMaterials == null ||
            roofMaterials.Length == 0)
        {
            return -1;
        }

        string osmMaterial =
            GetTagLower(
                way,
                "roof:material"
            );

        // =====================================================
        // OSM MATERIAL FIRST
        // =====================================================

        if (!string.IsNullOrEmpty(
                osmMaterial))
        {
            List<int> matches =
                new List<int>();

            for (int i = 0;
                 i < roofMaterials.Length;
                 i++)
            {
                Material material =
                    roofMaterials[i];

                if (material == null)
                    continue;

                if (RoofMaterialMatches(
                        material.name,
                        osmMaterial))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count > 0)
            {
                System.Random random =
                    CreateStableRandom(
                        osmId + 18901
                    );

                return
                    matches[
                        random.Next(
                            0,
                            matches.Count
                        )
                    ];
            }
        }

        // =====================================================
        // TYPE-APPROPRIATE MATERIAL
        // =====================================================

        List<int> preferred =
            new List<int>();

        for (int i = 0;
             i < roofMaterials.Length;
             i++)
        {
            Material material =
                roofMaterials[i];

            if (material == null)
                continue;

            string name =
                material.name
                    .ToLowerInvariant();

            bool suitable;

            if (roofType ==
                RoofType.Flat)
            {
                suitable =
                    name.Contains("concrete") ||
                    name.Contains("beton") ||
                    name.Contains("flat") ||
                    name.Contains("membrane") ||
                    name.Contains("gravel") ||
                    name.Contains("metal");
            }
            else
            {
                suitable =
                    name.Contains("tile") ||
                    name.Contains("kiremit") ||
                    name.Contains("slate") ||
                    name.Contains("shingle") ||
                    name.Contains("metal") ||
                    name.Contains("roof");
            }

            if (suitable)
            {
                preferred.Add(i);
            }
        }

        if (preferred.Count > 0)
        {
            System.Random random =
                CreateStableRandom(
                    osmId + 19001
                );

            return
                preferred[
                    random.Next(
                        0,
                        preferred.Count
                    )
                ];
        }

        return
            GetStableMaterialIndex(
                roofMaterials,
                osmId,
                19001
            );
    }

    private bool RoofMaterialMatches(
        string materialName,
        string osmValue)
    {
        if (string.IsNullOrEmpty(
                materialName))
        {
            return false;
        }

        string name =
            materialName
                .ToLowerInvariant();

        if (osmValue.Contains("tile"))
        {
            return
                name.Contains("tile") ||
                name.Contains("kiremit");
        }

        if (osmValue.Contains("metal"))
        {
            return
                name.Contains("metal") ||
                name.Contains("sheet");
        }

        if (osmValue.Contains("concrete"))
        {
            return
                name.Contains("concrete") ||
                name.Contains("beton");
        }

        if (osmValue.Contains("slate"))
        {
            return
                name.Contains("slate");
        }

        if (osmValue.Contains("shingle"))
        {
            return
                name.Contains("shingle");
        }

        return
            name.Contains(
                osmValue
            );
    }

    // =========================================================
    // MATERIAL PALETTE
    // =========================================================

    private int[] CreateMaterialPalette(
        Material[] materials,
        long seed,
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
                seed
            );

        // Shuffle
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

        int[] result =
            new int[count];

        for (int i = 0;
             i < count;
             i++)
        {
            result[i] =
                valid[i];
        }

        return result;
    }

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
    // STYLE SELECTION
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

        if (officeLike)
        {
            if (floorCount >= 4 &&
                roll < 70)
            {
                return
                    GlassPlazaStyle();
            }

            return
                OfficeStyle();
        }

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

        if (floorCount >= 5 &&
            roll < 22)
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
    // STYLES
    // =========================================================

    private BuildingStyle ClassicResidentialStyle()
    {
        return new BuildingStyle
        {
            styleName = "ClassicResidential",

            archetype =
                BuildingArchetype.ClassicResidential,

            targetBayWidth = 2.2f,

            windowWidth = 1.0f,
            windowHeight = 1.35f,
            windowBottom = 0.95f,
            windowSideMargin = 0.26f,

            shopHeight = 2.05f,
            shopSideMargin = 0.20f,
            shopChance = 0.55f,

            entranceWidth = 1.25f,
            entranceHeight = 2.2f,

            allowBalcony = true,
            balconyChance = 0.42f,
            balconyMaxWidth = 2.0f,

            fullGlassUpperFloors = false,
            fullGlassGroundFloor = false
        };
    }

    private BuildingStyle ModernResidentialStyle()
    {
        return new BuildingStyle
        {
            styleName = "ModernResidential",

            archetype =
                BuildingArchetype.ModernResidential,

            targetBayWidth = 2.75f,

            windowWidth = 1.45f,
            windowHeight = 1.55f,
            windowBottom = 0.78f,
            windowSideMargin = 0.24f,

            shopHeight = 2.15f,
            shopSideMargin = 0.18f,
            shopChance = 0.62f,

            entranceWidth = 1.4f,
            entranceHeight = 2.3f,

            allowBalcony = true,
            balconyChance = 0.26f,
            balconyMaxWidth = 2.2f,

            fullGlassUpperFloors = false,
            fullGlassGroundFloor = false
        };
    }

    private BuildingStyle MixedUseStyle()
    {
        return new BuildingStyle
        {
            styleName = "MixedUse",

            archetype =
                BuildingArchetype.MixedUse,

            targetBayWidth = 2.9f,

            windowWidth = 1.35f,
            windowHeight = 1.55f,
            windowBottom = 0.75f,
            windowSideMargin = 0.22f,

            shopHeight = 2.3f,
            shopSideMargin = 0.15f,
            shopChance = 0.88f,

            entranceWidth = 1.45f,
            entranceHeight = 2.35f,

            allowBalcony = false,
            balconyChance = 0f,
            balconyMaxWidth = 0f,

            fullGlassUpperFloors = false,
            fullGlassGroundFloor = false
        };
    }

    private BuildingStyle OfficeStyle()
    {
        return new BuildingStyle
        {
            styleName = "Office",

            archetype =
                BuildingArchetype.Office,

            targetBayWidth = 3.1f,

            windowWidth = 1.8f,
            windowHeight = 1.65f,
            windowBottom = 0.7f,
            windowSideMargin = 0.18f,

            shopHeight = 2.35f,
            shopSideMargin = 0.15f,
            shopChance = 0.5f,

            entranceWidth = 1.6f,
            entranceHeight = 2.45f,

            allowBalcony = false,
            balconyChance = 0f,
            balconyMaxWidth = 0f,

            fullGlassUpperFloors = false,
            fullGlassGroundFloor = false
        };
    }

    private static readonly Material[] plazaGlass = new Material[3];
    private static Material plazaMetal;

    private Material GetPlazaMaterial(long osmId, bool glass)
    {
        int variant = (int)((ulong)osmId % 3UL);
        Material cached = glass ? plazaGlass[variant] : plazaMetal;
        if (cached != null) return cached;
        Shader shader = glass ? Shader.Find("City/Interior Mapped Glass") : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return GetMaterial(facadeMaterials, 0);
        var material = new Material(shader);
        material.name = glass ? "Plaza_Glass_" + variant : "Plaza_Aluminum";
        Color tint = !glass ? new Color(0.12f, 0.15f, 0.18f) :
            variant == 0 ? new Color(0.16f, 0.32f, 0.42f) :
            variant == 1 ? new Color(0.22f, 0.39f, 0.38f) : new Color(0.28f, 0.34f, 0.43f);
        material.color = tint;
        if (glass && material.HasProperty("_RoomGrid"))
        {
            material.SetVector("_RoomGrid", new Vector4(2f, 1f, 0f, 0f));
            material.SetFloat("_InteriorStrength", 0.38f);
            material.SetFloat("_ReflectionStrength", 0.7f);
            material.SetFloat("_InteriorType", 2f);
            material.SetFloat("_RoomDepth", 4.5f);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", glass ? 0.72f : 0.8f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", glass ? 0.94f : 0.65f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", glass ? 0.94f : 0.65f);
        if (glass) plazaGlass[variant] = material; else plazaMetal = material;
        return material;
    }

    private BuildingStyle GlassPlazaStyle()
    {
        return new BuildingStyle
        {
            styleName = "GlassPlaza",

            archetype =
                BuildingArchetype.GlassPlaza,

            targetBayWidth = 3.3f,

            windowWidth = 2.4f,
            windowHeight = 2.5f,
            windowBottom = 0.18f,
            windowSideMargin = 0.1f,

            shopHeight = 2.45f,
            shopSideMargin = 0.08f,
            shopChance = 0.45f,

            entranceWidth = 1.9f,
            entranceHeight = 2.55f,

            allowBalcony = false,
            balconyChance = 0f,
            balconyMaxWidth = 0f,

            fullGlassUpperFloors = true,
            fullGlassGroundFloor = true
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

        // Levels
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
                    out int parsedLevels))
            {
                floorCount =
                    Mathf.Clamp(
                        parsedLevels,
                        1,
                        50
                    );
            }
        }

        // Height
        string heightTag =
            way.GetTag(
                "height"
            );

        if (TryParseMeters(
                heightTag,
                out float parsedHeight))
        {
            height =
                Mathf.Clamp(
                    parsedHeight,
                    2.5f,
                    300f
                );
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

    private bool TryParseMeters(
        string value,
        out float meters)
    {
        meters = 0f;

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        string cleaned =
            value
                .ToLowerInvariant()
                .Replace("meters", "")
                .Replace("meter", "")
                .Replace("metres", "")
                .Replace("metre", "")
                .Replace("m", "")
                .Trim();

        return
            float.TryParse(
                cleaned,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out meters
            );
    }

    // =========================================================
    // MATERIAL
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

    private Material GetMaterial(
        Material[] materials,
        int index)
    {
        if (materials == null ||
            index < 0 ||
            index >= materials.Length)
        {
            return null;
        }

        return
            materials[index];
    }

    // =========================================================
    // POLYGON CLEAN
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

        // Collinear / useless points.
        bool removed =
            true;

        int safety =
            100;

        while (removed &&
               result.Count > 3 &&
               safety-- > 0)
        {
            removed =
                false;

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

                if (a.sqrMagnitude < 0.001f ||
                    b.sqrMagnitude < 0.001f)
                {
                    result.RemoveAt(i);

                    removed =
                        true;

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

                    removed =
                        true;

                    break;
                }
            }
        }

        return result;
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
                    int testIndex =
                        remaining[j];

                    if (testIndex == previous ||
                        testIndex == current ||
                        testIndex == next)
                    {
                        continue;
                    }

                    if (PointInTriangle(
                            ToXZ(
                                points[testIndex]
                            ),
                            a,
                            b,
                            c))
                    {
                        containsPoint =
                            true;

                        break;
                    }
                }

                if (containsPoint)
                    continue;

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);

                remaining.RemoveAt(i);

                earFound =
                    true;

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

        return triangles;
    }

    // =========================================================
    // GEOMETRY
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
            ) >
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
        float area =
            0f;

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
    // EDGE
    // =========================================================

    private int FindLongestEdge(
        List<Vector3> footprint)
    {
        int result =
            0;

        float longest =
            -1f;

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
                longest =
                    length;

                result =
                    i;
            }
        }

        return result;
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

    private float RandomRange(
        System.Random random,
        float min,
        float max)
    {
        return
            min +
            (float)random.NextDouble() *
            (max - min);
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
            AddTriangleExpectedNormal(
                builder,
                builder.facadeTriangles,
                start + triangles[i],
                start + triangles[i + 1],
                start + triangles[i + 2],
                Vector3.down
            );
        }
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

        public readonly List<int> facadeTriangles =
            new List<int>();

        public readonly List<int> glassTriangles =
            new List<int>();

        public readonly List<int> roofTriangles =
            new List<int>();

        public readonly Dictionary<int, List<int>>
            shopTriangles =
                new Dictionary<int, List<int>>();

        public readonly Dictionary<int, List<int>>
            balconyTriangles =
                new Dictionary<int, List<int>>();
    }
}


// =============================================================
// BUILDING INFO
// =============================================================

public class BuildingInfo : MonoBehaviour
{
    public long osmId;

    public int floorCount;

    public float height;

    public string styleName;

    public string roofType;

    public float roofHeight;

    public int facadeMaterialIndex = -1;

    public int glassMaterialIndex = -1;

    public int roofMaterialIndex = -1;

    [HideInInspector]
    public Vector3[] footprint;
}
