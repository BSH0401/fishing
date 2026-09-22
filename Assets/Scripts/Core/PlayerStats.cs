using System;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>
    /// 스킬트리 레벨을 모두 합산해서 나온 "이번 판에 쓸 최종 스탯".
    /// 판이 시작될 때 한 번 계산해서 PlayerFish가 읽어간다.
    /// </summary>
    public class PlayerStats
    {
        // ── 기본 ────────────────────────────────────────────────
        public float MaxSurvivalTime;
        public float Size;
        public float MoveSpeed;
        public float MouthMultiplier = 1f;   // 입 크기 · 흡입력
        public float VisionMultiplier = 1f;
        public float TimeGainMultiplier = 1f;
        public float CurrencyMultiplier = 1f;
        /// <summary>지금까지 모은 히든 아이템 수. 결과 화면 표시용.</summary>
        public int HiddenItemsFound;

        // ── 액티브 해금 ─────────────────────────────────────────
        public bool HasBooster;
        public bool HasVacuum;
        public bool HasScaleArmor;
        public bool HasGoldenBait;
        public bool HasVolt;
        public bool HasMissile;

        // ── 액티브 강화 ─────────────────────────────────────────
        public float BoosterDistanceMultiplier = 1f;
        public bool  BoosterPierceAnySize;      // 부스터 위력 강화
        public float VacuumRangeMultiplier = 1f;
        public int   ArmorStacks;               // 비늘 경화 방어 횟수
        public float BaitRangeMultiplier = 1f;
        public int   BaitCount = 1;
        public float VoltMultiplier = 1f;       // 범위 + 마비 시간
        public float MissileRangeMultiplier = 1f;
        public int   MissileCount = 1;

        /// <summary>DB 기본값 + 스킬 레벨로 스탯을 재계산한다.</summary>
        public static PlayerStats Build(GameDatabase db, PlayerProgress progress)
        {
            var s = new PlayerStats
            {
                MaxSurvivalTime = db.baseSurvivalTime,
                Size            = db.baseSize,
                MoveSpeed       = db.baseMoveSpeed,
            };

            float sizeMult = 1f;
            int armorExtra = 0, baitExtra = 0, missileExtra = 0;

            foreach (var node in db.skills)
            {
                if (node == null) continue;
                int level = progress.GetSkillLevel(node.id);
                // 예전 세이브에는 지금 최대 레벨보다 높은 값이 남아 있을 수 있다 (치아 교정 15 → 8 등)
                level = Mathf.Min(level, node.maxLevel);
                if (level <= 0) continue;

                float v = node.valuePerLevel;
                float compounded = Pow(1f + v, level);
                // 액티브 강화는 가산으로 쌓는다. 복리로 두면 +50% × 5레벨이 7.6배가 되어
                // 볼트 마비가 쿨타임보다 길어지는 등 풀트리에서 스킬이 깨진다.
                // (가산이면 1 + 0.5 × 5 = 3.5배)
                float linear = 1f + v * level;

                switch (node.effectType)
                {
                    // 해금
                    case SkillEffectType.UnlockBooster:    s.HasBooster = true;    break;
                    case SkillEffectType.UnlockVacuum:     s.HasVacuum = true;     break;
                    case SkillEffectType.UnlockScaleArmor: s.HasScaleArmor = true; break;
                    case SkillEffectType.UnlockGoldenBait: s.HasGoldenBait = true; break;
                    case SkillEffectType.UnlockVolt:       s.HasVolt = true;       break;
                    case SkillEffectType.UnlockMissile:    s.HasMissile = true;    break;

                    // 기본 강화
                    case SkillEffectType.SurvivalTime:  s.MaxSurvivalTime += v * level; break;
                    case SkillEffectType.BodyScale:     sizeMult *= compounded;         break;
                    case SkillEffectType.MouthPower:    s.MouthMultiplier *= compounded; break;
                    case SkillEffectType.Vision:        s.VisionMultiplier *= compounded; break;
                    case SkillEffectType.TimeGain:      s.TimeGainMultiplier *= compounded; break;
                    case SkillEffectType.CurrencyGain:  s.CurrencyMultiplier *= compounded; break;

                    // 액티브 강화
                    case SkillEffectType.BoosterRange:     s.BoosterDistanceMultiplier *= linear;     break;
                    case SkillEffectType.BoosterPower:     s.BoosterPierceAnySize = true;             break;
                    case SkillEffectType.VacuumRange:      s.VacuumRangeMultiplier *= linear;         break;
                    case SkillEffectType.ScaleArmorStack:  armorExtra += level;                       break;
                    case SkillEffectType.BaitRange:        s.BaitRangeMultiplier *= linear;           break;
                    case SkillEffectType.BaitCount:        baitExtra += level;                        break;
                    case SkillEffectType.VoltPower:        s.VoltMultiplier *= linear;                break;
                    case SkillEffectType.MissilePower:
                        s.MissileRangeMultiplier *= linear;
                        missileExtra += level;
                        break;
                }
            }

            // 크기 — 덧붙인 장갑의 복리. 절대 상한을 넘지 않는다.
            s.Size = Mathf.Min(db.maxPlayerSize, db.baseSize * sizeMult);

            // 속도 — 크기와 함께 오르되 지수로 완화 (안 그러면 후반에 조작 불가)
            float speedMult = Pow(sizeMult, db.speedScalingExponent);
            s.MoveSpeed = Mathf.Min(db.maxMoveSpeed, db.baseMoveSpeed * speedMult);

            // 액티브 강화 — 해금 안 된 스킬의 강화는 무시된다
            s.ArmorStacks   = s.HasScaleArmor ? Mathf.Clamp(1 + armorExtra, 1, db.armorMaxStacks) : 0;
            s.BaitCount     = s.HasGoldenBait ? Mathf.Max(1, 1 + baitExtra) : 0;
            s.MissileCount  = s.HasMissile ? Mathf.Max(1, 1 + missileExtra) : 0;

            ApplyCodexBonuses(s, db, progress);
            ApplyHiddenItemBonus(s, db, progress);

            // 입 크기 상한 — 도감 보너스까지 합친 뒤에 건다
            s.MouthMultiplier = Mathf.Min(db.maxMouthMultiplier, s.MouthMultiplier);

            return s;
        }

        /// <summary>
        /// 물고기 도감 보너스. 종을 codexMilestone마리 먹을 때마다 그 종의 효과가 한 단계씩 붙는다.
        /// 스킬트리와 달리 "많이 먹는 것" 자체가 보상이 되는 축이다.
        /// </summary>
        /// <summary>
        /// 구석에 숨어 있는 히든 아이템. 하나당 재화 획득 +5%가 영구히 붙는다.
        /// 복리가 아니라 가산이다 — 8개를 다 모아도 +40%로, 다른 강화를 압도하지 않는다.
        /// </summary>
        static void ApplyHiddenItemBonus(PlayerStats s, GameDatabase db, PlayerProgress progress)
        {
            if (db == null || progress == null) return;
            int count = progress.HiddenItemCount;
            if (count <= 0) return;

            s.HiddenItemsFound = count;
            s.CurrencyMultiplier *= 1f + db.hiddenItemCurrencyBonus * count;
        }

        static void ApplyCodexBonuses(PlayerStats s, GameDatabase db, PlayerProgress progress)
        {
            if (db.allFish == null || db.allFish.Count == 0) return;

            int milestone = Mathf.Max(1, db.codexMilestone);
            float speedBonus = 0f;

            foreach (var fish in db.allFish)
            {
                if (fish == null || fish.codexBonusType == CodexBonusType.None) continue;

                int eaten = progress.GetCodexCount(fish.CodexKey);
                if (eaten < milestone) continue;

                int tiers = Mathf.Min(eaten / milestone, Mathf.Max(1, db.codexMaxTiers));
                float amount = fish.codexBonusPerTier * tiers;

                switch (fish.codexBonusType)
                {
                    case CodexBonusType.Currency:     s.CurrencyMultiplier *= 1f + amount; break;
                    case CodexBonusType.TimeGain:     s.TimeGainMultiplier *= 1f + amount; break;
                    case CodexBonusType.MouthPower:   s.MouthMultiplier    *= 1f + amount; break;
                    case CodexBonusType.Vision:       s.VisionMultiplier   *= 1f + amount; break;
                    case CodexBonusType.SurvivalTime: s.MaxSurvivalTime    += amount;      break;
                    case CodexBonusType.MoveSpeed:    speedBonus           += amount;      break;
                }
            }

            if (speedBonus > 0f)
                s.MoveSpeed = Mathf.Min(db.maxMoveSpeed * 1.5f, s.MoveSpeed * (1f + speedBonus));
        }

        /// <summary>도감 한 종의 현재 단계 수 (UI 표시용).</summary>
        public static int CodexTier(FishSpecies fish, GameDatabase db, PlayerProgress progress)
        {
            if (fish == null || db == null || progress == null) return 0;
            int milestone = Mathf.Max(1, db.codexMilestone);
            return Mathf.Min(progress.GetCodexCount(fish.CodexKey) / milestone,
                             Mathf.Max(1, db.codexMaxTiers));
        }

        static float Pow(float baseValue, float exp) => (float)Math.Pow(baseValue, exp);
    }
}
