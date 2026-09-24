using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>게임 설정 값. settings.json으로 저장된다 (진행도 세이브와 별개 — 세이브를 지워도 설정은 남는다).</summary>
    [Serializable]
    public class GameSettingsData
    {
        // ── 사운드 ──
        public float masterVolume = 0.8f;
        public float musicVolume = 0.7f;
        public float sfxVolume = 0.9f;
        public bool mute = false;

        // ── 화면 ──
        /// <summary>0 전체 화면(창 없는) · 1 창 모드 · 2 독점 전체 화면</summary>
        public int screenMode = 0;
        /// <summary>0이면 아직 고른 적 없음 → 지금 해상도를 그대로 쓴다</summary>
        public int width = 0;
        public int height = 0;
        public bool vSync = true;
        /// <summary>수직 동기화를 끈 경우의 프레임 제한. 0 = 무제한</summary>
        public int frameLimit = 60;

        // ── 조작 ──
        /// <summary>0 키보드(WASD·방향키) · 1 마우스 따라가기</summary>
        public int controlScheme = 0;
    }

    /// <summary>
    /// 설정을 불러오고, 저장하고, 실제로 적용한다. 어느 씬에서든 GameSettings.Data로 읽는다.
    ///
    ///   사운드  AudioListener.volume(전체·음소거) + AudioManager가 배경음·효과음 배율을 곱한다
    ///   화면    Screen.SetResolution / QualitySettings.vSyncCount / Application.targetFrameRate
    ///   조작    PlayerFish가 Changed 이벤트를 받아 바로 바꾼다 (판 중에도)
    /// </summary>
    public static class GameSettings
    {
        const string FileName = "settings.json";
        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static GameSettingsData _data;
        public static GameSettingsData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        /// <summary>값이 바뀌어 적용된 직후 (사운드·조작을 쓰는 곳이 듣는다)</summary>
        public static event Action Changed;

        public static readonly int[] FrameLimits = { 30, 60, 120, 144, 0 };
        public static readonly string[] ScreenModeNames = { "전체 화면", "창 모드", "독점 전체 화면" };
        public static readonly string[] ControlSchemeNames = { "키보드 (WASD · 방향키)", "마우스 따라가기" };

        // 게임이 시작될 때 한 번 — 첫 씬이 뜨기 전에 저장된 설정을 적용한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Load();
            ApplyAudio();
            ApplyDisplay(changeResolution: _data.width > 0 && _data.height > 0);
        }

        // ── 저장 / 불러오기 ─────────────────────────────────────
        public static void Load()
        {
            _data = null;
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonUtility.FromJson<GameSettingsData>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 설정을 읽지 못해 기본값을 씁니다: {e.Message}");
            }
            if (_data == null) _data = new GameSettingsData();
            Sanitize(_data);
        }

        public static void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 설정 저장 실패: {e.Message}");
            }
        }

        public static void ResetToDefaults()
        {
            var keepRes = new Vector2Int(Data.width, Data.height);
            _data = new GameSettingsData { width = keepRes.x, height = keepRes.y };
            ApplyAudio();
            ApplyDisplay(changeResolution: false);
            Changed?.Invoke();
        }

        static void Sanitize(GameSettingsData d)
        {
            d.masterVolume = Mathf.Clamp01(d.masterVolume);
            d.musicVolume = Mathf.Clamp01(d.musicVolume);
            d.sfxVolume = Mathf.Clamp01(d.sfxVolume);
            d.screenMode = Mathf.Clamp(d.screenMode, 0, ScreenModeNames.Length - 1);
            d.controlScheme = Mathf.Clamp(d.controlScheme, 0, ControlSchemeNames.Length - 1);
            if (Array.IndexOf(FrameLimits, d.frameLimit) < 0) d.frameLimit = 60;
            if (d.width < 0 || d.height < 0) { d.width = 0; d.height = 0; }
        }

        // ── 적용 ────────────────────────────────────────────────
        /// <summary>사운드 값을 바꾼 뒤 부른다.</summary>
        public static void ApplyAudio()
        {
            AudioListener.volume = Data.mute ? 0f : Data.masterVolume;
            Changed?.Invoke();
        }

        /// <summary>화면 값을 바꾼 뒤 부른다. changeResolution이 거짓이면 모드·동기화만 바꾼다.</summary>
        public static void ApplyDisplay(bool changeResolution = true)
        {
            var d = Data;
            QualitySettings.vSyncCount = d.vSync ? 1 : 0;
            Application.targetFrameRate = d.vSync || d.frameLimit <= 0 ? -1 : d.frameLimit;

            var mode = ModeOf(d.screenMode);
            int w = d.width > 0 ? d.width : Screen.width;
            int h = d.height > 0 ? d.height : Screen.height;

            if (changeResolution || Screen.fullScreenMode != mode)
                Screen.SetResolution(w, h, mode);
        }

        public static void ApplyControls() => Changed?.Invoke();

        public static FullScreenMode ModeOf(int index) => index switch
        {
            1 => FullScreenMode.Windowed,
            2 => FullScreenMode.ExclusiveFullScreen,
            _ => FullScreenMode.FullScreenWindow,
        };

        /// <summary>고를 수 있는 해상도 (주사율 무시, 큰 것부터). 목록이 비면 지금 해상도 하나.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var set = new HashSet<Vector2Int>();
            foreach (var r in Screen.resolutions)
                if (r.width >= 800 && r.height >= 600) set.Add(new Vector2Int(r.width, r.height));
            set.Add(new Vector2Int(Screen.width, Screen.height));
            if (Data.width > 0 && Data.height > 0) set.Add(new Vector2Int(Data.width, Data.height));

            var list = new List<Vector2Int>(set);
            list.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        public static Vector2Int CurrentResolution =>
            Data.width > 0 && Data.height > 0 ? new Vector2Int(Data.width, Data.height)
                                               : new Vector2Int(Screen.width, Screen.height);
    }
}
