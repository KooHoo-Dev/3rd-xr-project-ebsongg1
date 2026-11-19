using UnityEngine;

/// <summary>
/// 졸릴 때 플레이어가 방석(Cushion)을 가까이 놓아 주면 방석 위로 올라가 잔다.
///
/// 흐름
/// 1. 방석이 놓임 (Cushion.Placed): 고양이가 졸리고(달 아이콘이 튀는 중) 방석이 가까우면 방석을 잡아 둔다(Claim).
///    졸리지 않으면 깜짝 놀란 얼굴로 한 번 쳐다보기만 한다. 방석은 잠시 뒤 제자리로 돌아간다.
/// 2. 맞추기  : 지금 고양이 몸 크기를 재서 방석을 그만큼 키운다 (겹치지 않게)
/// 3. 걸어가기: 방석 가장자리 바로 밖까지 걸어간다 (걸어서 올라가면 발이 방석을 뚫으므로)
/// 4. 올라가기: 방석 쪽으로 돌아서 가운데로 폴짝 뛰어오른다. 높이는 방석 윗면에 맞춘다
/// 5. 자기    : 플레이어 쪽으로 돌아앉아 잠든다. 방석 위에서는 m_RecoveryMultiplier배 빨리 개운해지고 성장 보너스
/// 6. 내려오기: 깨면 바라보는 쪽 가장자리로 폴짝 내려와 책상 높이로 돌아가고, 방석은 제자리로 돌아간다
///
/// 재워 주지 않으면 CatNeeds가 그 자리에서 쓰러지듯 재운다 (이 스크립트와 상관없음).
/// 방석 연결은 CatHousePlacer가 놓을 때 SetCushion()으로 한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CatNeeds))]
public class CatSleeping : MonoBehaviour
{
    private enum Step { Idle, Fitting, Going, FacingCushion, HopUp, Settling, Sleeping, HopDown }

    [SerializeField, Tooltip("방석 가장자리에서 고양이까지 이 거리 안이면 '가져다줬다'로 본다 (m)")]
    private float m_OfferDistance = 0.25f;

    [SerializeField, Tooltip("방석을 고양이 몸 반지름의 몇 배로 키울지")]
    private float m_FitMargin = 1.2f;

    [SerializeField, Tooltip("방석이 커지는 동안 기다리는 시간 (초)")]
    private float m_FitWait = 0.35f;

    [SerializeField, Tooltip("가장자리에서 방석까지 띄우는 거리 (m)")]
    private float m_EdgeGap = 0.012f;

    [SerializeField, Tooltip("폴짝 뛰는 시간 (초)")]
    private float m_HopTime = 0.4f;

    [SerializeField, Tooltip("폴짝 뛸 때 가장 높이 올라가는 높이 (m)")]
    private float m_HopHeight = 0.03f;

    [SerializeField, Tooltip("방석 위에서 잘 때 졸림이 내려가는 속도 배율")]
    private float m_RecoveryMultiplier = 1.5f;

    [SerializeField, Tooltip("돌아서는 속도 (도/초)")]
    private float m_TurnSpeed = 240f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private CatNeeds m_Needs;
    private CatWander m_Wander;
    private CatEating m_Eating;
    private CatBehaviors m_Behaviors;
    private CatGrowth m_Growth;
    private CatEnding m_Ending;
    private CatFace m_Face;

    private Cushion m_Cushion;
    private Step m_Step = Step.Idle;
    private float m_Timer;
    private Vector3 m_EdgePoint;
    private Vector3 m_HopFrom, m_HopTo;
    private float m_HopT;

    /// <summary>방석으로 가는 중 · 방석 위 · 내려오는 중</summary>
    public bool IsBusy => m_Step != Step.Idle;

    /// <summary>방석에서 잔 횟수 (안내 · 결과용)</summary>
    public int CushionSleepCount { get; private set; }

    private void Awake()
    {
        m_Needs = GetComponent<CatNeeds>();
        m_Wander = GetComponent<CatWander>();
        m_Eating = GetComponent<CatEating>();
        m_Behaviors = GetComponent<CatBehaviors>();
        m_Growth = GetComponent<CatGrowth>();
        m_Ending = GetComponent<CatEnding>();
        m_Face = GetComponent<CatFace>();
    }

    private void OnDestroy()
    {
        if (m_Cushion != null)
            m_Cushion.Placed -= OnCushionPlaced;
    }

    /// <summary>방석 연결. CatHousePlacer가 부른다.</summary>
    public void SetCushion(Cushion cushion)
    {
        if (m_Cushion != null)
            m_Cushion.Placed -= OnCushionPlaced;
        m_Cushion = cushion;
        if (m_Cushion != null)
            m_Cushion.Placed += OnCushionPlaced;
    }

    private void OnCushionPlaced(Cushion cushion)
    {
        if (enabled == false || m_Step != Step.Idle || m_Needs.IsSleeping)
            return;

        float distance = Flat(cushion.transform.position - transform.position).magnitude - cushion.Radius;
        if (distance > m_OfferDistance)
            return;

        if (m_Needs.IsSleepy == false)
        {
            // 졸리지 않으면 한 번 쳐다보기만
            if (m_Face != null)
                m_Face.Show(CatExpression.Surprised, 1f);
            Log("방석이 왔지만 안 졸림");
            return;
        }

        if (IsCatFree() == false || cushion.Claim() == false)
            return;

        Log("방석으로!");
        if (m_Wander != null)
            m_Wander.Pause();
        cushion.FitTo(MeasureBodyRadius() * m_FitMargin);
        m_Timer = m_FitWait;
        m_Step = Step.Fitting;
    }

    private bool IsCatFree()
    {
        if (m_Eating != null && m_Eating.IsBusy)
            return false;
        if (m_Behaviors != null && m_Behaviors.IsBusy)
            return false;
        if (m_Growth != null && m_Growth.IsStageUpPending)
            return false;
        if (m_Ending != null && m_Ending.IsPlaying)
            return false;
        return true;
    }

    private void Update()
    {
        switch (m_Step)
        {
            case Step.Fitting:
                m_Timer -= Time.deltaTime;
                if (m_Timer <= 0f)
                    StartGoing();
                break;

            case Step.Going:
                // 걸어가는 중에 다른 일로 Pause()되어 목표가 취소됐으면 다시 보낸다.
                if (m_Wander != null && m_Wander.IsPaused)
                    m_Wander.MoveTo(m_EdgePoint, OnArrivedAtEdge);
                break;

            case Step.FacingCushion:
                if (TurnTowards(m_Cushion.transform.position))
                    StartHop(CushionTop(), Step.HopUp);
                break;

            case Step.HopUp:
                if (UpdateHop())
                    m_Step = Step.Settling;
                break;

            case Step.Settling:
                var cam = Camera.main;
                if (cam == null || TurnTowards(cam.transform.position))
                    FallAsleep();
                break;

            case Step.Sleeping:
                // 방석 위에 그대로 붙어 있게 (앵커가 다시 맞춰져도 방석과 같이 움직인다)
                transform.position = CushionTop();
                if (m_Needs.IsSleeping == false)
                    StartHopDown();
                break;

            case Step.HopDown:
                if (UpdateHop())
                    FinishHopDown();
                break;
        }
    }

    private void StartGoing()
    {
        var center = m_Cushion.transform.position;
        var dir = Flat(transform.position - center);
        if (dir.sqrMagnitude < 0.0001f)
            dir = Flat(-m_Cushion.transform.forward);
        m_EdgePoint = center + dir.normalized * (m_Cushion.Radius + m_EdgeGap);
        m_EdgePoint.y = center.y;
        m_Step = Step.Going;
        if (m_Wander != null)
            m_Wander.MoveTo(m_EdgePoint, OnArrivedAtEdge);
        else
            OnArrivedAtEdge();
    }

    private void OnArrivedAtEdge()
    {
        if (m_Step == Step.Going)
            m_Step = Step.FacingCushion;
    }

    private void FallAsleep()
    {
        m_Needs.HoldWanderOnWake = true;
        m_Needs.SleepRecoveryMultiplier = m_RecoveryMultiplier;
        m_Needs.FallAsleep();
        if (m_Growth != null)
            m_Growth.Care(CareKind.Sleep, true);
        CushionSleepCount++;
        m_Step = Step.Sleeping;
        Log("방석에서 잠듦");
    }

    /// <summary>바라보는 쪽 가장자리로 내려간다. 그쪽이 막혀 있으면 45도씩 돌려 가며 빈 곳을 찾는다.</summary>
    private void StartHopDown()
    {
        var center = m_Cushion.transform.position;
        var forward = Flat(transform.forward);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        float distance = m_Cushion.Radius + m_EdgeGap;

        var target = center + forward.normalized * distance;
        for (int i = 0; i < 8; i++)
        {
            float angle = ((i + 1) / 2) * 45f * (i % 2 == 0 ? 1f : -1f);
            var p = center + Quaternion.Euler(0f, angle, 0f) * forward.normalized * distance;
            bool walkable = m_Wander == null || m_Wander.IsOnWalkableSurface(p);
            bool blocked = m_Wander != null && m_Wander.IsBlocked(p, m_Cushion.transform);
            if (walkable && blocked == false)
            {
                target = p;
                break;
            }
        }
        target.y = center.y;
        StartHop(target, Step.HopDown);
        Log("깨서 내려옴");
    }

    private void FinishHopDown()
    {
        m_Needs.HoldWanderOnWake = false;
        m_Needs.SleepRecoveryMultiplier = 1f;
        m_Step = Step.Idle;
        if (m_Cushion != null)
            m_Cushion.Release();
        if (m_Wander != null)
            m_Wander.Resume();
    }

    private void StartHop(Vector3 to, Step step)
    {
        m_HopFrom = transform.position;
        m_HopTo = to;
        m_HopT = 0f;
        m_Step = step;
        SoundBank.Play(Sfx.Hop, transform.position);
    }

    /// <summary>포물선으로 폴짝. 다 뛰었으면 true</summary>
    private bool UpdateHop()
    {
        m_HopT += m_HopTime > 0f ? Time.deltaTime / m_HopTime : 1f;
        float t = Mathf.Clamp01(m_HopT);
        var pos = Vector3.Lerp(m_HopFrom, m_HopTo, t);
        pos.y += m_HopHeight * Scale() * 4f * t * (1f - t);
        transform.position = pos;
        return t >= 1f;
    }

    private Vector3 CushionTop()
    {
        return m_Cushion.transform.position + Vector3.up * m_Cushion.TopHeight;
    }

    /// <summary>target 쪽으로 돈다 (수평). 다 돌았으면 true</summary>
    private bool TurnTowards(Vector3 target)
    {
        var dir = Flat(target - transform.position);
        if (dir.sqrMagnitude < 0.0001f)
            return true;
        var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, look) < 5f;
    }

    /// <summary>지금 고양이 몸의 수평 반지름 (렌더러 경계 상자 기준, 3 ~ 12cm)</summary>
    private float MeasureBodyRadius()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return 0.04f;
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.03f, 0.12f);
    }

    /// <summary>아기 크기(0.06) 대비 지금 크기</summary>
    private float Scale()
    {
        return transform.lossyScale.x / 0.06f;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatSleeping] {message}");
    }
}
