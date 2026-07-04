using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Visual feedback when a brick is destroyed. There's no separate "tier" field on bricks,
    /// so both the screen shake and the particle burst scale off BrickTypeSO.maxHealth instead -
    /// a brick that takes more hits to break is implicitly a harder tier.
    /// </summary>
    public class BrickBreakEffects : MonoBehaviour
    {
        public BrickGridManager brickGridManager;
        public CameraShake cameraShake;
        public ParticleSystem breakParticlesPrefab;

        [Header("Screen Shake")]
        [Tooltip("Trauma added for a 1-health (weakest) brick.")]
        public float baseTrauma = 0.15f;
        [Tooltip("Extra trauma added per point of maxHealth above 1.")]
        public float traumaPerExtraHealth = 0.12f;

        [Header("Particles")]
        [Tooltip("Extra particles in the burst per point of maxHealth above 1, on top of the prefab's own burst count.")]
        public int particlesPerExtraHealth = 4;

        private void OnEnable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed += HandleBrickDestroyed;
        }

        private void OnDisable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed -= HandleBrickDestroyed;
        }

        private void HandleBrickDestroyed(Brick brick)
        {
            int tier = Mathf.Max(1, brick.BrickType.maxHealth);

            if (cameraShake != null)
                cameraShake.AddTrauma(baseTrauma + traumaPerExtraHealth * (tier - 1));

            SpawnBreakParticles(brick, tier);
        }

        private void SpawnBreakParticles(Brick brick, int tier)
        {
            if (breakParticlesPrefab == null) return;

            ParticleSystem instance = Instantiate(breakParticlesPrefab, brick.WorldPosition, Quaternion.identity);

            var main = instance.main;
            main.startColor = brick.BrickType.color;

            int extraParticles = particlesPerExtraHealth * (tier - 1);
            if (extraParticles > 0)
            {
                var emission = instance.emission;
                ParticleSystem.Burst burst = emission.GetBurst(0);
                burst.count = burst.count.constant + extraParticles;
                emission.SetBurst(0, burst);
            }

            instance.Play();
        }
    }
}
