using System.Collections;
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
        [Tooltip("Optional. Instantiated at the brick's position alongside the particle burst - a " +
                 "small screen-space ripple radiating from the break point. Leave unset for no ripple.")]
        public RippleEffect ripplePrefab;

        [Header("Screen Shake")]
        [Tooltip("Trauma added for a 1-health (weakest) brick.")]
        public float baseTrauma = 0.15f;
        [Tooltip("Extra trauma added per point of maxHealth above 1.")]
        public float traumaPerExtraHealth = 0.12f;

        [Header("Particles")]
        [Tooltip("Extra particles in the burst per point of maxHealth above 1, on top of the prefab's own burst count.")]
        public int particlesPerExtraHealth = 4;

        [Header("Hit Stop")]
        [Tooltip("How long gameplay freezes (Time.timeScale = 0) when a brick is destroyed, in " +
                 "real seconds. Runs on unscaled/realtime waiting so it isn't affected by its own " +
                 "freeze. Set to 0 to disable. If several bricks break within the same window " +
                 "(e.g. an exploding brick's chain reaction), the freeze simply extends rather " +
                 "than stacking multiple overlapping timescale resets.")]
        public float hitStopDuration = 0.1f;

        private Coroutine _hitStopCoroutine;

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
            SpawnRipple(brick);
            TriggerHitStop();
        }

        private void TriggerHitStop()
        {
            if (hitStopDuration <= 0f) return;

            if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
            _hitStopCoroutine = StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(hitStopDuration);
            Time.timeScale = 1f;
            _hitStopCoroutine = null;
        }

        private void SpawnBreakParticles(Brick brick, int tier)
        {
            if (breakParticlesPrefab == null) return;

            ParticleSystem instance = Instantiate(breakParticlesPrefab, brick.WorldPosition, Quaternion.identity);

            var main = instance.main;
            main.startColor = GetOpaqueTint(brick);

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

        private void SpawnRipple(Brick brick)
        {
            if (ripplePrefab == null) return;

            RippleEffect instance = Instantiate(ripplePrefab, brick.WorldPosition, Quaternion.identity);
            instance.Play(GetOpaqueTint(brick));
        }

        // Force full opacity - BrickTypeSO.color's alpha channel is meaningless for the brick's
        // own opaque mesh material, so some brick types leave it at 0. Left as-is, that alpha
        // would carry straight into the particle/ripple's own alpha-blended shaders and make the
        // whole effect invisible.
        private static Color GetOpaqueTint(Brick brick)
        {
            Color tint = brick.BrickType.color;
            tint.a = 1f;
            return tint;
        }
    }
}
