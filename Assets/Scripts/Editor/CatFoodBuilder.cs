using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 먹이 · 밥그릇 만들기] 한 번으로 먹이 · 밥그릇 · 먹이 상자 프리팹을 만든다.
///
/// - Assets/Prefabs/Food.prefab: 말린 생선(Dried_fish)을 길이 k_FoodLength로 맞추고
///   Rigidbody + BoxCollider + XRGrabInteractable + CatFood를 붙인다. 컨트롤러 그립으로 잡는다.
/// - Assets/Prefabs/FoodBowl.prefab: Unity 기본 도형으로 만든 작은 밥그릇.
///   XRSocketInteractor + 트리거 구 + FoodBowl.
/// - Assets/Prefabs/FoodBox.prefab: 먹이가 톡 튀어나오는 나무 상자 (윗면이 열려 있음). FoodBox + 상자 충돌체.
/// - 씬에 CatHousePlacer가 있으면 Bowl Prefab · Food Box Prefab 칸에 자동으로 넣는다.
/// 다시 실행하면 같은 경로의 프리팹을 덮어쓴다. 크기나 색은 아래 상수를 바꾸고 다시 실행하면 된다.
/// </summary>
public static class CatFoodBuilder
{
    private const string k_PrefabFolder = "Assets/Prefabs";
    private const string k_MaterialFolder = "Assets/Prefabs/FoodBowlMaterials";
    private const string k_FoodPath = k_PrefabFolder + "/Food.prefab";
    private const string k_BowlPath = k_PrefabFolder + "/FoodBowl.prefab";
    private const string k_BoxPath = k_PrefabFolder + "/FoodBox.prefab";
    private const string k_FishPath = "Assets/99. External Assets/Assets/Suriyun/Pspsps/Prefab/Accessory/Dried_fish.prefab";

    // 크기 (미터). 고양이 크기 0.06(아기) 기준
    private const float k_FoodLength = 0.045f;   // 생선 가장 긴 쪽 길이
    private const float k_FoodMass = 0.05f;      // kg
    private const float k_BowlDiameter = 0.05f;  // 밥그릇 바깥 지름
    private const float k_BowlHeight = 0.016f;   // 밥그릇 높이
    private const float k_SocketRadius = 0.06f;  // 먹이를 이 거리 안으로 가져가면 끌어당긴다
    private static readonly Vector3 k_BoxSize = new Vector3(0.07f, 0.03f, 0.055f); // 먹이 상자 가로 · 높이 · 세로
    private const float k_BoxThick = 0.004f;     // 상자 판 두께

    // 색
    private static readonly Color k_BowlColor = new Color(0.45f, 0.70f, 0.90f);   // 하늘색
    private static readonly Color k_InsideColor = new Color(0.95f, 0.95f, 0.92f); // 그릇 안쪽 (흰색)
    private static readonly Color k_BoxColor = new Color(0.78f, 0.58f, 0.38f);    // 나무
    private static readonly Color k_BoxInsideColor = new Color(0.55f, 0.38f, 0.24f); // 상자 안쪽 (어두운 나무)

    [MenuItem("Tools/Here Kitty/먹이 · 밥그릇 만들기")]
    public static void Build()
    {
        EnsureFolder(k_PrefabFolder);
        EnsureFolder(k_MaterialFolder);

        var food = BuildFood();
        if (food == null)
            return;
        var bowl = BuildBowl();
        var box = BuildBox(food);

        AssignToPlacer(bowl, box);
        AssetDatabase.SaveAssets();
        Selection.activeObject = box;
        EditorGUIUtility.PingObject(box);
        Debug.Log($"[CatFoodBuilder] 먹이 · 밥그릇 · 먹이 상자 프리팹을 만들었어요: {k_FoodPath}, {k_BowlPath}, {k_BoxPath}");
    }

    private static GameObject BuildFood()
    {
        var fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_FishPath);
        if (fishPrefab == null)
        {
            Debug.LogError($"[CatFoodBuilder] 말린 생선 프리팹을 찾지 못했어요: {k_FishPath}");
            return null;
        }

        var root = new GameObject("Food");
        var fish = (GameObject)PrefabUtility.InstantiatePrefab(fishPrefab);
        fish.transform.SetParent(root.transform, false);
        foreach (var col in fish.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(col);

        // 가장 긴 쪽이 k_FoodLength가 되도록 크기를 맞추고, 가운데를 root 원점에 맞춘다.
        var bounds = GetBounds(fish);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest > 0.0001f)
            fish.transform.localScale *= k_FoodLength / longest;
        bounds = GetBounds(fish);
        fish.transform.localPosition -= bounds.center;
        bounds = GetBounds(fish);

        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;

        var body = root.AddComponent<Rigidbody>();
        body.mass = k_FoodMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        // 얇은 평면 충돌체를 뚫고 떨어지지 않게
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.useDynamicAttach = true; // 잡은 자리 그대로 잡힌다 (생선이 손 가운데로 튀지 않게)

        root.AddComponent<CatFood>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_FoodPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject BuildBowl()
    {
        var bowlMat = MakeMaterial("FoodBowl_Outside", k_BowlColor);
        var insideMat = MakeMaterial("FoodBowl_Inside", k_InsideColor);

        var root = new GameObject("FoodBowl");

        // 그릇 몸통 (책상에 닿는 면이 y = 0)
        AddCylinder(root, "Body", new Vector3(0f, k_BowlHeight * 0.5f, 0f),
            new Vector3(k_BowlDiameter, k_BowlHeight, k_BowlDiameter), bowlMat);
        // 위에서 보면 안쪽이 하얗게 보이도록 얇은 원판을 살짝 띄워 둔다
        AddCylinder(root, "Inside", new Vector3(0f, k_BowlHeight + 0.0005f, 0f),
            new Vector3(k_BowlDiameter * 0.8f, 0.001f, k_BowlDiameter * 0.8f), insideMat);

        var slot = new GameObject("FoodSlot").transform;
        slot.SetParent(root.transform, false);
        slot.localPosition = new Vector3(0f, k_BowlHeight + 0.006f, 0f);

        // 소켓 판정용 트리거 (먹이를 이 안으로 가져가면 끌어당긴다)
        var trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = k_SocketRadius;
        trigger.center = new Vector3(0f, k_BowlHeight + 0.02f, 0f);

        var socket = root.AddComponent<XRSocketInteractor>();
        socket.attachTransform = slot;

        var bowl = root.AddComponent<FoodBowl>();
        var so = new SerializedObject(bowl);
        so.FindProperty("m_FoodSlot").objectReferenceValue = slot;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_BowlPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>윗면이 열린 나무 상자. 바닥 · 벽 충돌체를 남겨서 먹이가 상자 안에 놓인다.</summary>
    private static GameObject BuildBox(GameObject foodPrefab)
    {
        var wood = MakeMaterial("FoodBox_Wood", k_BoxColor);
        var inside = MakeMaterial("FoodBox_Inside", k_BoxInsideColor);

        var root = new GameObject("FoodBox");
        float w = k_BoxSize.x, h = k_BoxSize.y, d = k_BoxSize.z, t = k_BoxThick;

        AddCube(root, "Bottom", new Vector3(0f, t * 0.5f, 0f), new Vector3(w, t, d), inside);
        AddCube(root, "Wall_Front", new Vector3(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t), wood);
        AddCube(root, "Wall_Back", new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), wood);
        AddCube(root, "Wall_Left", new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - t * 2f), wood);
        AddCube(root, "Wall_Right", new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d - t * 2f), wood);

        // 먹이가 나와서 내려앉는 자리 (상자 바닥 바로 위)
        var spawn = new GameObject("SpawnPoint").transform;
        spawn.SetParent(root.transform, false);
        spawn.localPosition = new Vector3(0f, t + 0.01f, 0f);

        var box = root.AddComponent<FoodBox>();
        var so = new SerializedObject(box);
        so.FindProperty("m_FoodPrefab").objectReferenceValue = foodPrefab;
        so.FindProperty("m_SpawnPoint").objectReferenceValue = spawn;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_BoxPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>충돌체를 남겨 두는 큐브 (먹이 상자용)</summary>
    private static GameObject AddCube(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static GameObject AddCylinder(GameObject parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        // 원기둥 기본 높이는 2라서 y를 절반으로 맞춘다
        go.transform.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static Material MakeMaterial(string name, Color color)
    {
        string path = $"{k_MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.SetFloat("_Smoothness", 0.4f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void AssignToPlacer(GameObject bowlPrefab, GameObject boxPrefab)
    {
        var placer = Object.FindFirstObjectByType<CatHousePlacer>();
        if (placer == null)
            return;
        var so = new SerializedObject(placer);
        so.FindProperty("m_BowlPrefab").objectReferenceValue = bowlPrefab;
        so.FindProperty("m_FoodBoxPrefab").objectReferenceValue = boxPrefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(placer);
        Debug.Log("[CatFoodBuilder] 씬의 CatHousePlacer > Bowl Prefab · Food Box Prefab 칸에 연결했어요. 씬을 저장하세요.");
    }
}
