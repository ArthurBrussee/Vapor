using System;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif


[ExecuteInEditMode]
public class Vapor2 : MonoBehaviour {
	public enum ShadowResolution {
		Low,
		Normal,
		High,
		VeryHigh
	}

#if UNITY_EDITOR
	[NonSerialized] public bool NeedsRebake;
#endif
	//TODO: Change for scattering
	public float FogDensity = 0.1f;


	[Range(0.0f, 1.0f)] public float Anisotropy;

	public float InscatterIntensity = 0.1f;
	public Color AmbientLight = Color.white;
	public float AmbientIntensity = 0.1f;

	public Light Sun;
	public ShadowResolution ShadowResolutionSetting;

	[SerializeField] private NoiseLayer m_baseLayer = new NoiseLayer();
	[SerializeField] private NoiseLayer m_secondaryLayer = new NoiseLayer();
	[SerializeField] private NoiseLayer m_detailLayer = new NoiseLayer();
	[SerializeField] private ComputeShader m_scatteringShader;

	private RenderTexture m_densityTex;
	private RenderTexture m_scatterTex;

	private int m_scatterKernel;
	private int m_densityKernel;
	private int m_matrixKernel;

	private const int c_volumeDepth = 128;
	private const int c_zIterations = 256;

	private const int c_horizontalFogRes = 256;
	private const int c_verticalFogRes = 128;

	private Camera m_camera;
	private Material m_fogMat;

	//Shadowing data
	private CommandBuffer m_cmd;
	private CommandBuffer m_cmdScreen;


	private Material m_screenShadowMaterial;

	private RenderTexture m_shadowMatrixTexture;
	private RenderTexture m_shadowMap;

	private Texture2D m_matrixTextureRead;
	private static string[] s_matrixNames = { "unity_World2Shadow0", "unity_World2Shadow1", "unity_World2Shadow2", "unity_World2Shadow3" };


	private void OnEnable() {
#if UNITY_EDITOR
		if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) {
			return;
		}
#endif
		CreateResources();
	}

	private void CreateResources() {
		m_scatteringShader = Resources.Load<ComputeShader>("VaporSim");
		m_scatterKernel = m_scatteringShader.FindKernel("Scatter");
		m_densityKernel = m_scatteringShader.FindKernel("CalculateDensity");
		m_matrixKernel = m_scatteringShader.FindKernel("SetMatrices");
		m_screenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));

		m_shadowMatrixTexture = new RenderTexture(4, 5, 0, RenderTextureFormat.ARGBFloat);

		//TODO: Split resolutions?
		CreateTexture(ref m_scatterTex);
		CreateTexture(ref m_densityTex);

		NeedsRebake = false;

		if (m_baseLayer.NeedsBuild() || m_secondaryLayer.NeedsBuild() || m_detailLayer.NeedsBuild()) {
			BakeNoiseLayers();
		}

		m_camera = GetComponent<Camera>();
	}

	//TODO: Fix this up for ESM maps
	private int GetShadowMapResolution() {
		int pixelSize = Mathf.Max(Camera.current.pixelWidth, Camera.current.pixelHeight);

		switch (ShadowResolutionSetting) {
			case ShadowResolution.VeryHigh:
				pixelSize *= 2;
				break;

			case ShadowResolution.High:
				//
				break;

			case ShadowResolution.Normal:
				pixelSize /= 2;
				break;

			case ShadowResolution.Low:
				pixelSize /= 4;
				break;
		}

		return 512;

		return Mathf.NextPowerOfTwo(Mathf.RoundToInt(pixelSize * 1.9f));
	}

	public void BakeNoiseLayers() {
		m_baseLayer.Bake();
		m_secondaryLayer.Bake();
		m_detailLayer.Bake();
	}

	private void CreateTexture(ref RenderTexture tex) {
		if (tex == null) {
			if (tex != null) {
				DestroyImmediate(tex);
			}

			tex = new RenderTexture(c_horizontalFogRes, c_verticalFogRes, 0, RenderTextureFormat.ARGBHalf);
			tex.volumeDepth = c_volumeDepth;
			tex.isVolume = true;
			tex.enableRandomWrite = true;
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;

			tex.Create();

			RenderTexture.active = null;
		}
	}


	private void BindCompute() {

        m_baseLayer.Bind(m_densityKernel, m_scatteringShader, 0);
        m_secondaryLayer.Bind(m_densityKernel, m_scatteringShader, 1);
        m_detailLayer.Bind(m_densityKernel, m_scatteringShader, 2);

        m_scatteringShader.SetVector("_NoiseStrength",
            new Vector4(m_baseLayer.Strength, m_secondaryLayer.Strength, m_detailLayer.Strength));

        m_scatteringShader.SetTexture(m_densityKernel, "_DensityTextureWrite", m_densityTex);
        m_scatteringShader.SetTexture(m_scatterKernel, "_DensityTexture", m_densityTex);
        m_scatteringShader.SetTexture(m_scatterKernel, "_ScatterTexture", m_scatterTex);

        float near = m_camera.nearClipPlane;
        float far = m_camera.farClipPlane;

        Vector4 planeSettings = new Vector4(near, far, (far + near) / (2 * (far - near)), (-far * near) / (far - near));

        m_scatteringShader.SetVector("_PlaneSettings", planeSettings);

		m_scatteringShader.SetFloat("_Anisotropy", Anisotropy);

		const float c_denseMult = 0.0005f;
		m_scatteringShader.SetFloat("_InscatterIntensity", InscatterIntensity / c_denseMult * 0.25f);
        m_scatteringShader.SetFloat("_FogDensity", FogDensity * c_denseMult);

		Color ambientSet = AmbientLight;
		ambientSet.a = AmbientIntensity * 0.01f;
        m_scatteringShader.SetVector("_Ambient", ambientSet		);
        m_scatteringShader.SetVector("_TextureResolution",
            new Vector4(m_densityTex.width, m_densityTex.height, m_densityTex.volumeDepth));
        
        m_scatteringShader.SetVector("_CameraPos", Camera.current.transform.position);

		if (Sun != null) {
			m_scatteringShader.SetVector("_LightDirection", Sun.transform.forward);
			m_scatteringShader.SetVector("_LightColor", Sun.color * Sun.intensity);
			
			if (Sun.shadows != LightShadows.None) {
				m_scatteringShader.SetTexture(m_densityKernel, "_ShadowMapTexture", m_shadowMap);
				m_scatteringShader.SetTexture(m_scatterKernel, "_ShadowMapTexture", m_shadowMap);
			} else {
				m_scatteringShader.SetTexture(m_densityKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
				m_scatteringShader.SetTexture(m_matrixKernel, "_ShadowInfoTexture", Texture2D.whiteTexture);
			}
		}


		if (m_matrixTextureRead == null) {
			m_matrixTextureRead = new Texture2D(4, 5, TextureFormat.RGBAFloat, false);
		}

		Graphics.SetRenderTarget(m_shadowMatrixTexture);
		m_matrixTextureRead.ReadPixels(new Rect(0, 0, m_shadowMatrixTexture.width, m_shadowMatrixTexture.height), 0, 0, false);
		Graphics.SetRenderTarget(null);

		for (int j = 0; j < 4; ++j) {
			Matrix4x4 set = Matrix4x4.zero;

			for (int i = 0; i < 4; ++i) {
				var col = m_matrixTextureRead.GetPixel(i, 0);
				set[i, 0] = col.r;
				set[i, 1] = col.g;
				set[i, 2] = col.b;
				set[i, 3] = col.a;
			}

			m_scatteringShader.SetMatrix(s_matrixNames[j], set);
		}

		Color nearSplit = m_matrixTextureRead.GetPixel(0, 4);
		Color farSplit = m_matrixTextureRead.GetPixel(1, 4);

		m_scatteringShader.SetVector("_LightSplitsNear", nearSplit);
		m_scatteringShader.SetVector("_LightSplitsFar", farSplit);

		m_scatteringShader.SetFloat("_ShadowSoft", ShadowSoft);

		//TODO: Can do this better! Use _VaporItVp
		float vFov = Camera.current.fieldOfView;
        float vFovInRads = vFov * Mathf.Deg2Rad;
        float hFovInRads = 2 * Mathf.Atan(Camera.current.aspect * Mathf.Tan(vFovInRads * 0.5f));

        float xWidth = 2.0f * far * Mathf.Tan(0.5f * hFovInRads);
        float yWidth = 2.0f * far * Mathf.Tan(0.5f * vFovInRads);
		
        m_scatteringShader.SetVector("_Size", new Vector4(xWidth, yWidth));
        m_scatteringShader.SetMatrix("_CameraToWorld", m_camera.transform.localToWorldMatrix);
    }

	public float ShadowSoft;
	private void BindMaterial() {
		m_fogMat.SetFloat("_NearPlane", m_camera.nearClipPlane);
        m_fogMat.SetFloat("_FarPlane", m_camera.farClipPlane);
        m_fogMat.SetTexture("_ScatterTex", m_scatterTex);

		m_fogMat.SetTexture("_ShadowTexture", m_shadowMatrixTexture);
    }

	private void OnPreRender() {
		/*
		Matrix4x4 V = Camera.current.worldToCameraMatrix;
		Matrix4x4 P = Camera.current.projectionMatrix;
		P = GL.GetGPUProjectionMatrix(P, true);

		Matrix4x4 vp = P * V;
		var inv = vp.inverse;
		Shader.SetGlobalMatrix("_VaporItVP", inv);

		P = GL.GetGPUProjectionMatrix(P, false);
		vp = P * V;
		inv = vp.inverse;
		m_scatteringShader.SetMatrix("_VaporItVP", inv);
		*/

		if (Sun != null) {
			if (Sun.shadows != LightShadows.None) {
				if (m_cmd == null || Sun.GetCommandBuffers(LightEvent.AfterShadowMap).Length == 0) {
					Sun.RemoveAllCommandBuffers();

					if (m_shadowMap != null) {
						DestroyImmediate(m_shadowMap);
					}

					int res = GetShadowMapResolution();
					m_shadowMap = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat);

					m_cmd = new CommandBuffer();
					m_cmdScreen= new CommandBuffer();
					//Grab the shadow map!
					m_cmd.Blit(BuiltinRenderTextureType.CurrentActive, m_shadowMap);

					m_cmdScreen.Blit(Texture2D.whiteTexture, m_shadowMatrixTexture, m_screenShadowMaterial);

					//TODO: This fucks with the scene view, and is a frame behind :(
					Sun.AddCommandBuffer(LightEvent.AfterShadowMap, m_cmd);
					Sun.AddCommandBuffer(LightEvent.AfterScreenspaceMask, m_cmdScreen);
				}
			}
		}
	}


	private void OnRenderImage(RenderTexture source, RenderTexture destination) {
        if (m_fogMat == null) {
            m_fogMat = new Material(Shader.Find("Hidden/VaporPost2"));
        }

        BindCompute();
        m_scatteringShader.Dispatch(m_densityKernel, c_horizontalFogRes / 4, c_verticalFogRes / 4, c_volumeDepth / 4);
        m_scatteringShader.Dispatch(m_scatterKernel, c_horizontalFogRes / 8, c_verticalFogRes / 8, 1);

        BindMaterial();

        Graphics.Blit(source, destination, m_fogMat, 0);
	}
	
	private void OnDisable() {
        DestroyImmediate(m_densityTex);
        DestroyImmediate(m_scatterTex);
		DestroyImmediate(m_matrixTextureRead);
		DestroyImmediate(m_shadowMap);
		DestroyImmediate(m_shadowMatrixTexture);

		//Destroy all noises
		m_baseLayer.Destroy();
        m_secondaryLayer.Destroy();
        m_detailLayer.Destroy();
    }
}
