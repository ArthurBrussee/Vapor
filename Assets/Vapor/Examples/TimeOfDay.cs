using UnityEngine;

public class TimeOfDay : MonoBehaviour {
	public Vapor Vapor;

	public GameObject Sun;
	public GameObject Torches;
	public GameObject ReflectionProbes;

	public float DayTime;

	ReflectionProbe[] m_probes;

	void OnEnable() {
		m_probes = ReflectionProbes.GetComponentsInChildren<ReflectionProbe>();
		DayTime = 0.0f;
	}

	void OnGUI() {
		DayTime = GUI.HorizontalSlider(new Rect(0.0f, 0.0f, Screen.width, 20.0f), DayTime, 0.0f, 2.0f);
	}

	void Update () {
		DayTime += Time.deltaTime * 0.025f;
		const float c_minAmb = 0.15f;

		float realTime = Mathf.Repeat(DayTime, 1.0f);
		float ambient;

		if (realTime > 0.8f) {
			Torches.SetActive(true);
			Sun.transform.localRotation = Quaternion.Euler(0.0f, Mathf.Lerp(40.0f, 360.0f, Mathf.Abs(realTime - 0.8f) / 0.2f), 0.0f);
			ambient = c_minAmb;
		} else {
			Torches.SetActive(false);
			ambient = Mathf.Clamp01(1.0f - realTime/0.8f) * (1.0f - c_minAmb ) + c_minAmb;
            Sun.transform.localRotation = Quaternion.Euler(0.0f, Mathf.Repeat(DayTime, 1.0f) / 0.8f * 30.0f, 0.0f);
		}


		Vapor.Setting.AmbientLight.a = ambient;
		foreach (var probe in m_probes) {
			probe.intensity = ambient;
		}
	}
}
