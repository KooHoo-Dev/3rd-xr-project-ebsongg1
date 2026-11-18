using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 먹이(말린 생선)가 나오는 상자. 고양이 집을 사이에 두고 밥그릇 반대쪽에 놓인다.
///
/// 흐름
/// 1. 상자 안에서 먹이가 톡 튀어나와 상자 위에 놓인다. (나오는 동안은 잡을 수 없다)
/// 2. 플레이어가 먹이를 집어서 밥그릇까지 가져간다.
/// 3. 먹이가 밥그릇에 들어가거나(CatFood.InBowl) 먹혀서 사라지면 m_RespawnDelay 뒤에 새 먹이가 나온다.
/// 먹이는 한 번에 하나만 꺼내 둔다. 책상 밖으로 떨어지면(m_FallDistance 아래) 지우고 상자에서 다시 꺼낸다.
/// </summary>
[DisallowMultipleComponent]
public class FoodBox : MonoBehaviour
{
    [SerializeField, Tooltip("먹이 프리팹 (CatFood + XRGrabInteractable + Rigidbody)")]
    private GameObject m_FoodPrefab;

    [SerializeField, Tooltip("먹이가 나와서 놓이는 자리 (상자 위)")]
    private Transform m_SpawnPoint;

    [SerializeField, Tooltip("먹이가 밥그릇에 들어간 뒤 새 먹이가 나오기까지 (초)")]
    private float m_RespawnDelay = 1.5f;

    [SerializeField, Tooltip("튀어나오는 시간 (초)")]
    private float m_PopTime = 0.45f;

    [SerializeField, Tooltip("튀어나올 때 올라가는 높이 (m)")]
    private float m_PopHeight = 0.04f;

    [SerializeField, Tooltip("먹이가 상자보다 이만큼 아래로 떨어지면 다시 꺼낸다 (m)")]
    private float m_FallDistance = 0.5f;

    [SerializeField, Tooltip("고양이가 이 반경 안으로는 들어오지 않는다 (m)")]
    private float m_AvoidRadius = 0.06f;

    private CatFood m_Food;
    private float m_SpawnAt;

    public float AvoidRadius => m_AvoidRadius;

    private void Start()
    {
        m_SpawnAt = Time.time + 0.5f;
    }

    private void Update()
    {
        // 먹이가 밥그릇에 들어갔으면 더 이상 이 상자 몫이 아니다.
        if (m_Food != null && m_Food.InBowl)
            ReleaseFood();

        CheckFallen();

        // 다른 이유로 먹이가 사라졌으면 (지워짐 등) 새로 꺼낼 시간을 잡는다.
        if (m_Food == null && m_SpawnAt < 0f)
            m_SpawnAt = Time.time + m_RespawnDelay;

        if (m_Food == null && m_SpawnAt > 0f && Time.time >= m_SpawnAt)
            StartCoroutine(PopFood());
    }

    private void ReleaseFood()
    {
        m_Food = null;
        m_SpawnAt = Time.time + m_RespawnDelay;
    }

    private void CheckFallen()
    {
        if (m_Food == null)
            return;
        if (m_Food.transform.position.y > transform.position.y - m_FallDistance)
            return;
        var grab = m_Food.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            return;

        Destroy(m_Food.gameObject);
        m_Food = null;
        m_SpawnAt = Time.time;
    }

    /// <summary>상자 안에서 작게 시작해 위로 톡 튀어나온 뒤 상자 위에 내려앉는다.</summary>
    private IEnumerator PopFood()
    {
        m_SpawnAt = -1f;
        if (m_FoodPrefab == null)
            yield break;

        var spawn = m_SpawnPoint != null ? m_SpawnPoint : transform;
        // 앵커 아래가 아니라 월드에 둔다. (던지거나 떨어뜨릴 때 앵커 위치 보정의 영향을 받지 않게)
        var go = Instantiate(m_FoodPrefab, spawn.position, spawn.rotation);
        m_Food = go.GetComponent<CatFood>();
        if (m_Food == null)
            m_Food = go.AddComponent<CatFood>();

        var grab = go.GetComponent<XRGrabInteractable>();
        var body = go.GetComponent<Rigidbody>();
        if (grab != null)
            grab.enabled = false;
        if (body != null)
            body.isKinematic = true;

        var fullScale = go.transform.localScale;
        float t = 0f;
        while (t < m_PopTime && go != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / m_PopTime);
            go.transform.localScale = fullScale * Mathf.SmoothStep(0.2f, 1f, k);
            // 포물선: 올라갔다가 제자리로
            go.transform.position = spawn.position + Vector3.up * (m_PopHeight * 4f * k * (1f - k));
            yield return null;
        }
        if (go == null)
            yield break;

        go.transform.localScale = fullScale;
        go.transform.position = spawn.position;
        if (body != null)
            body.isKinematic = false;
        if (grab != null)
            grab.enabled = true;
    }
}
