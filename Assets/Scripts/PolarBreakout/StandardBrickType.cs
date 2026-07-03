using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// A plain brick with no special behavior - just health, score, and color/sprite
    /// inherited from the base class. Use this as a template for new brick types.
    /// </summary>
    [CreateAssetMenu(fileName = "StandardBrickType", menuName = "PolarBreakout/Brick Types/Standard")]
    public class StandardBrickType : BrickTypeSO
    {
        // Intentionally empty - relies entirely on base BrickTypeSO behavior.
    }
}
