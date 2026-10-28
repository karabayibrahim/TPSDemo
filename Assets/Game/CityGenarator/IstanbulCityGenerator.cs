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
    // ROAD
    // =========================================================

    [Header("Road")]

    [SerializeField]
    private Material roadMaterial;

    // =========================================================
    // BUILDING MATERIALS
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
        "Her bina bu listeden bir cam materyali seçer."
    )]
    [SerializeField]
    private Material[] glassMaterials;

    // =========================================================
    // SHOP
    // =========================================================

    [Header("Building - Shop Materials")]

    [Tooltip(
        "Dükkanlarda kullanýlacak materyaller. " +
        "Bir bina maksimum 3 farklý shop materyali kullanýr."
    )]
    [SerializeField]
    private Material[] shopMaterials;

    // =========================================================
    // BALCONY
    // =========================================================

    [Header("Building - Balcony Materials")]

    [Tooltip(
        "Balkonlarda kullanýlacak materyaller. " +
        "Bir bina maksimum 2 farklý balkon materyali kullanýr."
    )]
    [SerializeField]
    private Material[] balconyMaterials;

    // =========================================================
    // ROOF
    // =========================================================

    [Header("Building - Roof Materials")]

    [Tooltip(
        "Düz ve eðimli çatýlarda kullanýlacak materyaller."
    )]
    [SerializeField]
    private Material[] roofMaterials;

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
    // PARENTS
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

        CreateParents();

        OSMRoadGenerator roadGenerator =
            new OSMRoadGenerator(
                roadsParent,
                roadMaterial
            );

        OSMBuildingGenerator buildingGenerator =
            new OSMBuildingGenerator(
                buildingsParent,
                buildingMaterials,
                glassMaterials,
                shopMaterials,
                balconyMaterials,
                roofMaterials
            );

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
    // CREATE PARENTS
    // =========================================================

    private void CreateParents()
    {
        GameObject roadsObject =
            new GameObject(
                "Roads"
            );

        roadsObject.transform.SetParent(
            transform,
            false
        );

        roadsParent =
            roadsObject.transform;

        GameObject buildingsObject =
            new GameObject(
                "Buildings"
            );

        buildingsObject.transform.SetParent(
            transform,
            false
        );

        buildingsParent =
            buildingsObject.transform;
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

        roadsParent = null;
        buildingsParent = null;
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