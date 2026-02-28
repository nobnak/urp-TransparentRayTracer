/// <summary>
/// カメラにアタッチし、RF 用パラメータを保持する。RTAS は RayTracingAccelerationStructureManager シングルトンで共有される。
/// 描画は RayTracingRendererFeature のパスでカメラへ直接出力する。
/// </summary>
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RayTracingRenderer : MonoBehaviour {
    [Header("RT target layers")]
    [Tooltip("RT で対象にするレイヤー（カメラの Culling Mask とは別）。例: カメラは Nothing、RT は Everything")]
    [SerializeField] LayerMask _layerMask = -1;

    public enum OutputMode {
        Color = 0,
        UV = 1,
        Barycentric = 2,
        InstanceId = 3,
        Transparent = 4
    }

    [Header("Ray hit")]
    [Tooltip("オフ: 両面ヒット / オン: 前面のみヒット（裏面カリング）")]
    [SerializeField] bool _frontFaceOnly;

    [Header("Transparent")]
    [Tooltip("半透明モードの最大レイ再帰深度。8 推奨。")]
    [SerializeField, Range(2, 16)] int _maxTransparencyDepth = 8;
    [Tooltip("有効時、表面色に InstanceID 色を乗算する（半透明の動作確認用）。")]
    [SerializeField] bool _multiplyInstanceIdColor;

    [Header("Output")]
    [SerializeField] OutputMode _outputMode = OutputMode.Color;

    [Header("Debug")]
    [SerializeField] bool _showDebugOutputTexture;
    [SerializeField, Range(0.05f, 1f)] float _debugDisplayHeightPercent = 0.25f;

    [Header("Pass timing")]
    [Tooltip("RT パスをパイプラインのどこで実行するか。RT のみ表示なら AfterRendering 付近を推奨。")]
    [SerializeField] RenderPassEvent _renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

    Camera _camera;

    public Camera Camera => _camera;
    public RayTracingAccelerationStructure Rtas => RayTracingAccelerationStructureManager.Instance?.Rtas;
    public int InstanceCount => RayTracingAccelerationStructureManager.Instance?.InstanceCount ?? 0;

    public LayerMask LayerMask => _layerMask;
    public bool FrontFaceOnly => _frontFaceOnly;
    public OutputMode CurrentOutputMode => _outputMode;
    public int MaxTransparencyDepth => _maxTransparencyDepth;
    public bool MultiplyInstanceIdColor => _multiplyInstanceIdColor;
    public bool ShowDebugOutputTexture => _showDebugOutputTexture;
    public float DebugDisplayHeightPercent => _debugDisplayHeightPercent;
    public RenderPassEvent RenderPassEvent => _renderPassEvent;

    public bool IsValidForPass() {
        var mgr = RayTracingAccelerationStructureManager.Instance;
        return mgr != null && mgr.IsValid && _camera != null;
    }

    void OnEnable() {
        _camera = GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return;
        if (!SystemInfo.supportsRayTracing) {
            Debug.LogWarning("RayTracingRenderer: このデバイスはレイトレーシングをサポートしていません。");
            return;
        }
        RayTracingAccelerationStructureManager.EnsureExists();
    }

    void OnGUI() {
        if (!_showDebugOutputTexture || _camera == null) return;
        if (Event.current?.type != EventType.Repaint) return;
        int w = _camera.pixelWidth;
        int h = _camera.pixelHeight;
        if (w <= 0 || h <= 0) return;
        float dispH = Screen.height * _debugDisplayHeightPercent;
        float dispW = dispH * (w / (float)h);
        if (dispW <= 0 || dispH <= 0) return;
        var r = new Rect(0, Screen.height - dispH, dispW, dispH);
        GUI.color = Color.white;
        GUI.Label(r, "[RT Debug: カメラ直接出力のためテクスチャ表示は未対応]");
    }
}
