/// <summary>
/// シーンで唯一の RTAS を管理するシングルトン。複数カメラで RayTracingRenderer を使っても RTAS は 1 つだけ生成する。
/// </summary>
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RayTracingAccelerationStructureManager : MonoBehaviour {
    static RayTracingAccelerationStructureManager _instance;
    public static RayTracingAccelerationStructureManager Instance => _instance;

    RayTracingAccelerationStructure _rtas;
    List<(Renderer renderer, Mesh mesh, int handle)> _instanceEntries = new List<(Renderer, Mesh, int)>();
    uint _nextInstanceID;

    public RayTracingAccelerationStructure Rtas => _rtas;
    public int InstanceCount => _instanceEntries?.Count ?? 0;
    public bool IsValid => _rtas != null;

    static bool TryGetMesh(Renderer r, out Mesh mesh) {
        mesh = null;
        if (r is MeshRenderer mr) {
            var f = mr.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null) { mesh = f.sharedMesh; return true; }
        }
        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) { mesh = smr.sharedMesh; return true; }
        return false;
    }

    static IEnumerable<Renderer> EnumerateMeshRenderers() {
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) yield return r;
        foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) yield return r;
    }

    public static void EnsureExists() {
        if (_instance != null) return;
        if (!SystemInfo.supportsRayTracing) return;
        var go = new GameObject("[RayTracingAccelerationStructureManager]");
        _instance = go.AddComponent<RayTracingAccelerationStructureManager>();
        DontDestroyOnLoad(go);
    }

    void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable() {
        if (!SystemInfo.supportsRayTracing) return;
        var rtasSettings = new RayTracingAccelerationStructure.Settings(RayTracingAccelerationStructure.ManagementMode.Manual, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1);
        _rtas = new RayTracingAccelerationStructure(rtasSettings);
        _instanceEntries.Clear();
        _nextInstanceID = 0;
        BuildInstanceListOnce();
    }

    void OnDestroy() {
        if (_instance == this) _instance = null;
        _rtas?.Dispose();
    }

    void BuildInstanceListOnce() {
        foreach (var renderer in EnumerateMeshRenderers()) {
            if (!TryGetMesh(renderer, out var mesh)) continue;
            int handle = renderer.gameObject.activeInHierarchy ? AddInstanceToRtas(renderer, mesh) : -1;
            if (handle < 0) continue;
            _instanceEntries.Add((renderer, mesh, handle));
        }
    }

    void DiscoverNewRenderers() {
        var currentSet = new HashSet<Renderer>();
        foreach (var (r, _, _) in _instanceEntries) currentSet.Add(r);
        foreach (var renderer in EnumerateMeshRenderers()) {
            if (currentSet.Contains(renderer) || !TryGetMesh(renderer, out var mesh)) continue;
            currentSet.Add(renderer);
            int handle = renderer.gameObject.activeInHierarchy ? AddInstanceToRtas(renderer, mesh) : -1;
            if (handle < 0) continue;
            _instanceEntries.Add((renderer, mesh, handle));
        }
    }

    int AddInstanceToRtas(Renderer renderer, Mesh mesh) {
        if (renderer.sharedMaterial == null) {
            Debug.LogWarning($"RayTracingAccelerationStructureManager: {renderer.gameObject.name} に Material がありません。スキップします。");
            return -1;
        }
        // 位置が更新されないバグ: UpdateInstanceTransform を毎フレーム反映させるには Renderer.rayTracingMode を
        // RayTracingMode.DynamicTransform に設定する必要がある。未設定のままだと Static 扱いとなり、
        // UpdateInstanceTransform が RTAS に反映されず、GO を一度 Disabled にしないと位置が更新されない挙動になる。
        // 修正する場合は AddInstance の直前に renderer.rayTracingMode = RayTracingMode.DynamicTransform; を追加する。
        int n = mesh.subMeshCount;
        var subMeshFlags = new RayTracingSubMeshFlags[n];
        var flag = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
        for (int i = 0; i < n; i++) subMeshFlags[i] = flag;
        uint mask = (uint)(1 << (renderer.gameObject.layer % 8));
        return _rtas.AddInstance(renderer, subMeshFlags, true, false, mask, (uint)_nextInstanceID++);
    }

    void Update() {
        if (_rtas == null) return;
        DiscoverNewRenderers();
    }

    void LateUpdate() {
        if (_rtas == null) return;

        for (int i = _instanceEntries.Count - 1; i >= 0; i--) {
            var (renderer, mesh, handle) = _instanceEntries[i];
            if (renderer == null) {
                if (handle >= 0) _rtas.RemoveInstance(handle);
                _instanceEntries.RemoveAt(i);
                continue;
            }
            bool active = renderer.gameObject.activeInHierarchy;
            if (active) {
                if (handle < 0) {
                    int newHandle = AddInstanceToRtas(renderer, mesh);
                    if (newHandle >= 0) {
                        handle = newHandle;
                        _instanceEntries[i] = (renderer, mesh, handle);
                    }
                }
                // Renderer で追加したインスタンスは UpdateInstanceTransform(Renderer) を使う。Transform を Renderer から取得するため
                // Editor の RayTracingMode (Dynamic Transform) が正しく使われる。handle+matrix は AABB/Mesh 用。
                if (handle >= 0) _rtas.UpdateInstanceTransform(renderer);
            } else {
                if (handle >= 0) {
                    _rtas.RemoveInstance(handle);
                    _instanceEntries[i] = (renderer, mesh, -1);
                }
            }
        }

        _rtas.Build();
        if (_instanceEntries.Count == 0) {
            Debug.LogWarning("RayTracingAccelerationStructureManager: RTAS にインスタンスがありません。");
        }
    }
}
