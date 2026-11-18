using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 고양이 방석. 졸려하는 고양이 옆에 가져다 놓으면 고양이가 올라가 잔다 (CatSleeping).
///
/// 흐름
/// 1. 처음에는 제자리(먹이 상자 쪽 빈자리)에 있다. 고양이는 장애물로 보고 피해 다닌다.
/// 2. 플레이어가 그립으로 집어 옮기고 놓으면, 놓은 곳 근처에서
///    걸어 다닐 영역 안이고 집 · 밥그릇 · 상자와 겹치지 않는 가장 가까운 자리로 스르륵 미끄러져 바르게 놓인다.
///    책상 밖(바닥 등)에 놓으면 제자리로 돌아간다.
/// 3. 놓이면 Placed 이벤트. 고양이가 졸려서 Claim()하면 고양이 몸 크기에 맞춰 커지고(FitTo), 고양이가 내려올 때까지 잡을 수 없다.
///    아무도 Claim하지 않으면 m_IgnoredReturnDelay초 뒤 제자리로 돌아간다.
/// 4. 고양이가 깨서 내려오면 Release() → 원래 크기로 제자리에 돌아간다.
///
/// 물리로 떨어지지 않는다. (Rigidbody는 kinematic, 위치는 이 스크립트가 정한다)
/// 크기는 x · z만 바뀌고 높이(m_Height)는 그대로다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public class Cushion : MonoBehaviour
{
    [SerializeField, Tooltip("크기 1일 때 방석 반지름 (m)")]
    private float m_BaseRadius = 0.045f;

    [SerializeField, Tooltip("방석 윗면 높이 (m). 고양이가 이 높이에 올라선다")]
    private float m_Height = 0.016f;

    [SerializeField, Tooltip("다른 물건과 이만큼은 띄운다 (m)")]
    private float m_Gap = 0.01f;

    [SerializeField, Tooltip("놓은 곳에서 이 반경 안에서 빈자리를 찾는다 (m)")]
    private float m_SearchRadius = 0.3f;

    [SerializeField, Tooltip("자리로 미끄러지는 시간 (초)")]
    private float m_SlideTime = 0.25f;

    [SerializeField, Tooltip("놓았는데 고양이가 쓰지 않으면 이 시간 뒤 제자리로 돌아간다 (초)")]
    private float m_IgnoredReturnDelay = 4f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private XRGrabInteractable m_Grab;
    private Transform m_Anchor;
    private CatWander m_Wander;
    private Vector3 m_HomeLocal;
    private bool m_IsAtHome = true;
    private bool m_Claimed;
    private float m_ReturnAt = -1f;

    // 미끄러지기
    private bool m_Sliding;
    private float m_SlideT;
    private Vector3 m_FromPos, m_ToPos;
    private Quaternion m_FromRot, m_ToRot;
    private float m_FromScale, m_ToScale;
    private Action m_OnSlideDone;

    /// <summary>플레이어가 옮겨 놓았을 때 (자리 정리가 끝난 뒤)</summary>
    public event Action<Cushion> Placed;

    /// <summary>지금 방석 반지름 (m)</summary>
    public float Radius => m_BaseRadius * transform.localScale.x;

    /// <summary>방석 윗면 높이 (m)</summary>
    public float TopHeight => m_Height;

    /// <summary>제자리에 있는지</summary>
    public bool IsAtHome => m_IsAtHome && m_Sliding == false;

    private void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
    }

    private void OnEnable()
    {
        m_Grab.selectEntered.AddListener(OnGrabbed);
        m_Grab.selectExited.AddListener(OnReleased);
    }

    private void OnDisable()
    {
        m_Grab.selectEntered.RemoveListener(OnGrabbed);
        m_Grab.selectExited.RemoveListener(OnReleased);
    }

    /// <summary>CatHousePlacer가 놓은 직후 부른다. 지금 위치가 제자리가 되고, 고양이가 피해 다니도록 등록한다.</summary>
    public void Init(Transform anchor, CatWander wander)
    {
        m_Anchor = anchor;
        m_Wander = wander;
        m_HomeLocal = anchor != null ? anchor.InverseTransformPoint(transform.position) : transform.position;
        if (m_Wander != null)
            m_Wander.AddObstacle(transform, Radius + m_Gap);
    }

    /// <summary>고양이가 쓰겠다고 잡아 둔다. 그동안 플레이어가 집을 수 없고 제자리로 돌아가지 않는다.</summary>
    public bool Claim()
    {
        if (m_Claimed || m_Grab.isSelected)
            return false;
        m_Claimed = true;
        m_ReturnAt = -1f;
        m_Grab.enabled = false;
        return true;
    }

    /// <summary>고양이 몸 반지름에 맞춰 키운다. 커져서 다른 물건과 겹치면 근처 빈자리로 옮긴다.</summary>
    public void FitTo(float radius)
    {
        float scale = Mathf.Max(1f, radius / m_BaseRadius);
        var target = transform.position;
        if (TryFindSpot(transform.position, m_BaseRadius * scale, out var spot))
            target = spot;
        SlideTo(target, transform.rotation, scale, null);
        Log($"고양이 크기에 맞춤: 반지름 {m_BaseRadius * scale:0.000}m");
    }

    /// <summary>고양이가 내려온 뒤 부른다. 원래 크기로 제자리에 돌아간다.</summary>
    public void Release()
    {
        m_Claimed = false;
        m_Grab.enabled = true;
        ReturnHome();
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        m_Sliding = false;
        m_ReturnAt = -1f;
        m_IsAtHome = false;
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        if (m_Claimed)
            return;

        // 바닥 등 책상보다 한참 아래에 놓았으면 그냥 제자리로
        bool tooLow = m_Anchor != null && transform.position.y < m_Anchor.position.y - 0.2f;
        if (tooLow == false && TryFindSpot(transform.position, Radius, out var spot))
        {
            Log("놓은 곳 근처 빈자리로 정리");
            SlideTo(spot, Upright(transform.rotation), transform.localScale.x, () =>
            {
                m_IsAtHome = false;
                m_ReturnAt = Time.time + m_IgnoredReturnDelay;
                Placed?.Invoke(this);
            });
        }
        else
        {
            Log("놓을 자리가 없어 제자리로");
            ReturnHome();
        }
    }

    private void Update()
    {
        if (m_Sliding)
            UpdateSlide();

        if (m_ReturnAt > 0f && m_Claimed == false && m_Grab.isSelected == false && Time.time >= m_ReturnAt)
        {
            m_ReturnAt = -1f;
            Log("고양이가 안 써서 제자리로");
            ReturnHome();
        }
    }

    private void ReturnHome()
    {
        m_ReturnAt = -1f;
        var home = m_Anchor != null ? m_Anchor.TransformPoint(m_HomeLocal) : transform.position;
        var rot = m_Anchor != null ? m_Anchor.rotation : Upright(transform.rotation);
        SlideTo(home, rot, 1f, () => m_IsAtHome = true);
    }

    private void SlideTo(Vector3 position, Quaternion rotation, float scale, Action onDone)
    {
        m_FromPos = transform.position;
        m_FromRot = transform.rotation;
        m_FromScale = transform.localScale.x;
        m_ToPos = position;
        m_ToRot = rotation;
        m_ToScale = scale;
        m_SlideT = 0f;
        m_OnSlideDone = onDone;
        m_Sliding = true;
    }

    private void UpdateSlide()
    {
        m_SlideT += m_SlideTime > 0f ? Time.deltaTime / m_SlideTime : 1f;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_SlideT));
        transform.SetPositionAndRotation(Vector3.Lerp(m_FromPos, m_ToPos, t), Quaternion.Slerp(m_FromRot, m_ToRot, t));
        float s = Mathf.Lerp(m_FromScale, m_ToScale, t);
        transform.localScale = new Vector3(s, 1f, s);
        if (m_SlideT < 1f)
            return;

        m_Sliding = false;
        if (m_Wander != null)
            m_Wander.SetObstacleRadius(transform, Radius + m_Gap);
        var callback = m_OnSlideDone;
        m_OnSlideDone = null;
        callback?.Invoke();
    }

    /// <summary>
    /// from 근처에서 방석(반지름 radius)이 들어갈 빈자리를 찾는다.
    /// 가까운 곳부터 2cm 간격 고리로 넓혀 가며, 가운데와 테두리 8곳이 모두 걸어 다닐 영역 안이고
    /// 집 · 밥그릇 · 상자와 m_Gap 이상 떨어진 첫 자리. 높이는 책상(앵커) 높이.
    /// </summary>
    private bool TryFindSpot(Vector3 from, float radius, out Vector3 spot)
    {
        float y = m_Anchor != null ? m_Anchor.position.y : from.y;
        var center = new Vector3(from.x, y, from.z);
        const float step = 0.02f;
        for (float ring = 0f; ring <= m_SearchRadius; ring += step)
        {
            int count = ring <= 0f ? 1 : Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * ring / step));
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                var p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                if (IsClear(p, radius))
                {
                    spot = p;
                    return true;
                }
            }
        }
        spot = from;
        return false;
    }

    private bool IsClear(Vector3 p, float radius)
    {
        if (m_Wander == null)
            return true;
        if (m_Wander.IsBlocked(p, transform, radius + m_Gap))
            return false;
        if (m_Wander.IsOnWalkableSurface(p) == false)
            return false;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            var rim = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (m_Wander.IsOnWalkableSurface(rim) == false)
                return false;
        }
        return true;
    }

    private static Quaternion Upright(Quaternion rotation)
    {
        var forward = rotation * Vector3.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[Cushion] {message}");
    }
}
