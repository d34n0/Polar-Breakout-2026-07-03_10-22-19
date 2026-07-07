namespace PolarBreakout
{
    /// <summary>How a stage's completion condition is evaluated. Deliberately a plain enum +
    /// switch, not an interface/strategy plugin system - only two cases exist today, and a third
    /// (e.g. a score threshold) can be added later by extending the enum and LevelManager's
    /// switches, without needing to refactor this into an abstraction up front.</summary>
    public enum StageObjectiveType
    {
        /// <summary>Advance once destructible bricks drop to BrickGridManager's soft clear
        /// threshold (see LevelManager.ComputeClearThreshold).</summary>
        Clear,

        /// <summary>Advance once surviveDuration elapses, regardless of remaining bricks. A full
        /// clear before the timer runs out still grants the guaranteed-rare card bonus (see
        /// LevelManager.HandleLevelCleared) but doesn't end the stage early on its own.</summary>
        Survive,
    }
}
