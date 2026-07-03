using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Drops a specific power-up capsule when destroyed. The type is fixed per asset (rather
    /// than randomized) so a level designer can place a dedicated "Cannon brick" vs a
    /// "Multiball brick" exactly where they want it with the level painter.
    /// </summary>
    [CreateAssetMenu(fileName = "PowerUpBrickType", menuName = "PolarBreakout/Brick Types/Power-Up")]
    public class PowerUpBrickType : BrickTypeSO
    {
        [Header("Power-Up")]
        public PowerUpType powerUpType;

        public override void OnDestroyed(Brick brick)
        {
            var capsuleObject = new GameObject($"PowerUpCapsule_{powerUpType}");
            var capsule = capsuleObject.AddComponent<PowerUpCapsule>();
            capsule.Initialize(brick.WorldPosition, powerUpType);
        }
    }
}
