using UnityEngine;
using UnityEngine.Audio;

namespace PolarBreakout
{
    public enum DisplayModeOption { Fullscreen, Windowed, BorderlessWindowed }

    public enum ColorblindMode { None, Protanopia, Deuteranopia, Tritanopia }

    /// <summary>
    /// Central, static home for every player-facing setting - deliberately not a
    /// MonoBehaviour/singleton, matching this project's existing preference for plain managers
    /// (ScoreManager, LivesManager) over DontDestroyOnLoad lifecycles. Each scene calls Load()
    /// plus whichever Apply*() methods it needs at its own startup, rather than one persistent
    /// object surviving scene loads - avoids duplicate-instance lifecycle issues entirely.
    /// </summary>
    public static class GameSettings
    {
        private const string MasterVolumeKey = "Settings.MasterVolume";
        private const string MusicVolumeKey = "Settings.MusicVolume";
        private const string SFXVolumeKey = "Settings.SFXVolume";
        private const string TextSizeMultiplierKey = "Settings.TextSizeMultiplier";
        private const string InvertPaddleAxisKey = "Settings.InvertPaddleAxis";
        private const string DisplayModeKey = "Settings.DisplayMode";
        private const string ReduceMotionKey = "Settings.ReduceMotion";
        private const string ColorblindModeKey = "Settings.ColorblindMode";

        public static float MasterVolume = 1f;
        public static float MusicVolume = 1f;
        public static float SFXVolume = 1f;
        public static float TextSizeMultiplier = 1f;
        public static bool InvertPaddleAxis;
        public static DisplayModeOption DisplayMode = DisplayModeOption.Fullscreen;
        public static bool ReduceMotion;
        public static ColorblindMode ColorblindFilter = ColorblindMode.None;

        /// <summary>Fired after Load() or Save() - live UI (ScalableText, an already-open
        /// settings panel) subscribes to react without polling, matching the
        /// ScoreManager.OnScoreChanged event-driven convention already used elsewhere.</summary>
        public static event System.Action OnSettingsChanged;

        private static bool _loaded;
        private static AudioMixer _audioMixer;

        public static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            MusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
            SFXVolume = PlayerPrefs.GetFloat(SFXVolumeKey, 1f);
            TextSizeMultiplier = PlayerPrefs.GetFloat(TextSizeMultiplierKey, 1f);
            InvertPaddleAxis = PlayerPrefs.GetInt(InvertPaddleAxisKey, 0) != 0;
            DisplayMode = (DisplayModeOption)PlayerPrefs.GetInt(DisplayModeKey, (int)DisplayModeOption.Fullscreen);
            ReduceMotion = PlayerPrefs.GetInt(ReduceMotionKey, 0) != 0;
            ColorblindFilter = (ColorblindMode)PlayerPrefs.GetInt(ColorblindModeKey, (int)ColorblindMode.None);

            _loaded = true;
            OnSettingsChanged?.Invoke();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(MasterVolumeKey, MasterVolume);
            PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            PlayerPrefs.SetFloat(SFXVolumeKey, SFXVolume);
            PlayerPrefs.SetFloat(TextSizeMultiplierKey, TextSizeMultiplier);
            PlayerPrefs.SetInt(InvertPaddleAxisKey, InvertPaddleAxis ? 1 : 0);
            PlayerPrefs.SetInt(DisplayModeKey, (int)DisplayMode);
            PlayerPrefs.SetInt(ReduceMotionKey, ReduceMotion ? 1 : 0);
            PlayerPrefs.SetInt(ColorblindModeKey, (int)ColorblindFilter);
            PlayerPrefs.Save();

            OnSettingsChanged?.Invoke();
        }

        /// <summary>Guards Apply*() calls made before any explicit Load() - safe default so a
        /// scene that forgets to call Load() first still gets sensible values instead of the
        /// bare field initializers silently going untouched.</summary>
        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        /// <summary>Call once per scene with that scene's AudioMixer reference (there's no
        /// persistent audio object - each scene that has one wires it in before ApplyAudio()).</summary>
        public static void SetAudioMixer(AudioMixer mixer) => _audioMixer = mixer;

        public static void ApplyAudio()
        {
            EnsureLoaded();
            if (_audioMixer == null) return;

            _audioMixer.SetFloat("MasterVolume", LinearToDecibel(MasterVolume));
            _audioMixer.SetFloat("MusicVolume", LinearToDecibel(MusicVolume));
            _audioMixer.SetFloat("SFXVolume", LinearToDecibel(SFXVolume));
        }

        /// <summary>Converts a 0-1 linear UI slider value to decibels for an AudioMixer exposed
        /// parameter - floored well above 0 first so Log10 never produces -Infinity at the
        /// slider's minimum.</summary>
        public static float LinearToDecibel(float linear) => Mathf.Log10(Mathf.Max(linear, 0.0001f)) * 20f;

        public static void ApplyGraphics()
        {
            EnsureLoaded();
            Screen.fullScreenMode = DisplayMode switch
            {
                DisplayModeOption.Fullscreen => FullScreenMode.ExclusiveFullScreen,
                DisplayModeOption.BorderlessWindowed => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };
        }

        /// <summary>Applies Reduce Motion to every CameraShake in the current scene (there's
        /// only ever one, on the arena camera, but FindObjectsByType keeps this correct even if
        /// that ever changes). Colorblind filter application is added once the renderer feature
        /// exists.</summary>
        public static void ApplyAccessibility()
        {
            EnsureLoaded();
            foreach (var shake in Object.FindObjectsByType<CameraShake>(FindObjectsSortMode.None))
                shake.shakeMultiplier = ReduceMotion ? 0f : 1f;
        }
    }
}
