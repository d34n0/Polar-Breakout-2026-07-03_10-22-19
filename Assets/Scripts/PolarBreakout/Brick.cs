using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Runtime instance of a brick placed by BrickGridManager. Builds its own
    /// arc-segment mesh from the grid settings so it sits flush with its neighbors,
    /// and holds per-instance state (current health).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Brick : MonoBehaviour
    {
        public PolarCoordinate Coordinate { get; private set; }
        public BrickTypeSO BrickType { get; private set; }
        public int CurrentHealth;

        private BrickGridManager _manager;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propBlock;

        public void Initialize(BrickGridManager manager, PolarGridSettings settings, PolarCoordinate coord, BrickTypeSO type)
        {
            _manager = manager;
            Coordinate = coord;
            BrickType = type;
            CurrentHealth = type.maxHealth;

            settings.GetBrickRadialRange(coord, out float innerRadius, out float outerRadius);
            settings.GetBrickAngleRange(coord, out float startAngleDeg, out float endAngleDeg);

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = PolarMeshUtility.BuildArcSegmentMesh(
                innerRadius, outerRadius, startAngleDeg, endAngleDeg, settings.curveResolutionDegrees);

            _renderer = GetComponent<MeshRenderer>();

            // Use a MaterialPropertyBlock so bricks share one Material asset
            // (no per-instance material instantiation) while still showing
            // per-brick-type color.
            _propBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_Color", type.color);      // Built-in RP / Unlit
            _propBlock.SetColor("_BaseColor", type.color);   // URP Lit/Unlit
            _renderer.SetPropertyBlock(_propBlock);
        }

        /// <summary>Called by the ball's collision handling when it strikes this brick.</summary>
        public void Hit(GameObject ball)
        {
            if (BrickType == null) return;

            bool destroyed = BrickType.OnHit(this, ball);
            if (destroyed)
            {
                BrickType.OnDestroyed(this);
                _manager.NotifyBrickDestroyed(this);
                Destroy(gameObject);
            }
        }
    }
}
