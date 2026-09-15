using System;

namespace FishGame.Utils
{
    /// <summary>인크리멘탈 게임용 숫자 축약 표기 (1.2K, 3.4M ...).</summary>
    public static class NumberFormatter
    {
        static readonly string[] Suffixes = { "", "K", "M", "B", "T", "aa", "ab", "ac", "ad", "ae" };

        public static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            bool negative = value < 0;
            value = Math.Abs(value);

            if (value < 1000d)
            {
                string small = value < 10d && value % 1d != 0d
                    ? value.ToString("0.#")
                    : Math.Floor(value).ToString("0");
                return negative ? "-" + small : small;
            }

            int tier = (int)Math.Floor(Math.Log10(value) / 3d);
            tier = Math.Min(tier, Suffixes.Length - 1);
            double scaled = value / Math.Pow(1000d, tier);

            string body = scaled >= 100d ? scaled.ToString("0")
                        : scaled >= 10d  ? scaled.ToString("0.#")
                                         : scaled.ToString("0.##");

            return (negative ? "-" : "") + body + Suffixes[tier];
        }

        /// <summary>초를 mm:ss로.</summary>
        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = (int)seconds;
            return $"{total / 60:00}:{total % 60:00}";
        }

        /// <summary>제한시간 HUD용. 10초 미만이면 소수 1자리까지.</summary>
        public static string FormatCountdown(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            if (seconds < 10f) return seconds.ToString("0.0");
            return FormatTime(seconds);
        }
    }
}
