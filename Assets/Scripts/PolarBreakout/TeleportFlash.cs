using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One-shot "teleported out of existence" pop: a white disc that rapidly expands while fading
    /// to nothing at the given position, then destroys itself. Spawned at the spot where something
    /// just finished collapsing to zero scale (see BallManager.FlashOutTransientsAndPaddle), so
    /// the vanish reads as a bright teleport burst rather than the object merely shrinking away.
    /// Runs entirely on unscaled time, since its callers all fire during the death freeze
    /// (Time.timeScale 0).
    /// </summary>
    public class TeleportFlash : MonoBehaviour
    {
        /// <summary>Spawns a single flash at position. maxRadius is the disc's fully expanded
        /// world-space radius; the whole burst lasts duration seconds.</summary>
        public static void Spawn(Vector2 position, float maxRadius = 0.45f, float duration = 0.25f)
        {
            // Slightly toward the camera so the flash draws over whatever it's popping in front of.
            var go = new GameObject("TeleportFlash");
            go.transform.position = new Vector3(position.x, position.y, -0.2f);

            var flash = go.AddComponent<TeleportFlash>();
            flash._maxRadius = Mathf.Max(0.01f, maxRadius);
            flash._duration = Mathf.Max(0.0001f, duration);
        }

        private float _maxRadius;
        private float _duration;
        private float _elapsed;
        private Material _material;

        private void Start()
        {
            gameObject.AddComponent<MeshFilter>().mesh = PolarMeshUtility.BuildFilledCircleMesh(1f, 24);
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _material = PolarMeshUtility.CreateTransparentUnlitMaterial(Color.white);
            meshRenderer.sharedMaterial = _material;
            // Above bricks/paddle/pickups (all default order) - a teleport burst always reads on top.
            meshRenderer.sortingOrder = 100;

            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            // Fast-out expansion (most of the growth up front) with a linear fade - reads as a
            // sharp pop of light that dissipates, rather than a balloon inflating.
            float radius = _maxRadius * Mathf.Sin(t * Mathf.PI * 0.5f);
            transform.localScale = new Vector3(radius, radius, 1f);

            var faded = new Color(1f, 1f, 1f, 1f - t);
            _material.SetColor("_Color", faded);
            _material.SetColor("_BaseColor", faded);

            if (t >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // CreateTransparentUnlitMaterial makes a fresh Material instance per flash - destroyed
            // with the flash, or every burst would leak one.
            if (_material != null) Destroy(_material);
        }
    }
}
