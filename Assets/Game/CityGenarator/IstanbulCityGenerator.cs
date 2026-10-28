using UnityEngine;

public class IstanbulCityGenerator : MonoBehaviour
{
    // =========================================================
    // OSM
    // =========================================================

    [Header("OSM")]

    [SerializeField]
    private TextAsset osmFile;

    // =========================================================
    // TERRAIN
    // =========================================================

    [Header("Auto Terrain")]

    [SerializeField]
    private bool generateTerrain =
        true;

    [SerializeField]
    private OSMAutoTerrainGenerator
        autoTerrainGenerator;

    [SerializeField]
    private OSMTerrainElevation
        terrainElevation;

    [SerializeField]
    private OSMRoadTerrainConformer
        roadTerrainConformer;

    // =========================================================
    // ROAD
    // =========================================================

    [Header("Road")]

    [SerializeField]
    private bool generateRoads =
        true;

    [SerializeField]
    private Material roadMaterial;

    [SerializeField]
    private Material[] medianMaterials;

    // =========================================================
    // INTERSECTION
    // =========================================================

    [Header("Intersection")]

    [SerializeField]
    private bool generateIntersections =
        true;

    [SerializeField]
    private Material intersectionMaterial;

    // =========================================================
    // SIDEWALK
    // =========================================================

    [Header("Sidewalk")]

    [SerializeField]
    private bool generateSidewalks =
        true;

    [SerializeField]
    private Material[] sidewalkMaterials;

    [SerializeField]
    private Material[] curbMaterials;

    [SerializeField]
    private Material[] sidewalkBufferMaterials;

    // =========================================================
    // BUILDING
    // =========================================================

    [Header("Buildings")]

    [SerializeField]
    private bool generateBuildings =
        true;

    [Header("Building - Facade Materials")]

    [SerializeField]
    private Material[] buildingMaterials;

    [Header("Building - Glass Materials")]

    [SerializeField]
    private Material[] glassMaterials;

    [Header("Building - Shop Materials")]

    [SerializeField]
    private Material[] shopMaterials;

    [Header("Building - Balcony Materials")]

    [SerializeField]
    private Material[] balconyMaterials;

    [Header("Building - Roof Materials")]

    [SerializeField]
    private Material[] roofMaterials;

    // =========================================================
    // ROOTS
    // =========================================================

    private Transform roadsParent;

    private Transform intersectionsParent;

    private Transform sidewalksParent;

    private Transform buildingsParent;

    // =========================================================
    // GENERATE
    // =========================================================

    [ContextMenu("Generate Istanbul")]
    public void Generate()
    {
        if (osmFile == null)
        {
            Debug.LogError(
                "OSM file atanmadý."
            );

            return;
        }

        ClearCityObjects();

        // =====================================================
        // PARSE
        // =====================================================

        OSMMapData map =
            OSMParser.Parse(
                osmFile.text
            );

        if (map == null)
        {
            Debug.LogError(
                "OSM parse baþarýsýz."
            );

            return;
        }

        // =====================================================
        // COMPONENTS
        // =====================================================

        EnsureTerrainComponents();

        // =====================================================
        // NETWORK FIRST
        // =====================================================

        OSMRoadNetworkData roadNetwork =
            OSMRoadNetworkData.Build(
                map
            );

        OSMRoadEnvironmentData roadEnvironment =
            OSMRoadEnvironmentData.Build(
                map
            );

        // =====================================================
        // GENERATE TERRAIN
        // =====================================================

        Terrain generatedTerrain =
            null;

        if (generateTerrain)
        {
            generatedTerrain =
                autoTerrainGenerator
                    .GenerateTerrain(
                        map
                    );

            if (generatedTerrain != null)
            {
                terrainElevation.SetTerrain(
                    generatedTerrain
                );
            }
        }
        else
        {
            generatedTerrain =
                terrainElevation
                    .AssignedTerrain;
        }

        terrainElevation.Configure(
            transform
        );

        // =====================================================
        // CONFORM TERRAIN TO ROAD NETWORK
        //
        // En kritik sýra:
        // Terrain -> conform -> road mesh
        // =====================================================

        if (generatedTerrain != null &&
            roadTerrainConformer != null)
        {
            roadTerrainConformer.Conform(
                map,
                roadNetwork,
                transform,
                generatedTerrain
            );

            /*
             * Terrain deðiþti.
             * elevation ayný Terrain referansýný kullanýyor.
             */
        }

        // =====================================================
        // PARENTS
        // =====================================================

        CreateParents();

        // =====================================================
        // GENERATORS
        // =====================================================

        OSMRoadGenerator roadGenerator =
            new OSMRoadGenerator(
                roadsParent,
                roadMaterial,
                medianMaterials,
                terrainElevation,
                roadNetwork
            );

        Material finalIntersectionMaterial =
            intersectionMaterial != null
                ? intersectionMaterial
                : roadMaterial;

        OSMIntersectionGenerator
            intersectionGenerator =
                new OSMIntersectionGenerator(
                    intersectionsParent,
                    finalIntersectionMaterial,
                    terrainElevation,
                    roadNetwork
                );

        OSMSidewalkGenerator
            sidewalkGenerator =
                new OSMSidewalkGenerator(
                    sidewalksParent,
                    sidewalkMaterials,
                    curbMaterials,
                    sidewalkBufferMaterials,
                    terrainElevation,
                    roadNetwork,
                    roadEnvironment
                );

        OSMBuildingGenerator
            buildingGenerator =
                new OSMBuildingGenerator(
                    buildingsParent,
                    buildingMaterials,
                    glassMaterials,
                    shopMaterials,
                    balconyMaterials,
                    roofMaterials
                );

        // =====================================================
        // ROADS
        // =====================================================

        if (generateRoads)
        {
            foreach (OSMWay way
                     in map.ways)
            {
                if (way == null ||
                    !OSMRoadMetrics.IsRoad(
                        way
                    ))
                {
                    continue;
                }

                roadGenerator.Generate(
                    map,
                    way
                );
            }
        }

        // =====================================================
        // INTERSECTIONS
        // =====================================================

        if (generateRoads &&
            generateIntersections)
        {
            intersectionGenerator.Generate(
                map
            );
        }

        // =====================================================
        // SIDEWALKS
        // =====================================================

        if (generateSidewalks)
        {
            foreach (OSMWay way
                     in map.ways)
            {
                if (way == null ||
                    !OSMRoadMetrics.IsRoad(
                        way
                    ))
                {
                    continue;
                }

                sidewalkGenerator.Generate(
                    map,
                    way
                );
            }
        }

        // =====================================================
        // BUILDINGS
        // =====================================================

        if (generateBuildings)
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

                buildingGenerator.Generate(
                    map,
                    way
                );
            }

            terrainElevation.PlaceBuildings(
                buildingsParent
            );
        }

        Debug.Log(
            "Istanbul generation completed."
        );
    }

    // =========================================================
    // COMPONENTS
    // =========================================================

    private void EnsureTerrainComponents()
    {
        if (autoTerrainGenerator == null)
        {
            autoTerrainGenerator =
                GetComponent<
                    OSMAutoTerrainGenerator
                >();
        }

        if (autoTerrainGenerator == null)
        {
            autoTerrainGenerator =
                gameObject.AddComponent<
                    OSMAutoTerrainGenerator
                >();
        }

        // =====================================================

        if (terrainElevation == null)
        {
            terrainElevation =
                GetComponent<
                    OSMTerrainElevation
                >();
        }

        if (terrainElevation == null)
        {
            terrainElevation =
                gameObject.AddComponent<
                    OSMTerrainElevation
                >();
        }

        // =====================================================

        if (roadTerrainConformer == null)
        {
            roadTerrainConformer =
                GetComponent<
                    OSMRoadTerrainConformer
                >();
        }

        if (roadTerrainConformer == null)
        {
            roadTerrainConformer =
                gameObject.AddComponent<
                    OSMRoadTerrainConformer
                >();
        }
    }

    // =========================================================
    // PARENTS
    // =========================================================

    private void CreateParents()
    {
        GameObject roads =
            new GameObject(
                "Roads"
            );

        roads.transform.SetParent(
            transform,
            false
        );

        roadsParent =
            roads.transform;

        // =====================================================

        GameObject intersections =
            new GameObject(
                "Intersections"
            );

        intersections.transform.SetParent(
            transform,
            false
        );

        intersectionsParent =
            intersections.transform;

        // =====================================================

        GameObject sidewalks =
            new GameObject(
                "Sidewalks"
            );

        sidewalks.transform.SetParent(
            transform,
            false
        );

        sidewalksParent =
            sidewalks.transform;

        // =====================================================

        GameObject buildings =
            new GameObject(
                "Buildings"
            );

        buildings.transform.SetParent(
            transform,
            false
        );

        buildingsParent =
            buildings.transform;
    }

    // =========================================================
    // CLEAR
    // =========================================================

    [ContextMenu("Clear Istanbul")]
    public void Clear()
    {
        ClearCityObjects();

        if (autoTerrainGenerator == null)
        {
            autoTerrainGenerator =
                GetComponent<
                    OSMAutoTerrainGenerator
                >();
        }

        if (autoTerrainGenerator != null)
        {
            autoTerrainGenerator
                .ClearGeneratedTerrain();
        }
    }

    private void ClearCityObjects()
    {
        DestroyChild("Roads");

        DestroyChild("Intersections");

        DestroyChild("Sidewalks");

        DestroyChild("Buildings");

        roadsParent = null;

        intersectionsParent = null;

        sidewalksParent = null;

        buildingsParent = null;
    }

    private void DestroyChild(
        string name)
    {
        Transform child =
            transform.Find(
                name
            );

        if (child == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(
                child.gameObject
            );
        }
        else
        {
            DestroyImmediate(
                child.gameObject
            );
        }
    }
}