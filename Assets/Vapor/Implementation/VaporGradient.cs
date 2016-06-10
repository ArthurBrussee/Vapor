using System;
using UnityEngine;

[Serializable]
public class VaporGradient {
	public float Start;
	public float End = 100;

	public Gradient Gradient;

	public Texture2D BakeToTexture() {
		const int res = 128;

		var tex = new Texture2D(res, 1, TextureFormat.ARGB32, false);

		Color[] texColors = new Color[res];

		for (int i = 0; i < texColors.Length; i++) {
			float t = (float)i / (texColors.Length - 1);
			texColors[i] = Gradient.Evaluate(t);
		}
		tex.SetPixels(texColors);
		tex.Apply(false, true);

		return tex;
	}
}
