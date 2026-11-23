using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>Short, replayable emphasis that preserves the authored scale and color.</summary>
    public sealed class UIEmphasis : MonoBehaviour
    {
        Coroutine _animation;
        Vector3 _baseScale;
        Image _flashImage, _glow;
        Color _baseColor;
        bool _captured;

        public static void Pulse(Transform target, float duration = 0.12f, float strength = 0.14f, bool glow = false, bool immediate = false)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            Get(target).Play(duration, strength, null, Color.white, glow, immediate);
        }

        public static void Flash(Image target, Color tint, float duration = 1f)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            Get(target.transform).Play(duration, 0f, target, tint, false);
        }

        static UIEmphasis Get(Transform target)
        {
            var effect = target.GetComponent<UIEmphasis>();
            return effect != null ? effect : target.gameObject.AddComponent<UIEmphasis>();
        }

        void Play(float duration, float strength, Image flash, Color tint, bool glow, bool immediate = false)
        {
            if (_animation != null) StopCoroutine(_animation);
            Restore();
            _baseScale = transform.localScale;
            _flashImage = flash;
            if (flash != null) _baseColor = flash.color;
            _captured = true;
            if (glow && _glow == null)
            {
                _glow = UIKit.Image(transform, "Ready Glow", LabArt.SoftDot, Color.clear);
                UIKit.Stretch(_glow.rectTransform);
                _glow.rectTransform.offsetMin = new Vector2(-10f, -10f);
                _glow.rectTransform.offsetMax = new Vector2(10f, 10f);
            }
            _animation = StartCoroutine(Animate(Mathf.Max(0.01f, duration), strength, tint, glow, immediate));
        }

        IEnumerator Animate(float duration, float strength, Color tint, bool glow, bool immediate)
        {
            // A chained pulse must visibly grow on its first frame, not yield at scale 1.
            float elapsed = immediate ? Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, duration * 0.5f) : 0f;
            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(t * Mathf.PI);
                transform.localScale = _baseScale * (1f + strength * pulse);
                if (_flashImage != null) _flashImage.color = Color.Lerp(_baseColor, tint, pulse * 0.65f);
                if (_glow != null)
                    _glow.color = new Color(0.5f, 1f, 0.9f, glow ? pulse * 0.55f : 0f);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            _animation = null;
            Restore();
        }

        void Restore()
        {
            if (!_captured) return;
            transform.localScale = _baseScale;
            if (_flashImage != null) _flashImage.color = _baseColor;
            if (_glow != null) _glow.color = Color.clear;
        }

        void OnDisable()
        {
            if (_animation != null) StopCoroutine(_animation);
            _animation = null;
            Restore();
        }
    }
}
