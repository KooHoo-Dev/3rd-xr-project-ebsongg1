using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 간식 찾기 만들기] 한 번으로 간식 프리팹을 만들고 씬에 간식 찾기(SnackHunt)를 연결한다.
///
/// - Assets/Prefabs/Snack.prefab: 말린 생선(Dried_fish)을 길이 k_SnackLength로 맞추고
///   kinematic Rigidbody + BoxCollider + XRGrabInteractable + CatSnack을 붙인다.
///   바닥에서 멀리서도 보이도록 위로 피어오르는 금빛 반짝이(Sparkle, 파티클)를 자식으로 둔다.
/// - 반짝이 머티리얼(URP Particles/Unlit, 더하기 블렌드)과 동그란 점 텍스처를 Assets/Prefabs/SnackMaterials에 만든다.
/// - 씬에 "Snack Hunt" 오브젝트(SnackHunt)를 만들거나 찾아서 CatHousePlacer · ARPlaneManager · 간식 프리팹을 연결하고,
///   GameFlow > Snack Hunt 칸에도 넣는다.
/// 다시 실행하면 같은 경로의 프리팹을 덮어쓴다. 크기나 색은 아래 상수를 바꾸고 다시 실행하면 된다.
/// </summary>
public static class SnackHuntBuilder
{
    private const string k_PrefabFolder = "Assets/Prefabs";
    private const string k_MaterialFolder = "Assets/Prefabs/SnackMaterials";
    private const string k_SnackPath = k_PrefabFolder + "/Snack.prefab";
    private const string k_DotTexturePath = k_MaterialFolder + "/SparkleDot.png";
    private const string k_SparkleMaterialPath = k_MaterialFolder + "/Sparkle.mat";
    private const string k_FishPath = "Assets/99. External Assets/Assets/Suriyun/Pspsps/Prefab/Accessory/Dried_fish.prefab";

    // 크기 (미터). 바닥에 놓여도 눈에 띄도록 먹이(0.045)보다 조금 크게
    private const float k_SnackLength = 0.07f;
    private const float k_SnackMass = 0.05f;

    // 반짝이
    private static readonly Color k_SparkleColor = new Color(1f, 0.85f, 0.35f);
    private const float k_SparkleRate = 14f;        // 1초에 나오는 개수
    private const float k_SparkleLifetime = 1.5f;   // 하나가 사는 시간 (초)
    private const float k_SparkleSpeed = 0.12f;     // 위로 올라가는 빠르기 (m/s)
    private const float k_SparkleMinSize = 0.02f;
    private const float k_SparkleMaxSize = 0.05f;
    private const float k_SparkleRadius = 0.04f;    // 나오는 범위 반지름

    [MenuItem("Tools/Here Kitty/간식 찾기 만들기")]
    public static void Build()
    {
        EnsureFolder(k_PrefabFolder);
        EnsureFolder(k_MaterialFolder);

        var sparkleMaterial = BuildSparkleMaterial();
        var snack = BuildSnack(sparkleMaterial);
        if (snack == null)
            return;

        SetUpScene(snack);
        AssetDatabase.SaveAssets();
        Selection.activeObject = snack;
        EditorGUIUtility.PingObject(snack);
        Debug.Log($"[SnackHuntBuilder] 간식 프리팹을 만들었어요: {k_SnackPath}");
    }

    private static GameObject BuildSnack(Material sparkleMaterial)
    {
        var fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_FishPath);
        if (fishPrefab == null)
        {
            Debug.LogError($"[SnackHuntBuilder] 말린 생선 프리팹을 찾지 못했어요: {k_FishPath}");
            return null;
        }

        var root = new GameObject("Snack");
        var fish = (GameObject)PrefabUtility.InstantiatePrefab(fishPrefab);
        fish.transform.SetParent(root.transform, false);
        foreach (var col in fish.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(col);

        // 가장 긴 쪽이 k_SnackLength가 되도록 맞추고, 바닥(아랫면)이 root 원점에 오게 한다. (바닥에 눕혀 놓기 좋게)
        var bounds = GetBounds(fish);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest > 0.0001f)
            fish.transform.localScale *= k_SnackLength / longest;
        bounds = GetBounds(fish);
        fish.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        bounds = GetBounds(fish);

        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;

        // 숨어 있는 동안은 kinematic (CatSnack이 놓은 뒤에 풀어 준다)
        var body = root.AddComponent<Rigidbody>();
        body.mass = k_SnackMass;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var grab = root.AddComponent<XRGrabInteractable>();
        grab.useDynamicAttach = true;

        var sparkle = BuildSparkle(root.transform, bounds.size.y, sparkleMaterial);

        var snack = root.AddComponent<CatSnack>();
        var so = new SerializedObject(snack);
        so.FindProperty("m_Sparkle").objectReferenceValue = sparkle;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_SnackPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>생선 위에서 금빛 점이 천천히 피어올랐다 사라진다.</summary>
    private static GameObject BuildSparkle(Transform parent, float snackHeight, Material material)
    {
        var go = new GameObject("Sparkle");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, snackHeight * 0.5f, 0f);
        // 원뿔 방향(+Z)이 위를 보게
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        // AddComponent 직후 재생 중이면 일부 설정을 바꿀 수 없어서 멈춘다.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = k_SparkleLifetime;
        main.startSpeed = k_SparkleSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(k_SparkleMinSize, k_SparkleMaxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(k_SparkleColor, Color.white);
        main.maxParticles = 60;
        // 생선을 들고 움직여도 이미 나온 점은 그 자리에 남는다.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.rateOverTime = k_SparkleRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = k_SparkleRadius;

        // 나타났다가 사라지게 (투명도 0 → 1 → 0)
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        // 올라가면서 점점 작아진다
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    /// <summary>URP Particles/Unlit, 더하기 블렌드 (어두운 곳 · 밝은 곳 모두 빛나 보이게)</summary>
    private static Material BuildSparkleMaterial()
    {
        var texture = BuildDotTexture();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(k_SparkleMaterialPath);
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, k_SparkleMaterialPath);
        }
        else if (shader != null)
        {
            mat.shader = shader;
        }

        mat.SetTexture("_BaseMap", texture);
        mat.mainTexture = texture;
        mat.SetColor("_BaseColor", Color.white);
        // 반투명 + 더하기 블렌드 (URP 머티리얼 인스펙터에서 Surface Type = Transparent, Blending Mode = Additive로 둔 것과 같다)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>가운데가 밝고 바깥으로 갈수록 흐려지는 동그란 점 (64×64 PNG)</summary>
    private static Texture2D BuildDotTexture()
    {
        if (File.Exists(k_DotTexturePath) == false)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a; // 가운데에 더 모이게
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            File.WriteAllBytes(k_DotTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(k_DotTexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(k_DotTexturePath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(k_DotTexturePath);
    }

    /// <summary>씬에 SnackHunt를 두고 CatHousePlacer · ARPlaneManager · 간식 프리팹 · GameFlow를 연결한다.</summary>
    private static void SetUpScene(GameObject snackPrefab)
    {
        var placer = Object.FindFirstObjectByType<CatHousePlacer>();
        if (placer == null)
        {
            Debug.LogWarning("[SnackHuntBuilder] 씬에 CatHousePlacer가 없어서 간식 찾기를 연결하지 않았어요. 메인 씬을 열고 다시 실행하세요.");
            return;
        }

        var hunt = Object.FindFirstObjectByType<SnackHunt>();
        if (hunt == null)
        {
            var go = new GameObject("Snack Hunt");
            Undo.RegisterCreatedObjectUndo(go, "간식 찾기 만들기");
            hunt = go.AddComponent<SnackHunt>();
        }

        var placerSo = new SerializedObject(placer);
        var planeManager = placerSo.FindProperty("m_PlaneManager").objectReferenceValue as ARPlaneManager;
        if (planeManager == null)
            planeManager = Object.FindFirstObjectByType<ARPlaneManager>();

        var so = new SerializedObject(hunt);
        so.FindProperty("m_Placer").objectReferenceValue = placer;
        so.FindProperty("m_PlaneManager").objectReferenceValue = planeManager;
        so.FindProperty("m_SnackPrefab").objectReferenceValue = snackPrefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(hunt);

        var flow = Object.FindFirstObjectByType<GameFlow>();
        if (flow != null)
        {
            var flowSo = new SerializedObject(flow);
            flowSo.FindProperty("m_SnackHunt").objectReferenceValue = hunt;
            flowSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(flow);
        }

        EditorSceneManager.MarkSceneDirty(hunt.gameObject.scene);
        Debug.Log("[SnackHuntBuilder] 씬에 Snack Hunt를 연결했어요. 씬을 저장하세요.");
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
