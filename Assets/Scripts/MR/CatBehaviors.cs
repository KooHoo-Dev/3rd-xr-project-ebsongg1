using UnityEngine;

/// <summary>
/// 성장 단계에 따라 할 수 있는 행동이 늘어난다.
///
/// 아기   : 걷기 · 먹기 · 자기 + 쉬다가 가끔 엎드려 졸기 (Doze = Quadruped_Chiling)
/// 어린이 : + 인사 (플레이어가 다가오면 돌아보며 Hi) · 좋아하기 (쓰다듬으면 Cute1)
/// 어른   : + 땅 파기 (DigA) · 앉기 (Sit) · 기뻐하기 (먹은 뒤 · 쓰다듬을 때 Jump)
///
/// 쉬는 중(CatWander.IsResting)에 m_IdleActionInterval마다 지금 단계에서 할 수 있는 동작 중 하나를 고른다.
/// 동작하는 동안은 돌아다니기를 멈추고, 끝나면 다시 돌아다닌다.
/// 자는 중 · 먹는 중 · 단계가 오르는 중 · 다른 동작 중이면 하지 않는다.
///
/// 쓰다듬을 때 어떤 동작을 할지는 CatPetting이 PetReaction()으로 물어본다.
/// 먹고 난 뒤에는 CatEating이 OnFed()를 부른다.
///
/// 간식 찾기 (SnackHunt)
/// - 땅 파기(Dig)가 끝나면 Dug 이벤트 → SnackHunt가 바닥 어딘가에 간식을 숨긴다.
/// - 플레이어가 찾은 간식을 가져오면 SnackHunt가 TakeSnack()을 부른다.
///   플레이어 쪽으로 돌아서 받아먹고(Eat) 다 먹으면 콜백을 부른 뒤 기뻐한다(Joy).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CatNeeds))]
public class CatBehaviors : MonoBehaviour
{
    private enum Action { None, Doze, Hi, Dig, Sit, Joy, Snack }

    [Header("쉬다가 하는 동작")]
    [SerializeField, Tooltip("쉬는 중에 다른 동작을 할 간격 (최소 · 최대, 초)")]
    private Vector2 m_IdleActionInterval = new Vector2(8f, 15f);

    [Header("인사 (어린이부터)")]
    [SerializeField, Tooltip("플레이어가 이 거리보다 멀리 있다가 (m)")]
    private float m_GreetFarDistance = 1.0f;

    [SerializeField, Tooltip("이 거리 안으로 다가오면 인사한다 (m)")]
    private float m_GreetNearDistance = 0.6f;

    [SerializeField, Tooltip("인사한 뒤 다시 인사하기까지 (초)")]
    private float m_GreetCooldown = 20f;

    [Header("기뻐하기 (어른)")]
    [SerializeField, Range(0f, 1f), Tooltip("어른이 쓰다듬어질 때 Cute1 대신 기뻐하기(Jump)를 할 확률")]
    private float m_AdultPetJoyChance = 0.5f;

    [Header("공통")]
    [SerializeField, Tooltip("플레이어 쪽으로 도는 속도 (도/초)")]
    private float m_TurnSpeed = 240f;

    [SerializeField, Tooltip("클립 길이를 못 찾았을 때 동작 시간 (초)")]
    private float m_DefaultActionTime = 2f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    // 동작 → Animator 트리거 · 클립 이름 (CatAnimatorBuilder와 같은 이름)
    private static readonly int s_DozeHash = Animator.StringToHash("Doze");
    private static readonly int s_HiHash = Animator.StringToHash("Hi");
    private static readonly int s_DigHash = Animator.StringToHash("Dig");
    private static readonly int s_SitHash = Animator.StringToHash("Sit");
    private static readonly int s_JoyHash = Animator.StringToHash("Joy");
    private static readonly int s_PetHash = Animator.StringToHash("Pet");
    private static readonly int s_EatHash = Animator.StringToHash("Eat");
    private const string k_EatStateName = "Eat";

    private Animator m_Animator;
    private CatNeeds m_Needs;
    private CatWander m_Wander;
    private CatEating m_Eating;
    private CatGrowth m_Growth;
    private CatSleeping m_Sleeping;
    private CatFace m_Face;

    private Action m_Current = Action.None;
    private bool m_Turning;
    private float m_ActionTimer;
    private float m_NextIdleAction;
    private float m_NextGreet;
    private bool m_PlayerWasFar = true;
    private bool m_PendingJoy;
    private float m_SnackEatTime;
    private System.Action m_OnSnackEaten;

    /// <summary>땅 파기(Dig) 동작이 끝났을 때. SnackHunt가 간식을 숨긴다.</summary>
    public event System.Action Dug;

    /// <summary>동작 중인지 (도는 중 포함)</summary>
    public bool IsBusy => m_Current != Action.None;

    /// <summary>지금 간식을 받아먹을 수 있는지 (자는 중 · 먹는 중 · 다른 동작 중이 아닐 때)</summary>
    public bool CanTakeSnack => m_Current == Action.None && CanAct();

    private CatStage Stage => m_Growth != null ? m_Growth.Stage : CatStage.Baby;

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Needs = GetComponent<CatNeeds>();
        m_Wander = GetComponent<CatWander>();
        m_Eating = GetComponent<CatEating>();
        m_Growth = GetComponent<CatGrowth>();
        m_Sleeping = GetComponent<CatSleeping>();
        m_Face = GetComponent<CatFace>();
        ScheduleIdleAction();
    }

    /// <summary>
    /// 쓰다듬었을 때 재생할 트리거. 0이면 동작 없이 얼굴 · 진동만.
    /// 아기: 없음 / 어린이: 좋아하기(Cute1) / 어른: 좋아하기 또는 기뻐하기(Jump)
    /// </summary>
    public int PetReaction()
    {
        switch (Stage)
        {
            case CatStage.Baby:
                return 0;
            case CatStage.Child:
                return s_PetHash;
            default:
                return Random.value < m_AdultPetJoyChance ? s_JoyHash : s_PetHash;
        }
    }

    /// <summary>먹고 난 직후 CatEating이 부른다. 어른이면 기뻐한다.</summary>
    public void OnFed()
    {
        if (Stage == CatStage.Adult)
            m_PendingJoy = true;
    }

    /// <summary>
    /// 플레이어가 가져온 간식을 받아먹는다. 플레이어 쪽으로 돌고 eatTime 동안 먹은 뒤 onEaten을 부르고 기뻐한다.
    /// CanTakeSnack이 false면 아무것도 하지 않고 false를 돌려준다.
    /// </summary>
    public bool TakeSnack(float eatTime, System.Action onEaten)
    {
        if (CanTakeSnack == false)
            return false;
        m_SnackEatTime = eatTime;
        m_OnSnackEaten = onEaten;
        StartAction(Action.Snack, true);
        return true;
    }

    private void Update()
    {
        if (m_Current != Action.None)
        {
            UpdateAction();
            return;
        }

        if (CanAct() == false)
            return;

        if (m_PendingJoy)
        {
            m_PendingJoy = false;
            StartAction(Action.Joy, false);
            return;
        }

        if (TryGreet())
            return;

        if (m_Wander != null && m_Wander.IsResting && Time.time >= m_NextIdleAction)
        {
            ScheduleIdleAction();
            var action = PickIdleAction();
            if (action != Action.None)
                StartAction(action, action == Action.Hi);
        }
    }

    private bool CanAct()
    {
        if (m_Needs.IsSleeping)
            return false;
        if (m_Eating != null && m_Eating.IsBusy)
            return false;
        if (m_Sleeping != null && m_Sleeping.IsBusy)
            return false;
        if (m_Growth != null && m_Growth.IsStageUpPending)
            return false;
        // 다른 일(쓰다듬기 반응 등)로 멈춰 있으면 끼어들지 않는다.
        if (m_Wander != null && m_Wander.IsPaused)
            return false;
        return true;
    }

    /// <summary>어린이부터: 플레이어가 멀리 있다가 가까이 오면 돌아보며 인사</summary>
    private bool TryGreet()
    {
        var cam = Camera.main;
        if (cam == null)
            return false;
        var offset = cam.transform.position - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;

        bool greet = false;
        if (m_PlayerWasFar && distance < m_GreetNearDistance)
        {
            m_PlayerWasFar = false;
            greet = Stage >= CatStage.Child && Time.time >= m_NextGreet;
        }
        else if (distance > m_GreetFarDistance)
        {
            m_PlayerWasFar = true;
        }

        if (greet == false)
            return false;
        m_NextGreet = Time.time + m_GreetCooldown;
        StartAction(Action.Hi, true);
        return true;
    }

    /// <summary>지금 단계에서 쉬다가 할 수 있는 동작 중 하나 (위에 있는 것일수록 자주)</summary>
    private Action PickIdleAction()
    {
        float r = Random.value;
        switch (Stage)
        {
            case CatStage.Baby:
                return Action.Doze;
            case CatStage.Child:
                return r < 0.6f ? Action.Doze : Action.Hi;
            default:
                if (r < 0.35f) return Action.Dig;
                if (r < 0.6f) return Action.Sit;
                if (r < 0.8f) return Action.Hi;
                return Action.Doze;
        }
    }

    private void StartAction(Action action, bool facePlayer)
    {
        m_Current = action;
        m_Turning = facePlayer;
        if (m_Wander != null)
            m_Wander.Pause();
        if (m_Turning == false)
            PlayAnimation();
        Log($"{action} 시작 (단계 {Stage})");
    }

    private void UpdateAction()
    {
        if (m_Turning)
        {
            if (TurnToPlayer())
            {
                m_Turning = false;
                PlayAnimation();
            }
            return;
        }

        m_ActionTimer -= Time.deltaTime;

        // 먹는 동작이 먹는 시간보다 짧으면 끝날 때마다 다시 재생한다. (CatEating과 같은 방식)
        if (m_Current == Action.Snack && m_Animator != null && m_ActionTimer > 0.5f
            && m_Animator.IsInTransition(0) == false
            && m_Animator.GetCurrentAnimatorStateInfo(0).IsName(k_EatStateName) == false)
            m_Animator.SetTrigger(s_EatHash);

        if (m_ActionTimer > 0f)
            return;

        var finished = m_Current;
        Log($"{finished} 끝");
        m_Current = Action.None;
        if (m_Wander != null && m_Needs.IsSleeping == false)
            m_Wander.Resume();

        if (finished == Action.Dig)
        {
            Dug?.Invoke();
        }
        else if (finished == Action.Snack)
        {
            if (m_Animator != null)
                m_Animator.ResetTrigger(s_EatHash);
            var onEaten = m_OnSnackEaten;
            m_OnSnackEaten = null;
            onEaten?.Invoke();
            m_PendingJoy = true;
        }
    }

    private void PlayAnimation()
    {
        var (hash, clip) = AnimationOf(m_Current);
        m_ActionTimer = ClipLength(clip);
        if (m_Current == Action.Snack)
            m_ActionTimer = Mathf.Max(m_ActionTimer, m_SnackEatTime);
        if (m_Animator != null && m_Animator.runtimeAnimatorController != null)
            m_Animator.SetTrigger(hash);
        PlayActionSound();

        if (m_Face == null)
            return;
        switch (m_Current)
        {
            case Action.Hi:
            case Action.Joy:
            case Action.Snack:
                m_Face.Show(CatExpression.Happy, m_ActionTimer);
                break;
            case Action.Doze:
                m_Face.Show(CatExpression.Sleepy, m_ActionTimer);
                break;
            case Action.Dig:
                m_Face.Show(CatExpression.Excited, m_ActionTimer);
                break;
        }
    }

    private void PlayActionSound()
    {
        switch (m_Current)
        {
            case Action.Hi:
                SoundBank.Play(Sfx.Meow, transform.position);
                break;
            case Action.Dig:
                SoundBank.Play(Sfx.Dig, transform.position);
                break;
            case Action.Joy:
                SoundBank.Play(Sfx.Joy, transform.position);
                break;
            case Action.Snack:
                SoundBank.Play(Sfx.Eat, transform.position);
                break;
        }
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

    private static (int hash, string clip) AnimationOf(Action action)
    {
        switch (action)
        {
            case Action.Doze: return (s_DozeHash, "Quadruped_Chiling");
            case Action.Hi: return (s_HiHash, "Hi");
            case Action.Dig: return (s_DigHash, "DigA");
            case Action.Sit: return (s_SitHash, "Sit");
            case Action.Joy: return (s_JoyHash, "Jump");
            case Action.Snack: return (s_EatHash, "Quadruped_Eating");
            default: return (0, null);
        }
    }

    /// <summary>Animator Controller에 들어 있는 클립 길이. 동작이 끝나고 돌아오는 시간까지 조금 더한다.</summary>
    private float ClipLength(string clipName)
    {
        if (m_Animator == null || m_Animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clipName))
            return m_DefaultActionTime;
        foreach (var clip in m_Animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name.EndsWith(clipName))
                return clip.length + 0.3f;
        }
        return m_DefaultActionTime;
    }

    private void ScheduleIdleAction()
    {
        m_NextIdleAction = Time.time + Random.Range(m_IdleActionInterval.x, m_IdleActionInterval.y);
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[CatBehaviors] {message}");
    }
}
