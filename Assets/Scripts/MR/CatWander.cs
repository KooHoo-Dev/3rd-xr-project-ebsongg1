using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 고양이가 놓인 평면(책상) 안을 혼자 돌아다니게 한다.
///
/// 흐름
/// 1. 쉬기: 몇 초 동안 제자리에서 대기 (Animator Speed = 0)
/// 2. 걷기: 랜덤 방향 · 거리로 목표를 정하고 몸을 돌린 뒤 걸어간다 (Speed = 1)
/// 3. 가장자리: 한 발 앞(m_EdgeMargin)이 평면 테두리 밖이면 멈추고 Alert(멈칫) → 평면 안쪽으로 돌아선다
/// 4. 집: 한 발 앞이 고양이 집 자리면 멈칫 없이 집 반대쪽으로 돌아선다
/// 도착하면 다시 1번으로.
///
/// 시작은 CatHousePlacer가 고양이를 놓은 직후 Begin()을 불러서 한다.
/// 먹이 · 쓰다듬기 등 다른 동작을 할 때는 Pause() / Resume(), 특정 위치로 보낼 때는 MoveTo()를 쓴다.
///
/// 이동은 이 스크립트가 직접 Transform을 움직인다. (Animator의 Apply Root Motion은 꺼져 있어야 한다)
/// 고양이 모델의 앞쪽은 로컬 +Z.
/// </summary>
[DisallowMultipleComponent]
public class CatWander : MonoBehaviour
{
    private enum State { Waiting, Resting, Walking, Alerting, Paused }

    [Header("이동")]
    [SerializeField, Tooltip("걷는 속도 (m/s)")]
    private float m_WalkSpeed = 0.04f;

    [SerializeField, Tooltip("도는 속도 (도/초)")]
    private float m_TurnSpeed = 200f;

    [SerializeField, Tooltip("이 각도보다 많이 틀어져 있으면 제자리에서 먼저 돈다")]
    private float m_StartWalkAngle = 45f;

    [SerializeField, Tooltip("한 번에 걷는 거리 (최소 · 최대, m)")]
    private Vector2 m_WalkDistance = new Vector2(0.08f, 0.25f);

    [SerializeField, Tooltip("도착 후 쉬는 시간 (최소 · 최대, 초)")]
    private Vector2 m_RestTime = new Vector2(1.5f, 4f);

    [Header("가장자리 · 집")]
    [SerializeField, Tooltip("이만큼 앞이 평면 밖이면 멈춘다 (m). 고양이 머리가 책상 밖으로 나가지 않게")]
    private float m_EdgeMargin = 0.05f;

    [SerializeField, Tooltip("멈칫(Alert) 하는 시간 (초)")]
    private float m_AlertTime = 1.2f;

    [SerializeField, Range(0f, 1f), Tooltip("목표를 고를 때 가장자리 쪽이어도 그냥 가 볼 확률. 0이면 멈칫이 거의 안 나온다")]
    private float m_EdgeCuriosity = 0.3f;

    [SerializeField, Tooltip("고양이 집이 차지하는 영역 (집 기준 x · z 반지름, m)")]
    private Vector2 m_HouseHalfSize = new Vector2(0.09f, 0.08f);

    [SerializeField, Tooltip("평면 정보가 없을 때 집에서 이 반경 안에서만 돌아다닌다 (m)")]
    private float m_HomeRadius = 0.4f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private static readonly int s_SpeedHash = Animator.StringToHash("Speed");
    private static readonly int s_AlertHash = Animator.StringToHash("Alert");

    private Animator m_Animator;
    private ARPlane m_Plane;
    private Transform m_Home;
    private State m_State = State.Waiting;
    private float m_Timer;
    private Vector3 m_Target;
    private bool m_IsStepping;
    private bool m_ForcedMove;
    private Action m_OnArrived;

    /// <summary>지금 걷고 있는지 (돌기만 하는 중이면 false)</summary>
    public bool IsWalking => m_State == State.Walking && m_IsStepping;

    /// <summary>Pause()로 멈춰 있는지</summary>
    public bool IsPaused => m_State == State.Paused;

    /// <summary>걷는 속도. 성장 단계에서 바꿀 수 있다.</summary>
    public float WalkSpeed
    {
        get => m_WalkSpeed;
        set => m_WalkSpeed = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
    }

    /// <summary>돌아다니기 시작. plane은 고양이가 놓인 평면, home은 집이 붙은 앵커.</summary>
    public void Begin(ARPlane plane, Transform home)
    {
        m_Plane = plane;
        m_Home = home;
        StartRest();
    }

    /// <summary>돌아다니기를 멈춘다. 다른 동작(먹기 · 쓰다듬기 등)을 하기 전에 부른다.</summary>
    public void Pause()
    {
        if (m_State == State.Waiting)
            return;
        m_State = State.Paused;
        m_ForcedMove = false;
        m_OnArrived = null;
    }

    /// <summary>Pause() 후 다시 돌아다니게 한다. 잠깐 쉬었다가 출발한다.</summary>
    public void Resume()
    {
        if (m_State != State.Paused)
            return;
        StartRest();
    }

    /// <summary>
    /// 지정한 위치까지 걸어가게 한다. (예: 밥그릇)
    /// 가장자리 검사는 하지 않으니 평면 안의 위치를 넘길 것.
    /// 도착하면 Pause 상태가 되고 onArrived가 불린다. 볼일이 끝나면 Resume()을 부를 것.
    /// </summary>
    public void MoveTo(Vector3 worldPoint, Action onArrived = null)
    {
        if (m_State == State.Waiting)
            return;
        m_ForcedMove = true;
        m_OnArrived = onArrived;
        StartWalk(worldPoint);
    }

    private void Update()
    {
        switch (m_State)
        {
            case State.Resting:
                m_Timer -= Time.deltaTime;
                if (m_Timer <= 0f)
                    PickNewTarget();
                break;

            case State.Alerting:
                m_Timer -= Time.deltaTime;
                if (m_Timer <= 0f)
                    TurnAwayFromEdge();
                break;

            case State.Walking:
                UpdateWalk();
                break;
        }

        if (m_Animator != null)
        {
            float speed = IsWalking ? 1f : 0f;
            m_Animator.SetFloat(s_SpeedHash, speed, 0.1f, Time.deltaTime);
        }
    }

    private void UpdateWalk()
    {
        var pos = transform.position;
        var toTarget = m_Target - pos;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance < 0.01f)
        {
            Arrive();
            return;
        }

        // 목표 쪽으로 돈다. 많이 틀어져 있으면 제자리에서 돌기만 한다.
        var look = Quaternion.LookRotation(toTarget / distance, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        m_IsStepping = Quaternion.Angle(transform.rotation, look) < m_StartWalkAngle;
        if (m_IsStepping == false)
            return;

        var forward = Flat(transform.forward);
        if (m_ForcedMove == false)
        {
            var ahead = pos + forward * m_EdgeMargin;
            if (IsOnSurface(ahead) == false)
            {
                StartAlert();
                return;
            }
            if (IsInsideHouse(ahead))
            {
                TurnAwayFromHouse();
                return;
            }
        }

        pos += forward * Mathf.Min(m_WalkSpeed * Time.deltaTime, distance);
        // 출발 높이가 평면과 다르면(Cat Offset y 등) 걸으면서 평면 높이로 맞춘다.
        pos.y = Mathf.MoveTowards(pos.y, SurfaceY(pos), m_WalkSpeed * Time.deltaTime);
        transform.position = pos;
    }

    private void StartRest()
    {
        m_State = State.Resting;
        m_IsStepping = false;
        m_Timer = UnityEngine.Random.Range(m_RestTime.x, m_RestTime.y);
        Log($"쉬기 {m_Timer:0.0}초");
    }

    private void StartWalk(Vector3 target)
    {
        m_Target = target;
        m_State = State.Walking;
        m_IsStepping = false;
    }

    private void StartAlert()
    {
        m_State = State.Alerting;
        m_IsStepping = false;
        m_Timer = m_AlertTime;
        if (m_Animator != null)
            m_Animator.SetTrigger(s_AlertHash);
        Log("가장자리! 멈칫");
    }

    private void Arrive()
    {
        m_IsStepping = false;
        if (m_ForcedMove)
        {
            m_ForcedMove = false;
            m_State = State.Paused;
            var callback = m_OnArrived;
            m_OnArrived = null;
            callback?.Invoke();
            return;
        }
        StartRest();
    }

    /// <summary>
    /// 랜덤 방향 · 거리로 다음 목표를 고른다.
    /// 대부분은 평면 안쪽에 있는 목표를 고르지만, m_EdgeCuriosity 확률로 가장자리 쪽이어도 그냥 간다. (그러면 가다가 멈칫한다)
    /// </summary>
    private void PickNewTarget()
    {
        var pos = transform.position;
        bool curious = UnityEngine.Random.value < m_EdgeCuriosity;
        Vector3 target = pos;
        for (int i = 0; i < 10; i++)
        {
            float angle = UnityEngine.Random.Range(0f, 360f);
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            target = pos + dir * UnityEngine.Random.Range(m_WalkDistance.x, m_WalkDistance.y);
            if (IsInsideHouse(target))
                continue;
            if (curious || IsOnSurface(target + dir * m_EdgeMargin))
                break;
        }
        Log(curious ? "새 목표 (가장자리여도 가 보기)" : "새 목표");
        StartWalk(target);
    }

    /// <summary>멈칫한 뒤 평면 가운데 쪽(조금 랜덤하게)으로 돌아선다.</summary>
    private void TurnAwayFromEdge()
    {
        var pos = transform.position;
        var center = m_Plane != null ? m_Plane.center : (m_Home != null ? m_Home.position : pos);
        StartWalk(pos + RandomizedDirection(center - pos) * UnityEngine.Random.Range(m_WalkDistance.x, m_WalkDistance.y));
    }

    /// <summary>집에 부딪히기 전에 집 반대쪽으로 돌아선다.</summary>
    private void TurnAwayFromHouse()
    {
        var pos = transform.position;
        var away = m_Home != null ? pos - m_Home.position : -transform.forward;
        StartWalk(pos + RandomizedDirection(away) * UnityEngine.Random.Range(m_WalkDistance.x, m_WalkDistance.y));
    }

    /// <summary>방향을 수평으로 만들고 ±50도 안에서 살짝 비튼다.</summary>
    private Vector3 RandomizedDirection(Vector3 direction)
    {
        var flat = Flat(direction);
        if (flat.sqrMagnitude < 0.0001f)
            flat = -Flat(transform.forward);
        return Quaternion.Euler(0f, UnityEngine.Random.Range(-50f, 50f), 0f) * flat;
    }

    /// <summary>점이 돌아다녀도 되는 바닥 위인지. 평면이 있으면 평면 테두리, 없으면 집에서 일정 반경.</summary>
    private bool IsOnSurface(Vector3 worldPoint)
    {
        if (m_Plane != null)
            return CatHousePlacer.IsInsideBoundary(m_Plane, worldPoint);
        if (m_Home != null)
            return Flat(worldPoint - m_Home.position).magnitude < m_HomeRadius;
        return true;
    }

    private bool IsInsideHouse(Vector3 worldPoint)
    {
        if (m_Home == null)
            return false;
        var local = m_Home.InverseTransformPoint(worldPoint);
        return Mathf.Abs(local.x) < m_HouseHalfSize.x && Mathf.Abs(local.z) < m_HouseHalfSize.y;
    }

    /// <summary>평면 높이. 집 앵커가 평면 위에 있으므로 앵커 높이를 쓴다.</summary>
    private float SurfaceY(Vector3 current)
    {
        return m_Home != null ? m_Home.position.y : current.y;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0f ? v.normalized : v;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatWander] {message}");
    }
}
