using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전체 흐름: 시작 화면 → 고양이 집 배치 → 육성 → 엔딩 → 결과
///
/// 1. Title  : 플레이어 앞에 시작 패널을 띄운다. 집 배치(CatHousePlacer)는 꺼 둔다.
///             패널이 시야에서 많이 벗어나면 다시 앞으로 옮긴다. (헤드셋을 쓰고 둘러볼 때)
/// 2. Placing: 시작 버튼을 누르면 패널을 숨기고 배치를 켠다. 배치 안내 한 줄이 시선을 따라다닌다.
/// 3. Raising: 집을 놓으면 안내를 숨기고 돌본 시간을 재기 시작한다.
/// 4. Ending : 고양이가 다 자라면 (CatGrowth.IsComplete) 돌본 시간을 멈추고, 먹이 상자 · 먹이를 치우고,
///             CatEnding 연출(탐험가 차림 → 인사 → 가장자리로 떠나기)을 재생한다.
/// 5. Result : 고양이가 사라지면 결과 패널(돌본 시간 · 먹인 횟수 · 쓰다듬은 횟수)을 띄운다.
///             "처음부터 다시" 버튼은 씬을 다시 불러온다.
///
/// 시작 버튼 → StartGame(), 다시 버튼 → Restart() (Tools › Here Kitty › 시작 · 결과 화면 만들기)
/// </summary>
[DisallowMultipleComponent]
public class GameFlow : MonoBehaviour
{
    public enum State { Title, Placing, Raising, Ending, Result }

    [Header("연결")]
    [SerializeField] private CatHousePlacer m_Placer;

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

    private State m_State = State.Title;
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
            m_PlacementHint.gameObject.SetActive(false);
        if (m_StartPanel != null)
            m_StartPanel.gameObject.SetActive(true);
        if (m_ResultPanel != null)
            m_ResultPanel.gameObject.SetActive(false);
        m_PlaceTitleAt = Time.time + m_FirstPlaceDelay;
    }

    /// <summary>결과 패널의 "처음부터 다시" 버튼에서 부른다. 씬을 다시 불러온다.</summary>
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>시작 버튼에서 부른다.</summary>
    public void StartGame()
    {
        if (m_State != State.Title)
            return;
        if (m_StartPanel != null)
            m_StartPanel.gameObject.SetActive(false);
        if (m_Placer != null)
            m_Placer.enabled = true;
        if (m_PlacementHint != null)
        {
            m_PlacementHint.gameObject.SetActive(true);
            SnapInFront(m_PlacementHint, m_HintDistance, m_HintDrop);
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
            SnapInFront(panel, m_PanelDistance, m_PanelDrop);
    }

    private void OnPlaced()
    {
        if (m_PlacementHint != null)
            m_PlacementHint.gameObject.SetActive(false);
        m_RaisingStartTime = Time.time;
        var cat = m_Placer.PlacedCat;
        m_Growth = cat != null ? cat.GetComponent<CatGrowth>() : null;
        SetState(State.Raising);
    }

    private void StartEnding()
    {
        m_RaisingEndTime = Time.time;
        SetState(State.Ending);

        var cat = m_Placer != null ? m_Placer.PlacedCat : null;

        // 먹이 상자와 밖에 나와 있는 먹이를 치운다. (밥그릇 · 집은 남겨 둔다)
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
            m_ResultPanel.gameObject.SetActive(true);
            SnapInFront(m_ResultPanel, m_PanelDistance, m_PanelDrop);
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
