using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 고양이가 숨긴 간식 (말린 생선). 숨기기 · 건네주기 흐름은 SnackHunt가 맡고, 이 컴포넌트는 간식 한 개의 상태만 다룬다.
///
/// - 숨어 있는 동안: 바닥에 고정(kinematic)되어 있고 반짝이(m_Sparkle)가 켜져 있다.
/// - 처음 집으면: 찾은 것으로 표시하고 반짝이를 끈다.
/// - 집었다가 놓으면: 물리로 떨어진다. (XRI가 잡기 전 상태인 kinematic으로 되돌리므로 다음 프레임에 풀어 준다)
/// - 고양이에게 건네지면: 잡기를 끄고 다시 고정한다. SnackHunt가 작아지며 사라지게 한다.
///
/// 먹이(CatFood)와 달리 밥그릇 소켓에는 들어가지 않는다. (FoodBowl은 CatFood만 받는다)
/// 프리팹은 Tools › Here Kitty › 간식 찾기 만들기
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public class CatSnack : MonoBehaviour
{
    [SerializeField, Tooltip("숨어 있는 동안 켜 두는 반짝이. 찾으면 꺼진다")]
    private GameObject m_Sparkle;

    private XRGrabInteractable m_Grab;
    private Rigidbody m_Body;
    private bool m_WasHeld;

    /// <summary>지금 플레이어 손에 들려 있는지</summary>
    public bool IsHeld => m_Grab != null && m_Grab.isSelected;

    /// <summary>한 번이라도 집어서 찾았는지</summary>
    public bool WasFound { get; private set; }

    /// <summary>고양이에게 건네졌는지 (더 이상 잡을 수 없다)</summary>
    public bool IsGiven { get; private set; }

    private void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Body = GetComponent<Rigidbody>();
    }

    /// <summary>바닥에 숨긴다: 그 자리에 고정하고 반짝이를 켠다.</summary>
    public void Hide(Vector3 position, Quaternion rotation)
    {
        WasFound = false;
        IsGiven = false;
        Freeze();
        transform.SetPositionAndRotation(position, rotation);
        if (m_Grab != null)
            m_Grab.enabled = true;
        SetSparkle(true);
    }

    /// <summary>찾은 것으로 표시하고 반짝이를 끈다. (떨어진 간식을 제자리로 되돌릴 때 SnackHunt가 부른다)</summary>
    public void MarkFound()
    {
        WasFound = true;
        SetSparkle(false);
    }

    /// <summary>고양이에게 건넨다: 손에서 떼고 다시 잡을 수 없게 고정한다.</summary>
    public void Give()
    {
        IsGiven = true;
        // 잡기를 끄면 XRI가 손에서 놓게 한다.
        if (m_Grab != null)
            m_Grab.enabled = false;
        Freeze();
        SetSparkle(false);
    }

    private void Update()
    {
        bool held = IsHeld;
        if (held && WasFound == false)
        {
            WasFound = true;
            SetSparkle(false);
        }

        // 놓은 직후: XRI가 kinematic으로 되돌려 놓았으므로 물리로 떨어지게 풀어 준다.
        if (held == false && m_WasHeld && IsGiven == false && m_Body != null)
        {
            m_Body.isKinematic = false;
            m_Body.useGravity = true;
        }
        m_WasHeld = held;
    }

    private void Freeze()
    {
        if (m_Body == null)
            return;
        if (m_Body.isKinematic == false)
        {
            m_Body.linearVelocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
        }
        m_Body.isKinematic = true;
    }

    private void SetSparkle(bool on)
    {
        if (m_Sparkle != null)
            m_Sparkle.SetActive(on);
    }
}
