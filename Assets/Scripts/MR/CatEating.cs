using UnityEngine;

/// <summary>
/// 밥그릇에 먹이가 들어오면 고양이가 걸어가서 먹는다.
///
/// 흐름
/// 1. 기다리기: 연결된 FoodBowl에 먹이가 생길 때까지 (매 프레임 HasFood 확인)
/// 2. 걸어가기: CatWander.MoveTo()로 밥그릇 옆(m_EatDistance)까지 간다
/// 3. 돌기: 밥그릇 쪽으로 몸을 돌린다
/// 4. 먹기: Animator 트리거 Eat (Quadruped_Eating) + 웃는 얼굴, m_EatTime 동안. 먹이는 점점 작아진다
/// 5. 끝: CatNeeds.Feed() (배고픔 감소 · 기분 상승), 다시 돌아다니기
///
/// 자고 있을 때 먹이가 들어오면 m_WakeUpForFood가 켜져 있으면 깨워서 먹으러 가고, 꺼져 있으면 깰 때까지 기다린다.
/// 먹는 중(IsBusy)에는 CatNeeds가 재우지 않고, CatPetting이 동작을 끊지 않는다.
/// 밥그릇 연결은 CatHousePlacer가 놓을 때 FoodBowl.SetCat()으로 한다.
/// </summary>
[DisallowMultipleComponent]
public class CatEating : MonoBehaviour
{
    private enum State { Idle, Going, Turning, Eating }

    [SerializeField, Tooltip("밥그릇 가운데에서 이 거리에 멈춰서 먹는다 (m). 고양이 크기 0.06 기준. 고양이 입이 그릇에 닿게 조절")]
    private float m_EatDistance = 0.05f;

    [SerializeField, Tooltip("먹는 시간 (초)")]
    private float m_EatTime = 3f;

    [SerializeField, Tooltip("밥그릇 쪽으로 도는 속도 (도/초)")]
    private float m_TurnSpeed = 200f;

    [SerializeField, Tooltip("자고 있을 때 먹이가 들어오면 깨워서 먹으러 간다")]
    private bool m_WakeUpForFood = true;

    [SerializeField, Tooltip("위 거리 값의 기준이 되는 고양이 크기. 고양이가 커지면 거리도 같이 커진다")]
    private float m_ReferenceScale = 0.06f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private static readonly int s_EatHash = Animator.StringToHash("Eat");
    private const string k_EatStateName = "Eat";

    private Animator m_Animator;
    private CatNeeds m_Needs;
    private CatFace m_Face;
    private CatWander m_Wander;
    private CatGrowth m_Growth;
    private CatBehaviors m_Behaviors;
    private CatSleeping m_Sleeping;
    private FoodBowl m_Bowl;
    private State m_State = State.Idle;
    private float m_Timer;
    private float m_HungerAmount = -1f;
    private Vector3 m_EatPoint;

    /// <summary>먹으러 가는 중이거나 먹는 중인지</summary>
    public bool IsBusy => m_State != State.Idle;

    /// <summary>먹인 횟수 (결과 화면용)</summary>
    public int FeedCount { get; private set; }

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Needs = GetComponent<CatNeeds>();
        m_Face = GetComponent<CatFace>();
        m_Wander = GetComponent<CatWander>();
        m_Growth = GetComponent<CatGrowth>();
        m_Behaviors = GetComponent<CatBehaviors>();
        m_Sleeping = GetComponent<CatSleeping>();
    }

    /// <summary>밥그릇 연결. FoodBowl.SetCat()에서 부른다.</summary>
    public void SetBowl(FoodBowl bowl)
    {
        m_Bowl = bowl;
    }

    private void Update()
    {
        switch (m_State)
        {
            case State.Idle:
                if (m_Bowl != null && m_Bowl.HasFood)
                    TryStartGoing();
                break;

            case State.Going:
                UpdateGoing();
                break;

            case State.Turning:
                UpdateTurning();
                break;

            case State.Eating:
                UpdateEating();
                break;
        }
    }

    private void TryStartGoing()
    {
        // 방석으로 가는 중 · 방석 위에서 자는 중 · 내려오는 중이면 끝날 때까지 기다린다. (방석 위에서는 깨우지 않는다)
        if (m_Sleeping != null && m_Sleeping.IsBusy)
            return;

        if (m_Needs != null && m_Needs.IsSleeping)
        {
            if (m_WakeUpForFood == false)
                return;
            m_Needs.WakeUp();
            Log("먹이 냄새에 깸");
        }

        m_EatPoint = FindEatPoint();
        m_State = State.Going;
        Log("먹으러 감");

        if (m_Wander == null)
        {
            // 돌아다니기 스크립트가 없으면 그 자리에서 먹는다.
            m_State = State.Turning;
            return;
        }
        m_Wander.MoveTo(m_EatPoint, OnArrived);
    }

    private void UpdateGoing()
    {
        // 걸어가는 중에 다른 일로 Pause()되어 목표가 취소됐으면 다시 보낸다.
        if (m_Wander != null && m_Wander.IsPaused)
            m_Wander.MoveTo(m_EatPoint, OnArrived);
    }

    private void OnArrived()
    {
        if (m_State == State.Going)
            m_State = State.Turning;
    }

    private void UpdateTurning()
    {
        if (m_Bowl == null)
        {
            Finish();
            return;
        }

        var toBowl = m_Bowl.transform.position - transform.position;
        toBowl.y = 0f;
        if (toBowl.sqrMagnitude > 0.000001f)
        {
            var look = Quaternion.LookRotation(toBowl.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, look) > 3f)
                return;
        }
        StartEating();
    }

    private void StartEating()
    {
        if (m_Bowl == null || m_Bowl.HasFood == false)
        {
            Finish();
            return;
        }

        m_State = State.Eating;
        m_Timer = m_EatTime;
        m_HungerAmount = m_Bowl.Consume(m_EatTime);
        if (m_Animator != null)
            m_Animator.SetTrigger(s_EatHash);
        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, m_EatTime);
        Log("냠냠");
    }

    private void UpdateEating()
    {
        m_Timer -= Time.deltaTime;

        // 먹는 동작이 m_EatTime보다 짧으면 끝날 때마다 다시 재생한다.
        if (m_Animator != null && m_Timer > 0.5f && m_Animator.IsInTransition(0) == false
            && m_Animator.GetCurrentAnimatorStateInfo(0).IsName(k_EatStateName) == false)
            m_Animator.SetTrigger(s_EatHash);

        if (m_Timer > 0f)
            return;

        // 배고플 때 먹였으면 성장 보너스 (배부를 때 먹이는 건 보너스 없음)
        if (m_Growth != null)
            m_Growth.Care(CareKind.Feed, m_Needs != null && m_Needs.IsHungry);
        if (m_Needs != null)
            m_Needs.Feed(m_HungerAmount);
        FeedCount++;
        Log($"다 먹음 (총 {FeedCount}번)");
        // 어른이면 먹고 나서 기뻐하기 (CatBehaviors가 다음 프레임에 재생)
        if (m_Behaviors != null)
            m_Behaviors.OnFed();
        Finish();
    }

    private void Finish()
    {
        m_State = State.Idle;
        if (m_Animator != null)
            m_Animator.ResetTrigger(s_EatHash);
        if (m_Wander != null)
            m_Wander.Resume();
    }

    /// <summary>
    /// 밥그릇 옆에서 먹을 자리를 고른다.
    /// 지금 고양이가 있는 쪽부터 45도씩 돌려 가며, 평면 안이고 고양이 집과 겹치지 않는 첫 자리를 쓴다.
    /// </summary>
    private Vector3 FindEatPoint()
    {
        var bowl = m_Bowl.transform.position;
        float distance = Scaled(m_EatDistance);
        var dir = transform.position - bowl;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.000001f ? dir.normalized : -m_Bowl.transform.forward;

        var home = m_Bowl.transform.parent;
        for (int i = 0; i < 8; i++)
        {
            // 0, +45, -45, +90, -90 ... 순서로 가까운 쪽부터
            float angle = ((i + 1) / 2) * 45f * (i % 2 == 0 ? 1f : -1f);
            var point = bowl + Quaternion.Euler(0f, angle, 0f) * dir * distance;
            point.y = bowl.y;
            // 걸어 다닐 영역은 CatWander가 집 기준으로 복사해 둔 것을 쓴다. (공간 위치가 다시 맞춰져도 집과 어긋나지 않게)
            bool onPlane = m_Wander == null || m_Wander.IsOnWalkableSurface(point);
            bool clearOfHouse = home == null || IsClearOfHouse(home, point);
            if (onPlane && clearOfHouse)
                return point;
        }
        var fallback = bowl + dir * distance;
        fallback.y = bowl.y;
        return fallback;
    }

    /// <summary>집(앵커 위치, 가로 약 14cm · 세로 12cm)과 겹치지 않는지</summary>
    private static bool IsClearOfHouse(Transform home, Vector3 point)
    {
        var local = home.InverseTransformPoint(point);
        return Mathf.Abs(local.x) > 0.09f || Mathf.Abs(local.z) > 0.08f;
    }

    private float Scaled(float value)
    {
        if (m_ReferenceScale <= 0f)
            return value;
        return value * (transform.lossyScale.x / m_ReferenceScale);
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatEating] {message}");
    }
}
