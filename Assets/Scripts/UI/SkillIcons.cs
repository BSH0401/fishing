using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리 칸에 얹는 직접 그린 아이콘.
    ///
    ///   Resources/SkillIcons/&lt;SkillEffectType 이름&gt;.png   예) MouthPower.png, VacuumRange.png
    ///   없으면 액티브 해금은 HUD 아이템 아이콘(Resources/Icons/Item_*)을 같이 쓴다.
    ///   둘 다 없으면 null — 칸은 예전처럼 도형만 보인다.
    ///
    /// 그림을 새로 넣을 때는 파일 이름만 맞춰 SkillIcons 폴더에 두면 된다 (임포트 설정은 SkillIconImporter가 맞춘다).
    /// </summary>
    public static class SkillIcons
    {
        const string Folder = "SkillIcons/";

        /// <summary>전용 그림이 없을 때 대신 쓸 HUD 아이콘.</summary>
        static readonly Dictionary<SkillEffectType, string> Fallback = new Dictionary<SkillEffectType, string>
        {
            { SkillEffectType.UnlockBooster,    "Icons/Item_Booster" },
            { SkillEffectType.UnlockScaleArmor, "Icons/Item_ScaleArmor" },
            { SkillEffectType.UnlockGoldenBait, "Icons/Item_GoldenBait" },
            { SkillEffectType.UnlockVolt,       "Icons/Item_Volt" },
            { SkillEffectType.UnlockMissile,    "Icons/Item_Missile" },
        };

        static readonly Dictionary<SkillEffectType, Sprite> _cache = new Dictionary<SkillEffectType, Sprite>();

        static Sprite _pillOn, _pillOff;
        static bool _pillLoaded;

        /// <summary>해금 · 시작 칸(알약)의 둥근 판. 찍음 = 하늘색, 아직 = 회색. 없으면 null — 예전 도형을 쓴다.</summary>
        public static Sprite PillOn { get { LoadPills(); return _pillOn; } }
        public static Sprite PillOff { get { LoadPills(); return _pillOff; } }

        static void LoadPills()
        {
            if (_pillLoaded) return;
            _pillLoaded = true;
            _pillOn = Resources.Load<Sprite>(Folder + "Pill_On");
            _pillOff = Resources.Load<Sprite>(Folder + "Pill_Off");
        }

        public static Sprite For(SkillEffectType type)
        {
            if (_cache.TryGetValue(type, out var s)) return s;
            s = Resources.Load<Sprite>(Folder + type);
            if (s == null && Fallback.TryGetValue(type, out var path)) s = Resources.Load<Sprite>(path);
            _cache[type] = s;
            return s;
        }
    }
}
