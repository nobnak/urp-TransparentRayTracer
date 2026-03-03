# URP Translucent Ray Tracer (jp.nobnak.trt)

A ray tracing package for **Universal Render Pipeline (URP)**. It composites **DXR ray tracing with translucent object support** into the URP camera output. The `RayTracingRendererFeature` and RTAS (Ray Tracing Acceleration Structure) management blit the ray-traced result into the camera.

## Sample output

| [![Thumbnail](https://img.youtube.com/vi/_DGBGVbyu2c/maxresdefault.jpg)](https://youtube.com/shorts/_DGBGVbyu2c) | [![Thumbnail](https://img.youtube.com/vi/XD7UkK9tapw/maxresdefault.jpg)](https://youtube.com/shorts/XD7UkK9tapw) | [![Thumbnail](https://img.youtube.com/vi/xM3FAuqEgtk/maxresdefault.jpg)](https://youtube.com/shorts/xM3FAuqEgtk) |
|:---:|:---:|:---:|

## Requirements

- **Unity 2023.2 or later** (tested with Unity 6000.3)
- **Universal RP 17.0.0** (URP 17)
- Ray tracing–capable platform (DXR-capable GPU / build target)

## Installation (recommended: OpenUPM)

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

- Package page: [https://openupm.com/packages/jp.nobnak.trt](https://openupm.com/packages/jp.nobnak.trt)

## Usage

### 1. Add the feature to your URP Renderer

1. Select your URP asset (e.g. **UniversalRenderPipelineAsset**).
2. In **Renderer List**, select the Renderer you use (e.g. Forward Renderer).
3. Click **Add Renderer Feature** and add **Ray Tracing Renderer Feature**.
4. Assign the bundled **Ray Tracing Shader**: `RayTracingRT` (`Packages/jp.nobnak.trt/.../RayTracingRT.raytrace`).

### 2. Add RayTracingRenderer to the camera

Attach the **RayTracingRenderer** component to the **Camera** that should run ray tracing.

- **Layer Mask**: Layers to include in ray tracing (independent from the camera’s Culling Mask).
- **Front Face Only**: When on, only front faces are hit (back-face culling).
- **Max Translucency Depth**: Maximum ray recursion depth in translucent mode (recommended: 8).
- **Output Mode**: Color, UV, Barycentric, InstanceId, Translucent, etc.
- **Render Pass Event**: Where in the pipeline the ray tracing pass runs (e.g. Around **AfterRendering** if you want RT-only output).

### 3. RTAS (ray tracing acceleration structure)

**RayTracingAccelerationStructureManager** runs as a singleton in the scene and builds the RTAS from **MeshRenderer** and **SkinnedMeshRenderer** automatically. You do not need to register geometry manually. As long as the camera has **RayTracingRenderer**, it will use this RTAS for ray tracing.

### 4. Translucent mode

With **Output Mode** set to **Translucent**, ray tracing accounts for translucent objects and the result is alpha-blended (blit) into the URP color buffer.

## License

MIT License. Copyright (c) 2026 Nakata Nobuyuki. See [LICENSE](LICENSE) in the repository for the full text.
