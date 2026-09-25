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
        /// <summary>한 변(또는 지름)의 길이. 콜라이더와 그림이 모두 이 크기다.</summary>
        public float Size { get; private set; }

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
            // 원·삼각 스프라이트는 64px / 32ppu라 한 변이 2유닛이다. 그대로 size를 곱하면
            // 그림이 콜라이더의 두 배가 되어, 물고기가 장애물 바깥 절반을 뚫고 지나가는 것처럼 보였다.
            // 스프라이트 실제 크기로 나눠 그림 = 콜라이더가 되게 맞춘다.
            float spriteW = sprite != null ? Mathf.Max(0.0001f, sprite.bounds.size.x) : 1f;
            _sr.transform.localScale = Vector3.one * (size / spriteW);
            Size = size;

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
                if (o == null || !o.isActiveAndEnabled) continue;
                float reach = o.AvoidRadius + radius;
                if ((pos - o.Center).sqrMagnitude < reach * reach) return true;
            }
            return false;
        }

        /// <summary>그 자리와 겹치는 장애물 하나. 없으면 null.</summary>
        public static Obstacle FindOverlapping(Vector2 pos, float radius)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var o = _all[i];
                if (o == null || !o.isActiveAndEnabled) continue;
                float reach = o.AvoidRadius + radius;
                if ((pos - o.Center).sqrMagnitude < reach * reach) return o;
            }
            return null;
        }

        /// <summary>
        /// 원(반지름 radius)이 장애물 모양 안으로 파고들었으면 바깥으로 밀어낸 자리를 돌려준다.
        ///
        /// AI 물고기는 먹이 판정 때문에 트리거 콜라이더라 물리로는 장애물에 막히지 않는다.
        /// 조향(AvoidForce)만으로는 플레이어를 쫓거나 돌진할 때 장애물을 그대로 뚫고 지나갔다 —
        /// 그래서 매 물리 프레임 실제 모양(네모·원·삼각)으로 겹침을 풀어 준다.
        /// </summary>
        /// <returns>밀어냈으면 true. normal은 밀려난 방향(월드).</returns>
        public static bool PushOut(ref Vector2 pos, float radius, out Vector2 normal)
        {
            normal = Vector2.zero;
            bool moved = false;
            for (int i = 0; i < _all.Count; i++)
            {
                var o = _all[i];
                if (o == null || !o.isActiveAndEnabled || o.Size <= 0f) continue;

                float bound = (o.Shape == ObstacleShape.Disc ? 0.5f : 0.7072f) * o.Size + radius;
                if ((pos - o.Center).sqrMagnitude >= bound * bound) continue;

                if (o.ResolveLocal(pos, radius, out Vector2 resolved))
                {
                    Vector2 d = resolved - pos;
                    if (d.sqrMagnitude > 1e-8f) normal = (normal + d.normalized).normalized;
                    pos = resolved;
                    moved = true;
                }
            }
            return moved;
        }

        /// <summary>한 장애물에 대해 겹침을 푼다 (장애물 로컬 좌표에서 계산).</summary>
        bool ResolveLocal(Vector2 worldPos, float radius, out Vector2 result)
        {
            result = worldPos;
            float rot = transform.eulerAngles.z * Mathf.Deg2Rad;
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            Vector2 w = worldPos - Center;
            Vector2 p = new Vector2(w.x * c + w.y * s, -w.x * s + w.y * c);   // 역회전
            float h = Size * 0.5f;
            Vector2 q;

            switch (Shape)
            {
                case ObstacleShape.Disc:
                {
                    float d = p.magnitude, reach = h + radius;
                    if (d >= reach) return false;
                    Vector2 dir = d > 1e-5f ? p / d : Vector2.up;
                    q = dir * reach;
                    break;
                }
                case ObstacleShape.Triangle:
                {
                    Vector2 a = new Vector2(0f, h), b = new Vector2(-h, -h), e = new Vector2(h, -h);
                    Vector2 best = ClosestOnSegment(p, a, b);
                    Vector2 cand = ClosestOnSegment(p, b, e);
                    if ((cand - p).sqrMagnitude < (best - p).sqrMagnitude) best = cand;
                    cand = ClosestOnSegment(p, e, a);
                    if ((cand - p).sqrMagnitude < (best - p).sqrMagnitude) best = cand;

                    bool inside = InsideTriangle(p, a, b, e);
                    Vector2 diff = p - best;
                    float dist = diff.magnitude;
                    if (!inside && dist >= radius) return false;
                    Vector2 outDir = dist > 1e-5f ? (inside ? -diff / dist : diff / dist) : (p.sqrMagnitude > 1e-6f ? p.normalized : Vector2.up);
                    q = best + outDir * radius;
                    break;
                }
                default:
                {
                    Vector2 cl = new Vector2(Mathf.Clamp(p.x, -h, h), Mathf.Clamp(p.y, -h, h));
                    bool inside = Mathf.Abs(p.x) < h && Mathf.Abs(p.y) < h;
                    if (inside)
                    {
                        // 가장 가까운 면으로 내보낸다
                        float dx = h - Mathf.Abs(p.x), dy = h - Mathf.Abs(p.y);
                        q = dx < dy
                            ? new Vector2(Mathf.Sign(p.x == 0f ? 1f : p.x) * (h + radius), p.y)
                            : new Vector2(p.x, Mathf.Sign(p.y == 0f ? 1f : p.y) * (h + radius));
                    }
                    else
                    {
                        Vector2 diff = p - cl;
                        float dist = diff.magnitude;
                        if (dist >= radius) return false;
                        q = cl + diff / Mathf.Max(1e-5f, dist) * radius;
                    }
                    break;
                }
            }

            // 다시 월드로
            result = Center + new Vector2(q.x * c - q.y * s, q.x * s + q.y * c);
            return true;
        }

        static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (a.x - p.x) * (b.y - p.y) - (a.y - p.y) * (b.x - p.x);

        /// <summary>새 판을 만들 때 WorldBuilder가 호출 — 파괴된 항목을 정리한다.</summary>
        public static void ClearRegistry() => _all.RemoveAll(o => o == null);
    }
}
