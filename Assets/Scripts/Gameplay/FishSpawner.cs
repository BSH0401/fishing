using System.Collections;
using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 맵의 스폰 테이블에 따라 AI 물고기를 채워 넣는다.
    /// 프리팹은 1종(AIFish)만 쓰고 스프라이트/크기/패턴은 FishSpecies에서 주입한다.
    /// 오브젝트 풀을 써서 런타임 Instantiate를 피한다.
    /// </summary>
    public class FishSpawner : MonoBehaviour
    {
        [Header("프리팹")]
        [Tooltip("AIFish + FishBody + Rigidbody2D + CircleCollider2D(IsTrigger) 가 붙은 프리팹")]
        [SerializeField] AIFish fishPrefab;
        [SerializeField] Transform poolParent;
        [Tooltip("시작할 때 미리 만들어 둘 개수")]
        [SerializeField] int prewarmCount = 40;

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

        MapData _map;
        RunManager _run;
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

        public void BeginSpawning(MapData map, RunManager run)
        {
            StopSpawning();
            DespawnAll();

            _map = map;
            _run = run;
            if (_map == null || fishPrefab == null)
            {
                Debug.LogError("[FishSpawner] MapData 또는 fishPrefab이 없습니다.");
                return;
            }

            // 시작 시 절반은 즉시 채워서 빈 화면을 피한다
            int initial = Mathf.Max(1, _map.targetPopulation / 2);
            for (int i = 0; i < initial; i++) TrySpawnOne(ignorePlayerMargin: false);

            _spawnLoop = StartCoroutine(SpawnLoop());
        }

        public void StopSpawning()
        {
            if (_spawnLoop != null) { StopCoroutine(_spawnLoop); _spawnLoop = null; }
        }

        IEnumerator SpawnLoop()
        {
            var wait = new WaitForSeconds(_map.spawnInterval);
            while (true)
            {
                if (_run != null && !_run.IsPaused && _active.Count < _map.targetPopulation)
                    TrySpawnOne(ignorePlayerMargin: false);
                yield return wait;
            }
        }

        // ── 스폰 ────────────────────────────────────────────────
        bool TrySpawnOne(bool ignorePlayerMargin)
        {
            var species = PickSpecies();
            if (species == null) return false;

            Vector2 pos = FindSpawnPosition(species, ignorePlayerMargin);
            Vector2 heading = Random.value < 0.5f ? Vector2.left : Vector2.right;
            heading = (heading + Random.insideUnitCircle * 0.35f).normalized;

            Spawn(species, pos, heading);
            return true;
        }

        FishSpecies PickSpecies()
        {
            float total = 0f;
            foreach (var e in _map.spawnTable)
            {
                if (e?.species == null || e.weight <= 0f) continue;
                if (e.maxAlive > 0 && GetAlive(e.species) >= e.maxAlive) continue;
                total += e.weight;
            }
            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (var e in _map.spawnTable)
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
        /// 맵 전체에 균일하게 뿌리면 큰 맵에서 화면이 텅 비어 보인다.
        /// </summary>
        Vector2 FindSpawnPosition(FishSpecies species, bool ignorePlayerMargin)
        {
            var b = _map.WorldBounds;
            float m = species.size * 0.5f;
            Vector2 playerPos = _run != null && _run.Player != null
                ? (Vector2)_run.Player.transform.position : Vector2.zero;

            float view = _run != null ? _run.ViewRadius : 10f;
            float inner = ignorePlayerMargin ? species.size : Mathf.Max(_map.spawnMarginFromPlayer * 0.35f,
                                                                        view * spawnRingInner);
            float outer = Mathf.Max(inner + species.size * 2f, view * spawnRingOuter);

            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Mathf.Lerp(inner, outer, Random.value);
                Vector2 p = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                if (p.x >= b.xMin + m && p.x <= b.xMax - m &&
                    p.y >= b.yMin + m && p.y <= b.yMax - m)
                    return p;
            }

            // 링이 맵 밖으로만 나간다면(구석에 몰린 경우) 맵 안 아무 곳이나
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 p = new Vector2(
                    Random.Range(b.xMin + m, b.xMax - m),
                    Random.Range(b.yMin + m, b.yMax - m));
                if (Vector2.Distance(p, playerPos) >= inner * 0.6f) return p;
            }

            return new Vector2(
                Random.Range(b.xMin + m, b.xMax - m),
                Random.Range(b.yMin + m, b.yMax - m));
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

        public AIFish Spawn(FishSpecies species, Vector2 position, Vector2 heading)
        {
            var fish = _pool.Count > 0 ? _pool.Pop() : CreateInstance();
            fish.transform.SetPositionAndRotation(position, Quaternion.identity);
            fish.gameObject.SetActive(true);
            fish.Setup(species, _map.WorldBounds, this, heading);

            _active.Add(fish);
            _aliveCount[species] = GetAlive(species) + 1;
            return fish;
        }

        /// <summary>보스 게이트가 열렸을 때 RunManager가 호출.</summary>
        public void SpawnBoss(FishSpecies bossSpecies, Vector2 position)
        {
            if (bossSpecies == null || _boss != null) return;
            _boss = Spawn(bossSpecies, position, Vector2.left);
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
