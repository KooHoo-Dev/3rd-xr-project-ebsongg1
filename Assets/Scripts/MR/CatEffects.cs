using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 고양이 반응 효과 (그림 파일 없이 코드로 그린 스프라이트를 띄워 움직인다)
///
/// - 쓰다듬기: 한 번 쓰다듬을 때마다 머리 위로 하트가 살랑살랑 떠오르다 흐려진다. (m_HeartInterval보다 자주 나오지 않는다)
/// - 성장: 단계가 오르면 몸 주변으로 금색 반짝이가 팡 퍼졌다가 반짝이며 사라진다.
///
/// 하트 모양은 머리 위 기분 아이콘과 같다. (CatNeedsIcons.DrawHeart)
/// 효과는 고양이 자식이 아니라 따로 떠 있어서 고양이가 움직여도 제자리에서 퍼진다. 항상 카메라를 바라본다.
/// 크기는 고양이 크기에 맞춰 같이 커진다. (m_ReferenceScale 기준)
/// CatHousePlacer가 고양이를 놓을 때 없으면 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class CatEffects : MonoBehaviour
{
    [Header("쓰다듬기 하트")]
    [SerializeField, Tooltip("하트가 나오는 최소 간격 (초)")]
    private float m_HeartInterval = 0.35f;

    [SerializeField, Tooltip("하트 크기 (m)")]
    private float m_HeartSize = 0.016f;

    [SerializeField, Tooltip("하트가 떠오르는 높이 (m)")]
    private float m_HeartRise = 0.04f;

    [SerializeField, Tooltip("하트가 보이는 시간 (초)")]
    private float m_HeartLife = 1.1f;

    [SerializeField] private Color m_HeartColor = new Color(0.95f, 0.35f, 0.47f);

    [Header("성장 반짝이")]
    [SerializeField] private int m_SparkleCount = 16;

    [SerializeField, Tooltip("반짝이 한 개 크기 (m)")]
    private float m_SparkleSize = 0.014f;

    [SerializeField, Tooltip("반짝이가 퍼지는 거리 (m)")]
    private float m_SparkleSpread = 0.07f;

    [SerializeField, Tooltip("반짝이가 보이는 시간 (초)")]
    private float m_SparkleLife = 1f;

    [SerializeField] private Color m_SparkleColor = new Color(1f, 0.84f, 0.35f);

    [Header("크기 기준")]
    [SerializeField, Tooltip("위 크기 · 거리 값의 기준이 되는 고양이 크기")]
    private float m_ReferenceScale = 0.06f;

    private class Puff
    {
        public Transform t;
        public SpriteRenderer r;
        public Color color;
        public Vector3 velocity;
        public float size;
        public float age;
        public float life;
        public float spin;
        public float swayPhase;
        public float lastSide;
        public bool heart;
    }

    private static Sprite s_Heart;
    private static Sprite s_Sparkle;

    private readonly List<Puff> m_Puffs = new List<Puff>();
    private CatPetting m_Petting;
    private CatGrowth m_Growth;
    private Transform m_Head;
    private int m_LastPetCount;
    private float m_NextHeartAt;

    private void Awake()
    {
        m_Petting = GetComponent<CatPetting>();
        m_Growth = GetComponent<CatGrowth>();
        m_Head = FindChild(transform, "head");
        if (m_Head == null)
            m_Head = transform;
        if (s_Heart == null)
            s_Heart = CatNeedsIcons.MakeSprite("EffectHeart", CatNeedsIcons.DrawHeart);
        if (s_Sparkle == null)
            s_Sparkle = CatNeedsIcons.MakeSprite("EffectSparkle", DrawSparkle);
    }

    private void OnEnable()
    {
        if (m_Growth != null)
            m_Growth.StageChanged += OnStageChanged;
        if (m_Petting != null)
            m_LastPetCount = m_Petting.PetCount;
    }

    private void OnDisable()
    {
        if (m_Growth != null)
            m_Growth.StageChanged -= OnStageChanged;
    }

    private void OnDestroy()
    {
        foreach (var puff in m_Puffs)
        {
            if (puff.t != null)
                Destroy(puff.t.gameObject);
        }
        m_Puffs.Clear();
    }

    private float Scale => m_ReferenceScale > 0f ? transform.lossyScale.x / m_ReferenceScale : 1f;

    private void Update()
    {
        // 쓰다듬은 횟수가 늘면 하트 (CatPetting을 고치지 않고 횟수만 본다)
        if (m_Petting != null && m_Petting.PetCount != m_LastPetCount)
        {
            m_LastPetCount = m_Petting.PetCount;
            if (Time.time >= m_NextHeartAt)
            {
                m_NextHeartAt = Time.time + m_HeartInterval;
                SpawnHeart();
            }
        }
    }

    private void LateUpdate()
    {
        var cam = Camera.main;
        float dt = Time.deltaTime;
        for (int i = m_Puffs.Count - 1; i >= 0; i--)
        {
            var puff = m_Puffs[i];
            puff.age += dt;
            if (puff.t == null || puff.age >= puff.life)
            {
                if (puff.t != null)
                    Destroy(puff.t.gameObject);
                m_Puffs.RemoveAt(i);
                continue;
            }
            if (puff.age < 0f)
                continue; // 아직 차례가 안 됨 (반짝이를 조금씩 엇갈려 터뜨린다)

            float k = puff.age / puff.life;
            Animate(puff, k, dt);

            if (cam != null)
            {
                var face = Quaternion.LookRotation(puff.t.position - cam.transform.position, cam.transform.up);
                puff.t.rotation = face * Quaternion.Euler(0f, 0f, puff.spin * puff.age);
            }
        }
    }

    private void Animate(Puff puff, float k, float dt)
    {
        float scale;
        float alpha;
        if (puff.heart)
        {
            // 위로 떠오르며 좌우로 살랑, 처음엔 통 하고 커지고 끝에서 흐려진다.
            puff.velocity *= Mathf.Exp(-1.5f * dt);
            // 좌우 흔들림은 지난 프레임과의 차이만큼만 옮긴다. (제자리를 기준으로 살랑살랑)
            float side = Mathf.Sin(puff.age * 6f + puff.swayPhase) * puff.size * 0.6f;
            puff.t.position += puff.velocity * dt + CamRight() * (side - puff.lastSide);
            puff.lastSide = side;
            scale = k < 0.15f ? EaseOutBack(k / 0.15f) : 1f;
            alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
        }
        else
        {
            // 바깥으로 팡 퍼진 뒤 느려지고, 반짝반짝 깜빡이며 작아진다.
            puff.velocity *= Mathf.Exp(-4f * dt);
            puff.t.position += puff.velocity * dt;
            scale = (k < 0.12f ? EaseOutBack(k / 0.12f) : 1f - (k - 0.12f) / 0.88f * 0.7f);
            scale *= 0.85f + 0.15f * Mathf.Sin(puff.age * 25f + puff.swayPhase);
            alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
        }
        puff.t.localScale = Vector3.one * (puff.size * Mathf.Max(0f, scale));
        var c = puff.color;
        c.a *= Mathf.Clamp01(alpha);
        puff.r.color = c;
    }

    private void SpawnHeart()
    {
        float s = Scale;
        // 머리 위 상태 아이콘과 겹치지 않게 머리 옆 · 조금 앞쪽(플레이어 쪽)에서 나온다.
        float side = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.012f, 0.026f);
        var start = m_Head.position + Vector3.up * (0.02f * s) + CamRight() * (side * s) + ToCamera(m_Head.position) * (0.015f * s);
        var puff = Spawn("Heart Effect", s_Heart, m_HeartColor, start, m_HeartSize * s * Random.Range(0.85f, 1.1f), m_HeartLife);
        puff.heart = true;
        puff.velocity = Vector3.up * (m_HeartRise * s / m_HeartLife * 1.6f);
        puff.swayPhase = Random.Range(0f, Mathf.PI * 2f);
        puff.lastSide = Mathf.Sin(puff.swayPhase) * puff.size * 0.6f;
    }

    private void OnStageChanged(CatStage stage)
    {
        float s = Scale;
        var center = Vector3.Lerp(transform.position, m_Head.position, 0.6f);
        for (int i = 0; i < m_SparkleCount; i++)
        {
            // 위쪽으로 조금 더 많이 퍼지게
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
            dir.Normalize();
            var color = Color.Lerp(m_SparkleColor, Color.white, Random.Range(0f, 0.5f));
            var puff = Spawn("Sparkle Effect", s_Sparkle, color, center + dir * (0.01f * s),
                m_SparkleSize * s * Random.Range(0.6f, 1.2f), m_SparkleLife * Random.Range(0.8f, 1.1f));
            puff.velocity = dir * (m_SparkleSpread * s * Random.Range(3f, 4.5f));
            puff.spin = Random.Range(-90f, 90f);
            puff.swayPhase = Random.Range(0f, Mathf.PI * 2f);
            puff.age = -Random.Range(0f, 0.12f);
        }
    }

    private Puff Spawn(string objectName, Sprite sprite, Color color, Vector3 position, float size, float life)
    {
        var go = new GameObject(objectName);
        go.transform.position = position;
        go.transform.localScale = Vector3.zero;
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.color = new Color(color.r, color.g, color.b, 0f);
        r.sortingOrder = 20;
        var puff = new Puff { t = go.transform, r = r, color = color, size = size, life = life };
        m_Puffs.Add(puff);
        return puff;
    }

    private static Vector3 CamRight()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.right : Vector3.right;
    }

    /// <summary>from에서 카메라 쪽 수평 방향</summary>
    private static Vector3 ToCamera(Vector3 from)
    {
        var cam = Camera.main;
        if (cam == null)
            return Vector3.zero;
        var v = cam.transform.position - from;
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
    }

    /// <summary>네 꼭짓점이 뾰족하고 옆이 오목한 반짝 별 (|x|^½ + |y|^½ &lt; r^½)</summary>
    private static bool DrawSparkle(Vector2 p)
    {
        return Mathf.Sqrt(Mathf.Abs(p.x)) + Mathf.Sqrt(Mathf.Abs(p.y)) < Mathf.Sqrt(0.46f);
    }

    /// <summary>끝에서 살짝 넘쳤다가 돌아오는 곡선 (팝 느낌)</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (root.name == childName)
            return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, childName);
            if (found != null)
                return found;
        }
        return null;
    }
}
