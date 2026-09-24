using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Core
{
    /// <summary>
    /// 효과음 · 배경음 재생기. 자동 생성 싱글톤이라 씬에 배치할 필요가 없다.
    /// 볼륨은 설정(GameSettings)을 따른다: 전체·음소거는 AudioListener, 효과음·배경음은 여기서 곱한다.
    /// SoundBank의 클립이 비어 있으면 조용히 넘어간다 — 아트/사운드 없이도 게임이 돈다.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public const string BankResourceName = "SoundBank";
        const int VoiceCount = 8;

        static AudioManager _instance;

        public static AudioManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("~AudioManager") { hideFlags = HideFlags.HideAndDontSave };
                _instance = go.AddComponent<AudioManager>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        SoundBank _bank;
        AudioSource[] _voices;
        int _nextVoice;
        readonly Dictionary<AudioClip, float> _lastPlayed = new Dictionary<AudioClip, float>();

        AudioSource _loopSource;
        AudioClip _loopClip;
        float _loopBaseVolume = 1f;

        AudioSource _musicSource;
        AudioClip _musicClip;
        float _musicBaseVolume = 0.6f;

        public SoundBank Bank => _bank;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            _bank = Resources.Load<SoundBank>(BankResourceName);

            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;          // 2D
                src.ignoreListenerPause = true; // 일시정지 중 UI 소리도 들리게
                _voices[i] = src;
            }

            _loopSource = gameObject.AddComponent<AudioSource>();
            _loopSource.playOnAwake = false;
            _loopSource.loop = true;
            _loopSource.spatialBlend = 0f;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.ignoreListenerPause = true;

            GameSettings.Changed += OnSettingsChanged;
        }

        void OnDestroy() => GameSettings.Changed -= OnSettingsChanged;

        static float Sfx => GameSettings.Data.sfxVolume;
        static float Music => GameSettings.Data.musicVolume;

        void OnSettingsChanged()
        {
            float master = _bank != null ? _bank.masterVolume : 0.8f;
            if (_loopSource != null) _loopSource.volume = Mathf.Clamp01(_loopBaseVolume * master * Sfx);
            if (_musicSource != null) _musicSource.volume = Mathf.Clamp01(_musicBaseVolume * Music);
        }

        // ── 배경음 ──────────────────────────────────────────────
        /// <summary>배경음을 바꾼다. 같은 곡이면 이어서 튼다. null이면 멈춘다.</summary>
        public static void PlayMusic(AudioClip clip, float volume = 0.6f) => Instance.PlayMusicInternal(clip, volume);

        void PlayMusicInternal(AudioClip clip, float volume)
        {
            _musicBaseVolume = volume;
            _musicSource.volume = Mathf.Clamp01(volume * Music);
            if (clip == null)
            {
                if (_musicSource.isPlaying) _musicSource.Stop();
                _musicClip = null;
                return;
            }
            if (_musicClip == clip && _musicSource.isPlaying) return;
            _musicClip = clip;
            _musicSource.clip = clip;
            _musicSource.Play();
        }

        // ══════════════════════════════════════════════════════════
        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
            => Instance.PlayInternal(clip, volume, pitch);

        void PlayInternal(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;

            float minGap = _bank != null ? _bank.minInterval : 0.04f;
            if (_lastPlayed.TryGetValue(clip, out float last) && Time.unscaledTime - last < minGap)
                return;
            _lastPlayed[clip] = Time.unscaledTime;

            float variance = _bank != null ? _bank.pitchVariance : 0.1f;
            float master = _bank != null ? _bank.masterVolume : 0.8f;

            var src = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % VoiceCount;

            src.pitch = pitch * (1f + Random.Range(-variance, variance));
            src.volume = Mathf.Clamp01(volume * master * Sfx);
            src.PlayOneShot(clip);
        }

        /// <summary>제한시간 심박음처럼 계속 울려야 하는 소리.</summary>
        public static void SetLoop(AudioClip clip, float volume = 1f, float pitch = 1f)
            => Instance.SetLoopInternal(clip, volume, pitch);

        void SetLoopInternal(AudioClip clip, float volume, float pitch)
        {
            if (clip == null)
            {
                if (_loopSource.isPlaying) _loopSource.Stop();
                _loopClip = null;
                return;
            }

            float master = _bank != null ? _bank.masterVolume : 0.8f;
            _loopBaseVolume = volume;
            _loopSource.volume = Mathf.Clamp01(volume * master * Sfx);
            _loopSource.pitch = pitch;

            if (_loopClip != clip)
            {
                _loopClip = clip;
                _loopSource.clip = clip;
                _loopSource.Play();
            }
        }

        public static void StopLoop() => Instance.SetLoopInternal(null, 0f, 1f);
    }
}
