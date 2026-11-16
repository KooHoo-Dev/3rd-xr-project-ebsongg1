#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Quest의 공간 데이터(Space Setup의 책상·벽·바닥) 권한을 요청하고,
/// 허락받은 뒤에 평면 인식 같은 매니저 컴포넌트를 켠다.
/// 권한 없이 켜 두면 매니저가 아무것도 찾지 못하므로, 대상 컴포넌트는 Inspector에서 꺼 둔 상태로 시작한다.
/// (Meta OpenXR 패키지 문서의 Scene permission 예제를 바탕으로 함)
/// </summary>
public class ScenePermissionRequester : MonoBehaviour
{
    const string k_ScenePermission = "com.oculus.permission.USE_SCENE";

    [SerializeField]
    [Tooltip("권한을 받으면 켤 컴포넌트 (예: AR Plane Manager). Inspector에서 꺼 둔 상태로 넣는다.")]
    Behaviour[] m_EnableOnGranted;

    [SerializeField]
    [Tooltip("권한을 받았을 때 추가로 실행할 일")]
    UnityEvent m_PermissionGranted;

    [SerializeField]
    [Tooltip("사용자가 권한을 거부했을 때 실행할 일 (예: 안내 문구 띄우기)")]
    UnityEvent m_PermissionDenied;

    public bool IsGranted { get; private set; }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(k_ScenePermission))
        {
            OnGranted(k_ScenePermission);
            return;
        }

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += OnGranted;
        callbacks.PermissionDenied += OnDenied;

        Debug.Log($"[ScenePermission] 권한 요청: {k_ScenePermission}");
        Permission.RequestUserPermission(k_ScenePermission, callbacks);
#else
        // 에디터(XR Simulation 등)에서는 권한 개념이 없으므로 바로 켠다.
        OnGranted(k_ScenePermission);
#endif
    }

    void OnGranted(string permission)
    {
        Debug.Log($"[ScenePermission] 권한 허용됨: {permission}");
        IsGranted = true;

        foreach (var behaviour in m_EnableOnGranted)
        {
            if (behaviour != null)
                behaviour.enabled = true;
        }

        m_PermissionGranted.Invoke();
    }

    void OnDenied(string permission)
    {
        Debug.LogWarning($"[ScenePermission] 권한 거부됨: {permission}");
        m_PermissionDenied.Invoke();
    }
}
