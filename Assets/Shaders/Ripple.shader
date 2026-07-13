Shader "Custom/PolarBreakout/Ripple"
{
    Properties
    {
        _Progress ("Progress (0-1)", Range(0, 1)) = 0
        _Width ("Ring Width", Range(0.01, 1)) = 0.2
        _Distortion ("Distortion Strength", Range(0, 0.2)) = 0.03
        _RingColor ("Ring Tint", Color) = (1, 1, 1, 1)
        _RingIntensity ("Ring Intensity", Range(0, 5)) = 1.5
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
            Name "Ripple"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 localUV : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Progress;
                float _Width;
                float _Distortion;
                float4 _RingColor;
                float _RingIntensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.localUV = input.uv;
                output.screenPos = ComputeScreenPos(positions.positionCS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.localUV - 0.5;
                float localDist = length(centered) * 2.0;

                float ringMask = 1.0 - saturate(abs(localDist - _Progress) / max(_Width, 0.0001));
                ringMask = saturate(ringMask);
                float fadeEnvelope = 1.0 - _Progress;

                float2 dir = localDist > 0.0001 ? centered / (localDist * 0.5) : float2(0, 0);
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 distortedUV = screenUV + dir * ringMask * _Distortion * fadeEnvelope;

                float3 sceneColor = SampleSceneColor(distortedUV);
                float3 outColor = sceneColor + _RingColor.rgb * _RingIntensity * ringMask * fadeEnvelope;
                float outAlpha = ringMask * fadeEnvelope * _RingColor.a;

                return float4(outColor, outAlpha);
            }
            ENDHLSL
        }
    }
}
