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

            var collider = GetComponent<PolygonCollider2D>();
            var points = PolarMeshUtility.BuildArcOutlinePoints(
                innerRadius, outerRadius, startAngleDeg, endAngleDeg, settings.curveResolutionDegrees);
            collider.pathCount = 1;
            collider.SetPath(0, points);

            _renderer = GetComponent<MeshRenderer>();
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

            // Use a MaterialPropertyBlock so bricks share one Material asset
            // (no per-instance material instantiation) while still showing
            // per-brick-type color.
            _propBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_Color", tinted);      // Built-in RP / Unlit
            _propBlock.SetColor("_BaseColor", tinted);  // URP Lit/Unlit
            _renderer.SetPropertyBlock(_propBlock);
        }

        /// <summary>Called by the ball's collision handling when it strikes this brick.</summary>
        public void Hit(GameObject ball)
        {
            if (BrickType == null || IsDestroyed) return;

            bool destroyed = BrickType.OnHit(this, ball);
            if (destroyed)
            {
                IsDestroyed = true;
                BrickType.OnDestroyed(this);
                _manager.NotifyBrickDestroyed(this);
                Destroy(gameObject);
            }
            else
            {
                UpdateVisualColor();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var ball = collision.collider.GetComponent<BallController>();
            if (ball != null) Hit(ball.gameObject);
        }
    }
}
