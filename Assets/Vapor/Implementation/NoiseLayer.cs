using System;
using LibNoise;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

[Serializable]
public class NoiseLayer
{
	[Header("Noise settings")]
	public float Frequency = 5.0f;

	public float Persistence = 0.97f;
	public float Lacunarity = 1.3f;
	public int PerlinOctaves = 1;
	public int Seed = -1;

	[Header("Tile settings")]
	public Vector3 ScrollSpeed = Vector3.one;

	public Vector3 Scale = Vector3.one;
	public float Strength = 1.0f;

	const int c_noiseSize = 32;
	private const float c_scale = 100.0f;

	public Vector3 SetScale {
		get { return Scale * c_scale; }
	}

	public Vector3 SetInvScale {
		get { return new Vector3(1.0f / (c_scale * Scale.x), 1.0f / (c_scale * Scale.y), 1.0f / (c_scale * Scale.z)); }
	}

	public Vector3 SetScaledScrollSpeed {
		get { return Vector3.Scale(ScrollSpeed, SetInvScale); }
	}

	private Texture3D m_textureLayer;

	public Texture NoiseTexture {
		get { return m_textureLayer; }
	}

	public bool NeedsBuild() {
		return m_textureLayer == null;
	}

	public void Bake() {
		Color[] pixelsBuf = new Color[c_noiseSize * c_noiseSize * c_noiseSize];

		m_textureLayer = new Texture3D(c_noiseSize, c_noiseSize, c_noiseSize, TextureFormat.Alpha8, false);
		m_textureLayer.wrapMode = TextureWrapMode.Repeat;
		m_textureLayer.filterMode = FilterMode.Bilinear;
		
		int colIndex = 0;
		
		int seed = Seed;
		if (seed == -1) {
			seed = Random.Range(0, int.MaxValue);
		}

		Profiler.BeginSample("Generate noise");

		for (float zi = 0; zi < c_noiseSize; ++zi) {
			for (float yi = 0; yi < c_noiseSize; ++yi) {
				for (float xi = 0; xi < c_noiseSize; ++xi) {

					float x = xi / c_noiseSize;
					float y = yi / c_noiseSize;
					float z = zi / c_noiseSize;

					float noiseVal = 0.0f;
					float str = 1f;
					x *= Frequency;
					y *= Frequency;
					z *= Frequency;

					for (int index = 0; index < PerlinOctaves; ++index) {
						float noiseAt = (2.0f *
							Mathf.Abs(Utils.GradientCoherentNoise3D(Utils.MakeInt32Range(x), Utils.MakeInt32Range(y),
								Utils.MakeInt32Range(z), (seed + index) & uint.MaxValue)) - 1.0f);
						noiseVal += noiseAt * str;
						x *= Lacunarity;
						y *= Lacunarity;
						z *= Lacunarity;
						str *= Persistence;
					}

					pixelsBuf[colIndex].a = Mathf.Abs(noiseVal);

					++colIndex;
				}
			}
		}

        Profiler.EndSample();


        m_textureLayer.SetPixels(pixelsBuf);
		m_textureLayer.Apply();
	}


	public void Destroy() {
		Object.DestroyImmediate(m_textureLayer);
	}
    

	private static string[] s_texNames = {"_NoiseTex0", "_NoiseTex1", "_NoiseTex2"};
	private static string[] s_scaleNames = { "_NoiseScale0", "_NoiseScale1", "_NoiseScale2" };
	private static string[] s_scrollNames = { "_NoiseScroll0", "_NoiseScroll1", "_NoiseScroll2" };


	public void Bind(int kernel, ComputeShader compute, int i) {
		compute.SetTexture(kernel, s_texNames[i], m_textureLayer);
	    compute.SetVector(s_scaleNames[i], SetInvScale);
		compute.SetVector(s_scrollNames[i], SetScaledScrollSpeed * Time.time);
	}
}