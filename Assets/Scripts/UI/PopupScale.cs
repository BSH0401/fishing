using System.Collections;
using UnityEngine;

namespace FishGame.UI
{
    /// <summary>Unscaled popup animation with reversible closing and authored-state restoration.</summary>
    public sealed class PopupScale : MonoBehaviour
    {
        [SerializeField, Min(0f)] float duration = 0.2f;
        [SerializeField, Range(0.01f, 1f)] float startScale = 0.75f;
        Vector3 _originalScale;
        bool _hasOriginalScale;
        Coroutine _animation;
        bool _isHiding;
        CanvasGroup _fadeGroup;
        float _originalAlpha;
        bool _originalInteractable;
        bool _hasGroupState;

        public static bool IsHiding(GameObject root)
        {
            if (root == null) return false;
            var popup = root.GetComponent<PopupScale>();
            return popup != null && popup._isHiding;
        }

        public static void Show(GameObject root)
        {
            if (root == null) return;
            GetOrAdd(root).Play();
        }

        public static void Hide(GameObject root)
        {
            if (root == null || !root.activeSelf) return;
            GetOrAdd(root).Close();
        }

        static PopupScale GetOrAdd(GameObject root)
        {
            var popup = root.GetComponent<PopupScale>();
            return popup != null ? popup : root.AddComponent<PopupScale>();
        }

        void RememberScale()
        {
            if (_hasOriginalScale) return;
            _originalScale = transform.localScale;
            _hasOriginalScale = true;
        }

        void CancelAnimation()
        {
            if (_animation != null) StopCoroutine(_animation);
            _animation = null;
        }

        void Play()
        {
            RememberScale();
            bool reverseClose = _isHiding && gameObject.activeSelf;
            Vector3 fromScale = reverseClose ? transform.localScale : _originalScale * startScale;
            float fromAlpha = reverseClose && _fadeGroup != null ? _fadeGroup.alpha : _originalAlpha;
            CancelAnimation();
            _isHiding = false;
            RestoreGroup();
            transform.localScale = fromScale;
            if (_hasGroupState && _fadeGroup != null) _fadeGroup.alpha = fromAlpha;
            gameObject.SetActive(true);
            StartAnimation(fromScale, _originalScale, fromAlpha, _originalAlpha, false);
        }

        void Close()
        {
            // Repeated close clicks/ESC must not restart the 0.2-second exit.
            if (_isHiding) return;
            RememberScale();
            CancelAnimation();
            if (_fadeGroup == null)
            {
                _fadeGroup = GetComponent<CanvasGroup>();
                if (_fadeGroup == null) _fadeGroup = gameObject.AddComponent<CanvasGroup>();
            }
            if (!_hasGroupState)
            {
                _originalAlpha = _fadeGroup.alpha;
                _originalInteractable = _fadeGroup.interactable;
                _hasGroupState = true;
            }
            _isHiding = true;
            _fadeGroup.interactable = false;
            StartAnimation(transform.localScale, _originalScale * startScale, _fadeGroup.alpha, 0f, true);
        }

        void StartAnimation(Vector3 from, Vector3 to, float fromAlpha, float toAlpha, bool hideAtEnd)
        {
            if (duration <= 0f) { Complete(to, toAlpha, hideAtEnd); return; }
            _animation = StartCoroutine(Animate(from, to, fromAlpha, toAlpha, hideAtEnd));
        }

        IEnumerator Animate(Vector3 from, Vector3 to, float fromAlpha, float toAlpha, bool hideAtEnd)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = hideAtEnd ? Mathf.SmoothStep(0f, 1f, t) : 1f - Mathf.Pow(1f - t, 3f);
                transform.localScale = Vector3.LerpUnclamped(from, to, eased);
                if (_hasGroupState && _fadeGroup != null)
                    _fadeGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
            }
            Complete(to, toAlpha, hideAtEnd);
        }

        void Complete(Vector3 scale, float alpha, bool hideAtEnd)
        {
            _animation = null;
            transform.localScale = scale;
            if (_hasGroupState && _fadeGroup != null) _fadeGroup.alpha = alpha;
            if (hideAtEnd) gameObject.SetActive(false);
        }

        void RestoreGroup()
        {
            if (!_hasGroupState || _fadeGroup == null) return;
            _fadeGroup.alpha = _originalAlpha;
            _fadeGroup.interactable = _originalInteractable;
        }

        void OnDisable()
        {
            CancelAnimation();
            _isHiding = false;
            if (_hasOriginalScale) transform.localScale = _originalScale;
            RestoreGroup();
        }
    }
}
