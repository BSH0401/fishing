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

        // 배경음은 두 트랙을 번갈아 쓰며 교차 페이드한다 (구역이 바뀌면 곡이 부드럽게 넘어간다)
        const float MusicFadeSeconds = 1.5f;
        AudioSource[] _music = new AudioSource[2];
        float[] _musicFade = new float[2];      // 0~1 현재 페이드 값
        float[] _musicBase = new float[2];      // 곡마다 정한 볼륨
        int _musicActive;
        AudioClip _musicClip;
        // 곡을 바꿨다가 돌아오면 끊긴 자리부터 이어 튼다 (통로 근처를 오갈 때 처음부터 다시 나오지 않게)
        readonly Dictionary<AudioClip, float> _musicResume = new Dictionary<AudioClip, float>();

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

            for (int i = 0; i < 2; i++)
            {
                var m = gameObject.AddComponent<AudioSource>();
                m.playOnAwake = false;
                m.loop = true;
                m.spatialBlend = 0f;
                m.ignoreListenerPause = true;
                m.volume = 0f;
                _music[i] = m;
            }

            GameSettings.Changed += OnSettingsChanged;
        }

        void OnDestroy() => GameSettings.Changed -= OnSettingsChanged;

        static float Sfx => GameSettings.Data.sfxVolume;
        static float Music => GameSettings.Data.musicVolume;

        void OnSettingsChanged()
        {
            float master = _bank != null ? _bank.masterVolume : 0.8f;
            if (_loopSource != null) _loopSource.volume = Mathf.Clamp01(_loopBaseVolume * master * Sfx);
            ApplyMusicVolumes();
        }

        // ── 배경음 ──────────────────────────────────────────────
        /// <summary>
        /// 배경음을 바꾼다. 같은 곡이면 이어서 튼다(볼륨만 갱신). null이면 페이드 아웃.
        /// 다른 곡이면 1.5초 동안 교차 페이드한다.
        /// </summary>
        public static void PlayMusic(AudioClip clip, float volume = 0.6f) => Instance.PlayMusicInternal(clip, volume);

        void PlayMusicInternal(AudioClip clip, float volume)
        {
            var cur = _music[_musicActive];
            if (clip != null && _musicClip == clip && cur.isPlaying)
            {
                _musicBase[_musicActive] = volume;
                ApplyMusicVolumes();
                return;
            }

            // 지금 곡은 이어 틀 자리를 기억해 두고 페이드 아웃
            if (_musicClip != null && cur.isPlaying) _musicResume[_musicClip] = cur.time;

            _musicClip = clip;
            if (clip == null) { _musicActive = 1 - _musicActive; _music[_musicActive].Stop(); _musicFade[_musicActive] = 0f; return; }

            _musicActive = 1 - _musicActive;
            var next = _music[_musicActive];
            next.Stop();
            next.clip = clip;
            _musicBase[_musicActive] = volume;
            _musicFade[_musicActive] = 0f;
            if (_musicResume.TryGetValue(clip, out float t) && clip.length > 0f) next.time = Mathf.Repeat(t, clip.length);
            next.volume = 0f;
            next.Play();
        }

        void Update()
        {
            if (_music[0] == null) return;
            float step = Time.unscaledDeltaTime / MusicFadeSeconds;
            for (int i = 0; i < 2; i++)
            {
                float target = i == _musicActive && _musicClip != null ? 1f : 0f;
                _musicFade[i] = Mathf.MoveTowards(_musicFade[i], target, step);
                if (target == 0f && _musicFade[i] <= 0f && _music[i].isPlaying) _music[i].Stop();
            }
            ApplyMusicVolumes();
        }

        void ApplyMusicVolumes()
        {
            for (int i = 0; i < 2; i++)
                if (_music[i] != null) _music[i].volume = Mathf.Clamp01(_musicBase[i] * _musicFade[i] * Music);
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
