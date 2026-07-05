using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawned by BrickTypeSO.TryDropShards at a destroyed brick's position - falls toward the
    /// arena center and grants its shard amount to CurrencyManager if the paddle catches it along
    /// the way, exactly like PowerUpCapsule's "gravitate inward + sine wobble" motion and
    /// distance/angle catch check. Kept as a separate, simpler type from PowerUpCapsule (a small
    /// diamond shape, no per-type material hookup) since shards are a plain numeric currency, not
    /// a paddle ability.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ShardPickup : MonoBehaviour
    {
        [Tooltip("How fast the shard falls toward the arena center, world units/second.")]
        public float fallSpeed = 2f;
        [Tooltip("How far the shard sways either side of its straight-line path, in degrees.")]
        public float wobbleAmplitudeDegrees = 12f;
        [Tooltip("How fast the sway oscillates, radians/second.")]
        public float wobbleSpeed = 1.5f;
        [Tooltip("Half-height of the diamond shape, world units.")]
        public float visualRadius = 0.09f;
        public Color color = new Color(0.6f, 0.9f, 1f);

        public int Amount { get; private set; }

        /// <summary>Fired the instant any shard pickup is actually caught by the paddle, with
        /// the amount granted - mirrors PowerUpCapsule.OnAnyCapsuleCollected's decoupled pattern
        /// (no direct reference needed since shards are spawned dynamically at runtime).</summary>
        public static event System.Action<int> OnAnyShardCollected;

        private PaddleController _paddle;
        private CurrencyManager _currencyManager;
        private float _spawnAngleDegrees;
        private float _currentAngleDegrees;
        private float _radius;
        private float _elapsedTime;

        private void Awake()
        {
            _paddle = FindFirstObjectByType<PaddleController>();
            _currencyManager = FindFirstObjectByType<CurrencyManager>();
        }

        public void Initialize(Vector2 spawnWorldPosition, int amount)
        {
            Amount = amount;
            _radius = spawnWorldPosition.magnitude;
            _spawnAngleDegrees = Mathf.Atan2(spawnWorldPosition.y, spawnWorldPosition.x) * Mathf.Rad2Deg;

            BuildVisual();
            UpdateTransform();
        }

        private void BuildVisual()
        {
            GetComponent<MeshFilter>().mesh = BuildDiamondMesh(visualRadius);

            // Built entirely at runtime via AddComponent<MeshRenderer>(), which never
            // auto-assigns a material, so without this the renderer has nothing to draw.
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();

            var propBlock = new MaterialPropertyBlock();
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(propBlock);
        }

        private static Mesh BuildDiamondMesh(float radius)
        {
            var mesh = new Mesh { name = "ShardPickup" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(0f, radius, 0f),
                new Vector3(radius * 0.6f, 0f, 0f),
                new Vector3(0f, -radius, 0f),
                new Vector3(-radius * 0.6f, 0f, 0f),
            };
            mesh.uv = new Vector2[] { new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) };
            // Same -Z-facing winding convention as Bullet.cs's quad.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
                if (_currencyManager != null) _currencyManager.AddShards(Amount);
                OnAnyShardCollected?.Invoke(Amount);
                Destroy(gameObject);
            }
        }

        private void UpdateTransform()
        {
            float wobbleDegrees = Mathf.Sin(_elapsedTime * wobbleSpeed) * wobbleAmplitudeDegrees;
            _currentAngleDegrees = _spawnAngleDegrees + wobbleDegrees;

            float rad = _currentAngleDegrees * Mathf.Deg2Rad;
            transform.position = new Vector3(Mathf.Cos(rad) * _radius, Mathf.Sin(rad) * _radius, 0f);
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngleDegrees - 90f);
        }

        private bool IsCaughtByPaddle()
        {
            float paddleOuterRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            float paddleInnerRadius = _paddle.settings.paddleOrbitRadius - _paddle.radialThickness / 2f;
            if (_radius > paddleOuterRadius || _radius < paddleInnerRadius) return false;

            float angleDelta = Mathf.Abs(Mathf.DeltaAngle(_currentAngleDegrees, _paddle.CurrentAngleDegrees));
            return angleDelta <= _paddle.angularWidthDegrees / 2f;
        }
    }
}
