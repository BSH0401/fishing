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
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        public double currency = 0d;

        /// <summary>
        /// 지금까지 트리 전체에서 찍은 노드 수.
        /// 다음 노드의 가격이 이 값으로 결정된다 (전역 코스트 곡선).
        /// </summary>
        public int totalNodesPurchased = 0;

        public int highestUnlockedMap = 0;
        public int lastSelectedMap = 0;
        public List<int> clearedMaps = new List<int>();
        public List<SkillLevelEntry> skillLevels = new List<SkillLevelEntry>();
        public List<CodexEntry> codex = new List<CodexEntry>();

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

        // ── 맵 ──────────────────────────────────────────────────
        public bool IsMapCleared(int mapIndex) => clearedMaps.Contains(mapIndex);

        public void MarkMapCleared(int mapIndex)
        {
            if (!clearedMaps.Contains(mapIndex)) clearedMaps.Add(mapIndex);
            highestUnlockedMap = Mathf.Max(highestUnlockedMap, mapIndex + 1);
        }

        /// <summary>역직렬화 직후 호출. 캐시 무효화 + 값 보정.</summary>
        public void OnAfterLoad()
        {
            _skillCache = null;
            _codexCache = null;

            if (currency < 0d || double.IsNaN(currency)) currency = 0d;
            if (highestUnlockedMap < 0) highestUnlockedMap = 0;
            if (clearedMaps == null) clearedMaps = new List<int>();
            if (skillLevels == null) skillLevels = new List<SkillLevelEntry>();
            if (codex == null) codex = new List<CodexEntry>();

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
