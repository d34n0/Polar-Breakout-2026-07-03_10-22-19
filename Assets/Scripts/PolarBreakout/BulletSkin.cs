using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One selectable kind of cannon bullet - referenced by TurretSkin.bulletSkin, so each turret
    /// look can fire its own distinct-looking (and optionally distinct-feeling) shot rather than
    /// every turret sharing the same bolt. Parallel to PaddleSkin/TurretSkin: a plain data asset,
    /// applied by PaddleAbilities at fire time rather than baked into Bullet itself.
    /// </summary>
    [CreateAssetMenu(fileName = "BulletSkin", menuName = "Polar Breakout/Bullet Skin")]
    public class BulletSkin : ScriptableObject
    {
        public string displayName;
        [Tooltip("Tint applied to the bullet's default procedural bolt. Ignored if " +
                 "materialOverride is set.")]
        public Color color = new Color(1f, 0.95f, 0.4f);
        [Tooltip("Optional. Overrides the bullet's default procedural material entirely, so a " +
                 "bullet skin can use a custom shader/texture instead of a plain tinted bolt. " +
                 "Leave unset to use the default bolt with 'color' above.")]
        public Material materialOverride;
        [Tooltip("Multiplies PaddleAbilities.bulletSpeed for this bullet skin - 1 = no change, " +
                 "so different turrets can fire faster/slower shots.")]
        public float speedMultiplier = 1f;
    }
}
