using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Places prefab instances in a grid on the XZ plane. One MaterialPropertyBlock per instance
/// is created and applied to all Renderers in that instance, so material parameters can be
/// changed per instance later via GetPropertyBlock(index) and ApplyInstanceBlock(index).
/// Height per instance is varied by 3D simplex noise (XZ + time); hue uses the same noise value (hVal).
/// </summary>
public class GridInstancePlacer : MonoBehaviour {
	[SerializeField] GameObject prefab;
	[SerializeField] Vector2Int gridCount = new Vector2Int(5, 5);
	[SerializeField] Vector2 spacing = new Vector2(2f, 2f);
	[SerializeField] Transform parent;

	[Header("Noise (XZ + Time)")]
	[SerializeField] float noiseScaleHeight = 0.2f;
	[SerializeField] float noiseTimeScale = 0.5f;
	[SerializeField] float heightScale = 1f;
	[SerializeField] float heightScaleOffset;
	[SerializeField, Range(0f, 0.5f)] float hueVariation = 0.15f;

	static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

	readonly List<GameObject> _instances = new List<GameObject>();
	readonly List<MaterialPropertyBlock> _mpbs = new List<MaterialPropertyBlock>();
	readonly List<Renderer[]> _instanceRenderers = new List<Renderer[]>();
	bool _pendingRebuild;

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
		// Defer rebuild to next frame; CreateGrid() uses Instantiate which can trigger
		// SendMessage (e.g. OnTransformChildrenChanged) and SendMessage is not allowed during OnValidate.
		if (Application.isPlaying && isActiveAndEnabled && _instances.Count > 0)
			_pendingRebuild = true;
	}

	void Update() {
		if (_pendingRebuild) {
			_pendingRebuild = false;
			CreateGrid();
		} else if (_instances.Count > 0) {
			UpdateInstanceNoise();
		}
	}

	/// <summary>Instantiate or reuse prefabs in a grid and assign one MPB per instance. Rebuilds reuse existing instances when count matches.</summary>
	[ContextMenu("Create Grid")]
	public void CreateGrid() {
		if (prefab == null || !Application.isPlaying) return;
		Transform root = parent != null ? parent : transform;
		int needed = gridCount.x * gridCount.y;
		while (_instances.Count > needed) {
			int last = _instances.Count - 1;
			if (_instances[last] != null) Destroy(_instances[last]);
			_instances.RemoveAt(last);
			_mpbs.RemoveAt(last);
			_instanceRenderers.RemoveAt(last);
		}
		float offsetX = (gridCount.x - 1) * spacing.x * -0.5f;
		float offsetZ = (gridCount.y - 1) * spacing.y * -0.5f;
		float prefabScaleY = prefab.transform.localScale.y;
		Color baseColor = GetPrefabBaseColor();
		float t = Time.time * noiseTimeScale;
		int index = 0;
		for (int z = 0; z < gridCount.y; z++) {
			for (int x = 0; x < gridCount.x; x++) {
				float px = offsetX + x * spacing.x;
				float pz = offsetZ + z * spacing.y;
				float3 nHeight = new float3(px * noiseScaleHeight, pz * noiseScaleHeight, t);
				float hVal = noise.snoise(nHeight);
				Vector3 pos = new Vector3(px, 0f, pz);
				Vector3 scale = prefab.transform.localScale;
				scale.y = (prefabScaleY * heightScale + heightScaleOffset) * (0.5f + 0.5f * hVal);
				Color.RGBToHSV(baseColor, out float h, out float s, out float v);
				h = Mathf.Repeat(h + hueVariation * hVal, 1f);
				Color tint = Color.HSVToRGB(h, s, v);
				tint.a = baseColor.a;
				if (index < _instances.Count) {
					GameObject go = _instances[index];
					go.transform.position = pos;
					go.transform.localScale = scale;
					go.SetActive(true);
					go.name = $"{prefab.name}_{index}";
					_mpbs[index].SetColor(BaseColorId, tint);
					foreach (var r in _instanceRenderers[index]) r.SetPropertyBlock(_mpbs[index]);
				} else {
					GameObject go = Instantiate(prefab, pos, Quaternion.identity, root);
					go.transform.localScale = scale;
					go.hideFlags = HideFlags.DontSave;
					go.name = $"{prefab.name}_{index}";
					go.SetActive(true);
					Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
					var block = new MaterialPropertyBlock();
					block.SetColor(BaseColorId, tint);
					foreach (var r in renderers) r.SetPropertyBlock(block);
					_instances.Add(go);
					_mpbs.Add(block);
					_instanceRenderers.Add(renderers);
				}
				index++;
			}
		}
	}

	void UpdateInstanceNoise() {
		if (prefab == null || _instances.Count == 0) return;
		float offsetX = (gridCount.x - 1) * spacing.x * -0.5f;
		float offsetZ = (gridCount.y - 1) * spacing.y * -0.5f;
		float prefabScaleY = prefab.transform.localScale.y;
		Color baseColor = GetPrefabBaseColor();
		float t = Time.time * noiseTimeScale;
		for (int i = 0; i < _instances.Count; i++) {
			int x = i % gridCount.x;
			int z = i / gridCount.x;
			float px = offsetX + x * spacing.x;
			float pz = offsetZ + z * spacing.y;
			float3 nHeight = new float3(px * noiseScaleHeight, pz * noiseScaleHeight, t);
			float hVal = noise.snoise(nHeight);
			Vector3 scale = _instances[i].transform.localScale;
			scale.y = (prefabScaleY * heightScale + heightScaleOffset) * (0.5f + 0.5f * hVal);
			_instances[i].transform.localScale = scale;
			Color.RGBToHSV(baseColor, out float h, out float s, out float v);
			h = Mathf.Repeat(h + hueVariation * hVal, 1f);
			Color tint = Color.HSVToRGB(h, s, v);
			tint.a = baseColor.a;
			_mpbs[i].SetColor(BaseColorId, tint);
			foreach (var r in _instanceRenderers[i]) r.SetPropertyBlock(_mpbs[i]);
		}
	}

	Color GetPrefabBaseColor() {
		var r = prefab.GetComponentInChildren<Renderer>(true);
		if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColorId))
			return Color.white;
		return r.sharedMaterial.GetColor(BaseColorId);
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
