using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Drives a Dissolve shader graph material's exposed "_DissolveProgress" property (0 = fully
    /// visible, 1 = fully dissolved) over time via a MaterialPropertyBlock, so any object with a
    /// renderer can play a one-shot dissolve-out/dissolve-in transition on command - used by
    /// BallManager to fade the paddle and ball out/in around a life-lost respawn. Collects every
    /// Renderer under this GameObject (self + children) rather than just its own, since the
    /// paddle has cosmetic child parts (cannon turrets, a sprite-skinned paddle's own ship
    /// sprite) that should fade in lockstep with the main body rather than popping in/out
    /// mid-transition. Uses the base Renderer type rather than MeshRenderer specifically, since
    /// the ball renders via SpriteRenderer while the paddle's arc mesh renders via MeshRenderer.
    ///
    /// Temporarily swaps each renderer onto the Dissolve material only while a transition is
    /// running (and while left fully dissolved), restoring each renderer's own original material
    /// once a dissolve-in finishes, so gameplay rendering is unaffected the rest of the time.
    /// </summary>
    public class DissolveEffect : MonoBehaviour
    {
        [Tooltip("The Dissolve shader graph material (e.g. Assets/Shaders/Dissolve.mat). Its " +
                 "_DissolveProgress property is what this component animates.")]
        public Material dissolveMaterial;
        [Tooltip("Optional. Plays AudioManager.dissolveInSound/dissolveOutSound once per " +
                 "DissolveIn/DissolveOut call. Leave unset for a silent dissolve.")]
        public AudioManager audioManager;

        private Renderer[] _renderers;
        private Material[] _originalMaterials;
        private Color[] _originalColors;
        private MaterialPropertyBlock _propBlock;
        private Coroutine _activeTransition;

        private void Awake()
        {
            RefreshRenderers();
            _propBlock = new MaterialPropertyBlock();
        }

        /// <summary>Re-collects every child Renderer and its current material. Called from Awake
        /// (so a simple, always-DissolveIn-first user like the ball never sees a null renderer
        /// list) and again at the start of every DissolveOut (so a paddle's cosmetic children -
        /// built by sibling components in their own Awake, which may run before or after this
        /// one - are always included by the time a real dissolve cycle begins, and any skin
        /// changed since the last cycle isn't reverted by the eventual restore).</summary>
        private void RefreshRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _originalMaterials = new Material[_renderers.Length];
            _originalColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _originalMaterials[i] = _renderers[i].sharedMaterial;
                _originalColors[i] = GetMaterialColor(_originalMaterials[i]);
            }
        }

        /// <summary>Reads a material's own base tint (_BaseColor for Lit/Shader Graph materials,
        /// _Color for Unlit/legacy ones), falling back to white if neither property exists - fed
        /// into dissolveMaterial's _Tint_Color below so a renderer swapped onto the shared dissolve
        /// material during a transition still reads as roughly its own color instead of the
        /// dissolve shader's own flat grey default.</summary>
        private static Color GetMaterialColor(Material mat)
        {
            if (mat == null) return Color.white;
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            return Color.white;
        }

        /// <summary>Animates from fully visible to fully dissolved over duration seconds.</summary>
        public Coroutine DissolveOut(float duration)
        {
            if (_activeTransition != null) StopCoroutine(_activeTransition);
            RefreshRenderers();
            audioManager?.PlayDissolveOut();
            _activeTransition = StartCoroutine(AnimateProgress(0f, 1f, duration, restoreOriginalAtEnd: false));
            return _activeTransition;
        }

        /// <summary>Animates from fully dissolved back to fully visible over duration seconds,
        /// then restores each renderer's original (non-Dissolve) material.</summary>
        public Coroutine DissolveIn(float duration)
        {
            if (_activeTransition != null) StopCoroutine(_activeTransition);
            audioManager?.PlayDissolveIn();
            _activeTransition = StartCoroutine(AnimateProgress(1f, 0f, duration, restoreOriginalAtEnd: true));
            return _activeTransition;
        }

        /// <summary>Instantly (no animation) puts every renderer into the fully-dissolved
        /// (invisible) state - for an object that should start hidden and play its own DissolveIn
        /// later (e.g. BossController's spawn-in), mirroring ScaleInOvershoot's own "hide
        /// immediately in Awake, reveal via Play() later" pattern.</summary>
        public void SnapToDissolved()
        {
            if (_activeTransition != null)
            {
                StopCoroutine(_activeTransition);
                _activeTransition = null;
            }
            RefreshRenderers();
            SwapToDissolveMaterial();
            SetProgress(1f);
        }

        /// <summary>Swaps every renderer onto dissolveMaterial and tints it to that renderer's own
        /// original color via property block - shared setup for both an animated transition
        /// (AnimateProgress) and an instant snap (SnapToDissolved).</summary>
        private void SwapToDissolveMaterial()
        {
            if (dissolveMaterial == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                r.sharedMaterial = dissolveMaterial;
                // Tints the shared dissolve material to this renderer's own real color for the
                // whole transition, via property block (not the shared asset), so e.g. the
                // death zone's dark red doesn't flash as the dissolve shader's flat grey
                // default before snapping to its real look once the transition ends.
                r.GetPropertyBlock(_propBlock);
                _propBlock.SetColor("_Tint_Color", _originalColors[i]);
                r.SetPropertyBlock(_propBlock);
            }
        }

        private IEnumerator AnimateProgress(float from, float to, float duration, bool restoreOriginalAtEnd)
        {
            SwapToDissolveMaterial();

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

            if (restoreOriginalAtEnd)
                for (int i = 0; i < _renderers.Length; i++)
                    _renderers[i].sharedMaterial = _originalMaterials[i];
            _activeTransition = null;
        }

        private void SetProgress(float value)
        {
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat("_DissolveProgress", value);
                r.SetPropertyBlock(_propBlock);
            }
        }
    }
}
