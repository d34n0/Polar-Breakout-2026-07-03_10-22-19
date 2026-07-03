using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Tracks the player's remaining lives. A life is only lost once every ball is truly gone
    /// (BallManager.OnAllBallsLost) - losing individual multiball clones along the way doesn't
    /// cost anything, matching how BallManager itself distinguishes the two.
    ///
    /// This intentionally only tracks the count and fires events for it - actual life-lost /
    /// respawn / game-over presentation (animations, restart flow) is left for later work to
    /// hook into OnLivesChanged/OnGameOver rather than being built here.
    /// </summary>
    public class LivesManager : MonoBehaviour
    {
        [Tooltip("Lives the player starts each run with.")]
        public int startingLives = 3;

        public BallManager ballManager;

        public int CurrentLives { get; private set; }
        public bool IsGameOver { get; private set; }

        public event System.Action<int> OnLivesChanged;
        public event System.Action OnGameOver;

        private void Awake()
        {
            CurrentLives = startingLives;
        }

        private void OnEnable()
        {
            if (ballManager != null) ballManager.OnAllBallsLost += HandleAllBallsLost;
        }

        private void OnDisable()
        {
            if (ballManager != null) ballManager.OnAllBallsLost -= HandleAllBallsLost;
        }

        private void HandleAllBallsLost()
        {
            if (IsGameOver) return;

            CurrentLives = Mathf.Max(0, CurrentLives - 1);
            OnLivesChanged?.Invoke(CurrentLives);

            if (CurrentLives <= 0)
            {
                IsGameOver = true;
                OnGameOver?.Invoke();
            }
        }
    }
}
