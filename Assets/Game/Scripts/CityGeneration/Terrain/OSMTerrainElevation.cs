using System.Collections.Generic;
using UnityEngine;

public class OSMTerrainElevation : MonoBehaviour
{
    public enum BuildingPlacementMode
    {
        Minimum,
        Average,
        Median
    }

    // =========================================================
    // TERRAIN
    // =========================================================

    [Header("Terrain")]

    [SerializeField]
    private Terrain terrain;

    // =========================================================
    // SURFACE OFFSETS
    // =========================================================

    [Header("Surface Offsets")]

    [Tooltip(
        "Asfalt Terrain yüzeyinin kaç metre üzerinde olsun. " +
        "0.05 - 0.08 arasý iyi sonuç verir."
    )]
    [SerializeField]
    private float roadSurfaceOffset = 0.06f;

    [SerializeField]
    private float buildingFoundationSink = 0.08f;

    // =========================================================
    // BUILDINGS
    // =========================================================

    [Header("Building Placement")]

    [SerializeField]
    private BuildingPlacementMode buildingPlacementMode =
        BuildingPlacementMode.Minimum;

    private Transform cityRoot;

    // =========================================================
    // PUBLIC
    // =========================================================

    public float RoadSurfaceOffset
    {
        get
        {
            return roadSurfaceOffset;
        }
    }

    public Terrain AssignedTerrain
    {
        get
        {
            return terrain;
        }
    }

    // =========================================================
    // SET TERRAIN
    // =========================================================

    public void SetTerrain(
        Terrain newTerrain)
    {
        terrain =
            newTerrain;
    }

    // =========================================================
    // CONFIGURE
    // =========================================================

    public void Configure(
        Transform root)
    {
        cityRoot =
            root;

        if (terrain == null)
        {
            terrain =
                GetComponentInChildren<Terrain>();
        }
    }

    // =========================================================
    // RAW TERRAIN HEIGHT
    // =========================================================

    public float SampleLocalHeight(
        Vector3 cityLocalPoint)
    {
        if (terrain == null)
            return 0f;

        if (cityRoot == null)
        {
            cityRoot =
                transform;
        }

        Vector3 worldPoint =
            cityRoot.TransformPoint(
                new Vector3(
                    cityLocalPoint.x,
                    0f,
                    cityLocalPoint.z
                )
            );

        float worldY =
            terrain.SampleHeight(
                worldPoint
            ) +
            terrain.transform.position.y;

        Vector3 resultWorld =
            new Vector3(
                worldPoint.x,
                worldY,
                worldPoint.z
            );

        Vector3 resultLocal =
            cityRoot.InverseTransformPoint(
                resultWorld
            );

        return
            resultLocal.y;
    }

    // =========================================================
    // ROAD SURFACE
    //
    // Road, intersection ve kaldýrým bordürü
    // AYNI fonksiyonu kullanacak.
    // =========================================================

    public float SampleRoadSurfaceHeight(
        Vector3 cityLocalPoint)
    {
        return
            SampleLocalHeight(
                cityLocalPoint
            ) +
            roadSurfaceOffset;
    }

    // =========================================================
    // BUILDING BASE
    // =========================================================

    public float GetBuildingBaseHeight(
        Vector3[] footprint)
    {
        if (footprint == null ||
            footprint.Length == 0)
        {
            return 0f;
        }

        List<float> heights =
            new List<float>();

        Vector3 center =
            Vector3.zero;

        foreach (Vector3 point
                 in footprint)
        {
            center += point;

            heights.Add(
                SampleLocalHeight(
                    point
                )
            );
        }

        center /=
            footprint.Length;

        heights.Add(
            SampleLocalHeight(
                center
            )
        );

        float result;

        switch (buildingPlacementMode)
        {
            case BuildingPlacementMode.Average:
                {
                    float total = 0f;

                    foreach (float value
                             in heights)
                    {
                        total += value;
                    }

                    result =
                        total /
                        heights.Count;

                    break;
                }

            case BuildingPlacementMode.Median:
                {
                    heights.Sort();

                    int middle =
                        heights.Count /
                        2;

                    if (heights.Count %
                        2 == 0)
                    {
                        result =
                            (
                                heights[middle - 1] +
                                heights[middle]
                            ) *
                            0.5f;
                    }
                    else
                    {
                        result =
                            heights[middle];
                    }

                    break;
                }

            case BuildingPlacementMode.Minimum:
            default:
                {
                    result =
                        float.MaxValue;

                    foreach (float value
                             in heights)
                    {
                        result =
                            Mathf.Min(
                                result,
                                value
                            );
                    }

                    break;
                }
        }

        return
            result -
            buildingFoundationSink;
    }

    // =========================================================
    // BUILDINGS
    // =========================================================

    public void PlaceBuildings(
        Transform buildingsParent)
    {
        if (buildingsParent == null)
            return;

        BuildingInfo[] buildings =
            buildingsParent
                .GetComponentsInChildren<BuildingInfo>(
                    true
                );

        foreach (BuildingInfo building
                 in buildings)
        {
            if (building == null ||
                building.footprint == null ||
                building.footprint.Length == 0)
            {
                continue;
            }

            float y =
                GetBuildingBaseHeight(
                    building.footprint
                );

            Vector3 position =
                building.transform.localPosition;

            position.y = y;

            building.transform.localPosition =
                position;
        }
    }
}