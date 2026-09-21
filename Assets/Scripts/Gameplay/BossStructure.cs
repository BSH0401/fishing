using FishGame.Data;
using FishGame.Player;
using FishGame.Utils;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 바다 한가운데의 고리 구조물. 안에 보스가 갇혀 있다.
    ///
    /// 기존의 "보스방"을 대체한다. 따로 문을 열고 들어가는 대신,
    /// 부스터로 구조물에 들이받으면 구조물이 깨지고 보스가 풀려나
    /// 바다 전체를 돌아다니며 플레이어를 쫓는다.
    ///
    /// 그냥 스쳐도 깨지면 사고로 보스를 만나게 되므로, 반드시 부스터 중일 때만 깨진다.
    /// </summary>
    public class BossStructure : MonoBehaviour
    {
        public int ZoneIndex { get; private set; }
        public FishSpecies Boss { get; private set; }
        public bool IsBroken { get; private set; }

        SpriteRenderer _ring;
        SpriteRenderer _core;
        SpriteRenderer _glow;
        CircleCollider2D _trigger;
        float _radius;
        float _breakAnim;
        float _hintTimer;

        public static BossStructure Create(Transform parent, Vector3 position,
                                           int zoneIndex, FishSpecies boss, float radius)
        {
            var go = new GameObject("BossStructure");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var bs = go.AddComponent<BossStructure>();
            bs.Setup(zoneIndex, boss, radius);
            return bs;
        }

        public void Setup(int zoneIndex, FishSpecies boss, float radius)
        {
            ZoneIndex = zoneIndex;
            Boss = boss;
            _radius = Mathf.Max(1f, radius);
            IsBroken = false;
            _breakAnim = 0f;

            _glow = Prims.NewSprite(transform, "Glow", Prims.SoftDot,
                                    new Color(1f, 0.45f, 0.35f, 0.22f), -80);
            _glow.transform.localScale = Vector3.one * (_radius * 4.4f);

            _core = Prims.NewSprite(transform, "Core", Prims.SoftDot,
                                    new Color(0.85f, 0.25f, 0.22f, 0.55f), -66);
            _core.transform.localScale = Vector3.one * (_radius * 1.7f);

            _ring = Prims.NewSprite(transform, "Ring", Prims.Ring,
                                    new Color(0.24f, 0.26f, 0.32f, 1f), -60);
            _ring.transform.localScale = Vector3.one * (_radius * 2.2f);

            if (_trigger == null) _trigger = gameObject.AddComponent<CircleCollider2D>();
            _trigger.radius = _radius;
            _trigger.isTrigger = true;
            _trigger.enabled = true;
        }

        void Update()
        {
            if (IsBroken) { AnimateBreak(); return; }

            // 천천히 도는 고리 + 안쪽에서 맥동하는 붉은 빛 — 뭔가 갇혀 있다는 신호
            _ring.transform.Rotate(0f, 0f, 12f * Time.deltaTime);

            _hintTimer += Time.deltaTime;
            float pulse = 0.5f + Mathf.Sin(_hintTimer * 2.2f) * 0.5f;
            if (_core != null)
            {
                var c = _core.color;
                _core.color = new Color(c.r, c.g, c.b, 0.35f + pulse * 0.35f);
                _core.transform.localScale = Vector3.one * (_radius * (1.55f + pulse * 0.25f));
            }
        }

        void OnTriggerStay2D(Collider2D other) => TryBreak(other);
        void OnTriggerEnter2D(Collider2D other) => TryBreak(other);

        void TryBreak(Collider2D other)
        {
            if (IsBroken || other == null) return;

            var player = other.GetComponentInParent<PlayerFish>();
            if (player == null || !player.IsAlive) return;

            // 부스터 중일 때만 깨진다. 지나가다 부딪힌 걸로는 안 된다.
            if (!player.IsBoosting) return;

            Break();
        }

        public void Break()
        {
            if (IsBroken) return;
            IsBroken = true;

            if (_trigger != null) _trigger.enabled = false;

            Juice.Hit(0.10f, 1.5f);

            var run = RunManager.Instance;
            run?.ReleaseBoss(this);
        }

        void AnimateBreak()
        {
            if (_breakAnim >= 1f) return;
            _breakAnim = Mathf.Min(1f, _breakAnim + Time.unscaledDeltaTime * 1.6f);
            float k = Mathf.SmoothStep(0f, 1f, _breakAnim);

            if (_ring != null)
            {
                _ring.transform.localScale = Vector3.one * (_radius * 2.2f * (1f + k * 1.4f));
                _ring.transform.Rotate(0f, 0f, 260f * Time.unscaledDeltaTime);
                var c = _ring.color;
                _ring.color = new Color(c.r, c.g, c.b, 1f - k);
            }
            if (_core != null)
            {
                var c = _core.color;
                _core.color = new Color(c.r, c.g, c.b, (1f - k) * 0.6f);
            }
            if (_glow != null)
            {
                var c = _glow.color;
                _glow.color = new Color(c.r, c.g, c.b, Mathf.Sin(k * Mathf.PI) * 0.6f);
            }

            if (_breakAnim >= 1f) gameObject.SetActive(false);
        }
    }
}
