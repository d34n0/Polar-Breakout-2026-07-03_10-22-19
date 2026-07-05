using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Persists the player's chosen cosmetic skins, static and PlayerPrefs-backed like
    /// GameSettings - kept separate from it since this is about appearance/customization
    /// (eventually shop-purchased) rather than player preferences/accessibility. Paddle and
    /// turret skins are tracked independently (turrets no longer just mirror the paddle's look);
    /// the same pattern extends to bullets/blocks/background later.
    /// </summary>
    public static class CosmeticsManager
    {
        private const string PaddleSkinIndexKey = "Cosmetics.PaddleSkinIndex";
        private const string TurretSkinIndexKey = "Cosmetics.TurretSkinIndex";

        public static int PaddleSkinIndex { get; private set; }
        public static int TurretSkinIndex { get; private set; }

        /// <summary>Fired after SetPaddleSkin - PaddleController reacts to this instead of polling.</summary>
        public static event System.Action OnPaddleSkinChanged;
        /// <summary>Fired after SetTurretSkin - PaddleAbilities' cannon barrels react to this
        /// instead of polling.</summary>
        public static event System.Action OnTurretSkinChanged;

        private static bool _loaded;

        public static void Load()
        {
            PaddleSkinIndex = PlayerPrefs.GetInt(PaddleSkinIndexKey, 0);
            TurretSkinIndex = PlayerPrefs.GetInt(TurretSkinIndexKey, 0);
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

        public static int GetTurretSkinIndex()
        {
            EnsureLoaded();
            return TurretSkinIndex;
        }

        public static void SetTurretSkin(int index)
        {
            EnsureLoaded();
            TurretSkinIndex = index;
            PlayerPrefs.SetInt(TurretSkinIndexKey, index);
            PlayerPrefs.Save();
            OnTurretSkinChanged?.Invoke();
        }
    }
}
