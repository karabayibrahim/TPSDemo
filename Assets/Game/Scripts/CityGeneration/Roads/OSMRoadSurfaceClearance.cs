using System.Collections.Generic;
using UnityEngine;

public static class OSMRoadSurfaceClearance
{
    // Terrain is interpolated differently from the road triangles. Check inside
    // each triangle as well as at its vertices, without adding more geometry.
    public static void Apply(List<Vector3> vertices, List<int> triangles, OSMTerrainElevation elevation)
    {
        if (elevation == null) return;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            float lift = 0f;
            for (int sample = 0; sample < 4; sample++)
            {
                Vector3 p = sample == 0 ? (vertices[a] + vertices[b] + vertices[c]) / 3f :
                    sample == 1 ? vertices[a] * 0.6f + vertices[b] * 0.2f + vertices[c] * 0.2f :
                    sample == 2 ? vertices[a] * 0.2f + vertices[b] * 0.6f + vertices[c] * 0.2f :
                    vertices[a] * 0.2f + vertices[b] * 0.2f + vertices[c] * 0.6f;
                lift = Mathf.Max(lift, elevation.SampleLocalHeight(p) + 0.025f - p.y);
            }
            if (lift <= 0f) continue;
            vertices[a] += Vector3.up * lift;
            vertices[b] += Vector3.up * lift;
            vertices[c] += Vector3.up * lift;
        }
    }
}
