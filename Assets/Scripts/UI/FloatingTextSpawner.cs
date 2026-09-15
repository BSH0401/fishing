using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FishGame.UI
{
    /// <summary>포식할 때 "+3 +1.5s" 같은 텍스트를 월드 위치에 띄운다.</summary>
    public class FloatingTextSpawner : MonoBehaviour
    {
        [SerializeField] TMP_Text prefab;
        [SerializeField] RectTransform canvasRoot;
        [SerializeField] Camera worldCamera;
        [SerializeField] int poolSize = 16;
        [SerializeField] float lifetime = 0.9f;
        [SerializeField] float riseSpeed = 55f;

        readonly Queue<TMP_Text> _pool = new Queue<TMP_Text>();
        readonly List<(TMP_Text text, float born, Vector3 worldPos)> _active = new();

        void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (canvasRoot == null) canvasRoot = transform as RectTransform;
            if (prefab == null) { enabled = false; return; }

            for (int i = 0; i < poolSize; i++)
            {
                var t = Instantiate(prefab, canvasRoot);
                t.gameObject.SetActive(false);
                _pool.Enqueue(t);
            }
        }

        public void Spawn(Vector3 worldPos, string message)
        {
            if (!enabled || _pool.Count == 0) return;

            var t = _pool.Dequeue();
            t.text = message;
            t.alpha = 1f;
            t.gameObject.SetActive(true);
            _active.Add((t, Time.time, worldPos));
        }

        void LateUpdate()
        {
            if (worldCamera == null) worldCamera = Camera.main;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var (text, born, worldPos) = _active[i];
                float age = Time.time - born;

                if (age >= lifetime)
                {
                    text.gameObject.SetActive(false);
                    _pool.Enqueue(text);
                    _active.RemoveAt(i);
                    continue;
                }

                Vector3 screen = worldCamera != null
                    ? worldCamera.WorldToScreenPoint(worldPos)
                    : Vector3.zero;
                screen.y += age * riseSpeed;
                text.rectTransform.position = screen;
                text.alpha = 1f - (age / lifetime);
            }
        }
    }
}
