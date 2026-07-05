namespace PolarBreakout
{
    /// <summary>
    /// Every stat a Card can modify. Each is either "additive" (raw values from every acquired
    /// card summed directly - degrees, counts, seconds, world units) or "multiplicative" (each
    /// card's value is a fraction like 0.15 for +15%; RunModifiers sums them and applies the
    /// result once as 1 + total, so ten +10% cards add up to +100%, not 1.1^10 - see
    /// RunModifiers.GetMultiplier). Which convention a type uses is fixed by whichever consumer
    /// reads it - see PaddleController, BallController, PaddleAbilities, ScoreManager.
    /// </summary>
    public enum ModifierType
    {
        // Additive
        PaddleAngularWidthBonus,
        CannonAmmoBonus,
        TurretSpacingBonus,
        AutopilotDurationBonus,
        PhaseThresholdReduction,

        // Multiplicative (1 + sum of values)
        PaddleTurnSpeedMultiplier,
        BallSpeedMultiplier,
        BallSpeedRampMultiplier,
        BulletSpeedMultiplier,
        ScoreMultiplier,
        CapsuleBonusScoreMultiplier,
        ExtraLifeThresholdMultiplier,
    }
}
