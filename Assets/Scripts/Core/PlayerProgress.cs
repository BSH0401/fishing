using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>JsonUtility가 Dictionary를 직렬화하지 못하므로 리스트로 저장한다.</summary>
    [Serializable]
    public class SkillLevelEntry
    {
        public string id;
        public int level;
    }

    /// <summary>물고기 도감 — 종별 누적 포식 수.</summary>
    [Serializable]
    public class CodexEntry
    {
        public string speciesId;
        public int eaten;
    }

    /// <summary>
    /// 메인 화면에 남는 영구 진행도. JSON으로 직렬화된다.
    /// </summary>
    [Serializable]
    public class PlayerProgress
    {
        /// <summary>
        /// 3 = 스킬트리가 도면형(칸 231개)으로 바뀐 판. 2 이하 세이브는 스킬을 환불하고 초기화한다.
        /// </summary>
        public const int CurrentVersion = 3;

        public int version = CurrentVersion;

        /// <summary>파일에서 읽었을 때의 버전 (OnAfterLoad가 version을 올리기 전 값). 마이그레이션 판단용.</summary>
        [NonSerialized] public int loadedVersion = CurrentVersion;

        public double currency = 0d;

        /// <summary>
        /// 지금까지 트리 전체에서 찍은 노드 수.
        /// 다음 노드의 가격이 이 값으로 결정된다 (전역 코스트 곡선).
        /// </summary>
        public int totalNodesPurchased = 0;

        /// <summary>
        /// 지금까지 도달해 본 가장 깊은 존. 메인 화면의 '시작 구역 선택' 범위가 된다.
        /// (세이브 호환을 위해 필드 이름은 예전 그대로 둔다)
        /// </summary>
        public int highestUnlockedMap = 0;
        public int lastSelectedMap = 0;
        public List<int> clearedMaps = new List<int>();
        /// <summary>
        /// 강화 종류별 레벨 = 그 종류의 칸을 찍은 수. purchasedSlots에서 다시 계산되는 파생값이다
        /// (GameManager가 로드 직후 SkillTreeManager.SyncLevels로 맞춘다).
        /// </summary>
        public List<SkillLevelEntry> skillLevels = new List<SkillLevelEntry>();

        /// <summary>찍은 트리 칸의 id. 스킬 진행의 원본 데이터.</summary>
        public List<string> purchasedSlots = new List<string>();

        /// <summary>지금까지 스킬에 쓴 재화 합계 (통계 · 나중에 트리를 또 바꿀 때 정확히 환불하려고).</summary>
        public double totalSkillSpent = 0d;

        [NonSerialized] HashSet<string> _slotCache;
        public List<CodexEntry> codex = new List<CodexEntry>();

        /// <summary>먹은 히든 아이템의 ID. 한 번 먹으면 다시 생성되지 않는다.</summary>
        public List<string> hiddenItems = new List<string>();

        [NonSerialized] HashSet<string> _hiddenCache;

        // ── 통계 ────────────────────────────────────────────────
        public int totalRuns = 0;
        public int totalFishEaten = 0;
        public double totalCurrencyEarned = 0d;
        public float totalPlaySeconds = 0f;
        public float bestSurvivalSeconds = 0f;
        public double bestRunCurrency = 0d;

        [NonSerialized] Dictionary<string, int> _skillCache;
        [NonSerialized] Dictionary<string, int> _codexCache;

        // ── 스킬 ────────────────────────────────────────────────
        void BuildSkillCache()
        {
            _skillCache = new Dictionary<string, int>(skillLevels.Count);
            foreach (var e in skillLevels)
                if (!string.IsNullOrEmpty(e.id)) _skillCache[e.id] = e.level;
        }

        public int GetSkillLevel(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            if (_skillCache == null) BuildSkillCache();
            return _skillCache.TryGetValue(id, out int lv) ? lv : 0;
        }

        public void SetSkillLevel(string id, int level)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_skillCache == null) BuildSkillCache();
            _skillCache[id] = level;

            for (int i = 0; i < skillLevels.Count; i++)
            {
                if (skillLevels[i].id == id) { skillLevels[i].level = level; return; }
            }
            skillLevels.Add(new SkillLevelEntry { id = id, level = level });
        }

        // ── 트리 칸 ─────────────────────────────────────────────
        HashSet<string> SlotSet
        {
            get
            {
                if (_slotCache == null)
                {
                    _slotCache = new HashSet<string>();
                    foreach (var id in purchasedSlots)
                        if (!string.IsNullOrEmpty(id)) _slotCache.Add(id);
                }
                return _slotCache;
            }
        }

        public bool HasSlot(string id) => !string.IsNullOrEmpty(id) && SlotSet.Contains(id);

        /// <summary>새로 찍었으면 true.</summary>
        public bool AddSlot(string id)
        {
            if (string.IsNullOrEmpty(id) || !SlotSet.Add(id)) return false;
            purchasedSlots.Add(id);
            return true;
        }

        /// <summary>스킬 진행을 전부 지운다 (재화는 그대로).</summary>
        public void ClearSkills()
        {
            purchasedSlots.Clear();
            skillLevels.Clear();
            totalNodesPurchased = 0;
            _slotCache = null;
            _skillCache = null;
        }

        // ── 도감 ────────────────────────────────────────────────
        void BuildCodexCache()
        {
            _codexCache = new Dictionary<string, int>(codex.Count);
            foreach (var e in codex)
                if (!string.IsNullOrEmpty(e.speciesId)) _codexCache[e.speciesId] = e.eaten;
        }

        public int GetCodexCount(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return 0;
            if (_codexCache == null) BuildCodexCache();
            return _codexCache.TryGetValue(speciesId, out int c) ? c : 0;
        }

        public void AddCodexCount(string speciesId, int amount = 1)
        {
            if (string.IsNullOrEmpty(speciesId) || amount <= 0) return;
            if (_codexCache == null) BuildCodexCache();

            int next = GetCodexCount(speciesId) + amount;
            _codexCache[speciesId] = next;

            for (int i = 0; i < codex.Count; i++)
            {
                if (codex[i].speciesId == speciesId) { codex[i].eaten = next; return; }
            }
            codex.Add(new CodexEntry { speciesId = speciesId, eaten = next });
        }

        // ── 존 ──────────────────────────────────────────────────
        /// <summary>도달해 본 가장 깊은 존의 인덱스.</summary>
        public int DeepestZoneReached
        {
            get => highestUnlockedMap;
            set => highestUnlockedMap = Mathf.Max(highestUnlockedMap, value);
        }

        /// <summary>메인 화면에서 고른 시작 구역.</summary>
        public int SelectedStartZone
        {
            get => Mathf.Clamp(lastSelectedMap, 0, highestUnlockedMap);
            set => lastSelectedMap = Mathf.Max(0, value);
        }

        public bool IsZoneCleared(int zoneIndex) => clearedMaps.Contains(zoneIndex);

        public void MarkZoneCleared(int zoneIndex)
        {
            if (!clearedMaps.Contains(zoneIndex)) clearedMaps.Add(zoneIndex);
        }

        // ── 히든 아이템 ─────────────────────────────────────────
        void BuildHiddenCache()
        {
            _hiddenCache = new HashSet<string>();
            foreach (var id in hiddenItems)
                if (!string.IsNullOrEmpty(id)) _hiddenCache.Add(id);
        }

        public HashSet<string> HiddenItemSet
        {
            get
            {
                if (_hiddenCache == null) BuildHiddenCache();
                return _hiddenCache;
            }
        }

        public bool HasHiddenItem(string id) =>
            !string.IsNullOrEmpty(id) && HiddenItemSet.Contains(id);

        /// <summary>새로 먹었으면 true. 이미 갖고 있었으면 false.</summary>
        public bool AddHiddenItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!HiddenItemSet.Add(id)) return false;
            hiddenItems.Add(id);
            return true;
        }

        public int HiddenItemCount => HiddenItemSet.Count;

        /// <summary>역직렬화 직후 호출. 캐시 무효화 + 값 보정.</summary>
        public void OnAfterLoad()
        {
            loadedVersion = version;
            _skillCache = null;
            _slotCache = null;
            _codexCache = null;
            _hiddenCache = null;

            if (currency < 0d || double.IsNaN(currency)) currency = 0d;
            if (highestUnlockedMap < 0) highestUnlockedMap = 0;
            if (clearedMaps == null) clearedMaps = new List<int>();
            if (skillLevels == null) skillLevels = new List<SkillLevelEntry>();
            if (codex == null) codex = new List<CodexEntry>();
            if (hiddenItems == null) hiddenItems = new List<string>();
            if (purchasedSlots == null) purchasedSlots = new List<string>();
            if (double.IsNaN(totalSkillSpent) || totalSkillSpent < 0d) totalSkillSpent = 0d;

            // v1 세이브에는 totalNodesPurchased가 없다. 스킬 레벨 합으로 복원한다.
            if (totalNodesPurchased <= 0 && skillLevels.Count > 0)
            {
                int sum = 0;
                foreach (var e in skillLevels) sum += Mathf.Max(0, e.level);
                totalNodesPurchased = sum;
            }
            if (totalNodesPurchased < 0) totalNodesPurchased = 0;

            version = CurrentVersion;
        }
    }
}
