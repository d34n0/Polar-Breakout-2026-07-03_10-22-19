using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PolarBreakout.Rendering
{
    /// <summary>
    /// Fullscreen colorblind simulation/correction filter, driven by GameSettings.ColorblindFilter.
    /// Uses Unity 6's RenderGraph recording API (RecordRenderGraph), not the older Blit()
    /// pattern - matches PC_Renderer.asset's existing renderer-feature slot pattern (currently
    /// just ScreenSpaceAmbientOcclusion, a built-in feature; this is the project's first custom
    /// ScriptableRendererFeature).
    /// </summary>
    public class ColorblindFilterFeature : ScriptableRendererFeature
    {
        public Shader shader;

        private Material _material;
        private ColorblindFilterPass _pass;

        public override void Create()
        {
            if (shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(shader);
            _pass = new ColorblindFilterPass(_material)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;
            if (GameSettings.ColorblindFilter == ColorblindMode.None) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
        }
    }

    public class ColorblindFilterPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private static readonly int ColorMatrixR = Shader.PropertyToID("_ColorMatrixR");
        private static readonly int ColorMatrixG = Shader.PropertyToID("_ColorMatrixG");
        private static readonly int ColorMatrixB = Shader.PropertyToID("_ColorMatrixB");

        private class PassData
        {
            public Material material;
            public TextureHandle source;
        }

        public ColorblindFilterPass(Material material)
        {
            _material = material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            SetColorMatrix(GameSettings.ColorblindFilter);

            TextureHandle source = resourceData.cameraColor;

            var descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_ColorblindFilterTemp";
            descriptor.clearBuffer = false;
            descriptor.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Colorblind Filter", out var passData))
            {
                passData.material = _material;
                passData.source = source;

                builder.UseTexture(source);
                builder.SetRenderAttachment(destination, 0);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private void SetColorMatrix(ColorblindMode mode)
        {
            Vector3 r, g, b;
            switch (mode)
            {
                case ColorblindMode.Protanopia:
                    r = new Vector3(0.567f, 0.433f, 0f);
                    g = new Vector3(0.558f, 0.442f, 0f);
                    b = new Vector3(0f, 0.242f, 0.758f);
                    break;
                case ColorblindMode.Deuteranopia:
                    r = new Vector3(0.625f, 0.375f, 0f);
                    g = new Vector3(0.700f, 0.300f, 0f);
                    b = new Vector3(0f, 0.300f, 0.700f);
                    break;
                case ColorblindMode.Tritanopia:
                    r = new Vector3(0.950f, 0.050f, 0f);
                    g = new Vector3(0f, 0.433f, 0.567f);
                    b = new Vector3(0f, 0.475f, 0.525f);
                    break;
                default:
                    r = new Vector3(1f, 0f, 0f);
                    g = new Vector3(0f, 1f, 0f);
                    b = new Vector3(0f, 0f, 1f);
                    break;
            }
            _material.SetVector(ColorMatrixR, r);
            _material.SetVector(ColorMatrixG, g);
            _material.SetVector(ColorMatrixB, b);
        }
    }
}
