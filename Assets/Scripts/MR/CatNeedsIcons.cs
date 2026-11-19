using TMPro;
using UnityEngine;

/// <summary>
/// 고양이 머리 위에 성장 게이지와 상태값 아이콘 3개를 띄운다.
///
/// 맨 위: 성장 단계 이름 (아기 · 어린이 · 어른) + 성장 막대 (CatGrowth가 있을 때만)
///   - 자라는 중이면 금색, 배고프거나 기분이 나빠 멈췄으면 회색
///   - 단계가 오르기 직전(게이지 가득)에는 깜빡인다
/// 그 아래: 상태값 아이콘 3개
///
/// - 생선: 배부름 (1 - 배고픔)
/// - 하트: 기분
/// - 달: 기운 (1 - 졸림)
/// 아이콘 아래 막대는 셋 다 "꽉 찰수록 좋음"으로 맞췄다. 초록 → 노랑 → 빨강.
/// 돌봐야 하는 상태(배고픔 · 기분 나쁨 · 졸림)면 그 아이콘이 통통 튀어서 뭘 해 줘야 하는지 알려 준다.
///
/// 아이콘 그림은 따로 이미지 파일 없이 코드로 그린다. (DrawFish · DrawHeart · DrawMoon)
/// 아이콘 묶음은 고양이 자식이 아니라 따로 만들어서 머리 위를 따라다닌다.
/// (고양이 크기가 0.06이라 자식으로 두면 크기 계산이 헷갈려서) 항상 카메라를 바라본다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CatNeeds))]
public class CatNeedsIcons : MonoBehaviour
{
    [Header("위치 · 크기 (m)")]
    [SerializeField, Tooltip("머리 뼈에서 이만큼 위에 띄운다. 고양이 크기 0.06 기준, 커지면 같이 올라간다")]
    private float m_HeightAboveHead = 0.075f;

    [SerializeField, Tooltip("아이콘 한 개 크기")]
    private float m_IconSize = 0.016f;

    [SerializeField, Tooltip("아이콘 사이 간격 (가운데 → 가운데)")]
    private float m_Spacing = 0.022f;

    [SerializeField, Tooltip("위 높이 값의 기준이 되는 고양이 크기")]
    private float m_ReferenceScale = 0.06f;

    [Header("표시")]
    [SerializeField, Tooltip("자는 동안은 달 아이콘만 보여 준다")]
    private bool m_OnlyMoonWhileSleeping = true;

    [SerializeField, Tooltip("돌봐야 할 때 아이콘이 튀는 높이 (아이콘 크기 대비)")]
    private float m_BounceHeight = 0.6f;

    [SerializeField, Tooltip("돌봐야 할 때 아이콘이 커지는 정도 (0.3이면 최대 1.3배)")]
    private float m_BounceGrow = 0.3f;

    [SerializeField] private float m_BounceSpeed = 8f;

    [Header("성장 게이지")]
    [SerializeField, Tooltip("단계 이름 글꼴. 한글이 나오는 NotoSansKR-Regular SDF")]
    private TMP_FontAsset m_Font;

    [SerializeField, Tooltip("단계 이름 글자 크기 (월드 기준). 글자가 너무 크거나 작으면 이 값을 조절")]
    private float m_LabelFontSize = 0.1f;

    [SerializeField, Tooltip("성장 막대 높이 (m)")]
    private float m_GrowthBarHeight = 0.004f;

    private static readonly Color s_FishColor = new Color(1.00f, 0.60f, 0.25f);
    private static readonly Color s_HeartColor = new Color(0.95f, 0.35f, 0.47f);
    private static readonly Color s_MoonColor = new Color(0.50f, 0.55f, 0.95f);
    private static readonly Color s_Good = new Color(0.35f, 0.80f, 0.45f);
    private static readonly Color s_Okay = new Color(0.98f, 0.78f, 0.25f);
    private static readonly Color s_Bad = new Color(0.95f, 0.30f, 0.25f);
    private static readonly Color s_Growing = new Color(1.00f, 0.80f, 0.25f);
    private static readonly Color s_Stopped = new Color(0.60f, 0.60f, 0.60f);
    private static readonly string[] s_StageNames = { "아기", "어린이", "어른" };

    private static Sprite s_Disc, s_Fish, s_Heart, s_Moon, s_BarCenter, s_BarLeft;

    private class Icon
    {
        public Transform root;      // 튀는 동작은 이걸 움직인다
        public Transform fill;      // 막대 채우기 (x 크기로 값 표시)
        public SpriteRenderer fillRenderer;
        public float baseX;
    }

    private CatNeeds m_Needs;
    private Transform m_Head;
    private Transform m_Holder;
    private Icon m_FishIcon, m_HeartIcon, m_MoonIcon;
    private CatGrowth m_Growth;
    private Transform m_GrowthFill;
    private SpriteRenderer m_GrowthFillRenderer;
    private TextMeshPro m_StageLabel;
    private float m_GrowthBarWidth;

    private void Awake()
    {
        m_Needs = GetComponent<CatNeeds>();
        m_Growth = GetComponent<CatGrowth>();
        m_Head = FindChild(transform, "head");
        if (m_Head == null)
            m_Head = transform;
        CreateSprites();
        Build();
    }

    private void OnEnable()
    {
        if (m_Holder != null)
            m_Holder.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (m_Holder != null)
            m_Holder.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (m_Holder != null)
            Destroy(m_Holder.gameObject);
    }

    private void LateUpdate()
    {
        float scale = m_ReferenceScale > 0f ? transform.lossyScale.x / m_ReferenceScale : 1f;
        m_Holder.position = m_Head.position + Vector3.up * (m_HeightAboveHead * scale);

        var cam = Camera.main;
        if (cam != null)
        {
            var away = m_Holder.position - cam.transform.position;
            away.y = 0f; // 위아래로는 기울이지 않아서 글자판처럼 똑바로 서 있게
            if (away.sqrMagnitude > 0.0001f)
                m_Holder.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        bool sleeping = m_Needs.IsSleeping && m_OnlyMoonWhileSleeping;
        UpdateIcon(m_FishIcon, 1f - m_Needs.Hunger, m_Needs.IsHungry, sleeping == false, 0f);
        UpdateIcon(m_HeartIcon, m_Needs.Mood, m_Needs.IsSad, sleeping == false, 0.33f);
        UpdateIcon(m_MoonIcon, 1f - m_Needs.Sleepiness, m_Needs.IsSleepy && m_Needs.IsSleeping == false, true, 0.66f);

        // 자는 동안 달 아이콘을 가운데로
        m_MoonIcon.root.localPosition = new Vector3(sleeping ? 0f : m_MoonIcon.baseX, m_MoonIcon.root.localPosition.y, 0f);

        UpdateGrowth();
    }

    private void UpdateGrowth()
    {
        if (m_Growth == null || m_GrowthFill == null)
            return;

        float progress = m_Growth.IsStageUpPending ? 1f : m_Growth.Progress;
        m_GrowthFill.localScale = new Vector3(m_GrowthBarWidth * progress, m_GrowthFill.localScale.y, 1f);

        Color color = m_Growth.IsGrowing || m_Growth.IsComplete ? s_Growing : s_Stopped;
        if (m_Growth.IsStageUpPending)
        {
            // 단계가 오르기 직전: 반짝반짝
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
            color = Color.Lerp(s_Growing, Color.white, blink);
        }
        m_GrowthFillRenderer.color = color;

        if (m_StageLabel != null)
        {
            string label = s_StageNames[Mathf.Clamp((int)m_Growth.Stage, 0, s_StageNames.Length - 1)];
            if (m_Growth.IsComplete)
                label += " · 다 자람";
            if (m_StageLabel.text != label)
                m_StageLabel.text = label;
        }
    }

    private void UpdateIcon(Icon icon, float value, bool needsCare, bool visible, float phase)
    {
        if (icon.root.gameObject.activeSelf != visible)
            icon.root.gameObject.SetActive(visible);
        if (visible == false)
            return;

        value = Mathf.Clamp01(value);
        icon.fill.localScale = new Vector3(m_IconSize * value, icon.fill.localScale.y, 1f);
        icon.fillRenderer.color = value > 0.6f ? s_Good : (value > 0.3f ? s_Okay : s_Bad);

        // 돌봐야 하면 통통 튄다 (아이콘마다 박자를 조금씩 어긋나게)
        float bounce = needsCare ? Mathf.Abs(Mathf.Sin((Time.time + phase) * m_BounceSpeed * 0.5f)) : 0f;
        icon.root.localPosition = new Vector3(icon.baseX, bounce * m_BounceHeight * m_IconSize, 0f);
        icon.root.localScale = Vector3.one * (1f + bounce * m_BounceGrow);
    }

    private void Build()
    {
        m_Holder = new GameObject($"{name} Needs Icons").transform;
        m_FishIcon = MakeIcon("Fullness", s_Fish, s_FishColor, -m_Spacing);
        m_HeartIcon = MakeIcon("Mood", s_Heart, s_HeartColor, 0f);
        m_MoonIcon = MakeIcon("Energy", s_Moon, s_MoonColor, m_Spacing);

        if (m_Growth != null)
            BuildGrowthRow();
    }

    /// <summary>아이콘 줄 위에: 단계 이름 + 아이콘 줄 전체 너비의 성장 막대</summary>
    private void BuildGrowthRow()
    {
        var row = new GameObject("Growth").transform;
        row.SetParent(m_Holder, false);

        m_GrowthBarWidth = m_Spacing * 2f + m_IconSize;
        // 아이콘이 통통 튀어도(최대 약 1.1배 높이) 막대에 닿지 않게 띄운다.
        float barY = m_IconSize * 1.6f;
        AddSprite(row, "Bar", s_BarCenter, new Color(0f, 0f, 0f, 0.35f),
            new Vector3(0f, barY, 0f), new Vector2(m_GrowthBarWidth, m_GrowthBarHeight), 0);
        m_GrowthFillRenderer = AddSprite(row, "Fill", s_BarLeft, s_Growing,
            new Vector3(-m_GrowthBarWidth * 0.5f, barY, -0.0005f), new Vector2(m_GrowthBarWidth, m_GrowthBarHeight), 1);
        m_GrowthFill = m_GrowthFillRenderer.transform;

        var labelGo = new GameObject("StageLabel");
        labelGo.transform.SetParent(row, false);
        m_StageLabel = labelGo.AddComponent<TextMeshPro>();
        var font = m_Font != null ? m_Font : TMP_Settings.defaultFontAsset;
        if (font != null)
            m_StageLabel.font = font;
        m_StageLabel.fontSize = m_LabelFontSize;
        m_StageLabel.alignment = TextAlignmentOptions.Center;
        m_StageLabel.textWrappingMode = TextWrappingModes.NoWrap;
        m_StageLabel.color = Color.white;
        m_StageLabel.outlineWidth = 0.25f;
        m_StageLabel.outlineColor = new Color32(40, 40, 40, 255);
        m_StageLabel.rectTransform.sizeDelta = new Vector2(m_GrowthBarWidth, m_IconSize * 0.8f);
        m_StageLabel.transform.localPosition = new Vector3(0f, barY + m_GrowthBarHeight * 0.5f + m_IconSize * 0.45f, -0.0005f);
        m_StageLabel.text = s_StageNames[0];
    }

    /// <summary>
    /// 아이콘 하나: 흰 원 배경 + 그림 + 아래 막대(배경 · 채우기).
    /// 묶음의 앞(-z)이 카메라 쪽이라, 앞에 그릴 것일수록 z를 조금씩 앞으로 당긴다.
    /// </summary>
    private Icon MakeIcon(string iconName, Sprite picture, Color color, float x)
    {
        var root = new GameObject(iconName).transform;
        root.SetParent(m_Holder, false);
        root.localPosition = new Vector3(x, 0f, 0f);

        AddSprite(root, "Background", s_Disc, new Color(1f, 1f, 1f, 0.85f), Vector3.zero, Vector2.one * m_IconSize, 0);
        AddSprite(root, "Picture", picture, color, new Vector3(0f, 0f, -0.0005f), Vector2.one * m_IconSize * 0.8f, 1);

        float barY = -m_IconSize * 0.72f;
        float barH = m_IconSize * 0.18f;
        AddSprite(root, "Bar", s_BarCenter, new Color(0f, 0f, 0f, 0.35f), new Vector3(0f, barY, 0f), new Vector2(m_IconSize, barH), 0);
        var fill = AddSprite(root, "Fill", s_BarLeft, s_Good, new Vector3(-m_IconSize * 0.5f, barY, -0.0005f), new Vector2(m_IconSize, barH), 1);

        return new Icon { root = root, fill = fill.transform, fillRenderer = fill, baseX = x };
    }

    private static SpriteRenderer AddSprite(Transform parent, string spriteName, Sprite sprite, Color color, Vector3 localPos, Vector2 size, int order)
    {
        var go = new GameObject(spriteName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        r.sortingOrder = order;
        return r;
    }

    // ───────────── 아이콘 그림 (코드로 그리기) ─────────────
    // 모든 그림은 흰색 + 투명도로 그리고, 색은 SpriteRenderer.color로 입힌다.
    // 좌표 p는 그림 가운데가 (0, 0), 가로 · 세로 -0.5 ~ 0.5. 각 픽셀을 4×4로 나눠 찍어서 가장자리를 부드럽게 한다.

    private const int k_TextureSize = 64;

    internal delegate bool Shape(Vector2 p);

    private static void CreateSprites()
    {
        if (s_Disc != null)
            return;
        s_Disc = MakeSprite("Disc", p => p.magnitude < 0.48f);
        s_Fish = MakeSprite("Fish", DrawFish);
        s_Heart = MakeSprite("Heart", DrawHeart);
        s_Moon = MakeSprite("Moon", DrawMoon);

        var white = Texture2D.whiteTexture;
        var rect = new Rect(0f, 0f, white.width, white.height);
        s_BarCenter = Sprite.Create(white, rect, new Vector2(0.5f, 0.5f), white.width);
        s_BarLeft = Sprite.Create(white, rect, new Vector2(0f, 0.5f), white.width);
    }

    private static bool DrawFish(Vector2 p)
    {
        // 몸통: 타원
        var body = new Vector2((p.x + 0.08f) / 0.3f, p.y / 0.17f);
        bool inBody = body.sqrMagnitude < 1f;
        // 꼬리: 몸통 오른쪽에 벌어진 삼각형
        bool inTail = p.x > 0.17f && p.x < 0.42f && Mathf.Abs(p.y) < (p.x - 0.17f) * 0.9f;
        // 눈: 구멍
        bool inEye = (p - new Vector2(-0.25f, 0.04f)).magnitude < 0.035f;
        return (inBody || inTail) && inEye == false;
    }

    /// <summary>
    /// 하트 곡선 (x² + y² - 1)³ - x²y³ ≤ 0 하나로 그려서 이음매 없이 둥글게 이어진다.
    /// 곡선은 가로 ±1.14, 세로 -1 ~ 1.2 범위라 가운데(0.1)를 맞추고 그림의 약 72% 크기로 줄인다.
    /// 쓰다듬기 하트(CatEffects)도 같은 모양을 쓴다.
    /// </summary>
    internal static bool DrawHeart(Vector2 p)
    {
        const float k_Scale = 0.32f;
        float x = p.x / k_Scale;
        float y = p.y / k_Scale + 0.1f;
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y <= 0f;
    }

    private static bool DrawMoon(Vector2 p)
    {
        // 큰 원에서 오른쪽 위로 비켜 난 원을 빼서 초승달
        bool outer = p.magnitude < 0.34f;
        bool cut = (p - new Vector2(0.16f, 0.1f)).magnitude < 0.28f;
        return outer && cut == false;
    }

    internal static Sprite MakeSprite(string spriteName, Shape shape)
    {
        const int sub = 4;
        var tex = new Texture2D(k_TextureSize, k_TextureSize, TextureFormat.RGBA32, false)
        {
            name = $"CatNeedsIcon_{spriteName}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[k_TextureSize * k_TextureSize];
        for (int y = 0; y < k_TextureSize; y++)
        {
            for (int x = 0; x < k_TextureSize; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < sub; sy++)
                {
                    for (int sx = 0; sx < sub; sx++)
                    {
                        var p = new Vector2(
                            (x + (sx + 0.5f) / sub) / k_TextureSize - 0.5f,
                            (y + (sy + 0.5f) / sub) / k_TextureSize - 0.5f);
                        if (shape(p))
                            hits++;
                    }
                }
                byte alpha = (byte)(255 * hits / (sub * sub));
                pixels[y * k_TextureSize + x] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0f, 0f, k_TextureSize, k_TextureSize), new Vector2(0.5f, 0.5f), k_TextureSize);
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (root.name == childName)
            return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, childName);
            if (found != null)
                return found;
        }
        return null;
    }
}
