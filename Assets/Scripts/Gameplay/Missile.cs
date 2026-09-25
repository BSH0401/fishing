using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 미사일. 바라보는 방향으로 날아가 처음 맞은 물고기를 즉시 "먹힌 것"으로 처리한다.
    /// 보스와, 플레이어 크기 대비 너무 큰 물고기는 통과한다.
    /// 장애물 · 닫힌 통로 · 벽(Wall 레이어)에 부딪히면 그 자리에서 사라진다 — 건너편 물고기를 먹지 못한다.
    /// </summary>
    public class Missile : MonoBehaviour
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] TrailRenderer trail;

        static readonly List<Collider2D> HitBuffer = new List<Collider2D>(8);
        static readonly RaycastHit2D[] WallHits = new RaycastHit2D[4];
        static ContactFilter2D _wallFilter;
        static bool _wallFilterReady;

        ContactFilter2D _filter;
        Vector2 _velocity;
        float _expireAt;
        float _hitRadius;
        float _maxTargetSize;
        MissilePool _pool;

        public void Launch(Vector2 position, Vector2 direction, float speed, float lifetime,
                           float hitRadius, float maxTargetSize, LayerMask fishLayer, MissilePool pool)
        {
            _pool = pool;
            _velocity = direction.normalized * speed;
            _expireAt = Time.time + lifetime;
            _hitRadius = hitRadius;
            _maxTargetSize = maxTargetSize;

            _filter = new ContactFilter2D { useLayerMask = true, useTriggers = true };
            _filter.SetLayerMask(fishLayer);

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            gameObject.SetActive(true);
            if (trail != null) trail.Clear();
        }

        void Update()
        {
            var run = RunManager.Instance;
            if (run == null || run.IsPaused) return;

            if (Time.time >= _expireAt || !run.IsRunning) { Despawn(); return; }

            float dt = Time.deltaTime;
            Vector2 from = transform.position;
            Vector2 step = _velocity * dt;

            // 이번 프레임 이동 경로에 장애물 · 닫힌 통로 · 벽이 있으면 거기서 멈춘다.
            // 한 프레임에 여러 유닛을 날아가므로 도착점만 보면 얇은 통로 창살을 건너뛴다 — 경로 전체를 쓸어 본다.
            if (HitsWall(from, step, out Vector2 hitPoint))
            {
                // 맞은 자리에서 판정하지 않는다 — 판정 반경이 창살 너머까지 닿으면 막은 의미가 없다
                transform.position = hitPoint;
                Despawn();
                return;
            }
            transform.position += (Vector3)step;

            // 벽 밖으로 나가면 소멸
            Vector2 p = transform.position;
            if (!run.InsideWorld(p, -2f))
            {
                Despawn();
                return;
            }

            TryHit(run);
        }

        bool HitsWall(Vector2 from, Vector2 step, out Vector2 point)
        {
            point = from;
            float dist = step.magnitude;
            if (dist < 1e-5f) return false;

            if (!_wallFilterReady)
            {
                int wall = LayerMask.NameToLayer("Wall");
                _wallFilter = new ContactFilter2D { useLayerMask = true, useTriggers = false };
                _wallFilter.SetLayerMask(wall >= 0 ? (LayerMask)(1 << wall) : (LayerMask)0);
                _wallFilterReady = true;
            }

            float r = Mathf.Max(0.05f, _hitRadius * 0.25f);
            int n = Physics2D.CircleCast(from, r, step / dist, _wallFilter, WallHits, dist);
            if (n <= 0) return false;

            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (WallHits[i].collider != null && WallHits[i].distance < best)
                {
                    best = WallHits[i].distance;
                    point = WallHits[i].centroid;
                }
            return best < float.MaxValue;
        }

        void TryHit(RunManager run)
        {
            int count = Physics2D.OverlapCircle(transform.position, _hitRadius, _filter, HitBuffer);
            for (int i = 0; i < count; i++)
            {
                var body = HitBuffer[i].GetComponentInParent<FishBody>();
                if (body == null || !body.IsAlive || body.IsPlayer) continue;
                if (body.IsBoss) continue;                    // 보스는 미사일로 못 잡는다
                if (body.Size > _maxTargetSize) continue;     // 너무 큰 물고기는 통과

                run.Player?.ConsumeByEffect(body);
                Despawn();
                return;
            }
        }

        void Despawn()
        {
            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }
    }

    /// <summary>미사일 오브젝트 풀. PlayerFish가 하나 들고 쓴다.</summary>
    public class MissilePool
    {
        readonly Missile _prefab;
        readonly Transform _parent;
        readonly Stack<Missile> _pool = new Stack<Missile>();

        public MissilePool(Missile prefab, Transform parent, int prewarm = 6)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < prewarm; i++) _pool.Push(Create());
        }

        Missile Create()
        {
            var m = Object.Instantiate(_prefab, _parent);
            m.gameObject.SetActive(false);
            return m;
        }

        public Missile Get() => _pool.Count > 0 ? _pool.Pop() : Create();

        public void Return(Missile m)
        {
            if (m == null) return;
            m.gameObject.SetActive(false);
            _pool.Push(m);
        }
    }
}
