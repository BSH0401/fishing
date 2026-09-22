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
    /// 자리를 먼저 정하는 이유는 경계 근처에서 두 존의 물고기가 자연스럽게 섞이게 하려는 것이다.
    /// 통로를 빠져나오는 순간 눈앞의 물고기가 한꺼번에 바뀌면 이어진 공간처럼 느껴지지 않는다.
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

        [Header("플레이어 주변 스폰")]
        [Tooltip("화면 반경의 이 배수 바깥에서 스폰한다 (1.05 = 화면 바로 밖)")]
        [SerializeField] float spawnRingInner = 1.10f;
        [Tooltip("화면 반경의 이 배수 안쪽까지만 스폰한다")]
        [SerializeField] float spawnRingOuter = 1.85f;
        [Tooltip("화면 반경의 이 배수보다 멀어진 물고기는 회수해서 다시 쓴다")]
        [SerializeField] float despawnRadiusRatio = 2.8f;
        [Tooltip("멀어진 물고기를 정리하는 주기(초)")]
        [SerializeField] float cullInterval = 0.75f;

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

            // 시작 시 절반은 즉시 채워서 빈 화면을 피한다
            int initial = Mathf.Max(1, TargetPopulation / 2);
            for (int i = 0; i < initial; i++) TrySpawnOne();

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

                if (_run != null && !_run.IsPaused && _active.Count < TargetPopulation)
                    TrySpawnOne();

                yield return new WaitForSeconds(interval);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  스폰
        // ══════════════════════════════════════════════════════════
        bool TrySpawnOne()
        {
            if (!FindSpawnPosition(out Vector2 pos, out int zoneIndex)) return false;

            var species = PickSpecies(zoneIndex);
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

        /// <summary>그 자리가 속한 존의 스폰 테이블에서 뽑는다.</summary>
        FishSpecies PickSpecies(int zoneIndex)
        {
            var zone = _layout.GetZone(zoneIndex);
            if (zone == null || zone.spawnTable == null) return null;

            float total = 0f;
            foreach (var e in zone.spawnTable)
            {
                if (e?.species == null || e.weight <= 0f) continue;
                if (e.maxAlive > 0 && GetAlive(e.species) >= e.maxAlive) continue;
                total += e.weight;
            }
            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (var e in zone.spawnTable)
            {
                if (e?.species == null || e.weight <= 0f) continue;
                if (e.maxAlive > 0 && GetAlive(e.species) >= e.maxAlive) continue;
                roll -= e.weight;
                if (roll <= 0f) return e.species;
            }
            return null;
        }

        /// <summary>
        /// 플레이어 화면 바로 바깥의 링 안에서 자리를 찾는다.
        /// 맵 전체에 균일하게 뿌리면 큰 존에서 화면이 텅 비어 보인다.
        /// </summary>
        bool FindSpawnPosition(out Vector2 result, out int zoneIndex)
        {
            result = Vector2.zero;
            zoneIndex = _run != null ? _run.CurrentZoneIndex : 0;

            Vector2 playerPos = _run != null && _run.Player != null
                ? (Vector2)_run.Player.transform.position : Vector2.zero;

            float view = _run != null ? _run.ViewRadius : 10f;
            float inner = view * spawnRingInner;
            float outer = Mathf.Max(inner + 1f, view * spawnRingOuter);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Mathf.Lerp(inner, outer, Random.value);
                Vector2 p = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                if (!_layout.Contains(p, 1f)) continue;
                if (Obstacle.Overlaps(p, 1.5f)) continue;   // 구조물 안에서 튀어나오지 않게

                result = p;
                zoneIndex = _layout.ZoneIndexAt(p.y);
                return true;
            }

            // 링이 전부 벽 바깥이면(좁은 통로 안 등) 같은 높이의 안쪽 아무 곳
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float y = playerPos.y + Random.Range(-view, view);
                float cx = _layout.CenterXAt(y);
                float hw = _layout.HalfWidthAt(y);
                Vector2 p = new Vector2(cx + Random.Range(-hw * 0.85f, hw * 0.85f), y);

                if (!_layout.Contains(p, 1f)) continue;
                if (Obstacle.Overlaps(p, 1.5f)) continue;
                if (Vector2.Distance(p, playerPos) < inner * 0.5f) continue;

                result = p;
                zoneIndex = _layout.ZoneIndexAt(p.y);
                return true;
            }

            return false;
        }

        /// <summary>화면에서 한참 벗어난 물고기를 회수한다. 풀을 재활용해 밀도를 유지한다.</summary>
        void CullDistantFish()
        {
            if (_run == null || _run.Player == null) return;

            Vector2 playerPos = _run.Player.transform.position;
            float limit = _run.ViewRadius * despawnRadiusRatio;
            float limitSqr = limit * limit;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var fish = _active[i];
                if (fish == null) { _active.RemoveAt(i); continue; }
                if (fish == _boss) continue;      // 보스는 절대 회수하지 않는다

                if (((Vector2)fish.transform.position - playerPos).sqrMagnitude > limitSqr)
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
            int zone = _layout.ZoneIndexAt(position.y);
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
