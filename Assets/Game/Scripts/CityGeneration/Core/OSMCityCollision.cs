using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class OSMCityCollision : MonoBehaviour
{
    private void Start() { Refresh(); }

    [ContextMenu("Refresh City Colliders")]
    public void Refresh()
    {
        foreach (string name in new[] { "Roads", "Sidewalks", "Intersections" })
        {
            Transform root = transform.Find(name);
            if (root == null) continue;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
                if (collider.sharedMesh != filter.sharedMesh) collider.sharedMesh = filter.sharedMesh;
            }
        }
        foreach (BuildingInfo info in GetComponentsInChildren<BuildingInfo>())
        {
            if (info.footprint == null || info.footprint.Length < 3 || info.transform.Find("BuildingCollision") != null) continue;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < info.footprint.Length; i++)
            {
                Vector3 a = info.footprint[i], b = info.footprint[(i + 1) % info.footprint.Length];
                a.y = b.y = 0f; int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(a + Vector3.up * info.height); vertices.Add(b + Vector3.up * info.height);
                // CCW OSM footprints have their exterior on the right of each edge.
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
            // Keep the visible roof collision exact, but skip thousands of window/frame triangles.
            MeshFilter visual = info.GetComponentInChildren<MeshFilter>();
            if (visual != null && visual.sharedMesh != null)
            {
                Mesh mesh = visual.sharedMesh; Vector3[] source = mesh.vertices;
                int[] roof = mesh.GetTriangles(mesh.subMeshCount - 1);
                var remap = new Dictionary<int, int>();
                foreach (int index in roof)
                {
                    if (!remap.TryGetValue(index, out int mapped)) { mapped = vertices.Count; remap[index] = mapped; vertices.Add(source[index]); }
                    triangles.Add(mapped);
                }
            }
            var collisionMesh = new Mesh { name = "BuildingCollision_" + info.osmId };
            collisionMesh.SetVertices(vertices); collisionMesh.SetTriangles(triangles, 0); collisionMesh.RecalculateBounds();
            var child = new GameObject("BuildingCollision"); child.transform.SetParent(info.transform, false);
            child.AddComponent<MeshCollider>().sharedMesh = collisionMesh;
        }
    }
}
