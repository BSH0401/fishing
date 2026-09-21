using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 플레이어를 부드럽게 따라가고 맵 경계를 넘지 않는 2D 카메라.
    /// 몸이 커질수록 넓게 보이고, '카메라 장착' 스킬로 추가로 넓어진다.
    /// 화면 흔들림은 Juice에서 읽어온다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float smoothTime = 0.18f;
        [SerializeField] Vector2 offset = Vector2.zero;

        [Header("줌")]
        [Tooltip("크기 1일 때의 카메라 크기. 보통 GameDatabase.baseVision을 따른다.")]
        [SerializeField] float baseOrthoSize = 6.5f;
        [Tooltip("시야 = base × (크기 ^ 이 지수). 0.8이면 크기 45배일 때 시야 21배.")]
        [Range(0.3f, 1f)] [SerializeField] float zoomExponent = 0.8f;
        [SerializeField] float zoomSmoothTime = 0.5f;

        [Header("흔들림")]
        [Tooltip("흔들림 세기를 카메라 크기에 비례시킨다 (줌아웃돼도 같은 비율로 보이게)")]
        [SerializeField] bool scaleShakeWithZoom = true;

        Camera _cam;
        Vector3 _velocity;
        float _zoomVelocity;
        FishBody _targetBody;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = baseOrthoSize;
        }

        void Start()
        {
            if (target == null && RunManager.Instance != null && RunManager.Instance.Player != null)
                SetTarget(RunManager.Instance.Player.transform);
            Juice.Reset();
        }

        public void SetTarget(Transform t)
        {
            target = t;
            _targetBody = t != null ? t.GetComponent<FishBody>() : null;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                if (RunManager.Instance?.Player != null) SetTarget(RunManager.Instance.Player.transform);
                return;
            }

            var run = RunManager.Instance;
            float visionMult = run?.Stats != null ? run.Stats.VisionMultiplier : 1f;
            float baseSize = run?.Database != null ? run.Database.baseVision : baseOrthoSize;

            // ── 줌: 크기의 거듭제곱 — 커져도 화면상 비율이 일정하게 보인다 ──
            float size = _targetBody != null ? Mathf.Max(0.01f, _targetBody.Size) : 1f;
            float desiredSize = baseSize * Mathf.Pow(size, zoomExponent) * visionMult;

            _cam.orthographicSize = Mathf.SmoothDamp(
                _cam.orthographicSize, desiredSize, ref _zoomVelocity, zoomSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);

            // ── 위치 ──
            Vector3 desired = target.position + (Vector3)offset;
            desired.z = transform.position.z;

            if (run != null && run.Layout != null)
            {
                float halfH = _cam.orthographicSize;
                float halfW = halfH * _cam.aspect;

                // 세로: 통합 맵 전체가 범위
                float top = run.WorldTopY, bottom = run.WorldBottomY;
                desired.y = (top - bottom) <= halfH * 2f
                    ? (top + bottom) * 0.5f
                    : Mathf.Clamp(desired.y, bottom + halfH, top - halfH);

                // 가로: 벽이 기울어져 있으므로 "지금 높이의 폭"으로 잡는다.
                // 화면 위아래 끝의 폭도 함께 보고 좁은 쪽을 따라야 벽 바깥이 안 비친다.
                float hw = Mathf.Min(
                    run.WorldHalfWidthAt(desired.y),
                    Mathf.Min(run.WorldHalfWidthAt(desired.y + halfH),
                              run.WorldHalfWidthAt(desired.y - halfH)));
                float cx = run.WorldCenterXAt(desired.y);

                desired.x = hw <= halfW ? cx : Mathf.Clamp(desired.x, cx - hw + halfW, cx + hw - halfW);
            }

            // 히트스톱 중에도 카메라는 살아 있어야 흔들림이 보인다 → unscaled
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime,
                                                  Mathf.Infinity, Time.unscaledDeltaTime);

            // ── 흔들림 ──
            float shakeScale = scaleShakeWithZoom ? _cam.orthographicSize / Mathf.Max(0.01f, baseSize) : 1f;
            smoothed += (Vector3)(Juice.ShakeOffset * shakeScale);

            transform.position = smoothed;
            transform.rotation = Quaternion.Euler(0f, 0f, Juice.ShakeRoll);
        }
    }
}
