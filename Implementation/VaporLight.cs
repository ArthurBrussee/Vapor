using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using VaporAPI;

[ExecuteInEditMode]
public class VaporLight : VaporObject {
	static Mesh s_quadMesh;

	static Mesh QuadMesh {
		get {
			if (s_quadMesh == null) {
				var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
				s_quadMesh = go.GetComponent<MeshFilter>().sharedMesh;
				DestroyImmediate(go);
			}

			return s_quadMesh;
		}
	}

	static Material s_shadowFilterMaterial;

	static Material ShadowFilterMaterial {
		get {
			if (s_shadowFilterMaterial == null) {
				s_shadowFilterMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowFilterESM"));
			}

			return s_shadowFilterMaterial;
		}
	}

	static Material s_screenShadowMaterial;

	static Material ScreenShadowMaterial {
		get {
			if (s_screenShadowMaterial == null) {
				s_screenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));
			}

			return s_screenShadowMaterial;
		}
	}

	static Material s_shadowBlurMaterial;

	static Material ShadowBlurMaterial {
		get {
			if (s_shadowBlurMaterial == null) {
				s_shadowBlurMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowBlur"));
			}

			return s_shadowBlurMaterial;
		}
	}

	public float FogScatterIntensity = 1.0f;
	RenderTexture m_shadowMap;
	public float ShadowBlur = 2.0f;

	[Range(0.05f, 0.4f)] public float SpotBaseSize = 0.3f;

	CommandBuffer m_shadowCmd;

	//HACK: This buffer is used to extract the shadow matrix from the GPU
	public ComputeBuffer MatrixBuffer;

	//...by having this command buffer run in an after light event 
	CommandBuffer m_matrixCmdBuffer;
	Light m_light;

	public Light Light {
		get {
			if (m_light == null) {
				m_light = GetComponent<Light>();
			}

			return m_light;
		}
	}

	public LightType LightType => Light.type;

	bool ShadowSupported() {
		return m_light.type == LightType.Directional || m_light.type == LightType.Spot;
	}

	public bool HasShadow {
		get {
			if (!ShadowSupported()) {
				return false;
			}

			return m_light.shadows == LightShadows.Hard || m_light.shadows == LightShadows.Soft;
		}
	}

	public override float CullRange => m_light.range;

	void OnEnable() {
		m_light = GetComponent<Light>();
		Register(LightType == LightType.Directional);
		CreateShadowResources();
	}

	void OnDisable() {
		Deregister();

		if (!HasShadow) {
			return;
		}

		m_light.RemoveCommandBuffers(LightEvent.AfterShadowMap);
		m_light.RemoveCommandBuffers(LightEvent.AfterScreenspaceMask);

		m_shadowCmd.Dispose();

		if (LightType == LightType.Directional) {
			m_matrixCmdBuffer.Dispose();
			MatrixBuffer.Dispose();
		}

		DestroyImmediate(m_shadowMap);
	}


	int GetShadowMapResolution() {
		int mapSize = 0;

		//Adapted from unity source (shared through Unity QA)
		//Modified to give "max" size essentially

		//these constant can vary slightly based on platform capabilities.
		const int kShadowmapPointSizeMax = 1024;
		const int kShadowmapSpotSizeMax = 2048;
		const int kShadowmapDirSizeMax = 4096;

		const float kMultPoint = 1.0f; // Assume "Very High" shadow map resolution is 1x screen size for point lights.
		const float kMultSpot = 2.0f;  // Assume "Very High" shadow map resolution is 2x screen size for spot lights.
		const float kMultDir = 3.8f;
		// Assume "Very High" shadow map resolution is almost 4x of screen size for directional lights.

		bool customRes = m_light.shadowCustomResolution > 0;

		if (customRes) {
			mapSize = Mathf.NextPowerOfTwo(m_light.shadowCustomResolution);
		}
		else {
			int quality = 3 - (int) QualitySettings.shadowResolution;
			float pixelSize = Mathf.Max(Screen.width, Screen.height);

			int maxSize = -1;

			switch (LightType) {
				case LightType.Point: {
					// Based on light size on screen
					mapSize = Mathf.NextPowerOfTwo(Mathf.RoundToInt(pixelSize * kMultPoint));
					maxSize = kShadowmapPointSizeMax;
				}
					break;

				case LightType.Spot: {
					// Based on light size on screen
					mapSize = Mathf.NextPowerOfTwo(Mathf.RoundToInt(pixelSize * kMultSpot));
					maxSize = kShadowmapSpotSizeMax;
				}
					break;

				case LightType.Directional: {
					mapSize = Mathf.NextPowerOfTwo(Mathf.RoundToInt(pixelSize * kMultDir));
					maxSize = kShadowmapDirSizeMax;
				}
					break;
			}

			mapSize >>= quality;
			mapSize = Mathf.Clamp(mapSize, 32, maxSize);
		}

		return mapSize;
	}

	public void CreateShadowResources() {
		if (m_shadowMap != null || !HasShadow) {
			return;
		}

		m_light.RemoveAllCommandBuffers();
		m_shadowCmd = new CommandBuffer();

		CreateShadowTex();

		m_light.AddCommandBuffer(LightEvent.AfterShadowMap, m_shadowCmd);

		if (m_light.type != LightType.Directional) {
			return;
		}

		MatrixBuffer = new ComputeBuffer(5, 4 * 16);
		int matrixTemp = Shader.PropertyToID("_MatrixTemp");

		//Draw dummy quad after screenspace mask to grab world matrices & light splits from light
		m_matrixCmdBuffer = new CommandBuffer();
		m_matrixCmdBuffer.GetTemporaryRT(matrixTemp, 1, 1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
		m_matrixCmdBuffer.SetRenderTarget(matrixTemp);
		m_matrixCmdBuffer.DrawMesh(QuadMesh, Matrix4x4.identity, ScreenShadowMaterial, 0);
		m_matrixCmdBuffer.ReleaseTemporaryRT(matrixTemp);
		m_light.AddCommandBuffer(LightEvent.AfterScreenspaceMask, m_matrixCmdBuffer);

		UpdateCommandBuffer();
	}

	void CreateShadowTex() {
		int res = GetShadowMapResolution();
		m_shadowMap = new RenderTexture(res, res, 0, RenderTextureFormat.RHalf) {name = "VaporShadowMap"};
	}

	void UpdateCommandBuffer() {
		if (!HasShadow) {
			return;
		}


		RenderTargetIdentifier shadowId = BuiltinRenderTextureType.CurrentActive;
		int blurTemp = Shader.PropertyToID("_ShadowBlurTemp");

		m_shadowCmd.Clear();
		m_shadowCmd.SetShadowSamplingMode(shadowId, ShadowSamplingMode.RawDepth);
		m_shadowCmd.GetTemporaryRT(blurTemp, m_shadowMap.width, m_shadowMap.height, 0, FilterMode.Bilinear,
			RenderTextureFormat.RGFloat);

		m_shadowCmd.SetGlobalTexture("_ShadowMap", shadowId);
		m_shadowCmd.Blit(null, m_shadowMap, ShadowFilterMaterial);
		/*
		//Blur the shadow map - disabled atm
		m_shadowCmd.SetGlobalVector("_ShadowBlurSize", Vector2.right * ShadowBlur);
		m_shadowCmd.Blit((RenderTargetIdentifier) m_shadowMap, blurTemp, ShadowBlurMaterial, 0);

		m_shadowCmd.SetGlobalVector("_ShadowBlurSize", Vector2.up * ShadowBlur);
		m_shadowCmd.Blit(blurTemp, m_shadowMap, ShadowBlurMaterial, 0);*/
		m_shadowCmd.ReleaseTemporaryRT(blurTemp);
	}

	public override void Inject(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
		if (HasShadow) {
			if (GetShadowMapResolution() != m_shadowMap.width) {
				DestroyImmediate(m_shadowMap);
				CreateShadowTex();
			}
		}

		//TODO: This doesn't really need to run every frame
		UpdateCommandBuffer();

		//Setup basic params
		Vector4 posRange = transform.position;
		posRange.w = 1.0f / (m_light.range * m_light.range);
		compute.SetVector("_LightPosRange", posRange);

		Vector4 lightStrength = m_light.color * m_light.intensity * FogScatterIntensity;
		lightStrength *= 10;
		compute.SetVector("_LightColor", lightStrength);

		//Per light type things
		switch (LightType) {
			case LightType.Directional:
				int dirKernel;

				if (HasShadow) {
					if (QualitySettings.shadowCascades > 1) {
						dirKernel = vapor.LightDirKernel.GetKernel(VaporKernel.ShadowMode.Cascaded);
					}
					else {
						dirKernel = vapor.LightDirKernel.GetKernel(VaporKernel.ShadowMode.Shadowed);
					}
				}
				else {
					dirKernel = vapor.LightDirKernel.GetKernel(VaporKernel.ShadowMode.None);
				}

				compute.SetVector("_LightPosRange", m_light.transform.forward);

				if (HasShadow) {
					compute.SetBuffer(dirKernel, "_MatrixBuf", MatrixBuffer);
					compute.SetTexture(dirKernel, "_ShadowMapTexture", m_shadowMap);
				}
				else {
					compute.SetTexture(dirKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
				}

				vapor.SetLightAccum(dirKernel, false);

				Profiler.BeginSample("Dir Light pass");
				var tex = vapor.GetDensityTex();
				compute.DispatchScaled(dirKernel, tex.width, tex.height, tex.volumeDepth);
				Profiler.EndSample();
				break;

			case LightType.Point:
				vapor.SetLightAccum(vapor.LightPointKernel, false);
				vapor.InjectObject(viewProj, vapor.LightPointKernel, this);
				break;

			case LightType.Spot:
				int spotKernel =
					vapor.LightSpotKernel.GetKernel(HasShadow ? VaporKernel.ShadowMode.Shadowed : VaporKernel.ShadowMode.None);

				if (HasShadow) {
					Matrix4x4 v = transform.worldToLocalMatrix;
					Matrix4x4 p =
						GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(m_light.spotAngle, 1.0f,
							m_light.shadowNearPlane,
							m_light.range), true);

					//For some reason z is flipped :(
					p *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

					compute.SetMatrix("_SpotShadowMatrix", p * v);
					compute.SetTexture(spotKernel, "_SpotShadow", m_shadowMap);
				}

				var lightProjMatrix = Matrix4x4.identity;
				float d = Mathf.Deg2Rad * m_light.spotAngle * 0.5f;
				d = Mathf.Cos(d) / Mathf.Sin(d);
				lightProjMatrix[3, 2] = 2f / d;
				lightProjMatrix[3, 3] = SpotBaseSize;
				var mat = lightProjMatrix * transform.worldToLocalMatrix;
				compute.SetMatrix("_SpotMatrix", mat);
				if (m_light.cookie != null) {
					compute.SetTexture(spotKernel, "_SpotCookie", m_light.cookie);
				}
				else {
					compute.SetTexture(spotKernel, "_SpotCookie", vapor.SpotCookie);
				}

				vapor.SetLightAccum(spotKernel, false);
				vapor.InjectObject(viewProj, spotKernel, this);
				break;
		}
	}

	//Bit hacky, but spits out some world positions that are encapsulated in UV space bounds in main Vapor class
	public override void GetBounds(Transform space, List<Vector3> worldBounds) {
		Vector3 right, up, forward;
		var trans = transform;
		Vector3 position = trans.position;
		var range = m_light.range;

		switch (LightType) {
			case LightType.Point:
				//Add 8 frustum aligned corners...
				right = range * space.right;
				up = range * space.up;
				forward = range * space.forward;

				worldBounds.Add(position + right + up - forward);
				worldBounds.Add(position + right - up - forward);
				worldBounds.Add(position - right + up - forward);
				worldBounds.Add(position - right - up - forward);

				worldBounds.Add(position + right + up + forward);
				worldBounds.Add(position + right - up + forward);
				worldBounds.Add(position - right + up + forward);
				worldBounds.Add(position - right - up + forward);
				break;

			case LightType.Spot:
				float tanSize = Mathf.Clamp01(Mathf.Tan(m_light.spotAngle * Mathf.Deg2Rad)) * m_light.range;
				right = tanSize * trans.right;
				up = tanSize * trans.up;
				forward = range * trans.forward;

				worldBounds.Add(position + right + up + forward);
				worldBounds.Add(position + right - up + forward);
				worldBounds.Add(position - right + up + forward);
				worldBounds.Add(position - right - up + forward);

				worldBounds.Add(position + forward);
				break;
		}
	}
}