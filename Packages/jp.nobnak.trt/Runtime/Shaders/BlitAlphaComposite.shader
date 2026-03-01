// RT 出力をカメラ映像へアルファ合成する Blit 用。Blitter が _BlitTexture にソースをバインドする。
Shader "Hidden/URP-RayTracer/BlitAlphaComposite" {
    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 0
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest Always
        Cull Off

        Pass {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScaling.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitScaleBias;

            struct Attributes {
                uint vertexID : SV_VertexID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
                output.positionCS = pos;
                output.texcoord = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target {
                return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_BlitTexture, input.texcoord.xy, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
