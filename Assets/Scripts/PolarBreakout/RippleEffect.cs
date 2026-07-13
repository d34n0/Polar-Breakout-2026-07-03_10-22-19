using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Drives a one-shot screen-space distortion shockwave (Assets/Shaders/Ripple.shader) at the
    /// GameObject's own position - used as the visual counterpart to a particle burst on brick
    /// breaks and the boss's defeat (see BrickBreakEffects.SpawnBreakParticles/
    /// BossController.PlayDefeatSequence). Builds its own quad mesh procedurally (same
    /// "generate the mesh in code" approach Brick.cs uses for its hex mesh) so the prefab needs
    /// nothing beyond this component and a material - no external mesh asset dependency.
    /// Instantiate the prefab at the explosion point, call Play(tint), and it self-destructs once
    /// every wave has finished.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RippleEffect : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("World-space radius the ring expands to - sets the quad's local scale.")]
        public float maxRadius = 1.5f;
        [Tooltip("Seconds for a single ring to expand from the center out to maxRadius.")]
        public float duration = 0.35f;
        [Tooltip("Thickness of the expanding ring band, in the shader's normalized 0-1 radius units.")]
        public float ringWidth = 0.2f;
        [Tooltip("Screen-space UV distortion strength at the ring band.")]
        public float distortion = 0.03f;
        [Tooltip("Additive brightness of the ring's own color highlight.")]
        public float ringIntensity = 1.5f;

        [Header("Multi-wave")]
        [Tooltip("Number of rings fired in sequence - 1 for a simple brick pop, 2+ for a bigger " +
                 "boss-scale shockwave.")]
        public int waveCount = 1;
        [Tooltip("Seconds between the start of each successive wave when waveCount > 1.")]
        public float waveStagger = 0.15f;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            GetComponent<MeshFilter>().sharedMesh = BuildQuadMesh();
            _renderer = GetComponent<MeshRenderer>();
            _propBlock = new MaterialPropertyBlock();
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "RippleQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Starts the shockwave, tinted to color, and destroys this GameObject once the
        /// last wave finishes.</summary>
        public void Play(Color tint)
        {
            transform.localScale = new Vector3(maxRadius, maxRadius, 1f);
            StartCoroutine(PlayRoutine(tint));
        }

        private IEnumerator PlayRoutine(Color tint)
        {
            for (int wave = 0; wave < Mathf.Max(1, waveCount); wave++)
            {
                StartCoroutine(AnimateWave(tint));
                if (wave < waveCount - 1)
                    yield return new WaitForSecondsRealtime(waveStagger);
            }

            // Total lifetime covers every wave's own duration plus the stagger before it started.
            float lifetime = duration + waveStagger * Mathf.Max(0, waveCount - 1);
            yield return new WaitForSecondsRealtime(lifetime);
            Destroy(gameObject);
        }

        private IEnumerator AnimateWave(Color tint)
        {
            SetProperties(0f, tint);

            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            while (elapsed < safeDuration)
            {
                // Unscaled so the ripple isn't frozen out by BrickBreakEffects' hit-stop or the
                // boss's own defeat slow-mo - both set Time.timeScale near/at the moment this plays.
                elapsed += Time.unscaledDeltaTime;
                SetProperties(Mathf.Clamp01(elapsed / safeDuration), tint);
                yield return null;
            }
            SetProperties(1f, tint);
        }

        private void SetProperties(float progress, Color tint)
        {
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat("_Progress", progress);
            _propBlock.SetFloat("_Width", ringWidth);
            _propBlock.SetFloat("_Distortion", distortion);
            _propBlock.SetColor("_RingColor", tint);
            _propBlock.SetFloat("_RingIntensity", ringIntensity);
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}
