using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One selectable look for the paddle (and, via PaddleAbilities' cannon barrels, its
    /// turrets) - just a Material plus a display name for now. Deliberately minimal: the
    /// foundation for a future shop system covering the ship/weapons/bullets/blocks/background,
    /// starting here with the paddle and turrets only.
    /// </summary>
    [CreateAssetMenu(fileName = "PaddleSkin", menuName = "Polar Breakout/Paddle Skin")]
    public class PaddleSkin : ScriptableObject
    {
        public string displayName;
        public Material material;
    }
}
