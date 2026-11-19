using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 메뉴 [Tools > Here Kitty > 효과음 칸 만들기] 한 번으로 씬에 "Sound Bank"(SoundBank)를 만들고
/// 효과음 종류(Sfx)마다 빈 칸을 하나씩 채운다. 칸에 소리 파일을 끌어다 넣기만 하면 된다.
///
/// - 이미 있으면 새로 만들지 않고, 새로 생긴 종류의 칸만 추가한다. (넣어 둔 소리는 그대로)
/// - Button 칸이 비어 있으면 XRI 예제의 Button Pop.wav를 넣어 둔다.
/// - 씬에 AudioListener가 없으면 메인 카메라에 붙인다. (없으면 아무 소리도 안 들린다)
/// </summary>
public static class SoundBankBuilder
{
    private const string k_ButtonClipPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/DemoAssets/Audio/Button Pop.wav";

    [MenuItem("Tools/Here Kitty/효과음 칸 만들기")]
    public static void Build()
    {
        var bank = Object.FindFirstObjectByType<SoundBank>();
        if (bank == null)
        {
            var go = new GameObject("Sound Bank");
            Undo.RegisterCreatedObjectUndo(go, "효과음 칸 만들기");
            bank = go.AddComponent<SoundBank>();
        }

        Undo.RecordObject(bank, "효과음 칸 만들기");
        bank.EnsureEntries();
        EditorUtility.SetDirty(bank);

        FillButtonClip(bank);
        EnsureListener();

        EditorSceneManager.MarkSceneDirty(bank.gameObject.scene);
        Selection.activeObject = bank.gameObject;
        EditorGUIUtility.PingObject(bank.gameObject);
        Debug.Log("[SoundBankBuilder] 씬에 Sound Bank를 만들었어요. Entries의 각 칸에 소리 파일을 넣고 씬을 저장하세요.");
    }

    private static void FillButtonClip(SoundBank bank)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_ButtonClipPath);
        if (clip == null)
            return;

        var so = new SerializedObject(bank);
        var entries = so.FindProperty("m_Entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("id").enumValueIndex != (int)Sfx.Button)
                continue;
            var clips = entry.FindPropertyRelative("clips");
            if (clips.arraySize == 0)
            {
                clips.arraySize = 1;
                clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            }
            break;
        }
        so.ApplyModifiedProperties();
    }

    private static void EnsureListener()
    {
        if (Object.FindFirstObjectByType<AudioListener>() != null)
            return;
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[SoundBankBuilder] 씬에 AudioListener도 메인 카메라도 없어요. 소리가 들리지 않을 수 있어요.");
            return;
        }
        Undo.AddComponent<AudioListener>(cam.gameObject);
        Debug.Log("[SoundBankBuilder] 메인 카메라에 AudioListener를 붙였어요.");
    }
}
