using System;
using System.Collections.Generic;
using UnityEngine;

public class OSMAutoTerrainGenerator : MonoBehaviour
{
    // =========================================================
    // TERRAIN SIZE
    // =========================================================

    [Header("Terrain")]

    [Tooltip("OSM alanýnýn çevresine eklenecek boþluk.")]
    [SerializeField]
    private float terrainPadding = 80f;

    [Tooltip("Terrain'in maksimum dikey yüksekliði.")]
    [SerializeField]
    private float terrainVerticalSize = 120f;

    [Tooltip("257 hýzlý, 513 daha detaylý.")]
    [SerializeField]
    private int heightmapResolution = 257;

    [SerializeField]
    private Material terrainMaterial;

    [SerializeField]
    private TerrainLayer[] terrainLayers;

    // =========================================================
    // PROCEDURAL TERRAIN
    // =========================================================

    [Header("Procedural Elevation")]

    [SerializeField]
    private int terrainSeed = 14582;

    [Tooltip("Arazinin minimum yerden yüksekliði.")]
    [SerializeField]
    private float baseGroundHeight = 2f;

    [Tooltip("Tepelerin maksimum yaklaþýk yüksekliði.")]
    [SerializeField]
    private float hillAmplitude = 55f;

    [Header("Noise Scales")]

    [SerializeField]
    private float largeHillScale = 850f;

    [SerializeField]
    private float mediumHillScale = 320f;

    [SerializeField]
    private float smallHillScale = 110f;

    [Header("Noise Strength")]

    [Range(0f, 1f)]
    [SerializeField]
    private float largeHillStrength = 0.58f;

    [Range(0f, 1f)]
    [SerializeField]
    private float mediumHillStrength = 0.30f;

    [Range(0f, 1f)]
    [SerializeField]
    private float smallHillStrength = 0.12f;

    // =========================================================
    // ROAD
    // =========================================================

    [Header("Road Terrain Influence")]

    [Tooltip("Yol çevresindeki terrain yüksekliðini yumuþatýr.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float roadSmoothingStrength = 0.82f;

    [Tooltip("Yol geniþliðine ek olarak kaç metre terrain yumuþatýlsýn.")]
    [SerializeField]
    private float roadTerrainMargin = 5f;

    [Range(0, 5)]
    [SerializeField]
    private int roadSmoothIterations = 2;

    // =========================================================
    // WATER
    // =========================================================

    [Header("Water")]

    [Tooltip("Tahmini deniz/su yüksekliði.")]
    [SerializeField]
    private float waterLevel = 0.5f;

    [Tooltip("Nehir/kanal çevresindeki etki geniþliði.")]
    [SerializeField]
    private float waterwayWidth = 8f;

    [Range(0f, 1f)]
    [SerializeField]
    private float waterFlattenStrength = 1f;

    // =========================================================
    // BUILDINGS
    // =========================================================

    [Header("Building Terrain")]

    [SerializeField]
    private bool flattenBuildingGround = true;

    // =========================================================
    // OUTPUT
    // =========================================================

    private Terrain generatedTerrain;

    public Terrain GeneratedTerrain
    {
        get
        {
            return generatedTerrain;
        }
    }

    // =========================================================
    // GENERATE
    // =========================================================

    public Terrain GenerateTerrain(
        OSMMapData map)
    {
        if (map == null)
        {
            Debug.LogError(
                "Auto Terrain: OSMMapData null."
            );

            return null;
        }

        ClearGeneratedTerrain();

        int resolution =
            GetValidResolution(
                heightmapResolution
            );

        // =====================================================
        // OSM LOCAL BOUNDS
        // =====================================================

        GetOSMLocalBounds(
            map,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ
        );

        minX -= terrainPadding;
        maxX += terrainPadding;

        minZ -= terrainPadding;
        maxZ += terrainPadding;

        float sizeX =
            Mathf.Max(
                100f,
                maxX - minX
            );

        float sizeZ =
            Mathf.Max(
                100f,
                maxZ - minZ
            );

        TerrainSpace space =
            new TerrainSpace
            {
                originX = minX,
                originZ = minZ,

                sizeX = sizeX,
                sizeZ = sizeZ,

                verticalSize =
                    terrainVerticalSize,

                resolution =
                    resolution
            };

        // =====================================================
        // BASIC HEIGHTMAP
        // =====================================================

        float[,] heights =
            GenerateBaseHeightmap(
                space
            );

        // =====================================================
        // OSM MASKS
        // =====================================================

        float[,] roadMask =
            new float[
                resolution,
                resolution
            ];

        float[,] waterMask =
            new float[
                resolution,
                resolution
            ];

        BuildOSMMasks(
            map,
            space,
            roadMask,
            waterMask
        );

        // =====================================================
        // ROAD SMOOTH
        // =====================================================

        if (roadSmoothingStrength > 0f)
        {
            float[,] smoothHeights =
                CopyHeightmap(
                    heights
                );

            for (int i = 0;
                 i < roadSmoothIterations;
                 i++)
            {
                smoothHeights =
                    Blur3x3(
                        smoothHeights
                    );
            }

            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    float influence =
                        Mathf.Clamp01(
                            roadMask[z, x] *
                            roadSmoothingStrength
                        );

                    heights[z, x] =
                        Mathf.Lerp(
                            heights[z, x],
                            smoothHeights[z, x],
                            influence
                        );
                }
            }
        }

        // =====================================================
        // WATER
        // =====================================================

        float normalizedWaterHeight =
            Mathf.Clamp01(
                waterLevel /
                terrainVerticalSize
            );

        for (int z = 0;
             z < resolution;
             z++)
        {
            for (int x = 0;
                 x < resolution;
                 x++)
            {
                float influence =
                    Mathf.Clamp01(
                        waterMask[z, x] *
                        waterFlattenStrength
                    );

                if (influence <= 0f)
                    continue;

                heights[z, x] =
                    Mathf.Lerp(
                        heights[z, x],
                        normalizedWaterHeight,
                        influence
                    );
            }
        }

        // =====================================================
        // BUILDING FLATTEN
        // =====================================================

        if (flattenBuildingGround)
        {
            FlattenBuildingAreas(
                map,
                space,
                heights
            );
        }

        // =====================================================
        // CREATE TERRAIN DATA
        // =====================================================

        TerrainData terrainData =
            new TerrainData();

        terrainData.heightmapResolution =
            resolution;

        terrainData.size =
            new Vector3(
                sizeX,
                terrainVerticalSize,
                sizeZ
            );

        terrainData.SetHeights(
            0,
            0,
            heights
        );

        if (terrainLayers != null &&
            terrainLayers.Length > 0)
        {
            terrainData.terrainLayers =
                terrainLayers;
        }

        // =====================================================
        // CREATE GAMEOBJECT
        // =====================================================

        GameObject terrainObject =
            Terrain.CreateTerrainGameObject(
                terrainData
            );

        terrainObject.name =
            "GeneratedTerrain";

        terrainObject.transform.SetParent(
            transform,
            false
        );

        terrainObject.transform.localPosition =
            new Vector3(
                minX,
                0f,
                minZ
            );

        generatedTerrain =
            terrainObject.GetComponent<Terrain>();

        generatedTerrain.drawInstanced =
            true;

        generatedTerrain.heightmapPixelError =
            5f;

        if (terrainMaterial != null)
        {
            generatedTerrain.materialTemplate =
                terrainMaterial;
        }

        Debug.Log(
            $"Terrain generated: {sizeX:0}m x {sizeZ:0}m | Resolution: {resolution}"
        );

        return generatedTerrain;
    }

    // =========================================================
    // BASE HEIGHTMAP
    // =========================================================

    private float[,] GenerateBaseHeightmap(
        TerrainSpace space)
    {
        int resolution =
            space.resolution;

        float[,] heights =
            new float[
                resolution,
                resolution
            ];

        System.Random random =
            new System.Random(
                terrainSeed
            );

        float offsetX1 =
            random.Next(
                -100000,
                100000
            );

        float offsetZ1 =
            random.Next(
                -100000,
                100000
            );

        float offsetX2 =
            random.Next(
                -100000,
                100000
            );

        float offsetZ2 =
            random.Next(
                -100000,
                100000
            );

        float offsetX3 =
            random.Next(
                -100000,
                100000
            );

        float offsetZ3 =
            random.Next(
                -100000,
                100000
            );

        float totalStrength =
            largeHillStrength +
            mediumHillStrength +
            smallHillStrength;

        if (totalStrength <= 0f)
            totalStrength = 1f;

        for (int z = 0;
             z < resolution;
             z++)
        {
            float normalizedZ =
                z /
                (float)(
                    resolution - 1
                );

            float localZ =
                space.originZ +
                normalizedZ *
                space.sizeZ;

            for (int x = 0;
                 x < resolution;
                 x++)
            {
                float normalizedX =
                    x /
                    (float)(
                        resolution - 1
                    );

                float localX =
                    space.originX +
                    normalizedX *
                    space.sizeX;

                float large =
                    Mathf.PerlinNoise(
                        (
                            localX +
                            offsetX1
                        ) /
                        Mathf.Max(
                            1f,
                            largeHillScale
                        ),
                        (
                            localZ +
                            offsetZ1
                        ) /
                        Mathf.Max(
                            1f,
                            largeHillScale
                        )
                    );

                float medium =
                    Mathf.PerlinNoise(
                        (
                            localX +
                            offsetX2
                        ) /
                        Mathf.Max(
                            1f,
                            mediumHillScale
                        ),
                        (
                            localZ +
                            offsetZ2
                        ) /
                        Mathf.Max(
                            1f,
                            mediumHillScale
                        )
                    );

                float small =
                    Mathf.PerlinNoise(
                        (
                            localX +
                            offsetX3
                        ) /
                        Mathf.Max(
                            1f,
                            smallHillScale
                        ),
                        (
                            localZ +
                            offsetZ3
                        ) /
                        Mathf.Max(
                            1f,
                            smallHillScale
                        )
                    );

                float noise =
                    (
                        large *
                        largeHillStrength +
                        medium *
                        mediumHillStrength +
                        small *
                        smallHillStrength
                    ) /
                    totalStrength;

                /*
                 * Düz Perlin çok yumuþak kalabiliyor.
                 * SmoothStep ile daha okunur tepeler.
                 */

                noise =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        noise
                    );

                float meters =
                    baseGroundHeight +
                    noise *
                    hillAmplitude;

                heights[z, x] =
                    Mathf.Clamp01(
                        meters /
                        space.verticalSize
                    );
            }
        }

        return heights;
    }

    // =========================================================
    // OSM MASKS
    // =========================================================

    private void BuildOSMMasks(
        OSMMapData map,
        TerrainSpace space,
        float[,] roadMask,
        float[,] waterMask)
    {
        foreach (OSMWay way
                 in map.ways)
        {
            if (way == null ||
                way.nodeIds == null ||
                way.nodeIds.Count < 2)
            {
                continue;
            }

            List<Vector3> points =
                GetWayPoints(
                    map,
                    way
                );

            if (points.Count < 2)
                continue;

            // =================================================
            // ROAD
            // =================================================

            if (OSMRoadMetrics.IsRoad(
                    way))
            {
                float roadWidth =
                    OSMRoadMetrics.GetRoadWidth(
                        way
                    );

                float maskRadius =
                    roadWidth *
                    0.5f +
                    roadTerrainMargin;

                RasterizePolyline(
                    roadMask,
                    points,
                    maskRadius,
                    space
                );
            }

            // =================================================
            // WATER
            // =================================================

            if (IsWaterFeature(
                    way))
            {
                if (IsClosedPolygon(
                        way) &&
                    IsWaterArea(
                        way))
                {
                    RasterizePolygon(
                        waterMask,
                        points,
                        space
                    );
                }
                else
                {
                    float width =
                        GetWaterwayWidth(
                            way
                        );

                    RasterizePolyline(
                        waterMask,
                        points,
                        width,
                        space
                    );
                }
            }
        }

        // Su kenarlarýný biraz yumuþat.
        waterMask =
            BlurMaskInto(
                waterMask,
                2
            );

        /*
         * BlurMaskInto yeni array döndürdüðü için
         * orijinal waterMask'e kopyala.
         */

        int resolution =
            space.resolution;

        float[,] blurredWater =
            BlurMaskInto(
                waterMask,
                1
            );

        for (int z = 0;
             z < resolution;
             z++)
        {
            for (int x = 0;
                 x < resolution;
                 x++)
            {
                waterMask[z, x] =
                    blurredWater[z, x];
            }
        }
    }

    // =========================================================
    // BUILDING FLATTEN
    // =========================================================

    private void FlattenBuildingAreas(
        OSMMapData map,
        TerrainSpace space,
        float[,] heights)
    {
        foreach (OSMWay way
                 in map.ways)
        {
            if (way == null ||
                !way.HasTag(
                    "building"
                ))
            {
                continue;
            }

            if (!IsClosedPolygon(
                    way))
            {
                continue;
            }

            List<Vector3> polygon =
                GetWayPoints(
                    map,
                    way
                );

            if (polygon.Count < 3)
                continue;

            // Tekrarlanan kapanýþ noktasýný kaldýr.
            if (polygon.Count > 3 &&
                Vector3.Distance(
                    polygon[0],
                    polygon[
                        polygon.Count - 1
                    ]) < 0.05f)
            {
                polygon.RemoveAt(
                    polygon.Count - 1
                );
            }

            FlattenPolygon(
                polygon,
                space,
                heights
            );
        }
    }

    private void FlattenPolygon(
        List<Vector3> polygon,
        TerrainSpace space,
        float[,] heights)
    {
        GetPolygonPixelBounds(
            polygon,
            space,
            out int minX,
            out int maxX,
            out int minZ,
            out int maxZ
        );

        float targetHeight =
            float.MaxValue;

        bool found =
            false;

        // Önce polygon içindeki minimum yüksekliði bul.
        for (int z = minZ;
             z <= maxZ;
             z++)
        {
            for (int x = minX;
                 x <= maxX;
                 x++)
            {
                Vector2 localPoint =
                    PixelToLocalXZ(
                        x,
                        z,
                        space
                    );

                if (!PointInPolygon(
                        localPoint,
                        polygon))
                {
                    continue;
                }

                targetHeight =
                    Mathf.Min(
                        targetHeight,
                        heights[z, x]
                    );

                found =
                    true;
            }
        }

        if (!found)
            return;

        // Polygon içini düz yap.
        for (int z = minZ;
             z <= maxZ;
             z++)
        {
            for (int x = minX;
                 x <= maxX;
                 x++)
            {
                Vector2 localPoint =
                    PixelToLocalXZ(
                        x,
                        z,
                        space
                    );

                if (!PointInPolygon(
                        localPoint,
                        polygon))
                {
                    continue;
                }

                heights[z, x] =
                    targetHeight;
            }
        }
    }

    // =========================================================
    // WATER
    // =========================================================

    private bool IsWaterFeature(
        OSMWay way)
    {
        string natural =
            GetTagLower(
                way,
                "natural"
            );

        string waterway =
            GetTagLower(
                way,
                "waterway"
            );

        string water =
            GetTagLower(
                way,
                "water"
            );

        string landuse =
            GetTagLower(
                way,
                "landuse"
            );

        return
            natural == "water" ||
            natural == "coastline" ||
            !string.IsNullOrEmpty(
                waterway
            ) ||
            !string.IsNullOrEmpty(
                water
            ) ||
            landuse == "reservoir" ||
            landuse == "basin";
    }

    private bool IsWaterArea(
        OSMWay way)
    {
        string natural =
            GetTagLower(
                way,
                "natural"
            );

        string landuse =
            GetTagLower(
                way,
                "landuse"
            );

        string water =
            GetTagLower(
                way,
                "water"
            );

        return
            natural == "water" ||
            landuse == "reservoir" ||
            landuse == "basin" ||
            !string.IsNullOrEmpty(
                water
            );
    }

    private float GetWaterwayWidth(
        OSMWay way)
    {
        string waterway =
            GetTagLower(
                way,
                "waterway"
            );

        switch (waterway)
        {
            case "river":
                return
                    waterwayWidth *
                    2.5f;

            case "canal":
                return
                    waterwayWidth *
                    1.5f;

            case "stream":
                return
                    waterwayWidth *
                    0.65f;

            case "ditch":
                return
                    waterwayWidth *
                    0.4f;

            default:
                return
                    waterwayWidth;
        }
    }

    // =========================================================
    // RASTER POLYLINE
    // =========================================================

    private void RasterizePolyline(
        float[,] mask,
        List<Vector3> points,
        float radiusMeters,
        TerrainSpace space)
    {
        for (int i = 0;
             i < points.Count - 1;
             i++)
        {
            Vector2 a =
                LocalToPixelFloat(
                    points[i],
                    space
                );

            Vector2 b =
                LocalToPixelFloat(
                    points[i + 1],
                    space
                );

            float dx =
                b.x -
                a.x;

            float dz =
                b.y -
                a.y;

            int steps =
                Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Mathf.Max(
                            Mathf.Abs(dx),
                            Mathf.Abs(dz)
                        )
                    )
                );

            for (int step = 0;
                 step <= steps;
                 step++)
            {
                float t =
                    step /
                    (float)steps;

                float pixelX =
                    Mathf.Lerp(
                        a.x,
                        b.x,
                        t
                    );

                float pixelZ =
                    Mathf.Lerp(
                        a.y,
                        b.y,
                        t
                    );

                StampMask(
                    mask,
                    pixelX,
                    pixelZ,
                    radiusMeters,
                    space
                );
            }
        }
    }

    // =========================================================
    // MASK STAMP
    // =========================================================

    private void StampMask(
        float[,] mask,
        float centerX,
        float centerZ,
        float radiusMeters,
        TerrainSpace space)
    {
        float pixelSizeX =
            space.sizeX /
            (
                space.resolution - 1
            );

        float pixelSizeZ =
            space.sizeZ /
            (
                space.resolution - 1
            );

        int radiusX =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    radiusMeters /
                    pixelSizeX
                )
            );

        int radiusZ =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    radiusMeters /
                    pixelSizeZ
                )
            );

        int minX =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    centerX
                ) -
                radiusX
            );

        int maxX =
            Mathf.Min(
                space.resolution - 1,
                Mathf.CeilToInt(
                    centerX
                ) +
                radiusX
            );

        int minZ =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    centerZ
                ) -
                radiusZ
            );

        int maxZ =
            Mathf.Min(
                space.resolution - 1,
                Mathf.CeilToInt(
                    centerZ
                ) +
                radiusZ
            );

        for (int z = minZ;
             z <= maxZ;
             z++)
        {
            for (int x = minX;
                 x <= maxX;
                 x++)
            {
                float normalizedX =
                    (
                        x -
                        centerX
                    ) /
                    radiusX;

                float normalizedZ =
                    (
                        z -
                        centerZ
                    ) /
                    radiusZ;

                float distance =
                    Mathf.Sqrt(
                        normalizedX *
                        normalizedX +
                        normalizedZ *
                        normalizedZ
                    );

                if (distance >
                    1f)
                {
                    continue;
                }

                float influence =
                    1f -
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        distance
                    );

                mask[z, x] =
                    Mathf.Max(
                        mask[z, x],
                        influence
                    );
            }
        }
    }

    // =========================================================
    // RASTER POLYGON
    // =========================================================

    private void RasterizePolygon(
        float[,] mask,
        List<Vector3> polygon,
        TerrainSpace space)
    {
        GetPolygonPixelBounds(
            polygon,
            space,
            out int minX,
            out int maxX,
            out int minZ,
            out int maxZ
        );

        for (int z = minZ;
             z <= maxZ;
             z++)
        {
            for (int x = minX;
                 x <= maxX;
                 x++)
            {
                Vector2 point =
                    PixelToLocalXZ(
                        x,
                        z,
                        space
                    );

                if (PointInPolygon(
                        point,
                        polygon))
                {
                    mask[z, x] =
                        1f;
                }
            }
        }
    }

    // =========================================================
    // POINT IN POLYGON
    // =========================================================

    private bool PointInPolygon(
        Vector2 point,
        List<Vector3> polygon)
    {
        bool inside =
            false;

        int j =
            polygon.Count - 1;

        for (int i = 0;
             i < polygon.Count;
             i++)
        {
            float xi =
                polygon[i].x;

            float zi =
                polygon[i].z;

            float xj =
                polygon[j].x;

            float zj =
                polygon[j].z;

            bool intersect =
                (
                    (zi > point.y)
                    !=
                    (zj > point.y)
                ) &&
                (
                    point.x <
                    (
                        xj -
                        xi
                    ) *
                    (
                        point.y -
                        zi
                    ) /
                    (
                        zj -
                        zi +
                        0.000001f
                    ) +
                    xi
                );

            if (intersect)
            {
                inside =
                    !inside;
            }

            j =
                i;
        }

        return inside;
    }

    // =========================================================
    // PIXEL BOUNDS
    // =========================================================

    private void GetPolygonPixelBounds(
        List<Vector3> polygon,
        TerrainSpace space,
        out int minX,
        out int maxX,
        out int minZ,
        out int maxZ)
    {
        minX =
            space.resolution - 1;

        maxX =
            0;

        minZ =
            space.resolution - 1;

        maxZ =
            0;

        foreach (Vector3 point
                 in polygon)
        {
            Vector2 pixel =
                LocalToPixelFloat(
                    point,
                    space
                );

            minX =
                Mathf.Min(
                    minX,
                    Mathf.FloorToInt(
                        pixel.x
                    )
                );

            maxX =
                Mathf.Max(
                    maxX,
                    Mathf.CeilToInt(
                        pixel.x
                    )
                );

            minZ =
                Mathf.Min(
                    minZ,
                    Mathf.FloorToInt(
                        pixel.y
                    )
                );

            maxZ =
                Mathf.Max(
                    maxZ,
                    Mathf.CeilToInt(
                        pixel.y
                    )
                );
        }

        minX =
            Mathf.Clamp(
                minX,
                0,
                space.resolution - 1
            );

        maxX =
            Mathf.Clamp(
                maxX,
                0,
                space.resolution - 1
            );

        minZ =
            Mathf.Clamp(
                minZ,
                0,
                space.resolution - 1
            );

        maxZ =
            Mathf.Clamp(
                maxZ,
                0,
                space.resolution - 1
            );
    }

    // =========================================================
    // WAY POINTS
    // =========================================================

    private List<Vector3> GetWayPoints(
        OSMMapData map,
        OSMWay way)
    {
        List<Vector3> points =
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

            point.y =
                0f;

            points.Add(
                point
            );
        }

        return points;
    }

    // =========================================================
    // OSM BOUNDS
    // =========================================================

    private void GetOSMLocalBounds(
        OSMMapData map,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        Vector3[] corners =
        {
            OSMGeoUtils.GeoToUnity(
                map.minLatitude,
                map.minLongitude,
                map.centerLatitude,
                map.centerLongitude
            ),

            OSMGeoUtils.GeoToUnity(
                map.minLatitude,
                map.maxLongitude,
                map.centerLatitude,
                map.centerLongitude
            ),

            OSMGeoUtils.GeoToUnity(
                map.maxLatitude,
                map.minLongitude,
                map.centerLatitude,
                map.centerLongitude
            ),

            OSMGeoUtils.GeoToUnity(
                map.maxLatitude,
                map.maxLongitude,
                map.centerLatitude,
                map.centerLongitude
            )
        };

        minX =
            float.MaxValue;

        maxX =
            float.MinValue;

        minZ =
            float.MaxValue;

        maxZ =
            float.MinValue;

        foreach (Vector3 point
                 in corners)
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
    }

    // =========================================================
    // COORDINATE CONVERSION
    // =========================================================

    private Vector2 LocalToPixelFloat(
        Vector3 localPoint,
        TerrainSpace space)
    {
        float normalizedX =
            (
                localPoint.x -
                space.originX
            ) /
            space.sizeX;

        float normalizedZ =
            (
                localPoint.z -
                space.originZ
            ) /
            space.sizeZ;

        return new Vector2(
            normalizedX *
            (
                space.resolution - 1
            ),
            normalizedZ *
            (
                space.resolution - 1
            )
        );
    }

    private Vector2 PixelToLocalXZ(
        int pixelX,
        int pixelZ,
        TerrainSpace space)
    {
        float normalizedX =
            pixelX /
            (float)(
                space.resolution - 1
            );

        float normalizedZ =
            pixelZ /
            (float)(
                space.resolution - 1
            );

        return new Vector2(
            space.originX +
            normalizedX *
            space.sizeX,

            space.originZ +
            normalizedZ *
            space.sizeZ
        );
    }

    // =========================================================
    // BLUR
    // =========================================================

    private float[,] Blur3x3(
        float[,] source)
    {
        int height =
            source.GetLength(0);

        int width =
            source.GetLength(1);

        float[,] result =
            new float[
                height,
                width
            ];

        for (int z = 0;
             z < height;
             z++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                float total =
                    0f;

                int count =
                    0;

                for (int offsetZ = -1;
                     offsetZ <= 1;
                     offsetZ++)
                {
                    for (int offsetX = -1;
                         offsetX <= 1;
                         offsetX++)
                    {
                        int sampleX =
                            x +
                            offsetX;

                        int sampleZ =
                            z +
                            offsetZ;

                        if (sampleX < 0 ||
                            sampleX >= width ||
                            sampleZ < 0 ||
                            sampleZ >= height)
                        {
                            continue;
                        }

                        total +=
                            source[
                                sampleZ,
                                sampleX
                            ];

                        count++;
                    }
                }

                result[z, x] =
                    total /
                    Mathf.Max(
                        1,
                        count
                    );
            }
        }

        return result;
    }

    private float[,] BlurMaskInto(
        float[,] source,
        int iterations)
    {
        float[,] current =
            CopyHeightmap(
                source
            );

        for (int i = 0;
             i < iterations;
             i++)
        {
            current =
                Blur3x3(
                    current
                );
        }

        return current;
    }

    private float[,] CopyHeightmap(
        float[,] source)
    {
        int height =
            source.GetLength(0);

        int width =
            source.GetLength(1);

        float[,] copy =
            new float[
                height,
                width
            ];

        for (int z = 0;
             z < height;
             z++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                copy[z, x] =
                    source[z, x];
            }
        }

        return copy;
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private bool IsClosedPolygon(
        OSMWay way)
    {
        return
            way.nodeIds != null &&
            way.nodeIds.Count >= 4 &&
            way.nodeIds[0] ==
            way.nodeIds[
                way.nodeIds.Count - 1
            ];
    }

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
    // RESOLUTION
    // =========================================================

    private int GetValidResolution(
        int requested)
    {
        int[] valid =
        {
            33,
            65,
            129,
            257,
            513,
            1025,
            2049,
            4097
        };

        int closest =
            valid[0];

        int smallestDifference =
            Mathf.Abs(
                requested -
                closest
            );

        foreach (int value
                 in valid)
        {
            int difference =
                Mathf.Abs(
                    requested -
                    value
                );

            if (difference <
                smallestDifference)
            {
                closest =
                    value;

                smallestDifference =
                    difference;
            }
        }

        return closest;
    }

    // =========================================================
    // CLEAR
    // =========================================================

    [ContextMenu("Clear Generated Terrain")]
    public void ClearGeneratedTerrain()
    {
        Transform oldTerrain =
            transform.Find(
                "GeneratedTerrain"
            );

        if (oldTerrain == null)
        {
            generatedTerrain =
                null;

            return;
        }

        Terrain terrain =
            oldTerrain.GetComponent<Terrain>();

        TerrainData terrainData =
            terrain != null
                ? terrain.terrainData
                : null;

        if (Application.isPlaying)
        {
            Destroy(
                oldTerrain.gameObject
            );

            if (terrainData != null)
            {
                Destroy(
                    terrainData
                );
            }
        }
        else
        {
            DestroyImmediate(
                oldTerrain.gameObject
            );

            if (terrainData != null)
            {
                DestroyImmediate(
                    terrainData
                );
            }
        }

        generatedTerrain =
            null;
    }

    // =========================================================
    // STRUCT
    // =========================================================

    private struct TerrainSpace
    {
        public float originX;
        public float originZ;

        public float sizeX;
        public float sizeZ;

        public float verticalSize;

        public int resolution;
    }
}