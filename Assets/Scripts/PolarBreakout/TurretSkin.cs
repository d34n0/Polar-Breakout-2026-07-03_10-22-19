using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One selectable look for the paddle's cannon turrets - a sprite (turrets are purely
    /// cosmetic, no collider, so they're free to be actual 2D artwork rather than a procedural
    /// mesh) plus an optional material override for a custom shader effect. Parallel to
    /// PaddleSkin, kept as its own type since turrets are sprite-first while the paddle itself
    /// stays a shaded procedural mesh.
    /// </summary>
    [CreateAssetMenu(fileName = "TurretSkin", menuName = "Polar Breakout/Turret Skin")]
    public class TurretSkin : ScriptableObject
    {
        public string displayName;
        public Sprite sprite;
        [Tooltip("Optional. Overrides the default sprite material, so a turret skin can use a " +
                 "custom shader/effect instead of plain sprite rendering. Leave unset for " +
                 "standard sprite rendering.")]
        public Material materialOverride;
    }
}
