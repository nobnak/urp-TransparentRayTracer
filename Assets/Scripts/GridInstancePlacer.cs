using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places prefab instances in a grid on the XZ plane. One MaterialPropertyBlock per instance
/// is created and applied to all Renderers in that instance, so material parameters can be
/// changed per instance later via GetPropertyBlock(index) and ApplyInstanceBlock(index).
/// </summary>
public class GridInstancePlacer : MonoBehaviour {
	[SerializeField] GameObject prefab;
	[SerializeField] Vector2Int gridCount = new Vector2Int(5, 5);
	[SerializeField] Vector2 spacing = new Vector2(2f, 2f);
	[SerializeField] Transform parent;

	readonly List<GameObject> _instances = new List<GameObject>();
	readonly List<MaterialPropertyBlock> _mpbs = new List<MaterialPropertyBlock>();
	readonly List<Renderer[]> _instanceRenderers = new List<Renderer[]>();

	public int InstanceCount => _instances.Count;
	public GameObject Prefab => prefab;
	public Vector2Int GridCount => gridCount;
	public Vector2 Spacing => spacing;

	void OnEnable() {
		if (prefab != null && prefab.scene.IsValid())
			prefab.SetActive(false);
		if (Application.isPlaying) CreateGrid();
	}

	void OnDisable() {
		Clear();
	}

	void OnValidate() {
		if (Application.isPlaying && isActiveAndEnabled && _instances.Count > 0) {
			Clear();
			CreateGrid();
		}
	}

	/// <summary>Instantiate prefabs in a grid and assign one MPB per instance to all its Renderers. Called from OnEnable and OnValidate at runtime.</summary>
	[ContextMenu("Create Grid")]
	public void CreateGrid() {
		Clear();
		if (prefab == null || !Application.isPlaying) return;
		Transform root = parent != null ? parent : transform;
		float offsetX = (gridCount.x - 1) * spacing.x * -0.5f;
		float offsetZ = (gridCount.y - 1) * spacing.y * -0.5f;
		for (int z = 0; z < gridCount.y; z++) {
			for (int x = 0; x < gridCount.x; x++) {
				Vector3 pos = new Vector3(offsetX + x * spacing.x, 0f, offsetZ + z * spacing.y);
				GameObject go = Instantiate(prefab, pos, Quaternion.identity, root);
				go.hideFlags = HideFlags.DontSave;
				go.name = $"{prefab.name}_{_instances.Count}";
				go.SetActive(true);
				Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
				var block = new MaterialPropertyBlock();
				foreach (var r in renderers) r.SetPropertyBlock(block);
				_instances.Add(go);
				_mpbs.Add(block);
				_instanceRenderers.Add(renderers);
			}
		}
	}

	/// <summary>Remove all placed instances and clear MPB references.</summary>
	[ContextMenu("Clear Grid")]
	public void Clear() {
		foreach (var go in _instances) {
			if (go != null) {
				if (Application.isPlaying) Destroy(go);
				else DestroyImmediate(go);
			}
		}
		_instances.Clear();
		_mpbs.Clear();
		_instanceRenderers.Clear();
	}

	/// <summary>Get the MaterialPropertyBlock for the given instance index. Modify it then call ApplyInstanceBlock(index).</summary>
	public MaterialPropertyBlock GetPropertyBlock(int index) {
		if (index < 0 || index >= _mpbs.Count) return null;
		return _mpbs[index];
	}

	/// <summary>Re-apply the instance's MaterialPropertyBlock to all Renderers of that instance. Call after changing the block.</summary>
	public void ApplyInstanceBlock(int index) {
		if (index < 0 || index >= _instanceRenderers.Count) return;
		MaterialPropertyBlock block = _mpbs[index];
		foreach (var r in _instanceRenderers[index]) r.SetPropertyBlock(block);
	}

	/// <summary>Get the root GameObject of the instance at index.</summary>
	public GameObject GetInstance(int index) {
		if (index < 0 || index >= _instances.Count) return null;
		return _instances[index];
	}

	void OnDestroy() {
		Clear();
	}
}
