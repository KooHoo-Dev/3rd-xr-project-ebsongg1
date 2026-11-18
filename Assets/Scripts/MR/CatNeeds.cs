using System;
using UnityEngine;

/// <summary>
/// 고양이 상태값 3개 (모두 0 ~ 1)
/// - 배고픔 Hunger: 시간이 지나면 오른다. 먹이를 주면 내려간다. (1 = 아주 배고픔)
/// - 기분 Mood: 시간이 지나면 조금씩 내려가고, 배고프면 더 빨리 내려간다. 쓰다듬기 · 먹이로 오른다. (1 = 아주 좋음)
/// - 졸림 Sleepiness: 깨어 있으면 오르고, 자는 동안 내려간다. (1 = 아주 졸림)
///
/// 재우기
/// - 졸릴 때 플레이어가 방석을 가져다주면 CatSleeping이 방석 위로 올라가 FallAsleep()을 부른다.
///   방석 위에서는 SleepRecoveryMultiplier만큼 빨리 개운해진다.
/// - 아무도 재워 주지 않아 졸림이 m_FallAsleepAt 이상이 되면 그 자리에서 쓰러지듯 잠들고 기분이 조금 떨어진다.
/// 졸림이 m_WakeUpAt 이하로 내려가면 깨어나서 다시 돌아다닌다.
/// (HoldWanderOnWake가 켜져 있으면 깰 때 돌아다니기를 다시 시작하지 않는다. 방석에서 내려오는 건 CatSleeping이 맡는다)
///
/// 상태에 따라 CatFace의 기본 표정을 정한다. (졸림 → 19, 배고픔 · 기분 낮음 → 21, 기분 좋음 → 06, 그 외 → 03)
///
/// 플레이 중 Inspector에서 값을 직접 바꾸거나, 컴포넌트 ⋮ 메뉴의 "테스트: ..."로 먹이 · 쓰다듬기를 흉내 낼 수 있다.
/// </summary>
[DisallowMultipleComponent]
public class CatNeeds : MonoBehaviour
{
    [Header("현재 값 (플레이 중 직접 바꿔 테스트 가능)")]
    [SerializeField, Range(0f, 1f)] private float m_Hunger = 0.2f;
    [SerializeField, Range(0f, 1f)] private float m_Mood = 0.7f;
    [SerializeField, Range(0f, 1f)] private float m_Sleepiness = 0.1f;

    [Header("시간에 따른 변화 (초당)")]
    [SerializeField, Tooltip("배고픔 증가. 0.01이면 약 100초 만에 0 → 1")]
    private float m_HungerRise = 0.01f;

    [SerializeField, Tooltip("기분 감소")]
    private float m_MoodDecay = 0.004f;

    [SerializeField, Tooltip("배고픔이 m_HungryAt 이상일 때 기분 추가 감소")]
    private float m_HungryMoodDecay = 0.01f;

    [SerializeField, Tooltip("깨어 있을 때 졸림 증가")]
    private float m_SleepinessRise = 0.006f;

    [SerializeField, Tooltip("자는 동안 졸림 감소")]
    private float m_SleepRecovery = 0.05f;

    [SerializeField, Range(0f, 1f), Tooltip("자는 동안 배고픔이 오르는 속도 배율")]
    private float m_SleepHungerScale = 0.5f;

    [Header("기준값")]
    [SerializeField, Range(0f, 1f)] private float m_HungryAt = 0.7f;
    [SerializeField, Range(0f, 1f)] private float m_SadMoodAt = 0.3f;
    [SerializeField, Range(0f, 1f)] private float m_HappyMoodAt = 0.75f;
    [SerializeField, Range(0f, 1f), Tooltip("기분이 이 이상이면 꽉 찬 것으로 본다 (이때 쓰다듬으면 성장 보너스 없음)")]
    private float m_FullMoodAt = 0.95f;
    [SerializeField, Range(0f, 1f)] private float m_SleepyAt = 0.6f;
    [SerializeField, Range(0f, 1f)] private float m_FallAsleepAt = 0.9f;
    [SerializeField, Range(0f, 1f)] private float m_WakeUpAt = 0.1f;

    [Header("돌봄 효과")]
    [SerializeField, Tooltip("먹이 한 번에 배고픔 감소")]
    private float m_FeedHunger = 0.5f;

    [SerializeField, Tooltip("먹이 한 번에 기분 증가")]
    private float m_FeedMood = 0.1f;

    [SerializeField, Tooltip("쓰다듬기 한 번에 기분 증가")]
    private float m_PetMood = 0.15f;

    [SerializeField, Tooltip("재워 주지 않아 쓰러지듯 잠들 때 기분 감소")]
    private float m_ExhaustedMoodPenalty = 0.15f;

    private static readonly int s_SleepingHash = Animator.StringToHash("Sleeping");

    private Animator m_Animator;
    private CatWander m_Wander;
    private CatFace m_Face;
    private CatEating m_Eating;
    private CatSleeping m_Sleeping;
    private bool m_IsSleeping;
    private bool m_PausedWanderForSleep;

    /// <summary>값이 바뀔 때마다 (매 프레임 포함) 불린다. 머리 위 아이콘 등 UI용.</summary>
    public event Action<CatNeeds> Changed;

    /// <summary>잠들거나 깰 때 불린다. true = 잠듦</summary>
    public event Action<bool> SleepChanged;

    public float Hunger => m_Hunger;
    public float Mood => m_Mood;
    public float Sleepiness => m_Sleepiness;
    public bool IsSleeping => m_IsSleeping;
    public bool IsHungry => m_Hunger >= m_HungryAt;
    public bool IsSad => m_Mood <= m_SadMoodAt;
    public bool IsSleepy => m_Sleepiness >= m_SleepyAt;
    public bool IsMoodFull => m_Mood >= m_FullMoodAt;

    /// <summary>자는 동안 졸림이 내려가는 속도 배율. 방석 위에서 자면 CatSleeping이 올린다.</summary>
    public float SleepRecoveryMultiplier { get; set; } = 1f;

    /// <summary>true면 깰 때 돌아다니기를 다시 시작하지 않는다. (방석에서 내려오는 동작을 CatSleeping이 맡을 때)</summary>
    public bool HoldWanderOnWake { get; set; }

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Wander = GetComponent<CatWander>();
        m_Face = GetComponent<CatFace>();
        m_Eating = GetComponent<CatEating>();
        m_Sleeping = GetComponent<CatSleeping>();
    }

    /// <summary>지금 바로 잠든다. (방석 위에 올라간 뒤 CatSleeping이 부른다)</summary>
    public void FallAsleep()
    {
        if (m_IsSleeping == false)
            SetSleeping(true);
    }

    private void Update()
    {
        Tick(Time.deltaTime);
        UpdateSleep();
        UpdateFace();
        Changed?.Invoke(this);
    }

    /// <summary>먹이를 먹었을 때. amount는 배고픔 감소량 (생략하면 기본값)</summary>
    public void Feed(float amount = -1f)
    {
        m_Hunger = Clamp(m_Hunger - (amount < 0f ? m_FeedHunger : amount));
        m_Mood = Clamp(m_Mood + m_FeedMood);
    }

    /// <summary>쓰다듬었을 때. amount는 기분 증가량 (생략하면 기본값)</summary>
    public void Pet(float amount = -1f)
    {
        m_Mood = Clamp(m_Mood + (amount < 0f ? m_PetMood : amount));
    }

    /// <summary>자고 있으면 깨운다. (졸림은 그대로라 곧 다시 잠들 수 있다)</summary>
    public void WakeUp()
    {
        if (m_IsSleeping)
            SetSleeping(false);
    }

    private void Tick(float dt)
    {
        float hungerRise = m_HungerRise * (m_IsSleeping ? m_SleepHungerScale : 1f);
        m_Hunger = Clamp(m_Hunger + hungerRise * dt);

        float moodDecay = m_MoodDecay + (IsHungry ? m_HungryMoodDecay : 0f);
        m_Mood = Clamp(m_Mood - moodDecay * dt);

        if (m_IsSleeping)
            m_Sleepiness = Clamp(m_Sleepiness - m_SleepRecovery * SleepRecoveryMultiplier * dt);
        else
            m_Sleepiness = Clamp(m_Sleepiness + m_SleepinessRise * dt);
    }

    private void UpdateSleep()
    {
        if (m_IsSleeping)
        {
            if (m_Sleepiness <= m_WakeUpAt)
                SetSleeping(false);
            return;
        }

        // 다른 일(먹기 · 방석으로 가기 등)로 CatWander가 멈춰 있거나 바쁠 때는 그 일이 끝날 때까지 기다린다.
        bool busy = (m_Wander != null && m_Wander.IsPaused)
            || (m_Eating != null && m_Eating.IsBusy)
            || (m_Sleeping != null && m_Sleeping.IsBusy);
        if (m_Sleepiness >= m_FallAsleepAt && busy == false)
        {
            // 아무도 재워 주지 않았다 → 그 자리에서 쓰러지듯 잠들고 기분이 조금 떨어진다.
            m_Mood = Clamp(m_Mood - m_ExhaustedMoodPenalty);
            SetSleeping(true);
        }
    }

    private void SetSleeping(bool sleeping)
    {
        m_IsSleeping = sleeping;
        if (m_Animator != null && m_Animator.runtimeAnimatorController != null)
            m_Animator.SetBool(s_SleepingHash, sleeping);

        if (m_Wander != null)
        {
            if (sleeping)
            {
                m_Wander.Pause();
                m_PausedWanderForSleep = true;
            }
            else if (m_PausedWanderForSleep)
            {
                if (HoldWanderOnWake == false)
                    m_Wander.Resume();
                m_PausedWanderForSleep = false;
            }
        }

        SleepChanged?.Invoke(sleeping);
    }

    private void UpdateFace()
    {
        if (m_Face == null)
            return;

        CatExpression expression;
        if (m_Sleepiness >= m_SleepyAt)
            expression = CatExpression.Sleepy;
        else if (IsHungry || m_Mood <= m_SadMoodAt)
            expression = CatExpression.Sad;
        else if (m_Mood >= m_HappyMoodAt)
            expression = CatExpression.Happy;
        else
            expression = CatExpression.Default;

        // 매 프레임 불러도 같은 표정이면 CatFace가 아무것도 하지 않는다.
        if (m_Face.BaseExpression != expression)
            m_Face.SetBaseExpression(expression);
    }

    private static float Clamp(float value) => Mathf.Clamp01(value);

    [ContextMenu("테스트: 먹이 주기")]
    private void TestFeed()
    {
        Feed();
        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, 2f);
    }

    [ContextMenu("테스트: 쓰다듬기")]
    private void TestPet()
    {
        Pet();
        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, 2f);
    }

    [ContextMenu("테스트: 바로 졸리게")]
    private void TestSleepy()
    {
        m_Sleepiness = m_FallAsleepAt;
    }
}
