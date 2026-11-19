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

    [SerializeField, Tooltip("처음 놓을 때 고양이 크기 (아기 단계). 성장 단계: 아기 0.06 → 어린이 0.08 → 어른 0.1")]
    float m_CatScale = 0.06f;

    [SerializeField, Tooltip("집 기준으로 고양이를 놓을 위치 (집 문 앞, 책상 높이)")]
    Vector3 m_CatOffset = new Vector3(0f, 0f, 0.105f);

    [SerializeField, Tooltip("밥그릇 프리팹 (Tools › Here Kitty › 먹이 · 밥그릇 · 방석 만들기). 비워 두면 밥그릇 없이 놓는다")]
    private GameObject m_BowlPrefab;

    [SerializeField, Tooltip("집 기준으로 밥그릇을 놓을 위치. 평면 밖이면 반대쪽(x 뒤집기)에 놓는다")]
    private Vector3 m_BowlOffset = new Vector3(0.13f, 0f, 0.04f);

    [SerializeField, Tooltip("먹이 상자 프리팹 (Tools › Here Kitty › 먹이 · 밥그릇 · 방석 만들기). 비워 두면 먹이가 나오지 않는다")]
    private GameObject m_FoodBoxPrefab;

    [SerializeField, Tooltip("집 기준으로 먹이 상자를 놓을 위치. x는 밥그릇 반대쪽으로 자동으로 뒤집힌다. 평면 밖이면 다른 자리를 찾는다")]
    private Vector3 m_FoodBoxOffset = new Vector3(0.24f, 0f, 0.02f);

    [SerializeField, Tooltip("방석 프리팹 (Tools › Here Kitty › 먹이 · 밥그릇 · 방석 만들기). 비워 두면 방석 없이 놓는다")]
    private GameObject m_CushionPrefab;

    [SerializeField, Tooltip("방석 제자리 후보 (집 기준). x가 양수면 먹이 상자 쪽. 위에서부터 평면 안이고 다른 물건과 안 겹치는 첫 자리")]
    private Vector3[] m_CushionOffsets =
    {
        new Vector3(0.15f, 0f, -0.15f),
        new Vector3(0.26f, 0f, -0.13f),
        new Vector3(-0.13f, 0f, -0.14f),
        new Vector3(0.12f, 0f, 0.23f),
    };

    [Header("이벤트")]
    [SerializeField, Tooltip("다 놓은 뒤 실행. 고양이 Transform이 넘어온다")]
    UnityEvent<Transform> m_Placed;

    ARPlane m_TargetPlane;
    Pose m_TargetPose;
    bool m_HasTarget;

    public bool IsPlaced { get; private set; }
    public Transform PlacedCat { get; private set; }
    public ARPlane PlacedPlane { get; private set; }

    private void Awake()
    {
        if (m_Reticle != null)
        {
            // 미리보기 표시가 레이를 가로막지 않게 충돌체를 끈다.
            foreach (var col in m_Reticle.GetComponentsInChildren<Collider>())
                col.enabled = false;
            m_Reticle.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (m_PlaceAction.action != null)
        {
            m_PlaceAction.action.Enable();
            m_PlaceAction.action.performed += OnPlacePressed;
        }

        if (m_PlaneManager != null)
            m_PlaneManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    private void OnDisable()
    {
        if (m_PlaceAction.action != null)
            m_PlaceAction.action.performed -= OnPlacePressed;

        if (m_PlaneManager != null)
            m_PlaneManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    private void Update()
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
    private bool TryFindTarget(out ARPlane plane, out Pose pose)
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
            if (candidate.infinitePlane.Raycast(ray, out float distance) == false || distance > best)
                continue;

            var point = ray.GetPoint(distance);
            if (IsInsideBoundary(candidate, point) == false)
                continue;

            hitCount++;
            if (IsPlaceable(candidate, tableOnly) == false)
                continue;

            best = distance;
            plane = candidate;
            pose = new Pose(point, FacePlayer(point));
        }

        if (plane == null)
            LogStatus($"평면 {planeCount}개, 레이가 맞은 평면 {hitCount}개, 책상만 허용={tableOnly}");
        return plane != null;
    }

    /// <summary>점이 평면 테두리 다각형 안에 있는지 (평면 기준 x·z 좌표로 판정). CatWander도 가장자리 판정에 쓴다.</summary>
    public static bool IsInsideBoundary(ARPlane plane, Vector3 worldPoint)
    {
        var local = plane.transform.InverseTransformPoint(worldPoint);
        var p = new Vector2(local.x, local.z);
        var boundary = plane.boundary;
        if (boundary.IsCreated == false || boundary.Length < 3)
            return false;

        // 짝홀 규칙: 점에서 오른쪽으로 선을 그었을 때 테두리를 홀수 번 지나면 안쪽이다.
        bool inside = false;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            var a = boundary[i];
            var b = boundary[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = (inside == false); // true↔false 뒤집기
        }
        return inside;
    }

    float m_NextLogTime;

    /// <summary>놓을 곳을 못 찾을 때 이유를 1초에 한 번만 로그로 남긴다. (Quest에서는 MQDH 로그로 확인)</summary>
    private void LogStatus(string message)
    {
        if (m_DebugLog == false || Time.time < m_NextLogTime)
            return;
        m_NextLogTime = Time.time + 1f;
        Debug.Log($"[CatHousePlacer] 놓을 곳 없음: {message}");
    }

    private static bool IsPlaceable(ARPlane plane, bool tableOnly)
    {
        if (plane.alignment != PlaneAlignment.HorizontalUp)
            return false;
        if (tableOnly)
            return (plane.classifications & PlaneClassifications.Table) != 0;
        return true;
    }

    private bool HasAnyTable()
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
    private static Quaternion FacePlayer(Vector3 point)
    {
        var cam = Camera.main;
        if (cam == null)
            return Quaternion.identity;
        var toPlayer = cam.transform.position - point;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
    }

    private void OnPlacePressed(InputAction.CallbackContext _)
    {
        if (IsPlaced || m_HasTarget == false)
            return;
        Place(m_TargetPlane, m_TargetPose);
    }

    private void Place(ARPlane plane, Pose pose)
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

            // 놓은 평면 안에서 돌아다니기 시작. 프리팹에 CatWander가 없으면 기본값으로 붙인다.
            if (cat.TryGetComponent(out CatWander wander) == false)
                wander = cat.AddComponent<CatWander>();
            wander.Begin(plane, root);

            // 상태에 따라 나는 소리 (보채기 · 골골 · 성장)
            if (cat.GetComponent<CatSounds>() == null)
                cat.AddComponent<CatSounds>();

            // 쓰다듬기 하트 · 성장 반짝이
            if (cat.GetComponent<CatEffects>() == null)
                cat.AddComponent<CatEffects>();
        }

        PlaceFeeding(plane, root);

        IsPlaced = true;
        SoundBank.Play(Sfx.Place, pose.position);
        PlacedPlane = plane;
        if (m_Reticle != null)
            m_Reticle.SetActive(false);

        // 3) 평면 시각화를 숨긴다.
        SetPlanesVisible(false);

        m_Placed.Invoke(PlacedCat);
    }

    /// <summary>
    /// 집 한쪽에 밥그릇, 반대쪽 조금 떨어진 곳에 먹이 상자를 놓는다.
    /// 플레이어가 상자에서 먹이를 꺼내 집 건너편 밥그릇까지 옮기게 하려는 배치.
    /// 둘 다 고양이가 피해 다니도록 등록하고, 밥그릇은 고양이와 연결한다.
    /// </summary>
    private void PlaceFeeding(ARPlane plane, Transform root)
    {
        var cat = PlacedCat;
        var wander = cat != null ? cat.GetComponent<CatWander>() : null;
        float side = 1f; // 밥그릇이 놓인 쪽 (+x 또는 -x)

        if (m_BowlPrefab != null)
        {
            var offset = PickOffset(plane, root,
                m_BowlOffset,
                Mirror(m_BowlOffset));
            side = Mathf.Sign(offset.x == 0f ? 1f : offset.x);

            var bowl = Spawn(m_BowlPrefab, root, offset);
            if (bowl.TryGetComponent(out FoodBowl foodBowl))
            {
                if (wander != null)
                    wander.AddObstacle(bowl.transform, foodBowl.AvoidRadius);
                if (cat != null)
                {
                    if (cat.TryGetComponent(out CatEating eating) == false)
                        eating = cat.gameObject.AddComponent<CatEating>();
                    foodBowl.SetCat(eating);
                }
            }
        }

        if (m_FoodBoxPrefab != null)
        {
            // 밥그릇 반대쪽 → 같은 쪽 더 멀리 → 집 앞쪽 순서로 평면 안인 자리를 찾는다.
            var far = new Vector3(-side * Mathf.Abs(m_FoodBoxOffset.x), m_FoodBoxOffset.y, m_FoodBoxOffset.z);
            var offset = PickOffset(plane, root,
                far,
                far * 0.75f,
                new Vector3(side * (Mathf.Abs(m_BowlOffset.x) + 0.12f), m_FoodBoxOffset.y, m_BowlOffset.z),
                new Vector3(-side * 0.08f, m_FoodBoxOffset.y, 0.2f));

            var box = Spawn(m_FoodBoxPrefab, root, offset);
            if (wander != null && box.TryGetComponent(out FoodBox foodBox))
                wander.AddObstacle(box.transform, foodBox.AvoidRadius);
        }

        PlaceCushion(root, cat, wander, -side);
    }

    /// <summary>
    /// 먹이 상자 쪽(boxSide) 빈자리에 방석을 놓고 고양이와 연결한다.
    /// 후보 자리 중 방석 테두리까지 걸어 다닐 영역 안이고, 집 · 밥그릇 · 상자와 겹치지 않는 첫 자리.
    /// </summary>
    private void PlaceCushion(Transform root, Transform cat, CatWander wander, float boxSide)
    {
        if (m_CushionPrefab == null || m_CushionOffsets == null || m_CushionOffsets.Length == 0)
            return;

        const float radius = 0.055f; // 방석 반지름 + 여유
        var chosen = m_CushionOffsets[0];
        foreach (var c in m_CushionOffsets)
        {
            var local = new Vector3(c.x * boxSide, c.y, c.z);
            var p = root.TransformPoint(local);
            bool clear = wander == null
                || (wander.IsBlocked(p, null, radius) == false && wander.IsOnWalkableSurface(p)
                    && wander.IsOnWalkableSurface(p + root.right * radius) && wander.IsOnWalkableSurface(p - root.right * radius)
                    && wander.IsOnWalkableSurface(p + root.forward * radius) && wander.IsOnWalkableSurface(p - root.forward * radius));
            if (clear)
            {
                chosen = local;
                break;
            }
        }

        var go = Spawn(m_CushionPrefab, root, chosen);
        if (go.TryGetComponent(out Cushion cushion) == false)
            return;
        cushion.Init(root, wander);

        if (cat != null)
        {
            if (cat.TryGetComponent(out CatSleeping sleeping) == false)
                sleeping = cat.gameObject.AddComponent<CatSleeping>();
            sleeping.SetCushion(cushion);
        }
    }

    /// <summary>후보 중 평면 안인 첫 자리. 모두 밖이면 첫 후보.</summary>
    private static Vector3 PickOffset(ARPlane plane, Transform root, params Vector3[] candidates)
    {
        foreach (var c in candidates)
        {
            if (IsInsideBoundary(plane, root.TransformPoint(c)))
                return c;
        }
        return candidates[0];
    }

    private static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

    private static GameObject Spawn(GameObject prefab, Transform root, Vector3 localPosition)
    {
        var go = Instantiate(prefab, root, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        // 놓은 뒤에 새로 인식된 평면도 숨긴다.
        if (IsPlaced == false)
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

    private static void SetPlaneVisible(ARPlane plane, bool visible)
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
