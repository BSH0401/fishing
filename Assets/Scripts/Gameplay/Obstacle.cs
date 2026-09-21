using System.Collections.Generic;
using FishGame.Data;
using FishGame.Utils;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 헤엄쳐 지나갈 수 없는 구조물. 기획 PPT의 구역별 도형에 대응한다.
    ///
    ///   어항   네모 — 장식품(성·집)
    ///   하수구 원   — 쇠창살 원형
    ///   강     삼각 — 자갈 무더기
    ///   바다   삼각 — 암초
    ///
    /// 물리 콜라이더만 두면 물고기가 구석에 끼므로, 정적 목록을 들고 있다가
    /// AI와 플레이어가 조향할 때 반발력으로 읽어가게 한다.
    /// (Physics2D 질의를 매 프레임 돌리는 것보다 훨씬 싸다 — 맵 전체에 10개 안팎이다)
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        static readonly List<Obstacle> _all = new List<Obstacle>();
        public static IReadOnlyList<Obstacle> All => _all;

        /// <summary>조향용 근사 반경. 모양이 무엇이든 원으로 친다.</summary>
        public float AvoidRadius { get; private set; }
        public Vector2 Center { get; private set; }
        public ObstacleShape Shape { get; private set; }

        SpriteRenderer _sr;

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            Center = transform.position;
        }

        void OnDisable() => _all.Remove(this);

        // ══════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════
        public static Obstacle Create(Transform parent, Vector2 position, ObstacleShape shape,
                                      float size, float rotation, Color color, int sortingOrder)
        {
            var go = new GameObject($"Obstacle_{shape}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotation);

            var ob = go.AddComponent<Obstacle>();
            ob.Setup(shape, size, color, sortingOrder);
            return ob;
        }

        public void Setup(ObstacleShape shape, float size, Color color, int sortingOrder)
        {
            Shape = shape;
            size = Mathf.Max(0.5f, size);
            Center = transform.position;

            int wall = LayerMask.NameToLayer("Wall");
            if (wall >= 0) gameObject.layer = wall;

            Sprite sprite = shape switch
            {
                ObstacleShape.Disc     => Prims.Disc,
                ObstacleShape.Triangle => Prims.Triangle,
                _                      => Prims.White,
            };

            _sr = Prims.NewSprite(transform, "Body", sprite, color, sortingOrder);
            _sr.transform.localScale = Vector3.one * size;

            BuildCollider(shape, size);

            // 조향용 반경 — 네모는 대각선이 더 기니까 조금 크게 잡는다
            AvoidRadius = shape == ObstacleShape.Box ? size * 0.62f : size * 0.5f;

            if (GetComponent<Rigidbody2D>() == null)
                gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        }

        void BuildCollider(ObstacleShape shape, float size)
        {
            switch (shape)
            {
                case ObstacleShape.Disc:
                {
                    var c = gameObject.AddComponent<CircleCollider2D>();
                    c.radius = size * 0.5f;
                    break;
                }
                case ObstacleShape.Triangle:
                {
                    var c = gameObject.AddComponent<PolygonCollider2D>();
                    float h = size * 0.5f;
                    c.points = new[]
                    {
                        new Vector2(0f,  h),
                        new Vector2(-h, -h),
                        new Vector2( h, -h),
                    };
                    break;
                }
                default:
                {
                    var c = gameObject.AddComponent<BoxCollider2D>();
                    c.size = new Vector2(size, size);
                    break;
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        //  질의
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 장애물을 피하는 힘. 벽 회피(WallAvoidForce)와 같은 방식으로 더해 쓴다.
        /// 가까울수록 세게 밀어내고, 사거리 밖이면 0이다.
        /// </summary>
        public static Vector2 AvoidForce(Vector2 pos, float selfRadius, float margin)
        {
            if (_all.Count == 0) return Vector2.zero;

            Vector2 force = Vector2.zero;
            for (int i = 0; i < _all.Count; i++)
            {
                var o = _all[i];
                if (o == null) continue;

                Vector2 d = pos - o.Center;
                float dist = d.magnitude;
                float reach = o.AvoidRadius + selfRadius + margin;
                if (dist >= reach) continue;
                if (dist < 0.0001f) { force += Random.insideUnitCircle.normalized; continue; }

                force += d / dist * (1f - dist / reach);
            }
            return Vector2.ClampMagnitude(force, 1f);
        }

        /// <summary>그 자리가 장애물 안(또는 여유 거리 안)인가. 스폰·배치 검사에 쓴다.</summary>
        public static bool Overlaps(Vector2 pos, float radius)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var o = _all[i];
                if (o == null) continue;
                float reach = o.AvoidRadius + radius;
                if ((pos - o.Center).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        /// <summary>새 판을 만들 때 WorldBuilder가 호출 — 파괴된 항목을 정리한다.</summary>
        public static void ClearRegistry() => _all.RemoveAll(o => o == null);
    }
}
