using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// 컨트롤러로 고양이 머리를 쓰다듬으면 좋아한다.
///
/// 판정 방식
/// - 충돌체 대신 거리로 판정한다: 컨트롤러가 머리(head 뼈) 근처(m_PetRadius) 안에 있는 동안
///   움직인 거리를 모으고, 그 거리가 m_StrokeDistance를 넘으면 "한 번 쓰다듬음"으로 친다.
///   그냥 갖다 대고 가만히 있으면 쓰다듬기가 아니다. (먹이 줄 때 손이 스치는 것과 구분)
/// - 한 번 쓰다듬으면 m_Cooldown초 동안은 다시 반응하지 않는다. (연속 쓰다듬기 쿨타임)
///
/// 쓰다듬으면
/// - CatNeeds.Pet(): 기분 상승
/// - Animator 트리거 Pet (Cute1 동작) + 웃는 얼굴, 그 동안 돌아다니기 멈춤
/// - 컨트롤러 진동 (범위에 막 들어올 때도 약하게 한 번 진동해서 위치를 알려 준다)
/// - 자고 있을 때는 기분만 오르고 깨우지 않는다.
///
/// 컨트롤러는 씬의 XR Origin 아래 "Left Controller" · "Right Controller"를 자동으로 찾는다.
/// </summary>
[DisallowMultipleComponent]
public class CatPetting : MonoBehaviour
{
    [Header("판정")]
    [SerializeField, Tooltip("머리에서 이 거리 안에 컨트롤러가 있으면 쓰다듬는 중 (m). 고양이 크기 0.06 기준. 고양이 몸 전체를 덮을 만큼 넉넉하게")]
    private float m_PetRadius = 0.11f;

    [SerializeField, Tooltip("판정 중심을 머리 뼈에서 위로 올리는 높이 (m). 고양이 크기 0.06 기준. 책상에 부딪히지 않게 고양이 위쪽 공간을 판정")]
    private float m_HeadOffsetUp = 0.02f;

    [SerializeField, Tooltip("머리 근처에서 이만큼 움직이면 한 번 쓰다듬은 것 (m)")]
    private float m_StrokeDistance = 0.035f;

    [SerializeField, Tooltip("한 번 쓰다듬은 뒤 다시 반응하기까지 시간 (초)")]
    private float m_Cooldown = 2.5f;

    [SerializeField, Tooltip("고양이 크기가 커지면 판정 거리도 같이 키운다 (성장 단계 대비)")]
    private bool m_ScaleWithSize = true;

    [SerializeField, Tooltip("위 거리 값들의 기준이 되는 고양이 크기")]
    private float m_ReferenceScale = 0.06f;

    [Header("반응")]
    [SerializeField, Tooltip("좋아하는 동작(Cute1) 동안 돌아다니기를 멈추는 시간 (초)")]
    private float m_ReactionTime = 2f;

    [SerializeField, Range(0f, 1f)] private float m_HapticAmplitude = 0.3f;
    [SerializeField] private float m_HapticDuration = 0.1f;

    [SerializeField, Range(0f, 1f), Tooltip("손이 판정 범위에 들어왔을 때 알려 주는 약한 진동 세기 (0이면 끔)")]
    private float m_EnterHapticAmplitude = 0.12f;

    [SerializeField] private float m_EnterHapticDuration = 0.03f;

    [SerializeField, Tooltip("판정 범위를 Scene 뷰에 표시 (디버그용)")]
    private bool m_DrawGizmo = true;

    private static readonly int s_PetHash = Animator.StringToHash("Pet");

    private class Hand
    {
        public Transform transform;
        public HapticImpulsePlayer haptics;
        public Vector3 lastPosition;
        public bool wasInside;
    }

    private readonly List<Hand> m_Hands = new List<Hand>();
    private Transform m_Head;
    private Animator m_Animator;
    private CatNeeds m_Needs;
    private CatFace m_Face;
    private CatWander m_Wander;
    private CatEating m_Eating;
    private CatGrowth m_Growth;
    private CatBehaviors m_Behaviors;
    private CatSleeping m_Sleeping;
    private float m_Stroke;
    private float m_CooldownUntil;
    private float m_ResumeWanderAt = -1f;

    /// <summary>쓰다듬은 횟수 (결과 화면용)</summary>
    public int PetCount { get; private set; }

    private void Awake()
    {
        m_Animator = GetComponentInChildren<Animator>();
        m_Needs = GetComponent<CatNeeds>();
        m_Face = GetComponent<CatFace>();
        m_Wander = GetComponent<CatWander>();
        m_Eating = GetComponent<CatEating>();
        m_Growth = GetComponent<CatGrowth>();
        m_Behaviors = GetComponent<CatBehaviors>();
        m_Sleeping = GetComponent<CatSleeping>();
        m_Head = FindChild(transform, "head");
        if (m_Head == null)
        {
            Debug.LogWarning("[CatPetting] head 뼈를 찾지 못해 고양이 위치를 기준으로 판정해요.");
            m_Head = transform;
        }
    }

    private void Start()
    {
        FindHands();
    }

    private void Update()
    {
        if (m_Hands.Count == 0)
            FindHands();

        UpdateReaction();

        var center = HeadCenter();
        float radius = Scaled(m_PetRadius);
        bool anyInside = false;

        foreach (var hand in m_Hands)
        {
            if (hand.transform == null)
                continue;
            var pos = hand.transform.position;
            bool inside = (pos - center).sqrMagnitude < radius * radius;
            if (inside)
            {
                anyInside = true;
                // 범위에 막 들어오면 톡 하고 약하게 진동해서 "여기서 문지르면 된다"는 걸 알려 준다.
                if (hand.wasInside == false && hand.haptics != null && m_EnterHapticAmplitude > 0f)
                    hand.haptics.SendHapticImpulse(m_EnterHapticAmplitude, m_EnterHapticDuration);
                // 들어온 첫 프레임은 이동 거리를 세지 않는다 (밖에서 휙 들어온 거리는 쓰다듬기가 아님)
                if (hand.wasInside)
                    m_Stroke += (pos - hand.lastPosition).magnitude;
                if (m_Stroke >= Scaled(m_StrokeDistance) && Time.time >= m_CooldownUntil)
                    PetOnce(hand);
            }
            hand.wasInside = inside;
            hand.lastPosition = pos;
        }

        if (anyInside == false || Time.time < m_CooldownUntil)
            m_Stroke = 0f;
    }

    private void PetOnce(Hand hand)
    {
        m_Stroke = 0f;
        m_CooldownUntil = Time.time + m_Cooldown;
        PetCount++;

        // 기분이 꽉 차 있지 않을 때 쓰다듬었으면 성장 보너스 (연타로 빨리 크지 않게)
        if (m_Growth != null)
            m_Growth.Care(CareKind.Pet, m_Needs != null && m_Needs.IsMoodFull == false);
        if (m_Needs != null)
            m_Needs.Pet();
        if (hand.haptics != null)
            hand.haptics.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration);

        // 자고 있으면 기분만 오르고 동작 · 표정은 그대로 (잠자는 얼굴이 우선)
        if (m_Needs != null && m_Needs.IsSleeping)
            return;

        // 먹으러 가거나 먹는 중 · 다른 동작(인사 · 땅 파기 등) 중이면 웃는 얼굴만 보여 주고, 동작 · 돌아다니기는 건드리지 않는다.
        if ((m_Eating != null && m_Eating.IsBusy) || (m_Behaviors != null && m_Behaviors.IsBusy)
            || (m_Sleeping != null && m_Sleeping.IsBusy))
        {
            if (m_Face != null)
                m_Face.Show(CatExpression.Happy, m_ReactionTime);
            return;
        }

        if (m_Face != null)
            m_Face.Show(CatExpression.Happy, m_ReactionTime);

        // 단계별 반응: 아기는 동작 없음(얼굴 · 진동만), 어린이는 좋아하기, 어른은 좋아하기 또는 기뻐하기
        int reaction = m_Behaviors != null ? m_Behaviors.PetReaction() : s_PetHash;
        if (m_Animator != null && reaction != 0)
            m_Animator.SetTrigger(reaction);

        // 돌아다니던 중이면 좋아하는 동안 멈춘다. (다른 일로 이미 멈춰 있으면 건드리지 않는다)
        if (m_Wander != null && (m_Wander.IsPaused == false || m_ResumeWanderAt > 0f))
        {
            m_Wander.Pause();
            m_ResumeWanderAt = Time.time + m_ReactionTime;
        }
    }

    private void UpdateReaction()
    {
        if (m_ResumeWanderAt < 0f || Time.time < m_ResumeWanderAt)
            return;
        m_ResumeWanderAt = -1f;
        if (m_Wander != null && (m_Needs == null || m_Needs.IsSleeping == false))
            m_Wander.Resume();
    }

    private Vector3 HeadCenter()
    {
        return m_Head.position + Vector3.up * Scaled(m_HeadOffsetUp);
    }

    private float Scaled(float value)
    {
        if (m_ScaleWithSize == false || m_ReferenceScale <= 0f)
            return value;
        return value * (transform.lossyScale.x / m_ReferenceScale);
    }

    private void FindHands()
    {
        m_Hands.Clear();
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin == null)
            return;
        foreach (var handName in new[] { "Left Controller", "Right Controller" })
        {
            var t = FindChild(origin.transform, handName);
            if (t == null)
                continue;
            m_Hands.Add(new Hand
            {
                transform = t,
                haptics = t.GetComponentInChildren<HapticImpulsePlayer>(),
                lastPosition = t.position,
            });
        }
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (m_DrawGizmo == false)
            return;
        var head = m_Head != null ? m_Head : FindChild(transform, "head");
        if (head == null)
            return;
        Gizmos.color = new Color(1f, 0.5f, 0.6f, 0.6f);
        float scale = (m_ScaleWithSize && m_ReferenceScale > 0f) ? transform.lossyScale.x / m_ReferenceScale : 1f;
        Gizmos.DrawWireSphere(head.position + Vector3.up * m_HeadOffsetUp * scale, m_PetRadius * scale);
    }
}
