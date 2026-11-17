using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>고양이 표정 종류. 번호는 Pspsps 얼굴 머티리얼 M_Chibi_Emo_xx.</summary>
public enum CatExpression
{
    Default,   // 03 기본
    Happy,     // 06 웃음 (쓰다듬기 · 먹기 · 기분 높음)
    Excited,   // 04 신남 (성장 · Victory)
    Sad,       // 21 시무룩 · 슬픔 (배고픔 · 기분 낮음)
    Annoyed,   // 10 언짢음 (오래 무시당함)
    Sleepy,    // 19 졸림
    Sleeping,  // 05 잠 (Sleep 동작)
    Surprised, // 02 놀람 (가장자리 Alert)
}

/// <summary>
/// 고양이 얼굴 머티리얼을 바꿔 표정을 나타낸다.
/// 몸 렌더러의 두 번째 머티리얼 칸(Element 1)이 얼굴이라, 그 칸만 바꿔 끼운다.
///
/// 표정 우선순위 (위가 이긴다)
/// 1. 잠: Animator의 Sleeping이 true면 항상 Sleeping 얼굴
/// 2. 잠깐 표정: Show(표정, 초) — 쓰다듬기 · 놀람 등. 시간이 지나면 기본 표정으로 돌아간다
/// 3. 기본 표정: SetBaseExpression(표정) — 상태값(배고픔 · 기분 · 졸림)에 따라 CatNeeds가 정한다
///
/// 머티리얼은 Tools > Here Kitty > 고양이 애니메이터 만들기를 실행하면 자동으로 채워진다.
/// </summary>
[DisallowMultipleComponent]
public class CatFace : MonoBehaviour
{
    [Serializable]
    private struct FaceEntry
    {
        public CatExpression expression;
        public Material material;
    }

    [SerializeField, Tooltip("표정별 얼굴 머티리얼")]
    private FaceEntry[] m_Faces = Array.Empty<FaceEntry>();

    [SerializeField, Tooltip("얼굴이 들어 있는 머티리얼 칸 번호 (Chibi_Cat은 1)")]
    private int m_FaceSlot = 1;

    private static readonly int s_SleepingHash = Animator.StringToHash("Sleeping");

    private readonly Dictionary<CatExpression, Material> m_Lookup = new Dictionary<CatExpression, Material>();
    private Renderer m_Renderer;
    private Animator m_Animator;
    private CatExpression m_Base = CatExpression.Default;
    private CatExpression m_Temporary;
    private float m_TemporaryUntil = -1f;
    private CatExpression m_Applied;
    private bool m_HasApplied;

    /// <summary>지금 보이는 표정</summary>
    public CatExpression Current => m_Applied;

    /// <summary>평소 표정 (잠깐 표정이 끝나면 돌아갈 표정)</summary>
    public CatExpression BaseExpression => m_Base;

    private void Awake()
    {
        foreach (var face in m_Faces)
        {
            if (face.material != null)
                m_Lookup[face.expression] = face.material;
        }

        m_Animator = GetComponentInChildren<Animator>();
        m_Renderer = FindFaceRenderer();
        if (m_Renderer == null)
            Debug.LogWarning($"[CatFace] 머티리얼 칸이 {m_FaceSlot + 1}개 이상인 렌더러를 찾지 못했어요. 표정이 바뀌지 않아요.");
    }

    private void OnEnable()
    {
        m_HasApplied = false;
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    /// <summary>평소 표정을 정한다. (상태값에 따라)</summary>
    public void SetBaseExpression(CatExpression expression)
    {
        m_Base = expression;
        Refresh();
    }

    /// <summary>seconds초 동안 잠깐 표정을 보여 준다. 다시 부르면 새 표정 · 시간으로 덮어쓴다.</summary>
    public void Show(CatExpression expression, float seconds)
    {
        m_Temporary = expression;
        m_TemporaryUntil = Time.time + seconds;
        Refresh();
    }

    /// <summary>잠깐 표정을 바로 끝낸다.</summary>
    public void ClearTemporary()
    {
        m_TemporaryUntil = -1f;
        Refresh();
    }

    private void Refresh()
    {
        Apply(Decide());
    }

    private CatExpression Decide()
    {
        if (m_Animator != null && m_Animator.runtimeAnimatorController != null && m_Animator.GetBool(s_SleepingHash))
            return CatExpression.Sleeping;
        if (Time.time < m_TemporaryUntil)
            return m_Temporary;
        return m_Base;
    }

    private void Apply(CatExpression expression)
    {
        if (m_HasApplied && expression == m_Applied)
            return;
        if (m_Renderer == null || m_Lookup.TryGetValue(expression, out var material) == false)
            return;

        // sharedMaterials를 통째로 바꿔 넣는다. (materials를 쓰면 머티리얼 복사본이 생긴다)
        var materials = m_Renderer.sharedMaterials;
        if (m_FaceSlot < 0 || m_FaceSlot >= materials.Length)
            return;
        materials[m_FaceSlot] = material;
        m_Renderer.sharedMaterials = materials;

        m_Applied = expression;
        m_HasApplied = true;
    }

    private Renderer FindFaceRenderer()
    {
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.sharedMaterials.Length > m_FaceSlot)
                return r;
        }
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.sharedMaterials.Length > m_FaceSlot)
                return r;
        }
        return null;
    }
}
