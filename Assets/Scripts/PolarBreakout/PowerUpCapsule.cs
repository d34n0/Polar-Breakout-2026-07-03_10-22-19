using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawned by PowerUpBrickType at a destroyed brick's position. Falls toward the arena
    /// center (same "gravitate inward" idea as everything else in this polar arena) while
    /// swaying side to side in a slow sine wave, and grants its ability if the paddle catches
    /// it along the way. The wobble means a straight radial line from spawn point to center no
    /// longer guarantees a catch - the player has to actually track it, and can miss it. Uses
    /// plain distance/angle math against the paddle rather than a physics collider, matching
    /// how BallController already checks the outer wall/death zone manually instead of via
    /// physics triggers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PowerUpCapsule : MonoBehaviour
    {
        [Tooltip("How fast the capsule falls toward the arena center, world units/second.")]
        public float fallSpeed = 2f;
        [Tooltip("Half-size of the capsule's visual quad, world units.")]
        public float visualSize = 0.25f;

        [Header("Wobble")]
        [Tooltip("How far the capsule sways either side of its straight-line path, in degrees.")]
        public float wobbleAmplitudeDegrees = 12f;
        [Tooltip("How fast the sway oscillates, radians/second. Lower = slower, lazier drift.")]
        public float wobbleSpeed = 1.5f;

        public PowerUpType Type { get; private set; }

        private PaddleController _paddle;
        private float _spawnAngleDegrees;
        private float _currentAngleDegrees;
        private float _radius;
        private float _elapsedTime;

        private void Awake()
        {
            _paddle = FindFirstObjectByType<PaddleController>();
        }

        public void Initialize(Vector2 spawnWorldPosition, PowerUpType type)
        {
            Type = type;
            _radius = spawnWorldPosition.magnitude;
            _spawnAngleDegrees = Mathf.Atan2(spawnWorldPosition.y, spawnWorldPosition.x) * Mathf.Rad2Deg;

            BuildVisual();
            UpdateTransform();
        }

        private void BuildVisual()
        {
            var mesh = new Mesh { name = "PowerUpCapsule" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-visualSize, -visualSize, 0f),
                new Vector3(visualSize, -visualSize, 0f),
                new Vector3(visualSize, visualSize, 0f),
                new Vector3(-visualSize, visualSize, 0f),
            };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().mesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            // Bricks get away without ever assigning a Material because Brick.prefab already has
            // one serialized on its MeshRenderer from being set up in the Editor. This capsule is
            // built entirely at runtime via AddComponent<MeshRenderer>(), which - unlike adding
            // the component through the Inspector - never auto-assigns a material, so without
            // this line the renderer has nothing to draw at all.
            renderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();

            var propBlock = new MaterialPropertyBlock();
            Color color = GetDefaultColor(Type);
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(propBlock);
        }

        private static Color GetDefaultColor(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Multiball: return new Color(0.2f, 0.8f, 1f);
                case PowerUpType.Autopilot: return new Color(1f, 0.85f, 0.1f);
                case PowerUpType.Cannon: return new Color(1f, 0.3f, 0.2f);
                default: return Color.white;
            }
        }

        private void Update()
        {
            if (_paddle == null)
            {
                Destroy(gameObject);
                return;
            }

            _elapsedTime += Time.deltaTime;
            _radius -= fallSpeed * Time.deltaTime;
            UpdateTransform();

            if (_radius <= _paddle.settings.deathZoneRadius)
            {
                Destroy(gameObject);
                return;
            }

            if (IsCaughtByPaddle())
            {
                var abilities = _paddle.GetComponent<PaddleAbilities>();
                if (abilities != null) abilities.CollectPowerUp(Type);
                Destroy(gameObject);
            }
        }

        private void UpdateTransform()
        {
            // Sine wave starts at zero offset (elapsedTime == 0 at spawn), so a capsule spawned
            // directly on the paddle is still caught immediately rather than the wobble
            // spuriously nudging it out of catch range on the very first frame.
            float wobbleDegrees = Mathf.Sin(_elapsedTime * wobbleSpeed) * wobbleAmplitudeDegrees;
            _currentAngleDegrees = _spawnAngleDegrees + wobbleDegrees;

            float rad = _currentAngleDegrees * Mathf.Deg2Rad;
            transform.position = new Vector3(Mathf.Cos(rad) * _radius, Mathf.Sin(rad) * _radius, 0f);
        }

        private bool IsCaughtByPaddle()
        {
            float paddleOuterRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            float paddleInnerRadius = _paddle.settings.paddleOrbitRadius - _paddle.radialThickness / 2f;
            if (_radius > paddleOuterRadius || _radius < paddleInnerRadius) return false;

            // Checked against the wobbled angle, not the original spawn angle - what the player
            // sees on screen is what has to line up with the paddle.
            float angleDelta = Mathf.Abs(Mathf.DeltaAngle(_currentAngleDegrees, _paddle.CurrentAngleDegrees));
            return angleDelta <= _paddle.angularWidthDegrees / 2f;
        }
    }
}
