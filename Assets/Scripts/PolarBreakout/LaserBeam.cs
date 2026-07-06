using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Legendary "Laser Cannon" card effect - a genuine continuous beam fired from a cannon
    /// turret, not a fast-traveling piercing bullet. The whole beam appears instantly, spanning
    /// from the turret out to the arena's outer wall, damages every brick already touching it
    /// (a static trigger collider still correctly fires OnTriggerEnter2D for anything it's
    /// created already overlapping, once the physics step processes it), then disappears after
    /// a short duration. Width scales with how many Split the Atom cards are also equipped - see
    /// PaddleAbilities.FireBarrel, which computes that and passes it in.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class LaserBeam : MonoBehaviour
    {
        [Tooltip("How long the beam stays active (visible and damaging), seconds.")]
        public float duration = 0.15f;

        private float _elapsed;

        /// <param name="origin">World position the beam starts from (the turret's muzzle).</param>
        /// <param name="angleDegrees">Direction the beam points, same polar convention as the
        /// rest of the arena (0 = +X axis).</param>
        /// <param name="width">Full width of the beam, world units - see PaddleAbilities'
        /// laserBeamBaseWidth/laserBeamWidthPerExtraBullet.</param>
        /// <param name="length">How far the beam extends from origin - typically the remaining
        /// distance to the arena's outer wall, so it always reaches the edge.</param>
        /// <param name="materialOverride">Optional. A future "laser skin" material - leave unset
        /// to use the default plain-colored beam.</param>
        /// <param name="colorOverride">Optional. Tints the default beam when no materialOverride
        /// is set.</param>
        public void Initialize(Vector2 origin, float angleDegrees, float width, float length,
            Material materialOverride = null, Color? colorOverride = null)
        {
            transform.position = origin;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);

            var rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            // Offset so the box extends forward from origin (local +X) rather than being
            // centered on it - the beam should only ever cut things in front of the turret.
            var collider = GetComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(length, width);
            collider.offset = new Vector2(length / 2f, 0f);

            BuildVisual(length, width, materialOverride, colorOverride);
        }

        private void BuildVisual(float length, float width, Material materialOverride, Color? colorOverride)
        {
            var mesh = new Mesh { name = "LaserBeam" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(0f, -width / 2f, 0f),
                new Vector3(length, -width / 2f, 0f),
                new Vector3(length, width / 2f, 0f),
                new Vector3(0f, width / 2f, 0f),
            };
            mesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            // Same -Z-facing winding convention as Bullet.cs's quad.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().mesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            if (materialOverride != null)
            {
                renderer.sharedMaterial = materialOverride;
                return;
            }

            renderer.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();
            var propBlock = new MaterialPropertyBlock();
            Color color = colorOverride ?? new Color(1f, 0.25f, 0.25f);
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(propBlock);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var brick = other.GetComponent<Brick>();
            if (brick != null) brick.Hit(gameObject);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= duration) Destroy(gameObject);
        }
    }
}
