using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 간식 찾기 이벤트 (기획서 6-4). 어른 고양이가 땅을 파면 바닥 어딘가에 간식(말린 생선)이 숨겨진다.
///
/// 흐름
/// 1. 숨기기: 고양이의 땅 파기(CatBehaviors.Dug)가 끝나면, 간식이 밖에 없고 m_Cooldown이 지났을 때 간식을 숨긴다.
///    - 바닥 평면(Floor로 인식된 평면, 없으면 눈높이보다 충분히 낮은 가장 낮은 수평 평면) 위
///    - 플레이어에게서 m_HideDistance 거리, 시선 정면에서 m_MinAngleFromGaze도 이상 벗어난 곳 (시야 밖 = 숨긴 느낌)
///    - 평면 가장자리(벽 쪽)에서 m_EdgeMargin 안쪽, 고양이 집에서 m_MinDistanceFromHome 이상 떨어진 곳
///    조건에 맞는 곳이 없으면 각도 → 거리 순으로 조건을 풀어 가며 다시 찾는다.
///    바닥 평면이 하나도 없으면 XR Origin 높이(바닥)를 기준으로 플레이어 옆 뒤쪽에 둔다.
/// 2. 찾기: 간식 위에 반짝이가 떠 있다. 플레이어가 그립으로 집으면 찾은 것 (FoundCount)
/// 3. 건네주기: 간식을 고양이 가까이(m_GiveDistance) 가져가면 고양이가 돌아서 받아먹고(CatBehaviors.TakeSnack)
///    다 먹으면 성장 보너스(CareKind.Snack) · 배부름 · 기분이 오르고 기뻐한다. (GivenCount)
/// 4. 놓쳐서 바닥보다 m_FallDistance 아래로 떨어지면 숨겼던 자리로 되돌린다.
///
/// 안내 메시지는 GameFlow가 HasSnack · FoundCount · IsSnackHeld · GivenCount를 보고 띄운다.
/// 엔딩이 시작되면 GameFlow가 Clear()를 부른다.
/// 씬 설정은 Tools › Here Kitty › 간식 찾기 만들기
/// </summary>
[DisallowMultipleComponent]
public class SnackHunt : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private CatHousePlacer m_Placer;

    [SerializeField, Tooltip("바닥 평면을 찾을 Plane Manager. 비워 두면 씬에서 찾는다")]
    private ARPlaneManager m_PlaneManager;

    [SerializeField, Tooltip("간식 프리팹 (CatSnack + XRGrabInteractable + Rigidbody, 반짝이 자식)")]
    private GameObject m_SnackPrefab;

    [Header("숨기기")]
    [SerializeField, Tooltip("플레이어에게서 이 거리 사이에 숨긴다 (최소 · 최대, m)")]
    private Vector2 m_HideDistance = new Vector2(1.2f, 2.5f);

    [SerializeField, Tooltip("시선 정면에서 이 각도보다 벗어난 곳에 숨긴다 (도). 시야 밖이라 고개를 돌려야 보인다")]
    private float m_MinAngleFromGaze = 70f;

    [SerializeField, Tooltip("바닥 평면 가장자리(벽 쪽)에서 이만큼 안쪽에 숨긴다 (m)")]
    private float m_EdgeMargin = 0.3f;

    [SerializeField, Tooltip("고양이 집에서 이만큼은 떨어진 곳에 숨긴다 (m)")]
    private float m_MinDistanceFromHome = 0.6f;

    [SerializeField, Tooltip("이 높이보다 눈높이에 가까운 평면은 바닥으로 보지 않는다 (m). 책상 · 소파 제외용")]
    private float m_MinFloorDepth = 1.0f;

    [SerializeField, Tooltip("간식을 건네준 뒤 다음 간식을 숨기기까지 (초)")]
    private float m_Cooldown = 30f;

    [SerializeField, Tooltip("바닥에서 이만큼 띄워 놓는다 (m)")]
    private float m_FloorLift = 0.01f;

    [Header("건네주기")]
    [SerializeField, Tooltip("간식을 고양이에게서 이 거리 안으로 가져가면 받아먹는다 (m). 고양이 크기 0.1(어른) 기준")]
    private float m_GiveDistance = 0.15f;

    [SerializeField, Tooltip("받아먹는 시간 (초)")]
    private float m_EatTime = 2.5f;

    [SerializeField, Range(0f, 1f), Tooltip("다 먹으면 배고픔이 이만큼 줄어든다")]
    private float m_HungerAmount = 0.2f;

    [SerializeField, Tooltip("고양이 입 앞 이 거리에 간식을 놓고 먹인다 (m). 고양이 크기 0.1 기준")]
    private float m_MouthDistance = 0.06f;

    [SerializeField, Tooltip("간식이 바닥보다 이만큼 아래로 떨어지면 숨긴 자리로 되돌린다 (m)")]
    private float m_FallDistance = 0.5f;

    [SerializeField, Tooltip("위 거리 값의 기준이 되는 고양이 크기")]
    private float m_ReferenceScale = 0.1f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private const int k_TriesPerPass = 30;

    private Transform m_Cat;
    private Transform m_Home;
    private CatBehaviors m_Behaviors;
    private CatGrowth m_Growth;
    private CatNeeds m_Needs;
    private CatSnack m_Snack;
    private Vector3 m_HideSpot;
    private Quaternion m_HideRotation;
    private float m_FloorY;
    private float m_NextHideAt;
    private bool m_Cleared;
    private readonly List<ARPlane> m_Floors = new List<ARPlane>();

    /// <summary>간식이 밖에 나와 있는지 (숨겨져 있거나 들고 있거나)</summary>
    public bool HasSnack => m_Snack != null && m_Snack.IsGiven == false;

    /// <summary>간식이 숨겨져 있고 아직 못 찾았는지</summary>
    public bool IsSnackHidden => HasSnack && m_Snack.WasFound == false;

    /// <summary>간식을 손에 들고 있는지</summary>
    public bool IsSnackHeld => HasSnack && m_Snack.IsHeld;

    /// <summary>숨긴 횟수</summary>
    public int HiddenCount { get; private set; }

    /// <summary>찾은(처음 집은) 횟수</summary>
    public int FoundCount { get; private set; }

    /// <summary>고양이에게 건네준 횟수 (결과 화면용)</summary>
    public int GivenCount { get; private set; }

    private void Awake()
    {
        if (m_PlaneManager == null)
            m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();
    }

    private void OnDestroy()
    {
        if (m_Behaviors != null)
            m_Behaviors.Dug -= OnDug;
    }

    /// <summary>엔딩 때 부른다. 밖에 나와 있는 간식을 치우고 더 숨기지 않는다.</summary>
    public void Clear()
    {
        m_Cleared = true;
        if (m_Snack != null && m_Snack.IsGiven == false)
            Destroy(m_Snack.gameObject);
        m_Snack = null;
    }

    private void Update()
    {
        if (m_Cat == null)
        {
            TryBindCat();
            return;
        }
        if (HasSnack == false)
            return;

        if (m_Snack.WasFound && IsNearCat(m_Snack.transform.position))
            TryGive();
        else if (m_Snack.IsHeld == false)
            CheckFallen();
    }

    private void TryBindCat()
    {
        if (m_Placer == null || m_Placer.PlacedCat == null)
            return;
        m_Cat = m_Placer.PlacedCat;
        m_Home = m_Cat.parent;
        m_Behaviors = m_Cat.GetComponent<CatBehaviors>();
        m_Growth = m_Cat.GetComponent<CatGrowth>();
        m_Needs = m_Cat.GetComponent<CatNeeds>();
        if (m_Behaviors != null)
            m_Behaviors.Dug += OnDug;
        else
            Debug.LogWarning("[SnackHunt] 고양이에 CatBehaviors가 없어서 간식을 숨길 수 없어요.");
    }

    private void OnDug()
    {
        if (CanHide() == false)
            return;
        HideSnack();
    }

    private bool CanHide()
    {
        if (m_Cleared || isActiveAndEnabled == false || m_SnackPrefab == null)
            return false;
        if (HasSnack || Time.time < m_NextHideAt)
            return false;
        if (m_Growth != null && (m_Growth.Stage != CatStage.Adult || m_Growth.IsComplete))
            return false;
        return true;
    }

    private void HideSnack()
    {
        if (TryFindHideSpot(out m_HideSpot) == false)
        {
            Log("숨길 곳을 못 찾음");
            m_NextHideAt = Time.time + 5f;
            return;
        }
        m_HideRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // 앵커 아래가 아니라 월드에 둔다. (먹이와 같은 이유: 들고 다니거나 떨어뜨릴 때 앵커 보정의 영향을 받지 않게)
        var go = Instantiate(m_SnackPrefab, m_HideSpot, m_HideRotation);
        m_Snack = go.GetComponent<CatSnack>();
        if (m_Snack == null)
            m_Snack = go.AddComponent<CatSnack>();
        m_Snack.Hide(m_HideSpot, m_HideRotation);
        HiddenCount++;
        Log($"간식 숨김 ({m_HideSpot}, 총 {HiddenCount}번)");
    }

    /// <summary>
    /// 숨길 자리를 고른다. 아래 순서로 조건을 풀어 가며 찾는다.
    ///   1) 시야 밖 + m_HideDistance  2) 아무 방향 + m_HideDistance  3) 아무 방향 + 더 가까이
    /// 바닥 평면이 없으면 XR Origin 높이를 바닥으로 보고 시야 밖에 둔다.
    /// </summary>
    private bool TryFindHideSpot(out Vector3 spot)
    {
        spot = default;
        var cam = Camera.main;
        if (cam == null)
            return false;
        var head = cam.transform.position;
        var forward = Flat(cam.transform.forward);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        CollectFloors(head.y);
        if (m_Floors.Count == 0)
        {
            m_FloorY = FallbackFloorY(head.y);
            var dir = Quaternion.Euler(0f, RandomOutOfViewAngle(), 0f) * forward;
            spot = head + dir * m_HideDistance.x;
            spot.y = m_FloorY + m_FloorLift;
            Log("바닥 평면이 없어 XR Origin 높이에 숨김");
            return true;
        }

        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < k_TriesPerPass; i++)
            {
                float angle = pass == 0 ? RandomOutOfViewAngle() : Random.Range(-180f, 180f);
                float minDistance = pass < 2 ? m_HideDistance.x : 0.5f;
                float distance = Random.Range(minDistance, m_HideDistance.y);
                var flat = head + Quaternion.Euler(0f, angle, 0f) * forward * distance;

                foreach (var floor in m_Floors)
                {
                    var point = new Vector3(flat.x, floor.transform.position.y, flat.z);
                    if (IsGoodSpot(floor, point) == false)
                        continue;
                    m_FloorY = point.y;
                    spot = point + Vector3.up * m_FloorLift;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>평면 안이고 가장자리에서 m_EdgeMargin 안쪽이며 고양이 집에서 떨어져 있는지</summary>
    private bool IsGoodSpot(ARPlane floor, Vector3 point)
    {
        if (CatHousePlacer.IsInsideBoundary(floor, point) == false)
            return false;
        var right = Vector3.right * m_EdgeMargin;
        var ahead = Vector3.forward * m_EdgeMargin;
        if (CatHousePlacer.IsInsideBoundary(floor, point + right) == false
            || CatHousePlacer.IsInsideBoundary(floor, point - right) == false
            || CatHousePlacer.IsInsideBoundary(floor, point + ahead) == false
            || CatHousePlacer.IsInsideBoundary(floor, point - ahead) == false)
            return false;
        if (m_Home != null && Vector3.Distance(Flat3(m_Home.position), Flat3(point)) < m_MinDistanceFromHome)
            return false;
        return true;
    }

    /// <summary>바닥으로 인식된 평면. 없으면 눈높이보다 m_MinFloorDepth 이상 낮은 수평 평면 중 가장 낮은 것</summary>
    private void CollectFloors(float headY)
    {
        m_Floors.Clear();
        if (m_PlaneManager == null)
            return;

        ARPlane lowest = null;
        foreach (var plane in m_PlaneManager.trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp)
                continue;
            if ((plane.classifications & PlaneClassifications.Floor) != 0)
            {
                m_Floors.Add(plane);
                continue;
            }
            float y = plane.transform.position.y;
            if (headY - y >= m_MinFloorDepth && (lowest == null || y < lowest.transform.position.y))
                lowest = plane;
        }
        if (m_Floors.Count == 0 && lowest != null)
            m_Floors.Add(lowest);
    }

    /// <summary>바닥 평면이 없을 때: XR Origin 높이 (바닥 기준 추적이면 바닥). 그것도 없으면 눈높이 1.5m 아래</summary>
    private static float FallbackFloorY(float headY)
    {
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin != null && headY - origin.transform.position.y > 0.5f)
            return origin.transform.position.y;
        return headY - 1.5f;
    }

    /// <summary>시선 정면에서 m_MinAngleFromGaze ~ 180도 사이, 왼쪽 또는 오른쪽</summary>
    private float RandomOutOfViewAngle()
    {
        float angle = Random.Range(Mathf.Clamp(m_MinAngleFromGaze, 0f, 180f), 180f);
        return Random.value < 0.5f ? -angle : angle;
    }

    private bool IsNearCat(Vector3 point)
    {
        // 고양이 몸 가운데쯤 (발밑에서 조금 위)을 기준으로 잰다.
        var center = m_Cat.position + Vector3.up * Scaled(m_GiveDistance * 0.4f);
        return Vector3.Distance(point, center) < Scaled(m_GiveDistance);
    }

    private void TryGive()
    {
        if (m_Behaviors == null || m_Behaviors.TakeSnack(m_EatTime, OnSnackEaten) == false)
            return;

        // 고양이가 플레이어 쪽으로 돌아서 먹으므로, 고양이 앞 플레이어 쪽에 간식을 놓는다.
        var cam = Camera.main;
        var toPlayer = cam != null ? Flat(cam.transform.position - m_Cat.position) : m_Cat.forward;
        if (toPlayer.sqrMagnitude < 0.0001f)
            toPlayer = m_Cat.forward;
        var snack = m_Snack;
        snack.Give();
        snack.transform.SetPositionAndRotation(
            m_Cat.position + toPlayer * Scaled(m_MouthDistance) + Vector3.up * 0.005f,
            Quaternion.LookRotation(Vector3.Cross(toPlayer, Vector3.up), Vector3.up));
        StartCoroutine(ShrinkAndDestroy(snack.gameObject, m_EatTime + 0.5f));
        Log("간식 건네줌");
    }

    private void OnSnackEaten()
    {
        if (m_Growth != null)
            m_Growth.Care(CareKind.Snack, true);
        if (m_Needs != null)
            m_Needs.Feed(m_HungerAmount);
        GivenCount++;
        m_NextHideAt = Time.time + m_Cooldown;
        Log($"간식 다 먹음 (총 {GivenCount}번)");
    }

    /// <summary>놓친 간식이 바닥을 뚫고 떨어졌으면 숨겼던 자리로 되돌린다. (이미 찾았으면 반짝이 없이)</summary>
    private void CheckFallen()
    {
        if (m_Snack.transform.position.y > m_FloorY - m_FallDistance)
            return;
        bool found = m_Snack.WasFound;
        m_Snack.Hide(m_HideSpot, m_HideRotation);
        Log("간식이 떨어져서 숨긴 자리로 되돌림");
        if (found)
            m_Snack.MarkFound();
    }

    private IEnumerator ShrinkAndDestroy(GameObject snack, float duration)
    {
        var start = snack.transform.localScale;
        float t = 0f;
        while (t < duration && snack != null)
        {
            t += Time.deltaTime;
            snack.transform.localScale = start * (1f - Mathf.Clamp01(t / duration));
            yield return null;
        }
        if (snack != null)
            Destroy(snack);
        if (m_Snack != null && m_Snack.gameObject == snack)
            m_Snack = null;
    }

    private float Scaled(float value)
    {
        if (m_Cat == null || m_ReferenceScale <= 0f)
            return value;
        return value * (m_Cat.lossyScale.x / m_ReferenceScale);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0f ? v.normalized : v;
    }

    private static Vector3 Flat3(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[SnackHunt] {message}");
    }

    [ContextMenu("테스트: 지금 간식 숨기기")]
    private void TestHide()
    {
        if (m_Cat == null)
            TryBindCat();
        if (HasSnack == false)
            HideSnack();
    }
}
