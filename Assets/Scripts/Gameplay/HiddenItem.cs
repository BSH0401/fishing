using FishGame.Player;
using FishGame.Utils;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 구역 구석에 숨어 있는 수집품. 먹으면 영구적으로 재화 획득량 +5%.
    ///
    /// 한 번 먹으면 세이브에 기록되어 다시 생성되지 않는다.
    /// 그래서 "아래로 내려가기"만이 아니라 "구석을 뒤지기"에도 이유가 생긴다.
    /// </summary>
    public class HiddenItem : MonoBehaviour
    {
        /// <summary>세이브에 남는 고유 ID. "존이름#번호" 형태.</summary>
        public string ItemId { get; private set; }
        public int ZoneIndex { get; private set; }
        public bool Collected { get; private set; }

        SpriteRenderer _icon;
        SpriteRenderer _glow;
        CircleCollider2D _trigger;

        float _bobPhase;
        float _baseY;
        float _scale;
        float _collectAnim;

        public static HiddenItem Create(Transform parent, Vector3 position,
                                        string id, int zoneIndex, float scale)
        {
            var go = new GameObject($"HiddenItem_{id}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var item = go.AddComponent<HiddenItem>();
            item.Setup(id, zoneIndex, scale);
            return item;
        }

        public void Setup(string id, int zoneIndex, float scale = 1.6f)
        {
            ItemId = id;
            ZoneIndex = zoneIndex;
            Collected = false;
            _scale = Mathf.Max(0.5f, scale);
            _baseY = transform.position.y;
            _bobPhase = Random.value * Mathf.PI * 2f;

            _glow = Prims.NewSprite(transform, "Glow", Prims.SoftDot,
                                    new Color(1f, 0.88f, 0.45f, 0.35f), -58);
            _glow.transform.localScale = Vector3.one * (_scale * 3.2f);

            _icon = Prims.NewSprite(transform, "Icon", Prims.Diamond,
                                    new Color(1f, 0.82f, 0.32f, 1f), -55);
            _icon.transform.localScale = Vector3.one * _scale;

            if (_trigger == null) _trigger = gameObject.AddComponent<CircleCollider2D>();
            _trigger.radius = _scale * 0.9f;
            _trigger.isTrigger = true;
            _trigger.enabled = true;
        }

        void Update()
        {
            if (Collected) { AnimateCollect(); return; }

            // 위아래로 살짝 떠다니고 반짝인다 — 멀리서도 눈에 띄어야 한다
            _bobPhase += Time.deltaTime * 1.5f;
            var p = transform.position;
            p.y = _baseY + Mathf.Sin(_bobPhase) * _scale * 0.35f;
            transform.position = p;

            if (_icon != null)
                _icon.transform.Rotate(0f, 0f, 45f * Time.deltaTime);

            if (_glow != null)
            {
                float pulse = 0.25f + Mathf.Abs(Mathf.Sin(_bobPhase * 0.8f)) * 0.3f;
                var c = _glow.color;
                _glow.color = new Color(c.r, c.g, c.b, pulse);
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (Collected || other == null) return;

            var player = other.GetComponentInParent<PlayerFish>();
            if (player == null || !player.IsAlive) return;

            Collect();
        }

        void Collect()
        {
            if (Collected) return;
            Collected = true;

            if (_trigger != null) _trigger.enabled = false;

            Juice.Hit(0.05f, 0.5f);
            RunManager.Instance?.ReportHiddenItem(this);
        }

        void AnimateCollect()
        {
            if (_collectAnim >= 1f) return;
            _collectAnim = Mathf.Min(1f, _collectAnim + Time.unscaledDeltaTime * 2.2f);
            float k = Mathf.SmoothStep(0f, 1f, _collectAnim);

            transform.position += Vector3.up * (Time.unscaledDeltaTime * _scale * 3f);

            if (_icon != null)
            {
                _icon.transform.localScale = Vector3.one * (_scale * (1f + k * 1.2f));
                _icon.transform.Rotate(0f, 0f, 420f * Time.unscaledDeltaTime);
                var c = _icon.color;
                _icon.color = new Color(c.r, c.g, c.b, 1f - k);
            }
            if (_glow != null)
            {
                var c = _glow.color;
                _glow.color = new Color(c.r, c.g, c.b, Mathf.Sin(k * Mathf.PI) * 0.7f);
            }

            if (_collectAnim >= 1f) gameObject.SetActive(false);
        }
    }
}
