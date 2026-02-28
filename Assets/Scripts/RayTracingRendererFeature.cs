/// <summary>
/// カメラにアタッチされた RayTracingRenderer を参照し、RT パスをエンキューする。RayTracingShader は本 Feature で保持する。
/// </summary>
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RayTracingRendererFeature : ScriptableRendererFeature {
    [SerializeField] RayTracingShader _rayTracingShader;

    RayTracingPass _pass;

    public override void Create() {
        _pass = new RayTracingPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (_rayTracingShader == null) return;

        var cam = renderingData.cameraData.camera;
        if (cam == null) return;

        var rtRenderer = cam.GetComponent<RayTracingRenderer>();
        if (rtRenderer == null || !rtRenderer.IsValidForPass()) return;

        _pass.SetTarget(rtRenderer, _rayTracingShader);
        renderer.EnqueuePass(_pass);
    }
}
