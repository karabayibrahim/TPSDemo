using UnityEngine;

public class IstanbulCityGenerator :
    MonoBehaviour
{
    // =========================================================
    // OSM
    // =========================================================

    [Header("OSM")]

    [SerializeField]
    private TextAsset osmFile;

    // =========================================================
    // ROAD
    // =========================================================

    [Header("Road")]

    [SerializeField]
    private Material roadMaterial;

    // =========================================================
    // FACADE
    // =========================================================

    [Header("Building - Facade Materials")]

    [Tooltip(
        "Her bina bu listeden bir dýþ cephe materyali seçer."
    )]
    [SerializeField]
    private Material[] buildingMaterials;

    // =========================================================
    // GLASS
    // =========================================================

    [Header("Building - Glass Materials")]

    [Tooltip(
        "Her bina için bir cam tipi seçilir. " +
        "Ayný binadaki pencereler ayný cam materyalini kullanýr."
    )]
    [SerializeField]
    private Material[] glassMaterials;

    // =========================================================
    // SHOP
    // =========================================================

    [Header("Building - Shop Materials")]

    [Tooltip(
        "Dükkanlarda kullanýlacak materyaller. " +
        "Bir binada maksimum 3 farklý dükkan materyali kullanýlýr."
    )]
    [SerializeField]
    private Material[] shopMaterials;

    // =========================================================
    // BALCONY
    // =========================================================

    [Header("Building - Balcony Materials")]

    [Tooltip(
        "Balkon slab ve korkuluklarýnda kullanýlacak materyaller. " +
        "Bir binada maksimum 2 farklý balkon materyali kullanýlýr."
    )]
    [SerializeField]
    private Material[] balconyMaterials;

    // =========================================================
    // GENERATION
    // =========================================================

    [Header("Generation")]

    [SerializeField]
    private bool generateRoads =
        true;

    [SerializeField]
    private bool generateBuildings =
        true;

    // =========================================================
    // ROOTS
    // =========================================================

    private Transform roadsParent;

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

        Clear();

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
        // ROOTS
        // =====================================================

        CreateParents();

        // =====================================================
        // ROAD GENERATOR
        // =====================================================

        OSMRoadGenerator roadGenerator =
            new OSMRoadGenerator(
                roadsParent,
                roadMaterial
            );

        // =====================================================
        // BUILDING GENERATOR
        // =====================================================

        OSMBuildingGenerator buildingGenerator =
            new OSMBuildingGenerator(
                buildingsParent,
                buildingMaterials,
                glassMaterials,
                shopMaterials,
                balconyMaterials
            );

        // =====================================================
        // WAYS
        // =====================================================

        foreach (OSMWay way
                 in map.ways)
        {
            if (generateRoads)
            {
                roadGenerator.Generate(
                    map,
                    way
                );
            }

            if (generateBuildings)
            {
                buildingGenerator.Generate(
                    map,
                    way
                );
            }
        }

        Debug.Log(
            "Istanbul generation completed."
        );
    }

    // =========================================================
    // CREATE ROOTS
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
        Transform roads =
            transform.Find(
                "Roads"
            );

        Transform buildings =
            transform.Find(
                "Buildings"
            );

        if (roads != null)
        {
            DestroyGeneratedObject(
                roads.gameObject
            );
        }

        if (buildings != null)
        {
            DestroyGeneratedObject(
                buildings.gameObject
            );
        }

        roadsParent =
            null;

        buildingsParent =
            null;
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