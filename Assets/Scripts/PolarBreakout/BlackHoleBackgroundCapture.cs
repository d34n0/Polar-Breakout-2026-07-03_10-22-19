using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Renders ONLY the background (the "BG" layer) into a dedicated RenderTexture and feeds it
    /// to the black hole material's _BlackHoleBackgroundTex property, so its distortion shader has
    /// something to sample that's guaranteed to be just the background - never the paddle, ball,
    /// or anything else that happens to render Opaque. URP's shared _CameraOpaqueTexture (what the
    /// Shader Graph "Scene Color" node reads) has no way to selectively exclude objects - it's the
    /// whole camera's opaque pass or nothing - so a second, layer-culled camera is the standard way
    /// to isolate "just this background" for an effect like this.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class BlackHoleBackgroundCapture : MonoBehaviour
    {
        [Tooltip("The main gameplay camera to mirror (position, orthographic size) - the capture " +
                 "camera must see exactly the same view the player does for the distortion UVs to " +
                 "line up with what's actually on screen. Falls back to Camera.main if unset.")]
        public Camera sourceCamera;

        [Tooltip("The black hole's own material - the captured background gets assigned to its " +
                 "_BlackHoleBackgroundTex property every frame.")]
        public Material targetMaterial;

        private const string BackgroundLayerName = "BG";
        private const string TexturePropertyName = "_BlackHoleBackgroundTex";

        private Camera _captureCamera;
        private RenderTexture _renderTexture;
        private int _lastWidth, _lastHeight;

        private void Awake()
        {
            _captureCamera = GetComponent<Camera>();
            _captureCamera.cullingMask = 1 << LayerMask.NameToLayer(BackgroundLayerName);
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.black;

            if (sourceCamera == null) sourceCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (sourceCamera == null) return;

            EnsureRenderTexture();

            // Mirror the gameplay camera's view exactly - this is a 2D game, so position and
            // orthographic size are the only things that ever actually change.
            transform.position = sourceCamera.transform.position;
            transform.rotation = sourceCamera.transform.rotation;
            _captureCamera.orthographic = sourceCamera.orthographic;
            _captureCamera.orthographicSize = sourceCamera.orthographicSize;
            _captureCamera.nearClipPlane = sourceCamera.nearClipPlane;
            _captureCamera.farClipPlane = sourceCamera.farClipPlane;
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(2, Screen.width);
            int height = Mathf.Max(2, Screen.height);
            if (_renderTexture != null && width == _lastWidth && height == _lastHeight) return;

            if (_renderTexture != null) _renderTexture.Release();
            _renderTexture = new RenderTexture(width, height, 16) { name = "BlackHoleBackgroundCapture" };
            _captureCamera.targetTexture = _renderTexture;
            if (targetMaterial != null) targetMaterial.SetTexture(TexturePropertyName, _renderTexture);

            _lastWidth = width;
            _lastHeight = height;
        }

        private void OnDestroy()
        {
            if (_renderTexture != null) _renderTexture.Release();
        }
    }
}
