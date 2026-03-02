using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridInstancePlacer))]
public class GridInstancePlacerEditor : Editor {
	public override void OnInspectorGUI() {
		DrawDefaultInspector();
		var placer = (GridInstancePlacer)target;
		EditorGUILayout.Space(4);
		using (new EditorGUILayout.HorizontalScope()) {
			if (GUILayout.Button("Create Grid")) placer.CreateGrid();
			if (GUILayout.Button("Clear Grid")) placer.Clear();
		}
		if (placer.InstanceCount > 0)
			EditorGUILayout.HelpBox($"Instances: {placer.InstanceCount}", MessageType.None);
	}
}
