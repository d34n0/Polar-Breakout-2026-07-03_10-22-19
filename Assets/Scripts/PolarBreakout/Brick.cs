using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Runtime instance of a brick placed by BrickGridManager. Every hex brick at a given
    /// hexSize/hexGap is congruent, so unlike the old arc-segment bricks this doesn't build its
    /// own unique mesh/collider outline - BrickGridManager builds one shared mesh and outline once
    /// and hands them to every Brick instance. Still holds per-instance state (current health).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class Brick : MonoBehaviour
    {
        [Tooltip("How long the hit-flash (see BrickTypeSO.hitFlashColor) lasts, in seconds, for both a surviving hit and the final destroying hit. " +
                 "Overridden automatically for ExplodingBrickType bricks (see Initialize) to match their fuseDuration.")]
        public float flashDuration = 0.3f;//0.08f;
        [Tooltip("How fast the destroying hit-flash blinks between hitFlashColor and the brick's own color, seconds per toggle.")]
        public float blinkIntervalSeconds = 0.06f;

        public HexCoordinate Coordinate { get; private set; }
        public BrickTypeSO BrickType { get; private set; }
        public int CurrentHealth;

        /// <summary>True from the moment this brick is destroyed, even though the actual
        /// Destroy(gameObject) call is deferred to end of frame. Callers that might touch a
        /// brick more than once in the same frame (e.g. an exploding brick's blast radius
        /// overlapping another explosion) should check this instead of relying on nullness,
        /// since the brick is still a live object with stale health until then.</summary>
        public bool IsDestroyed { get; private set; }

        public PolarGridSettings Settings { get; private set; }
        public BrickGridManager Manager => _manager;
        public Vector2 WorldPosition => Settings.HexToWorld(Coordinate);

        private BrickGridManager _manager;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propBlock;
        private PolygonCollider2D _collider;
        private Coroutine _flashCoroutine;
        private Material _normalMaterial;

        public void Initialize(BrickGridManager manager, PolarGridSettings settings, HexCoordinate coord, BrickTypeSO type,
                                Mesh sharedHexMesh, Vector2[] sharedHexOutline)
        {
            _manager = manager;
            Settings = settings;
            Coordinate = coord;
            BrickType = type;
            CurrentHealth = type.maxHealth;

            // An exploding brick's fuse (the delay before it detonates and chains into
            // neighbors - see ExplodingBrickType.OnFlashComplete) is driven by this same
            // flashDuration/DestroyAfterFlash timer, so the visual flash and the moment it
            // actually explodes always line up as one single, consistent delay.
            if (type is ExplodingBrickType exploding) flashDuration = exploding.fuseDuration;

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = sharedHexMesh;

            _collider = GetComponent<PolygonCollider2D>();
            _collider.pathCount = 1;
            _collider.SetPath(0, sharedHexOutline);

            _renderer = GetComponent<MeshRenderer>();
            // Falls back to whatever material Brick.prefab already has (shared across every
            // brick type by default) unless this specific type opts into its own shader.
            if (type.materialOverride != null) _renderer.sharedMaterial = type.materialOverride;
            // Snapshot whatever material is actually showing now - Hit()/DestroyAfterFlash swap
            // to hitFlashMaterial and need to know exactly what to restore afterward.
            _normalMaterial = _renderer.sharedMaterial;
            UpdateVisualColor();
        }

        // Darkens the brick's color as it takes damage, so multi-hit bricks visibly show
        // how close they are to breaking instead of looking identical until they vanish.
        private void UpdateVisualColor()
        {
            float healthFraction = BrickType.maxHealth > 0
                ? Mathf.Clamp01((float)CurrentHealth / BrickType.maxHealth)
                : 1f;
            Color tinted = Color.Lerp(Color.black, BrickType.color, Mathf.Lerp(0.4f, 1f, healthFraction));
            SetRenderColor(tinted);
        }

        // Use a MaterialPropertyBlock so bricks share one Material asset
        // (no per-instance material instantiation) while still showing
        // per-brick-type color.
        private void SetRenderColor(Color color)
        {
            _propBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_Color", color);      // Built-in RP / Unlit
            _propBlock.SetColor("_BaseColor", color);  // URP Lit/Unlit
            _renderer.SetPropertyBlock(_propBlock);
        }

        // Swaps the renderer's actual Material (not just a property-block color) between the
        // brick's normal material and BrickType.hitFlashMaterial - same shared-asset swap/restore
        // pattern DissolveEffect uses, so no per-brick material instance ever gets created.
        private void SetFlashMaterialShowing(bool flashing)
        {
            if (BrickType.hitFlashMaterial == null) return;
            _renderer.sharedMaterial = flashing ? BrickType.hitFlashMaterial : _normalMaterial;
        }

        /// <summary>Called by the ball's collision handling when it strikes this brick.</summary>
        public void Hit(GameObject ball)
        {
            if (BrickType == null || IsDestroyed) return;

            bool destroyed = BrickType.OnHit(this, ball);

            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            SetFlashMaterialShowing(true);
            SetRenderColor(BrickType.hitFlashColor);

            if (destroyed)
            {
                IsDestroyed = true;
                _manager.audioManager?.PlaySfx(BrickType.destroyedSound);
                BrickType.OnDestroyed(this);
                _manager.NotifyBrickDestroyed(this);

                // Stop reacting to further collisions immediately - the brick is already
                // logically destroyed, it's just sticking around a moment longer (still
                // invisible - see below) before the GameObject actually goes away.
                _collider.enabled = false;
                // Hide instantly rather than playing out the flash/blink visually - the shatter
                // effect (see BrickBreakEffects, spawned from NotifyBrickDestroyed just above)
                // reads as the brick breaking now instead. DestroyAfterFlash's blink timer still
                // runs unchanged underneath regardless (it's also this brick's fuse for
                // ExplodingBrickType - see below). Skipped for ExplodingBrickType specifically:
                // its flash IS a deliberate "lit fuse" warning the player is meant to see before
                // it chains into neighbors (see ExplodingBrickType's own doc comment), not just
                // cosmetic destroy feedback like every other brick type's flash.
                if (!(BrickType is ExplodingBrickType)) _renderer.enabled = false;
                _flashCoroutine = StartCoroutine(DestroyAfterFlash());
            }
            else
            {
                _manager.audioManager?.PlaySfx(BrickType.hitSound);
                _flashCoroutine = StartCoroutine(RevertColorAfterFlash());
            }
        }

        private IEnumerator DestroyAfterFlash()
        {
            // Blinks between white and the brick's own color rather than holding a single
            // steady flash - at the short default duration it reads about the same as a solid
            // flash, but for a much longer fuse (see ExplodingBrickType.fuseDuration) it gives a
            // clear "about to explode" ticking effect instead of just staying lit the whole time.
            float elapsed = 0f;
            bool showFlash = true;
            while (elapsed < flashDuration)
            {
                SetFlashMaterialShowing(showFlash);
                SetRenderColor(showFlash ? BrickType.hitFlashColor : BrickType.color);
                showFlash = !showFlash;

                float step = Mathf.Min(blinkIntervalSeconds, flashDuration - elapsed);
                yield return new WaitForSeconds(step);
                elapsed += step;
            }

            BrickType.OnFlashComplete(this);
            Destroy(gameObject);
        }

        private IEnumerator RevertColorAfterFlash()
        {
            yield return new WaitForSeconds(flashDuration);
            SetFlashMaterialShowing(false);
            UpdateVisualColor();
            _flashCoroutine = null;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var ball = collision.collider.GetComponent<BallController>();
            if (ball != null) Hit(ball.gameObject);
        }
    }
}
