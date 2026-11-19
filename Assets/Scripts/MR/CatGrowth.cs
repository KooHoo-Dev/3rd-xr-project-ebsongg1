using System;
using UnityEngine;

/// <summary>성장 단계</summary>
public enum CatStage
{
    Baby,
    Child,
    Adult,
}

/// <summary>
/// 고양이 성장 게이지와 3단계 (아기 → 어린이 → 어른).
///
/// 게이지 오르는 규칙
/// - 기본: 배고프지 않고 기분이 나쁘지 않은 동안 시간에 따라 오른다. 단계마다 m_StageSeconds초면 가득 찬다.
///   (자는 동안에도 오른다. 배고프거나 기분이 나쁘면 멈춘다 → IsGrowing = false)
/// - 보너스: 필요할 때 돌봐 주면 게이지가 조금 더 오른다.
///   먹이는 배고플 때 줬을 때만, 쓰다듬기는 기분이 꽉 차 있지 않을 때만 (CatEating · CatPetting이 Care()를 부른다)
///   그래서 연타해도 빨라지지 않는다.
/// - m_SpeedMultiplier: 시연 영상용. 2면 두 배 빨리 큰다.
///
/// 단계가 오르면
/// - 고양이가 바쁘지 않을 때(자는 중 · 먹는 중 · 다른 동작 중이 아닐 때)까지 기다렸다가
///   크기를 m_StageScales로 천천히 키우고, Victory 동작 + 웃는 얼굴
/// - 걷는 속도도 크기에 맞춰 빨라진다
/// - StageChanged 이벤트
/// 어른 게이지까지 다 차면 IsComplete = true, Completed 이벤트 (엔딩 시작용)
///
/// 플레이 중 Inspector의 ⋮ 메뉴 "테스트: ..."로 바로 다음 단계로 넘길 수 있다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CatNeeds))]
public class CatGrowth : MonoBehaviour
{
    [Header("현재 값 (플레이 중 직접 바꿔 테스트 가능)")]
    [SerializeField] private CatStage m_Stage = CatStage.Baby;
    [SerializeField, Range(0f, 1f)] private float m_Progress = 0f;

    [Header("속도")]
    [SerializeField, Tooltip("한 단계 게이지가 가득 차는 데 걸리는 시간 (초). 돌봄 보너스 없이 상태만 좋게 유지했을 때")]
    private float m_StageSeconds = 180f;

    [SerializeField, Tooltip("성장 속도 배율. 시연 영상 빌드에서 2 ~ 3으로 올린다")]
    private float m_SpeedMultiplier = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("배고플 때 먹이를 주면 게이지가 이만큼 더 오른다 (0.05 = 5%)")]
    private float m_FeedBonus = 0.05f;

    [SerializeField, Range(0f, 1f), Tooltip("기분이 꽉 차 있지 않을 때 쓰다듬으면 게이지가 이만큼 더 오른다")]
    private float m_PetBonus = 0.05f;

    [SerializeField, Range(0f, 1f), Tooltip("졸릴 때 방석을 가져다줘서 재우면 게이지가 이만큼 더 오른다")]
    private float m_SleepBonus = 0.05f;

    [SerializeField, Range(0f, 1f), Tooltip("숨겨진 간식을 찾아서 가져다주면 게이지가 이만큼 더 오른다")]
    private float m_SnackBonus = 0.1f;

    [Header("단계")]
    [SerializeField, Tooltip("단계별 고양이 크기 (아기 · 어린이 · 어른)")]
    private float[] m_StageScales = { 0.06f, 0.08f, 0.1f };

    [SerializeField, Tooltip("단계가 오를 때 커지는 시간 (초)")]
    private float m_GrowTime = 1.2f;

    [SerializeField, Tooltip("단계가 오를 때 Victory 동작 동안 돌아다니기를 멈추는 시간 (초)")]
    private float m_CelebrateTime = 2.5f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private static readonly int s_VictoryHash = Animator.StringToHash("Victory");

    private CatNeeds m_Needs;
    private CatWander m_Wander;
    private CatEating m_Eating;
    private CatSleeping m_Sleeping;
    private CatFace m_Face;
    private Animator m_Animator;
    private float m_BaseWalkSpeed;
    private bool m_StageUpPending;
    private bool m_Complete;
    private float m_GrowTimer = -1f;
    private float m_GrowFrom;
    private float m_GrowTo;
    private float m_ResumeWanderAt = -1f;

    /// <summary>단계가 오를 때 (새 단계)</summary>
    public event Action<CatStage> StageChanged;

    /// <summary>어른 게이지까지 다 찼을 때 한 번</summary>
    public event Action Completed;

    public CatStage Stage => m_Stage;

    /// <summary>지금 단계 게이지 (0 ~ 1)</summary>
    public float Progress => m_Progress;

    /// <summary>지금 게이지가 오르고 있는지. 배고프거나 기분이 나쁘면 false</summary>
    public bool IsGrowing => m_Complete == false && m_StageUpPending == false && CanGrow();

    /// <summary>단계가 올라가는 중인지 (게이지는 찼고 고양이가 한가해지길 기다리거나 커지는 중)</summary>
    public bool IsStageUpPending => m_StageUpPending || m_GrowTimer >= 0f;

    /// <summary>어른 게이지까지 다 찼는지</summary>
    public bool IsComplete => m_Complete;

    /// <summary>시연 영상용 속도 배율. GameFlow 등에서 바꿀 수 있다.</summary>
    public float SpeedMultiplier
    {
        get => m_SpeedMultiplier;
        set => m_SpeedMultiplier = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        m_Needs = GetComponent<CatNeeds>();
        m_Wander = GetComponent<CatWander>();
        m_Eating = GetComponent<CatEating>();
        m_Sleeping = GetComponent<CatSleeping>();
        m_Face = GetComponent<CatFace>();
        m_Animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        // CatHousePlacer가 Instantiate 직후 크기를 정하므로 Start에서 맞춘다.
        if (m_Wander != null)
            m_BaseWalkSpeed = m_Wander.WalkSpeed / Mathf.Max(0.0001f, ScaleRatio(transform.localScale.x));
        ApplyScale(StageScale(m_Stage));
    }

    /// <summary>
    /// 돌봐 줬을 때 부른다. needed는 그 돌봄이 필요했는지 (배고플 때 먹이 · 기분이 꽉 차지 않았을 때 쓰다듬기).
    /// 필요했을 때만 게이지 보너스를 준다.
    /// </summary>
    public void Care(CareKind kind, bool needed)
    {
        if (needed == false || m_Complete || m_StageUpPending)
            return;
        float bonus = BonusOf(kind);
        AddProgress(bonus);
        Log($"돌봄 보너스 {kind} +{bonus:0.00} → {m_Progress:0.00}");
    }

    private float BonusOf(CareKind kind)
    {
        switch (kind)
        {
            case CareKind.Feed: return m_FeedBonus;
            case CareKind.Pet: return m_PetBonus;
            case CareKind.Sleep: return m_SleepBonus;
            case CareKind.Snack: return m_SnackBonus;
            default: return 0f;
        }
    }

    private void Update()
    {
        if (m_Complete == false && m_StageUpPending == false && CanGrow() && m_StageSeconds > 0f)
            AddProgress(Time.deltaTime * m_SpeedMultiplier / m_StageSeconds);

        if (m_StageUpPending && IsCatFree())
            StartStageUp();

        UpdateGrowAnimation();
        UpdateCelebration();
    }

    private bool CanGrow()
    {
        return m_Needs.IsHungry == false && m_Needs.IsSad == false;
    }

    private void AddProgress(float amount)
    {
        m_Progress = Mathf.Clamp01(m_Progress + amount);
        if (m_Progress < 1f)
            return;

        if (m_Stage == CatStage.Adult)
        {
            m_Complete = true;
            Log("다 자람!");
            Completed?.Invoke();
        }
        else
        {
            m_StageUpPending = true;
        }
    }

    /// <summary>자는 중 · 먹는 중 · 다른 동작 중(돌아다니기 멈춤)이 아닐 때</summary>
    private bool IsCatFree()
    {
        if (m_Needs.IsSleeping)
            return false;
        if (m_Eating != null && m_Eating.IsBusy)
            return false;
        if (m_Sleeping != null && m_Sleeping.IsBusy)
            return false;
        if (m_Wander != null && m_Wander.IsPaused)
            return false;
        return true;
    }

    private void StartStageUp()
    {
        m_StageUpPending = false;
        m_Stage = m_Stage + 1;
        m_Progress = 0f;

        m_GrowFrom = transform.localScale.x;
        m_GrowTo = StageScale(m_Stage);
        m_GrowTimer = 0f;

        if (m_Animator != null)
            m_Animator.SetTrigger(s_VictoryHash);
        if (m_Face != null)
            m_Face.Show(CatExpression.Excited, m_CelebrateTime);
        if (m_Wander != null)
        {
            m_Wander.Pause();
            m_ResumeWanderAt = Time.time + m_CelebrateTime;
        }

        Log($"성장! → {m_Stage}");
        StageChanged?.Invoke(m_Stage);
    }

    private void UpdateGrowAnimation()
    {
        if (m_GrowTimer < 0f)
            return;
        m_GrowTimer += Time.deltaTime;
        float t = m_GrowTime > 0f ? Mathf.Clamp01(m_GrowTimer / m_GrowTime) : 1f;
        // 살짝 넘쳤다가 돌아오는 느낌 (뿅)
        float eased = 1f + 1.6f * Mathf.Pow(t - 1f, 3f) + 0.6f * Mathf.Pow(t - 1f, 2f);
        ApplyScale(Mathf.LerpUnclamped(m_GrowFrom, m_GrowTo, eased));
        if (t >= 1f)
        {
            ApplyScale(m_GrowTo);
            m_GrowTimer = -1f;
        }
    }

    private void UpdateCelebration()
    {
        if (m_ResumeWanderAt < 0f || Time.time < m_ResumeWanderAt)
            return;
        m_ResumeWanderAt = -1f;
        if (m_Wander != null && m_Needs.IsSleeping == false)
            m_Wander.Resume();
    }

    private void ApplyScale(float scale)
    {
        transform.localScale = Vector3.one * scale;
        if (m_Wander != null && m_BaseWalkSpeed > 0f)
            m_Wander.WalkSpeed = m_BaseWalkSpeed * ScaleRatio(scale);
    }

    private float StageScale(CatStage stage)
    {
        int i = (int)stage;
        if (m_StageScales == null || m_StageScales.Length == 0)
            return transform.localScale.x;
        return m_StageScales[Mathf.Clamp(i, 0, m_StageScales.Length - 1)];
    }

    /// <summary>아기 크기 대비 비율</summary>
    private float ScaleRatio(float scale)
    {
        float baby = StageScale(CatStage.Baby);
        return baby > 0f ? scale / baby : 1f;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatGrowth] {message}");
    }

    [ContextMenu("테스트: 게이지 가득 채우기")]
    private void TestFillGauge()
    {
        AddProgress(1f);
    }

    [ContextMenu("테스트: 게이지 90%로")]
    private void TestNearlyFull()
    {
        if (m_Complete == false && m_StageUpPending == false)
            m_Progress = 0.9f;
    }
}

/// <summary>돌봄 종류 (CatGrowth.Care에 넘긴다)</summary>
public enum CareKind
{
    Feed,
    Pet,
    Sleep,
    Snack,
}
