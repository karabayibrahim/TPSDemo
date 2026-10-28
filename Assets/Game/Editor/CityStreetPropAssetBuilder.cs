using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityStreetPropAssetBuilder
{
    private const string Folder = "Assets/Game/";
    private static Material material;

    [MenuItem("Tools/City/Build Street Prop Prefabs")]
    public static void Build()
    {
        material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Materials/StreetProps/StreetProps.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("City/Street Props")) { name = "City Street Props", enableInstancing = true };
            AssetDatabase.CreateAsset(material, Folder + "Materials/StreetProps/StreetProps.mat");
        }
        string[] cars = { "sedan", "hatchback-sports", "suv", "van", "delivery", "taxi" };
        Vector3[] sizes = { new Vector3(1.78f, 1.46f, 4.3f), new Vector3(1.75f, 1.45f, 3.8f), new Vector3(1.86f, 1.65f, 4.5f), new Vector3(1.9f, 2f, 4.8f), new Vector3(1.9f, 2.25f, 5f), new Vector3(1.78f, 1.46f, 4.3f) };
        Color[] paints = { new Color(0.67f, 0.66f, 0.61f), new Color(0.24f, 0.28f, 0.32f), new Color(0.26f, 0.16f, 0.14f) };
        for (int i = 0; i < cars.Length; i++)
            for (int variant = 0; variant < (cars[i] == "taxi" ? 1 : paints.Length); variant++)
                BuildModel("cars", cars[i], "Car_" + cars[i] + "_" + variant,
                    sizes[i], cars[i] == "taxi" ? new Color(0.76f, 0.5f, 0.07f) : paints[variant], true, false);
        BuildModel("roads", "light-curved", "Lamp_Curved", new Vector3(0f, 6.4f, 0f), Color.gray, false, true);
        BuildModel("roads", "light-square", "Lamp_Square", new Vector3(0f, 6.4f, 0f), Color.gray, false, true);
        BuildModel("roads", "dumpster", "Dumpster_Green", new Vector3(1.35f, 1.05f, 1.05f), new Color(0.2f, 0.3f, 0.25f), false, false);
        BuildModel("roads", "dumpster", "Dumpster_Grey", new Vector3(1.35f, 1.05f, 1.05f), new Color(0.32f, 0.34f, 0.34f), false, false);
        BuildBin();
        AssetDatabase.Refresh();
    }

    private static void BuildModel(string pack, string source, string name, Vector3 size, Color tint, bool vehicle, bool lamp)
    {
        string path = "Assets/ThirdParty/Kenney/" + pack + "/Models/FBX format/";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path + source + ".fbx");
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(path + "Textures/colormap.png");
        if (!textureImporter.isReadable) { textureImporter.isReadable = true; textureImporter.SaveAndReimport(); }
        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(path + "Textures/colormap.png");
        GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path + source + ".fbx"));
        try
        {
            Bounds bounds = new Bounds(); bool first = true;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            Vector3 scale = lamp ? Vector3.one * (size.y / bounds.size.y) :
                new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            if (lamp) { scale.x *= 0.7f; scale.z *= 0.75f; }
            Vector3 offset = lamp ? new Vector3(0f, -bounds.min.y * scale.y, 0f) :
                new Vector3(-bounds.center.x * scale.x, -bounds.min.y * scale.y, -bounds.center.z * scale.z);
            Matrix4x4 normalize = Matrix4x4.TRS(offset, lamp ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, scale);
            var combines = new List<CombineInstance>();
            var temporary = new List<Mesh>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh copy = Object.Instantiate(filter.sharedMesh);
                Vector2[] uvs = copy.uv;
                Color[] colors = new Color[copy.vertexCount];
                for (int i = 0; i < colors.Length; i++)
                {
                    Color original = uvs.Length > i ? palette.GetPixelBilinear(uvs[i].x, uvs[i].y) : Color.gray;
                    float brightness = original.grayscale;
                    float saturation = Mathf.Max(original.r, Mathf.Max(original.g, original.b)) - Mathf.Min(original.r, Mathf.Min(original.g, original.b));
                    Color color = Color.Lerp(new Color(brightness, brightness, brightness), original, 0.45f) * 0.8f;
                    if (vehicle && saturation > 0.24f)
                        color = original.b > original.r && original.g > original.r && brightness > 0.56f ?
                            new Color(0.15f, 0.22f, 0.27f) : tint * Mathf.Lerp(0.65f, 1f, brightness);
                    if (vehicle && brightness > 0.76f && saturation < 0.24f)
                        color = new Color(0.12f, 0.18f, 0.22f);
                    else if (!vehicle && !lamp && brightness > 0.08f) color = tint * Mathf.Lerp(0.6f, 1.2f, brightness);
                    if (lamp) color = Color.Lerp(new Color(0.06f, 0.075f, 0.085f), new Color(0.2f, 0.22f, 0.22f), brightness);
                    color.a = 0f; colors[i] = color;
                }
                copy.colors = colors; temporary.Add(copy);
                for (int submesh = 0; submesh < copy.subMeshCount; submesh++)
                    combines.Add(new CombineInstance { mesh = copy, subMeshIndex = submesh, transform = normalize * root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            }
            if (lamp) AddBox(combines, temporary, new Vector3(0f, 6.12f, 1.15f), new Vector3(0.28f, 0.055f, 0.6f), new Color(0.8f, 0.7f, 0.5f, 1f));
            Save(name, combines, temporary, lamp);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void BuildBin()
    {
        var combines = new List<CombineInstance>(); var temporary = new List<Mesh>();
        AddBox(combines, temporary, new Vector3(0f, 0.39f, 0f), new Vector3(0.4f, 0.78f, 0.4f), new Color(0.14f, 0.23f, 0.2f, 0f));
        AddBox(combines, temporary, new Vector3(0f, 0.81f, 0f), new Vector3(0.45f, 0.07f, 0.45f), new Color(0.1f, 0.13f, 0.13f, 0f));
        AddBox(combines, temporary, new Vector3(0f, 0.65f, 0.202f), new Vector3(0.26f, 0.11f, 0.01f), new Color(0.025f, 0.03f, 0.03f, 0f));
        Save("Bin_Green", combines, temporary, false);
    }

    private static void AddBox(List<CombineInstance> combines, List<Mesh> temporary, Vector3 center, Vector3 scale, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(cube);
        Color[] colors = new Color[mesh.vertexCount]; for (int i = 0; i < colors.Length; i++) colors[i] = color;
        mesh.colors = colors; temporary.Add(mesh);
        combines.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(center, Quaternion.identity, scale) });
    }

    private static void Save(string name, List<CombineInstance> combines, List<Mesh> temporary, bool lamp)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(combines.ToArray(), true, true);
        mesh.RecalculateBounds();
        string path = Folder + "Models/StreetProps/" + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) AssetDatabase.CreateAsset(mesh, path);
        else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; AssetDatabase.SaveAssetIfDirty(mesh); }
        var go = new GameObject(name);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (lamp)
            {
                var collider = go.AddComponent<CapsuleCollider>(); collider.radius = 0.12f; collider.height = 6.3f; collider.center = new Vector3(0f, 3.15f, 0f);
                var head = new GameObject("Street light"); head.transform.SetParent(go.transform, false); head.transform.localPosition = new Vector3(0f, 6.02f, 1.15f); head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light light = head.AddComponent<Light>(); light.type = LightType.Spot; light.color = new Color(1f, 0.77f, 0.47f); light.range = 14f; light.spotAngle = 110f; light.innerSpotAngle = 80f; light.intensity = 3.5f; light.shadows = LightShadows.None; light.enabled = false;
            }
            else { var collider = go.AddComponent<BoxCollider>(); collider.center = mesh.bounds.center; collider.size = mesh.bounds.size; }
            PrefabUtility.SaveAsPrefabAsset(go, Folder + "Prefabs/StreetProps/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(go); foreach (Mesh part in temporary) Object.DestroyImmediate(part); }
    }
}
