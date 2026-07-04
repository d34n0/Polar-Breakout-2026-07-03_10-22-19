using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Runtime instance of a brick placed by BrickGridManager. Builds its own
    /// arc-segment mesh and matching collider from the grid settings so it sits
    /// flush with its neighbors, and holds per-instance state (current health).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class Brick : MonoBehaviour
    {
        [Tooltip("How long the white hit-flash lasts, in seconds, for both a surviving hit and the final destroying hit.")]
        public float flashDuration = 0.08f;

        public PolarCoordinate Coordinate { get; private set; }
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
        public Vector2 WorldPosition => Settings.PolarToWorld(Coordinate);

        private BrickGridManager _manager;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propBlock;
        private PolygonCollider2D _collider;
        private Coroutine _flashCoroutine;

        public void Initialize(BrickGridManager manager, PolarGridSettings settings, PolarCoordinate coord, BrickTypeSO type)
        {
            _manager = manager;
            Settings = settings;
            Coordinate = coord;
            BrickType = type;
            CurrentHealth = type.maxHealth;

            settings.GetBrickRadialRange(coord, out float innerRadius, out float outerRadius);
            settings.GetBrickAngleRange(coord, out float startAngleDeg, out float endAngleDeg);

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = PolarMeshUtility.BuildArcSegmentMesh(
                innerRadius, outerRadius, startAngleDeg, endAngleDeg, settings.curveResolutionDegrees);

            _collider = GetComponent<PolygonCollider2D>();
            var points = PolarMeshUtility.BuildArcOutlinePoints(
                innerRadius, outerRadius, startAngleDeg, endAngleDeg, settings.curveResolutionDegrees);
            _collider.pathCount = 1;
            _collider.SetPath(0, points);

            _renderer = GetComponent<MeshRenderer>();
            // Falls back to whatever material Brick.prefab already has (shared across every
            // brick type by default) unless this specific type opts into its own shader.
            if (type.materialOverride != null) _renderer.sharedMaterial = type.materialOverride;
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

        /// <summary>Called by the ball's collision handling when it strikes this brick.</summary>
        public void Hit(GameObject ball)
        {
            if (BrickType == null || IsDestroyed) return;

            bool destroyed = BrickType.OnHit(this, ball);

            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            SetRenderColor(Color.white);

            if (destroyed)
            {
                IsDestroyed = true;
                BrickType.OnDestroyed(this);
                _manager.NotifyBrickDestroyed(this);

                // Stop reacting to further collisions immediately - the brick is already
                // logically destroyed, it's just sticking around a moment longer to show
                // the flash before the GameObject actually goes away.
                _collider.enabled = false;
                _flashCoroutine = StartCoroutine(DestroyAfterFlash());
            }
            else
            {
                _flashCoroutine = StartCoroutine(RevertColorAfterFlash());
            }
        }

        private IEnumerator DestroyAfterFlash()
        {
            yield return new WaitForSeconds(flashDuration);
            Destroy(gameObject);
        }

        private IEnumerator RevertColorAfterFlash()
        {
            yield return new WaitForSeconds(flashDuration);
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
