using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Shows/hides and updates a bullet-count label for the Cannon power-up, driven entirely by
    /// PaddleAbilities.OnCannonAmmoChanged - visible only while CannonAmmo is above 0, same
    /// display-layer spirit as SurviveTimerController/HUDController.
    /// </summary>
    public class CannonAmmoUIController : MonoBehaviour
    {
        public PaddleAbilities paddleAbilities;
        public GameObject ammoRoot;
        public TextMeshProUGUI ammoText;

        private void OnEnable()
        {
            if (paddleAbilities != null) paddleAbilities.OnCannonAmmoChanged += HandleCannonAmmoChanged;
        }

        private void OnDisable()
        {
            if (paddleAbilities != null) paddleAbilities.OnCannonAmmoChanged -= HandleCannonAmmoChanged;
        }

        private void Start()
        {
            // Show the correct starting state immediately instead of waiting for the first change.
            if (paddleAbilities != null) HandleCannonAmmoChanged(paddleAbilities.CannonAmmo);
        }

        private void HandleCannonAmmoChanged(int ammo)
        {
            if (ammoRoot != null) ammoRoot.SetActive(ammo > 0);
            if (ammoText != null) ammoText.text = "Bullets: " + ammo;
        }
    }
}
