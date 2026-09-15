using UnityEngine;
using UnityEngine.EventSystems;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리 스크롤 영역 위에서 마우스 휠을 줌으로 바꾼다.
    ///
    /// ScrollRect가 있는 GameObject에 같이 붙인다. 휠 이벤트는 자식(노드 버튼)에서
    /// 위로 버블링되므로, 노드 위에 커서를 올린 채 굴려도 줌이 먹는다.
    /// ScrollRect의 scrollSensitivity는 0으로 두어 세로 스크롤과 겹치지 않게 한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SkillTreeZoomArea : MonoBehaviour, IScrollHandler
    {
        [SerializeField] SkillTreeUI tree;
        [Tooltip("휠 한 칸당 배율. 1.12 = 12%씩")]
        [Min(1.01f)] [SerializeField] float zoomStep = 1.12f;
        [Tooltip("체크하면 커서 위치를 기준으로 확대/축소한다 (지도 UI처럼)")]
        [SerializeField] bool zoomTowardCursor = true;

        Canvas _canvas;

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (tree == null) tree = GetComponentInParent<SkillTreeUI>();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (tree == null) return;

            float delta = eventData.scrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return;

            float factor = delta > 0f ? zoomStep : 1f / zoomStep;

            if (zoomTowardCursor)
            {
                Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? _canvas.worldCamera
                    : null;
                tree.ZoomBy(factor, eventData.position, cam);
            }
            else
            {
                tree.ZoomBy(factor);
            }
        }
    }
}
