Shader "Custom/URP-Unlit" {
    Properties {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass {
            Cull Off
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes IN) {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }

        Pass {
            Name "RayTracing"
            Tags { "LightMode" = "RayTracing" }
            HLSLPROGRAM
            #pragma raytracing RayTracing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UnityRayTracingMeshUtils.cginc"

            #define OUTPUT_MODE_COLOR 0u
            #define OUTPUT_MODE_UV 1u
            #define OUTPUT_MODE_BARYCENTRIC 2u
            #define OUTPUT_MODE_INSTANCE_ID 3u
            #define OUTPUT_MODE_TRANSPARENT 4u
            RaytracingAccelerationStructure _AccelStruct;
            uint _InstanceMask;
            uint _OutputMode;
            uint _MaxTransparencyDepth;
            uint _InstanceIdColorMix;
            uint _RayFlags;

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            struct RayPayload {
                float2 barycentrics;
                uint hit;
                uint instanceId;
                uint primitiveIndex;
                float3 color;
                float alpha;
                uint depth;
                float2 uv;
                float thickness;
                uint thicknessProbeInstanceId;
                uint isThicknessProbe;
            };
            struct AttributeData {
                float2 barycentrics;
            };

            #define RAY_FLAGS_CULL_BACK 0x10u
            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attr : SV_IntersectionAttributes) {
                if (payload.isThicknessProbe != 0u) {
                    if (InstanceID() == payload.thicknessProbeInstanceId)
                        payload.thickness = RayTCurrent();
                    return;
                }
                payload.barycentrics = attr.barycentrics;
                payload.hit = 1;
                payload.instanceId = InstanceID();
                payload.primitiveIndex = PrimitiveIndex();
                uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float2 uv0 = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0);
                float2 uv1 = UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0);
                float2 uv2 = UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0);
                float w0 = 1.0 - attr.barycentrics.x - attr.barycentrics.y;
                float2 uv = w0 * uv0 + attr.barycentrics.x * uv1 + attr.barycentrics.y * uv2;
                payload.uv = uv;

                half4 base = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0) * _BaseColor;
                float3 hitColor = base.rgb;
                float hitAlpha = base.a;
                if (_InstanceIdColorMix != 0u) {
                    float id = (float)InstanceID();
                    hitColor *= float3(frac(id * 0.067), frac(id * 0.127 + 0.3), frac(id * 0.197 + 0.6));
                }

                if (_OutputMode == OUTPUT_MODE_COLOR) {
                    payload.color = hitColor;
                    payload.alpha = 1.0;
                } else if (_OutputMode == OUTPUT_MODE_TRANSPARENT) {
                    float effectiveAlpha = hitAlpha;
                    if ((_RayFlags & RAY_FLAGS_CULL_BACK) != 0u) {
                        float3 hitPos = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
                        float3 rayDir = WorldRayDirection();
                        const float kThickEpsilon = 1e-4;
                        const float kThickTMin = 1e-3;
                        RayDesc thickRay;
                        thickRay.Origin = hitPos + rayDir * kThickEpsilon;
                        thickRay.Direction = rayDir;
                        thickRay.TMin = kThickTMin;
                        thickRay.TMax = 100000.0;
                        RayPayload thickPayload = payload;
                        thickPayload.isThicknessProbe = 1u;
                        thickPayload.thicknessProbeInstanceId = InstanceID();
                        thickPayload.thickness = 0.0;
                        uint instanceMask = _InstanceMask != 0u ? _InstanceMask : 0xFFFFFFFFu;
                        TraceRay(_AccelStruct, 0u, instanceMask, 0, 0, 0, thickRay, thickPayload);
                        float d = thickPayload.thickness;
                        if (d > 0.0)
                            effectiveAlpha = 1.0 - (1.0 - hitAlpha) * exp(-max(hitAlpha, 0.01) * d);
                    }
                    float t = 1.0 - payload.alpha;
                    payload.color += t * effectiveAlpha * hitColor;
                    payload.alpha += t * saturate(effectiveAlpha);
                    if (payload.alpha < 1.0 - 1e-5 && payload.depth < _MaxTransparencyDepth) {
                        float3 hitPos = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
                        float3 rayDir = WorldRayDirection();
                        RayDesc ray;
                        ray.Origin = hitPos + rayDir * 1e-4;
                        ray.Direction = rayDir;
                        ray.TMin = 0.0;
                        ray.TMax = 100000.0;
                        payload.depth++;
                        uint instanceMask = _InstanceMask != 0u ? _InstanceMask : 0xFFFFFFFFu;
                        TraceRay(_AccelStruct, _RayFlags, instanceMask, 0, 0, 0, ray, payload);
                    }
                }
                // UV / Barycentric / InstanceId は barycentrics, uv, instanceId を設定済み。RayGen が参照する。
            }
            ENDHLSL
        }
    }
}
