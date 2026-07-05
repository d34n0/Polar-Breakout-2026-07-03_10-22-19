using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Attach to any TMP text that should respond to the Text Size accessibility setting.
    /// Caches the authored font size once, then rescales relative to that base whenever
    /// GameSettings changes - never compounds on repeated changes, unlike multiplying the
    /// current size in place would.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ScalableText : MonoBehaviour
    {
        private TextMeshProUGUI _text;
        private float _baseFontSize;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            _baseFontSize = _text.fontSize;
        }

        private void OnEnable()
        {
            GameSettings.OnSettingsChanged += Rescale;
            Rescale();
        }

        private void OnDisable()
        {
            GameSettings.OnSettingsChanged -= Rescale;
        }

        private void Rescale()
        {
            _text.fontSize = _baseFontSize * GameSettings.TextSizeMultiplier;
        }
    }
}
