using System;
using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using FishGame.Gameplay;
using UnityEngine;

namespace FishGame.Player
{
    public enum ControlScheme { Keyboard, MouseFollow }

    /// <summary>
    /// 플레이어 물고기.
    /// 이동은 FishMotor에 맡기고, 여기서는 "무엇을 할지"만 결정한다.
    ///
    /// 액티브 스킬 (기획서 2판)
    ///   부스터    우클릭 — 앞으로 대쉬, 경로의 적을 크기 무시하고 먹음
    ///   청소기    좌클릭 홀드 — 범위 내 소형 물고기를 끌어당김
    ///   비늘 경화 패시브 — 먹힐 뻔한 판정을 N회 막고 짧게 무적
    ///   황금 미끼 자동 — 미끼를 뿌려 주변 물고기를 끌어모음
    ///   10만 볼트 자동 — 주변 전기 충격, 일반 2초 / 보스 0.1초 마비
    ///   미사일    자동 — 바라보는 방향으로 발사, 맞은 적 즉시 포식 처리
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(FishBody))]
    [RequireComponent(typeof(FishMotor))]
    public class PlayerFish : MonoBehaviour
    {
        [Header("조작")]
        [SerializeField] ControlScheme controlScheme = ControlScheme.Keyboard;
        [Tooltip("마우스 조작 모드에서 이 거리 안쪽이면 감속해서 멈춘다")]
        [SerializeField] float mouseDeadZone = 0.5f;

        [Header("포식 판정")]
        [Tooltip("입 위치 (앞쪽, 플레이어 크기 배수)")]
        [SerializeField] float mouthForwardRatio = 0.45f;
        [Tooltip("몸통 반경 (피포식 판정, 크기 배수)")]
        [SerializeField] float bodyRadiusRatio = 0.5f;
        [SerializeField] LayerMask fishLayer;

        [Header("연출")]
        [SerializeField] ParticleSystem eatEffect;
        [SerializeField] ParticleSystem boosterEffect;
        [SerializeField] Transform vacuumIndicator;
        [SerializeField] Transform voltIndicator;
        [SerializeField] SpriteRenderer armorIndicator;

        [Header("프리팹")]
        [SerializeField] Bait baitPrefab;
        [SerializeField] Missile missilePrefab;

        // ── 런타임 ──────────────────────────────────────────────
        Rigidbody2D _rb;
        FishBody _body;
        FishMotor _motor;
        FishInput _input;
        PlayerStats _stats;
        GameDatabase _db;
        RunManager _run;
        Camera _cam;

        readonly List<Collider2D> _overlap = new List<Collider2D>(32);
        ContactFilter2D _filter;

        Vector2 _moveInput;

        // 부스터
        float _boosterTimer, _boosterCooldown;
        Vector2 _boosterVelocity;
        readonly HashSet<FishBody> _boosterHits = new HashSet<FishBody>();

        // 청소기
        bool _vacuumActive;

        // 비늘 경화
        int _armorRemaining;
        float _invulnerableUntil;

        // 인게임 성장
        float _runStartSize = 1f;

        // 자동 스킬
        float _baitTimer, _voltTimer, _missileTimer;
        float _voltFlashUntil;

        readonly List<Bait> _baitPool = new List<Bait>();
        MissilePool _missilePool;

        // ── 공개 상태 ───────────────────────────────────────────
        public FishBody Body => _body;
        public FishMotor Motor => _motor;
        public bool IsAlive => _body != null && _body.IsAlive;
        public float Size => _body != null ? _body.Size : 1f;
        public bool IsBoosting => _boosterTimer > 0f;
        public bool IsVacuuming => _vacuumActive;
        public bool IsInvulnerable => Time.time < _invulnerableUntil;
        public int ArmorRemaining => _armorRemaining;
        public int ArmorMax => _stats != null ? _stats.ArmorStacks : 0;

        /// <summary>이번 판 시작 시의 크기 (스킬트리로 정해진 값).</summary>
        public float RunStartSize => _runStartSize;
        /// <summary>이번 판에서 커질 수 있는 최대 크기.</summary>
        public float MaxRunSize =>
            _db != null && _db.inRunGrowthEnabled ? _runStartSize * _db.growthMaxMultiplier : _runStartSize;
        /// <summary>성장 진행도 0~1. HUD 바에 쓴다.</summary>
        public float GrowthProgress01
        {
            get
            {
                float max = MaxRunSize;
                if (max <= _runStartSize + 0.0001f) return 1f;
                return Mathf.Clamp01((Size - _runStartSize) / (max - _runStartSize));
            }
        }

        public float BoosterCooldownNormalized =>
            _stats == null || !_stats.HasBooster || _db == null ? 1f
            : Mathf.Clamp01(1f - _boosterCooldown / Mathf.Max(0.01f, _db.boosterCooldown));

        public float BaitCooldownNormalized =>
            _stats == null || !_stats.HasGoldenBait || _db == null ? 1f
            : Mathf.Clamp01(1f - _baitTimer / Mathf.Max(0.01f, _db.baitInterval));

        public float VoltCooldownNormalized =>
            _stats == null || !_stats.HasVolt || _db == null ? 1f
            : Mathf.Clamp01(1f - _voltTimer / Mathf.Max(0.01f, _db.voltInterval));

        public float MissileCooldownNormalized =>
            _stats == null || !_stats.HasMissile || _db == null ? 1f
            : Mathf.Clamp01(1f - _missileTimer / Mathf.Max(0.01f, _db.missileInterval));

        /// <summary>입 판정 중심 (월드 좌표).</summary>
        public Vector2 MouthPosition =>
            _rb.position + _motor.Facing * (_body.Size * mouthForwardRatio);

        /// <summary>입 판정 반경. 치아 교정으로 커진다.</summary>
        public float MouthRadius =>
            _body.Size * (_db != null ? _db.baseMouthRatio : 0.45f) *
            (_stats != null ? _stats.MouthMultiplier : 1f);

        public float VacuumRadius =>
            (_db != null ? _db.vacuumRadius : 5f) *
            (_stats != null ? _stats.VacuumRangeMultiplier * _stats.MouthMultiplier : 1f);

        public float VoltRadius =>
            (_db != null ? _db.voltRadius : 6f) * (_stats != null ? _stats.VoltMultiplier : 1f);

        // ══════════════════════════════════════════════════════════
        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<FishBody>();
            _motor = GetComponent<FishMotor>();
            _cam = Camera.main;

            _filter = new ContactFilter2D { useLayerMask = true, useTriggers = true };
            _filter.SetLayerMask(fishLayer);

            _input = new FishInput();
        }

        void OnEnable() => _input.Enable();
        void OnDisable() => _input.Disable();
        void OnDestroy() => _input.Dispose();

        /// <summary>RunManager가 판 시작 시 호출.</summary>
        public void Initialize(PlayerStats stats, GameDatabase db, RunManager run)
        {
            _stats = stats;
            _db = db;
            _run = run;

            _body.ResetBody();
            _runStartSize = stats.Size;
            _body.Size = stats.Size;

            _motor.SetSpriteRenderer(_body.Renderer);
            _motor.SetReferenceSpeed(stats.MoveSpeed);
            _motor.acceleration = stats.MoveSpeed * 5f;
            _motor.drag = 3.2f;
            _motor.turnRate = 540f;
            _motor.ResetMotor(Vector2.right);

            _boosterTimer = _boosterCooldown = 0f;
            _boosterHits.Clear();
            _vacuumActive = false;
            _armorRemaining = stats.ArmorStacks;
            _invulnerableUntil = 0f;

            _baitTimer = db.baitInterval;
            _voltTimer = db.voltInterval;
            _missileTimer = db.missileInterval;

            if (vacuumIndicator != null) vacuumIndicator.gameObject.SetActive(false);
            if (voltIndicator != null) voltIndicator.gameObject.SetActive(false);
            UpdateArmorIndicator();

            EnsurePools();
        }

        void EnsurePools()
        {
            if (missilePrefab != null && _missilePool == null)
                _missilePool = new MissilePool(missilePrefab, null, 6);

            if (baitPrefab != null && _baitPool.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    var b = Instantiate(baitPrefab);
                    b.gameObject.SetActive(false);
                    _baitPool.Add(b);
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        void Update()
        {
            if (!Ready()) return;

            ReadMoveInput();
            TickTimers(Time.deltaTime);
            HandleBoosterInput();
            HandleVacuumInput();
            UpdateIndicators();
        }

        void FixedUpdate()
        {
            if (!Ready())
            {
                if (_motor != null) _motor.Frozen = true;
                return;
            }

            _motor.Frozen = false;

            if (_boosterTimer > 0f)
            {
                // 구조물에 정면으로 박으면 부스터가 끊긴다.
                // 그냥 두면 속도가 빨라 콜라이더를 뚫고 나가는 일이 생긴다.
                if (BoosterWouldHitObstacle()) CancelBoosterOnImpact();
                else
                {
                    _motor.OverrideVelocity(_boosterVelocity);
                    ResolveBoosterHits();
                }
            }
            else
            {
                _motor.SetDesiredVelocity(_moveInput * _stats.MoveSpeed);
            }

            if (_vacuumActive) ApplyVacuum(Time.fixedDeltaTime);

            ResolveFishContacts();
            ClampToBounds();
        }

        bool Ready() =>
            IsAlive && _stats != null && _db != null && _run != null && !_run.IsPaused && _run.IsRunning;

        // ── 입력 ────────────────────────────────────────────────
        void ReadMoveInput()
        {
            if (controlScheme == ControlScheme.Keyboard)
            {
                _moveInput = Vector2.ClampMagnitude(_input.Move.ReadValue<Vector2>(), 1f);
                return;
            }

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) { _moveInput = Vector2.zero; return; }

            Vector2 screen = _input.Point.ReadValue<Vector2>();
            Vector3 world = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));
            Vector2 toTarget = (Vector2)world - _rb.position;

            _moveInput = toTarget.magnitude <= mouseDeadZone
                ? Vector2.zero
                : Vector2.ClampMagnitude(toTarget / Mathf.Max(1f, mouseDeadZone * 4f), 1f);
        }

        void TickTimers(float dt)
        {
            if (_boosterTimer > 0f) _boosterTimer -= dt;
            if (_boosterCooldown > 0f) _boosterCooldown -= dt;

            if (_stats.HasGoldenBait)
            {
                _baitTimer -= dt;
                if (_baitTimer <= 0f) { FireGoldenBait(); _baitTimer = _db.baitInterval; }
            }

            if (_stats.HasVolt)
            {
                _voltTimer -= dt;
                if (_voltTimer <= 0f) { FireVolt(); _voltTimer = _db.voltInterval; }
            }

            if (_stats.HasMissile)
            {
                _missileTimer -= dt;
                if (_missileTimer <= 0f) { FireMissiles(); _missileTimer = _db.missileInterval; }
            }
        }

        // ── 부스터 ──────────────────────────────────────────────
        void HandleBoosterInput()
        {
            if (!_stats.HasBooster || _boosterCooldown > 0f) return;
            if (!_input.Booster.WasPressedThisFrame()) return;

            Vector2 dir = _moveInput.sqrMagnitude > 0.01f ? _moveInput.normalized : _motor.Facing;
            float distance = _db.boosterDistance * _body.Size * _stats.BoosterDistanceMultiplier;

            _boosterVelocity = dir * (distance / Mathf.Max(0.02f, _db.boosterDuration));
            _boosterTimer = _db.boosterDuration;
            _boosterCooldown = _db.boosterCooldown;
            _boosterHits.Clear();

            if (boosterEffect != null) boosterEffect.Play();
            Juice.Shake(0.18f);
            PlaySfx(AudioManager.Instance.Bank?.booster);
        }

        /// <summary>이번 물리 프레임에 장애물 안으로 들어가는가. 한 프레임 앞을 내다본다.</summary>
        bool BoosterWouldHitObstacle()
        {
            Vector2 next = _rb.position + _boosterVelocity * Time.fixedDeltaTime;
            return Obstacle.Overlaps(next, _body.Size * bodyRadiusRatio);
        }

        /// <summary>
        /// 부스터가 구조물에 막혔을 때. 대쉬를 끊고 살짝 튕겨낸다.
        /// 피해는 주지 않는다 — 벽에 부딪혔다고 죽으면 조작이 무서워진다.
        /// </summary>
        void CancelBoosterOnImpact()
        {
            _boosterTimer = 0f;
            _boosterHits.Clear();

            Vector2 back = -_boosterVelocity.normalized;
            _motor.OverrideVelocity(back * (_stats.MoveSpeed * 0.55f));

            Juice.Hit(0.04f, 0.6f);
            _body.Pop(0.12f);
        }

        /// <summary>부스터 경로에 걸린 물고기를 크기 판정 없이(또는 완화해서) 먹는다.</summary>
        void ResolveBoosterHits()
        {
            float radius = _body.Size * _db.boosterHitRadiusRatio;
            int count = Physics2D.OverlapCircle(_rb.position, radius, _filter, _overlap);

            for (int i = 0; i < count; i++)
            {
                var other = _overlap[i].GetComponentInParent<FishBody>();
                if (other == null || other == _body || !other.IsAlive) continue;
                if (!_boosterHits.Add(other)) continue;

                // 위력 강화 전에는 "조금 더 큰 것까지"만 먹을 수 있다
                if (!_stats.BoosterPierceAnySize)
                {
                    float limit = _body.Size * _db.boosterEatSizeMultiplier;
                    if (other.Size > limit) continue;
                }
                if (other.IsBoss && !_stats.BoosterPierceAnySize) continue;

                ConsumeByEffect(other);
            }
        }

        // ── 청소기 ──────────────────────────────────────────────
        void HandleVacuumInput()
        {
            _vacuumActive = _stats.HasVacuum && _input.Vacuum.IsPressed();
        }

        void ApplyVacuum(float dt)
        {
            float radius = VacuumRadius;
            int count = Physics2D.OverlapCircle(_rb.position, radius, _filter, _overlap);

            for (int i = 0; i < count; i++)
            {
                var other = _overlap[i].GetComponentInParent<FishBody>();
                if (other == null || other == _body || !other.IsAlive || other.IsBoss) continue;

                var ai = other.GetComponent<AIFish>();
                float ratio = ai != null && ai.Species != null
                    ? Mathf.Max(ai.Species.vacuumableSizeRatio, _db.vacuumSizeRatio)
                    : _db.vacuumSizeRatio;
                if (other.Size > _body.Size * ratio) continue;

                var orb = other.GetComponent<Rigidbody2D>();
                if (orb == null) continue;

                Vector2 toPlayer = _rb.position - orb.position;
                float dist = toPlayer.magnitude;
                if (dist < 0.01f) continue;

                // 가까울수록 세게 — 입 앞에서 확 빨려들어간다
                float strength = Mathf.Lerp(_db.vacuumPullForce, _db.vacuumPullForce * 0.35f,
                                            Mathf.Clamp01(dist / radius));
                orb.linearVelocity = Vector2.MoveTowards(
                    orb.linearVelocity, toPlayer / dist * strength, strength * 5f * dt);
            }
        }

        // ── 황금 미끼 ───────────────────────────────────────────
        void FireGoldenBait()
        {
            if (baitPrefab == null || _baitPool.Count == 0) return;

            int count = Mathf.Max(1, _stats.BaitCount);
            float radius = _db.baitRadius * _stats.BaitRangeMultiplier;

            for (int i = 0; i < count; i++)
            {
                var bait = GetPooledBait();
                if (bait == null) break;

                // 플레이어 주변에 흩뿌린다
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized *
                                 UnityEngine.Random.Range(radius * 0.25f, radius * 0.7f);
                Vector2 pos = _run.ClampToWorld(_rb.position + offset, 1f);

                bait.Launch(pos, radius, _db.baitLifetime);
            }

            PlaySfx(AudioManager.Instance.Bank?.baitDrop);
        }

        Bait GetPooledBait()
        {
            foreach (var b in _baitPool)
                if (b != null && !b.gameObject.activeSelf) return b;

            var extra = Instantiate(baitPrefab);
            extra.gameObject.SetActive(false);
            _baitPool.Add(extra);
            return extra;
        }

        // ── 10만 볼트 ───────────────────────────────────────────
        void FireVolt()
        {
            float radius = VoltRadius;
            float stunNormal = _db.voltStunNormal * _stats.VoltMultiplier;
            float stunBoss = _db.voltStunBoss * _stats.VoltMultiplier;

            int count = Physics2D.OverlapCircle(_rb.position, radius, _filter, _overlap);
            for (int i = 0; i < count; i++)
            {
                var other = _overlap[i].GetComponentInParent<FishBody>();
                if (other == null || other == _body || !other.IsAlive) continue;
                other.Stun(other.IsBoss ? stunBoss : stunNormal);
            }

            Juice.Shake(0.32f);
            PlaySfx(AudioManager.Instance.Bank?.volt);
            _voltFlashUntil = Time.time + 0.25f;
            if (voltIndicator != null)
            {
                voltIndicator.gameObject.SetActive(true);
                float local = radius * 2f / Mathf.Max(0.01f, transform.localScale.x);
                voltIndicator.localScale = new Vector3(local, local, 1f);
            }
        }

        // ── 미사일 ──────────────────────────────────────────────
        void FireMissiles()
        {
            if (missilePrefab == null) return;
            EnsurePools();

            int count = Mathf.Max(1, _stats.MissileCount);
            Vector2 forward = _motor.Facing;
            float baseAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;

            float spread = _db.missileSpreadAngle;
            float step = count > 1 ? spread / (count - 1) : 0f;
            float start = count > 1 ? baseAngle - spread * 0.5f : baseAngle;

            float hitRadius = _db.missileHitRadius * _stats.MissileRangeMultiplier;
            float maxTarget = _body.Size * _db.missileMaxTargetSizeMultiplier;

            for (int i = 0; i < count; i++)
            {
                float angle = (start + step * i) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                var missile = _missilePool.Get();
                missile.Launch(MouthPosition, dir, _db.missileSpeed, _db.missileLifetime,
                               hitRadius, maxTarget, fishLayer, _missilePool);
            }

            PlaySfx(AudioManager.Instance.Bank?.missile);
        }

        // ── 포식 / 피포식 ───────────────────────────────────────
        void ResolveFishContacts()
        {
            float bodyR = _body.Size * bodyRadiusRatio;
            float mouthR = MouthRadius;
            float scanR = bodyR + mouthR + _body.Size * mouthForwardRatio;

            int count = Physics2D.OverlapCircle(_rb.position, scanR, _filter, _overlap);
            if (count == 0) return;

            Vector2 mouth = MouthPosition;

            for (int i = 0; i < count; i++)
            {
                var other = _overlap[i].GetComponentInParent<FishBody>();
                if (other == null || other == _body || !other.IsAlive) continue;

                float otherR = other.BodyRadius;
                float dist = Vector2.Distance(_rb.position, other.transform.position);

                if (FishBody.CanEat(_body, other, _db.eatSizeTolerance))
                {
                    if (Vector2.Distance(mouth, other.transform.position) <= mouthR + otherR)
                    {
                        ConsumeByEffect(other);
                        continue;
                    }
                }
                else if (FishBody.CanEat(other, _body, _db.eatSizeTolerance))
                {
                    // 마비된 물고기는 물지 못한다
                    if (other.IsStunned) continue;
                    if (dist <= bodyR + otherR * 0.8f) { TakeHit(other); return; }
                }
            }
        }

        /// <summary>
        /// 미사일 · 부스터 · 입 판정 어디서든 물고기를 "먹은 것"으로 처리하는 단일 진입점.
        /// </summary>
        public void ConsumeByEffect(FishBody prey)
        {
            if (prey == null || !prey.IsAlive || _run == null) return;

            var ai = prey.GetComponent<AIFish>();
            var species = ai != null ? ai.Species : null;

            bool wasBoss = prey.IsBoss;
            float preySize = prey.Size;
            float sizeRatio = Mathf.Clamp01(preySize / Mathf.Max(0.01f, _body.Size));

            prey.Consume(_body);
            _run.ReportFishEaten(species, wasBoss);
            Grow(preySize);

            // ── 타격감 ──
            // 큰 걸 먹을수록 오래 멈추고 크게 흔들린다. 잔챙이는 거의 티가 안 난다.
            if (wasBoss)
            {
                Juice.Hit(0.16f, 0.85f);
                _body.Pop(0.30f);
                PlaySfx(AudioManager.Instance.Bank?.eatBoss);
            }
            else
            {
                float stop = Mathf.Lerp(0.012f, 0.075f, sizeRatio);
                float shake = Mathf.Lerp(0.05f, 0.42f, sizeRatio);
                Juice.Hit(stop, shake);
                _body.Pop(Mathf.Lerp(0.06f, 0.20f, sizeRatio));

                var bank = AudioManager.Instance.Bank;
                PlaySfx(sizeRatio > 0.55f ? bank?.eatBig : bank?.eatSmall,
                        pitch: Mathf.Lerp(1.15f, 0.85f, sizeRatio));
            }

            if (eatEffect != null) eatEffect.Play();
        }

        /// <summary>
        /// 먹은 만큼 커진다. 면적(질량)을 더하는 방식이라
        ///   새 크기 = √(내 크기² + 먹이 크기² × 효율)
        /// 커질수록 잔챙이로는 거의 안 크고, 큰 걸 먹으면 확 큰다.
        /// </summary>
        void Grow(float preySize)
        {
            if (_db == null || !_db.inRunGrowthEnabled || preySize <= 0f) return;

            float max = MaxRunSize;
            if (_body.Size >= max - 0.0001f) return;

            float mass = _body.Size * _body.Size + preySize * preySize * _db.growthMassEfficiency;
            _body.Size = Mathf.Min(max, Mathf.Sqrt(mass));
        }

        /// <summary>더 큰 물고기에게 물렸다. 비늘 경화가 남아 있으면 막는다.</summary>
        void TakeHit(FishBody attacker)
        {
            if (IsInvulnerable) return;

            if (_armorRemaining > 0)
            {
                _armorRemaining--;
                _invulnerableUntil = Time.time + _db.armorInvulnerability;
                UpdateArmorIndicator();

                Vector2 away = (_rb.position - (Vector2)attacker.transform.position).normalized;
                if (away.sqrMagnitude < 0.01f) away = Vector2.up;
                _motor.OverrideVelocity(away * _db.armorKnockback, alignHeading: false);

                Juice.Hit(0.09f, 0.55f);
                _body.Pop(0.22f);
                PlaySfx(AudioManager.Instance.Bank?.armorBlock);
                return;
            }

            _body.Consume(attacker);
            _motor.Frozen = true;

            Juice.Hit(0.14f, 0.9f);
            PlaySfx(AudioManager.Instance.Bank?.death);

            _run.EndRun(RunEndReason.Eaten);
        }

        // ── 표시 ────────────────────────────────────────────────
        void UpdateIndicators()
        {
            if (vacuumIndicator != null)
            {
                bool on = _vacuumActive;
                if (vacuumIndicator.gameObject.activeSelf != on)
                    vacuumIndicator.gameObject.SetActive(on);
                if (on)
                {
                    float local = VacuumRadius * 2f / Mathf.Max(0.01f, transform.localScale.x);
                    vacuumIndicator.localScale = new Vector3(local, local, 1f);
                }
            }

            if (voltIndicator != null && voltIndicator.gameObject.activeSelf && Time.time > _voltFlashUntil)
                voltIndicator.gameObject.SetActive(false);

            if (armorIndicator != null)
            {
                var c = armorIndicator.color;
                c.a = IsInvulnerable ? 0.6f : (_armorRemaining > 0 ? 0.28f : 0f);
                armorIndicator.color = c;
            }
        }

        void UpdateArmorIndicator()
        {
            if (armorIndicator == null) return;
            armorIndicator.gameObject.SetActive(_armorRemaining > 0);
        }

        /// <summary>
        /// 벽을 뚫고 나갔을 때 되돌리는 안전망. 실제 충돌은 WorldBuilder가 만든
        /// 콜라이더가 처리하고, 이건 부스터로 빠르게 통과했을 때의 보험이다.
        /// </summary>
        void ClampToBounds()
        {
            if (_run == null) return;
            float r = _body.Size * bodyRadiusRatio;
            Vector2 p = _run.ClampToWorld(_rb.position, r);
            if (p != _rb.position) _rb.position = p;
        }

        static void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip != null) AudioManager.Play(clip, volume, pitch);
        }

        void OnDrawGizmosSelected()
        {
            float size = _body != null ? _body.Size : 1f;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, size * bodyRadiusRatio);

            if (Application.isPlaying && _motor != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(MouthPosition, MouthRadius);
            }
        }
    }
}
