using UnityEngine;
using System.Collections;


[CreateAssetMenu(fileName = "VaporSettings", menuName = "Vapor Setting", order = 300)]
public class VaporSetting : ScriptableObject {
	[Header("Global settings")]
	public Color Albedo = new Color(0.1f, 0.1f, 0.1f); //sig_s / sig_t
	public float Extinction = 0.15f; //sig_t



	public Color Emissive = Color.black;

	[ColorUsage(true, true, 0.0f, 8.0f, 0.125f, 3.0f)]
	public Color AmbientLight = Color.black;
}
