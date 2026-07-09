using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Scales this transform up from zero to its own authored localScale over duration seconds,
    /// shaped by curve - author the curve to rise past 1 before settling back to exactly 1 (the
    /// default does this: overshoots to 1.15 at 60% of the way through, then eases back to 1.0 by
    /// the end) for a "pops open, springs back" reveal. Captures its own localScale as the target
    /// in Awake, so whatever scale is already authored on the object in the Editor is preserved as
    /// the settled size - no separate target-scale field to keep in sync by hand.
    ///
    /// Also drives any child ParticleSystems through the same reveal: Unity particle systems do
    /// NOT scale their rendered particle size with the transform (only position/velocity respect
    /// "Local" scaling mode, size never does), so a mesh-only transform.localScale animation would
    /// leave attached particle effects sitting at full constant size the instant they start
    /// emitting - which also happens immediately on scene load via each system's own playOnAwake,
    /// well before this component's own reveal is ever triggered. To fix both problems together,
    /// this stops every child ParticleSystem (clearing anything already emitted) in Awake so
    /// nothing renders until Play() is actually called, then re-starts them in Play() and rewrites
    /// every currently-alive particle's size in lockstep with the same curve each frame of Animate.
    /// </summary>
    public class ScaleInOvershoot : MonoBehaviour
    {
        [Tooltip("How long the whole scale-in takes, seconds.")]
        public float duration = 0.6f;
        [Tooltip("Maps elapsed/duration (0-1) to a multiplier on the object's own authored scale - " +
                 "author this to rise past 1 before settling back to exactly 1 for an overshoot " +
                 "'pop' reveal. Defaults to a gentle overshoot to ~1.15 at 60% before easing back.")]
        public AnimationCurve curve = DefaultOvershootCurve();
        [Tooltip("Plays automatically on Start. Leave off to trigger manually via Play() instead - " +
                 "e.g. timed to a level's build-in sequence rather than whenever this object first " +
                 "activates.")]
        public bool playOnStart = true;

        private Vector3 _targetScale;
        private Coroutine _routine;
        private ParticleSystem[] _particleSystems;
        private float[] _particleBaseSizes;
        private ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[64];

        private void Awake()
        {
            _targetScale = transform.localScale;
            // Hidden immediately regardless of playOnStart, so an object with playOnStart false
            // (triggered externally instead, e.g. BallManager.deathZonePortal) doesn't sit fully
            // visible at its authored scale until Play() eventually fires - it should read as "not
            // there yet" the whole time in between, not visible-then-suddenly-invisible-then-growing.
            transform.localScale = Vector3.zero;

            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            _particleBaseSizes = new float[_particleSystems.Length];
            for (int i = 0; i < _particleSystems.Length; i++)
            {
                _particleBaseSizes[i] = _particleSystems[i].main.startSize.constant;
                // Stop-and-clear before this system's own playOnAwake gets a chance to emit at full
                // size on the very first frame - Play() below is what actually starts it, at the
                // right moment, sized from zero.
                _particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Start()
        {
            if (playOnStart) Play();
        }

        /// <summary>(Re)starts the scale-in from zero - safe to call again mid-animation.</summary>
        public void Play()
        {
            if (_routine != null) StopCoroutine(_routine);
            transform.localScale = Vector3.zero;
            foreach (var ps in _particleSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
                // Force an immediate particle rather than waiting on the system's own emission
                // rate - several of these systems emit as slowly as ~1/sec, so without this the
                // reveal's whole duration could pass with zero live particles for
                // ApplyParticleSizeMultiplier to resize, leaving nothing visible until well after
                // the mesh has already finished scaling up.
                ps.Emit(1);
            }
            _routine = StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            while (elapsed < safeDuration)
            {
                // Unscaled so this still plays correctly even if something pauses Time.timeScale
                // right at game start (matches DissolveEffect/HexWipeTransition's own reasoning).
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float multiplier = curve.Evaluate(t);
                transform.localScale = _targetScale * multiplier;
                ApplyParticleSizeMultiplier(multiplier);
                yield return null;
            }
            transform.localScale = _targetScale;
            // Left alone from here on - newly emitted particles just use their own authored
            // Constant start size (1x), which is exactly the settled look this animates towards.
            _routine = null;
        }

        /// <summary>Rewrites every currently-alive particle's size to baseSize * multiplier, on
        /// every child ParticleSystem - the only way to make particle size actually track this
        /// reveal, since transform.localScale never affects rendered particle size in Unity.</summary>
        private void ApplyParticleSizeMultiplier(float multiplier)
        {
            for (int s = 0; s < _particleSystems.Length; s++)
            {
                var ps = _particleSystems[s];
                int count = ps.particleCount;
                if (count == 0) continue;
                if (_particleBuffer.Length < count) _particleBuffer = new ParticleSystem.Particle[count];

                int written = ps.GetParticles(_particleBuffer);
                float size = _particleBaseSizes[s] * multiplier;
                for (int i = 0; i < written; i++)
                    _particleBuffer[i].startSize = size;
                ps.SetParticles(_particleBuffer, written);
            }
        }

        private static AnimationCurve DefaultOvershootCurve()
        {
            var c = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.6f, 1.15f),
                new Keyframe(1f, 1f));
            c.SmoothTangents(0, 0.3f);
            c.SmoothTangents(1, 0.3f);
            c.SmoothTangents(2, 0.3f);
            return c;
        }
    }
}
