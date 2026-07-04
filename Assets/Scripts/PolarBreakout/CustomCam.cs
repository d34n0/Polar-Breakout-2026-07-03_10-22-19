using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Zooms the orthographic camera out as the ball moves farther from the arena center, so it
    /// stays comfortably in view instead of the camera being sized for a fixed worst-case
    /// distance at all times. Deliberately does not move the camera - BallController's own
    /// screen-edge bounce (see BounceOffScreenEdges) assumes the camera's viewport is centered
    /// on the arena origin, so panning it to follow the ball would desync the ball's bounce
    /// boundary from what's actually visible on screen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CustomCam : MonoBehaviour
    {
        [Tooltip("The ball (or any point) whose distance from the arena center drives the zoom.")]
        public Transform target;
        [Tooltip("Extra room added beyond the target's distance from center, so it isn't sitting exactly at the edge of the view.")]
        public float viewPadding = 2f;
        [Tooltip("Smallest orthographic size the camera will zoom in to.")]
        public float minSize = 5f;
        [Tooltip("Largest orthographic size the camera will zoom out to.")]
        public float maxSize = 12f;

        private Camera _cam;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float distanceFromCenter = new Vector2(target.position.x, target.position.y).magnitude;
            float desiredSize = distanceFromCenter + viewPadding;
            _cam.orthographicSize = Mathf.Clamp(desiredSize, minSize, maxSize);
        }
    }
}
