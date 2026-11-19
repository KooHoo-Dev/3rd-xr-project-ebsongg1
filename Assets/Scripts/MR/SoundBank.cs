using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>효과음 종류. 순서를 바꾸면 Sound Bank의 칸이 어긋나니 새 소리는 맨 뒤에 추가한다.</summary>
public enum Sfx
{
    Meow,         // 야옹: 쓰다듬기 · 인사
    MeowNeed,     // 보채는 야옹: 배고픔 · 기분 나쁨 · 졸림이 시작될 때, 계속 안 돌봐 주면 가끔
    Eat,          // 냠냠: 밥그릇 · 간식 먹기
    Joy,          // 신난 소리: 기뻐하기 (어른)
    Purr,         // 골골 (반복): 자는 동안
    Hop,          // 폴짝: 방석에 오르내리기
    FoodPop,      // 뽁: 먹이 상자에서 먹이가 튀어나옴
    FoodInBowl,   // 톡: 먹이가 밥그릇에 쏙
    StageUp,      // 반짝: 성장 단계가 오름
    Dig,          // 사각사각: 땅 파기
    SnackSparkle, // 반짝 (반복): 숨겨진 간식 위치 알려 주기
    SnackFound,   // 찾았다: 간식을 처음 집음
    Goodbye,      // 작별 야옹: 엔딩에서 손 흔들기
    Button,       // 딸깍: 시작 · 다시 버튼 (2D)
    Place,        // 뿅: 고양이 집 놓기
    Hint,         // 띵: 안내 메시지가 뜰 때 (2D)
    Result,       // 징글: 결과 화면 (2D)
}

/// <summary>
/// 효과음 모음. 씬에 하나 두고(Tools › Here Kitty › 효과음 칸 만들기) 각 칸에 소리 파일을 넣는다.
///
/// - 칸이 비어 있으면 아무 소리도 내지 않는다. (소리 파일 없이도 오류 없이 돌아간다)
/// - 한 칸에 여러 개를 넣으면 재생할 때마다 랜덤으로 고르고, 음높이도 pitchJitter만큼 살짝 흔들어 덜 반복적으로 들리게 한다.
/// - 3D(spatial) 소리는 난 위치에서 들린다. 고양이 소리는 고양이 쪽에서, 간식 반짝이는 간식 쪽에서 들려서 방향을 알 수 있다.
///   2D 소리(UI)는 어디서나 같은 크기로 들린다.
/// - 같은 소리가 minInterval보다 자주 나지 않게 막는다. (쓰다듬기 연타 등)
/// - 원본 파일이 길면 maxDuration초만 틀고 끝을 살짝 줄이며 끊는다. (먹는 소리 등)
///
/// 사용법
///   SoundBank.Play(Sfx.Meow, transform.position);       // 한 번 재생
///   var loop = SoundBank.StartLoop(Sfx.Purr, transform); // 반복 재생 (따라다님)
///   SoundBank.StopLoop(loop);
/// </summary>
[DisallowMultipleComponent]
public class SoundBank : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        [HideInInspector] public string name;
        public Sfx id;

        [Tooltip("소리 파일. 여러 개 넣으면 랜덤으로 하나")]
        public AudioClip[] clips;

        [Range(0f, 1f)] public float volume = 1f;

        [Range(0f, 0.5f), Tooltip("재생할 때마다 음높이를 ± 이만큼 흔든다")]
        public float pitchJitter = 0.05f;

        [Tooltip("켜면 소리가 난 위치에서 들린다 (3D). 끄면 어디서나 같은 크기 (UI용 2D)")]
        public bool spatial = true;

        [Tooltip("같은 소리를 다시 내기까지 최소 간격 (초)")]
        public float minInterval = 0.1f;

        [Tooltip("원본이 길 때 이 시간(초)만 틀고 끝을 살짝 줄이며 끊는다. 0이면 끝까지 (반복 소리에는 쓰지 않는다)")]
        public float maxDuration = 0f;
    }

    [SerializeField, Tooltip("효과음 칸. 메뉴로 만들면 종류마다 한 칸씩 채워진다")]
    private Entry[] m_Entries = Array.Empty<Entry>();

    [Header("공통")]
    [SerializeField, Range(0f, 1f)] private float m_MasterVolume = 1f;

    [SerializeField, Tooltip("3D 소리가 이 거리까지는 같은 크기로 들린다 (m)")]
    private float m_MinDistance = 0.3f;

    [SerializeField, Tooltip("3D 소리가 이 거리부터는 더 작아지지 않는다 (m)")]
    private float m_MaxDistance = 6f;

    [SerializeField, Tooltip("한 번에 겹쳐 낼 수 있는 소리 개수")]
    private int m_PoolSize = 12;

    [SerializeField, Tooltip("maxDuration으로 끊을 때 소리를 줄여 가는 시간 (초)")]
    private float m_CutFade = 0.3f;

    private static SoundBank s_Instance;

    private readonly Dictionary<Sfx, Entry> m_Map = new Dictionary<Sfx, Entry>();
    private readonly Dictionary<Sfx, float> m_LastPlayed = new Dictionary<Sfx, float>();
    private AudioSource[] m_Pool;
    private Coroutine[] m_Cuts;
    private int m_Next;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Debug.LogWarning("[SoundBank] 씬에 Sound Bank가 두 개 있어요. 먼저 있던 것을 씁니다.");
            return;
        }
        s_Instance = this;

        foreach (var e in m_Entries)
        {
            if (e != null)
                m_Map[e.id] = e;
        }

        m_Pool = new AudioSource[Mathf.Max(1, m_PoolSize)];
        m_Cuts = new Coroutine[m_Pool.Length];
        for (int i = 0; i < m_Pool.Length; i++)
        {
            var go = new GameObject($"Sfx {i}");
            go.transform.SetParent(transform, false);
            m_Pool[i] = go.AddComponent<AudioSource>();
            m_Pool[i].playOnAwake = false;
        }
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    /// <summary>position에서 한 번 재생한다. 2D 소리면 position은 무시한다.</summary>
    public static void Play(Sfx id, Vector3 position)
    {
        if (s_Instance != null)
            s_Instance.PlayAt(id, position);
    }

    /// <summary>플레이어 머리 위치에서 한 번 재생한다. (UI 소리용)</summary>
    public static void Play(Sfx id)
    {
        var cam = Camera.main;
        Play(id, cam != null ? cam.transform.position : Vector3.zero);
    }

    /// <summary>
    /// follow를 따라다니며 반복 재생한다. 소리 파일이 없으면 null.
    /// 멈출 때는 StopLoop()에 돌려받은 AudioSource를 넘긴다.
    /// </summary>
    public static AudioSource StartLoop(Sfx id, Transform follow)
    {
        if (s_Instance == null || follow == null)
            return null;
        var entry = s_Instance.Find(id);
        var clip = PickClip(entry);
        if (clip == null)
            return null;

        var go = new GameObject($"Loop {id}");
        go.transform.SetParent(follow, false);
        var source = go.AddComponent<AudioSource>();
        s_Instance.Setup(source, entry, clip);
        source.loop = true;
        // 여러 개가 동시에 시작해도 똑같이 들리지 않게 시작 위치를 흩는다.
        source.time = Random.Range(0f, clip.length);
        source.Play();
        return source;
    }

    public static void StopLoop(AudioSource loop)
    {
        if (loop == null)
            return;
        loop.Stop();
        Destroy(loop.gameObject);
    }

    private void PlayAt(Sfx id, Vector3 position)
    {
        var entry = Find(id);
        var clip = PickClip(entry);
        if (clip == null)
            return;

        if (m_LastPlayed.TryGetValue(id, out float last) && Time.time - last < entry.minInterval)
            return;
        m_LastPlayed[id] = Time.time;

        int index = NextSource();
        var source = m_Pool[index];

        // 이 칸에서 이전 소리를 끊으려고 기다리던 것이 있으면 취소한다.
        if (m_Cuts[index] != null)
        {
            StopCoroutine(m_Cuts[index]);
            m_Cuts[index] = null;
        }

        source.transform.position = position;
        Setup(source, entry, clip);
        source.loop = false;
        source.Play();

        // 원본이 길면 maxDuration만큼만 틀고 끊는다. (pitch가 높으면 원본이 빨리 끝나므로 실제 길이로 비교)
        float length = clip.length / Mathf.Max(0.01f, source.pitch);
        if (entry.maxDuration > 0f && entry.maxDuration < length)
            m_Cuts[index] = StartCoroutine(CutAfter(index, entry.maxDuration));
    }

    /// <summary>seconds 뒤에 m_CutFade 동안 소리를 줄이고 멈춘다.</summary>
    private System.Collections.IEnumerator CutAfter(int index, float seconds)
    {
        var source = m_Pool[index];
        float fade = Mathf.Clamp(m_CutFade, 0f, seconds);
        yield return new WaitForSeconds(seconds - fade);

        float start = source.volume;
        for (float t = 0f; t < fade; t += Time.deltaTime)
        {
            source.volume = start * (1f - t / fade);
            yield return null;
        }
        source.Stop();
        source.volume = start;
        m_Cuts[index] = null;
    }

    private Entry Find(Sfx id)
    {
        return m_Map.TryGetValue(id, out var entry) ? entry : null;
    }

    private static AudioClip PickClip(Entry entry)
    {
        if (entry == null || entry.clips == null || entry.clips.Length == 0)
            return null;
        // 빈 칸이 섞여 있어도 되게 몇 번 다시 고른다.
        for (int i = 0; i < 4; i++)
        {
            var clip = entry.clips[Random.Range(0, entry.clips.Length)];
            if (clip != null)
                return clip;
        }
        foreach (var clip in entry.clips)
        {
            if (clip != null)
                return clip;
        }
        return null;
    }

    /// <summary>쉬고 있는 소리 칸의 번호. 모두 재생 중이면 가장 오래된 것을 끊고 쓴다.</summary>
    private int NextSource()
    {
        for (int i = 0; i < m_Pool.Length; i++)
        {
            int k = (m_Next + i) % m_Pool.Length;
            if (m_Pool[k].isPlaying == false)
            {
                m_Next = (k + 1) % m_Pool.Length;
                return k;
            }
        }
        int index = m_Next;
        m_Next = (m_Next + 1) % m_Pool.Length;
        return index;
    }

    private void Setup(AudioSource source, Entry entry, AudioClip clip)
    {
        source.clip = clip;
        source.volume = entry.volume * m_MasterVolume;
        source.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
        source.spatialBlend = entry.spatial ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = m_MinDistance;
        source.maxDistance = m_MaxDistance;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;
    }

    /// <summary>종류마다 한 칸씩 있게 채우고, 칸 이름을 종류 이름으로 맞춘다. (메뉴 · 인스펙터에서 부른다)</summary>
    public void EnsureEntries()
    {
        var old = new Dictionary<Sfx, Entry>();
        foreach (var e in m_Entries)
        {
            if (e != null && old.ContainsKey(e.id) == false)
                old[e.id] = e;
        }

        var ids = (Sfx[])Enum.GetValues(typeof(Sfx));
        var list = new Entry[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            if (old.TryGetValue(ids[i], out var e) == false)
                e = Defaults(ids[i]);
            e.id = ids[i];
            e.name = ids[i].ToString();
            list[i] = e;
        }
        m_Entries = list;
    }

    /// <summary>새 칸의 기본값. UI는 2D, 반복 소리는 작게, 자주 날 수 있는 소리는 간격을 둔다.</summary>
    private static Entry Defaults(Sfx id)
    {
        var e = new Entry { id = id };
        switch (id)
        {
            case Sfx.Meow:
                e.pitchJitter = 0.1f;
                e.minInterval = 0.8f;
                break;
            case Sfx.MeowNeed:
                e.pitchJitter = 0.08f;
                e.minInterval = 3f;
                break;
            case Sfx.Eat:
                e.volume = 0.7f;
                e.minInterval = 0.5f;
                e.maxDuration = 2.5f;
                break;
            case Sfx.Purr:
                e.volume = 0.5f;
                e.pitchJitter = 0f;
                break;
            case Sfx.Dig:
                e.volume = 0.7f;
                break;
            case Sfx.SnackSparkle:
                e.volume = 0.4f;
                e.pitchJitter = 0f;
                break;
            case Sfx.Button:
            case Sfx.Hint:
            case Sfx.Result:
            case Sfx.SnackFound:
                e.spatial = false;
                e.pitchJitter = 0f;
                e.volume = id == Sfx.Hint ? 0.5f : 0.8f;
                break;
        }
        return e;
    }

    private void Reset()
    {
        EnsureEntries();
    }

    private void OnValidate()
    {
        // 칸 이름이 비어 있으면(종류를 새로 추가한 뒤 등) 다시 맞춘다.
        bool needs = m_Entries == null || m_Entries.Length != Enum.GetValues(typeof(Sfx)).Length;
        if (needs == false)
        {
            foreach (var e in m_Entries)
            {
                if (e == null || e.name != e.id.ToString())
                {
                    needs = true;
                    break;
                }
            }
        }
        if (needs)
            EnsureEntries();
    }
}
