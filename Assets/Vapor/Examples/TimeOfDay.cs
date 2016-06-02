using UnityEngine;
using System.Collections;

public class TimeOfDay : MonoBehaviour {
	public Vapor.Vapor Vapor;

	public GameObject Sun;
	public GameObject Torches;

	public GameObject ReflectionProbes;

	public float DayTime;

	private ReflectionProbe[] m_probes;


	void OnEnable() {
		m_probes = ReflectionProbes.GetComponentsInChildren<ReflectionProbe>();
		DayTime = 0.0f;
	}

	void OnGUI() {
		DayTime = GUI.HorizontalSlider(new Rect(0.0f, 0.0f, Screen.width, 20.0f), DayTime, 0.0f, 2.0f);
		//DayTime += Time.deltaTime * 0.01f;
	}

	// Update is called once per frame
	void Update () {
		float realTime = Mathf.PingPong(DayTime, 1.0f);

		Sun.transform.localRotation = Quaternion.Euler(DayTime * 180.0f, 0.0f, 0.0f);


		float ambient;

		if (realTime > 0.6f) {
			Torches.SetActive(true);
			ambient = 0.2f;
		} else {
			Torches.SetActive(false);
			ambient = Mathf.Clamp01(1.0f - realTime * 1.5f);

		}


		Vapor.Setting.AmbientLight.a = ambient;
		foreach (var probe in m_probes) {
			probe.intensity = ambient;
		}
	}
}
