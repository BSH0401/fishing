using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 물고기 도감. 메인 화면의 패널로 열린다.
    ///
    /// 종을 codexMilestone(100)마리 먹을 때마다 그 종의 고유 효과가 한 단계씩 붙는다.
    /// 행 하나하나가 "이 물고기를 더 먹을 이유"다.
    /// </summary>
    public class CodexUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] GameObject root;
        [SerializeField] Button openButton;
        [SerializeField] Button closeButton;

        [Header("목록")]
        [SerializeField] RectTransform listContainer;
        [SerializeField] CodexRow rowPrefab;
        [SerializeField] TMP_Text summaryText;

        readonly List<CodexRow> _rows = new List<CodexRow>();
        GameManager _game;
        bool _built;

        void Awake()
        {
            // 메인 화면의 다른 창과 같은 수중 실험실 스타일
            if (root != null) LabStyle.Panel(root.GetComponent<Image>(), fill: new Color(0.03f, 0.10f, 0.13f, 0.97f));
            LabStyle.Button(openButton);
            LabStyle.Button(closeButton, primary: true);
            if (root != null) root.SetActive(false);
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        void Start()
        {
            _game = GameManager.Instance;
            if (_game != null) _game.OnProgressChanged += RefreshIfOpen;
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= RefreshIfOpen;
        }

        public void Open()
        {
            if (_game == null || _game.Database == null) return;
            if (!_built) Build();
            if (root != null) root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        void RefreshIfOpen()
        {
            if (root != null && root.activeSelf) Refresh();
        }

        // ── 생성 ────────────────────────────────────────────────
        void Build()
        {
            if (listContainer == null || rowPrefab == null) return;

            foreach (Transform c in listContainer) Destroy(c.gameObject);
            _rows.Clear();

            // 예전에 생성한 씬은 목록이 보기 영역보다 600 넓고 줄 폭을 맞추지 않아, 줄 왼쪽(아이콘·이름)이 잘려 보였다
            listContainer.anchorMin = new Vector2(0f, 1f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.pivot = new Vector2(0.5f, 1f);
            listContainer.sizeDelta = new Vector2(0f, listContainer.sizeDelta.y);
            listContainer.anchoredPosition = Vector2.zero;
            var layout = listContainer.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            foreach (var fish in _game.Database.allFish)
            {
                if (fish == null) continue;
                var row = Instantiate(rowPrefab, listContainer);
                row.Bind(fish);
                _rows.Add(row);
            }
            _built = true;
        }

        void Refresh()
        {
            var db = _game.Database;
            var progress = _game.Progress;

            int discovered = 0, total = 0;
            foreach (var row in _rows)
            {
                row.Refresh(db, progress);
                total++;
                if (progress.GetCodexCount(row.Species.CodexKey) > 0) discovered++;
            }

            if (summaryText != null)
            {
                int tiers = 0;
                foreach (var row in _rows)
                    tiers += PlayerStats.CodexTier(row.Species, db, progress);

                summaryText.text =
                    $"발견 {discovered}/{total}종   ·   획득한 도감 효과 {tiers}단계\n" +
                    $"<size=80%>한 종을 {db.codexMilestone}마리 먹을 때마다 그 종의 효과가 한 단계씩 붙습니다</size>";
            }
        }
    }
}
