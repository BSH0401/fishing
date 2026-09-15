using FishGame.Data;

namespace FishGame.Core
{
    /// <summary>스킬 구매 가능 여부와 사유.</summary>
    public enum PurchaseResult { Success, MaxLevel, NotEnoughCurrency, PrerequisiteLocked, Invalid }

    /// <summary>
    /// 스킬트리 구매 로직. UI와 진행도 사이의 유일한 통로.
    ///
    /// 코스트는 노드별이 아니라 "지금까지 찍은 총 노드 수"로 결정된다.
    /// 그래서 어떤 순서로 찍든 N번째 노드의 가격은 같고,
    /// 플레이어는 '무엇을 먼저 찍을까'만 고민하면 된다.
    /// </summary>
    public static class SkillTreeManager
    {
        public static bool ArePrerequisitesMet(SkillNode node, PlayerProgress progress)
        {
            if (node == null) return false;
            foreach (var req in node.prerequisites)
            {
                if (req?.node == null) continue;
                if (progress.GetSkillLevel(req.node.id) < req.level) return false;
            }
            return true;
        }

        public static PurchaseResult CanPurchase(SkillNode node, PlayerProgress progress, out double cost)
        {
            cost = 0d;
            if (node == null || progress == null) return PurchaseResult.Invalid;

            int level = progress.GetSkillLevel(node.id);
            if (level >= node.EffectiveMaxLevel) return PurchaseResult.MaxLevel;
            if (!ArePrerequisitesMet(node, progress)) return PurchaseResult.PrerequisiteLocked;

            cost = node.GetCost(progress.totalNodesPurchased);
            if (progress.currency < cost) return PurchaseResult.NotEnoughCurrency;

            return PurchaseResult.Success;
        }

        /// <summary>다음 노드 하나의 가격 (어떤 노드든 동일, costMultiplier 제외).</summary>
        public static double NextNodeBaseCost(PlayerProgress progress)
            => SkillCostCurve.CostOfNode(progress.totalNodesPurchased + 1);

        /// <summary>실제 구매. 성공하면 재화를 차감하고 레벨을 올린 뒤 저장까지 한다.</summary>
        public static PurchaseResult Purchase(SkillNode node, GameManager game)
        {
            if (game == null || node == null) return PurchaseResult.Invalid;

            var result = CanPurchase(node, game.Progress, out double cost);
            if (result != PurchaseResult.Success) return result;

            game.Progress.currency -= cost;
            game.Progress.SetSkillLevel(node.id, game.Progress.GetSkillLevel(node.id) + 1);
            game.Progress.totalNodesPurchased++;
            game.NotifyProgressChanged();
            return PurchaseResult.Success;
        }

        public static string DescribeResult(PurchaseResult r) => r switch
        {
            PurchaseResult.Success            => "구매 완료",
            PurchaseResult.MaxLevel           => "최대 레벨",
            PurchaseResult.NotEnoughCurrency  => "재화 부족",
            PurchaseResult.PrerequisiteLocked => "선행 스킬 필요",
            _                                 => "구매 불가",
        };
    }
}
