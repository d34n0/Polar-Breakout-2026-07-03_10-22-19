using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Cannon-ability projectile. Flies outward in a straight radial line from where it was
    /// fired, destroys the first brick it touches, and despawns if it reaches the outer wall
    /// without hitting anything.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class Bullet : MonoBehaviour
    {
        [Tooltip("Half-length of the visual bolt along its direction of travel, world units.")]
        public float visualLength = 0.2f;
        [Tooltip("Half-width of the visual bolt, world units.")]
        public float visualWidth = 0.05f;

        private Rigidbody2D _rb;
        private PolarGridSettings _settings;
        private Camera _arenaCamera;

        private void Awake()
        {
            _arenaCamera = Camera.main;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = GetComponent<CircleCollider2D>();
            collider.radius = 0.1f;

            BuildVisual();
        }

        private void BuildVisual()
        {
            var mesh = new Mesh { name = "Bullet" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-visualLength, -visualWidth, 0f),
                new Vector3(visualLength, -visualWidth, 0f),
                new Vector3(visualLength, visualWidth, 0f),
                new Vector3(-visualLength, visualWidth, 0f),
            };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            // Wound so the computed normal faces -Z (toward the camera), matching the convention
            // used elsewhere in PolarMeshUtility - only ever looked right because
            // GetProceduralUnlitMaterial() explicitly disables culling; a custom material with
            // normal back-face culling would make this bolt invisible.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().mesh = mesh;

            // Same reasoning as PowerUpCapsule: this is built entirely at runtime, so the
            // MeshRenderer has no material to draw with until one is explicitly assigned.
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();

            var propBlock = new MaterialPropertyBlock();
            Color color = new Color(1f, 0.95f, 0.4f);
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(propBlock);
        }

        /// <param name="origin">World position to spawn at - should already be clear of the
        /// paddle's own collider to avoid an immediate spurious collision.</param>
        /// <param name="angleDegrees">Direction to fly, in the same polar convention as the
        /// rest of the arena (0 = +X axis).</param>
        /// <param name="materialOverride">Optional. Overrides the default procedural material -
        /// applied here (rather than a field set before Awake) since BuildVisual() already runs
        /// during AddComponent, before a caller gets a chance to configure anything.</param>
        public void Launch(Vector2 origin, float angleDegrees, float speed, PolarGridSettings settings, Material materialOverride = null)
        {
            _settings = settings;
            _rb.position = origin;
            _rb.rotation = angleDegrees;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);

            float rad = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            _rb.linearVelocity = direction * speed;

            if (materialOverride != null) GetComponent<MeshRenderer>().sharedMaterial = materialOverride;
        }

        private void FixedUpdate()
        {
            if (_arenaCamera != null && _arenaCamera.orthographic)
            {
                float halfHeight = _arenaCamera.orthographicSize;
                float halfWidth = halfHeight * _arenaCamera.aspect;
                Vector2 pos = _rb.position;
                if (Mathf.Abs(pos.x) > halfWidth || Mathf.Abs(pos.y) > halfHeight)
                    Destroy(gameObject);
            }
            else if (_settings != null && _rb.position.magnitude > _settings.outerWallRadius)
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var brick = collision.collider.GetComponent<Brick>();
            if (brick == null) return;

            brick.Hit(gameObject);
            Destroy(gameObject);
        }
    }
}
