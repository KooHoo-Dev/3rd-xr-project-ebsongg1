using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 컨트롤러로 책상(수평 평면)을 가리키고 트리거를 누르면 그 자리에 고양이 집과 고양이를 놓는다.
///
/// 흐름
/// 1. 매 프레임 컨트롤러에서 앞으로 레이를 쏴서 인식된 평면에 맞는지 본다. (평면 데이터로 직접 계산, 충돌체 안 씀)
/// 2. 놓을 수 있는 평면이면 그 지점에 미리보기 표시(레티클)를 보여 준다.
/// 3. 트리거를 누르면 그 평면에 앵커를 붙이고, 앵커 아래에 집과 고양이를 만든다.
/// 4. 다 놓은 뒤에는 평면 시각화를 숨겨 화면을 깔끔하게 한다. (충돌체는 남겨 둔다)
///
/// 평면 고르기 규칙 (기획서 7장)
/// - 책상(Table)으로 인식된 평면이 하나라도 있으면 책상에만 놓을 수 있다.
/// - 책상이 하나도 없으면 바닥 등 위를 향한 수평 평면이면 어디든 놓을 수 있다.
/// </summary>
public class CatHousePlacer : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] ARPlaneManager m_PlaneManager;
    [SerializeField] ARAnchorManager m_AnchorManager;

    [Header("입력")]
    [SerializeField, Tooltip("레이를 쏠 기준. 보통 Right Controller")]
    Transform m_RayOrigin;

    [SerializeField, Tooltip("놓기 버튼. XRI Right Interaction/Activate (오른손 트리거)")]
    InputActionProperty m_PlaceAction;

    [SerializeField] float m_MaxDistance = 5f;

    [SerializeField, Tooltip("놓을 곳을 못 찾을 때 이유를 로그로 남긴다 (디버그용)")]
    bool m_DebugLog = true;

    [Header("놓을 것")]
    [SerializeField, Tooltip("놓을 위치 미리보기 (얇은 원판 등). 충돌체는 자동으로 꺼진다")]
    GameObject m_Reticle;

    [SerializeField, Tooltip("고양이 집 프리팹 (없으면 비워 둬도 됨)")]
    GameObject m_HousePrefab;

    [SerializeField, Tooltip("고양이 프리팹 (예: Chibi_Cat_00)")]
    GameObject m_CatPrefab;

    [SerializeField, Tooltip("처음 놓을 때 고양이 크기 (아기 단계). 어린이 0.1 · 어른 0.125 기준이면 아기는 0.075")]
    float m_CatScale = 0.075f;

    [SerializeField, Tooltip("집 기준으로 고양이를 놓을 위치 (집 앞쪽)")]
    Vector3 m_CatOffset = new Vector3(0f, 0f, 0.12f);

    [Header("이벤트")]
    [SerializeField, Tooltip("다 놓은 뒤 실행. 고양이 Transform이 넘어온다")]
    UnityEvent<Transform> m_Placed;

    ARPlane m_TargetPlane;
    Pose m_TargetPose;
    bool m_HasTarget;

    public bool IsPlaced { get; private set; }
    public Transform PlacedCat { get; private set; }
    public ARPlane PlacedPlane { get; private set; }

    void Awake()
    {
        if (m_Reticle != null)
        {
            // 미리보기 표시가 레이를 가로막지 않게 충돌체를 끈다.
            foreach (var col in m_Reticle.GetComponentsInChildren<Collider>())
                col.enabled = false;
            m_Reticle.SetActive(false);
        }
    }

    void OnEnable()
    {
        if (m_PlaceAction.action != null)
        {
            m_PlaceAction.action.Enable();
            m_PlaceAction.action.performed += OnPlacePressed;
        }

        if (m_PlaneManager != null)
            m_PlaneManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    void OnDisable()
    {
        if (m_PlaceAction.action != null)
            m_PlaceAction.action.performed -= OnPlacePressed;

        if (m_PlaneManager != null)
            m_PlaneManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    void Update()
    {
        if (IsPlaced)
            return;

        m_HasTarget = TryFindTarget(out m_TargetPlane, out m_TargetPose);

        if (m_Reticle != null)
        {
            m_Reticle.SetActive(m_HasTarget);
            if (m_HasTarget)
                m_Reticle.transform.SetPositionAndRotation(m_TargetPose.position, m_TargetPose.rotation);
        }
    }

    /// <summary>
    /// 컨트롤러 레이가 놓을 수 있는 평면에 맞았는지 찾는다.
    /// 물리 충돌체 대신 평면 데이터로 직접 계산한다:
    ///   1) 레이와 평면(무한히 넓은 면)이 만나는 점을 구하고
    ///   2) 그 점이 평면의 실제 테두리(boundary 다각형) 안에 있는지 확인한다.
    /// 충돌체의 앞면/뒷면 방향이나 메시 생성 시점에 영향을 받지 않아 에디터와 Quest에서 똑같이 동작한다.
    /// </summary>
    bool TryFindTarget(out ARPlane plane, out Pose pose)
    {
        plane = null;
        pose = default;
        if (m_RayOrigin == null || m_PlaneManager == null)
        {
            LogStatus("Ray Origin 또는 Plane Manager가 비어 있음");
            return false;
        }

        var ray = new Ray(m_RayOrigin.position, m_RayOrigin.forward);
        bool tableOnly = HasAnyTable();
        int planeCount = 0, hitCount = 0;
        float best = m_MaxDistance;

        foreach (var candidate in m_PlaneManager.trackables)
        {
            planeCount++;
            if (!candidate.infinitePlane.Raycast(ray, out float distance) || distance > best)
                continue;

            var point = ray.GetPoint(distance);
            if (!IsInsideBoundary(candidate, point))
                continue;

            hitCount++;
            if (!IsPlaceable(candidate, tableOnly))
                continue;

            best = distance;
            plane = candidate;
            pose = new Pose(point, FacePlayer(point));
        }

        if (plane == null)
            LogStatus($"평면 {planeCount}개, 레이가 맞은 평면 {hitCount}개, 책상만 허용={tableOnly}");
        return plane != null;
    }

    /// <summary>점이 평면 테두리 다각형 안에 있는지 (평면 기준 x·z 좌표로 판정)</summary>
    static bool IsInsideBoundary(ARPlane plane, Vector3 worldPoint)
    {
        var local = plane.transform.InverseTransformPoint(worldPoint);
        var p = new Vector2(local.x, local.z);
        var boundary = plane.boundary;
        if (!boundary.IsCreated || boundary.Length < 3)
            return false;

        // 짝홀 규칙: 점에서 오른쪽으로 선을 그었을 때 테두리를 홀수 번 지나면 안쪽이다.
        bool inside = false;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            var a = boundary[i];
            var b = boundary[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    float m_NextLogTime;

    /// <summary>놓을 곳을 못 찾을 때 이유를 1초에 한 번만 로그로 남긴다. (Quest에서는 MQDH 로그로 확인)</summary>
    void LogStatus(string message)
    {
        if (!m_DebugLog || Time.time < m_NextLogTime)
            return;
        m_NextLogTime = Time.time + 1f;
        Debug.Log($"[CatHousePlacer] 놓을 곳 없음: {message}");
    }

    static bool IsPlaceable(ARPlane plane, bool tableOnly)
    {
        if (plane.alignment != PlaneAlignment.HorizontalUp)
            return false;
        if (tableOnly)
            return (plane.classifications & PlaneClassifications.Table) != 0;
        return true;
    }

    bool HasAnyTable()
    {
        if (m_PlaneManager == null)
            return false;
        foreach (var p in m_PlaneManager.trackables)
        {
            if (p.alignment == PlaneAlignment.HorizontalUp && (p.classifications & PlaneClassifications.Table) != 0)
                return true;
        }
        return false;
    }

    /// <summary>놓는 물체가 플레이어 쪽을 보도록 수평 방향만 돌린다.</summary>
    static Quaternion FacePlayer(Vector3 point)
    {
        var cam = Camera.main;
        if (cam == null)
            return Quaternion.identity;
        var toPlayer = cam.transform.position - point;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
    }

    void OnPlacePressed(InputAction.CallbackContext _)
    {
        if (IsPlaced || !m_HasTarget)
            return;
        Place(m_TargetPlane, m_TargetPose);
    }

    void Place(ARPlane plane, Pose pose)
    {
        // 1) 평면에 앵커를 붙인다. 실패하면(시뮬레이션 등) 빈 오브젝트로 대신한다.
        Transform root = null;
        if (m_AnchorManager != null && m_AnchorManager.enabled)
        {
            var anchor = m_AnchorManager.AttachAnchor(plane, pose);
            if (anchor != null)
                root = anchor.transform;
        }
        if (root == null)
        {
            Debug.LogWarning("[CatHousePlacer] 앵커를 만들지 못해 일반 오브젝트로 고정합니다.");
            root = new GameObject("Cat Home (no anchor)").transform;
            root.SetPositionAndRotation(pose.position, pose.rotation);
        }

        // 2) 앵커 아래에 집과 고양이를 만든다.
        if (m_HousePrefab != null)
            Instantiate(m_HousePrefab, root, false);

        if (m_CatPrefab != null)
        {
            var cat = Instantiate(m_CatPrefab, root, false);
            cat.transform.localPosition = m_CatOffset;
            cat.transform.localRotation = Quaternion.identity;
            cat.transform.localScale = Vector3.one * m_CatScale;
            PlacedCat = cat.transform;
        }

        IsPlaced = true;
        PlacedPlane = plane;
        if (m_Reticle != null)
            m_Reticle.SetActive(false);

        // 3) 평면 시각화를 숨긴다.
        SetPlanesVisible(false);

        m_Placed.Invoke(PlacedCat);
    }

    void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        // 놓은 뒤에 새로 인식된 평면도 숨긴다.
        if (!IsPlaced)
            return;
        foreach (var plane in args.added)
            SetPlaneVisible(plane, false);
    }

    public void SetPlanesVisible(bool visible)
    {
        if (m_PlaneManager == null)
            return;
        foreach (var plane in m_PlaneManager.trackables)
            SetPlaneVisible(plane, visible);
    }

    static void SetPlaneVisible(ARPlane plane, bool visible)
    {
        // ARPlaneMeshVisualizer는 평면이 갱신될 때마다 렌더러를 다시 켜므로, 렌더러만 끄면 곧바로 다시 보인다.
        // 그래서 시각화 컴포넌트 자체를 끈다. 꺼지면 패키지가 알아서 MeshRenderer · LineRenderer를 숨긴다.
        // 충돌체(MeshCollider)는 켜진 채로 남는다. (모양은 끈 시점 그대로 고정된다)
        var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (visualizer != null)
        {
            visualizer.enabled = visible;
            return;
        }

        foreach (var r in plane.GetComponentsInChildren<Renderer>())
            r.enabled = visible;
    }
}
