using System.Collections;
using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 통합 맵의 물고기를 채워 넣는다.
    /// 프리팹은 1종(AIFish)만 쓰고 스프라이트/크기/패턴은 FishSpecies에서 주입한다.
    ///
    /// 맵이 하나로 합쳐지면서 스폰 규칙이 바뀌었다.
    /// 예전: "이 맵의 스폰 테이블에서 뽑는다"
    /// 지금: "먼저 자리를 정하고, 그 자리가 속한 존의 테이블에서 뽑는다"
    ///
    /// 플레이어 기준 스폰 (R13):
    ///   · 스폰 — 플레이어 화면 바로 바깥의 링(화면 반경 × spawnRingInner ~ spawnRingOuter)에서만 태어난다.
    ///   · 디스폰 — 플레이어에게서 화면 반경 × despawnRadiusRatio보다 멀어지면 회수한다.
    ///   · 한도 — 구역 전체가 아니라 '플레이어 주변(디스폰 범위 안)'의 물고기 수를 targetPopulation과 비교한다.
    ///     주변이 절반 아래로 비면 spawnBurstMax마리까지 몰아서 채운다 (구역 이동 · 대쉬 직후).
    ///   범위 값은 GameDatabase(밸런스 시트 플레이어 탭)에서 조절한다 — 예전엔 씬에 저장된 값(2.8)이 코드 기본값을 덮었다.
    ///   화면 반경 기준인 이유: 구역마다 물고기 크기와 시야가 수십 배 달라서 고정 거리면 바다에선 화면 안에서 사라진다.
    /// </summary>
    public class FishSpawner : MonoBehaviour
    {
        [Header("프리팹")]
        [Tooltip("AIFish + FishBody + Rigidbody2D + CircleCollider2D(IsTrigger) 가 붙은 프리팹")]
        [SerializeField] AIFish fishPrefab;
        [SerializeField] Transform poolParent;
        [Tooltip("시작할 때 미리 만들어 둘 개수")]
        [SerializeField] int prewarmCount = 48;

        readonly Stack<AIFish> _pool = new Stack<AIFish>();
        readonly List<AIFish> _active = new List<AIFish>();
        readonly Dictionary<FishSpecies, int> _aliveCount = new Dictionary<FishSpecies, int>();

        [Header("디스폰 확인 주기")]
        [Tooltip("멀어진 물고기를 정리하는 주기(초). 범위 값은 GameDatabase의 '스폰 — 플레이어 기준'")]
        [SerializeField] float cullInterval = 0.5f;

        GameDatabase Db => _run != null ? _run.Database : null;
        float RingInner => Db != null ? Db.spawnRingInner : 1.1f;
        float RingOuter => Db != null ? Db.spawnRingOuter : 1.85f;
        float DespawnRatio => Db != null ? Mathf.Max(RingOuter + 0.1f, Db.despawnRadiusRatio) : 2.2f;
        int BurstMax => Db != null ? Mathf.Max(1, Db.spawnBurstMax) : 4;
        Vector2 PlayerPos => _run != null && _run.Player != null ? (Vector2)_run.Player.transform.position : Vector2.zero;

        RunManager _run;
        WorldLayout _layout;
        float _nextCullAt;
        Coroutine _spawnLoop;
        AIFish _boss;

        public int ActiveCount => _active.Count;
        public AIFish CurrentBoss => _boss;

        void Awake()
        {
            if (poolParent == null) poolParent = transform;
            for (int i = 0; i < prewarmCount; i++) _pool.Push(CreateInstance());
        }

        AIFish CreateInstance()
        {
            var inst = Instantiate(fishPrefab, poolParent);
            inst.gameObject.SetActive(false);
            return inst;
        }

        // ══════════════════════════════════════════════════════════
        //  시작 / 정지
        // ══════════════════════════════════════════════════════════
        public void BeginSpawning(RunManager run)
        {
            StopSpawning();
            DespawnAll();

            _run = run;
            _layout = run != null ? run.Layout : null;

            if (_layout == null || fishPrefab == null)
            {
                Debug.LogError("[FishSpawner] WorldLayout 또는 fishPrefab이 없습니다.");
                return;
            }

            // 시작 시 절반은 즉시 채워서 빈 화면을 피한다.
            // 판이 막 시작한 순간이라 화면 안에서 태어나도 어색하지 않다 — 링 바깥에서만 채우면 첫 몇 초가 텅 빈다
            int initial = Mathf.Max(1, TargetPopulation / 2);
            for (int i = 0; i < initial; i++) TrySpawnOne(StartFillInner);

            _spawnLoop = StartCoroutine(SpawnLoop());
        }

        public void StopSpawning()
        {
            if (_spawnLoop != null) { StopCoroutine(_spawnLoop); _spawnLoop = null; }
        }

        ZoneData CurrentZone => _run != null ? _run.CurrentZone : null;

        int TargetPopulation
        {
            get
            {
                var z = CurrentZone;
                return z != null ? z.targetPopulation : 26;
            }
        }

        IEnumerator SpawnLoop()
        {
            while (true)
            {
                var z = CurrentZone;
                float interval = z != null ? z.spawnInterval : 0.35f;

                if (_run != null && _run.IsRunning && !_run.IsPaused)
                {
                    int target = TargetPopulation;
                    int near = CountNearPlayer();
                    if (near < target)
                    {
                        // 주변이 절반 아래로 비었으면(구역 이동 · 대쉬 직후) 몰아서 채운다
                        int n = near < target / 2 ? Mathf.Min(BurstMax, target - near) : 1;
                        for (int i = 0; i < n; i++) TrySpawnOne();
                    }
                }

                yield return new WaitForSeconds(interval);
            }
        }

        /// <summary>
        /// 플레이어 주변(디스폰 범위 안)에 있는 지금 구역 물고기 수. 보스는 세지 않는다.
        /// 다른 구역 물고기는 먹으러 갈 수 없으니 한도에 세지 않는다 (QA 22)
        /// </summary>
        int CountNearPlayer()
        {
            if (_run == null) return 0;
            int zone = _run.CurrentZoneIndex;
            Vector2 p = PlayerPos;
            float r = _run.ViewRadius * DespawnRatio, r2 = r * r;
            int n = 0;
            foreach (var f in _active)
            {
                if (f == null || f == _boss || !IsActiveZone(f.HomeZone, zone)) continue;
                if (((Vector2)f.transform.position - p).sqrMagnitude <= r2) n++;
            }
            return n;
        }

        // ══════════════════════════════════════════════════════════
        //  스폰
        // ══════════════════════════════════════════════════════════
        /// <summary>R14: 지금 구역과 바로 이전 구역 물고기가 같이 산다.</summary>
        static bool IsActiveZone(int fishZone, int currentZone) => fishZone == currentZone || fishZone == currentZone - 1;

        /// <summary>판 시작 채우기의 안쪽 반경 (화면 반경 배수). 플레이어 바로 옆에서는 태어나지 않게.</summary>
        const float StartFillInner = 0.35f;

        bool TrySpawnOne(float innerRatio = -1f)
        {
            if (!FindSpawnPosition(innerRatio, out Vector2 pos)) return false;

            var species = PickSpecies(out int zoneIndex);
            if (species == null) return false;

            // 자리를 먼저 잡았으니 그 종이 들어갈 만큼 벽에서 떨어져 있는지 다시 확인
            pos = _layout.Clamp(pos, species.size * 0.5f);
            // 큰 종은 벽에서 밀려난 자리가 장애물과 겹칠 수 있다 — 그 크기로 다시 본다
            if (Obstacle.Overlaps(pos, species.size * 0.5f)) return false;

            Vector2 heading = Random.value < 0.5f ? Vector2.left : Vector2.right;
            heading = (heading + Random.insideUnitCircle * 0.35f).normalized;

            Spawn(species, pos, zoneIndex, heading);
            return true;
        }

        /// <summary>
        /// 지금 구역 + 이전 구역 스폰 테이블을 합쳐 뽑는다 (R14). 이전 구역 종은 previousZoneSpawnWeight를 곱한 가중치.
        /// zoneIndex = 뽑힌 종이 속한 구역 (도감 · 재화 보조 배율 · 한도 계산에 쓴다).
        /// </summary>
        FishSpecies PickSpecies(out int zoneIndex)
        {
            int cur = _run != null ? _run.CurrentZoneIndex : 0;
            zoneIndex = cur;
            var zCur = _layout.GetZone(cur);
            var zPrev = cur > 0 ? _layout.GetZone(cur - 1) : null;
            float prevW = cur > 0 && _run != null && _run.Database != null ? _run.Database.previousZoneSpawnWeight : 0f;
            if (zPrev == zCur) zPrev = null;

            // R12: 내 크기에 비해 너무 작은 종은 뺀다. 단, 뽑을 종이 하나도 없으면 제한을 풀어 화면이 비지 않게 한다.
            float minSize = 0f;
            if (_run != null && _run.Player != null && _run.Database != null)
                minSize = _run.Player.Size * _run.Database.minSpawnSizeRatio;

            float total = SumWeights(zCur, minSize, 1f) + SumWeights(zPrev, minSize, prevW);
            if (total <= 0f && minSize > 0f) { minSize = 0f; total = SumWeights(zCur, 0f, 1f) + SumWeights(zPrev, 0f, prevW); }
            if (total <= 0f) return null;

            float roll = Random.value * total;
            var hit = Roll(zCur, minSize, 1f, ref roll);
            if (hit != null) return hit;
            hit = Roll(zPrev, minSize, prevW, ref roll);
            if (hit != null) zoneIndex = cur - 1;
            return hit;
        }

        FishSpecies Roll(ZoneData zone, float minSize, float mult, ref float roll)
        {
            if (zone == null || zone.spawnTable == null || mult <= 0f) return null;
            foreach (var e in zone.spawnTable)
            {
                if (!Eligible(e, minSize)) continue;
                roll -= e.weight * mult;
                if (roll <= 0f) return e.species;
            }
            return null;
        }

        bool Eligible(SpawnEntry e, float minSize)
        {
            if (e?.species == null || e.weight <= 0f) return false;
            if (e.maxAlive > 0 && GetAlive(e.species) >= e.maxAlive) return false;
            if (e.species.size < minSize && !e.species.isBoss) return false;
            return true;
        }

        float SumWeights(ZoneData zone, float minSize, float mult)
        {
            if (zone == null || zone.spawnTable == null || mult <= 0f) return 0f;
            float total = 0f;
            foreach (var e in zone.spawnTable) if (Eligible(e, minSize)) total += e.weight * mult;
            return total;
        }

        /// <summary>
        /// 플레이어 화면 바로 바깥의 링 안에서 자리를 찾는다.
        /// 맵 전체에 균일하게 뿌리면 큰 존에서 화면이 텅 비어 보인다.
        /// </summary>
        bool FindSpawnPosition(float innerRatio, out Vector2 result)
        {
            result = Vector2.zero;

            Vector2 playerPos = PlayerPos;

            float view = _run != null ? _run.ViewRadius : 10f;
            float inner = view * (innerRatio > 0f ? innerRatio : RingInner);
            float outer = Mathf.Max(inner + 1f, view * RingOuter);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Mathf.Lerp(inner, outer, Random.value);
                Vector2 p = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                if (!_layout.Contains(p, 1f)) continue;
                if (Obstacle.Overlaps(p, 1.5f)) continue;   // 구조물 안에서 튀어나오지 않게

                result = p;
                return true;
            }

            // 링이 거의 벽 바깥이면(작은 어항 · 벽 가까이) 원 안의 아무 곳 — 화면 밖 · 디스폰 범위 안
            float despawn = view * DespawnRatio * 0.95f;
            float wall = Mathf.Max(1.5f, _layout.ActiveRadius - 1f);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 p = Random.insideUnitCircle * wall;
                if (Obstacle.Overlaps(p, 1.5f)) continue;
                float d = Vector2.Distance(p, playerPos);
                if (d < Mathf.Min(inner, view * 1.02f) || d > despawn) continue;   // 화면 안에서 튀어나오거나, 태어나자마자 디스폰되지 않게
                result = p;
                return true;
            }
            // 원이 화면보다 작으면 화면 안이라도 원 안 아무 곳 (어항 초반)
            if (wall < inner)
            {
                result = Random.insideUnitCircle * wall;
                return true;
            }
            return false;
        }

        /// <summary>화면에서 한참 벗어난 물고기를 회수한다. 풀을 재활용해 밀도를 유지한다.</summary>
        void CullDistantFish()
        {
            if (_run == null || _run.Player == null) return;

            Vector2 playerPos = _run.Player.transform.position;
            float limit = _run.ViewRadius * DespawnRatio;
            float limitSqr = limit * limit;
            // 다른 구역 물고기는 화면 밖으로 나가는 즉시 회수한다 (한도를 안 세더라도 풀을 붙잡고 있으므로)
            float offscreen = _run.ViewRadius * 1.1f;
            float offscreenSqr = offscreen * offscreen;
            int zone = _run.CurrentZoneIndex;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var fish = _active[i];
                if (fish == null) { _active.RemoveAt(i); continue; }
                if (fish == _boss) continue;      // 보스는 절대 회수하지 않는다

                float d2 = ((Vector2)fish.transform.position - playerPos).sqrMagnitude;
                if (d2 > limitSqr || (!IsActiveZone(fish.HomeZone, zone) && d2 > offscreenSqr))
                    Despawn(fish);
            }
        }

        void Update()
        {
            if (_run == null || !_run.IsRunning || _run.IsPaused) return;
            if (Time.time < _nextCullAt) return;
            _nextCullAt = Time.time + cullInterval;
            CullDistantFish();
        }

        public AIFish Spawn(FishSpecies species, Vector2 position, int zoneIndex, Vector2 heading)
        {
            var fish = _pool.Count > 0 ? _pool.Pop() : CreateInstance();
            fish.transform.SetPositionAndRotation(position, Quaternion.identity);
            fish.gameObject.SetActive(true);
            fish.Setup(species, _layout, zoneIndex, this, heading);

            _active.Add(fish);
            _aliveCount[species] = GetAlive(species) + 1;
            return fish;
        }

        /// <summary>바다의 구조물이 깨졌을 때 RunManager가 호출.</summary>
        public AIFish SpawnBoss(FishSpecies bossSpecies, Vector2 position)
        {
            if (bossSpecies == null || _boss != null || _layout == null) return _boss;
            int zone = _run != null ? _run.CurrentZoneIndex : _layout.ZoneIndexAt(position);
            _boss = Spawn(bossSpecies, position, zone, Vector2.left);
            return _boss;
        }

        // ── 디스폰 ──────────────────────────────────────────────
        public void Despawn(AIFish fish)
        {
            if (fish == null) return;
            if (_active.Remove(fish) && fish.Species != null)
                _aliveCount[fish.Species] = Mathf.Max(0, GetAlive(fish.Species) - 1);

            if (fish == _boss) _boss = null;

            fish.gameObject.SetActive(false);
            fish.transform.SetParent(poolParent, false);
            _pool.Push(fish);
        }

        public void DespawnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--) Despawn(_active[i]);
            _active.Clear();
            _aliveCount.Clear();
            _boss = null;
        }

        int GetAlive(FishSpecies s) => _aliveCount.TryGetValue(s, out int c) ? c : 0;

    }
}
