using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Trauma-based screen shake: callers add trauma (0-1) via AddTrauma() rather than
    /// specifying an offset directly, so several shakes triggered in the same frame (e.g.
    /// multiple bricks breaking at once) stack naturally instead of one cancelling another.
    /// Attach directly to the camera that should shake.
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        [Tooltip("World-space offset applied at maximum trauma (trauma = 1).")]
        public float maxOffset = 0.3f;
        [Tooltip("How quickly trauma drains back to zero, in trauma/second.")]
        public float traumaDecayPerSecond = 1.5f;

        private float _trauma;
        private Vector3 _restPosition;

        private void Awake()
        {
            _restPosition = transform.localPosition;
        }

        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        private void LateUpdate()
        {
            if (_trauma <= 0f)
            {
                if (transform.localPosition != _restPosition)
                    transform.localPosition = _restPosition;
                return;
            }

            // Square the trauma so weak shakes fall off fast but strong ones still feel punchy.
            float shake = _trauma * _trauma;
            Vector2 offset = Random.insideUnitCircle * (maxOffset * shake);
            transform.localPosition = _restPosition + new Vector3(offset.x, offset.y, 0f);

            _trauma = Mathf.Max(0f, _trauma - traumaDecayPerSecond * Time.deltaTime);
        }
    }
}
