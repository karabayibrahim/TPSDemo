using System.Collections.Generic;
using UnityEngine;

public static class OSMInteriorMappingData
{
    public static void Prepare(Mesh mesh, int shopSubmeshCount = 0)
    {
        if (mesh == null || mesh.subMeshCount < 2) return;
        for (int submesh = 1; submesh <= 1 + shopSubmeshCount; submesh++)
            PrepareSubmesh(mesh, submesh);
    }

    public static void PrepareSubmesh(Mesh mesh, int submesh)
    {
        if (mesh == null || submesh >= mesh.subMeshCount) return;
        var vertices = mesh.vertices;
        var data = new List<Vector4>();
        mesh.GetUVs(1, data);
        if (data.Count != vertices.Length)
            data = new List<Vector4>(new Vector4[vertices.Length]);
        var triangles = mesh.GetTriangles(submesh);
        // Glass is emitted as independent quads with four consecutive vertices.
        for (int t = 0; t + 5 < triangles.Length; t += 6)
        {
            int first = Mathf.Min(triangles[t], Mathf.Min(triangles[t + 1], triangles[t + 2]));
            if (first + 3 >= vertices.Length) continue;
            Vector3 center = (vertices[first] + vertices[first + 3]) * 0.5f;
            float seed = Mathf.Repeat(Mathf.Sin(Vector3.Dot(center, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f, 1f);
            Vector4 room = new Vector4(Vector3.Distance(vertices[first], vertices[first + 1]),
                Vector3.Distance(vertices[first], vertices[first + 2]), seed, 0f);
            for (int i = 0; i < 4; i++) data[first + i] = room;
        }
        mesh.SetUVs(1, data);
    }
}
