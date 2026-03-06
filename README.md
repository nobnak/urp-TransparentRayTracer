# URP Translucent Ray Tracer (jp.nobnak.trt)

A ray tracing package for **Universal Render Pipeline (URP)**. It composites **DXR ray tracing with translucent object support** into the URP camera output via `RayTracingRendererFeature` and RTAS management.

---

## Sample output

| [![Thumbnail](https://img.youtube.com/vi/_DGBGVbyu2c/maxresdefault.jpg)](https://youtube.com/shorts/_DGBGVbyu2c) | [![Thumbnail](https://img.youtube.com/vi/XD7UkK9tapw/maxresdefault.jpg)](https://youtube.com/shorts/XD7UkK9tapw) | [![Thumbnail](https://img.youtube.com/vi/xM3FAuqEgtk/maxresdefault.jpg)](https://youtube.com/shorts/xM3FAuqEgtk) |
|:---:|:---:|:---:|

## Requirements

- **Unity 2023.2 or later** (tested with Unity 6000.3)
- **Universal RP 17.0.0** (URP 17)
- **DXR-capable platform** (GPU and build target)

---

## Installation (OpenUPM)

This package is listed on [OpenUPM](https://openupm.com/packages/jp.nobnak.trt). We recommend installing it as follows.

### 1. Add Scoped Registry

1. Open **Edit → Project Settings → Package Manager**.
2. Under **Scoped Registries**, click **+** and set:

   | Field   | Value |
   |--------|--------|
   | Name   | `OpenUPM` |
   | URL    | `https://package.openupm.com` |
   | Scope(s) | `jp.nobnak` |

3. Click **Save**.

### 2. Add the package

1. Open **Window → Package Manager**.
2. Set the **Packages:** dropdown to **My Registries** (or ensure OpenUPM appears in the list if using multiple registries).
3. Select **URP Translucent Ray Tracer (jp.nobnak.trt)** and click **Install**.

Alternatively, use **Add package by name** and enter `jp.nobnak.trt`.

Package page: [openupm.com/packages/jp.nobnak.trt](https://openupm.com/packages/jp.nobnak.trt)

---

## Quick start

### 1. Add the Renderer Feature

1. Select your URP asset (e.g. **UniversalRenderPipelineAsset**).
2. In **Renderer List**, select the Renderer you use (e.g. Forward Renderer).
3. Click **Add Renderer Feature** and add **Ray Tracing Renderer Feature**.
4. Assign the bundled **Ray Tracing Shader**: `RayTracingRT` (`Packages/jp.nobnak.trt/.../RayTracingRT.raytrace`).

### 2. Add RayTracingRenderer to the camera

Attach the **RayTracingRenderer** component to the **Camera** that should run ray tracing.

| Setting | Description |
|--------|-------------|
| **Layer Mask** | Layers included in ray tracing (independent of the camera Culling Mask). |
| **Front Face Only** | When on, only front faces are hit (back-face culling). |
| **Max Translucency Depth** | Ray recursion limit in translucent mode (recommended: 8). |
| **Output Mode** | Color, UV, Barycentric, InstanceId, **Translucent**, etc. Use **Translucent** to alpha-blend the RT result over the URP color buffer. |
| **Render Pass Event** | When the RT pass runs (e.g. **AfterRenderingTransparents** to composite on top of URP). |

### 3. RTAS

**RayTracingAccelerationStructureManager** runs as a singleton and builds the RTAS from **MeshRenderer** and **SkinnedMeshRenderer** automatically. No manual geometry registration is needed. Any camera with **RayTracingRenderer** uses this RTAS.

---

## How it works

How the ray-traced image (with alpha) is composited onto the URP camera output.

### Pipeline flow

1. **URP default rendering**  
   Opaque and transparent objects are drawn into the camera’s active color texture.
2. **Ray tracing pass**  
   `RayTracingPass` runs DXR `DispatchRays` and writes the result into a **UAV** (R16G16B16A16_SFloat texture with `enableRandomWrite`).  
   - Each ray carries `color` and `alpha` in `RayPayload`.  
   - When **Output Mode** is **Translucent**, a miss still uses `payload.alpha` to blend the background; the final pixel is written as `float4(color, alpha)`.
3. **Alpha composite blit**  
   The `BlitAlphaComposite` shader blits that UAV onto the camera’s active color texture with **Blend SrcAlpha OneMinusSrcAlpha**.  
   The ray-traced image is composited on top of URP’s frame as a translucent overlay.

### Implementation details

| Item | Description |
|------|-------------|
| **RT output format** | `GraphicsFormat.R16G16B16A16_SFloat`, `enableRandomWrite = true` (UAV). Output includes alpha. |
| **Composite** | `Hidden/URP-RayTracer/BlitAlphaComposite` with `Blend SrcAlpha OneMinusSrcAlpha`, ZWrite Off. |
| **Timing** | `RayTracingRenderer.RenderPassEvent` (e.g. `AfterRenderingTransparents`) runs the RT pass after URP opaque and transparent passes so the RT result is composited on top. |
| **Layers** | `RayTracingRenderer.LayerMask` selects which objects are included in RT. RTAS is built automatically from MeshRenderer / SkinnedMeshRenderer by `RayTracingAccelerationStructureManager`. |
| **Recursion depth** | `Max Translucency Depth` sets the ray recursion limit (recommended: 8). Align with `#pragma max_recursion_depth 16` in the shader. |

### Shader and pass roles

- **RayTracingRT.raytrace**  
  RayGen casts one ray per pixel; Hit/Miss update `RayPayload`’s `color` and `alpha`. In Translucent mode, a miss blends the background with `(1 - alpha)` and writes `float4(color, alpha)` to `_OutputTexture`.
- **RayTracingPass**  
  Blits the RT result into the active color texture via `Blitter.BlitTexture(..., GetAlphaCompositeBlitMaterial(), 0)`. In Translucent mode this blit overlays the RT image with alpha on top of URP’s render.

Together, this produces a translucent overlay of the ray-traced result on URP’s frame buffer.

---

## License

MIT License. Copyright (c) 2026 Nakata Nobuyuki. See [LICENSE](LICENSE) in the repository for the full text.
