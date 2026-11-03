using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectC.Client
{
    // One bounded 2D sound service for all scenes. It never reads hidden card data or
    // network commands; callers submit presentation cues after accepted state changes.
    [DefaultExecutionOrder(-300)]
    public sealed class ClientAudio : MonoBehaviour
    {
        private const string LibraryPath = "Audio/ClientSoundLibrary";
        private const string UiVolumeKey = "ProjectC.Audio.UiSfxVolume";
        private const string BattleVolumeKey = "ProjectC.Audio.BattleSfxVolume";
        private const float DefaultUiVolume = .65f;
        private const float DefaultBattleVolume = .8f;
        private const int UiVoiceCount = 3;
        private const int BattleVoiceCount = 6;
        private const int ResultVoiceCount = 1;
        private const float OutputHeadroom = .7f;
        private const float HitDelay = .065f;
        private const float ResultDelay = .18f;
        private const float MinimumPitch = .5f;
        private const float MaximumPitch = 1.5f;
        private static readonly int CueCount = Enum.GetValues(typeof(SoundCue)).Length;
        private static ClientAudio _instance;
        private readonly SoundDefinition[] _sounds = new SoundDefinition[CueCount];
        private readonly double[] _lastPlayed = new double[CueCount];
        private readonly bool[] _warned = new bool[CueCount];
        private Voice[] _voices;
        private AudioListener _fallbackListener;
        private float _uiVolume;
        private float _battleVolume;
        private string _turnMatch;
        private int _turnGame, _turnNumber;
        private string _resultMatch;
        private int _resultGame;
        internal event Action<SoundCue> SoundPlayed;
        public static ClientAudio Shared => Ensure();

        private sealed class Voice
        {
            public AudioSource Source;
            public bool Ui;
            public float Gain;
            public double Until;
        }

        public float UiVolume
        {
            get => _uiVolume;
            set => SetVolume(value, true);
        }

        public float BattleVolume
        {
            get => _battleVolume;
            set => SetVolume(value, false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartAudio()
        {
            Ensure();
        }

        private static ClientAudio Ensure()
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ClientAudio>();
                if (_instance == null)
                {
                    _instance = new GameObject("ClientAudio").AddComponent<ClientAudio>();
                }
            }
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _uiVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(UiVolumeKey, DefaultUiVolume));
            _battleVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BattleVolumeKey, DefaultBattleVolume));
            for (int i = 0; i < CueCount; ++i)
            {
                _lastPlayed[i] = double.NegativeInfinity;
            }
            var library = Resources.Load<SoundLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogWarning("Sound library is missing; sound effects will be skipped.", this);
            }
            else
            {
                foreach (var sound in library.Sounds ?? Array.Empty<SoundDefinition>())
                {
                    if (sound != null && (uint)sound.Cue < CueCount)
                    {
                        _sounds[(int)sound.Cue] = sound;
                    }
                }
            }
            _voices = new Voice[UiVoiceCount + BattleVoiceCount + ResultVoiceCount];
            for (int i = 0; i < _voices.Length; ++i)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                source.loop = false;
                source.dopplerLevel = 0;
                _voices[i] = new Voice { Source = source, Ui = i < UiVoiceCount };
            }
            // Lobby and deck editor already have listeners. The fallback is enabled only
            // in scenes without one, including standalone BattleScene and prefab previews.
            _fallbackListener = gameObject.AddComponent<AudioListener>();
            RefreshListener();
            SceneManager.sceneLoaded += Loaded;
            SceneManager.sceneUnloaded += Unloaded;
        }

        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            RefreshListener();
        }

        private void Unloaded(Scene scene)
        {
            RefreshListener();
        }

        private void RefreshListener()
        {
            bool otherListener = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (listener != _fallbackListener && listener.isActiveAndEnabled)
                {
                    otherListener = true;
                    break;
                }
            }
            _fallbackListener.enabled = !otherListener;
        }

        public static bool Play(SoundCue cue)
        {
            return Shared.PlayCue(cue);
        }

        private bool PlayCue(SoundCue cue)
        {
            int index = (int)cue;
            if ((uint)index >= CueCount)
            {
                return false;
            }
            var sound = _sounds[index];
            if (sound == null || sound.Clip == null)
            {
                if (!_warned[index])
                {
                    _warned[index] = true;
                    Debug.LogWarning("Missing sound clip for " + cue + "; gameplay continues without it.", this);
                }
                return false;
            }
            bool ui = IsUi(cue);
            if ((ui ? _uiVolume : _battleVolume) <= 0 || AudioListener.volume <= 0 || sound.Gain <= 0)
            {
                return false;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            // A spell can emit SpellActivated and EffectActivated in one event batch.
            // Use one shared gate so its cast is heard once, without delaying state updates.
            int gate = cue == SoundCue.CardUse ? (int)SoundCue.Effect : index;
            if (now - _lastPlayed[gate] < sound.Cooldown)
            {
                return false;
            }
            _lastPlayed[gate] = now;
            bool result = cue == SoundCue.Victory || cue == SoundCue.Defeat;
            int first = ui ? 0 : UiVoiceCount;
            int end = ui ? UiVoiceCount : UiVoiceCount + BattleVoiceCount;
            Voice voice = result ? _voices[_voices.Length - 1] : _voices[first];
            if (!result)
            {
                for (int i = first; i < end; ++i)
                {
                    if (_voices[i].Until <= now)
                    {
                        voice = _voices[i];
                        break;
                    }
                    if (_voices[i].Until < voice.Until)
                    {
                        voice = _voices[i];
                    }
                }
            }
            float delay = cue == SoundCue.Hit ? HitDelay : result ? ResultDelay : 0;
            voice.Source.Stop();
            voice.Source.clip = sound.Clip;
            voice.Source.pitch = Mathf.Clamp(sound.Pitch, MinimumPitch, MaximumPitch);
            voice.Gain = Mathf.Clamp01(sound.Gain);
            voice.Source.volume = voice.Gain * (ui ? _uiVolume : _battleVolume) * OutputHeadroom;
            voice.Until = now + delay + sound.Clip.length / voice.Source.pitch;
            if (delay > 0)
            {
                voice.Source.PlayScheduled(AudioSettings.dspTime + delay);
            }
            else
            {
                voice.Source.Play();
            }
            SoundPlayed?.Invoke(cue);
            return true;
        }

        private static bool IsUi(SoundCue cue)
        {
            return cue == SoundCue.CardSelect || cue == SoundCue.CardDeselect || cue == SoundCue.ButtonClick;
        }

        public void PlayTurn(string match, int game, int turn)
        {
            if (_turnMatch == match && _turnGame == game && _turnNumber == turn)
            {
                return;
            }
            _turnMatch = match;
            _turnGame = game;
            _turnNumber = turn;
            PlayCue(SoundCue.TurnStart);
        }

        public void PlayResult(string match, int game, int winner, int viewer)
        {
            if (_resultMatch == match && _resultGame == game)
            {
                return;
            }
            _resultMatch = match;
            _resultGame = game;
            if (winner > 0 && viewer > 0)
            {
                PlayCue(winner == viewer ? SoundCue.Victory : SoundCue.Defeat);
            }
        }

        public static void StopBattleIfRunning()
        {
            if (_instance != null)
            {
                _instance.StopBattle();
            }
        }

        public void StopBattle()
        {
            for (int i = UiVoiceCount; i < _voices.Length; ++i)
            {
                _voices[i].Source.Stop();
                _voices[i].Until = 0;
            }
            for (int i = 0; i < CueCount; ++i)
            {
                if (!IsUi((SoundCue)i))
                {
                    _lastPlayed[i] = double.NegativeInfinity;
                }
            }
        }

        private void SetVolume(float value, bool ui)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Sound volume must be finite.");
            }
            value = Mathf.Clamp01(value);
            if (ui)
            {
                _uiVolume = value;
            }
            else
            {
                _battleVolume = value;
            }
            PlayerPrefs.SetFloat(ui ? UiVolumeKey : BattleVolumeKey, value);
            foreach (var voice in _voices)
            {
                if (voice.Ui == ui)
                {
                    voice.Source.volume = value * voice.Gain * OutputHeadroom;
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                PlayerPrefs.Save();
            }
        }

        private void OnApplicationQuit()
        {
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= Loaded;
                SceneManager.sceneUnloaded -= Unloaded;
                _instance = null;
            }
        }
    }
}
