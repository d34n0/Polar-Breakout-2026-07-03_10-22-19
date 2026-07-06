using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Cannon-ability projectile. Flies outward in a straight radial line from where it was
    /// fired, destroys the first brick it touches, and despawns if it reaches the outer wall
    /// without hitting anything. A "pierce" (laser) bullet instead flies straight through every
    /// brick it touches rather than stopping at the first one - see Launch's pierce parameter. A
    /// bullet with ricochets remaining (see Launch's ricochets parameter and Ricochet Rounds)
    /// instead bounces off the radial direction at its point of impact - the same "reflect off
    /// an outward-facing surface" math BallController uses for the outer wall - continuing on to
    /// potentially hit further bricks, until its ricochets run out.
    ///
    /// The collider is always a trigger, never a physical one - with lots of bullets in flight
    /// at once, a physical collider let bullets collide with each other (and occasionally wedge
    /// against overlapping brick edges), bouncing off and losing velocity instead of flying
    /// straight like they're supposed to. A trigger only ever reports the overlap - see
    /// OnTriggerEnter2D - with no physics response at all, so that can't happen.
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
        [Tooltip("Multiplies visualLength for the trailing edge only when piercing, so a laser " +
                 "bullet reads as a long streak behind its actual point of impact rather than a " +
                 "short bolt - the leading edge (where hits actually register) stays the same " +
                 "size as a normal bullet's.")]
        public float pierceTrailLengthMultiplier = 4f;

        private Rigidbody2D _rb;
        private PolarGridSettings _settings;
        private Camera _arenaCamera;
        private bool _pierce;
        private int _ricochetsRemaining;

        private void Awake()
        {
            _arenaCamera = Camera.main;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = GetComponent<CircleCollider2D>();
            collider.radius = 0.1f;
            collider.isTrigger = true;

            BuildVisual();
        }

        private void BuildVisual()
        {
            RebuildMesh(visualLength, visualLength);

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

        /// <summary>Rebuilds the bolt's quad mesh with independent front/back extents along its
        /// direction of travel (local +X) - a plain bullet is symmetric (frontLength ==
        /// backLength), a piercing one keeps the same frontLength (so its leading tip still lines
        /// up with where the small collider actually registers hits) but a much longer backLength
        /// trailing behind it.</summary>
        private void RebuildMesh(float frontLength, float backLength)
        {
            var mesh = new Mesh { name = "Bullet" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-backLength, -visualWidth, 0f),
                new Vector3(frontLength, -visualWidth, 0f),
                new Vector3(frontLength, visualWidth, 0f),
                new Vector3(-backLength, visualWidth, 0f),
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
        }

        /// <param name="origin">World position to spawn at - should already be clear of the
        /// paddle's own collider to avoid an immediate spurious collision.</param>
        /// <param name="angleDegrees">Direction to fly, in the same polar convention as the
        /// rest of the arena (0 = +X axis).</param>
        /// <param name="materialOverride">Optional. Overrides the default procedural material -
        /// applied here (rather than a field set before Awake) since BuildVisual() already runs
        /// during AddComponent, before a caller gets a chance to configure anything.</param>
        /// <param name="colorOverride">Optional. Retints the default procedural bolt (via the
        /// same MaterialPropertyBlock BuildVisual already set up) without needing a whole custom
        /// material - ignored if materialOverride is also set, since a custom material's own
        /// colors take precedence.</param>
        /// <param name="pierce">When true, this becomes a laser bullet: it flies straight through
        /// every brick it touches (still damaging each one via Brick.Hit) instead of stopping at
        /// the first, and gets a long trailing visual streak.</param>
        /// <param name="ricochets">How many times this bullet bounces off a brick instead of
        /// being destroyed on hit (see Ricochet), before finally being destroyed on the next hit
        /// after they run out. Ignored while pierce is true, since a piercing bullet is never
        /// destroyed on a brick hit in the first place.</param>
        public void Launch(Vector2 origin, float angleDegrees, float speed, PolarGridSettings settings,
            Material materialOverride = null, Color? colorOverride = null, bool pierce = false, int ricochets = 0)
        {
            _settings = settings;
            _pierce = pierce;
            _ricochetsRemaining = ricochets;
            _rb.position = origin;
            _rb.rotation = angleDegrees;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);

            float rad = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            _rb.linearVelocity = direction * speed;

            var renderer = GetComponent<MeshRenderer>();
            if (materialOverride != null)
            {
                renderer.sharedMaterial = materialOverride;
            }
            else if (colorOverride.HasValue)
            {
                var propBlock = new MaterialPropertyBlock();
                propBlock.SetColor("_Color", colorOverride.Value);
                propBlock.SetColor("_BaseColor", colorOverride.Value);
                renderer.SetPropertyBlock(propBlock);
            }

            if (pierce) RebuildMesh(visualLength, visualLength * pierceTrailLengthMultiplier);
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

        /// <summary>A plain bullet destroys itself after damaging the first brick it touches; a
        /// piercing one (see Launch's pierce parameter) doesn't - it keeps flying straight
        /// through every brick in its path instead of stopping at the first. A bullet with
        /// ricochets remaining bounces instead of being destroyed - see Ricochet.</summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            var brick = other.GetComponent<Brick>();
            if (brick == null) return;

            brick.Hit(gameObject);
            if (_pierce) return;

            if (_ricochetsRemaining > 0)
            {
                _ricochetsRemaining--;
                Ricochet();
                return;
            }

            Destroy(gameObject);
        }

        /// <summary>Reflects the bullet's velocity off the radial direction at its current
        /// position - the same "bounce off an outward-facing surface" math BallController uses
        /// for the outer wall (see BounceOffCircularWall) - so a ricocheting bullet bounces back
        /// through the arena rather than just stopping dead at the first brick it hits. Updates
        /// the visual's rotation to match, since the bolt mesh is drawn along local +X.</summary>
        private void Ricochet()
        {
            Vector2 normal = _rb.position.normalized;
            Vector2 velocity = _rb.linearVelocity;
            velocity -= 2f * Vector2.Dot(velocity, normal) * normal;
            _rb.linearVelocity = velocity;

            float angleDegrees = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            _rb.rotation = angleDegrees;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);
        }
    }
}
