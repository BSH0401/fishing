using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 황금 미끼. 일정 시간 동안 반경 안의 물고기를 자기 쪽으로 끌어당긴다.
    /// AIFish가 매 프레임 Bait.Nearest()를 물어보는 구조라 미끼 자신은 아주 가볍다.
    /// </summary>
    public class Bait : MonoBehaviour
    {
        static readonly List<Bait> Active = new List<Bait>();

        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] float bobAmplitude = 0.25f;
        [SerializeField] float bobFrequency = 1.6f;
        [SerializeField] float spinSpeed = 90f;

        float _radius;
        float _expireAt;
        Vector3 _basePosition;
        float _phase;

        public float Radius => _radius;

        public static IReadOnlyList<Bait> ActiveBaits => Active;

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        public void Launch(Vector2 position, float radius, float lifetime)
        {
            _basePosition = position;
            transform.position = position;
            _radius = Mathf.Max(0.5f, radius);
            _expireAt = Time.time + Mathf.Max(0.2f, lifetime);
            _phase = Random.value * Mathf.PI * 2f;
            gameObject.SetActive(true);
        }

        void Update()
        {
            var run = RunManager.Instance;
            if (run != null && run.IsPaused) return;

            if (Time.time >= _expireAt) { gameObject.SetActive(false); return; }

            _phase += bobFrequency * Time.deltaTime * Mathf.PI * 2f;
            transform.position = _basePosition + new Vector3(0f, Mathf.Sin(_phase) * bobAmplitude, 0f);
            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

            // 사라지기 직전에 서서히 흐려진다
            if (spriteRenderer != null)
            {
                float remain = _expireAt - Time.time;
                var c = spriteRenderer.color;
                c.a = remain < 1f ? Mathf.Clamp01(remain) : 1f;
                spriteRenderer.color = c;
            }
        }

        /// <summary>pos에서 끌어당김 범위 안에 있는 가장 가까운 미끼. 없으면 null.</summary>
        public static Bait Nearest(Vector2 pos)
        {
            Bait best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < Active.Count; i++)
            {
                var b = Active[i];
                if (b == null || !b.isActiveAndEnabled) continue;

                float sqr = ((Vector2)b.transform.position - pos).sqrMagnitude;
                if (sqr > b._radius * b._radius) continue;
                if (sqr < bestSqr) { bestSqr = sqr; best = b; }
            }
            return best;
        }

        public static void DespawnAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] != null) Active[i].gameObject.SetActive(false);
            Active.Clear();
        }
    }
}
