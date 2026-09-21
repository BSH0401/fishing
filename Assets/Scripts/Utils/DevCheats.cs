#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FishGame.Core;
using UnityEngine;

namespace FishGame.Utils
{
    /// <summary>
    /// 진행도 치트의 실제 동작. UI(DevConsole)와 분리해 둬서
    /// 나중에 에디터 메뉴나 테스트 코드에서도 그대로 불러 쓸 수 있다.
    ///
    /// 모든 함수는 끝에 NotifyProgressChanged를 불러 스탯 재계산 · 스킬트리 갱신 · 저장까지 한 번에 한다.
    /// </summary>
    public static class DevCheats
    {
        static GameManager GM => GameManager.Instance;
        static PlayerProgress P => GM != null ? GM.Progress : null;

        static bool Ready() => GM != null && P != null && GM.Database != null;

        static void Commit() => GM.NotifyProgressChanged();

        // ── 재화 ────────────────────────────────────────────────
        public static void AddCurrency(double amount)
        {
            if (!Ready()) return;
            P.currency += amount;
            Commit();
        }

        public static void MultiplyCurrency(double factor)
        {
            if (!Ready()) return;
            P.currency = System.Math.Max(1d, P.currency) * factor;
            Commit();
        }

        // ── 스킬 ────────────────────────────────────────────────
        public static string MaxAllSkills()
        {
            if (!Ready()) return null;

            int total = 0;
            foreach (var node in GM.Database.skills)
            {
                if (node == null) continue;
                P.SetSkillLevel(node.id, node.maxLevel);
                total += node.maxLevel;
            }
            // 코스트 곡선이 "지금까지 찍은 총 노드 수"를 보므로 같이 맞춰야
            // 이후에 뭔가 더 찍을 때 가격이 엉뚱하게 나오지 않는다.
            P.totalNodesPurchased = total;
            Commit();
            return $"스킬 전부 최대 — 노드 {total}개";
        }

        public static string ResetSkills()
        {
            if (!Ready()) return null;
            P.skillLevels.Clear();
            P.totalNodesPurchased = 0;
            P.OnAfterLoad();          // 캐시 무효화
            Commit();
            return "스킬을 초기화했습니다 (재화는 그대로)";
        }

        // ── 구역 ────────────────────────────────────────────────
        public static string UnlockAllZones()
        {
            if (!Ready()) return null;
            P.DeepestZoneReached = GM.Database.DeepestZoneIndex;
            Commit();
            return $"구역 {GM.Database.ZoneCount}개 전부 해금 — 시작 구역을 고를 수 있습니다";
        }

        public static string LockZones()
        {
            if (!Ready()) return null;
            // DeepestZoneReached는 줄어들지 않게 막혀 있어서 필드를 직접 되돌린다
            P.highestUnlockedMap = 0;
            P.lastSelectedMap = 0;
            GM.SelectStartZone(0);
            Commit();
            return "구역을 어항만 남기고 잠갔습니다";
        }

        // ── 히든 아이템 ─────────────────────────────────────────
        public static string CollectAllHidden()
        {
            if (!Ready()) return null;

            int added = 0;
            foreach (var z in GM.Database.zones)
            {
                if (z == null) continue;
                // WorldBuilder.BuildHiddenItems와 같은 규칙의 ID
                for (int k = 0; k < z.hiddenItemCount; k++)
                    if (P.AddHiddenItem($"{z.name}#{k}")) added++;
            }
            Commit();
            return $"히든 아이템 {added}개 획득 — 총 {P.HiddenItemCount}개 (재화 +{P.HiddenItemCount * 5}%)";
        }

        public static string ResetHidden()
        {
            if (!Ready()) return null;
            P.hiddenItems.Clear();
            P.OnAfterLoad();
            Commit();
            return "히든 아이템을 초기화했습니다 — 다음 판부터 다시 나옵니다";
        }

        // ── 도감 ────────────────────────────────────────────────
        public static string FillCodex()
        {
            if (!Ready()) return null;

            var db = GM.Database;
            int target = Mathf.Max(1, db.codexMilestone) * Mathf.Max(1, db.codexMaxTiers);
            int species = 0;

            foreach (var f in db.allFish)
            {
                if (f == null) continue;
                int have = P.GetCodexCount(f.CodexKey);
                if (have < target) P.AddCodexCount(f.CodexKey, target - have);
                species++;
            }
            Commit();
            return $"도감 {species}종을 최대 단계({db.codexMaxTiers})까지 채웠습니다";
        }

        public static string ResetCodex()
        {
            if (!Ready()) return null;
            P.codex.Clear();
            P.OnAfterLoad();
            Commit();
            return "도감을 초기화했습니다";
        }
    }
}
#endif
