using FishGame.Core;
using FishGame.Data;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 배경음을 게임에 꽂는다. (구글 드라이브 FishGame/사운드에서 고른 곡)
    ///
    ///   Audio/Music/BGM_Menu.ogg          → 타이틀 · 메인 메뉴     Feel Good Island (CC0)
    ///   Audio/Music/BGM_Zone1_Tank.ogg    → 1 어항                  Underwater Theme II (CC0)
    ///   Audio/Music/BGM_Zone2_Sewer.ogg   → 2 하수구                Quantum Stone (CC0)
    ///   Audio/Music/BGM_Zone3_River.ogg   → 3 강                    Relaxing
    ///   Audio/Music/BGM_Zone4_Ocean.ogg   → 4 바다                  Submarine Game Music (CC0)
    ///
    /// 볼륨은 곡마다 녹음 크기(평균 dB)를 재서 비슷하게 들리도록 맞춘 값이다.
    /// 곡을 바꾸려면 같은 이름으로 파일만 덮어쓰면 된다 (mp3는 반복 지점에 틈이 생기니 ogg/wav 권장).
    ///
    /// 비어 있는 칸만 자동으로 채운다 — 인스펙터에서 직접 바꾼 곡은 건드리지 않는다.
    /// 메뉴(FishGame ▸ 8. 배경음 적용)로 돌리면 모두 이 표대로 다시 맞춘다.
    /// </summary>
    [InitializeOnLoad]
    public static class SoundApplier
    {
        public const string MusicFolder = "Assets/_Project/Audio/Music";

        static readonly (string file, float volume)[] ZoneMusic =
        {
            ("BGM_Zone1_Tank",  0.70f),
            ("BGM_Zone2_Sewer", 1.00f),   // 녹음이 작다 (평균 -25dB)
            ("BGM_Zone3_River", 0.30f),   // 녹음이 크다 (평균 -12dB)
            ("BGM_Zone4_Ocean", 0.45f),
        };
        const string MenuFile = "BGM_Menu";
        const float MenuVolume = 0.47f;

        static SoundApplier()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                int n = Apply(overwrite: false);
                if (n > 0) Debug.Log($"[FishGame] 배경음 {n}곳 연결 (비어 있던 칸만)");
            };
        }

        [MenuItem("FishGame/8. 배경음 적용", false, 8)]
        static void ApplyMenu()
        {
            int n = Apply(overwrite: true);
            Debug.Log($"[FishGame] 배경음 적용 — {n}곳 변경");
        }

        /// <summary>overwrite=false면 비어 있는 칸만 채운다.</summary>
        public static int Apply(bool overwrite)
        {
            int changed = 0;

            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>("Assets/_Project/Resources/" + AudioManager.BankResourceName + ".asset");
            if (bank == null)
                foreach (var guid in AssetDatabase.FindAssets("t:SoundBank"))
                { bank = AssetDatabase.LoadAssetAtPath<SoundBank>(AssetDatabase.GUIDToAssetPath(guid)); if (bank != null) break; }

            var menu = LoadClip(MenuFile);
            if (bank != null && menu != null && (bank.menuMusic == null || (overwrite && bank.menuMusic != menu)))
            {
                bank.menuMusic = menu;
                bank.menuMusicVolume = MenuVolume;
                EditorUtility.SetDirty(bank);
                changed++;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:ZoneData"))
            {
                var zone = AssetDatabase.LoadAssetAtPath<ZoneData>(AssetDatabase.GUIDToAssetPath(guid));
                if (zone == null || zone.zoneIndex < 0 || zone.zoneIndex >= ZoneMusic.Length) continue;
                var (file, volume) = ZoneMusic[zone.zoneIndex];
                var clip = LoadClip(file);
                if (clip == null) continue;
                if (zone.music != null && !(overwrite && zone.music != clip)) continue;
                zone.music = clip;
                zone.musicVolume = volume;
                EditorUtility.SetDirty(zone);
                changed++;
            }

            if (changed > 0) AssetDatabase.SaveAssets();
            return changed;
        }

        static AudioClip LoadClip(string name)
        {
            foreach (var ext in new[] { ".ogg", ".wav", ".mp3" })
            {
                string path = $"{MusicFolder}/{name}{ext}";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) return clip;
            }
            return null;
        }
    }

    /// <summary>배경음 임포트 설정 — 길고 큰 곡이라 스트리밍 · Vorbis로 메모리를 아낀다.</summary>
    public class MusicImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(SoundApplier.MusicFolder + "/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            importer.defaultSampleSettings = s;
        }
    }
}
