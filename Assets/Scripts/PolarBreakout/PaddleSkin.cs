using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One selectable look for the paddle (and, via PaddleAbilities' cannon barrels, its
    /// turrets) - a Material applied to the paddle's own procedural arc mesh. Using the arc
    /// mesh's own material (rather than an overlay sprite) guarantees the visual always exactly
    /// matches the paddle's true collision shape/curvature - a textured material (e.g. a
    /// spaceship hull painted on via the arc mesh's existing arc-length x radial UV mapping)
    /// reads as a proper part of the same curved shape every other skin uses. Anything that
    /// should extend past the arc's own silhouette (wings, turrets, etc.) is a separate child
    /// sprite instead - see PaddleAbilities' cannon barrels for the established pattern.
    /// Foundation for a future shop system covering the ship/weapons/bullets/blocks/background,
    /// starting here with the paddle and turrets only.
    /// </summary>
    [CreateAssetMenu(fileName = "PaddleSkin", menuName = "Polar Breakout/Paddle Skin")]
    public class PaddleSkin : ScriptableObject
    {
        public string displayName;
        public Material material;
    }
}
