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
        [Tooltip("화면 가로 절반이 현재 구역 반폭의 몇 배까지 커질 수 있는지. 1.1 = 벽 바깥이 살짝 보이는 정도")]
        [Range(0.5f, 2f)] [SerializeField] float maxZoneWidthInView = 1.1f;
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

        /// <summary>
        /// 줌과 위치를 즉시 목표값으로 맞춘다. 판 시작 직후 첫 스폰이
        /// "아직 줌아웃이 안 된 좁은 화면" 기준으로 일어나지 않게 RunManager가 부른다.
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null && RunManager.Instance?.Player != null) SetTarget(RunManager.Instance.Player.transform);
            if (target == null) return;
            _cam.orthographicSize = DesiredOrthoSize();
            transform.position = DesiredPosition(_cam.orthographicSize);
            _velocity = Vector3.zero;
            _zoomVelocity = 0f;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                if (RunManager.Instance?.Player != null) SetTarget(RunManager.Instance.Player.transform);
                return;
            }

            _cam.orthographicSize = Mathf.SmoothDamp(
                _cam.orthographicSize, DesiredOrthoSize(), ref _zoomVelocity, zoomSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);

            Vector3 desired = DesiredPosition(_cam.orthographicSize);

            // 히트스톱 중에도 카메라는 살아 있어야 흔들림이 보인다 → unscaled
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime,
                                                  Mathf.Infinity, Time.unscaledDeltaTime);

            // 부스터처럼 순간적으로 빠를 때 스무딩이 따라가지 못해 플레이어가 화면 밖으로 나가지 않게
            smoothed = KeepTargetInView(smoothed, _cam.orthographicSize, 0.8f);

            // ── 흔들림 ──
            var run = RunManager.Instance;
            float baseSize = run?.Database != null ? run.Database.baseVision : baseOrthoSize;
            float shakeScale = scaleShakeWithZoom ? _cam.orthographicSize / Mathf.Max(0.01f, baseSize) : 1f;
            smoothed += (Vector3)(Juice.ShakeOffset * shakeScale);

            transform.position = smoothed;
            transform.rotation = Quaternion.Euler(0f, 0f, Juice.ShakeRoll);
        }

        float DesiredOrthoSize()
        {
            var run = RunManager.Instance;
            float visionMult = run?.Stats != null ? run.Stats.VisionMultiplier : 1f;
            float baseSize = run?.Database != null ? run.Database.baseVision : baseOrthoSize;

            // 줌: 크기의 거듭제곱 — 커져도 화면상 비율이 일정하게 보인다
            float size = _targetBody != null ? Mathf.Max(0.01f, _targetBody.Size) : 1f;
            float desiredSize = baseSize * Mathf.Pow(size, zoomExponent) * visionMult;

            // 화면이 지금 구역보다 넓어지지 않게. 크기·시야를 다 올리면 바다 폭의
            // 몇 배까지 보이게 돼서 벽 바깥 허공이 화면 대부분을 차지했다.
            if (run != null && run.CurrentZone != null && _cam.aspect > 0.01f)
            {
                float zoneHalfW = run.CurrentZone.MaxHalfWidth;
                float maxOrtho = zoneHalfW * maxZoneWidthInView / _cam.aspect;
                // 몸이 화면을 다 가리는 것도 막는다
                float minOrtho = size * 1.5f;
                desiredSize = Mathf.Clamp(desiredSize, Mathf.Min(minOrtho, maxOrtho), Mathf.Max(minOrtho, maxOrtho));
            }
            return desiredSize;
        }

        Vector3 DesiredPosition(float halfH)
        {
            var run = RunManager.Instance;
            Vector3 desired = target.position + (Vector3)offset;
            desired.z = transform.position.z;

            if (run != null && run.Layout != null)
            {
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

            // 벽 바깥을 안 비추는 것보다 플레이어가 화면에 있는 것이 우선이다.
            // (통로 입구처럼 폭이 급히 좁아지는 곳에서 위 계산이 플레이어를 화면 밖으로 밀어냈다)
            return KeepTargetInView(desired, halfH, 0.75f);
        }

        /// <summary>대상이 화면 가장자리 비율(margin01) 안쪽에 오도록 카메라 위치를 보정한다.</summary>
        Vector3 KeepTargetInView(Vector3 camPos, float halfH, float margin01)
        {
            float halfW = halfH * _cam.aspect;
            float bodyR = _targetBody != null ? _targetBody.Size * 0.5f : 0.5f;
            float limX = Mathf.Max(0f, halfW * margin01 - bodyR);
            float limY = Mathf.Max(0f, halfH * margin01 - bodyR);
            Vector3 t = target.position;
            camPos.x = Mathf.Clamp(camPos.x, t.x - limX, t.x + limX);
            camPos.y = Mathf.Clamp(camPos.y, t.y - limY, t.y + limY);
            return camPos;
        }
    }
}
