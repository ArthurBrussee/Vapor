using UnityEngine;

public class TimeOfDay : MonoBehaviour {
	public Vapor Vapor;

	public GameObject Sun;
	public GameObject Torches;
	public GameObject ReflectionProbes;

	public float CurrentTime;

	ReflectionProbe[] m_probes;

	void OnEnable() {
		m_probes = ReflectionProbes.GetComponentsInChildren<ReflectionProbe>();
		CurrentTime = 0.0f;
	}

	void OnGUI() {
		CurrentTime = GUI.HorizontalSlider(new Rect(0.0f, 0.0f, Screen.width, 20.0f), CurrentTime, 0.0f, 2.0f);
	}

	void Update () {
		CurrentTime += Time.deltaTime * 0.025f;
		const float minAmb = 0.15f;

		float wrappedTime = Mathf.Repeat(CurrentTime, 1.0f);
		float ambient;

		const float daytimeFactor = 0.8f;

		var rotationRange = 55.0f;

		if (wrappedTime > daytimeFactor) {
			Torches.SetActive(true);
			Sun.transform.localRotation = Quaternion.Euler(Mathf.Lerp(rotationRange, 360.0f, Mathf.Abs(wrappedTime - 0.8f) / (1 - daytimeFactor)), 0.0f, 0.0f);
			ambient = minAmb;
		} else {
			Torches.SetActive(false);
			ambient = Mathf.Clamp01(1.0f - wrappedTime / daytimeFactor) * (1.0f - minAmb) + minAmb;
            Sun.transform.localRotation = Quaternion.Euler(wrappedTime / 0.8f * rotationRange, 0.0f, 0.0f);
		}

		Vapor.Setting.AmbientLight.a = ambient;
		foreach (var probe in m_probes) {
			probe.intensity = ambient;
		}
	}
}
