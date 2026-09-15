using System;
using System.IO;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>
    /// PlayerProgress를 JSON으로 Application.persistentDataPath에 저장/로드한다.
    /// 저장은 임시 파일에 쓴 뒤 교체하는 방식이라 중간에 죽어도 세이브가 깨지지 않는다.
    /// </summary>
    public static class SaveSystem
    {
        const string FileName = "progress.json";
        const string BackupName = "progress.backup.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        static string BackupPath => Path.Combine(Application.persistentDataPath, BackupName);
        static string TempPath => SavePath + ".tmp";

        public static bool SaveExists => File.Exists(SavePath);

        public static void Save(PlayerProgress progress)
        {
            if (progress == null) return;
            try
            {
                progress.version = PlayerProgress.CurrentVersion;
                string json = JsonUtility.ToJson(progress, prettyPrint: true);

                File.WriteAllText(TempPath, json);

                if (File.Exists(SavePath))
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    File.Move(SavePath, BackupPath);
                }
                File.Move(TempPath, SavePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 실패: {e.Message}");
            }
        }

        public static PlayerProgress Load()
        {
            var loaded = TryLoadFrom(SavePath);
            if (loaded == null)
            {
                loaded = TryLoadFrom(BackupPath);
                if (loaded != null) Debug.LogWarning("[SaveSystem] 본 세이브가 손상되어 백업에서 복구했습니다.");
            }

            if (loaded == null) loaded = new PlayerProgress();
            loaded.OnAfterLoad();
            return loaded;
        }

        static PlayerProgress TryLoadFrom(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<PlayerProgress>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 로드 실패 ({path}): {e.Message}");
                return null;
            }
        }

        public static void DeleteSave()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 삭제 실패: {e.Message}");
            }
        }
    }
}
