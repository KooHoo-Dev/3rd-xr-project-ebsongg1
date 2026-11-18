using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 고양이 집 옆 밥그릇.
///
/// 흐름
/// 1. 플레이어가 먹이 상자(FoodBox)에서 꺼낸 먹이를 밥그릇에 가져가면 XRSocketInteractor가 끌어당긴다.
/// 2. 들어온 먹이는 소켓에서 떼어 밥그릇에 고정한다. (다시 집을 수 없게 잡기를 끈다)
///    고양이(CatEating)는 HasFood를 보고 걸어와서 먹는다.
/// 3. 고양이가 Consume()을 부르면 먹이가 작아지며 사라진다.
/// 밥그릇이 비어 있을 때만 먹이를 받는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRSocketInteractor))]
public class FoodBowl : MonoBehaviour
{
    [SerializeField, Tooltip("밥그릇 안에서 먹이가 놓일 자리")]
    private Transform m_FoodSlot;

    [SerializeField, Tooltip("고양이가 돌아다닐 때 이 반경 안으로는 들어오지 않는다 (m). 먹으러 올 때는 예외")]
    private float m_AvoidRadius = 0.04f;

    [SerializeField, Tooltip("상태가 바뀔 때 로그 남기기 (디버그용)")]
    private bool m_DebugLog = false;

    private XRSocketInteractor m_Socket;
    private CatFood m_FoodInBowl;    // 밥그릇에 들어간 먹이
    private CatFood m_PendingFood;   // 이번 프레임에 소켓에 들어온 먹이 (다음 Update에서 고정)

    /// <summary>밥그릇에 먹이가 있는지. 고양이가 이걸 보고 먹으러 온다.</summary>
    public bool HasFood => m_FoodInBowl != null;

    public float AvoidRadius => m_AvoidRadius;

    private void Awake()
    {
        m_Socket = GetComponent<XRSocketInteractor>();
        if (m_FoodSlot != null)
            m_Socket.attachTransform = m_FoodSlot;
    }

    private void OnEnable()
    {
        m_Socket.selectEntered.AddListener(OnSelectEntered);
    }

    private void OnDisable()
    {
        m_Socket.selectEntered.RemoveListener(OnSelectEntered);
    }

    /// <summary>고양이를 연결한다. CatHousePlacer가 집 · 고양이 · 밥그릇을 놓은 뒤 부른다.</summary>
    public void SetCat(CatEating cat)
    {
        if (cat != null)
            cat.SetBowl(this);
    }

    /// <summary>고양이가 먹기 시작할 때 부른다. duration 동안 먹이가 작아지다 사라진다. 먹이의 배고픔 감소량을 돌려준다.</summary>
    public float Consume(float duration)
    {
        if (m_FoodInBowl == null)
            return -1f;
        var food = m_FoodInBowl;
        m_FoodInBowl = null;
        float amount = food.HungerAmount;
        StartCoroutine(ShrinkAndDestroy(food.gameObject, duration));
        return amount;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        var food = args.interactableObject.transform.GetComponent<CatFood>();
        if (food == null || HasFood || m_PendingFood != null)
            return;
        // 선택 이벤트 안에서 바로 선택을 끊으면 XRI 처리 순서가 꼬일 수 있어서 다음 Update에서 처리한다.
        m_PendingFood = food;
    }

    private void Update()
    {
        if (m_PendingFood != null)
            LockInBowl(m_PendingFood);

        // 밥그릇이 비어 있을 때만 소켓이 먹이를 받는다.
        m_Socket.socketActive = HasFood == false;
    }

    /// <summary>소켓에서 떼어 밥그릇 자리에 고정하고, 다시 집을 수 없게 한다.</summary>
    private void LockInBowl(CatFood food)
    {
        m_PendingFood = null;
        if (food == null)
            return;

        var grab = food.GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            if (grab.isSelected && m_Socket.interactionManager != null)
                m_Socket.interactionManager.SelectExit((IXRSelectInteractor)m_Socket, (IXRSelectInteractable)grab);
            grab.enabled = false;
        }

        var body = food.GetComponent<Rigidbody>();
        if (body != null)
        {
            if (body.isKinematic == false)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }

        var slot = m_FoodSlot != null ? m_FoodSlot : transform;
        food.transform.SetParent(slot, true);
        food.transform.SetPositionAndRotation(slot.position, slot.rotation);

        food.InBowl = true;
        m_FoodInBowl = food;
        Log("먹이가 밥그릇에 들어옴");
    }

    private IEnumerator ShrinkAndDestroy(GameObject food, float duration)
    {
        var start = food.transform.localScale;
        float t = 0f;
        while (t < duration && food != null)
        {
            t += Time.deltaTime;
            food.transform.localScale = start * (1f - Mathf.Clamp01(t / duration));
            yield return null;
        }
        if (food != null)
            Destroy(food);
    }

    private void Log(string message)
    {
        if (m_DebugLog)
            Debug.Log($"[FoodBowl] {message}");
    }
}
