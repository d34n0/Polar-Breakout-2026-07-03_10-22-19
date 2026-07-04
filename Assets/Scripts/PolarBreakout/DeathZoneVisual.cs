using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Purely visual - draws a filled, semi-transparent warning disc at the arena center
    /// matching PolarGridSettings.deathZoneRadius, so the danger area is visible during play
    /// instead of only being felt when the ball disappears into it.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class DeathZoneVisual : MonoBehaviour
    {
        public PolarGridSettings settings;
        public Color color = new Color(0.6f, 0.05f, 0.05f, 0.35f);
        [Tooltip("Number of segments approximating the circle - higher is smoother.")]
        public int circleSegments = 48;
        [Tooltip("Optional. Overrides the generated transparent material with a custom one (e.g. " +
                 "a Shader Graph for a pulsing/animated warning effect) instead of just a flat " +
                 "color. Leave unset to use the plain transparent unlit material tinted by Color.")]
        public Material materialOverride;

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogWarning("DeathZoneVisual has no PolarGridSettings assigned.", this);
                return;
            }

            transform.position = Vector3.zero;

            GetComponent<MeshFilter>().mesh = PolarMeshUtility.BuildFilledCircleMesh(settings.deathZoneRadius, circleSegments);

            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materialOverride != null
                ? materialOverride
                : PolarMeshUtility.CreateTransparentUnlitMaterial(color);
            // Drawn well behind bricks/paddle/ball (all left at the default sorting order) so it
            // never fights with anything actually sitting at the arena center.
            renderer.sortingOrder = -100;
        }
    }
}
