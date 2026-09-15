using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 물고기 한 마리의 "헤엄치는 느낌"을 전담한다. 플레이어와 AI가 함께 쓴다.
    ///
    /// 핵심 3가지:
    ///  1) 가속과 감속을 따로 둔다 — 물속이라 멈출 땐 미끄러지듯 느려진다
    ///  2) 선회 속도를 제한한다 — 방향키를 꺾어도 즉시 도는 게 아니라 호를 그린다
    ///  3) 진행 방향으로 몸을 회전시키고 꼬리를 흔든다 — 좌우 반전만 하면 뻣뻣해 보인다
    ///
    /// 콜라이더는 원이라 회전해도 물리에 영향이 없다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [DisallowMultipleComponent]
    public class FishMotor : MonoBehaviour
    {
        [Header("가감속")]
        [Tooltip("목표 속도까지 붙는 가속도 (유닛/초²)")]
        [Min(0.1f)] public float acceleration = 30f;
        [Tooltip("입력이 없을 때 물의 저항으로 감속하는 정도")]
        [Min(0.1f)] public float drag = 6f;
        [Tooltip("방향을 크게 꺾을수록 속도가 줄어드는 정도. 0이면 감속 없음.")]
        [Range(0f, 1f)] public float turnSpeedLoss = 0.45f;

        [Header("선회")]
        [Tooltip("몸이 도는 최대 각속도 (도/초)")]
        [Min(10f)] public float turnRate = 420f;

        [Header("꼬리 흔들림")]
        [Tooltip("좌우로 흔드는 최대 각도")]
        [Range(0f, 25f)] public float swayAmplitude = 8f;
        [Tooltip("정지 상태의 흔들림 주기(Hz). 빠를수록 더 자주 흔든다.")]
        [Range(0.1f, 8f)] public float swayFrequency = 2f;
        [Tooltip("최고 속도일 때 주기 배수")]
        [Range(1f, 5f)] public float swaySpeedFactor = 2.4f;
        [Tooltip("멈춰 있을 때도 살짝 흔들리는 비율")]
        [Range(0f, 1f)] public float idleSwayRatio = 0.35f;

        [Header("표시")]
        [SerializeField] SpriteRenderer spriteRenderer;
        [Tooltip("스프라이트가 왼쪽을 보고 그려졌으면 체크")]
        public bool spriteFacesLeft = false;

        Rigidbody2D _rb;
        Vector2 _desiredVelocity;
        float _headingDeg;
        float _swayPhase;
        float _referenceSpeed = 6f;

        /// <summary>현재 몸이 향하는 방향 (정규화).</summary>
        public Vector2 Facing { get; private set; } = Vector2.right;

        /// <summary>현재 속도.</summary>
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        /// <summary>이번 프레임 이동을 완전히 정지시킬지 (마비 · 일시정지용).</summary>
        public bool Frozen { get; set; }

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;   // 물리 회전은 막고, 보이는 회전만 우리가 준다
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _headingDeg = transform.eulerAngles.z;
            _swayPhase = Random.value * Mathf.PI * 2f;
        }

        public void SetSpriteRenderer(SpriteRenderer sr) => spriteRenderer = sr;

        /// <summary>속도 정규화 기준값. 이 속도일 때 꼬리 흔들림이 최대가 된다.</summary>
        public void SetReferenceSpeed(float speed) => _referenceSpeed = Mathf.Max(0.1f, speed);

        /// <summary>매 FixedUpdate에 원하는 속도를 넣어준다.</summary>
        public void SetDesiredVelocity(Vector2 velocity) => _desiredVelocity = velocity;

        /// <summary>대쉬처럼 물리를 무시하고 속도를 강제할 때.</summary>
        public void OverrideVelocity(Vector2 velocity, bool alignHeading = true)
        {
            _rb.linearVelocity = velocity;
            if (alignHeading && velocity.sqrMagnitude > 0.01f)
                _headingDeg = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        }

        void FixedUpdate()
        {
            if (_rb == null) return;

            if (Frozen)
            {
                _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, Vector2.zero, drag * 3f * Time.fixedDeltaTime);
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector2 current = _rb.linearVelocity;
            bool hasInput = _desiredVelocity.sqrMagnitude > 0.0001f;

            if (hasInput)
            {
                // 방향을 크게 꺾을수록 목표 속력을 줄인다 — 급선회하면 자연히 느려진다
                float alignment = current.sqrMagnitude > 0.01f
                    ? Vector2.Dot(current.normalized, _desiredVelocity.normalized)
                    : 1f;
                float penalty = Mathf.Lerp(1f - turnSpeedLoss, 1f, Mathf.InverseLerp(-1f, 1f, alignment));
                Vector2 target = _desiredVelocity * penalty;

                _rb.linearVelocity = Vector2.MoveTowards(current, target, acceleration * dt);
            }
            else
            {
                // 물의 저항 — 지수 감쇠라 처음엔 빠르게, 나중엔 천천히 멈춘다
                _rb.linearVelocity = current * Mathf.Exp(-drag * dt);
                if (_rb.linearVelocity.sqrMagnitude < 0.0004f) _rb.linearVelocity = Vector2.zero;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 v = _rb != null ? _rb.linearVelocity : Vector2.zero;
            float speed = v.magnitude;

            // ── 진행 방향으로 서서히 회전 ──
            if (speed > 0.05f)
            {
                float targetDeg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                _headingDeg = Mathf.MoveTowardsAngle(_headingDeg, targetDeg, turnRate * dt);
            }

            float rad = _headingDeg * Mathf.Deg2Rad;
            Facing = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            // ── 꼬리 흔들림 ──
            float speed01 = Mathf.Clamp01(speed / _referenceSpeed);
            float freq = swayFrequency * Mathf.Lerp(1f, swaySpeedFactor, speed01);
            _swayPhase += freq * Mathf.PI * 2f * dt;

            float swayScale = Mathf.Lerp(idleSwayRatio, 1f, speed01);
            float sway = Mathf.Sin(_swayPhase) * swayAmplitude * swayScale;

            // ── 위아래가 뒤집히지 않게: 왼쪽을 볼 땐 회전 대신 Y 플립 ──
            bool facingLeft = Facing.x < 0f;
            float visualDeg = _headingDeg + (facingLeft ? sway : -sway);

            if (spriteRenderer != null)
            {
                // 왼쪽으로 그려진 스프라이트는 X로 한번 뒤집어 "오른쪽 기준"으로 맞춘다.
                spriteRenderer.flipX = spriteFacesLeft;
                // 왼쪽을 향할 땐 몸이 뒤집히므로 Y를 뒤집어 배가 아래로 오게 한다.
                spriteRenderer.flipY = facingLeft;
            }

            transform.rotation = Quaternion.Euler(0f, 0f, visualDeg);
        }

        /// <summary>풀에서 재사용할 때 상태를 되돌린다.</summary>
        public void ResetMotor(Vector2 heading)
        {
            Frozen = false;
            _desiredVelocity = Vector2.zero;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;

            if (heading.sqrMagnitude > 0.01f)
                _headingDeg = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
            _swayPhase = Random.value * Mathf.PI * 2f;

            transform.rotation = Quaternion.Euler(0f, 0f, _headingDeg);
        }
    }
}
