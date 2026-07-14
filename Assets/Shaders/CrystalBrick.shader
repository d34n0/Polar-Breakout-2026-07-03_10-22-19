Shader "Custom/PolarBreakout/CrystalBrick"
{
    Properties
    {
        _PurpleColor ("Crystal Color", Color) = (0.55, 0.33, 0.85, 1)

        _TreeTex ("Background Tree", 2D) = "black" {}
        _TreeStrength ("Tree Visibility", Range(0, 1)) = 0.6

        _SheenTex ("Sheen Texture", 2D) = "black" {}
        _SheenColor ("Sheen Color", Color) = (1, 1, 1, 1)
        _SheenIntensity ("Sheen Intensity", Range(0, 3)) = 1.2
        _SheenScrollSpeed ("Sheen Scroll Speed (X,Y)", Vector) = (0.3, 0.2, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "CrystalBrick"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_TreeTex);
            SAMPLER(sampler_TreeTex);
            TEXTURE2D(_SheenTex);
            SAMPLER(sampler_SheenTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PurpleColor;
                float4 _TreeTex_ST;
                float _TreeStrength;
                float4 _SheenTex_ST;
                float4 _SheenColor;
                float _SheenIntensity;
                float4 _SheenScrollSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Background tree: shows through as a lighter tint wherever the (grayscale) tree
                // texture is bright, so it reads as a silhouette glowing inside the crystal.
                // Left as an empty/black texture, it contributes nothing.
                float2 treeUV = TRANSFORM_TEX(input.uv, _TreeTex);
                float4 treeSample = SAMPLE_TEXTURE2D(_TreeTex, sampler_TreeTex, treeUV);
                float treeMask = saturate(dot(treeSample.rgb, float3(0.299, 0.587, 0.114)) * treeSample.a);
                float3 colorWithTree = lerp(_PurpleColor.rgb, lerp(_PurpleColor.rgb, float3(1, 1, 1), 0.7), treeMask * _TreeStrength);

                // Animated sheen: scrolls the sheen mask over time and adds it as a white highlight.
                // Swap in a diagonal streak texture for a moving shine; left black, it's invisible.
                float2 sheenUV = TRANSFORM_TEX(input.uv, _SheenTex) + _SheenScrollSpeed.xy * _Time.y;
                float4 sheenSample = SAMPLE_TEXTURE2D(_SheenTex, sampler_SheenTex, sheenUV);
                float sheenMask = saturate(dot(sheenSample.rgb, float3(0.299, 0.587, 0.114)) * sheenSample.a);

                float3 finalColor = saturate(colorWithTree + sheenMask * _SheenIntensity * _SheenColor.rgb);
                return float4(finalColor, _PurpleColor.a);
            }
            ENDHLSL
        }
    }
}
