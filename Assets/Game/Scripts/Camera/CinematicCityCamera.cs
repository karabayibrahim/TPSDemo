using System.Collections.Generic;
using UnityEngine;

public class CinematicCityCamera : MonoBehaviour
{
    // =========================================================
    // CINEMATIC
    // =========================================================

    [Header("Cinematic")]

    [SerializeField]
    private bool cinematicActive = false;

    [Tooltip("Boþ býrakýlýrsa Camera.main kullanýlýr.")]
    [SerializeField]
    private Camera cinematicCamera;

    [Tooltip("Kameranýn yol yüzeyinden yüksekliði.")]
    [SerializeField]
    private float cameraHeight = 2.4f;

    [Tooltip("Metre / saniye.")]
    [SerializeField]
    private float movementSpeed = 4.5f;

    [Tooltip("Kamera dönüþ yumuþaklýðý.")]
    [SerializeField]
    private float rotationSmoothness = 3f;

    [Tooltip("Kamera kaç metre ileri baksýn.")]
    [SerializeField]
    private float lookAheadDistance = 8f;

    // =========================================================
    // NATURAL MOVEMENT
    // =========================================================

    [Header("Natural Movement")]

    [Tooltip("Yol merkezinden saða/sola küçük hareket.")]
    [SerializeField]
    private float lateralMovement = 0.10f;

    [SerializeField]
    private float lateralMovementSpeed = 0.25f;

    [Tooltip("Bakýþ hedefinin yol yüzeyinden yüksekliði.")]
    [SerializeField]
    private float lookHeightOffset = 1.8f;

    // =========================================================
    // ROAD SELECTION
    // =========================================================

    [Header("Road Selection")]

    [Tooltip("Residential / service / living_street yollarý daha çok tercih edilir.")]
    [SerializeField]
    private bool preferSmallStreets = true;

    [Tooltip("Motorway ve trunk yollarýna girmez.")]
    [SerializeField]
    private bool avoidHighways = true;

    // =========================================================
    // BUILDING AVOIDANCE
    // =========================================================

    [Header("Building Avoidance")]

    [Tooltip("Bina bounds çevresinde güvenlik payý.")]
    [SerializeField]
    private float buildingClearance = 0.5f;

    [Tooltip("Yol segmentinde kaç metrede bir bina kontrolü yapýlsýn.")]
    [SerializeField]
    private float buildingCheckSpacing = 1.5f;

    // =========================================================
    // ROUTE
    // =========================================================

    [Header("Route")]

    [Tooltip("Dead-end gelince geri dönebilir.")]
    [SerializeField]
    private bool allowTurnBackAtDeadEnd = true;

    [Tooltip("Baþlangýç rotasý için kaç deneme yapýlsýn.")]
    [SerializeField]
    private int startRouteAttempts = 200;

    // =========================================================
    // RANDOM
    // =========================================================

    [Header("Random")]

    [SerializeField]
    private int cinematicSeed = 58127;

    // =========================================================
    // REFERENCES
    // =========================================================

    private OSMMapData map;

    private OSMTerrainElevation elevation;

    private Transform cityRoot;

    private Transform buildingsParent;

    // =========================================================
    // ROAD GRAPH
    // =========================================================

    private readonly Dictionary<long, Vector3>
        nodePositions =
            new Dictionary<long, Vector3>();

    private readonly Dictionary<long, List<RoadLink>>
        graph =
            new Dictionary<long, List<RoadLink>>();

    // =========================================================
    // BUILDINGS
    // =========================================================

    private readonly List<Bounds>
        buildingBounds =
            new List<Bounds>();

    // =========================================================
    // RANDOM
    // =========================================================

    private System.Random random;

    // =========================================================
    // CURRENT ROUTE
    // =========================================================

    private long previousNodeId = -1;

    private long currentNodeId = -1;

    private long targetNodeId = -1;

    private Vector3 segmentStart;

    private Vector3 segmentEnd;

    private float segmentLength;

    private float segmentProgress;

    private bool initialized = false;

    private float smoothedCameraY;

    // =========================================================
    // ROAD LINK
    // =========================================================

    private class RoadLink
    {
        public long targetNodeId;

        public string highway;

        public float preferenceWeight;
    }

    // =========================================================
    // PUBLIC
    // =========================================================

    public bool CinematicActive
    {
        get
        {
            return cinematicActive;
        }
    }

    public bool IsInitialized
    {
        get
        {
            return initialized;
        }
    }

    public int RoadNodeCount
    {
        get
        {
            return graph.Count;
        }
    }

    // =========================================================
    // INITIALIZE
    // =========================================================

    public void Initialize(
        OSMMapData map,
        OSMTerrainElevation elevation,
        Transform cityRoot,
        Transform buildingsParent)
    {
        this.map =
            map;

        this.elevation =
            elevation;

        this.cityRoot =
            cityRoot;

        this.buildingsParent =
            buildingsParent;

        initialized =
            false;

        previousNodeId =
            -1;

        currentNodeId =
            -1;

        targetNodeId =
            -1;

        segmentProgress =
            0f;

        // =====================================================
        // CAMERA
        // =====================================================

        ResolveCamera();

        if (cinematicCamera == null)
        {
            Debug.LogError(
                "CinematicCityCamera: Main Camera bulunamadý. " +
                "Kameranýn Tag deðerini MainCamera yap."
            );

            return;
        }

        // =====================================================
        // RANDOM
        // =====================================================

        random =
            new System.Random(
                cinematicSeed
            );

        // =====================================================
        // BUILD GRAPH
        // =====================================================

        BuildRoadGraph();

        // =====================================================
        // BUILDING BOUNDS
        // =====================================================

        BuildBuildingBounds();

        // =====================================================
        // START ROUTE
        // =====================================================

        initialized =
            StartRandomRoute();

        Debug.Log(
            "Cinematic initialize -> " +
            $"Initialized: {initialized} | " +
            $"Road Nodes: {graph.Count} | " +
            $"Buildings: {buildingBounds.Count} | " +
            $"Camera: {(cinematicCamera != null ? cinematicCamera.name : "NULL")}"
        );

        if (initialized &&
            cinematicActive)
        {
            PlaceCameraImmediately();
        }
    }

    // =========================================================
    // CAMERA
    // =========================================================

    private void ResolveCamera()
    {
        if (cinematicCamera != null)
            return;

        cinematicCamera =
            Camera.main;

        if (cinematicCamera != null)
            return;

        Camera[] cameras =
            FindObjectsByType<Camera>(
                FindObjectsSortMode.None
            );

        if (cameras != null &&
            cameras.Length > 0)
        {
            cinematicCamera =
                cameras[0];

            Debug.LogWarning(
                "CinematicCityCamera: MainCamera tag bulunamadý. " +
                $"Geçici olarak {cinematicCamera.name} kullanýlýyor."
            );
        }
    }

    // =========================================================
    // SET ACTIVE
    // =========================================================

    public void SetCinematicActive(
        bool active)
    {
        cinematicActive =
            active;

        if (!active)
            return;

        ResolveCamera();

        if (!initialized)
        {
            Debug.LogWarning(
                "Cinematic aktif edildi fakat sistem henüz initialize edilmedi."
            );

            return;
        }

        if (currentNodeId < 0 ||
            targetNodeId < 0)
        {
            if (!StartRandomRoute())
            {
                Debug.LogWarning(
                    "Cinematic rota baþlatýlamadý."
                );

                return;
            }
        }

        PlaceCameraImmediately();
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!cinematicActive)
            return;

        if (!initialized)
            return;

        if (cinematicCamera == null)
        {
            ResolveCamera();

            if (cinematicCamera == null)
                return;
        }

        UpdateMovement();
    }

    // =========================================================
    // MOVEMENT
    // =========================================================

    private void UpdateMovement()
    {
        if (segmentLength <
            0.05f)
        {
            if (!AdvanceToNextRoad())
            {
                RestartRoute();
            }

            return;
        }

        segmentProgress +=
            movementSpeed *
            Time.deltaTime /
            segmentLength;

        while (segmentProgress >=
               1f)
        {
            segmentProgress -=
                1f;

            if (!AdvanceToNextRoad())
            {
                if (!RestartRoute())
                {
                    return;
                }
            }
        }

        // =====================================================
        // ROAD POSITION
        // =====================================================

        Vector3 localPosition =
            Vector3.Lerp(
                segmentStart,
                segmentEnd,
                segmentProgress
            );

        Vector3 direction =
            segmentEnd -
            segmentStart;

        direction.y = 0f;

        if (direction.sqrMagnitude <
            0.0001f)
        {
            direction =
                Vector3.forward;
        }
        else
        {
            direction.Normalize();
        }

        // =====================================================
        // SMALL LATERAL MOTION
        // =====================================================

        Vector3 right =
            new Vector3(
                direction.z,
                0f,
                -direction.x
            );

        float lateral =
            Mathf.Sin(
                Time.time *
                lateralMovementSpeed
            ) *
            lateralMovement;

        localPosition +=
            right *
            lateral;

        // =====================================================
        // HEIGHT
        // =====================================================

        float desiredY =
            GetRoadHeight(
                localPosition
            ) +
            cameraHeight;

        if (smoothedCameraY ==
            0f)
        {
            smoothedCameraY =
                desiredY;
        }

        smoothedCameraY =
            Mathf.Lerp(
                smoothedCameraY,
                desiredY,
                1f -
                Mathf.Exp(
                    -7f *
                    Time.deltaTime
                )
            );

        localPosition.y =
            smoothedCameraY;

        // =====================================================
        // WORLD POSITION
        // =====================================================

        Vector3 worldPosition =
            LocalToWorld(
                localPosition
            );

        cinematicCamera.transform.position =
            worldPosition;

        // =====================================================
        // LOOK TARGET
        // =====================================================

        Vector3 lookLocal =
            localPosition +
            direction *
            lookAheadDistance;

        lookLocal.y =
            GetRoadHeight(
                lookLocal
            ) +
            lookHeightOffset;

        Vector3 lookWorld =
            LocalToWorld(
                lookLocal
            );

        Vector3 lookDirection =
            lookWorld -
            cinematicCamera.transform.position;

        if (lookDirection.sqrMagnitude >
            0.001f)
        {
            Quaternion desiredRotation =
                Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up
                );

            cinematicCamera.transform.rotation =
                Quaternion.Slerp(
                    cinematicCamera.transform.rotation,
                    desiredRotation,
                    1f -
                    Mathf.Exp(
                        -rotationSmoothness *
                        Time.deltaTime
                    )
                );
        }
    }

    // =========================================================
    // NEXT ROAD
    // =========================================================

    private bool AdvanceToNextRoad()
    {
        if (targetNodeId < 0)
            return false;

        previousNodeId =
            currentNodeId;

        currentNodeId =
            targetNodeId;

        RoadLink next =
            ChooseNextLink(
                currentNodeId,
                previousNodeId
            );

        // =====================================================
        // DEAD END
        // =====================================================

        if (next == null &&
            allowTurnBackAtDeadEnd)
        {
            next =
                FindLink(
                    currentNodeId,
                    previousNodeId
                );
        }

        if (next == null)
            return false;

        targetNodeId =
            next.targetNodeId;

        return
            SetupCurrentSegment();
    }

    // =========================================================
    // RESTART ROUTE
    // =========================================================

    private bool RestartRoute()
    {
        bool success =
            StartRandomRoute();

        if (success)
        {
            PlaceCameraImmediately();
        }

        return success;
    }

    // =========================================================
    // START RANDOM ROUTE
    // =========================================================

    private bool StartRandomRoute()
    {
        if (graph.Count == 0)
            return false;

        List<long> candidates =
            new List<long>();

        foreach (
            KeyValuePair<
                long,
                List<RoadLink>
            > pair
            in graph)
        {
            if (pair.Value != null &&
                pair.Value.Count > 0)
            {
                candidates.Add(
                    pair.Key
                );
            }
        }

        if (candidates.Count == 0)
            return false;

        int attempts =
            Mathf.Max(
                10,
                startRouteAttempts
            );

        for (int attempt = 0;
             attempt < attempts;
             attempt++)
        {
            long start =
                candidates[
                    random.Next(
                        candidates.Count
                    )
                ];

            RoadLink next =
                ChooseNextLink(
                    start,
                    -1
                );

            if (next == null)
                continue;

            previousNodeId =
                -1;

            currentNodeId =
                start;

            targetNodeId =
                next.targetNodeId;

            segmentProgress =
                0f;

            if (SetupCurrentSegment())
            {
                return true;
            }
        }

        /*
         * Bina kontrolü yüzünden rota bulunamazsa
         * herhangi bir geçerli road link dene.
         */

        foreach (long start
                 in candidates)
        {
            if (!graph.TryGetValue(
                    start,
                    out List<RoadLink> links))
            {
                continue;
            }

            if (links.Count == 0)
                continue;

            previousNodeId =
                -1;

            currentNodeId =
                start;

            targetNodeId =
                links[0].targetNodeId;

            segmentProgress =
                0f;

            if (SetupCurrentSegment())
            {
                Debug.LogWarning(
                    "Cinematic: güvenli rota bulunamadý, fallback yol kullanýlýyor."
                );

                return true;
            }
        }

        return false;
    }

    // =========================================================
    // SEGMENT
    // =========================================================

    private bool SetupCurrentSegment()
    {
        if (!nodePositions.TryGetValue(
                currentNodeId,
                out segmentStart))
        {
            return false;
        }

        if (!nodePositions.TryGetValue(
                targetNodeId,
                out segmentEnd))
        {
            return false;
        }

        Vector3 horizontal =
            segmentEnd -
            segmentStart;

        horizontal.y =
            0f;

        segmentLength =
            horizontal.magnitude;

        return
            segmentLength >
            0.05f;
    }

    // =========================================================
    // CHOOSE NEXT LINK
    // =========================================================

    private RoadLink ChooseNextLink(
        long nodeId,
        long nodeToAvoid)
    {
        if (!graph.TryGetValue(
                nodeId,
                out List<RoadLink> links))
        {
            return null;
        }

        if (links == null ||
            links.Count == 0)
        {
            return null;
        }

        List<RoadLink> safeLinks =
            new List<RoadLink>();

        List<RoadLink> fallbackLinks =
            new List<RoadLink>();

        foreach (RoadLink link
                 in links)
        {
            if (link == null)
                continue;

            if (link.targetNodeId ==
                    nodeToAvoid &&
                links.Count > 1)
            {
                continue;
            }

            fallbackLinks.Add(
                link
            );

            if (!nodePositions.TryGetValue(
                    nodeId,
                    out Vector3 start))
            {
                continue;
            }

            if (!nodePositions.TryGetValue(
                    link.targetNodeId,
                    out Vector3 end))
            {
                continue;
            }

            if (SegmentPassesThroughBuilding(
                    start,
                    end))
            {
                continue;
            }

            safeLinks.Add(
                link
            );
        }

        List<RoadLink> candidates =
            safeLinks.Count > 0
                ? safeLinks
                : fallbackLinks;

        if (candidates.Count == 0)
            return null;

        return
            WeightedRandomLink(
                candidates
            );
    }

    // =========================================================
    // WEIGHTED RANDOM
    // =========================================================

    private RoadLink WeightedRandomLink(
        List<RoadLink> links)
    {
        float total =
            0f;

        foreach (RoadLink link
                 in links)
        {
            total +=
                Mathf.Max(
                    0.01f,
                    link.preferenceWeight
                );
        }

        float roll =
            (float)random.NextDouble() *
            total;

        float current =
            0f;

        foreach (RoadLink link
                 in links)
        {
            current +=
                Mathf.Max(
                    0.01f,
                    link.preferenceWeight
                );

            if (roll <=
                current)
            {
                return link;
            }
        }

        return
            links[
                links.Count - 1
            ];
    }

    // =========================================================
    // FIND SPECIFIC LINK
    // =========================================================

    private RoadLink FindLink(
        long from,
        long to)
    {
        if (!graph.TryGetValue(
                from,
                out List<RoadLink> links))
        {
            return null;
        }

        foreach (RoadLink link
                 in links)
        {
            if (link.targetNodeId ==
                to)
            {
                return link;
            }
        }

        return null;
    }

    // =========================================================
    // BUILD GRAPH
    // =========================================================

    private void BuildRoadGraph()
    {
        graph.Clear();

        nodePositions.Clear();

        if (map == null)
            return;

        foreach (OSMWay way
                 in map.ways)
        {
            if (way == null ||
                !OSMRoadMetrics.IsRoad(
                    way) ||
                way.nodeIds == null ||
                way.nodeIds.Count < 2)
            {
                continue;
            }

            string highway =
                way.GetTag(
                    "highway"
                )
                .Trim()
                .ToLowerInvariant();

            if (!CanUseRoad(
                    highway))
            {
                continue;
            }

            float preference =
                GetRoadPreference(
                    highway
                );

            for (int i = 0;
                 i < way.nodeIds.Count - 1;
                 i++)
            {
                long a =
                    way.nodeIds[i];

                long b =
                    way.nodeIds[i + 1];

                if (a == b)
                    continue;

                if (!TryAddNodePosition(
                        a))
                {
                    continue;
                }

                if (!TryAddNodePosition(
                        b))
                {
                    continue;
                }

                AddGraphLink(
                    a,
                    b,
                    highway,
                    preference
                );

                AddGraphLink(
                    b,
                    a,
                    highway,
                    preference
                );
            }
        }
    }

    // =========================================================
    // NODE POSITION
    // =========================================================

    private bool TryAddNodePosition(
        long nodeId)
    {
        if (nodePositions.ContainsKey(
                nodeId))
        {
            return true;
        }

        if (!map.nodes.TryGetValue(
                nodeId,
                out OSMNode node))
        {
            return false;
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

        nodePositions.Add(
            nodeId,
            point
        );

        return true;
    }

    // =========================================================
    // ADD GRAPH LINK
    // =========================================================

    private void AddGraphLink(
        long from,
        long to,
        string highway,
        float preference)
    {
        if (!graph.TryGetValue(
                from,
                out List<RoadLink> list))
        {
            list =
                new List<RoadLink>();

            graph.Add(
                from,
                list
            );
        }

        foreach (RoadLink existing
                 in list)
        {
            if (existing.targetNodeId ==
                to)
            {
                return;
            }
        }

        list.Add(
            new RoadLink
            {
                targetNodeId =
                    to,

                highway =
                    highway,

                preferenceWeight =
                    preference
            }
        );
    }

    // =========================================================
    // ROAD FILTER
    // =========================================================

    private bool CanUseRoad(
        string highway)
    {
        switch (highway)
        {
            case "footway":
            case "path":
            case "cycleway":
            case "steps":
            case "pedestrian":
            case "track":
                return false;

            case "motorway":
            case "motorway_link":
            case "trunk":
            case "trunk_link":

                return
                    !avoidHighways;
        }

        return true;
    }

    // =========================================================
    // ROAD PREFERENCE
    // =========================================================

    private float GetRoadPreference(
        string highway)
    {
        if (!preferSmallStreets)
            return 1f;

        switch (highway)
        {
            case "living_street":
                return 7f;

            case "service":
                return 6f;

            case "residential":
                return 6f;

            case "unclassified":
                return 4f;

            case "tertiary":
                return 3f;

            case "tertiary_link":
                return 2.5f;

            case "secondary":
                return 1.8f;

            case "secondary_link":
                return 1.5f;

            case "primary":
                return 0.9f;

            case "primary_link":
                return 0.8f;

            case "trunk":
                return 0.2f;

            case "motorway":
                return 0.1f;

            default:
                return 2f;
        }
    }

    // =========================================================
    // BUILDING BOUNDS
    // =========================================================

    private void BuildBuildingBounds()
    {
        buildingBounds.Clear();

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
            if (building == null)
                continue;

            Renderer[] renderers =
                building
                    .GetComponentsInChildren<Renderer>(
                        true
                    );

            if (renderers == null ||
                renderers.Length == 0)
            {
                continue;
            }

            bool hasBounds =
                false;

            Bounds combined =
                new Bounds();

            foreach (Renderer renderer
                     in renderers)
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    combined =
                        renderer.bounds;

                    hasBounds =
                        true;
                }
                else
                {
                    combined.Encapsulate(
                        renderer.bounds
                    );
                }
            }

            if (hasBounds)
            {
                buildingBounds.Add(
                    combined
                );
            }
        }
    }

    // =========================================================
    // BUILDING CHECK
    // =========================================================

    private bool SegmentPassesThroughBuilding(
        Vector3 localStart,
        Vector3 localEnd)
    {
        if (buildingBounds.Count == 0 ||
            cityRoot == null)
        {
            return false;
        }

        float distance =
            Vector3.Distance(
                localStart,
                localEnd
            );

        int samples =
            Mathf.Max(
                2,
                Mathf.CeilToInt(
                    distance /
                    Mathf.Max(
                        0.5f,
                        buildingCheckSpacing
                    )
                )
            );

        for (int i = 1;
             i < samples;
             i++)
        {
            float t =
                i /
                (float)samples;

            Vector3 localPoint =
                Vector3.Lerp(
                    localStart,
                    localEnd,
                    t
                );

            Vector3 worldPoint =
                LocalToWorld(
                    localPoint
                );

            foreach (Bounds bounds
                     in buildingBounds)
            {
                if (worldPoint.x >
                        bounds.min.x -
                        buildingClearance &&
                    worldPoint.x <
                        bounds.max.x +
                        buildingClearance &&
                    worldPoint.z >
                        bounds.min.z -
                        buildingClearance &&
                    worldPoint.z <
                        bounds.max.z +
                        buildingClearance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // =========================================================
    // HEIGHT
    // =========================================================

    private float GetRoadHeight(
        Vector3 localPoint)
    {
        if (elevation == null)
            return 0f;

        return
            elevation.SampleRoadSurfaceHeight(
                localPoint
            );
    }

    // =========================================================
    // LOCAL -> WORLD
    // =========================================================

    private Vector3 LocalToWorld(
        Vector3 localPoint)
    {
        if (cityRoot == null)
            return localPoint;

        return
            cityRoot.TransformPoint(
                localPoint
            );
    }

    // =========================================================
    // PLACE CAMERA
    // =========================================================

    private void PlaceCameraImmediately()
    {
        if (cinematicCamera == null)
            return;

        if (currentNodeId < 0 ||
            targetNodeId < 0)
        {
            return;
        }

        Vector3 localPosition =
            Vector3.Lerp(
                segmentStart,
                segmentEnd,
                segmentProgress
            );

        localPosition.y =
            GetRoadHeight(
                localPosition
            ) +
            cameraHeight;

        smoothedCameraY =
            localPosition.y;

        Vector3 worldPosition =
            LocalToWorld(
                localPosition
            );

        cinematicCamera.transform.position =
            worldPosition;

        Vector3 direction =
            segmentEnd -
            segmentStart;

        direction.y =
            0f;

        if (direction.sqrMagnitude <
            0.001f)
        {
            return;
        }

        direction.Normalize();

        Vector3 lookLocal =
            localPosition +
            direction *
            lookAheadDistance;

        lookLocal.y =
            GetRoadHeight(
                lookLocal
            ) +
            lookHeightOffset;

        Vector3 lookWorld =
            LocalToWorld(
                lookLocal
            );

        cinematicCamera.transform.rotation =
            Quaternion.LookRotation(
                (
                    lookWorld -
                    worldPosition
                ).normalized,
                Vector3.up
            );
    }
}