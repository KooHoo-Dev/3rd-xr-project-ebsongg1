using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼 손맛: 레이를 올리면 살짝 커지고, 누르면 쏙 작아졌다가 놓으면 통통 튀며 돌아온다.
/// (색만 바뀌는 기본 Button 반응에 크기 변화를 더한다)
///
/// 컨트롤러 레이의 포인터 이벤트(XR UI Input Module)로 동작한다.
/// GameFlow가 패널 안 버튼에 없으면 붙인다. 시작 · 결과 화면 빌더도 버튼을 만들 때 붙인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class UIButtonBounce : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField, Tooltip("레이를 올렸을 때 크기")]
    private float m_HoverScale = 1.06f;

    [SerializeField, Tooltip("누르고 있을 때 크기")]
    private float m_PressScale = 0.9f;

    [SerializeField, Tooltip("목표 크기를 따라가는 빠르기 (클수록 빠르고 더 통통 튐)")]
    private float m_Stiffness = 400f;

    [SerializeField, Range(0f, 1f), Tooltip("튀는 정도를 줄이는 값 (1이면 튀지 않음)")]
    private float m_Damping = 0.45f;

    private Selectable m_Selectable;
    private Vector3 m_BaseScale;
    private bool m_Hover;
    private bool m_Pressed;
    private float m_Scale = 1f;
    private float m_Velocity;

    private void Awake()
    {
        m_Selectable = GetComponent<Selectable>();
        m_BaseScale = transform.localScale;
    }

    private void OnDisable()
    {
        m_Hover = false;
        m_Pressed = false;
        m_Scale = 1f;
        m_Velocity = 0f;
        transform.localScale = m_BaseScale;
    }

    public void OnPointerEnter(PointerEventData eventData) => m_Hover = true;

    public void OnPointerExit(PointerEventData eventData)
    {
        m_Hover = false;
        m_Pressed = false;
    }

    public void OnPointerDown(PointerEventData eventData) => m_Pressed = true;

    public void OnPointerUp(PointerEventData eventData) => m_Pressed = false;

    private void Update()
    {
        float target = 1f;
        if (m_Selectable.IsInteractable())
        {
            if (m_Pressed)
                target = m_PressScale;
            else if (m_Hover)
                target = m_HoverScale;
        }

        // 스프링: 목표를 살짝 넘었다가 돌아와서 통통 튀는 느낌이 난다.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        float damping = 2f * Mathf.Sqrt(m_Stiffness) * m_Damping;
        m_Velocity += ((target - m_Scale) * m_Stiffness - m_Velocity * damping) * dt;
        m_Scale += m_Velocity * dt;
        transform.localScale = m_BaseScale * m_Scale;
    }
}
