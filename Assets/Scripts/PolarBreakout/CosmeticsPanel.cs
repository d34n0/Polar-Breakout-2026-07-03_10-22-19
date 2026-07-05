using UnityEngine;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Options menu selector for the paddle's skin - a minimal stand-in for the eventual shop
    /// screen, just enough to pick among whatever PaddleSkin assets the paddle has been given
    /// (see PaddleController.availableSkins). Toggles are read generically by index rather than
    /// one named handler per skin, since a future shop will likely offer more than a handful.
    /// </summary>
    public class CosmeticsPanel : MonoBehaviour
    {
        [Tooltip("One toggle per available paddle skin, same order as PaddleController.availableSkins.")]
        public Toggle[] skinToggles;

        private void OnEnable()
        {
            SetToggleForCurrentSkin();
            foreach (var toggle in skinToggles)
                toggle.onValueChanged.AddListener(OnAnyToggleChanged);
        }

        private void OnDisable()
        {
            foreach (var toggle in skinToggles)
                toggle.onValueChanged.RemoveListener(OnAnyToggleChanged);
        }

        private void OnAnyToggleChanged(bool isOn)
        {
            if (!isOn) return;

            for (int i = 0; i < skinToggles.Length; i++)
            {
                if (skinToggles[i].isOn)
                {
                    CosmeticsManager.SetPaddleSkin(i);
                    return;
                }
            }
        }

        private void SetToggleForCurrentSkin()
        {
            int current = CosmeticsManager.GetPaddleSkinIndex();
            for (int i = 0; i < skinToggles.Length; i++)
                skinToggles[i].SetIsOnWithoutNotify(i == current);
        }
    }
}
