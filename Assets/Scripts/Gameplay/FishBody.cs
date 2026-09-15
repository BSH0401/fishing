using System;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 플레이어와 AI 물고기가 공통으로 갖는 "몸".
    /// 포식 판정은 여기 있는 Size 하나만 보고 결정된다.
    /// 회전·유영 연출은 FishMotor가, 이동 결정은 PlayerFish / AIFish가 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FishBody : MonoBehaviour
    {
        [SerializeField] float size = 1f;
        [SerializeField] bool isPlayer = false;
        [SerializeField] bool isBoss = false;
        [SerializeField] SpriteRenderer spriteRenderer;
        [Tooltip("size 1일 때의 월드 스케일 배수")]
        [SerializeField] float visualScaleMultiplier = 1f;

        public bool IsAlive { get; private set; } = true;
        public bool IsPlayer => isPlayer;
        public bool IsBoss => isBoss;
        public SpriteRenderer Renderer => spriteRenderer;

        /// <summary>남은 마비 시간(초). 0보다 크면 움직이지 못한다.</summary>
        public float StunRemaining { get; private set; }
        public bool IsStunned => StunRemaining > 0f;

        /// <summary>잡아먹혔을 때 호출. 인자는 잡아먹은 쪽.</summary>
        public event Action<FishBody> OnConsumed;

        public float Size
        {
            get => size;
            set { size = Mathf.Max(0.01f, value); ApplyVisualScale(); }
        }

        public float VisualScaleMultiplier
        {
            get => visualScaleMultiplier;
            set { visualScaleMultiplier = Mathf.Max(0.01f, value); ApplyVisualScale(); }
        }

        /// <summary>포식 판정에 쓰는 몸통 반경 (월드 단위).</summary>
        public float BodyRadius => size * 0.5f;

        // ── 스케일 팝 (먹을 때 몸이 살짝 튕기는 연출) ─────────────
        [Header("연출")]
        [Tooltip("팝 지속 시간")]
        [SerializeField] float popDuration = 0.18f;
        float _popTimer;
        float _popStrength;

        void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            ApplyVisualScale();
        }

        void OnValidate() => ApplyVisualScale();

        void Update()
        {
            if (StunRemaining > 0f) StunRemaining -= Time.deltaTime;

            if (_popTimer > 0f)
            {
                // 히트스톱 중에도 보이게 unscaled
                _popTimer -= Time.unscaledDeltaTime;
                ApplyVisualScale();
            }
        }

        /// <summary>
        /// 몸을 잠깐 부풀렸다 되돌린다. 먹는 순간 이걸 넣으면 "씹었다"는 느낌이 산다.
        /// strength 0.15 = 최대 15% 부풀기.
        /// </summary>
        public void Pop(float strength = 0.15f)
        {
            _popStrength = Mathf.Max(_popStrength, strength);
            _popTimer = popDuration;
            ApplyVisualScale();
        }

        public void ApplyVisualScale()
        {
            float s = Mathf.Max(0.01f, size * visualScaleMultiplier);

            if (_popTimer > 0f && popDuration > 0f)
            {
                // 0 → 1 → 0 으로 부풀었다 돌아오는 곡선
                float t = 1f - Mathf.Clamp01(_popTimer / popDuration);
                float curve = Mathf.Sin(t * Mathf.PI);
                s *= 1f + _popStrength * curve;
                if (_popTimer <= 0f) _popStrength = 0f;
            }

            transform.localScale = new Vector3(s, s, 1f);
        }

        public void SetBoss(bool value) => isBoss = value;

        /// <summary>전기 충격 등으로 마비시킨다. 이미 더 긴 마비 중이면 유지.</summary>
        public void Stun(float seconds)
        {
            if (seconds <= 0f) return;
            StunRemaining = Mathf.Max(StunRemaining, seconds);
        }

        /// <summary>이 물고기가 eater에게 잡아먹혔다.</summary>
        public void Consume(FishBody eater)
        {
            if (!IsAlive) return;
            IsAlive = false;
            OnConsumed?.Invoke(eater);
        }

        /// <summary>풀에서 재사용할 때 되살린다.</summary>
        public void ResetBody()
        {
            IsAlive = true;
            StunRemaining = 0f;
            _popTimer = 0f;
            _popStrength = 0f;
            OnConsumed = null;
        }

        /// <summary>eater가 target을 먹을 수 있는가? tolerance는 GameDatabase.eatSizeTolerance.</summary>
        public static bool CanEat(FishBody eater, FishBody target, float tolerance)
        {
            if (eater == null || target == null) return false;
            if (!eater.IsAlive || !target.IsAlive) return false;
            return eater.Size * tolerance >= target.Size;
        }
    }
}
