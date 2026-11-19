using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 안내 메시지가 가리키는 물건(밥그릇 · 방석 등) 위에 떠서 위아래로 통통 움직이는 화살표.
/// 처음 하는 사람이 "이게 밥그릇이구나, 이게 방석이구나" 알 수 있게 한다.
///
/// - Show(target): target 위에 팝 하고 나타난다. target이 움직이면 따라간다.
/// - Hide(): 작아지며 사라진다.
/// - target을 손에 들고 있는 동안은 숨는다. (이미 찾았으니까) 내려놓으면 다시 보인다.
/// - 좌우로만 돌아서 항상 플레이어 쪽을 본다. 흰 테두리가 있어서 어떤 배경에서도 잘 보인다.
/// 그림 파일 없이 코드로 그린다. GameFlow가 만들어 쓴다.
/// </summary>
public class HintArrow : MonoBehaviour
{
    [SerializeField, Tooltip("화살표 크기 (m)")]
    private float m_Size = 0.045f;

    [SerializeField, Tooltip("물건 윗면에서 이만큼 위에 띄운다 (m)")]
    private float m_Gap = 0.025f;

    [SerializeField, Tooltip("위아래로 움직이는 폭 (m)")]
    private float m_BobHeight = 0.012f;

    [SerializeField, Tooltip("위아래로 움직이는 빠르기")]
    private float m_BobSpeed = 4f;

    [SerializeField] private Color m_Color = new Color(0.93f, 0.48f, 0.42f);

    private static Sprite s_Fill;
    private static Sprite s_Outline;

    private Transform m_Target;
    private XRGrabInteractable m_TargetGrab;
    private SpriteRenderer m_FillRenderer;
    private SpriteRenderer m_OutlineRenderer;
    private Transform m_Visual;
    private bool m_On;
    private float m_Visible;
    private float m_Pop;

    /// <summary>씬에 화살표를 하나 만든다. (처음엔 숨어 있다)</summary>
    public static HintArrow Create(string arrowName)
    {
        var go = new GameObject(arrowName);
        return go.AddComponent<HintArrow>();
    }

    private void Awake()
    {
        if (s_Fill == null)
        {
            s_Fill = CatNeedsIcons.MakeSprite("ArrowFill", p => DrawArrow(p, 0f));
            s_Outline = CatNeedsIcons.MakeSprite("ArrowOutline", p => DrawArrow(p, 0.06f));
        }

        m_Visual = new GameObject("Visual").transform;
        m_Visual.SetParent(transform, false);
        m_OutlineRenderer = CreateRenderer("Outline", s_Outline, Color.white, 30);
        m_FillRenderer = CreateRenderer("Fill", s_Fill, m_Color, 31);
        m_Visual.gameObject.SetActive(false);
    }

    private SpriteRenderer CreateRenderer(string rendererName, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(rendererName);
        go.transform.SetParent(m_Visual, false);
        // 테두리가 채움보다 살짝 뒤에 있게
        go.transform.localPosition = new Vector3(0f, 0f, order == 30 ? 0.0005f : 0f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = color;
        r.sortingOrder = order;
        return r;
    }

    public void Show(Transform target)
    {
        if (target == null)
        {
            Hide();
            return;
        }
        if (target != m_Target || m_On == false)
            m_Pop = 0f;
        m_Target = target;
        m_TargetGrab = target.GetComponentInParent<XRGrabInteractable>();
        m_On = true;
    }

    public void Hide()
    {
        m_On = false;
    }

    private void LateUpdate()
    {
        bool want = m_On && m_Target != null && m_Target.gameObject.activeInHierarchy
            && (m_TargetGrab == null || m_TargetGrab.isSelected == false);

        // 나타날 때는 팝, 사라질 때는 빠르게 줄어든다.
        m_Visible = Mathf.MoveTowards(m_Visible, want ? 1f : 0f, Time.deltaTime * (want ? 4f : 6f));
        if (want)
            m_Pop = Mathf.Min(1f, m_Pop + Time.deltaTime * 4f);
        else
            m_Pop = Mathf.Min(m_Pop, m_Visible);

        if (m_Visible <= 0f || m_Target == null)
        {
            if (m_Visual.gameObject.activeSelf)
                m_Visual.gameObject.SetActive(false);
            return;
        }
        if (m_Visual.gameObject.activeSelf == false)
            m_Visual.gameObject.SetActive(true);

        // 물건 윗면 가운데 위에 띄운다. (방석은 쓸 때 커지므로 매 프레임 잰다)
        var bounds = TargetBounds();
        float bob = (Mathf.Sin(Time.time * m_BobSpeed) * 0.5f + 0.5f) * m_BobHeight;
        var pos = new Vector3(bounds.center.x, bounds.max.y + m_Gap + m_Size * 0.5f + bob, bounds.center.z);
        transform.position = pos;

        var cam = Camera.main;
        if (cam != null)
        {
            var toArrow = pos - cam.transform.position;
            toArrow.y = 0f;
            if (toArrow.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toArrow, Vector3.up);
        }

        float scale = want ? EaseOutBack(m_Pop) : m_Visible;
        m_Visual.localScale = Vector3.one * (m_Size * Mathf.Max(0f, scale));
        SetAlpha(m_FillRenderer, m_Color, m_Visible);
        SetAlpha(m_OutlineRenderer, Color.white, m_Visible);
    }

    private Bounds TargetBounds()
    {
        bool found = false;
        var bounds = new Bounds(m_Target.position, Vector3.zero);
        foreach (var r in m_Target.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled == false || r is ParticleSystemRenderer)
                continue;
            if (found)
            {
                bounds.Encapsulate(r.bounds);
            }
            else
            {
                bounds = r.bounds;
                found = true;
            }
        }
        return bounds;
    }

    private static void SetAlpha(SpriteRenderer r, Color color, float alpha)
    {
        color.a *= Mathf.Clamp01(alpha);
        r.color = color;
    }

    /// <summary>아래를 가리키는 화살표. grow만큼 두껍게 그리면 테두리용</summary>
    private static bool DrawArrow(Vector2 p, float grow)
    {
        // 머리: 아래로 뾰족한 삼각형
        bool head = p.y > -0.40f - grow && p.y < 0.02f + grow
            && Mathf.Abs(p.x) < Mathf.Min((p.y + 0.40f) * 0.85f + grow, 0.36f + grow);
        // 몸통: 위가 둥근 막대
        bool stem = Mathf.Abs(p.x) < 0.12f + grow && p.y > -0.05f && p.y < 0.30f;
        bool cap = (p - new Vector2(0f, 0.30f)).magnitude < 0.12f + grow;
        return head || stem || cap;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
