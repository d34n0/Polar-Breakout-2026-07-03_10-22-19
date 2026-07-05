using UnityEngine;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Options menu selector for the paddle's and turrets' skins - a minimal stand-in for the
    /// eventual shop screen, just enough to pick among whatever PaddleSkin/TurretSkin assets have
    /// been assigned (see PaddleController.availableSkins / PaddleAbilities.availableTurretSkins).
    /// Toggles are read generically by index rather than one named handler per skin, since a
    /// future shop will likely offer more than a handful of each.
    /// </summary>
    public class CosmeticsPanel : MonoBehaviour
    {
        [Tooltip("One toggle per available paddle skin, same order as PaddleController.availableSkins.")]
        public Toggle[] paddleSkinToggles;
        [Tooltip("One toggle per available turret skin, same order as PaddleAbilities.availableTurretSkins.")]
        public Toggle[] turretSkinToggles;

        private void OnEnable()
        {
            SetToggleForCurrentSkin(paddleSkinToggles, CosmeticsManager.GetPaddleSkinIndex());
            SetToggleForCurrentSkin(turretSkinToggles, CosmeticsManager.GetTurretSkinIndex());

            foreach (var toggle in paddleSkinToggles) toggle.onValueChanged.AddListener(OnAnyPaddleToggleChanged);
            foreach (var toggle in turretSkinToggles) toggle.onValueChanged.AddListener(OnAnyTurretToggleChanged);
        }

        private void OnDisable()
        {
            foreach (var toggle in paddleSkinToggles) toggle.onValueChanged.RemoveListener(OnAnyPaddleToggleChanged);
            foreach (var toggle in turretSkinToggles) toggle.onValueChanged.RemoveListener(OnAnyTurretToggleChanged);
        }

        private void OnAnyPaddleToggleChanged(bool isOn)
        {
            int index = FindActiveIndex(paddleSkinToggles, isOn);
            if (index >= 0) CosmeticsManager.SetPaddleSkin(index);
        }

        private void OnAnyTurretToggleChanged(bool isOn)
        {
            int index = FindActiveIndex(turretSkinToggles, isOn);
            if (index >= 0) CosmeticsManager.SetTurretSkin(index);
        }

        private static int FindActiveIndex(Toggle[] toggles, bool isOn)
        {
            if (!isOn) return -1;

            for (int i = 0; i < toggles.Length; i++)
                if (toggles[i].isOn) return i;
            return -1;
        }

        private static void SetToggleForCurrentSkin(Toggle[] toggles, int current)
        {
            for (int i = 0; i < toggles.Length; i++)
                toggles[i].SetIsOnWithoutNotify(i == current);
        }
    }
}
