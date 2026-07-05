Shader "Hidden/PolarBreakout/ColorblindFilter"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "ColorblindFilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float3 _ColorMatrixR;
            float3 _ColorMatrixG;
            float3 _ColorMatrixB;

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 rgb = color.rgb;
                float3 outRgb;
                outRgb.r = dot(rgb, _ColorMatrixR);
                outRgb.g = dot(rgb, _ColorMatrixG);
                outRgb.b = dot(rgb, _ColorMatrixB);
                return float4(outRgb, color.a);
            }
            ENDHLSL
        }
    }
}
