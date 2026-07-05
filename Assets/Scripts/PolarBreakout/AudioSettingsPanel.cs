using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using TMPro;

namespace PolarBreakout
{
    /// <summary>
    /// Binds the Audio category's UI (3 volume sliders + Text Size toggles) to GameSettings.
    /// The AudioMixer field is optional - if left unset, the sliders still update GameSettings/
    /// PlayerPrefs but there's no mixer to push volume into yet; assign your mixer asset here
    /// once you've created it via Window > Audio > Audio Mixer.
    /// </summary>
    public class AudioSettingsPanel : MonoBehaviour
    {
        [Tooltip("Optional. Your MainMixer.mixer asset - created via Window > Audio > Audio " +
                 "Mixer with Master/Music/SFX groups, each exposing a volume parameter named " +
                 "MasterVolume/MusicVolume/SFXVolume. Leave unset and sliders still update " +
                 "GameSettings/PlayerPrefs, just with nothing to push volume into yet.")]
        public AudioMixer mixer;

        [Header("Volume Sliders")]
        public Slider masterSlider;
        public Slider musicSlider;
        public Slider sfxSlider;
        public TextMeshProUGUI masterValueText;
        public TextMeshProUGUI musicValueText;
        public TextMeshProUGUI sfxValueText;

        [Header("Text Size")]
        public Toggle smallTextToggle;
        public Toggle normalTextToggle;
        public Toggle largeTextToggle;

        private const float SmallMultiplier = 0.85f;
        private const float NormalMultiplier = 1f;
        private const float LargeMultiplier = 1.25f;

        private void OnEnable()
        {
            if (mixer != null) GameSettings.SetAudioMixer(mixer);

            masterSlider.value = GameSettings.MasterVolume;
            musicSlider.value = GameSettings.MusicVolume;
            sfxSlider.value = GameSettings.SFXVolume;
            UpdateValueLabels();
            SetToggleForCurrentMultiplier();

            masterSlider.onValueChanged.AddListener(OnMasterChanged);
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
            sfxSlider.onValueChanged.AddListener(OnSFXChanged);
            smallTextToggle.onValueChanged.AddListener(OnSmallToggled);
            normalTextToggle.onValueChanged.AddListener(OnNormalToggled);
            largeTextToggle.onValueChanged.AddListener(OnLargeToggled);
        }

        private void OnDisable()
        {
            masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
            musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
            sfxSlider.onValueChanged.RemoveListener(OnSFXChanged);
            smallTextToggle.onValueChanged.RemoveListener(OnSmallToggled);
            normalTextToggle.onValueChanged.RemoveListener(OnNormalToggled);
            largeTextToggle.onValueChanged.RemoveListener(OnLargeToggled);
        }

        private void OnMasterChanged(float value)
        {
            GameSettings.MasterVolume = value;
            GameSettings.ApplyAudio();
            GameSettings.Save();
            UpdateValueLabels();
        }

        private void OnMusicChanged(float value)
        {
            GameSettings.MusicVolume = value;
            GameSettings.ApplyAudio();
            GameSettings.Save();
            UpdateValueLabels();
        }

        private void OnSFXChanged(float value)
        {
            GameSettings.SFXVolume = value;
            GameSettings.ApplyAudio();
            GameSettings.Save();
            UpdateValueLabels();
        }

        private void OnSmallToggled(bool isOn) { if (isOn) SetTextSize(SmallMultiplier); }
        private void OnNormalToggled(bool isOn) { if (isOn) SetTextSize(NormalMultiplier); }
        private void OnLargeToggled(bool isOn) { if (isOn) SetTextSize(LargeMultiplier); }

        private void SetTextSize(float multiplier)
        {
            GameSettings.TextSizeMultiplier = multiplier;
            GameSettings.Save();
        }

        private void SetToggleForCurrentMultiplier()
        {
            float m = GameSettings.TextSizeMultiplier;
            smallTextToggle.SetIsOnWithoutNotify(Mathf.Approximately(m, SmallMultiplier));
            normalTextToggle.SetIsOnWithoutNotify(Mathf.Approximately(m, NormalMultiplier) || (!Mathf.Approximately(m, SmallMultiplier) && !Mathf.Approximately(m, LargeMultiplier)));
            largeTextToggle.SetIsOnWithoutNotify(Mathf.Approximately(m, LargeMultiplier));
        }

        private void UpdateValueLabels()
        {
            if (masterValueText != null) masterValueText.text = Mathf.RoundToInt(masterSlider.value * 100f) + "%";
            if (musicValueText != null) musicValueText.text = Mathf.RoundToInt(musicSlider.value * 100f) + "%";
            if (sfxValueText != null) sfxValueText.text = Mathf.RoundToInt(sfxSlider.value * 100f) + "%";
        }
    }
}
