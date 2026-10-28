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
    // RUNTIME
    // =========================================================

    [Header("Runtime")]

    [Tooltip(
        "Play Mode başladığında şehir tekrar üretilir. " +
        "Cinematic kamera için açık bırakılması önerilir."
    )]
    [SerializeField]
    private bool generateOnPlay = true;

    // =========================================================
    // TERRAIN
    // =========================================================

    [Header("Auto Terrain")]

    [SerializeField]
    private bool generateTerrain = true;

    [SerializeField]
    private OSMAutoTerrainGenerator autoTerrainGenerator;

    [SerializeField]
    private OSMTerrainElevation terrainElevation;

    [SerializeField]
    private OSMRoadTerrainConformer roadTerrainConformer;

    // =========================================================
    // ROAD
    // =========================================================

    [Header("Road")]

    [SerializeField]
    private bool generateRoads = true;

    [SerializeField]
    private Material roadMaterial;

    [SerializeField]
    private Material[] medianMaterials;

    // =========================================================
    // INTERSECTION
    // =========================================================

    [Header("Intersection")]

    [SerializeField]
    private bool generateIntersections = true;

    [SerializeField]
    private Material intersectionMaterial;

    // =========================================================
    // SIDEWALK
    // =========================================================

    [Header("Sidewalk")]

    [SerializeField]
    private bool generateSidewalks = true;

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
    private bool generateBuildings = true;

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
    // CINEMATIC
    // =========================================================

    [Header("Cinematic")]

    [Tooltip(
        "Play Mode sırasında Main Camera şehirde otomatik dolaşır."
    )]
    [SerializeField]
    private bool cinematicActive = false;

    [SerializeField]
    private CinematicCityCamera cinematicCityCamera;

    // =========================================================
    // GENERATED ROOTS
    // =========================================================

    private Transform roadsParent;

    private Transform intersectionsParent;

    private Transform sidewalksParent;

    private Transform buildingsParent;

    // =========================================================
    // UNITY START
    // =========================================================

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        if (generateOnPlay)
        {
            Generate();
        }
    }

    // =========================================================
    // GENERATE
    // =========================================================

    [ContextMenu("Generate Istanbul")]
    public void Generate()
    {
        if (osmFile == null)
        {
            Debug.LogError(
                "IstanbulCityGenerator: OSM file atanmadı."
            );

            return;
        }

        // =====================================================
        // STOP OLD CAMERA
        // =====================================================

        if (cinematicCityCamera != null)
        {
            cinematicCityCamera
                .SetCinematicActive(
                    false
                );
        }

        // =====================================================
        // CLEAR OLD GENERATED OBJECTS
        // =====================================================

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
                "IstanbulCityGenerator: OSM parse başarısız."
            );

            return;
        }

        // =====================================================
        // COMPONENTS
        // =====================================================

        EnsureRequiredComponents();

        // =====================================================
        // ROAD NETWORK
        // =====================================================

        OSMRoadNetworkData roadNetwork =
            OSMRoadNetworkData.Build(
                map
            );

        // =====================================================
        // ENVIRONMENT
        // =====================================================

        OSMRoadEnvironmentData roadEnvironment =
            OSMRoadEnvironmentData.Build(
                map
            );

        // =====================================================
        // TERRAIN
        // =====================================================

        Terrain activeTerrain =
            null;

        if (generateTerrain)
        {
            activeTerrain =
                autoTerrainGenerator
                    .GenerateTerrain(
                        map
                    );

            if (activeTerrain != null)
            {
                terrainElevation
                    .SetTerrain(
                        activeTerrain
                    );
            }
        }
        else
        {
            activeTerrain =
                terrainElevation
                    .AssignedTerrain;
        }

        // =====================================================
        // ELEVATION
        // =====================================================

        terrainElevation.Configure(
            transform
        );

        // =====================================================
        // TERRAIN -> ROAD CONFORM
        // =====================================================

        if (activeTerrain != null &&
            roadTerrainConformer != null)
        {
            roadTerrainConformer
                .Conform(
                    map,
                    roadNetwork,
                    transform,
                    activeTerrain
                );
        }

        // =====================================================
        // CREATE ROOTS
        // =====================================================

        OSMLandscapePainter landscapePainter = GetComponent<OSMLandscapePainter>();
        if (landscapePainter != null && activeTerrain != null)
            landscapePainter.Paint(map, activeTerrain, transform);

        CreateParents();

        // =====================================================
        // ROAD GENERATOR
        // =====================================================

        OSMRoadGenerator roadGenerator =
            new OSMRoadGenerator(
                roadsParent,
                roadMaterial,
                medianMaterials,
                terrainElevation,
                roadNetwork
            );

        // =====================================================
        // INTERSECTION
        // =====================================================

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

        // =====================================================
        // SIDEWALK
        // =====================================================

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

        // =====================================================
        // BUILDING
        // =====================================================

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

        int roadCount =
            0;

        if (generateRoads)
        {
            foreach (OSMWay way
                     in map.ways)
            {
                if (way == null)
                    continue;

                if (!OSMRoadMetrics
                        .IsRoad(
                            way
                        ))
                {
                    continue;
                }

                roadGenerator.Generate(
                    map,
                    way
                );

                roadCount++;
            }
        }

        // =====================================================
        // INTERSECTIONS
        // =====================================================

        if (generateRoads &&
            generateIntersections)
        {
            intersectionGenerator
                .Generate(
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
                if (way == null)
                    continue;

                if (!OSMRoadMetrics
                        .IsRoad(
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

        int buildingCount =
            0;

        if (generateBuildings)
        {
            foreach (OSMWay way
                     in map.ways)
            {
                if (way == null)
                    continue;

                if (!way.HasTag(
                        "building"
                    ))
                {
                    continue;
                }

                buildingGenerator.Generate(
                    map,
                    way
                );

                buildingCount++;
            }
        }

        // =====================================================
        // BUILDINGS -> TERRAIN
        // =====================================================

        if (generateBuildings &&
            terrainElevation != null &&
            buildingsParent != null)
        {
            terrainElevation
                .PlaceBuildings(
                    buildingsParent
                );
        }

        // =====================================================
        // CINEMATIC
        //
        // MUTLAKA binalardan sonra.
        // =====================================================

        OSMCityPropsGenerator streetProps = GetComponent<OSMCityPropsGenerator>();
        if (streetProps != null)
            streetProps.Generate(map);

        OSMCityCollision cityCollision = GetComponent<OSMCityCollision>();
        if (cityCollision != null)
            cityCollision.Refresh();

        SetupCinematicCamera(
            map
        );

        // =====================================================
        // DONE
        // =====================================================

        Debug.Log(
            "Istanbul generated.\n" +
            $"Road Ways: {roadCount}\n" +
            $"Building Ways: {buildingCount}\n" +
            $"Runtime: {Application.isPlaying}\n" +
            $"Cinematic: {cinematicActive}"
        );
    }

    // =========================================================
    // COMPONENTS
    // =========================================================

    private void EnsureRequiredComponents()
    {
        // =====================================================
        // AUTO TERRAIN
        // =====================================================

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
        // TERRAIN ELEVATION
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
        // ROAD TERRAIN CONFORMER
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

        // =====================================================
        // CINEMATIC CAMERA
        // =====================================================

        if (cinematicCityCamera == null)
        {
            cinematicCityCamera =
                GetComponent<
                    CinematicCityCamera
                >();
        }

        if (cinematicCityCamera == null)
        {
            cinematicCityCamera =
                gameObject.AddComponent<
                    CinematicCityCamera
                >();
        }
    }

    // =========================================================
    // CINEMATIC
    // =========================================================

    private void SetupCinematicCamera(
        OSMMapData map)
    {
        if (cinematicCityCamera == null)
        {
            cinematicCityCamera =
                GetComponent<
                    CinematicCityCamera
                >();
        }

        if (cinematicCityCamera == null)
        {
            cinematicCityCamera =
                gameObject.AddComponent<
                    CinematicCityCamera
                >();
        }

        cinematicCityCamera.Initialize(
            map,
            terrainElevation,
            transform,
            buildingsParent
        );

        cinematicCityCamera
            .SetCinematicActive(
                cinematicActive
            );
    }

    // =========================================================
    // CINEMATIC CONTROLS
    // =========================================================

    public void EnableCinematic()
    {
        cinematicActive =
            true;

        if (cinematicCityCamera != null)
        {
            cinematicCityCamera
                .SetCinematicActive(
                    true
                );
        }
    }

    public void DisableCinematic()
    {
        cinematicActive =
            false;

        if (cinematicCityCamera != null)
        {
            cinematicCityCamera
                .SetCinematicActive(
                    false
                );
        }
    }

    public void ToggleCinematic()
    {
        cinematicActive =
            !cinematicActive;

        if (cinematicCityCamera != null)
        {
            cinematicCityCamera
                .SetCinematicActive(
                    cinematicActive
                );
        }
    }

    // =========================================================
    // CREATE PARENTS
    // =========================================================

    private void CreateParents()
    {
        // =====================================================
        // ROADS
        // =====================================================

        GameObject roadsObject =
            new GameObject(
                "Roads"
            );

        roadsObject.transform
            .SetParent(
                transform,
                false
            );

        roadsParent =
            roadsObject.transform;

        // =====================================================
        // INTERSECTIONS
        // =====================================================

        GameObject intersectionObject =
            new GameObject(
                "Intersections"
            );

        intersectionObject.transform
            .SetParent(
                transform,
                false
            );

        intersectionsParent =
            intersectionObject.transform;

        // =====================================================
        // SIDEWALKS
        // =====================================================

        GameObject sidewalkObject =
            new GameObject(
                "Sidewalks"
            );

        sidewalkObject.transform
            .SetParent(
                transform,
                false
            );

        sidewalksParent =
            sidewalkObject.transform;

        // =====================================================
        // BUILDINGS
        // =====================================================

        GameObject buildingObject =
            new GameObject(
                "Buildings"
            );

        buildingObject.transform
            .SetParent(
                transform,
                false
            );

        buildingsParent =
            buildingObject.transform;
    }

    // =========================================================
    // CLEAR
    // =========================================================

    [ContextMenu("Clear Istanbul")]
    public void Clear()
    {
        if (cinematicCityCamera != null)
        {
            cinematicCityCamera
                .SetCinematicActive(
                    false
                );
        }

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

        Debug.Log(
            "Istanbul cleared."
        );
    }

    // =========================================================
    // CLEAR CITY OBJECTS
    // =========================================================

    private void ClearCityObjects()
    {
        DestroyChild(
            "Roads"
        );

        DestroyChild(
            "Intersections"
        );

        DestroyChild(
            "Sidewalks"
        );

        DestroyChild(
            "Buildings"
        );

        roadsParent =
            null;

        intersectionsParent =
            null;

        sidewalksParent =
            null;

        buildingsParent =
            null;
    }

    // =========================================================
    // DESTROY CHILD
    // =========================================================

    private void DestroyChild(
        string childName)
    {
        Transform child =
            transform.Find(
                childName
            );

        if (child == null)
            return;

        DestroyGeneratedObject(
            child.gameObject
        );
    }

    // =========================================================
    // DESTROY
    // =========================================================

    private void DestroyGeneratedObject(
        GameObject obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(
                obj
            );
        }
        else
        {
            DestroyImmediate(
                obj
            );
        }
    }
}