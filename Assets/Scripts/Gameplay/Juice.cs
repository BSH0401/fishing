using System.Collections;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 타격감 담당. 히트스톱과 화면 흔들림을 한 곳에서 관리한다.
    ///
    /// 어디서든 Juice.Hit(...) 한 줄로 부를 수 있게 자동 생성 싱글톤이다.
    /// 씬에 미리 배치할 필요가 없다.
    /// </summary>
    public class Juice : MonoBehaviour
    {
        static Juice _instance;

        public static Juice Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("~Juice") { hideFlags = HideFlags.HideAndDontSave };
                _instance = go.AddComponent<Juice>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        [Header("흔들림")]
        [Tooltip("트라우마 1일 때의 최대 흔들림 거리 (월드 단위 · 카메라 크기 비례)")]
        [SerializeField] float maxShakeOffset = 0.55f;
        [Tooltip("트라우마 1일 때의 최대 회전 (도)")]
        [SerializeField] float maxShakeRoll = 2.2f;
        [Tooltip("초당 트라우마 감소량. 클수록 빨리 잦아든다.")]
        [SerializeField] float traumaDecay = 2.4f;
        [SerializeField] float shakeFrequency = 26f;

        float _trauma;
        float _seed;
        bool _hitStopping;

        /// <summary>이번 프레임의 흔들림 오프셋. CameraFollow가 읽어간다.</summary>
        public static Vector2 ShakeOffset { get; private set; }
        /// <summary>이번 프레임의 흔들림 회전(도).</summary>
        public static float ShakeRoll { get; private set; }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _seed = Random.value * 100f;
        }

        void Update()
        {
            // 일시정지 중에도 잦아들어야 하므로 unscaled
            float dt = Time.unscaledDeltaTime;

            if (_trauma > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - traumaDecay * dt);

                // 트라우마 제곱 — 약한 흔들림은 거의 안 보이고 강한 것만 확 튄다
                float shake = _trauma * _trauma;
                float t = Time.unscaledTime * shakeFrequency;

                ShakeOffset = new Vector2(
                    (Mathf.PerlinNoise(_seed, t) * 2f - 1f) * maxShakeOffset * shake,
                    (Mathf.PerlinNoise(_seed + 17f, t) * 2f - 1f) * maxShakeOffset * shake);
                ShakeRoll = (Mathf.PerlinNoise(_seed + 41f, t) * 2f - 1f) * maxShakeRoll * shake;
            }
            else if (ShakeOffset != Vector2.zero || ShakeRoll != 0f)
            {
                ShakeOffset = Vector2.zero;
                ShakeRoll = 0f;
            }
        }

        // ══════════════════════════════════════════════════════════
        /// <summary>흔들림만. amount는 0~1.</summary>
        public static void Shake(float amount)
        {
            var j = Instance;
            j._trauma = Mathf.Clamp01(j._trauma + Mathf.Clamp01(amount));
        }

        /// <summary>화면을 아주 잠깐 멈춘다. 씹는 맛의 90%가 여기서 나온다.</summary>
        public static void HitStop(float seconds)
        {
            if (seconds <= 0f) return;
            var j = Instance;
            if (j._hitStopping) return;
            j.StartCoroutine(j.HitStopRoutine(seconds));
        }

        /// <summary>지금 히트스톱으로 시간이 멈춰 있는가. 멈춤이 끝나면 Juice가 스스로 timeScale을 되돌린다.</summary>
        public static bool IsHitStopping => _instance != null && _instance._hitStopping;

        /// <summary>히트스톱 + 흔들림을 한 번에.</summary>
        public static void Hit(float stopSeconds, float shakeAmount)
        {
            HitStop(stopSeconds);
            Shake(shakeAmount);
        }

        IEnumerator HitStopRoutine(float seconds)
        {
            // 이미 멈춰 있으면(일시정지) 건드리지 않는다
            if (Time.timeScale <= 0.01f) yield break;

            _hitStopping = true;
            Time.timeScale = 0f;

            yield return new WaitForSecondsRealtime(seconds);

            // 히트스톱 중에 플레이어가 ESC를 눌렀다면 그대로 멈춰 있어야 한다
            bool pausedMeanwhile = RunManager.Instance != null && RunManager.Instance.IsPaused;
            if (!pausedMeanwhile) Time.timeScale = 1f;

            _hitStopping = false;
        }

        /// <summary>씬을 넘어갈 때 남은 흔들림을 지운다.</summary>
        public static void Reset()
        {
            if (_instance == null) return;
            _instance._trauma = 0f;
            ShakeOffset = Vector2.zero;
            ShakeRoll = 0f;
        }
    }
}
