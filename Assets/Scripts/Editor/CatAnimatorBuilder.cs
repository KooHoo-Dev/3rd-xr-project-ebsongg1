using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 고양이 애니메이터 만들기] 한 번으로 고양이 Animator Controller를 만들고,
/// 컨트롤러가 연결된 고양이 프리팹(Variant)까지 만든다.
///
/// 상태 구성
/// - Locomotion (기본): 블렌드 트리. Speed 0 = Quadruped_Idle, 1 = Quadruped_Walk
/// - 트리거 동작: Alert, Eat, Pet, Hi, Victory, Dig → 한 번 재생 후 Locomotion으로 돌아온다
/// - Sleeping (bool): true면 Sleep, false가 되면 Locomotion으로 돌아온다
///
/// 결과
/// - Assets/Animations/CatAnimator.controller
/// - Assets/Prefabs/Cat.prefab: 원본 고양이 프리팹의 Variant. 에셋 폴더의 원본 프리팹은 건드리지 않는다.
///   원본은 씬의 CatHousePlacer > Cat Prefab 칸에 있는 프리팹, 비어 있으면 Chibi_Cat_00.
/// - 씬에 CatHousePlacer가 있으면 Cat Prefab 칸을 Cat.prefab으로 바꾼다.
///
/// 다시 실행하면 컨트롤러를 새로 만들고 Cat.prefab에 다시 연결한다.
/// 클립이나 전환 시간을 바꾸려면 아래 상수를 고치고 다시 실행하면 된다.
/// </summary>
public static class CatAnimatorBuilder
{
    private const string k_AnimFolder = "Assets/Animations";
    private const string k_ControllerPath = k_AnimFolder + "/CatAnimator.controller";
    private const string k_PrefabFolder = "Assets/Prefabs";
    private const string k_CatPrefabPath = k_PrefabFolder + "/Cat.prefab";

    private const string k_ClipFolder = "Assets/99. External Assets/Assets/Suriyun/Animations/Pspsps Animations/Character Animations";
    private const string k_DefaultCatPath = "Assets/99. External Assets/Assets/Suriyun/Pspsps/Prefab/Characters/Chibi_Cat_00.prefab";

    // 표정 → 얼굴 머티리얼 번호 (M_Chibi_Emo_xx)
    private const string k_EmoFolder = "Assets/99. External Assets/Assets/Suriyun/Pspsps/Materials/Emotions";
    private static readonly (CatExpression expression, int number)[] k_Faces =
    {
        (CatExpression.Default, 3),
        (CatExpression.Happy, 6),
        (CatExpression.Excited, 4),
        (CatExpression.Sad, 21),
        (CatExpression.Annoyed, 10),
        (CatExpression.Sleepy, 19),
        (CatExpression.Sleeping, 5),
        (CatExpression.Surprised, 2),
    };

    // 파라미터 이름 (CatWander 등 스크립트에서 이 이름으로 부른다)
    public const string ParamSpeed = "Speed";
    public const string ParamSleeping = "Sleeping";

    // 트리거 이름 → 재생할 클립 (Anim_Chibi@{클립}.fbx)
    private static readonly (string trigger, string clip)[] k_TriggerActions =
    {
        ("Alert", "Alert"),
        ("Eat", "Quadruped_Eating"),
        ("Pet", "Cute1"),
        ("Hi", "Hi"),
        ("Victory", "Victory"),
        ("Dig", "DigA"),
    };

    private const string k_IdleClip = "Quadruped_Idle";
    private const string k_WalkClip = "Quadruped_Walk";
    private const string k_SleepClip = "Sleep";

    // 전환 시간 (초)
    private const float k_EnterDuration = 0.15f; // 동작으로 들어갈 때
    private const float k_ExitDuration = 0.2f;   // 동작이 끝나고 돌아올 때
    private const float k_ExitTime = 0.9f;       // 동작 클립이 이만큼(비율) 재생되면 돌아오기 시작

    [MenuItem("Tools/Here Kitty/고양이 애니메이터 만들기")]
    public static void Build()
    {
        EnsureFolder(k_AnimFolder);
        EnsureFolder(k_PrefabFolder);

        var controller = BuildController();
        if (controller == null)
            return;

        var catPrefab = BuildCatPrefab(controller);
        if (catPrefab == null)
            return;

        AssignToPlacer(catPrefab);
        AssetDatabase.SaveAssets();

        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);
        Debug.Log($"[CatAnimatorBuilder] 애니메이터를 만들었어요: {k_ControllerPath} → {k_CatPrefabPath}");
    }

    private static AnimatorController BuildController()
    {
        // 클립을 먼저 모두 찾는다. 하나라도 없으면 아무것도 만들지 않는다.
        var idle = LoadClip(k_IdleClip);
        var walk = LoadClip(k_WalkClip);
        var sleep = LoadClip(k_SleepClip);
        if (idle == null || walk == null || sleep == null)
            return null;

        var actionClips = new AnimationClip[k_TriggerActions.Length];
        for (int i = 0; i < k_TriggerActions.Length; i++)
        {
            actionClips[i] = LoadClip(k_TriggerActions[i].clip);
            if (actionClips[i] == null)
                return null;
        }

        // 새로 만든다 (블렌드 트리 같은 하위 에셋이 남지 않도록 기존 파일은 지운다)
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(k_ControllerPath) != null)
            AssetDatabase.DeleteAsset(k_ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(k_ControllerPath);

        controller.AddParameter(ParamSpeed, AnimatorControllerParameterType.Float);
        controller.AddParameter(ParamSleeping, AnimatorControllerParameterType.Bool);
        foreach (var action in k_TriggerActions)
            controller.AddParameter(action.trigger, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        sm.entryPosition = new Vector3(0f, 0f);
        sm.anyStatePosition = new Vector3(0f, 120f);
        sm.exitPosition = new Vector3(800f, 0f);

        // Locomotion: Speed 0 → 대기, 1 → 걷기
        var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = ParamSpeed;
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1f);
        sm.defaultState = locomotion;
        MoveState(sm, locomotion, new Vector3(300f, 0f));

        // 트리거 동작: Any State → 동작 → Locomotion
        for (int i = 0; i < k_TriggerActions.Length; i++)
        {
            var state = sm.AddState(k_TriggerActions[i].trigger, new Vector3(600f, -150f + i * 60f));
            state.motion = actionClips[i];

            var enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, k_TriggerActions[i].trigger);
            enter.canTransitionToSelf = false;
            enter.hasExitTime = false;
            enter.duration = k_EnterDuration;

            var back = state.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = k_ExitTime;
            back.duration = k_ExitDuration;
        }

        // 잠자기: Locomotion ↔ Sleep
        var sleepState = sm.AddState("Sleep", new Vector3(300f, 150f));
        sleepState.motion = sleep;

        var toSleep = locomotion.AddTransition(sleepState);
        toSleep.AddCondition(AnimatorConditionMode.If, 0f, ParamSleeping);
        toSleep.hasExitTime = false;
        toSleep.duration = 0.3f;

        var wake = sleepState.AddTransition(locomotion);
        wake.AddCondition(AnimatorConditionMode.IfNot, 0f, ParamSleeping);
        wake.hasExitTime = false;
        wake.duration = 0.3f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// 원본 고양이 프리팹의 Variant를 만들고 Animator에 컨트롤러를 연결한다.
    /// 이동은 스크립트(CatWander)가 맡으므로 Root Motion은 끈다.
    /// </summary>
    private static GameObject BuildCatPrefab(AnimatorController controller)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(k_CatPrefabPath);
        if (existing != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(k_CatPrefabPath);
            bool ok = SetupAnimator(contents, controller);
            if (ok)
                PrefabUtility.SaveAsPrefabAsset(contents, k_CatPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            return ok ? AssetDatabase.LoadAssetAtPath<GameObject>(k_CatPrefabPath) : null;
        }

        var source = GetSourceCatPrefab();
        if (source == null)
        {
            Debug.LogError($"[CatAnimatorBuilder] 원본 고양이 프리팹을 찾지 못했어요: {k_DefaultCatPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        instance.name = "Cat";
        GameObject saved = null;
        if (SetupAnimator(instance, controller))
            saved = PrefabUtility.SaveAsPrefabAsset(instance, k_CatPrefabPath);
        Object.DestroyImmediate(instance);
        return saved;
    }

    private static bool SetupAnimator(GameObject root, AnimatorController controller)
    {
        var animator = root.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogError("[CatAnimatorBuilder] 고양이 프리팹에 Animator가 없어요.");
            return false;
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // 돌아다니기 스크립트도 붙여 둔다 (Inspector에서 속도 등을 조절할 수 있게)
        if (root.GetComponent<CatWander>() == null)
            root.AddComponent<CatWander>();

        SetupFace(root);

        // 상태값 (배고픔 · 기분 · 졸림)
        if (root.GetComponent<CatNeeds>() == null)
            root.AddComponent<CatNeeds>();

        // 쓰다듬기
        if (root.GetComponent<CatPetting>() == null)
            root.AddComponent<CatPetting>();
        return true;
    }

    /// <summary>CatFace를 붙이고 표정별 얼굴 머티리얼을 채운다.</summary>
    private static void SetupFace(GameObject root)
    {
        var face = root.GetComponent<CatFace>();
        if (face == null)
            face = root.AddComponent<CatFace>();

        var so = new SerializedObject(face);
        var list = so.FindProperty("m_Faces");
        list.arraySize = k_Faces.Length;
        for (int i = 0; i < k_Faces.Length; i++)
        {
            string path = $"{k_EmoFolder}/M_Chibi_Emo_{k_Faces[i].number:D2}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                Debug.LogWarning($"[CatAnimatorBuilder] 얼굴 머티리얼을 찾지 못했어요: {path}");

            var element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("expression").enumValueIndex = (int)k_Faces[i].expression;
            element.FindPropertyRelative("material").objectReferenceValue = material;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject GetSourceCatPrefab()
    {
        var placer = Object.FindFirstObjectByType<CatHousePlacer>();
        if (placer != null)
        {
            var so = new SerializedObject(placer);
            var current = so.FindProperty("m_CatPrefab").objectReferenceValue as GameObject;
            // 이미 Cat.prefab이 연결돼 있으면 그걸 원본으로 쓰면 안 된다 (Variant의 Variant가 됨)
            if (current != null && AssetDatabase.GetAssetPath(current) != k_CatPrefabPath)
                return current;
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(k_DefaultCatPath);
    }

    private static AnimationClip LoadClip(string clipName)
    {
        string path = $"{k_ClipFolder}/Anim_Chibi@{clipName}.fbx";
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && clip.name.StartsWith("__preview__") == false)
                return clip;
        }
        Debug.LogError($"[CatAnimatorBuilder] 애니메이션 클립을 찾지 못했어요: {path}");
        return null;
    }

    private static void MoveState(AnimatorStateMachine sm, AnimatorState state, Vector3 position)
    {
        var states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state == state)
                states[i].position = position;
        }
        sm.states = states;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void AssignToPlacer(GameObject catPrefab)
    {
        var placer = Object.FindFirstObjectByType<CatHousePlacer>();
        if (placer == null)
            return;
        var so = new SerializedObject(placer);
        so.FindProperty("m_CatPrefab").objectReferenceValue = catPrefab;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(placer);
        Debug.Log("[CatAnimatorBuilder] 씬의 CatHousePlacer > Cat Prefab 칸을 Cat.prefab으로 바꿨어요. 씬을 저장하세요.");
    }
}
