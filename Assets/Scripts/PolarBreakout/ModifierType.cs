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
        /// <summary>Extra bullets fired per barrel per shot (beyond the base 1), fanned out
        /// symmetrically around the barrel's own aim direction - doesn't cost any extra ammo,
        /// a shot is still a shot regardless of how many bullets it actually fires.</summary>
        ExtraBulletsPerBarrel,
        /// <summary>Not a count - any nonzero total means the cannon fires piercing laser
        /// bullets instead of normal ones (see PaddleAbilities.FireBarrel/Bullet.Launch's pierce
        /// parameter). Same "big value flips a threshold-based bool" convention as
        /// PhaseThresholdReduction.</summary>
        LaserBeamEnabled,

        // Multiplicative (1 + sum of values)
        PaddleTurnSpeedMultiplier,
        BallSpeedMultiplier,
        BallSpeedRampMultiplier,
        BulletSpeedMultiplier,
        ScoreMultiplier,
        CapsuleBonusScoreMultiplier,
        ExtraLifeThresholdMultiplier,

        /// <summary>Additive - appended here rather than alongside the other additive entries
        /// above so every existing card asset's serialized `type` integer (a plain enum index)
        /// stays pointed at the same entry. How many extra times a bullet ricochets off a brick
        /// instead of being destroyed on hit (see Bullet.Launch's ricochets parameter) - the
        /// first Ricochet Rounds card grants 1 (one bounce before the bullet is spent), and each
        /// further copy stacks an additional bounce.</summary>
        BulletRicochetBonus,

        /// <summary>Not a count - any nonzero total means a second paddle (see
        /// PaddleAbilities.twinPaddle/RefreshTwinPaddle) is active, mirroring the main paddle's
        /// angle 180 degrees opposite. Same "big value flips a threshold-based bool" convention
        /// as LaserBeamEnabled/PhaseThresholdReduction.</summary>
        TwinPaddleEnabled,

        /// <summary>Additive - appended here rather than alongside the other additive entries
        /// above so every existing card asset's serialized `type` integer stays pointed at the
        /// same entry (same reasoning as BulletRicochetBonus above). Extra seconds added to the
        /// Drone power-up's base duration (see PaddleAbilities.droneDuration) every time a Drone
        /// capsule is caught, whether that's a fresh activation or a refresh of an already-active
        /// Drone.</summary>
        DroneDurationBonus,
        /// <summary>Multiplicative - how much faster the Drone fires at bricks (see
        /// PaddleAbilities.droneFireInterval/DroneController's own fire timer) - e.g. a total of
        /// 0.4 fires 40% more often (a shorter interval), not a flat rate.</summary>
        DroneFireRateMultiplier,
        /// <summary>Additive - extra Drone(s) spawned alongside the first whenever a Drone
        /// capsule activates the power-up fresh (not on a refresh-only recollect while one is
        /// already active) - see PaddleAbilities.SpawnDrones.</summary>
        DroneCountBonus,
    }
}
