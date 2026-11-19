using System;
using UnityEngine;

/// <summary>
/// 엔딩 연출: 탐험가 차림 → 플레이어에게 인사 → 책상 가장자리로 걸어가서 사라지기
///
/// GameFlow가 고양이가 다 자란 뒤 Play()를 부른다.
/// 1. 기다리기 : 먹는 중 · 다른 동작 중 · 단계가 오르는 중이면 끝날 때까지. 자고 있으면 깨운다
/// 2. 정리     : 상태값 · 쓰다듬기 · 먹기 · 단계별 행동 · 머리 위 아이콘을 끈다
/// 3. 차려입기 : 모자 · 가방(m_Props)이 뿅 나타나고 Victory 동작
/// 4. 인사     : 플레이어 쪽으로 돌아서 Hi
/// 5. 떠나기   : 플레이어 반대쪽으로, 집 · 밥그릇 · 먹이 상자를 피한 방향을 골라 책상 가장자리까지 걷는다
/// 6. 사라지기 : 가장자리에서 한 번 돌아보고 점점 작아지며 사라진다 → onFinished
///
/// 소품은 CatAnimatorBuilder가 뼈(Head_equipment · Body_equipment)에 미리 붙여 꺼 둔다.
/// </summary>
[DisallowMultipleComponent]
public class CatEnding : MonoBehaviour
{
    private enum Step { None, Waiting, Dressing, TurningToWave, Waving, Walking, LookingBack, Vanishing, Done }

    [SerializeField, Tooltip("엔딩 때 나타날 소품 (모자 · 가방). 처음에는 꺼 둔다")]
    private GameObject[] m_Props;

    [SerializeField, Tooltip("소품이 뿅 나타나는 시간 (초)")]
    private float m_PropPopTime = 0.5f;

    [SerializeField, Tooltip("떠날 때 가장 멀리 걸어가는 거리 (m). 가장자리가 이보다 멀면 여기서 사라진다")]
    private float m_MaxLeaveDistance = 0.45f;

    [SerializeField, Tooltip("가장자리에서 이만큼 안쪽에서 멈춘다 (m)")]
    private float m_EdgeMargin = 0.04f;

    [SerializeField, Tooltip("사라지는 시간 (초)")]
    private float m_VanishTime = 1f;

    [SerializeField, Tooltip("플레이어 쪽으로 도는 속도 (도/초)")]
    private float m_TurnSpeed = 240f;

    [SerializeField, Tooltip("클립 길이를 못 찾았을 때 동작 시간 (초)")]
    private float m_DefaultActionTime = 2f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private static readonly int s_VictoryHash = Animator.StringToHash("Victory");
    private static readonly int s_HiHash = Animator.StringToHash("Hi");

    private Animator m_Animator;
    private CatNeeds m_Needs;
    private CatWander m_Wander;
    private CatEating m_Eating;
    private CatBehaviors m_Behaviors;
    private CatGrowth m_Growth;
    private CatSleeping m_Sleeping;
    private CatFace m_Face;

    private Step m_Step = Step.None;
    private float m_Timer;
    private float m_StepTime;
    private Vector3[] m_PropScales;
    private Vector3 m_StartScale;
    private Action m_OnFinished;

    /// <summary>엔딩이 진행 중인지</summary>
    public bool IsPlaying => m_Step != Step.None && m_Step != Step.Done;

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Needs = GetComponent<CatNeeds>();
        m_Wander = GetComponent<CatWander>();
        m_Eating = GetComponent<CatEating>();
        m_Behaviors = GetComponent<CatBehaviors>();
        m_Growth = GetComponent<CatGrowth>();
        m_Sleeping = GetComponent<CatSleeping>();
        m_Face = GetComponent<CatFace>();
    }

    /// <summary>엔딩 시작. 다 끝나면 (고양이가 사라지면) onFinished가 불린다.</summary>
    public void Play(Action onFinished)
    {
        if (IsPlaying)
            return;
        m_OnFinished = onFinished;
        m_Step = Step.Waiting;
        Log("엔딩 시작 (고양이가 한가해지길 기다림)");
    }

    private void Update()
    {
        switch (m_Step)
        {
            case Step.Waiting:
                if (IsCatFree())
                    StartDressing();
                break;

            case Step.Dressing:
                UpdateDressing();
                break;

            case Step.TurningToWave:
                if (TurnToPlayer())
                    StartWaving();
                break;

            case Step.Waving:
                m_Timer -= Time.deltaTime;
                if (m_Timer <= 0f)
                    StartWalking();
                break;

            case Step.LookingBack:
                if (TurnToPlayer())
                {
                    m_Timer -= Time.deltaTime;
                    if (m_Timer <= 0f)
                        StartVanishing();
                }
                break;

            case Step.Vanishing:
                UpdateVanishing();
                break;
        }
    }

    private bool IsCatFree()
    {
        if (m_Needs != null && m_Needs.IsSleeping)
            m_Needs.WakeUp();
        if (m_Eating != null && m_Eating.IsBusy)
            return false;
        if (m_Behaviors != null && m_Behaviors.IsBusy)
            return false;
        // 방석 위에서 깨웠으면 내려올 때까지 기다린다.
        if (m_Sleeping != null && m_Sleeping.IsBusy)
            return false;
        if (m_Growth != null && m_Growth.IsStageUpPending)
            return false;
        return true;
    }

    /// <summary>다른 기능을 끄고 소품을 뿅 나타나게 한다.</summary>
    private void StartDressing()
    {
        // 엔딩 동안 다른 기능이 끼어들지 않게 끈다. (CatNeedsIcons는 꺼지면 아이콘 묶음을 숨긴다)
        DisableIfPresent<CatNeeds>();
        DisableIfPresent<CatPetting>();
        DisableIfPresent<CatEating>();
        DisableIfPresent<CatBehaviors>();
        DisableIfPresent<CatSleeping>();
        DisableIfPresent<CatGrowth>();
        DisableIfPresent<CatNeedsIcons>();

        if (m_Wander != null)
            m_Wander.Pause();

        m_PropScales = new Vector3[m_Props != null ? m_Props.Length : 0];
        for (int i = 0; i < m_PropScales.Length; i++)
        {
            if (m_Props[i] == null)
                continue;
            m_PropScales[i] = m_Props[i].transform.localScale;
            m_Props[i].transform.localScale = Vector3.zero;
            m_Props[i].SetActive(true);
        }

        if (m_Animator != null)
            m_Animator.SetTrigger(s_VictoryHash);
        m_StepTime = 0f;
        m_Timer = Mathf.Max(ClipLength("Victory"), m_PropPopTime);
        if (m_Face != null)
            m_Face.Show(CatExpression.Excited, m_Timer);
        m_Step = Step.Dressing;
        Log("탐험가 차림");
    }

    private void UpdateDressing()
    {
        m_StepTime += Time.deltaTime;
        float t = m_PropPopTime > 0f ? Mathf.Clamp01(m_StepTime / m_PropPopTime) : 1f;
        // 살짝 넘쳤다가 돌아오는 뿅
        float pop = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f);
        for (int i = 0; i < m_PropScales.Length; i++)
        {
            if (m_Props[i] != null)
                m_Props[i].transform.localScale = m_PropScales[i] * (t >= 1f ? 1f : pop);
        }

        m_Timer -= Time.deltaTime;
        if (m_Timer <= 0f)
            m_Step = Step.TurningToWave;
    }

    private void StartWaving()
    {
        if (m_Animator != null)
            m_Animator.SetTrigger(s_HiHash);
        SoundBank.Play(Sfx.Goodbye, transform.position);
        m_Timer = ClipLength("Hi");
        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, m_Timer);
        m_Step = Step.Waving;
        Log("안녕!");
    }

    private void StartWalking()
    {
        m_Step = Step.Walking;
        var target = PickLeavePoint();
        Log("떠나는 중");
        if (m_Wander == null)
        {
            OnArrivedAtEdge();
            return;
        }
        m_Wander.MoveTo(target, OnArrivedAtEdge);
    }

    private void OnArrivedAtEdge()
    {
        m_Step = Step.LookingBack;
        m_Timer = 0.6f; // 돌아본 채로 잠깐 멈췄다가 사라진다
    }

    private void StartVanishing()
    {
        m_StartScale = transform.localScale;
        m_StepTime = 0f;
        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, m_VanishTime + 1f);
        m_Step = Step.Vanishing;
    }

    private void UpdateVanishing()
    {
        m_StepTime += Time.deltaTime;
        float t = m_VanishTime > 0f ? Mathf.Clamp01(m_StepTime / m_VanishTime) : 1f;
        // 살짝 커졌다가 쏙 작아진다
        float k = t < 0.2f ? Mathf.Lerp(1f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 0f, (t - 0.2f) / 0.8f);
        transform.localScale = m_StartScale * k;
        if (t < 1f)
            return;

        m_Step = Step.Done;
        Log("사라짐");
        var callback = m_OnFinished;
        m_OnFinished = null;
        gameObject.SetActive(false);
        callback?.Invoke();
    }

    /// <summary>
    /// 떠날 곳 고르기: 플레이어 반대쪽을 기준으로 0, ±30, ±60, ±90도 방향을 차례로 보고,
    /// 걸어가는 길에 집 · 장애물이 없는 첫 방향에서 평면 가장자리(또는 최대 거리)까지 간다.
    /// </summary>
    private Vector3 PickLeavePoint()
    {
        var pos = transform.position;
        var away = -transform.forward; // 플레이어를 보고 있으니 등 뒤가 플레이어 반대쪽
        var cam = Camera.main;
        if (cam != null)
            away = pos - cam.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;

        float[] angles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
        Vector3 best = pos;
        float bestDistance = -1f;
        foreach (float angle in angles)
        {
            var dir = Quaternion.Euler(0f, angle, 0f) * away;
            float distance = WalkableDistance(pos, dir, out bool blocked);
            if (blocked == false && distance > 0.08f)
                return pos + dir * distance;
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = pos + dir * distance;
            }
        }
        return best;
    }

    /// <summary>dir 방향으로 1cm씩 가 보면서 평면 안인 거리를 잰다. 집 · 장애물에 닿으면 blocked</summary>
    private float WalkableDistance(Vector3 from, Vector3 dir, out bool blocked)
    {
        blocked = false;
        const float step = 0.01f;
        float distance = 0f;
        for (float d = step; d <= m_MaxLeaveDistance + m_EdgeMargin; d += step)
        {
            var p = from + dir * d;
            if (m_Wander != null && m_Wander.IsOnWalkableSurface(p) == false)
                break;
            if (m_Wander != null && d > 0.05f && m_Wander.IsBlocked(p))
            {
                blocked = true;
                break;
            }
            distance = d;
        }
        return Mathf.Clamp(distance - m_EdgeMargin, 0f, m_MaxLeaveDistance);
    }

    /// <summary>플레이어 쪽으로 돈다. 다 돌았으면 true</summary>
    private bool TurnToPlayer()
    {
        var cam = Camera.main;
        if (cam == null)
            return true;
        var toPlayer = cam.transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
            return true;
        var look = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, look) < 5f;
    }

    private void DisableIfPresent<T>() where T : Behaviour
    {
        var component = GetComponent<T>();
        if (component != null)
            component.enabled = false;
    }

    private float ClipLength(string clipName)
    {
        if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
            return m_DefaultActionTime;
        foreach (var clip in m_Animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name.EndsWith(clipName))
                return clip.length + 0.2f;
        }
        return m_DefaultActionTime;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatEnding] {message}");
    }
}
