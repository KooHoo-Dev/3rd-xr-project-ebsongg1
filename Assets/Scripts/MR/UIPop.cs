using System.Collections;
using UnityEngine;

/// <summary>
/// World Space 패널 · 안내가 갑자기 켜지고 꺼지지 않게, 나타날 때는 살짝 커지며(팝) 또렷해지고
/// 사라질 때는 살짝 작아지며 흐려진 뒤 꺼지게 한다.
///
/// 따로 붙일 필요 없이 UIPop.Show(transform) / UIPop.Hide(transform)로 부르면 없을 때 붙인다.
/// - Show: 꺼져 있으면 켜고 팝. 이미 보이고 있어도 다시 팝한다 (안내 문구가 바뀌었을 때 눈에 띄게)
/// - Hide: 흐려진 뒤 GameObject를 끈다. 흐려지는 동안은 버튼이 눌리지 않는다
/// - HideNow: 켜 둔 채로 바로 투명하게 (위치를 잡기 전에 엉뚱한 곳에 보이지 않게)
/// 크기는 처음 본 localScale을 기준으로 곱한다. (캔버스 스케일 0.001을 그대로 유지)
/// </summary>
[DisallowMultipleComponent]
public class UIPop : MonoBehaviour
{
    [SerializeField, Tooltip("나타나는 시간 (초)")]
    private float m_ShowTime = 0.25f;

    [SerializeField, Tooltip("사라지는 시간 (초)")]
    private float m_HideTime = 0.18f;

    [SerializeField, Tooltip("나타나기 시작할 때 크기 (기준 크기에 곱함)")]
    private float m_StartScale = 0.85f;

    [SerializeField, Tooltip("사라질 때 이 크기까지 줄어든다")]
    private float m_EndScale = 0.92f;

    private CanvasGroup m_Group;
    private Vector3 m_BaseScale;
    private bool m_Ready;
    private Coroutine m_Routine;

    /// <summary>지금 보이는 중인지 (사라지는 중이면 false)</summary>
    public bool IsShown { get; private set; }

    public static void Show(Transform target)
    {
        if (target != null)
            Get(target).ShowSelf();
    }

    public static void Hide(Transform target)
    {
        if (target != null)
            Get(target).HideSelf();
    }

    public static void HideNow(Transform target)
    {
        if (target != null)
            Get(target).HideNowSelf();
    }

    private static UIPop Get(Transform target)
    {
        if (target.TryGetComponent(out UIPop pop) == false)
            pop = target.gameObject.AddComponent<UIPop>();
        pop.Init();
        return pop;
    }

    /// <summary>꺼진 오브젝트에 붙이면 Awake가 늦게 불리므로 처음 쓸 때 직접 준비한다.</summary>
    private void Init()
    {
        if (m_Ready)
            return;
        m_Ready = true;
        m_BaseScale = transform.localScale;
        if (TryGetComponent(out m_Group) == false)
            m_Group = gameObject.AddComponent<CanvasGroup>();
        IsShown = gameObject.activeSelf && m_Group.alpha > 0f;
    }

    private void ShowSelf()
    {
        IsShown = true;
        if (gameObject.activeSelf == false)
        {
            m_Group.alpha = 0f;
            gameObject.SetActive(true);
        }
        Restart(ShowRoutine());
    }

    private void HideSelf()
    {
        IsShown = false;
        if (gameObject.activeInHierarchy == false)
        {
            gameObject.SetActive(false);
            return;
        }
        Restart(HideRoutine());
    }

    private void HideNowSelf()
    {
        IsShown = false;
        Stop();
        SetInteractable(false);
        m_Group.alpha = 0f;
    }

    private void Restart(IEnumerator routine)
    {
        Stop();
        m_Routine = StartCoroutine(routine);
    }

    private void Stop()
    {
        if (m_Routine != null)
            StopCoroutine(m_Routine);
        m_Routine = null;
    }

    private IEnumerator ShowRoutine()
    {
        SetInteractable(true);
        float fromAlpha = m_Group.alpha;
        for (float t = 0f; t < m_ShowTime; t += Time.unscaledDeltaTime)
        {
            float k = t / m_ShowTime;
            m_Group.alpha = Mathf.Lerp(fromAlpha, 1f, k);
            transform.localScale = m_BaseScale * Mathf.LerpUnclamped(m_StartScale, 1f, EaseOutBack(k));
            yield return null;
        }
        m_Group.alpha = 1f;
        transform.localScale = m_BaseScale;
        m_Routine = null;
    }

    private IEnumerator HideRoutine()
    {
        SetInteractable(false);
        float fromAlpha = m_Group.alpha;
        float fromScale = transform.localScale.x / Mathf.Max(0.0001f, m_BaseScale.x);
        for (float t = 0f; t < m_HideTime; t += Time.unscaledDeltaTime)
        {
            float k = t / m_HideTime;
            m_Group.alpha = Mathf.Lerp(fromAlpha, 0f, k);
            transform.localScale = m_BaseScale * Mathf.Lerp(fromScale, m_EndScale, k * k);
            yield return null;
        }
        m_Group.alpha = 0f;
        transform.localScale = m_BaseScale;
        m_Routine = null;
        gameObject.SetActive(false);
    }

    private void SetInteractable(bool on)
    {
        m_Group.interactable = on;
        m_Group.blocksRaycasts = on;
    }

    private void OnDisable()
    {
        // 사라지는 도중에 밖에서 꺼 버려도 다음에 켤 때 크기가 어긋나지 않게
        if (m_Ready && m_Routine != null)
        {
            m_Routine = null;
            transform.localScale = m_BaseScale;
        }
    }

    /// <summary>끝에서 살짝 넘쳤다가 돌아오는 곡선 (팝 느낌)</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
