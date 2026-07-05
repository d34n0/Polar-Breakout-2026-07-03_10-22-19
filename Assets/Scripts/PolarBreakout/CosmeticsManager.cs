using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Persists the player's chosen cosmetic skins, static and PlayerPrefs-backed like
    /// GameSettings - kept separate from it since this is about appearance/customization
    /// (eventually shop-purchased) rather than player preferences/accessibility. Starts with
    /// just the paddle's skin; the same pattern extends to weapons/bullets/blocks/background
    /// later.
    /// </summary>
    public static class CosmeticsManager
    {
        private const string PaddleSkinIndexKey = "Cosmetics.PaddleSkinIndex";

        public static int PaddleSkinIndex { get; private set; }

        /// <summary>Fired after SetPaddleSkin - anything currently showing the paddle's material
        /// (PaddleController, and PaddleAbilities' cannon barrels via PaddleController.OnSkinApplied)
        /// reacts to this instead of polling.</summary>
        public static event System.Action OnPaddleSkinChanged;

        private static bool _loaded;

        public static void Load()
        {
            PaddleSkinIndex = PlayerPrefs.GetInt(PaddleSkinIndexKey, 0);
            _loaded = true;
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        public static int GetPaddleSkinIndex()
        {
            EnsureLoaded();
            return PaddleSkinIndex;
        }

        public static void SetPaddleSkin(int index)
        {
            EnsureLoaded();
            PaddleSkinIndex = index;
            PlayerPrefs.SetInt(PaddleSkinIndexKey, index);
            PlayerPrefs.Save();
            OnPaddleSkinChanged?.Invoke();
        }
    }
}
