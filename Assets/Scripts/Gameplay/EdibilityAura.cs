using FishGame.UI;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 물고기 뒤에 깔리는 옅은 빛 — "먹을 수 있나"를 한눈에 보여 준다. (QA R10)
    ///
    ///   초록   지금 크기로 먹을 수 있다
    ///   빨강   나를 먹을 수 있다 (천천히 맥박친다)
    ///
    /// 판정은 실제 포식과 같은 FishBody.CanEat(eatSizeTolerance)이라, 판 중에 커지면 그 자리에서 색이 바뀐다.
    /// 몸 그림(SpriteRenderer)의 자식으로 붙어 회전 · 좌우 반전을 그대로 따라간다.
    /// </summary>
    [DisallowMultipleComponent]
    public class EdibilityAura : MonoBehaviour
    {
        static readonly Color EdibleColor = new Color(0.35f, 1f, 0.55f, 0.38f);
        static readonly Color DangerColor = new Color(1f, 0.26f, 0.26f, 0.5f);

        const float WidthScale = 1.45f;    // 몸 그림 폭 대비 빛 크기
        const float HeightScale = 2.1f;    // 물고기 그림은 납작해서 세로를 더 키운다
        const float FadeSpeed = 10f;

        FishBody _body;
        SpriteRenderer _aura;
        Sprite _fittedFor;
        float _alpha;
        Color _color = EdibleColor;

        void Awake() => _body = GetComponent<FishBody>();

        void EnsureAura()
        {
            if (_aura != null || _body == null || _body.Renderer == null) return;
            var go = new GameObject("EdibilityAura");
            go.transform.SetParent(_body.Renderer.transform, false);
            _aura = go.AddComponent<SpriteRenderer>();
            _aura.sprite = LabArt.SoftDot;
            _aura.color = Color.clear;
        }

        /// <summary>종이 바뀌면(풀 재사용) 그림 크기에 맞춰 빛을 다시 잰다.</summary>
        void Fit()
        {
            var body = _body.Renderer;
            if (body.sprite == _fittedFor) return;
            _fittedFor = body.sprite;

            _aura.sortingLayerID = body.sortingLayerID;
            _aura.sortingOrder = body.sortingOrder - 1;

            Vector3 dot = _aura.sprite != null ? _aura.sprite.bounds.size : Vector3.one;
            Bounds b = body.sprite != null ? body.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
            _aura.transform.localPosition = new Vector3(b.center.x, b.center.y, 0f);
            _aura.transform.localScale = new Vector3(
                b.size.x * WidthScale / Mathf.Max(1e-4f, dot.x),
                b.size.y * HeightScale / Mathf.Max(1e-4f, dot.y), 1f);
        }

        void LateUpdate()
        {
            EnsureAura();
            if (_aura == null) return;
            Fit();

            float target = 0f;
            var run = RunManager.Instance;
            var player = run != null && run.Player != null ? run.Player.Body : null;
            if (run != null && run.IsRunning && player != null && player.IsAlive && _body.IsAlive)
            {
                float tol = run.Database != null ? run.Database.eatSizeTolerance : 1f;
                // 실제 충돌 판정과 같은 순서 — 서로 먹을 수 있으면 플레이어가 먼저 먹는다
                if (FishBody.CanEat(player, _body, tol))
                {
                    _color = EdibleColor;
                    target = 1f;
                }
                else if (FishBody.CanEat(_body, player, tol))
                {
                    _color = DangerColor;
                    target = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f + GetInstanceID() * 0.37f);
                }
            }

            _alpha = Mathf.MoveTowards(_alpha, target, FadeSpeed * Time.unscaledDeltaTime);
            var c = _color;
            c.a *= _alpha;
            _aura.color = c;
            _aura.enabled = _alpha > 0.001f;
        }
    }
}
