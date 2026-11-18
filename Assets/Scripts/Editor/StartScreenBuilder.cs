using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 시작 · 결과 화면 만들기] 한 번으로 지금 열린 씬에 다음을 만든다.
///
/// - Start Panel: 앱 이름 · 한 줄 소개 · 사용법 4줄 · 시작 버튼 · 만든 사람 (World Space Canvas)
/// - Placement Hint: 시작 후 "책상을 가리키고 트리거..." 안내 한 줄 (World Space Canvas)
/// - Result Panel: 엔딩 후 돌본 시간 · 먹인 횟수 · 쓰다듬은 횟수 + "처음부터 다시" 버튼 (World Space Canvas)
/// - EventSystem + XR UI Input Module: 컨트롤러 레이로 버튼을 누르기 위해 필요
/// - GameFlow: 시작 버튼 → StartGame(), 다시 버튼 → Restart() 연결, CatHousePlacer · 결과 숫자 칸 연결
/// - CatHousePlacer는 꺼 둔다 (시작 버튼을 누르면 GameFlow가 켠다)
///
/// 다시 실행하면 같은 이름의 오브젝트를 지우고 새로 만든다. 문구 · 색 · 크기는 아래 상수를 고치고 다시 실행하면 된다.
/// 캔버스 단위: 1픽셀 = 1mm (스케일 0.001)
/// </summary>
public static class StartScreenBuilder
{
    private const string k_FontPath = "Assets/99. External Assets/Font/Noto_Sans_KR/NotoSansKR-Regular SDF.asset";
    private const string k_FlowName = "GameFlow";
    private const string k_PanelName = "Start Panel";
    private const string k_HintName = "Placement Hint";
    private const string k_ResultName = "Result Panel";
    private const string k_EventSystemName = "EventSystem";

    // 문구
    private const string k_Title = "Here, Kitty!";
    private const string k_Subtitle = "책상 위에서 아기 고양이를 키워 보세요";
    private static readonly string[] k_HowTo =
    {
        "책상을 가리키고 <b>트리거</b> → 고양이 집 놓기",
        "상자에서 생선을 <b>그립</b>으로 집어 밥그릇에",
        "고양이 머리를 컨트롤러로 문질러 쓰다듬기",
        "잘 돌보면 아기 → 어린이 → 어른으로 자라요",
    };
    private const string k_StartLabel = "시작하기";
    private const string k_Credit = "만든 사람: 송은빈";
    private const string k_Hint = "책상을 가리키고 <b>트리거</b>를 눌러 고양이 집을 놓으세요";

    private const string k_ResultTitle = "고양이가 여행을 떠났어요!";
    private const string k_ResultSubtitle = "잘 돌봐 줘서 고마워요";
    private static readonly string[] k_ResultLabels = { "돌본 시간", "먹인 횟수", "쓰다듬은 횟수" };
    private const string k_RestartLabel = "처음부터 다시";
    private const float k_ResultHeight = 450f;

    // 크기 (mm)
    private const float k_PanelWidth = 560f;
    private const float k_PanelHeight = 540f;
    private const float k_Padding = 44f;

    // 색 (고양이 집과 맞춤: 크림 · 코랄)
    private static readonly Color k_PanelColor = new Color(1.00f, 0.96f, 0.90f, 0.96f);
    private static readonly Color k_AccentColor = new Color(0.93f, 0.48f, 0.42f);
    private static readonly Color k_TextColor = new Color(0.25f, 0.20f, 0.18f);
    private static readonly Color k_SubTextColor = new Color(0.45f, 0.40f, 0.37f);
    private static readonly Color k_LineColor = new Color(0.88f, 0.80f, 0.72f);
    private static readonly Color k_HintColor = new Color(0.15f, 0.13f, 0.12f, 0.85f);

    [MenuItem("Tools/Here Kitty/시작 · 결과 화면 만들기")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontPath);
        if (font == null)
        {
            Debug.LogError($"[StartScreenBuilder] 한글 글꼴을 찾지 못했어요: {k_FontPath}");
            return;
        }

        DestroyExisting(k_FlowName);
        DestroyExisting(k_PanelName);
        DestroyExisting(k_HintName);
        DestroyExisting(k_ResultName);

        EnsureEventSystem();
        var panel = BuildStartPanel(font, out var startButton);
        var hint = BuildHint(font);
        var result = BuildResultPanel(font, out var restartButton, out var values);

        var flowGo = new GameObject(k_FlowName);
        Undo.RegisterCreatedObjectUndo(flowGo, "Build Start Screen");
        var flow = flowGo.AddComponent<GameFlow>();

        var placer = Object.FindFirstObjectByType<CatHousePlacer>(FindObjectsInactive.Include);
        var so = new SerializedObject(flow);
        so.FindProperty("m_Placer").objectReferenceValue = placer;
        so.FindProperty("m_StartPanel").objectReferenceValue = panel;
        so.FindProperty("m_PlacementHint").objectReferenceValue = hint;
        so.FindProperty("m_ResultPanel").objectReferenceValue = result;
        so.FindProperty("m_ResultTimeText").objectReferenceValue = values[0];
        so.FindProperty("m_ResultFeedText").objectReferenceValue = values[1];
        so.FindProperty("m_ResultPetText").objectReferenceValue = values[2];
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(startButton.onClick, flow.StartGame);
        UnityEventTools.AddPersistentListener(restartButton.onClick, flow.Restart);
        // 결과 패널은 엔딩 뒤에만 보인다.
        result.gameObject.SetActive(false);

        if (placer != null)
        {
            Undo.RecordObject(placer, "Disable CatHousePlacer");
            placer.enabled = false;
            EditorUtility.SetDirty(placer);
        }
        else
        {
            Debug.LogWarning("[StartScreenBuilder] 씬에서 CatHousePlacer를 찾지 못했어요. GameFlow의 Placer 칸을 직접 연결하세요.");
        }

        EditorSceneManager.MarkSceneDirty(flowGo.scene);
        Selection.activeObject = panel.gameObject;
        Debug.Log("[StartScreenBuilder] 시작 · 결과 화면을 만들었어요. 씬을 저장하세요.");
    }

    private static Transform BuildStartPanel(TMP_FontAsset font, out Button startButton)
    {
        var canvas = CreateWorldCanvas(k_PanelName, new Vector2(k_PanelWidth, k_PanelHeight));
        // 플레이 전 Scene 뷰에서 보기 좋게 대략 눈높이 앞에 둔다. 실행하면 GameFlow가 플레이어 앞으로 옮긴다.
        canvas.transform.position = new Vector3(0f, 1.4f, 0.8f);

        var bg = CreateImage(canvas.transform, "Background", k_PanelColor, RoundedSprite());
        Stretch(bg.rectTransform);

        float y = k_Padding - 8f;
        var title = CreateText(canvas.transform, "Title", k_Title, font, 64f, k_AccentColor, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        PlaceTop(title.rectTransform, y, k_PanelWidth - k_Padding * 2f, 80f);
        y += 84f;

        var subtitle = CreateText(canvas.transform, "Subtitle", k_Subtitle, font, 24f, k_SubTextColor, TextAlignmentOptions.Center);
        PlaceTop(subtitle.rectTransform, y, k_PanelWidth - k_Padding * 2f, 36f);
        y += 52f;

        PlaceTop(CreateImage(canvas.transform, "Line_Top", k_LineColor, null).rectTransform, y, k_PanelWidth - k_Padding * 2f, 2f);
        y += 20f;

        for (int i = 0; i < k_HowTo.Length; i++)
        {
            var dot = CreateImage(canvas.transform, $"HowTo_Dot_{i + 1}", k_AccentColor, KnobSprite());
            PlaceTop(dot.rectTransform, y + 14f, 12f, 12f, -k_PanelWidth * 0.5f + k_Padding + 6f);

            var line = CreateText(canvas.transform, $"HowTo_{i + 1}", k_HowTo[i], font, 22f, k_TextColor, TextAlignmentOptions.Left);
            float lineWidth = k_PanelWidth - k_Padding * 2f - 24f;
            PlaceTop(line.rectTransform, y, lineWidth, 40f, -k_PanelWidth * 0.5f + k_Padding + 24f + lineWidth * 0.5f);
            y += 42f;
        }
        y += 14f;

        PlaceTop(CreateImage(canvas.transform, "Line_Bottom", k_LineColor, null).rectTransform, y, k_PanelWidth - k_Padding * 2f, 2f);
        y += 26f;

        startButton = CreateButton(canvas.transform, "Start Button", font, k_StartLabel);
        PlaceTop((RectTransform)startButton.transform, y, 240f, 64f);
        y += 84f;

        var credit = CreateText(canvas.transform, "Credit", k_Credit, font, 20f, k_SubTextColor, TextAlignmentOptions.Center);
        PlaceTop(credit.rectTransform, y, k_PanelWidth - k_Padding * 2f, 30f);

        return canvas.transform;
    }

    private static Transform BuildHint(TMP_FontAsset font)
    {
        var canvas = CreateWorldCanvas(k_HintName, new Vector2(600f, 56f));
        canvas.transform.position = new Vector3(0f, 1.1f, 0.7f);
        // 안내는 누를 것이 없으니 레이를 막지 않게 레이캐스터를 뺀다.
        Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());

        var bg = CreateImage(canvas.transform, "Background", k_HintColor, RoundedSprite());
        bg.raycastTarget = false;
        Stretch(bg.rectTransform);

        var text = CreateText(canvas.transform, "Text", k_Hint, font, 22f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.margin = new Vector4(16f, 0f, 16f, 0f);
        return canvas.transform;
    }

    private static Canvas CreateWorldCanvas(string canvasName, Vector2 sizeMm)
    {
        var go = new GameObject(canvasName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Build Start Screen");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<TrackedDeviceGraphicRaycaster>();

        var rect = (RectTransform)go.transform;
        rect.sizeDelta = sizeMm;
        rect.localScale = Vector3.one * 0.001f;
        return canvas;
    }

    private static Image CreateImage(Transform parent, string imageName, Color color, Sprite sprite)
    {
        var go = new GameObject(imageName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string textName, string content, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(textName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        // 문구가 칸보다 길면 글자를 조금 줄여서 넘치지 않게
        text.enableAutoSizing = true;
        text.fontSizeMax = size;
        text.fontSizeMin = size * 0.7f;
        return text;
    }

    /// <summary>
    /// 결과 패널: 제목 · 한 줄 · 숫자 3줄(왼쪽 이름, 오른쪽 값) · 처음부터 다시 버튼.
    /// values에 값 텍스트 3개(돌본 시간 · 먹인 횟수 · 쓰다듬은 횟수)를 돌려준다.
    /// </summary>
    private static Transform BuildResultPanel(TMP_FontAsset font, out Button restartButton, out TextMeshProUGUI[] values)
    {
        var canvas = CreateWorldCanvas(k_ResultName, new Vector2(k_PanelWidth, k_ResultHeight));
        canvas.transform.position = new Vector3(0.7f, 1.4f, 0.8f);

        var bg = CreateImage(canvas.transform, "Background", k_PanelColor, RoundedSprite());
        Stretch(bg.rectTransform);

        float inner = k_PanelWidth - k_Padding * 2f;
        float y = k_Padding - 8f;
        var title = CreateText(canvas.transform, "Title", k_ResultTitle, font, 40f, k_AccentColor, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        PlaceTop(title.rectTransform, y, inner, 60f);
        y += 66f;

        var subtitle = CreateText(canvas.transform, "Subtitle", k_ResultSubtitle, font, 24f, k_SubTextColor, TextAlignmentOptions.Center);
        PlaceTop(subtitle.rectTransform, y, inner, 36f);
        y += 52f;

        PlaceTop(CreateImage(canvas.transform, "Line_Top", k_LineColor, null).rectTransform, y, inner, 2f);
        y += 20f;

        values = new TextMeshProUGUI[k_ResultLabels.Length];
        float half = inner * 0.5f;
        for (int i = 0; i < k_ResultLabels.Length; i++)
        {
            var label = CreateText(canvas.transform, $"Label_{i + 1}", k_ResultLabels[i], font, 24f, k_TextColor, TextAlignmentOptions.Left);
            PlaceTop(label.rectTransform, y, half, 44f, -half * 0.5f);

            values[i] = CreateText(canvas.transform, $"Value_{i + 1}", "-", font, 28f, k_AccentColor, TextAlignmentOptions.Right);
            values[i].fontStyle = FontStyles.Bold;
            PlaceTop(values[i].rectTransform, y, half, 44f, half * 0.5f);
            y += 46f;
        }
        y += 14f;

        PlaceTop(CreateImage(canvas.transform, "Line_Bottom", k_LineColor, null).rectTransform, y, inner, 2f);
        y += 26f;

        restartButton = CreateButton(canvas.transform, "Restart Button", font, k_RestartLabel);
        PlaceTop((RectTransform)restartButton.transform, y, 260f, 64f);

        return canvas.transform;
    }

    private static Button CreateButton(Transform parent, string buttonName, TMP_FontAsset font, string label)
    {
        var image = CreateImage(parent, buttonName, k_AccentColor, RoundedSprite());
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.88f, 0.85f);
        colors.pressedColor = new Color(0.85f, 0.70f, 0.66f);
        button.colors = colors;

        var text = CreateText(image.transform, "Label", label, font, 30f, Color.white, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform);
        return button;
    }

    /// <summary>패널 위쪽에서 top만큼 내려온 자리에 놓는다. x는 패널 가운데 기준.</summary>
    private static void PlaceTop(RectTransform rect, float top, float width, float height, float x = 0f)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -top);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Sprite RoundedSprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    private static Sprite KnobSprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    /// <summary>EventSystem이 없으면 만들고, 입력 모듈을 XR UI Input Module로 맞춘다.</summary>
    private static void EnsureEventSystem()
    {
        var eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (eventSystem == null)
        {
            var go = new GameObject(k_EventSystemName);
            Undo.RegisterCreatedObjectUndo(go, "Build Start Screen");
            eventSystem = go.AddComponent<EventSystem>();
        }

        // 다른 입력 모듈이 있으면 XR UI Input Module과 부딪히므로 지운다.
        foreach (var module in eventSystem.GetComponents<BaseInputModule>())
        {
            if ((module is XRUIInputModule) == false)
                Undo.DestroyObjectImmediate(module);
        }
        if (eventSystem.GetComponent<XRUIInputModule>() == null)
            Undo.AddComponent<XRUIInputModule>(eventSystem.gameObject);
    }

    /// <summary>
    /// 지금 열린 씬의 최상위 오브젝트 중 이름이 같은 것을 지운다.
    /// (씬 전체 목록을 먼저 뽑아 두면, 부모를 지운 뒤 이미 지워진 자식을 읽다가 에러가 나서 최상위만 본다)
    /// </summary>
    private static void DestroyExisting(string objectName)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go != null && go.name == objectName)
                Undo.DestroyObjectImmediate(go);
        }
    }
}
