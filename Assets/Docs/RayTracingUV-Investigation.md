# Unity DXR でメッシュ UV を使う方法（詳細調査）

## 1. 前提：DXR で渡されるもの

- **closest-hit / anyhit** に渡るのは **SV_IntersectionAttributes** のみ。  
  Unity の三角形ジオメトリではこれは **バリセントリック座標 `float2`**（(u, v)、3頂点の重みは (1-u-v, u, v)）。
- 頂点バッファ・インデックスバッファは **ジオメトリ（BLAS）に紐づいており**、ヒット時に「そのジオメトリ用のバッファ」が DXR によって利用可能になる。Unity はこれらを特定のグローバル名でヒットシェーダーにバインドする。

---

## 2. Unity の頂点フェッチ API：UnityRayTracingMeshUtils

### 2.1 公式の言及

[RayTracingGeometryInstanceConfig.vertexAttributes](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Rendering.RayTracingGeometryInstanceConfig-vertexAttributes.html) より:

> You can access other vertex attributes in shader code using **helper functions from UnityRayTracingMeshUtils.cginc** header.

頂点フォーマットに Position に加え **Normal / TexCoord0** などを `VertexAttributeDescriptor` で指定し、そのジオメトリを RTAS に追加すると、ヒットシェーダー側でこのヘルパーから頂点属性（UV 含む）を読める。

### 2.2 インクルードと場所

- ファイル名: **`UnityRayTracingMeshUtils.cginc`**
- Unity 組み込みの CG インクルードの一つ。  
  インストール先の `Data/CGIncludes/`（Windows）などにあり、プロジェクトに同梱されていなくても `#include "UnityRayTracingMeshUtils.cginc"` で参照できる。
- **HLSLPROGRAM** 内でも使用可能（サンプル [RayTracingDynamicMeshGeometry](https://github.com/INedelcu/RayTracingDynamicMeshGeometry) で HLSL から include している）。

### 2.3 シェーダーが前提とするバッファ・定数（エンジンがバインド）

| 名前 | 型 | 説明 |
|------|-----|------|
| `unity_MeshVertexBuffer_RT` | ByteAddressBuffer | 頂点バッファ |
| `unity_MeshIndexBuffer_RT` | ByteAddressBuffer | インデックスバッファ |
| `unity_MeshVertexDeclaration_RT` | StructuredBuffer | 頂点属性ごとのオフセット・フォーマット・次元 |
| `unity_MeshIndexSize_RT` | uint | 0=非インデックス, 2=16bit, 4=32bit |
| `unity_MeshVertexSize_RT` | uint | 1頂点あたりバイト数（ストライド） |
| `unity_MeshBaseVertex_RT` | uint | インデックスに足すベース頂点 |
| `unity_MeshIndexStart_RT` | uint | インデックスバッファの開始オフセット |
| `unity_MeshStartVertex_RT` | uint | 非インデックス時の先頭頂点インデックス |

これらは **ジオメトリを RTAS に追加する際に、そのジオメトリ用の頂点・インデックスとして Unity がセットする**。  
C# で明示的に SetBuffer する必要はない（Geometry / Mesh 経由で追加した場合）。

### 2.4 提供される関数

- **`uint3 UnityRayTracingFetchTriangleIndices(uint primitiveIndex)`**  
  - 三角形インデックスから 3 頂点インデックスを取得。  
  - 通常は `PrimitiveIndex()` を渡す。

- **`float2 UnityRayTracingFetchVertexAttribute2(uint vertexIndex, uint attributeType)`**  
  - 指定頂点の 2 成分属性（主に **TexCoord0**）を取得。

- **`float3 UnityRayTracingFetchVertexAttribute3(uint vertexIndex, uint attributeType)`**  
  - Position / Normal など 3 成分用。

- **`float4 UnityRayTracingFetchVertexAttribute4(...)`**  
  - 4 成分用。

属性タイプ定数（`attributeType`）:

- `kVertexAttributePosition` = 0  
- `kVertexAttributeNormal` = 1  
- `kVertexAttributeTangent` = 2  
- `kVertexAttributeColor` = 3  
- **`kVertexAttributeTexCoord0` = 4**  
- TexCoord1〜7 = 5〜11  

### 2.5 closest-hit で UV を取得する流れ（HLSL）

```hlsl
#include "UnityRayTracingMeshUtils.cginc"

struct AttributeData { float2 barycentrics; };

[shader("closesthit")]
void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attr : SV_IntersectionAttributes)
{
    uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float2 uv0 = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0);
    float2 uv1 = UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0);
    float2 uv2 = UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0);

    float w0 = 1.0 - attr.barycentrics.x - attr.barycentrics.y;
    float w1 = attr.barycentrics.x;
    float w2 = attr.barycentrics.y;
    float2 uv = w0 * uv0 + w1 * uv1 + w2 * uv2;

    payload.color = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0).rgb * _BaseColor.rgb;
}
```

**重要**: `vertexAttributes`（またはメッシュの頂点レイアウト）に **TexCoord0** が含まれていること。  
含まれていないと `unity_MeshVertexDeclaration_RT` のオフセットが無効になり、正しい UV が取れない。

---

## 3. C# 側：インスタンスの追加方法と UV の扱い

Unity には主に次の 3 通りがある。

### 3.1 AddInstance(Renderer, ...) — 現在のプロジェクト

- **Renderer** からメッシュ・マテリアルを取得して 1 インスタンス追加。
- 頂点／インデックスは **その Renderer の Mesh** のものが BLAS 構築に使われる。
- ヒット時に **どのシェーダーが呼ばれるか** はレンダーパイプラインと **SetRayTracingShaderPass** に依存。
  - 本プロジェクトでは **SetRayTracingShaderPass(rayTracingShader, "RayTracing")** を呼び、**DispatchRays に渡した .raytrace の "RayTracing" パス**に含まれる closest-hit を使用している。
- このとき、Unity が **ヒットしたジオメトリの頂点バッファを `unity_MeshVertexBuffer_RT` 等としてバインドしているか** は公式には明示されていないが、DXR 上は「その BLAS の頂点バッファ」がヒット時に紐づくため、**同じ名前でバインドしている可能性はある**。
- **試す価値あり**:  
  - メッシュに **UV0** がある（Mesh.uv が存在し、頂点レイアウトに TexCoord0 が含まれる）ことを確認したうえで、  
  - **.raytrace の closest-hit** に `#include "UnityRayTracingMeshUtils.cginc"` と上記の UV 取得コードを追加し、  
  - 動くか確認する。  
- 動かない、または不安定な場合は、**3.2 または 3.3** に切り替える。

### 3.2 AddInstance(ref RayTracingMeshInstanceConfig, matrix, ...)

- **Mesh + サブメッシュ + Material** をまとめた設定で追加。
- Mesh は **頂点レイアウトに TexCoord0 を含む** 必要がある（例: `SetVertexBufferParams` で TexCoord0 を指定した Mesh、またはインポートしたモデルのメッシュで UV があるもの）。
- **ヒットグループ（closest-hit）は、その Material のシェーダーの「RayTracing 用パス」から取られる**。  
  そのため **SetRayTracingShaderPass(rayTracingShader, "PassName")** で、UV を読む closest-hit を持つ **マテリアル側のパス名** を指定する必要がある。
- マテリアルのシェーダーに、例えば次のようなパスを用意する:
  - `Pass { Name "RayTracing" ... }` など
  - その中で `#include "UnityRayTracingMeshUtils.cginc"` と上記の UV 取得
  - ペイロード構造は **.raytrace の raygen と一致**させる（同じ RayPayload を共有）。
- **RayTracingMeshInstanceConfig** は Mesh をそのまま渡すだけなので、**Mesh に UV さえあれば**、追加の頂点バッファ作成は不要。  
  公式ドキュメントの「other vertex attributes ... accessible ... using helper functions from UnityRayTracingMeshUtils.cginc」は、**Geometry 経由だけでなく Mesh 経由でも同様**と解釈できる。

### 3.3 AddInstance(ref RayTracingGeometryInstanceConfig, matrix, ...)

- 頂点バッファ・インデックスバッファを **自前で用意** する方法。
- **vertexBuffer**: `GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Raw` で作成。  
  中身のレイアウトは **vertexAttributes** で定義。
- **vertexAttributes**:  
  - Position 必須（Float32 / Float16 / SNorm16）。  
  - UV を使う場合は `new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0)` などを追加。
- **indexBuffer** / indexCount / indexStart、**vertexCount** / **vertexStart** も設定。
- メッシュデータを自前で用意するか、**Mesh.GetVertexBuffer(0)** / **Mesh.GetIndexBuffer()** で取得したバッファを流用できる（Mesh のレイアウトと vertexAttributes を一致させる必要あり）。
- この経路でも、Unity はそのジオメトリ用に `unity_MeshVertexBuffer_RT` 等をバインドし、**UnityRayTracingMeshUtils** の関数が使える。

---

## 4. 実装パス別まとめ

| 方式 | C# | シェーダー側 | 備考 |
|------|-----|--------------|------|
| **A. 現状のまま試す** | AddInstance(Renderer) のまま | .raytrace の closest-hit に UnityRayTracingMeshUtils + UV 取得 | メッシュに UV があることだけ確認。動けば最小変更。 |
| **B. Mesh + Material で確実に** | AddInstance(ref **RayTracingMeshInstanceConfig**(mesh, subMesh, material), matrix, ...) | **Material** の RayTracing パスに closest-hit を書き、UnityRayTracingMeshUtils で UV 取得。**SetRayTracingShaderPass**(rtShader, "RayTracing") でそのパスを指定。 | ペイロードを .raytrace と共通化。 |
| **C. 頂点バッファを完全に自前** | AddInstance(ref **RayTracingGeometryInstanceConfig**, matrix, ...)。vertexBuffer / indexBuffer / vertexAttributes（Position + TexCoord0）を設定。 | 同じく UnityRayTracingMeshUtils で UV 取得。 | 動的メッシュやカスタム頂点レイアウト向け。 |

---

## 5. 本プロジェクトで UV を入れる場合の推奨手順

1. **まず A を試す**  
   - RayTracingRT.raytrace の closest-hit に  
     `#include "UnityRayTracingMeshUtils.cginc"` と、  
     `UnityRayTracingFetchTriangleIndices` / `UnityRayTracingFetchVertexAttribute2(..., kVertexAttributeTexCoord0)` およびバリセントリック補間を追加。  
   - 使用しているメッシュに UV があるか確認（Mesh.uv やインポート設定）。  
   - 動作し、かつパフォーマンス・安定性に問題なければそのまま運用可能。

2. **A でバッファがバインドされない／クラッシュする場合**  
   - **B** に切り替え:  
     - 対象オブジェクトだけでもよいので、**RayTracingMeshInstanceConfig(mesh, subMeshIndex, material)** で追加するコードパスを用意。  
     - その material のシェーダー（URP-Unlit 等）の **RayTracing パス** に、上記の UV 取得ロジックを書く。  
     - DispatchRays の前に **SetRayTracingShaderPass(rayTracingShader, "RayTracing")** を呼び、ペイロードを .raytrace と合わせる。

3. **複数メッシュ・動的メッシュを細かく制御したい場合**  
   - **C** を検討: Mesh.GetVertexBuffer / GetIndexBuffer と vertexAttributes（Position + TexCoord0）で **RayTracingGeometryInstanceConfig** を組み、AddInstance(ref config, ...) で追加。

---

## 6. 参考：UnityRayTracingMeshUtils の実装例（抜粋）

- [UnityRayTracingMeshUtils.cginc](https://github.com/TwoTailsGames/Unity-Built-in-Shaders/blob/master/CGIncludes/UnityRayTracingMeshUtils.cginc)（Built-in リポジトリ）
- サンプルで **Position のみ** フェッチしている例: [RayTracingDynamicMeshGeometry](https://github.com/INedelcu/RayTracingDynamicMeshGeometry)（Assets/Shaders/MeshInstanceDynamicGeometry.shader の closest-hit で `UnityRayTracingFetchVertexAttribute3` / `UnityRayTracingFetchTriangleIndices` を使用）
- UV まで含めた頂点属性補間の説明: [RayTracingShader_VertexAttributeInterpolation](https://github.com/INedelcu/RayTracingShader_VertexAttributeInterpolation)

---

## 7. 実例：シリコンスタジオブログ（Unity6 + URP）

[Unity6 + URP で動くパストレーサーを実装してみよう Part 1](https://blog.siliconstudio.co.jp/2024/12/1923/) では、マテリアルの closest-hit で **UnityRaytracingMeshUtils.cginc** を include し、次の流れで頂点データ（UV 含む）を取得している。

- `UnityRayTracingFetchTriangleIndices(PrimitiveIndex())` でヒットした三角形の 3 頂点インデックスを取得
- 各頂点で `UnityRayTracingFetchVertexAttribute3`（Position/Normal）/ `UnityRayTracingFetchVertexAttribute2`（TexCoord0）などを呼ぶ
- バリセントリック `(1 - u - v, u, v)` で補間してヒット位置の UV を計算
- その UV で `SAMPLE_TEXTURE2D_LOD(_BaseMap, ..., v.texCoord0, 0)` によりベースマップをサンプル

また、**SetRayTracingShaderPass(rayTracingShader, "MyPathTracing")** で「マテリアル側の Pass 名」を指定し、closest-hit を **マテリアルのシェーダ**（Lit のコピーに RayTracing パスを追加したもの）から実行している。  
この構成にすると、Unity がジオメトリに紐づく頂点バッファをヒット時にバインドし、`UnityRayTracingFetchVertexAttribute*` が利用可能になる。

---

## 8. 公式ドキュメントリンク

- [RayTracingGeometryInstanceConfig.vertexAttributes](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Rendering.RayTracingGeometryInstanceConfig-vertexAttributes.html) — vertexAttributes と UnityRayTracingMeshUtils の記載
- [RayTracingGeometryInstanceConfig.vertexBuffer](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Rendering.RayTracingGeometryInstanceConfig-vertexBuffer.html) — Vertex | Raw の指定
- [RayTracingAccelerationStructure.AddInstance](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Rendering.RayTracingAccelerationStructure.AddInstance.html) — Renderer / MeshInstanceConfig / GeometryInstanceConfig の各オーバーロード
- [RayTracingAccelerationStructure.AddInstances](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Rendering.RayTracingAccelerationStructure.AddInstances.html) — AddInstances + インスタンスデータとビルトインバッファ（SH, PrevObjectToWorld 等）
- [CommandBuffer.SetRayTracingShaderPass](https://docs.unity3d.com/ScriptReference/Rendering.CommandBuffer.SetRayTracingShaderPass.html) — ヒットシェーダーに使うパス（マテリアル側）の指定
