using UnityEngine;

/// <summary>
/// 고양이 먹이 표시. 밥그릇(FoodBowl)은 이 컴포넌트가 붙은 물체만 받는다.
/// 잡기는 같은 오브젝트의 XRGrabInteractable + Rigidbody가 맡는다. (Tools › Here Kitty › 먹이 · 밥그릇 · 방석 만들기)
/// 먹이는 먹이 상자(FoodBox)에서 나온다.
/// </summary>
[DisallowMultipleComponent]
public class CatFood : MonoBehaviour
{
    [SerializeField, Tooltip("먹으면 배고픔이 이만큼 줄어든다 (0 ~ 1). 음수면 CatNeeds의 기본값")]
    private float m_HungerAmount = -1f;

    public float HungerAmount => m_HungerAmount;

    /// <summary>밥그릇에 들어갔는지. FoodBowl이 정하고, FoodBox는 이걸 보고 새 먹이를 꺼낸다.</summary>
    public bool InBowl { get; set; }
}
