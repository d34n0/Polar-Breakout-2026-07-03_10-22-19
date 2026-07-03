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
            renderer.sharedMaterial = PolarMeshUtility.CreateTransparentUnlitMaterial(color);
            // Drawn well behind bricks/paddle/ball (all left at the default sorting order) so it
            // never fights with anything actually sitting at the arena center.
            renderer.sortingOrder = -100;
        }
    }
}
