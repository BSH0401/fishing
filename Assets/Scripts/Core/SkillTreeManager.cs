using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>칸 구매 가능 여부와 사유.</summary>
    public enum PurchaseResult { Success, MaxLevel, NotEnoughCurrency, PrerequisiteLocked, Invalid }

    /// <summary>
    /// 스킬트리 구매 로직. UI와 진행도 사이의 유일한 통로.
    ///
    /// 트리는 칸(SkillTreeSlot)들의 그래프다.
    ///   · 칸 하나는 한 번만 찍는다 (이미 찍었으면 MaxLevel).
    ///   · 선으로 이어진 이웃 칸 중 하나라도 찍혀 있어야 열린다 (아니면 PrerequisiteLocked).
    ///     시작 칸은 처음부터 찍힌 것으로 본다.
    ///   · 가격은 "지금까지 찍은 총 칸 수"로 정해지고, 종류별 costMultiplier만 다르다.
    /// </summary>
    public static class SkillTreeManager
    {
        // ── 상태 조회 ───────────────────────────────────────────
        public static bool IsPurchased(GameDatabase db, int index, PlayerProgress progress)
        {
            var slot = db?.GetSlot(index);
            if (slot == null || progress == null) return false;
            return slot.IsRoot || progress.HasSlot(slot.id);
        }

        /// <summary>이웃 중 찍힌 칸이 있어 열려 있는가. (이미 찍은 칸도 true)</summary>
        public static bool IsReachable(GameDatabase db, int index, PlayerProgress progress)
        {
            var slot = db?.GetSlot(index);
            if (slot == null) return false;
            if (IsPurchased(db, index, progress)) return true;
            foreach (int n in slot.links)
                if (IsPurchased(db, n, progress)) return true;
            return false;
        }

        public static PurchaseResult CanPurchase(GameDatabase db, int index, PlayerProgress progress, out double cost)
        {
            cost = 0d;
            var slot = db?.GetSlot(index);
            if (slot == null || progress == null || slot.skill == null) return PurchaseResult.Invalid;

            if (IsPurchased(db, index, progress)) return PurchaseResult.MaxLevel;
            if (!IsReachable(db, index, progress)) return PurchaseResult.PrerequisiteLocked;

            cost = slot.skill.GetCost(progress.totalNodesPurchased);
            if (progress.currency < cost) return PurchaseResult.NotEnoughCurrency;
            return PurchaseResult.Success;
        }

        /// <summary>다음 칸 하나의 기본 가격 (costMultiplier 제외).</summary>
        public static double NextNodeBaseCost(PlayerProgress progress)
            => SkillCostCurve.CostOfNode(progress.totalNodesPurchased + 1);

        /// <summary>실제 구매. 성공하면 재화를 차감하고 칸을 찍은 뒤 저장까지 한다.</summary>
        public static PurchaseResult Purchase(int index, GameManager game)
        {
            if (game == null || game.Database == null) return PurchaseResult.Invalid;
            var db = game.Database;
            var progress = game.Progress;

            var result = CanPurchase(db, index, progress, out double cost);
            if (result != PurchaseResult.Success) return result;

            var slot = db.GetSlot(index);
            progress.currency -= cost;
            progress.totalSkillSpent += cost;
            progress.AddSlot(slot.id);
            progress.SetSkillLevel(slot.skill.id, progress.GetSkillLevel(slot.skill.id) + 1);
            progress.totalNodesPurchased++;
            game.NotifyProgressChanged();
            return PurchaseResult.Success;
        }

        /// <summary>
        /// 찍은 칸 목록에서 종류별 레벨과 총 칸 수를 다시 계산한다.
        /// 칸 목록이 원본이고 레벨은 파생값이다 — 도면이 바뀌어 없어진 칸은 여기서 빠진다.
        /// </summary>
        public static void SyncLevels(GameDatabase db, PlayerProgress progress)
        {
            if (db == null || progress == null) return;
            // 트리가 아직 생성되지 않은 DB로 돌리면 찍은 칸이 전부 "도면에 없음"이 되어 세이브가 날아간다
            if (db.skillTree == null || db.skillTree.Count == 0) return;

            var counts = new Dictionary<string, int>();
            var valid = new List<string>();
            foreach (var id in progress.purchasedSlots)
            {
                var slot = db.GetSlot(db.SlotIndex(id));
                if (slot == null || slot.IsRoot || slot.skill == null || valid.Contains(id)) continue;
                valid.Add(id);
                counts.TryGetValue(slot.skill.id, out int c);
                counts[slot.skill.id] = c + 1;
            }

            if (valid.Count != progress.purchasedSlots.Count)
                Debug.LogWarning($"[SkillTree] 도면에 없는 칸 {progress.purchasedSlots.Count - valid.Count}개를 세이브에서 뺐습니다.");

            progress.ClearSkills();
            foreach (var id in valid) progress.AddSlot(id);
            foreach (var kv in counts) progress.SetSkillLevel(kv.Key, kv.Value);
            progress.totalNodesPurchased = valid.Count;
        }

        /// <summary>
        /// 도면형 트리 이전(세이브 v2 이하)의 스킬을 환불하고 비운다.
        /// 옛 트리는 칸이 아니라 "레벨"로 저장돼 있어 새 칸에 옮겨 담을 방법이 없다.
        /// 환불액 = 옛 곡선으로 그만큼 찍었을 때의 누적 가격 × 옛 트리의 평균 배율(1.5).
        /// 돌려준 재화가 있으면 그 액수를, 할 일이 없었으면 0을 돌려준다.
        /// </summary>
        public static double MigrateLegacySave(PlayerProgress progress)
        {
            if (progress == null || progress.purchasedSlots.Count > 0) return 0d;
            // 버전만 보고 거르지 않는다: 트리가 없는 DB로 실행된 동안 저장되면 SaveSystem이 version을 3으로
            // 올려 버려, 옛 레벨이 남은 채 v3로 읽힌다. 칸 목록이 비어 있는데 레벨/칸 수가 남아 있으면
            // (정상적인 v3에서는 SyncLevels가 항상 비워 두므로) 옛 트리의 흔적이다.

            int oldNodes = progress.totalNodesPurchased;
            if (oldNodes <= 0)
                foreach (var e in progress.skillLevels) oldNodes += Mathf.Max(0, e.level);
            if (oldNodes <= 0) { progress.ClearSkills(); return 0d; }

            const double AverageLegacyMultiplier = 1.5d;
            double refund = System.Math.Ceiling(SkillCostCurve.LegacyCumulativeCost(oldNodes) * AverageLegacyMultiplier);

            progress.ClearSkills();
            progress.currency += refund;
            Debug.Log($"[SkillTree] 스킬트리가 새 도면으로 바뀌어 예전 스킬 {oldNodes}레벨을 초기화하고 " +
                      $"재화 {refund:N0}을 돌려줬습니다.");
            return refund;
        }

        public static string DescribeResult(PurchaseResult r) => r switch
        {
            PurchaseResult.Success            => "구매 완료",
            PurchaseResult.MaxLevel           => "이미 찍음",
            PurchaseResult.NotEnoughCurrency  => "재화 부족",
            PurchaseResult.PrerequisiteLocked => "이어진 칸을 먼저 찍어야 함",
            _                                 => "구매 불가",
        };
    }
}
