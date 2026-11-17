using System;
using UnityEngine;

/// <summary>
/// 고양이 상태값 3개 (모두 0 ~ 1)
/// - 배고픔 Hunger: 시간이 지나면 오른다. 먹이를 주면 내려간다. (1 = 아주 배고픔)
/// - 기분 Mood: 시간이 지나면 조금씩 내려가고, 배고프면 더 빨리 내려간다. 쓰다듬기 · 먹이로 오른다. (1 = 아주 좋음)
/// - 졸림 Sleepiness: 깨어 있으면 오르고, 자는 동안 내려간다. (1 = 아주 졸림)
///
/// 졸림이 m_FallAsleepAt 이상이 되면 CatWander를 멈추고 잠든다 (Animator Sleeping = true → Sleep 동작 · 잠자는 얼굴).
/// 졸림이 m_WakeUpAt 이하로 내려가면 깨어나서 다시 돌아다닌다.
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

    private static readonly int s_SleepingHash = Animator.StringToHash("Sleeping");

    private Animator m_Animator;
    private CatWander m_Wander;
    private CatFace m_Face;
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

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Wander = GetComponent<CatWander>();
        m_Face = GetComponent<CatFace>();
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
            m_Sleepiness = Clamp(m_Sleepiness - m_SleepRecovery * dt);
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

        // 다른 일(먹기 등)로 CatWander가 멈춰 있을 때는 그 일이 끝날 때까지 기다린다.
        bool busy = m_Wander != null && m_Wander.IsPaused;
        if (m_Sleepiness >= m_FallAsleepAt && busy == false)
            SetSleeping(true);
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
