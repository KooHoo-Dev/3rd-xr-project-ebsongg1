using UnityEngine;

/// <summary>
/// 고양이 상태에 따라 나는 소리. (동작마다 나는 소리는 각 스크립트가 SoundBank.Play로 직접 낸다)
///
/// - 보채는 야옹(MeowNeed): 배고픔 · 기분 나쁨 · 졸림이 새로 시작될 때 한 번,
///   그 뒤로도 돌봐 주지 않으면 m_NeedRepeat초마다 다시 (무시했을 때 반응)
/// - 골골(Purr): 자는 동안 반복
/// - 반짝(StageUp): 성장 단계가 오를 때
/// CatHousePlacer가 고양이를 놓을 때 없으면 붙인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CatNeeds))]
public class CatSounds : MonoBehaviour
{
    [SerializeField, Tooltip("돌봐 주지 않으면 이 간격으로 다시 보챈다 (초). 0이면 처음 한 번만")]
    private float m_NeedRepeat = 25f;

    private CatNeeds m_Needs;
    private CatGrowth m_Growth;
    private bool m_WasHungry;
    private bool m_WasSad;
    private bool m_WasSleepy;
    private bool m_WasSleeping;
    private float m_NextNeedMeow = -1f;
    private AudioSource m_PurrLoop;

    private void Awake()
    {
        m_Needs = GetComponent<CatNeeds>();
        m_Growth = GetComponent<CatGrowth>();
    }

    private void OnEnable()
    {
        if (m_Growth != null)
            m_Growth.StageChanged += OnStageChanged;
    }

    private void OnDisable()
    {
        if (m_Growth != null)
            m_Growth.StageChanged -= OnStageChanged;
        SoundBank.StopLoop(m_PurrLoop);
        m_PurrLoop = null;
        m_WasSleeping = false;
    }

    private void Update()
    {
        UpdatePurr();
        UpdateNeedMeow();
    }

    private void UpdatePurr()
    {
        // 엔딩 등으로 상태값이 꺼져 있으면 자는 것으로 보지 않는다.
        bool sleeping = m_Needs.enabled && m_Needs.IsSleeping;
        if (sleeping == m_WasSleeping)
            return;
        m_WasSleeping = sleeping;
        if (sleeping)
        {
            m_PurrLoop = SoundBank.StartLoop(Sfx.Purr, transform);
        }
        else
        {
            SoundBank.StopLoop(m_PurrLoop);
            m_PurrLoop = null;
        }
    }

    private void UpdateNeedMeow()
    {
        if (m_Needs.enabled == false)
            return;

        bool hungry = m_Needs.IsHungry;
        bool sad = m_Needs.IsSad;
        bool sleepy = m_Needs.IsSleepy && m_Needs.IsSleeping == false;
        bool started = (hungry && m_WasHungry == false) || (sad && m_WasSad == false) || (sleepy && m_WasSleepy == false);
        m_WasHungry = hungry;
        m_WasSad = sad;
        m_WasSleepy = sleepy;

        bool needy = (hungry || sad || sleepy) && m_Needs.IsSleeping == false;
        if (needy == false)
        {
            m_NextNeedMeow = -1f;
            return;
        }

        // 새로 시작됐거나, 이제 막 보챌 상태가 됐거나(깼는데 배고픈 등), 다시 보챌 시간이 됐을 때
        if (started || m_NextNeedMeow < 0f || Time.time >= m_NextNeedMeow)
        {
            SoundBank.Play(Sfx.MeowNeed, transform.position);
            m_NextNeedMeow = m_NeedRepeat > 0f ? Time.time + m_NeedRepeat : float.MaxValue;
        }
    }

    private void OnStageChanged(CatStage stage)
    {
        SoundBank.Play(Sfx.StageUp, transform.position);
    }
}
