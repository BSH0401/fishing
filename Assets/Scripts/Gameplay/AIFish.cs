using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 필드 물고기. 기획서대로 "한 가지의 단순한 움직임 패턴"만 갖되,
    /// 그 위에 스티어링(부드러운 선회 · 이웃 회피 · 속도 변주)을 얹어
    /// 격자처럼 움직이지 않고 물속에 있는 것처럼 보이게 한다.
    ///
    /// 포식 판정 자체는 PlayerFish가 단독으로 처리한다(중복 처리 방지).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(FishBody))]
    [RequireComponent(typeof(FishMotor))]
    public class AIFish : MonoBehaviour
    {
        [SerializeField] FishSpecies species;

        [Header("스티어링")]
        [Tooltip("이웃과 이 거리(자기 크기 배수) 안이면 서로 밀어낸다")]
        [SerializeField] float separationRadiusRatio = 1.6f;
        [SerializeField] float separationWeight = 1.1f;
        [Tooltip("벽에서 이 거리(자기 크기 배수) 안이면 미리 방향을 튼다")]
        [SerializeField] float wallAvoidRatio = 2.2f;
        [SerializeField] float wallAvoidWeight = 2.2f;
        [Tooltip("개체마다 속도를 ±이 비율만큼 다르게 준다")]
        [Range(0f, 0.5f)] [SerializeField] float speedVariance = 0.18f;

        public FishSpecies Species => species;
        public FishBody Body => _body;

        Rigidbody2D _rb;
        FishBody _body;
        FishMotor _motor;
        FishSpawner _spawner;
        // 물고기는 자기 존 안에서만 논다. 통합 맵이 되면서 "맵 전체"가 아니라
        // "내가 속한 구역"이 활동 범위가 됐다 — 안 그러면 어항 치어가 바다까지 헤엄쳐 간다.
        WorldLayout _layout;
        int _homeZone;
        float _homeTop, _homeBottom;

        /// <summary>이 물고기가 속한 구역. 스포너의 밀도 계산과 디버깅에 쓴다.</summary>
        public int HomeZone => _homeZone;

        Vector2 _heading = Vector2.right;
        Vector2 _wanderTarget;
        float _spawnTime;
        float _patternSeed;
        float _stateTimer;
        float _speedScale = 1f;
        bool _ambushTriggered;

        // 이웃 검색 버퍼 — 물고기가 많으므로 프레임마다 할당하지 않는다
        static readonly List<Collider2D> NeighborBuffer = new List<Collider2D>(12);
        static ContactFilter2D _neighborFilter;
        static bool _filterReady;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<FishBody>();
            _motor = GetComponent<FishMotor>();

            if (!_filterReady)
            {
                _neighborFilter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
                _neighborFilter.SetLayerMask(1 << gameObject.layer);
                _filterReady = true;
            }
        }

        /// <summary>스포너가 풀에서 꺼내며 호출.</summary>
        public void Setup(FishSpecies data, WorldLayout layout, int zoneIndex,
                          FishSpawner spawner, Vector2 initialHeading)
        {
            species = data;
            _layout = layout;
            _homeZone = zoneIndex;

            if (_layout != null)
            {
                var slice = _layout.GetSlice(zoneIndex);
                _homeTop = slice.YTop;
                // 통로까지는 내려갈 수 있게 조금 여유를 준다 (통로 안에서도 자연스럽게 보이도록)
                _homeBottom = slice.HasCorridor ? slice.CorridorBottom : slice.YBottom;
            }
            else
            {
                _homeTop = 12f;
                _homeBottom = -12f;
            }

            _spawner = spawner;
            _spawnTime = Time.time;
            _patternSeed = Random.value * 100f;
            _stateTimer = 0f;
            _ambushTriggered = false;
            _speedScale = 1f + Random.Range(-speedVariance, speedVariance);
            _heading = initialHeading.sqrMagnitude > 0.01f ? initialHeading.normalized : Vector2.right;

            _body.ResetBody();
            _body.Size = data.size;
            _body.VisualScaleMultiplier = data.visualScaleMultiplier;
            _body.SetBoss(data.isBoss);

            if (_body.Renderer != null)
            {
                _body.Renderer.sprite = data.sprite;
                _body.Renderer.color = data.tint;
            }

            // 종별 유영 개성
            _motor.spriteFacesLeft = data.spriteFacesLeft;
            _motor.turnRate = data.turnRate;
            _motor.swayAmplitude = data.swayAmplitude;
            _motor.swayFrequency = data.swayFrequency;
            _motor.acceleration = data.moveSpeed * 4.5f;
            _motor.drag = 2.5f;
            _motor.SetReferenceSpeed(data.moveSpeed);
            _motor.SetSpriteRenderer(_body.Renderer);
            _motor.ResetMotor(_heading);

            _wanderTarget = RandomPointInBounds();
            _body.OnConsumed += HandleConsumed;
        }

        void HandleConsumed(FishBody eater)
        {
            _body.OnConsumed -= HandleConsumed;
            if (_spawner != null) _spawner.Despawn(this);
            else gameObject.SetActive(false);
        }

        void FixedUpdate()
        {
            if (species == null || !_body.IsAlive) return;

            var run = RunManager.Instance;
            bool paused = run != null && run.IsPaused;
            _motor.Frozen = paused || _body.IsStunned;
            if (_motor.Frozen) return;

            _stateTimer += Time.fixedDeltaTime;

            Vector2 desired = ComputeDesiredVelocity();
            // 패턴이 낸 속도(매복 돌진·도망 1.3배·미끼 유인)는 상한에서 지워지면 안 된다
            float patternSpeed = desired.magnitude;
            desired += SeparationForce() * (species.moveSpeed * separationWeight);
            desired += WallAvoidForce() * (species.moveSpeed * wallAvoidWeight);

            // 장애물은 벽보다 조금 더 세게 피한다. 벽은 스치며 따라가도 자연스럽지만,
            // 구조물에 옆구리를 붙이고 비비는 건 딱 봐도 이상하다.
            desired += Obstacle.AvoidForce(_rb.position, species.size * 0.5f, species.size * 1.2f)
                       * (species.moveSpeed * wallAvoidWeight * 1.4f);

            float maxSpeed = Mathf.Max(species.moveSpeed * _speedScale, patternSpeed);
            if (desired.sqrMagnitude > maxSpeed * maxSpeed)
                desired = desired.normalized * maxSpeed;

            _motor.SetDesiredVelocity(desired);

            if (desired.sqrMagnitude > 0.01f)
                _heading = Vector2.Lerp(_heading, desired.normalized, 0.15f).normalized;

            KeepInsideBounds();
        }

        // ══════════════════════════════════════════════════════════
        //  패턴
        // ══════════════════════════════════════════════════════════
        static float EatTolerance =>
            RunManager.Instance != null && RunManager.Instance.Database != null
                ? RunManager.Instance.Database.eatSizeTolerance : 1f;

        Vector2 ComputeDesiredVelocity()
        {
            float speed = species.moveSpeed * _speedScale;
            Vector2 pos = _rb.position;

            // 황금 미끼가 근처에 있으면 패턴을 무시하고 달려든다
            var bait = Bait.Nearest(pos);
            if (bait != null && !species.isBoss)
            {
                var db = RunManager.Instance?.Database;
                float lureMult = db != null ? db.baitLureSpeedMultiplier : 1.3f;
                return ((Vector2)bait.transform.position - pos).normalized * speed * lureMult;
            }

            Transform playerT = GetPlayerTransform(out FishBody playerBody);

            switch (species.pattern)
            {
                case AIPatternType.Straight:
                    return _heading * speed;

                case AIPatternType.SineWave:
                {
                    float t = (Time.time - _spawnTime + _patternSeed) * species.patternParam2 * Mathf.PI * 2f;
                    Vector2 perp = new Vector2(-_heading.y, _heading.x);
                    return _heading * speed + perp * (Mathf.Cos(t) * species.patternParam);
                }

                case AIPatternType.Wander:
                    return WanderVelocity(pos, speed);

                case AIPatternType.Chase:
                {
                    if (playerBody != null && playerBody.IsAlive &&
                        Vector2.Distance(pos, playerT.position) < species.patternParam &&
                        species.size > playerBody.Size &&
                        !FishBody.CanEat(playerBody, _body, EatTolerance))   // 플레이어가 먹을 수 있는 상대는 쫓지 않는다
                    {
                        // 목표의 조금 앞을 노린다 — 뒤꽁무니만 쫓지 않아 자연스럽다
                        Vector2 lead = (Vector2)playerT.position + PredictLead(playerT) ;
                        return (lead - pos).normalized * speed;
                    }
                    return WanderVelocity(pos, speed);
                }

                case AIPatternType.Flee:
                {
                    if (playerBody != null && playerBody.IsAlive &&
                        Vector2.Distance(pos, playerT.position) < species.patternParam &&
                        FishBody.CanEat(playerBody, _body, EatTolerance))   // 실제 포식 판정과 같은 기준
                    {
                        Vector2 away = (pos - (Vector2)playerT.position).normalized;
                        // 벽으로 몰리지 않게 중심 쪽 성분을 살짝 섞는다
                        Vector2 toCenter = (HomeCenter - pos).normalized;
                        return (away * 1.0f + toCenter * 0.35f).normalized * speed * 1.3f;
                    }
                    goto case AIPatternType.SineWave;
                }

                case AIPatternType.Ambush:
                {
                    if (!_ambushTriggered && playerBody != null && playerBody.IsAlive &&
                        Vector2.Distance(pos, playerT.position) < species.patternParam)
                    {
                        _ambushTriggered = true;
                        _stateTimer = 0f;
                        _heading = ((Vector2)playerT.position - pos).normalized;
                    }

                    if (_ambushTriggered)
                    {
                        if (_stateTimer > 1.2f) { _ambushTriggered = false; _stateTimer = 0f; }
                        return _heading * speed * Mathf.Max(1f, species.patternParam2);
                    }
                    // 대기 중에도 완전히 멈추지 않고 아주 느리게 부유한다
                    return _heading * speed * 0.08f;
                }

                default:
                    return _heading * speed;
            }
        }

        Vector2 WanderVelocity(Vector2 pos, float speed)
        {
            if (_stateTimer > 3f || Vector2.Distance(pos, _wanderTarget) < species.size * 1.2f)
            {
                _wanderTarget = NextWanderTarget(pos);
                _stateTimer = 0f;
            }

            // 목표에 가까워지면 속도를 줄여 부드럽게 도착한다
            float dist = Vector2.Distance(pos, _wanderTarget);
            float arrive = Mathf.Clamp01(dist / (species.patternParam + species.size * 2f));
            return (_wanderTarget - pos).normalized * speed * Mathf.Lerp(0.35f, 1f, arrive);
        }

        /// <summary>지금 방향에서 크게 벗어나지 않는 곳을 다음 목표로 고른다.</summary>
        Vector2 NextWanderTarget(Vector2 pos)
        {
            float radius = Mathf.Max(2f, species.patternParam);
            for (int i = 0; i < 6; i++)
            {
                float angle = Random.Range(-70f, 70f) * Mathf.Deg2Rad;
                Vector2 dir = Rotate(_heading, angle);
                Vector2 candidate = pos + dir * Random.Range(radius * 0.6f, radius * 1.8f);
                // 장애물 안을 목표로 잡으면 그쪽으로 계속 밀고 들어가다 끼인다
                if (InsideHome(candidate) && !Obstacle.Overlaps(candidate, species.size * 0.6f))
                    return candidate;
            }
            return RandomPointInBounds();
        }

        Vector2 PredictLead(Transform target)
        {
            var rb = target.GetComponent<Rigidbody2D>();
            if (rb == null) return Vector2.zero;
            return rb.linearVelocity * 0.35f;
        }

        // ══════════════════════════════════════════════════════════
        //  스티어링 보조력
        // ══════════════════════════════════════════════════════════
        Vector2 SeparationForce()
        {
            float radius = species.size * separationRadiusRatio;
            int count = Physics2D.OverlapCircle(_rb.position, radius, _neighborFilter, NeighborBuffer);
            if (count <= 1) return Vector2.zero;

            Vector2 push = Vector2.zero;
            int used = 0;
            for (int i = 0; i < count && i < 12; i++)
            {
                var other = NeighborBuffer[i];
                if (other == null || other.transform == transform) continue;

                Vector2 delta = _rb.position - (Vector2)other.transform.position;
                float sqr = delta.sqrMagnitude;
                if (sqr < 0.0001f) { delta = Random.insideUnitCircle.normalized * 0.1f; sqr = 0.01f; }
                if (sqr > radius * radius) continue;

                push += delta / sqr;   // 가까울수록 강하게
                used++;
            }
            return used == 0 ? Vector2.zero : Vector2.ClampMagnitude(push, 1f);
        }

        // ── 활동 범위 ───────────────────────────────────────────
        Vector2 HomeCenter => new Vector2(CenterXAt((_homeTop + _homeBottom) * 0.5f),
                                          (_homeTop + _homeBottom) * 0.5f);

        float HalfWidthAt(float y) => _layout != null ? _layout.HalfWidthAt(y) : 20f;
        float CenterXAt(float y) => _layout != null ? _layout.CenterXAt(y) : 0f;

        bool InsideHome(Vector2 p)
        {
            if (p.y > _homeTop || p.y < _homeBottom) return false;
            return Mathf.Abs(p.x - CenterXAt(p.y)) <= HalfWidthAt(p.y);
        }

        /// <summary>
        /// 벽을 피하는 힘. 통합 맵은 벽이 기울어져 있어서 y마다 폭이 다르므로
        /// 고정된 사각형이 아니라 그 높이에서의 실제 폭을 매번 본다.
        /// </summary>
        Vector2 WallAvoidForce()
        {
            float margin = species.size * wallAvoidRatio;
            Vector2 p = _rb.position;
            Vector2 force = Vector2.zero;

            float cx = CenterXAt(p.y);
            float hw = HalfWidthAt(p.y);

            float left   = p.x - (cx - hw);
            float right  = (cx + hw) - p.x;
            float bottom = p.y - _homeBottom;
            float top    = _homeTop - p.y;

            if (left   < margin) force.x += 1f - left / margin;
            if (right  < margin) force.x -= 1f - right / margin;
            if (bottom < margin) force.y += 1f - bottom / margin;
            if (top    < margin) force.y -= 1f - top / margin;

            return Vector2.ClampMagnitude(force, 1f);
        }

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static Transform GetPlayerTransform(out FishBody body)
        {
            var run = RunManager.Instance;
            if (run != null && run.Player != null)
            {
                body = run.Player.Body;
                return run.Player.transform;
            }
            body = null;
            return null;
        }

        Vector2 RandomPointInBounds()
        {
            float m = species != null ? species.size * 0.5f : 0.5f;
            float y = Random.Range(_homeBottom + m, _homeTop - m);
            float cx = CenterXAt(y);
            float hw = Mathf.Max(m + 0.1f, HalfWidthAt(y));
            return new Vector2(Random.Range(cx - hw + m, cx + hw - m), y);
        }

        /// <summary>경계를 넘어가면 안쪽으로 되돌린다. (밀어내기는 WallAvoidForce가 먼저 처리)</summary>
        void KeepInsideBounds()
        {
            Vector2 p = _rb.position;
            float m = species.size * 0.5f;

            p.y = Mathf.Clamp(p.y, _homeBottom + m, _homeTop - m);

            float cx = CenterXAt(p.y);
            float limit = Mathf.Max(0.1f, HalfWidthAt(p.y) - m);
            p.x = Mathf.Clamp(p.x, cx - limit, cx + limit);

            if (p != _rb.position) _rb.position = p;
        }
    }
}
