using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 플레이어를 화면 한가운데에 두는 2D 카메라.
    /// R12: 예전엔 벽 바깥을 안 비추려고 맵 경계에서 멈췄는데, 벽 근처에서 플레이어가 화면 한쪽으로 쏠렸다 풀렸다 하며
    /// 카메라 워크가 일관성이 없다는 QA가 있어 "항상 플레이어 중심"으로 바꿨다.
    /// 몸이 커질수록 넓게 보이고, '카메라 장착' 스킬로 추가로 넓어진다.
    /// 화면 흔들림은 Juice에서 읽어온다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] Transform target;
        [Tooltip("플레이어를 따라가는 부드러움. 작을수록 딱 붙는다 (0 = 즉시)")]
        [SerializeField] float smoothTime = 0.06f;
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
        // 흔들림을 뺀 카메라 위치. 흔들림이 섞인 transform.position에서 다음 프레임 스무딩을 시작하면
        // 흔들림이 매 프레임 누적돼 프레임이 높을수록 크게 흔들렸다 (QA: 고프레임에서 흔들림 과함)
        Vector3 _basePos;
        bool _hasBase;
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
            _basePos = transform.position;
            _hasBase = true;
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

            if (!_hasBase) { _basePos = transform.position; _hasBase = true; }

            // 히트스톱 중에도 카메라는 살아 있어야 흔들림이 보인다 → unscaled.
            // 스무딩은 흔들림을 뺀 위치(_basePos)에서만 한다 — 흔들림은 매 프레임 위에 얹기만 한다.
            Vector3 smoothed = Vector3.SmoothDamp(_basePos, desired, ref _velocity, smoothTime,
                                                  Mathf.Infinity, Time.unscaledDeltaTime);

            // 부스터처럼 순간적으로 빠를 때 스무딩이 따라가지 못해 플레이어가 화면 밖으로 나가지 않게
            smoothed = KeepTargetInView(smoothed, _cam.orthographicSize, 0.15f);
            _basePos = smoothed;

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

        /// <summary>항상 플레이어 중심. 맵 끝에서도 멈추지 않는다 (벽 바깥은 어두운 바깥 색으로 보인다).</summary>
        Vector3 DesiredPosition(float halfH)
        {
            Vector3 desired = target.position + (Vector3)offset;
            desired.z = transform.position.z;
            return desired;
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
