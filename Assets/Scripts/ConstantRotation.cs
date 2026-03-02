using UnityEngine;

/// <summary>
/// Rotates the transform at a constant speed around a configurable axis (degrees per second).
/// </summary>
public class ConstantRotation : MonoBehaviour {
	[SerializeField] Vector3 axis = Vector3.up;
	[SerializeField] float degreesPerSecond = 90f;

	void Update() {
		transform.Rotate(axis.normalized, degreesPerSecond * Time.deltaTime, Space.Self);
	}
}
