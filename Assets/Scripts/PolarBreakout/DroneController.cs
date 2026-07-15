using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawned by PaddleAbilities when a Drone power-up activates. Hovers near the paddle at a
    /// radius just outside its orbit (so it always stays farther from the arena center - the
    /// black hole - than the paddle itself, never drifting inward), auto-fires Bullet.Launch
    /// shots at the nearest destructible brick (falling back to an active boss if no bricks
    /// remain), and phases out via DissolveEffect once PaddleAbilities.BeginPhaseOut is called
    /// (the Drone power-up's duration has run out). Purely a hovering shooter - no Rigidbody2D/
    /// Collider2D, since it never needs to physically collide with anything itself, only fire
    /// projectiles (matching PaddleAbilities.BuildCannonVisual's cosmetic-sprite-only turrets).
    /// </summary>
    public class DroneController : MonoBehaviour
    {
        private PaddleController _paddle;
        private AudioManager _audioManager;
        private Material _dissolveMaterial;
        private Material _bulletMaterial;
        private SpriteRenderer _renderer;

        private float _hoverRadiusOffset;
        private float _hoverOrbitPhaseDegrees;
        private float _hoverWobbleAmplitudeDegrees;
        private float _hoverWobbleSpeed;
        private float _hoverFollowSpeed;
        private float _fireInterval;
        private float _bulletSpeed;
        private float _visualRadius;

        private float _hoverWobbleTime;
        private float _fireTimer;
        private bool _isPhasingOut;

        /// <param name="orbitPhaseOffsetDegrees">Spreads multiple simultaneous drones (see
        /// ModifierType.DroneCountBonus) apart around the paddle rather than stacking exactly on
        /// top of one another.</param>
        public void Initialize(PaddleController paddle, AudioManager audioManager, Material dissolveMaterial,
            Sprite sprite, Color tintColor, float visualRadius, float hoverRadiusOffset, float orbitPhaseOffsetDegrees,
            float hoverWobbleAmplitudeDegrees, float hoverWobbleSpeed, float hoverFollowSpeed,
            float fireInterval, float bulletSpeed, Material bulletMaterial)
        {
            _paddle = paddle;
            _audioManager = audioManager;
            _dissolveMaterial = dissolveMaterial;
            _bulletMaterial = bulletMaterial;

            _visualRadius = Mathf.Max(0.01f, visualRadius);
            _hoverRadiusOffset = hoverRadiusOffset;
            _hoverOrbitPhaseDegrees = orbitPhaseOffsetDegrees;
            _hoverWobbleAmplitudeDegrees = hoverWobbleAmplitudeDegrees;
            _hoverWobbleSpeed = hoverWobbleSpeed;
            _hoverFollowSpeed = Mathf.Max(0.01f, hoverFollowSpeed);
            _fireInterval = Mathf.Max(0.01f, fireInterval);
            _bulletSpeed = bulletSpeed;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite != null ? sprite : BuildFallbackCircleSprite();
            _renderer.color = tintColor;
            // A small negative local Z, same trick BuildCannonVisual uses, so the drone reliably
            // draws in front of the paddle/bricks instead of leaving draw order to chance.
            var pos = transform.position;
            pos.z = -0.02f;
            transform.position = pos;

            ApplyVisualScale();

            // Snap straight to the target hover spot on spawn instead of chasing from the arena
            // origin - MoveTowards below would otherwise visibly fly the drone in from (0,0) on
            // its very first frame.
            transform.position = ComputeTargetHoverPosition();
        }

        /// <summary>Scales the sprite so it renders at visualRadius world-unit radius regardless
        /// of whatever sprite was assigned (or the generated fallback's own native size).</summary>
        private void ApplyVisualScale()
        {
            float nativeRadius = _renderer.sprite.bounds.extents.x;
            float scale = nativeRadius > 0.0001f ? _visualRadius / nativeRadius : 1f;
            transform.localScale = Vector3.one * scale;
        }

        /// <summary>Builds a small soft-edged white circle texture and wraps it in a Sprite -
        /// used when no droneSprite is assigned on PaddleAbilities, so the drone is always
        /// visible out of the box rather than silently rendering nothing (the exact bug a null
        /// SpriteRenderer.sprite caused elsewhere in this project).</summary>
        private static Sprite BuildFallbackCircleSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - dist + 1.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void Update()
        {
            if (_paddle == null)
            {
                Destroy(gameObject);
                return;
            }

            if (_isPhasingOut) return;

            _hoverWobbleTime += Time.deltaTime * _hoverWobbleSpeed;
            transform.position = Vector2.MoveTowards(transform.position, ComputeTargetHoverPosition(),
                _hoverFollowSpeed * Time.deltaTime);

            TickFireTimer();
        }

        /// <summary>Hover radius is always paddleOrbitRadius + hoverRadiusOffset - strictly
        /// farther from the arena center than the paddle itself, so the drone can never drift
        /// toward the black hole regardless of the wobble below. Angle tracks the paddle's own
        /// current angle (plus this drone's own orbit phase, for when more than one is active)
        /// with a gentle sine wobble layered on top so it reads as hovering rather than rigidly
        /// locked in place.</summary>
        private Vector2 ComputeTargetHoverPosition()
        {
            float wobbleDegrees = Mathf.Sin(_hoverWobbleTime) * _hoverWobbleAmplitudeDegrees;
            float targetAngleDegrees = _paddle.CurrentAngleDegrees + _hoverOrbitPhaseDegrees + wobbleDegrees;
            float targetRadius = _paddle.settings.paddleOrbitRadius + _hoverRadiusOffset;

            float rad = targetAngleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * targetRadius;
        }

        private void TickFireTimer()
        {
            _fireTimer += Time.deltaTime;
            if (_fireTimer < _fireInterval) return;
            if (!TryFindNearestTargetPosition(out Vector2 targetPosition)) return;

            _fireTimer = 0f;
            Fire(targetPosition);
        }

        /// <summary>Nearest non-destroyed, non-indestructible brick by straight-line distance
        /// from the drone's own current position - falls back to an active, undefeated boss (if
        /// any) once no such brick remains, so the drone still has something useful to do during
        /// a boss stage instead of just idling.</summary>
        private bool TryFindNearestTargetPosition(out Vector2 position)
        {
            position = Vector2.zero;
            bool found = false;
            float bestSqrDistance = float.MaxValue;

            foreach (var brick in Object.FindObjectsByType<Brick>(FindObjectsSortMode.None))
            {
                if (brick.IsDestroyed || brick.BrickType == null || brick.BrickType.isIndestructible) continue;

                Vector2 brickPos = brick.WorldPosition;
                float sqrDistance = ((Vector2)transform.position - brickPos).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    position = brickPos;
                    found = true;
                }
            }
            if (found) return true;

            var boss = Object.FindFirstObjectByType<BossController>();
            if (boss != null && !boss.IsDefeated)
            {
                position = boss.transform.position;
                return true;
            }

            return false;
        }

        /// <summary>Fires a single straight-line Bullet at targetPosition (a snapshot, not a
        /// homing shot - matching how every other bullet in this project already flies), exactly
        /// mirroring PaddleAbilities.FireBarrel's own ball-collision-ignore setup so drone shots
        /// pass through any ball in play instead of physically bouncing off it.</summary>
        private void Fire(Vector2 targetPosition)
        {
            Vector2 toTarget = targetPosition - (Vector2)transform.position;
            if (toTarget.sqrMagnitude < 0.0001f) return;
            float angleDegrees = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;

            var bulletObject = new GameObject("DroneBullet");
            var bullet = bulletObject.AddComponent<Bullet>();
            bullet.Launch(transform.position, angleDegrees, _bulletSpeed, _paddle.settings, _bulletMaterial);
            _audioManager?.PlayBullet();

            var bulletCollider = bulletObject.GetComponent<Collider2D>();
            foreach (var ball in Object.FindObjectsByType<BallController>(FindObjectsSortMode.None))
            {
                var ballCollider = ball.GetComponent<Collider2D>();
                if (ballCollider != null) Physics2D.IgnoreCollision(bulletCollider, ballCollider, true);
            }
        }

        /// <summary>Starts the drone's dissolve-out-then-destroy sequence - called by
        /// PaddleAbilities once the Drone power-up's duration has fully run out. Safe to call
        /// more than once; only the first call actually starts anything.</summary>
        public void BeginPhaseOut(float duration)
        {
            if (_isPhasingOut) return;
            _isPhasingOut = true;
            StartCoroutine(PhaseOutRoutine(duration));
        }

        private IEnumerator PhaseOutRoutine(float duration)
        {
            var dissolve = gameObject.AddComponent<DissolveEffect>();
            dissolve.dissolveMaterial = _dissolveMaterial;
            dissolve.audioManager = _audioManager;
            yield return dissolve.DissolveOut(duration);
            Destroy(gameObject);
        }
    }
}
