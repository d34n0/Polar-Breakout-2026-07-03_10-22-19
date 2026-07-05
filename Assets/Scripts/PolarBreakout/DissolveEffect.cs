using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Drives a Dissolve shader graph material's exposed "_DissolveProgress" property (0 = fully
    /// visible, 1 = fully dissolved) over time via a MaterialPropertyBlock, so any object with a
    /// renderer can play a one-shot dissolve-out/dissolve-in transition on command - used by
    /// BallManager to fade the paddle and ball out/in around a life-lost respawn. Uses the base
    /// Renderer type rather than MeshRenderer specifically, since the ball renders via
    /// SpriteRenderer while the paddle renders via MeshRenderer.
    ///
    /// Temporarily swaps the renderer onto the Dissolve material only while a transition is
    /// running (and while left fully dissolved), restoring the object's normal material once a
    /// dissolve-in finishes, so gameplay rendering is unaffected the rest of the time.
    /// </summary>
    public class DissolveEffect : MonoBehaviour
    {
        [Tooltip("The Dissolve shader graph material (e.g. Assets/Shaders/Dissolve.mat). Its " +
                 "_DissolveProgress property is what this component animates.")]
        public Material dissolveMaterial;

        private Renderer _renderer;
        private Material _originalMaterial;
        private MaterialPropertyBlock _propBlock;
        private Coroutine _activeTransition;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _originalMaterial = _renderer.sharedMaterial;
            _propBlock = new MaterialPropertyBlock();
        }

        /// <summary>Animates from fully visible to fully dissolved over duration seconds.</summary>
        public Coroutine DissolveOut(float duration)
        {
            if (_activeTransition != null) StopCoroutine(_activeTransition);
            _activeTransition = StartCoroutine(AnimateProgress(0f, 1f, duration, restoreOriginalAtEnd: false));
            return _activeTransition;
        }

        /// <summary>Animates from fully dissolved back to fully visible over duration seconds,
        /// then restores the object's original (non-Dissolve) material.</summary>
        public Coroutine DissolveIn(float duration)
        {
            if (_activeTransition != null) StopCoroutine(_activeTransition);
            _activeTransition = StartCoroutine(AnimateProgress(1f, 0f, duration, restoreOriginalAtEnd: true));
            return _activeTransition;
        }

        private IEnumerator AnimateProgress(float from, float to, float duration, bool restoreOriginalAtEnd)
        {
            if (dissolveMaterial != null) _renderer.sharedMaterial = dissolveMaterial;

            float elapsed = 0f;
            duration = Mathf.Max(0.0001f, duration);
            while (elapsed < duration)
            {
                // Unscaled so a pause (Time.timeScale = 0) mid-transition doesn't leave the
                // paddle/ball stuck partway dissolved until the player happens to unpause.
                elapsed += Time.unscaledDeltaTime;
                SetProgress(Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }
            SetProgress(to);

            if (restoreOriginalAtEnd) _renderer.sharedMaterial = _originalMaterial;
            _activeTransition = null;
        }

        private void SetProgress(float value)
        {
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat("_DissolveProgress", value);
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}
