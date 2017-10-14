﻿using UnityEngine;

[CreateAssetMenu(fileName = "VaporSettings", menuName = "Vapor Setting", order = 300)]
public class VaporSetting : ScriptableObject {
	[Header("Global settings")] public Color Albedo = new Color(0.1f, 0.1f, 0.1f); //sig_s / sig_t
	public float Extinction = 0.15f; //sig_t

	[ColorUsage(true, true, 0.0f, 32.0f, 0.125f, 5.0f)] public Color Emissive = Color.black;
	[ColorUsage(true, true, 0.0f, 32.0f, 0.125f, 5.0f)] public Color AmbientLight = Color.black;

	const int c_gradientRes = 128;

	public VaporGradient HeightGradient = new VaporGradient();
	public VaporGradient DistanceGradient = new VaporGradient();

	Texture2D m_gradientTex;

	public Texture2D GradientTex {
		get {

			if (m_gradientTex == null) {
				UpdateGradients();
			}
			return m_gradientTex;
		}
	}

	public void UpdateGradients() {
		if (m_gradientTex == null) {
			m_gradientTex = new Texture2D(c_gradientRes, c_gradientRes, TextureFormat.ARGB32, false) {
				wrapMode = TextureWrapMode.Clamp
			};

			m_gradientTex.hideFlags = HideFlags.HideAndDontSave;
		}

		Color[] texColors = new Color[c_gradientRes * c_gradientRes];

		for (int i = 0; i < c_gradientRes; i++) {
			for (int j = 0; j < c_gradientRes; j++) {
				float ti = (float)i / (c_gradientRes - 1);
				float tj = (float)j / (c_gradientRes - 1);


				Color colx = DistanceGradient.Gradient.Evaluate(ti);
				Color coly = HeightGradient.Gradient.Evaluate(tj);

				texColors[i + j * c_gradientRes] = colx * coly;
			}
		}

		m_gradientTex.SetPixels(texColors);
		m_gradientTex.Apply();
	}


	public void Bind(ComputeShader comp, int kernel, VaporSetting blendTo, float blendTime) {
		var albedo = Color.Lerp(Albedo, blendTo.Albedo, blendTime);
		float extinction = Mathf.Lerp(Extinction, blendTo.Extinction, blendTime);
		var emissive = Color.Lerp(Emissive, blendTo.Emissive, blendTime);

		comp.SetVector("_AlbedoExt", new Vector4(albedo.r, albedo.g, albedo.b, extinction));
		comp.SetFloat("_Extinction", extinction);

		emissive = emissive * emissive.a;
		emissive.r /= (albedo.r + 1);
		emissive.g /= (albedo.g + 1);
		emissive.b /= (albedo.b + 1);

		var ambientLight = Color.Lerp(AmbientLight, blendTo.AmbientLight, blendTime);
		var ambientEmissive = ambientLight * ambientLight.a;
		var emittedLight = (emissive + ambientEmissive) * 5.0f;

		comp.SetVector("_Emissive", new Vector4(emittedLight.r, emittedLight.g, emittedLight.b));
		comp.SetFloat("_Time", Time.time * 10.0f);

		float hstart = Mathf.Lerp(HeightGradient.Start, blendTo.HeightGradient.Start, blendTime);
		float hend = Mathf.Lerp(HeightGradient.End, blendTo.HeightGradient.End, blendTime);

		float dstart = Mathf.Lerp(DistanceGradient.Start, blendTo.DistanceGradient.Start, blendTime);
		float dend = Mathf.Lerp(DistanceGradient.End, blendTo.DistanceGradient.End, blendTime);

		float heightSize = Mathf.Max(0, hend - hstart);
		float distSize = Mathf.Max(0, dend - dstart);

		comp.SetVector("_GradientSettings",
			new Vector4(1.0f / heightSize, -hstart / heightSize,
				1.0f / distSize, -dstart / distSize));

		comp.SetTexture(kernel, "_GradientTexture", GradientTex);

		if (blendTime > 0) {
			comp.SetTexture(kernel, "_GradientTextureBlend", blendTo.GradientTex);
			comp.SetFloat("_SettingBlend", blendTime);
		}
	}
}