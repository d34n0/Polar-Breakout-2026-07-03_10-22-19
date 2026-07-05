using UnityEngine;

namespace PolarBreakout
{
    [System.Serializable]
    public struct CardEffect
    {
        public ModifierType type;
        [Tooltip("Additive types: raw amount (degrees/count/seconds/units). Multiplicative " +
                 "types: a fraction like 0.15 for +15% - see ModifierType.")]
        public float value;
    }
}
