using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임 전체 흐름: 시작 화면 → 고양이 집 배치 → 육성 → 엔딩 → 결과
///
/// 1. Title  : 플레이어 앞에 시작 패널을 띄운다. 집 배치(CatHousePlacer)는 꺼 둔다.
///             패널이 시야에서 많이 벗어나면 다시 앞으로 옮긴다. (헤드셋을 쓰고 둘러볼 때)
/// 2. Placing: 시작 버튼을 누르면 패널을 숨기고 배치를 켠다. 배치 안내 한 줄이 시선을 따라다닌다.
/// 3. Raising: 집을 놓으면 안내를 숨기고 돌본 시간을 재기 시작한다.
///             처음 한 번씩 안내 한 줄을 띄운다 (같은 안내 칸을 다시 쓴다):
///             집을 놓은 직후 → 쓰다듬기, 처음 배고플 때 → 먹이 주기, 처음 졸릴 때 → 방석으로 재우기.
///             그 행동을 하거나 m_HintDuration초가 지나면 사라진다. 이미 해 본 행동이면 건너뛴다.
///             간식 찾기(SnackHunt): 처음 숨겼을 때 → 바닥에서 찾아보기, 처음 집었을 때 → 고양이에게 가져다주기.
///             성장 단계가 오르면 같은 안내 칸에 축하 문구를 잠깐 띄운다. (떠 있던 안내는 끝나면 다시 보여 준다)
/// 4. Ending : 고양이가 다 자라면 (CatGrowth.IsComplete) 돌본 시간을 멈추고, 먹이 상자 · 먹이를 치우고,
///             CatEnding 연출(탐험가 차림 → 인사 → 가장자리로 떠나기)을 재생한다.
/// 5. Result : 고양이가 사라지면 결과 패널(돌본 시간 · 먹인 횟수 · 쓰다듬은 횟수)을 띄운다.
///             "처음부터 다시" 버튼은 씬을 다시 불러온다.
///
/// 시작 버튼 → StartGame(), 다시 버튼 → Restart() (Tools › Here Kitty › 시작 · 결과 화면 만들기)
/// 패널 · 안내는 UIPop으로 팝 하고 나타나고 흐려지며 사라진다. 버튼에는 UIButtonBounce를 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class GameFlow : MonoBehaviour
{
    public enum State { Title, Placing, Raising, Ending, Result }

    [Header("연결")]
    [SerializeField] private CatHousePlacer m_Placer;

    [SerializeField, Tooltip("간식 찾기. 비워 두면 씬에서 찾는다")]
    private SnackHunt m_SnackHunt;

    [SerializeField, Tooltip("시작 패널 (World Space Canvas)")]
    private Transform m_StartPanel;

    [SerializeField, Tooltip("배치 안내 한 줄 (World Space Canvas)")]
    private Transform m_PlacementHint;

    [SerializeField, Tooltip("결과 패널 (World Space Canvas)")]
    private Transform m_ResultPanel;

    [SerializeField] private TMP_Text m_ResultTimeText;
    [SerializeField] private TMP_Text m_ResultFeedText;
    [SerializeField] private TMP_Text m_ResultPetText;

    [Header("시작 패널 위치")]
    [SerializeField, Tooltip("플레이어 앞 거리 (m)")]
    private float m_PanelDistance = 0.8f;

    [SerializeField, Tooltip("눈높이보다 이만큼 아래 (m)")]
    private float m_PanelDrop = 0.1f;

    [SerializeField, Tooltip("시선에서 이 각도보다 벗어나면 패널을 다시 앞으로 옮긴다")]
    private float m_RecenterAngle = 50f;

    [SerializeField, Tooltip("앱 시작 후 패널을 처음 놓기까지 기다리는 시간 (헤드셋 위치가 잡히길 기다림, 초)")]
    private float m_FirstPlaceDelay = 0.5f;

    [Header("배치 안내 위치")]
    [SerializeField] private float m_HintDistance = 0.7f;

    [SerializeField, Tooltip("눈높이보다 이만큼 아래. 책상을 가리지 않게 시야 아래쪽에 둔다 (m)")]
    private float m_HintDrop = 0.28f;

    [SerializeField, Tooltip("안내가 시선을 따라오는 빠르기")]
    private float m_HintFollowSpeed = 3f;

    [Header("처음 한 번 안내 (육성 중)")]
    [SerializeField] private string m_PetHint = "고양이 머리를 컨트롤러로 <b>문질러</b> 쓰다듬어 보세요";
    [SerializeField] private string m_FeedHint = "배고파해요! 상자에서 생선을 <b>그립</b>으로 집어 밥그릇에 넣어 주세요";
    [SerializeField] private string m_SleepHint = "졸려해요! 방석을 <b>그립</b>으로 집어 고양이 옆에 놓아 주세요";
    [SerializeField] private string m_SnackFindText = "고양이가 간식을 숨겼어요! 바닥에서 <b>반짝이는 생선</b>을 찾아보세요";
    [SerializeField] private string m_SnackGiveText = "찾았다! <b>고양이에게 직접</b> 가져다주세요";

    [SerializeField, Tooltip("간식 찾기 안내는 찾을 때까지 이 시간 동안 띄운다 (초)")]
    private float m_SnackHintDuration = 40f;

    [SerializeField, Tooltip("집을 놓고 쓰다듬기 안내가 뜨기까지 (초)")]
    private float m_PetHintDelay = 2f;

    [SerializeField, Tooltip("행동을 안 해도 이 시간이 지나면 안내가 사라진다 (초)")]
    private float m_HintDuration = 20f;

    [Header("성장 축하")]
    [SerializeField] private string m_ChildText = "쑥쑥! 고양이가 <b>어린이</b>가 됐어요";
    [SerializeField] private string m_AdultText = "다 컸다! 고양이가 <b>어른</b>이 됐어요";

    [SerializeField, Tooltip("축하 문구를 띄우는 시간 (초)")]
    private float m_StageTextDuration = 3f;

    private enum CareHint { Pet, Feed, Sleep, SnackFind, SnackGive, Count }

    private State m_State = State.Title;
    private TMP_Text m_HintLabel;
    private readonly bool[] m_HintShown = new bool[(int)CareHint.Count];
    private int m_ActiveHint = -1;
    private int m_HintBaseline;
    private float m_HintHideAt;
    private string m_ActiveHintText;
    private float m_NoticeUntil = -1f;
    private float m_PetHintAt;
    private CatNeeds m_CatNeeds;
    private CatPetting m_CatPetting;
    private CatEating m_CatEating;
    private CatSleeping m_CatSleeping;
    private float m_PlaceTitleAt;
    private float m_RaisingStartTime;
    private float m_RaisingEndTime = -1f;
    private CatGrowth m_Growth;
    private int m_FeedCount;
    private int m_PetCount;

    /// <summary>상태가 바뀔 때</summary>
    public event Action<State> StateChanged;

    public State CurrentState => m_State;

    /// <summary>집을 놓은 뒤 지난 시간 (초). 엔딩 후에는 멈춘다. 결과 화면용</summary>
    public float CareSeconds
    {
        get
        {
            if (m_State == State.Title || m_State == State.Placing)
                return 0f;
            float end = m_RaisingEndTime >= 0f ? m_RaisingEndTime : Time.time;
            return end - m_RaisingStartTime;
        }
    }

    /// <summary>놓인 고양이 (배치 전에는 null)</summary>
    public Transform Cat => m_Placer != null ? m_Placer.PlacedCat : null;

    private void Awake()
    {
        if (m_Placer != null)
            m_Placer.enabled = false;
        if (m_PlacementHint != null)
        {
            m_HintLabel = m_PlacementHint.GetComponentInChildren<TMP_Text>(true);
            m_PlacementHint.gameObject.SetActive(false);
        }
        if (m_StartPanel != null)
        {
            m_StartPanel.gameObject.SetActive(true);
            // 플레이어 앞에 놓기 전까지는 투명하게 둔다. (놓을 때 팝 하고 나타난다)
            UIPop.HideNow(m_StartPanel);
        }
        if (m_ResultPanel != null)
            m_ResultPanel.gameObject.SetActive(false);
        AddButtonBounce(m_StartPanel);
        AddButtonBounce(m_ResultPanel);
        m_PlaceTitleAt = Time.time + m_FirstPlaceDelay;
        if (m_SnackHunt == null)
            m_SnackHunt = FindFirstObjectByType<SnackHunt>();
    }

    /// <summary>결과 패널의 "처음부터 다시" 버튼에서 부른다. 씬을 다시 불러온다.</summary>
    public void Restart()
    {
        SoundBank.Play(Sfx.Button);
        // 버튼 소리가 끊기지 않게 잠깐 기다렸다가 다시 불러온다.
        Invoke(nameof(ReloadScene), 0.25f);
    }

    private void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>시작 버튼에서 부른다.</summary>
    public void StartGame()
    {
        if (m_State != State.Title)
            return;
        SoundBank.Play(Sfx.Button);
        UIPop.Hide(m_StartPanel);
        if (m_Placer != null)
            m_Placer.enabled = true;
        if (m_PlacementHint != null)
        {
            SnapInFront(m_PlacementHint, m_HintDistance, m_HintDrop);
            UIPop.Show(m_PlacementHint);
        }
        SetState(State.Placing);
    }

    private void Update()
    {
        switch (m_State)
        {
            case State.Title:
                UpdateTitle();
                break;

            case State.Placing:
                FollowGaze(m_PlacementHint, m_HintDistance, m_HintDrop);
                if (m_Placer != null && m_Placer.IsPlaced)
                    OnPlaced();
                break;

            case State.Raising:
                UpdateCareHints();
                if (m_Growth != null && m_Growth.IsComplete)
                    StartEnding();
                break;

            case State.Result:
                KeepInView(m_ResultPanel);
                break;
        }
    }

    private void UpdateTitle()
    {
        if (m_StartPanel == null)
            return;
        if (m_PlaceTitleAt > 0f)
        {
            if (Time.time < m_PlaceTitleAt)
                return;
            m_PlaceTitleAt = -1f;
            SnapInFront(m_StartPanel, m_PanelDistance, m_PanelDrop);
            UIPop.Show(m_StartPanel);
            return;
        }
        KeepInView(m_StartPanel);
    }

    /// <summary>고개를 많이 돌려서 패널이 안 보이면 다시 앞으로</summary>
    private void KeepInView(Transform panel)
    {
        var cam = Camera.main;
        if (panel == null || cam == null)
            return;
        var toPanel = panel.position - cam.transform.position;
        if (Vector3.Angle(Flat(cam.transform.forward), Flat(toPanel)) > m_RecenterAngle)
        {
            // 순간이동하면 눈에 거슬리므로 앞으로 옮긴 뒤 다시 팝 하고 나타나게 한다.
            SnapInFront(panel, m_PanelDistance, m_PanelDrop);
            UIPop.Show(panel);
        }
    }

    private void OnPlaced()
    {
        UIPop.Hide(m_PlacementHint);
        m_RaisingStartTime = Time.time;
        var cat = m_Placer.PlacedCat;
        if (cat != null)
        {
            m_Growth = cat.GetComponent<CatGrowth>();
            m_CatNeeds = cat.GetComponent<CatNeeds>();
            m_CatPetting = cat.GetComponent<CatPetting>();
            m_CatEating = cat.GetComponent<CatEating>();
            m_CatSleeping = cat.GetComponent<CatSleeping>();
        }
        if (m_Growth != null)
            m_Growth.StageChanged += OnStageChanged;
        m_PetHintAt = Time.time + m_PetHintDelay;
        SetState(State.Raising);
    }

    /// <summary>쓰다듬기 · 먹이 · 재우기 안내를 처음 한 번씩 띄우고, 그 행동을 하면 숨긴다.</summary>
    private void UpdateCareHints()
    {
        if (m_PlacementHint == null || m_HintLabel == null)
            return;

        // 축하 문구가 떠 있는 동안은 새 안내를 띄우지 않는다. (떠 있던 안내의 사라짐 판정은 아래에서 계속한다)
        bool notice = m_NoticeUntil > 0f;
        if (notice)
        {
            FollowGaze(m_PlacementHint, m_HintDistance, m_HintDrop);
            if (Time.time >= m_NoticeUntil)
            {
                m_NoticeUntil = -1f;
                notice = false;
                // 축하 전에 떠 있던 안내가 아직 남아 있으면 다시 보여 주고, 없으면 숨긴다.
                if (m_ActiveHint >= 0)
                {
                    m_HintLabel.text = m_ActiveHintText;
                    UIPop.Show(m_PlacementHint);
                }
                else
                {
                    UIPop.Hide(m_PlacementHint);
                }
            }
        }

        // 간식을 처음 집은 순간에는 다른 안내가 떠 있어도 바꿔서 "고양이에게 직접" 안내를 바로 보여 준다.
        // (생선이 먹이와 똑같이 생겨서 밥그릇에 넣는 줄 알기 쉽다)
        if (m_ActiveHint >= 0 && m_ActiveHint != (int)CareHint.SnackGive && m_HintShown[(int)CareHint.SnackGive] == false
            && m_SnackHunt != null && m_SnackHunt.IsSnackHeld)
            HideCareHint();

        if (m_ActiveHint >= 0)
        {
            if (notice == false)
                FollowGaze(m_PlacementHint, m_HintDistance, m_HintDrop);
            if (CareCount((CareHint)m_ActiveHint) > m_HintBaseline || Time.time >= m_HintHideAt
                || (m_ActiveHint == (int)CareHint.Sleep && m_CatNeeds != null && m_CatNeeds.IsSleeping))
                HideCareHint();
            return;
        }

        if (notice)
            return;
        if (Time.time >= m_PetHintAt)
            TryShowCareHint(CareHint.Pet, m_PetHint);
        if (m_ActiveHint < 0 && m_CatNeeds != null && m_CatNeeds.IsHungry)
            TryShowCareHint(CareHint.Feed, m_FeedHint);
        if (m_ActiveHint < 0 && m_CatSleeping != null && m_CatNeeds != null && m_CatNeeds.IsSleepy && m_CatNeeds.IsSleeping == false)
            TryShowCareHint(CareHint.Sleep, m_SleepHint);
        if (m_ActiveHint < 0 && m_SnackHunt != null && m_SnackHunt.IsSnackHidden)
            TryShowCareHint(CareHint.SnackFind, m_SnackFindText, m_SnackHintDuration);
        if (m_ActiveHint < 0 && m_SnackHunt != null && m_SnackHunt.IsSnackHeld)
            TryShowCareHint(CareHint.SnackGive, m_SnackGiveText);
    }

    /// <summary>duration이 0 이하면 m_HintDuration 동안 띄운다.</summary>
    private void TryShowCareHint(CareHint hint, string text, float duration = 0f)
    {
        int i = (int)hint;
        if (m_HintShown[i])
            return;
        m_HintShown[i] = true;
        // 이미 해 본 행동이면 안내하지 않는다.
        if (CareCount(hint) > 0)
            return;

        m_ActiveHint = i;
        m_HintBaseline = CareCount(hint);
        m_HintHideAt = Time.time + (duration > 0f ? duration : m_HintDuration);
        m_ActiveHintText = text;
        m_HintLabel.text = text;
        SoundBank.Play(Sfx.Hint);
        // 이미 떠 있으면(다른 안내를 바꿔 띄울 때) 자리는 그대로 두고 문구만 바꿔 팝 한다.
        if (m_PlacementHint.gameObject.activeSelf == false)
            SnapInFront(m_PlacementHint, m_HintDistance, m_HintDrop);
        UIPop.Show(m_PlacementHint);
    }

    /// <summary>안내를 끝낸다. 축하 문구가 떠 있으면 그건 그대로 두고 끝날 때 함께 숨긴다.</summary>
    private void HideCareHint()
    {
        m_ActiveHint = -1;
        if (m_NoticeUntil < 0f)
            UIPop.Hide(m_PlacementHint);
    }

    /// <summary>성장 단계가 오르면 안내 칸에 축하 문구를 잠깐 띄운다. (반짝 소리는 CatSounds가 낸다)</summary>
    private void OnStageChanged(CatStage stage)
    {
        if (m_State != State.Raising || m_PlacementHint == null || m_HintLabel == null)
            return;
        string text = stage == CatStage.Child ? m_ChildText : stage == CatStage.Adult ? m_AdultText : null;
        if (string.IsNullOrEmpty(text))
            return;

        m_NoticeUntil = Time.time + m_StageTextDuration;
        m_HintLabel.text = text;
        if (m_PlacementHint.gameObject.activeSelf == false)
            SnapInFront(m_PlacementHint, m_HintDistance, m_HintDrop);
        UIPop.Show(m_PlacementHint);
    }

    /// <summary>패널 안 버튼에 누를 때 통통 튀는 반응을 붙인다.</summary>
    private static void AddButtonBounce(Transform panel)
    {
        if (panel == null)
            return;
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button.GetComponent<UIButtonBounce>() == null)
                button.gameObject.AddComponent<UIButtonBounce>();
        }
    }

    private int CareCount(CareHint hint)
    {
        switch (hint)
        {
            case CareHint.Pet: return m_CatPetting != null ? m_CatPetting.PetCount : 0;
            case CareHint.Feed: return m_CatEating != null ? m_CatEating.FeedCount : 0;
            case CareHint.Sleep: return m_CatSleeping != null ? m_CatSleeping.CushionSleepCount : 0;
            case CareHint.SnackFind: return m_SnackHunt != null ? m_SnackHunt.FoundCount : 0;
            case CareHint.SnackGive: return m_SnackHunt != null ? m_SnackHunt.GivenCount : 0;
            default: return 0;
        }
    }

    private void StartEnding()
    {
        m_RaisingEndTime = Time.time;
        m_NoticeUntil = -1f;
        HideCareHint();
        if (m_Growth != null)
            m_Growth.StageChanged -= OnStageChanged;
        SetState(State.Ending);

        var cat = m_Placer != null ? m_Placer.PlacedCat : null;

        // 먹이 상자와 밖에 나와 있는 먹이 · 간식을 치운다. (밥그릇 · 집은 남겨 둔다)
        if (m_SnackHunt != null)
            m_SnackHunt.Clear();
        foreach (var box in FindObjectsByType<FoodBox>(FindObjectsSortMode.None))
            box.gameObject.SetActive(false);
        foreach (var food in FindObjectsByType<CatFood>(FindObjectsSortMode.None))
        {
            if (food.InBowl == false)
                food.gameObject.SetActive(false);
        }

        if (cat != null && cat.TryGetComponent(out CatEnding ending))
            ending.Play(ShowResult);
        else
            ShowResult();
    }

    private void ShowResult()
    {
        // 엔딩은 마지막 식사가 끝난 뒤 시작하므로 여기서 읽는다. (고양이가 꺼져 있어도 값은 남아 있다)
        var cat = m_Placer != null ? m_Placer.PlacedCat : null;
        m_FeedCount = cat != null && cat.TryGetComponent(out CatEating eating) ? eating.FeedCount : 0;
        m_PetCount = cat != null && cat.TryGetComponent(out CatPetting petting) ? petting.PetCount : 0;

        if (m_ResultTimeText != null)
            m_ResultTimeText.text = FormatTime(CareSeconds);
        if (m_ResultFeedText != null)
            m_ResultFeedText.text = $"{m_FeedCount}번";
        if (m_ResultPetText != null)
            m_ResultPetText.text = $"{m_PetCount}번";

        if (m_ResultPanel != null)
        {
            SoundBank.Play(Sfx.Result);
            SnapInFront(m_ResultPanel, m_PanelDistance, m_PanelDrop);
            UIPop.Show(m_ResultPanel);
        }
        SetState(State.Result);
    }

    /// <summary>452초 → "7분 32초"</summary>
    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = total / 60;
        int secs = total % 60;
        return minutes > 0 ? $"{minutes}분 {secs}초" : $"{secs}초";
    }

    private void SetState(State state)
    {
        m_State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>시선 앞(수평 방향 기준)에 바로 놓고 플레이어를 보게 한다.</summary>
    private static void SnapInFront(Transform target, float distance, float drop)
    {
        if (target == null || TryGetPoseInFront(distance, drop, out var pos, out var rot) == false)
            return;
        target.SetPositionAndRotation(pos, rot);
    }

    /// <summary>시선 앞으로 부드럽게 따라온다.</summary>
    private void FollowGaze(Transform target, float distance, float drop)
    {
        if (target == null || TryGetPoseInFront(distance, drop, out var pos, out var rot) == false)
            return;
        float t = 1f - Mathf.Exp(-m_HintFollowSpeed * Time.deltaTime);
        target.SetPositionAndRotation(Vector3.Lerp(target.position, pos, t), Quaternion.Slerp(target.rotation, rot, t));
    }

    private static bool TryGetPoseInFront(float distance, float drop, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;
        var cam = Camera.main;
        if (cam == null)
            return false;
        var forward = Flat(cam.transform.forward);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        position = cam.transform.position + forward * distance + Vector3.down * drop;
        // World Space Canvas는 +Z가 화면 안쪽이라, 플레이어에서 패널 쪽을 바라보게 돌린다.
        rotation = Quaternion.LookRotation(forward, Vector3.up);
        return true;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0f ? v.normalized : v;
    }
}
