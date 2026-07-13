using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// The boss turret's own projectile - mirrors Bullet.cs's shape and trigger-based philosophy
    /// (a physical collider would let it collide with the ball/paddle's own colliders and lose
    /// velocity, or wedge against them, instead of flying straight). Flies in a straight line
    /// toward wherever the ball was when fired (non-homing, same convention as the player's own
    /// Bullet). On hitting the ball, manually reflects its velocity (see BallController.
    /// ReflectVelocity) since a trigger collider produces no physics response of its own. On
    /// hitting the paddle, counts as a kill - forces every ball currently in play into its death
    /// sequence via BallManager.KillAllBallsInPlay.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class BossBullet : MonoBehaviour
    {
        public float visualRadius = 0.15f;
        public Sprite bulletSprite;

        private Rigidbody2D _rb;
        private PolarGridSettings _settings;
        private BallManager _ballManager;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = GetComponent<CircleCollider2D>();
            collider.radius = visualRadius;
            collider.isTrigger = true;

            if (bulletSprite != null)
                BuildSpriteVisual();
            else
                BuildFallbackVisual();
        }

        private void BuildSpriteVisual()
        {
            var rendererGO = new GameObject("Visual");
            rendererGO.transform.SetParent(transform, worldPositionStays: false);
            var renderer = rendererGO.AddComponent<SpriteRenderer>();
            renderer.sprite = bulletSprite;
        }

        /// <summary>Small procedural quad used until a real bulletSprite is assigned - keeps the
        /// bullet visible/testable rather than an invisible functional-only trigger.</summary>
        private void BuildFallbackVisual()
        {
            var visualGO = new GameObject("Visual");
            visualGO.transform.SetParent(transform, worldPositionStays: false);

            var meshFilter = visualGO.AddComponent<MeshFilter>();
            var meshRenderer = visualGO.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = "BossBulletFallback" };
            float r = visualRadius;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-r, -r, 0f), new Vector3(r, -r, 0f),
                new Vector3(r, r, 0f), new Vector3(-r, r, 0f),
            };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.mesh = mesh;

            meshRenderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();
            var propBlock = new MaterialPropertyBlock();
            Color color = new Color(1f, 0.2f, 0.6f);
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            meshRenderer.SetPropertyBlock(propBlock);
        }

        /// <param name="origin">World position to spawn at.</param>
        /// <param name="targetPosition">World position to fly toward - a snapshot of the ball's
        /// position at fire time, not a continuously-homing target.</param>
        /// <param name="settings">Needed for the outer-wall despawn check in FixedUpdate.</param>
        public void Launch(Vector2 origin, Vector2 targetPosition, float speed, PolarGridSettings settings)
        {
            _settings = settings;
            _rb.position = origin;

            Vector2 direction = (targetPosition - origin).normalized;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;

            float angleDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);
            _rb.linearVelocity = direction * speed;
        }

        private void FixedUpdate()
        {
            if (_settings != null && _rb.position.magnitude > _settings.outerWallRadius)
                Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var ball = other.GetComponent<BallController>();
            if (ball != null)
            {
                Vector2 normal = ((Vector2)ball.transform.position - _rb.position).normalized;
                ball.ReflectVelocity(normal);
                Destroy(gameObject);
                return;
            }

            var paddle = other.GetComponent<PaddleController>();
            if (paddle != null)
            {
                _ballManager?.KillAllBallsInPlay();
                Destroy(gameObject);
            }
        }

        /// <summary>Assigned by BossTurret's Fire (via a public setter rather than a Launch
        /// parameter, since only the paddle-hit path needs it) - kept optional so a bullet fired
        /// without one simply never triggers a kill on paddle contact.</summary>
        public void SetBallManager(BallManager ballManager) => _ballManager = ballManager;
    }
}
