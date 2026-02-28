/// <summary>
/// RT を実行し、結果をカメラのアクティブカラーテクスチャへ Blit する ScriptableRenderPass。
/// UAV が必要なため、DispatchRays は enableRandomWrite の一時テクスチャへ出力してからカメラへ Blit する。
/// </summary>
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class RayTracingPass : ScriptableRenderPass {
    const int RayFlags_CullBackFacingTriangles = 0x10;
    const string PassName = "RayTracing";

    RayTracingRenderer _renderer;
    RayTracingShader _shader;

    class PassData {
        public RayTracingRenderer renderer;
        public RayTracingShader shader;
        public TextureHandle rtOutputTexture;
        public TextureHandle activeColorTexture;
    }

    public void SetTarget(RayTracingRenderer renderer, RayTracingShader shader) {
        _renderer = renderer;
        _shader = shader;
        if (renderer != null) renderPassEvent = renderer.RenderPassEvent;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext) {
        if (_renderer == null || _shader == null || !_renderer.IsValidForPass()) return;

        var cam = _renderer.Camera;
        if (cam == null) return;
        int w = cam.pixelWidth;
        int h = cam.pixelHeight;
        if (w <= 0 || h <= 0) return;

        var rtOutputDesc = new TextureDesc(w, h) {
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = true,
            name = "RayTracingUAV"
        };
        TextureHandle rtOutput = renderGraph.CreateTexture(rtOutputDesc);

        using (var builder = renderGraph.AddUnsafePass<PassData>(PassName, out var passData)) {
            var resourceData = frameContext.Get<UniversalResourceData>();
            passData.renderer = _renderer;
            passData.shader = _shader;
            passData.rtOutputTexture = rtOutput;
            passData.activeColorTexture = resourceData.activeColorTexture;
            builder.UseTexture(passData.rtOutputTexture, AccessFlags.Write);
            builder.UseTexture(passData.activeColorTexture, AccessFlags.Write);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }
    }

    static void ExecutePass(PassData data, UnsafeGraphContext context) {
        var renderer = data.renderer;
        var shader = data.shader;
        var cam = renderer.Camera;
        var rtas = renderer.Rtas;
        if (cam == null || rtas == null) return;

        int w = cam.pixelWidth;
        int h = cam.pixelHeight;
        if (w <= 0 || h <= 0) return;

        var cmd = context.cmd;
        int instanceMask = (renderer.LayerMask.value & 0xFF) != 0 ? (renderer.LayerMask.value & 0xFF) : 0xFF;
        int rayFlags = renderer.FrontFaceOnly ? RayFlags_CullBackFacingTriangles : 0;

        cmd.SetRayTracingAccelerationStructure(shader, "_AccelStruct", rtas);
        cmd.SetRayTracingIntParam(shader, "_RenderWidth", w);
        cmd.SetRayTracingIntParam(shader, "_RenderHeight", h);
        cmd.SetRayTracingIntParam(shader, "_InstanceCount", renderer.InstanceCount);
        cmd.SetRayTracingIntParam(shader, "_InstanceMask", instanceMask);
        cmd.SetRayTracingIntParam(shader, "_Orthographic", cam.orthographic ? 1 : 0);
        cmd.SetRayTracingIntParam(shader, "_RayFlags", rayFlags);

        cmd.SetRayTracingVectorParam(shader, "_CameraFrustum", GetCameraFrustum(cam, w, h));
        cmd.SetRayTracingVectorParam(shader, "_CameraPosition", cam.transform.position);
        cmd.SetRayTracingVectorParam(shader, "_CameraBackgroundColor", cam.backgroundColor);
        cmd.SetRayTracingMatrixParam(shader, "_CameraToWorldMatrix", cam.cameraToWorldMatrix);
        cmd.SetRayTracingIntParam(shader, "_OutputMode", (int)renderer.CurrentOutputMode);
        cmd.SetGlobalInt(Shader.PropertyToID("_OutputMode"), (int)renderer.CurrentOutputMode);
        cmd.SetGlobalInt(Shader.PropertyToID("_InstanceMask"), instanceMask);
        cmd.SetGlobalInt(Shader.PropertyToID("_MaxTransparencyDepth"), renderer.MaxTransparencyDepth);
        cmd.SetGlobalInt(Shader.PropertyToID("_InstanceIdColorMix"), renderer.MultiplyInstanceIdColor ? 1 : 0);
        cmd.SetGlobalInt(Shader.PropertyToID("_RayFlags"), rayFlags);
        cmd.SetRayTracingTextureParam(shader, "_OutputTexture", data.rtOutputTexture);
        cmd.SetRayTracingShaderPass(shader, "RayTracing");
        cmd.DispatchRays(shader, "MainRayGen", (uint)w, (uint)h, 1, cam);

        CommandBuffer nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
        nativeCmd.SetRenderTarget(data.activeColorTexture);
        Blitter.BlitTexture(nativeCmd, data.rtOutputTexture, new Vector4(1, 1, 0, 0), 0, false);
    }

    static Vector4 GetCameraFrustum(Camera camera, int outputWidth, int outputHeight) {
        float aspect = (float)outputWidth / Mathf.Max(outputHeight, 1);
        if (camera.orthographic) {
            float h = Mathf.Max(camera.orthographicSize, 0.001f);
            float w = h * aspect;
            return new Vector4(-w, w, -h, h);
        }
        var P = camera.projectionMatrix;
        float cotHalfFov = P.m11;
        if (Mathf.Abs(cotHalfFov) < 0.001f) cotHalfFov = 0.001f;
        float tanHalfFov = 1f / cotHalfFov;
        float halfH = tanHalfFov;
        float halfW = tanHalfFov * aspect;
        return new Vector4(-halfW, halfW, -halfH, halfH);
    }
}
