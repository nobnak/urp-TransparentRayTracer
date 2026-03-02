using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Places prefab instances in a grid on the XZ plane. One MaterialPropertyBlock per instance
/// is created and applied to all Renderers in that instance, so material parameters can be
/// changed per instance later via GetPropertyBlock(index) and ApplyInstanceBlock(index).
/// Height and hue per instance are varied by 2D simplex noise (snoise) over horizontal position.
/// </summary>
public class GridInstancePlacer : MonoBehaviour {
	[SerializeField] GameObject prefab;
	[SerializeField] Vector2Int gridCount = new Vector2Int(5, 5);
	[SerializeField] Vector2 spacing = new Vector2(2f, 2f);
	[SerializeField] Transform parent;

	[Header("Noise (XZ)")]
	[SerializeField] float noiseScale = 0.2f;
	[SerializeField] float heightScale = 1f;
	[SerializeField] float heightScaleOffset;
	[SerializeField, Range(0f, 0.5f)] float hueVariation = 0.15f;

	static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
		float prefabScaleY = prefab.transform.localScale.y;
		Color baseColor = GetPrefabBaseColor();
		for (int z = 0; z < gridCount.y; z++) {
			for (int x = 0; x < gridCount.x; x++) {
				float px = offsetX + x * spacing.x;
				float pz = offsetZ + z * spacing.y;
				float2 n = new float2(px * noiseScale, pz * noiseScale);
				float nHeight = noise.snoise(n);
				float nHue = noise.snoise(n + new float2(17.7f, 31.3f));
				Vector3 pos = new Vector3(px, 0f, pz);
				GameObject go = Instantiate(prefab, pos, Quaternion.identity, root);
				Vector3 scale = prefab.transform.localScale;
				scale.y = (prefabScaleY * heightScale + heightScaleOffset) * (0.5f + 0.5f * nHeight);
				go.transform.localScale = scale;
				go.hideFlags = HideFlags.DontSave;
				go.name = $"{prefab.name}_{_instances.Count}";
				go.SetActive(true);
				Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
				var block = new MaterialPropertyBlock();
				Color.RGBToHSV(baseColor, out float h, out float s, out float v);
				h = Mathf.Repeat(h + hueVariation * nHue, 1f);
				Color tint = Color.HSVToRGB(h, s, v);
				tint.a = baseColor.a;
				block.SetColor(BaseColorId, tint);
				foreach (var r in renderers) r.SetPropertyBlock(block);
				_instances.Add(go);
				_mpbs.Add(block);
				_instanceRenderers.Add(renderers);
			}
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
