using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using VaporAPI;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
[ImageEffectAllowedInSceneView]
public class Vapor : MonoBehaviour {
	public enum QualitySetting {
		Low,
		Medium,
		High,
		Custom
	}

	[SerializeField] [Tooltip("Physical properties of the fog")]
	VaporSetting m_setting;

	[Tooltip("Properties to blend towards controlled by the blend time")] [SerializeField]
	VaporSetting m_blendToSetting;

	[SerializeField] [Range(0.0f, 1.0f)] float m_blendTime;

	/// <summary>
	///     Lerp factor between <see cref="Setting" /> and <see cref="BlendToSetting" />
	/// </summary>
	public float BlendTime {
		get => m_blendTime;
		set => m_blendTime = Mathf.Clamp01(value);
	}

	/// <summary>
	///     Current blend setting. Control blending with <see cref="BlendTime" />
	/// </summary>
	public VaporSetting Setting {
		get {
			if (m_setting == null) {
				m_setting = DefaultSetting;
			}

			return m_setting;
		}
	}

	/// <summary>
	///     Default Vapor setting
	/// </summary>
	public static VaporSetting DefaultSetting => Resources.Load<VaporSetting>("DefaultVaporSetting");

	/// <summary>
	///     Target blend setting. Control blending with <see cref="BlendTime" />
	/// </summary>
	public VaporSetting BlendToSetting {
		get {
			if (m_blendToSetting == null) {
				m_blendToSetting = DefaultSetting;
			}

			return m_blendToSetting;
		}
	}

	public static Color DefaultScatteringColor = 200.0f * new Color(1.0f / 650.0f, 1.0f / 530.0f, 1.0f / 460.0f);


	[Range(0.0f, 1.0f)] public float DirectionalScattering;

	public Color DirectionalScatteringColor = Color.white * 0.5f;

	public float ScatteringIntensity = 1.0f;
	public Color ScatteringColor = DefaultScatteringColor;
	public float AtmosphereThickness = 10.0f;

	public float AtmosphereRingPower = 15.0f;
	public float AtmosphereRingSize = 10.0f;
	public float AveragingSpeed = 0.2f;
	[Range(0.0f, 2.0f)] public float TemporalStrength = 0.5f;
	public float DepthCurvePower = 4.0f;

	[Range(0.0f, 1.0f)] public float NoiseColorStrength;
	[Range(0.0f, 1.0f)] public float NoiseExtinctionStrength;

	public Vector3 NoiseWeights = new Vector3(5.0f, 2.0f, 1.0f);
	public Vector3 NoiseFrequency = new Vector3(0.1f, 0.2f, 0.5f);
	public Vector3 NoiseSpeed = new Vector3(1, 2, 3);
	public float NoisePower = 2.5f;
	public Texture2D NoiseTexture;

	[HideInInspector] public Texture2D SpotCookie;
	Texture2D m_blueNoiseTex;

	CullingGroup m_cullGroup;
	BoundingSphere[] m_spheres = new BoundingSphere[64];
	Camera m_camera;

	[Range(0.5f, 2.0f)] public float GlobalResolutionMult = 1.0f;
	[Range(0.5f, 2.0f)] public float DepthResolutionMult = 1.0f;

	/// <summary>
	///     Whether or not Forward shaders have the proper Vapor integration. Disable to revert to using a fullscreen pass to
	///     apply Vapor
	/// </summary>
	public bool ShadersHaveVaporIntegrated = true;

	public bool DisplayInSceneView;

	const int c_defaultHorizontalRes = 160;
	const int c_defaultDepthRes = 128;

	/// <summary>
	///     Nr. of pixels horizontally in the volume texture
	/// </summary>
	public int HorizontalRes => Mathf.RoundToInt(c_defaultHorizontalRes * GlobalResolutionMult / 8.0f) * 8;

	/// <summary>
	///     Nr. of pixels vertically in the volume texture
	/// </summary>
	public int VerticalRes => Mathf.RoundToInt(c_defaultHorizontalRes * 9.0f / 16.0f * GlobalResolutionMult / 8.0f) * 8;

	/// <summary>
	///     Nr. of pixels in depth direction in the volume texture
	/// </summary>
	public int DepthRes => Mathf.RoundToInt(c_defaultDepthRes * GlobalResolutionMult * DepthResolutionMult / 8.0f) * 8;

	bool IsEditorCamera {
		get {
		#if UNITY_EDITOR
			return SceneView.currentDrawingSceneView != null && SceneView.currentDrawingSceneView.camera == Camera.current;
		#else
			return false;
		#endif
		}
	}

	ComputeShader m_vaporCompute;

	RenderTexture m_densityTex;
	RenderTexture m_scatterTex;
	RenderTexture m_scatterTexOld;
	RenderTexture m_integratedTexture;

	RenderTexture m_localLightTexR;
	RenderTexture m_localLightTexG;
	RenderTexture m_localLightTexB;

	public const int BytesPerFroxel = 2 * 4 * 4 + 4 * 3;

	int m_densityKernel;

	[NonSerialized] internal int ZoneKernel;
	[NonSerialized] internal int CustomLightKernel;
	[NonSerialized] internal int LightPointKernel;
	[NonSerialized] internal VaporKernel LightSpotKernel;
	[NonSerialized] internal VaporKernel LightDirKernel;

	int m_scatterKernel;
	int m_integrateKernel;
	int m_integrateClearKernel;
	int m_lightClearKernel;

	Material m_fogMat;

	Matrix4x4 m_vpMatrixOld;
	int[] m_offset = new int[3];
	int[] m_res = new int[3];

	bool m_instant;
	List<Vector3> m_worldBounds = new List<Vector3>();

	int m_sampleIndex;
	const int c_sampleCount = 8;

	void OnEnable() {
		m_camera = GetComponent<Camera>();
		m_cullGroup = new CullingGroup();
		m_cullGroup.SetBoundingSpheres(m_spheres);

		//Break dependence on Resources? Could cause stalls for people grmbl
		m_vaporCompute = Resources.Load<ComputeShader>("VaporSim");
		m_densityKernel = m_vaporCompute.FindKernel("FogDensity");
		ZoneKernel = m_vaporCompute.FindKernel("ZoneWrite");
		CustomLightKernel = m_vaporCompute.FindKernel("CustomLightWrite");
		LightPointKernel = m_vaporCompute.FindKernel("LightPoint");
		LightSpotKernel = new VaporKernel(m_vaporCompute, "LightSpot");
		LightDirKernel = new VaporKernel(m_vaporCompute, "LightDirectional");
		m_lightClearKernel = m_vaporCompute.FindKernel("LightClear");
		m_scatterKernel = m_vaporCompute.FindKernel("Scatter");
		m_integrateKernel = m_vaporCompute.FindKernel("Integrate");
		m_integrateClearKernel = m_vaporCompute.FindKernel("IntegrateClear");
		
		m_fogMat = new Material(Shader.Find("Hidden/VaporPost")) {hideFlags = HideFlags.HideAndDontSave};

		m_blueNoiseTex = Resources.Load<Texture2D>("BlueNoise");

		CreateTextures();
		MarkInstantRender();
	}

	void CreateTextures() {
		CreateTexture(ref m_densityTex);
		CreateTexture(ref m_localLightTexR, RenderTextureFormat.RHalf);
		CreateTexture(ref m_localLightTexG, RenderTextureFormat.RHalf);
		CreateTexture(ref m_localLightTexB, RenderTextureFormat.RHalf);
		CreateTexture(ref m_scatterTex);
		CreateTexture(ref m_scatterTexOld);
		CreateTexture(ref m_integratedTexture);
	}

	void OnDisable() {
		DestroyImmediate(m_densityTex);
		DestroyImmediate(m_localLightTexR);
		DestroyImmediate(m_localLightTexG);
		DestroyImmediate(m_localLightTexB);
		DestroyImmediate(m_scatterTex);
		DestroyImmediate(m_scatterTexOld);
		DestroyImmediate(m_integratedTexture);

		if (m_cullGroup != null) {
			m_cullGroup.Dispose();
		}
	}

	void CreateTexture(ref RenderTexture tex, RenderTextureFormat format = RenderTextureFormat.ARGBHalf) {
		if (tex != null) {
			DestroyImmediate(tex);
		}

		tex = new RenderTexture(HorizontalRes, VerticalRes, 0, format) {
			volumeDepth = DepthRes,
			dimension = TextureDimension.Tex3D,
			enableRandomWrite = true,
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear
		};

		tex.Create();
	}

	//TODO: This jitter doesn't seem ideal
	float GetHaltonValue(int index, int radix) {
		float result = 0f;
		float fraction = 1.0f / radix;

		while (index > 0) {
			result += index % radix * fraction;
			index /= radix;
			fraction /= radix;
		}

		return result;
	}

	Vector2 GenerateRandomOffset() {
		var offset = new Vector2(
			GetHaltonValue(m_sampleIndex & 1023, 2),
			GetHaltonValue(m_sampleIndex & 1023, 3));

		if (++m_sampleIndex >= c_sampleCount) {
			m_sampleIndex = 0;
		}

		return offset;
	}

	void Update() {
		if (m_densityTex == null || m_densityTex.width != HorizontalRes || m_densityTex.height != VerticalRes || m_densityTex.volumeDepth != DepthRes) {
			CreateTextures();
			MarkInstantRender();
		}

		while (VaporObject.All.Count >= m_spheres.Length) {
			Array.Resize(ref m_spheres, m_spheres.Length * 2);
		}

		for (int i = 0; i < VaporObject.All.Count; i++) {
			var vap = VaporObject.All[i];
			m_spheres[i].position = vap.transform.position;
			m_spheres[i].radius = vap.CullRange;
		}
	}

	void OnPreRender() {
		if (IsEditorCamera && !DisplayInSceneView) {
			m_vaporCompute.SetTexture(m_integrateClearKernel, "_IntegratedTexture", m_integratedTexture);
			m_vaporCompute.DispatchScaled(m_integrateClearKernel, m_integratedTexture.width, m_integratedTexture.height, m_integratedTexture.volumeDepth);
			Shader.SetGlobalTexture("_VaporFogTexture", m_integratedTexture);
			return;
		}

		if (Camera.current.stereoEnabled && Camera.current.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right) {
			return;
		}


		m_cullGroup.SetBoundingSphereCount(VaporObject.All.Count);

		//Get direction light & bind random acess textures
		foreach (VaporObject vap in VaporObject.All) {
			var vaporLight = vap as VaporLight;

			if (vaporLight == null || !vaporLight.HasShadow || vaporLight.LightType != LightType.Directional) {
				continue;
			}

			vaporLight.CreateShadowResources();
			Graphics.SetRandomWriteTarget(1, vaporLight.MatrixBuffer);
			//Graphics.SetRandomWriteTarget(6, vaporLight.LightSplitsBuffer);
		}

		Shader.SetGlobalTexture("_VaporFogTexture", m_integratedTexture);

		bool inForward = Camera.current.actualRenderingPath == RenderingPath.Forward;
		Shader.SetGlobalFloat("_VaporForward", inForward ? 1 : 0);
	}

	float DeviceToLinearDepth(float device) {
		Vector4 planeSettings = GetPlaneSettings(m_camera.nearClipPlane, m_camera.farClipPlane);
		return device / (planeSettings.y - device * planeSettings.z);
	}

	Vector3 GetUvFromWorld(Vector3 world, Matrix4x4 viewProj) {
		var trans = transform;
		var forward = trans.forward;

		var plane = new Plane(forward, trans.position + forward * m_camera.nearClipPlane);
		float dist = plane.GetDistanceToPoint(world);
		world -= Mathf.Min(0, dist) * transform.forward;

		Vector3 device = viewProj.MultiplyPoint(world);

		Vector3 uv;
		uv.z = DeviceToLinearDepth(Mathf.Clamp01(device.z));
		uv.z = Mathf.Pow(Mathf.Clamp01(uv.z), 1.0f / DepthCurvePower);
		uv.x = (device.x + 1.0f) * 0.5f;
		uv.y = (device.y + 1.0f) * 0.5f;

		uv.x = Mathf.Clamp01(uv.x);
		uv.y = Mathf.Clamp01(uv.y);
		uv.z = Mathf.Clamp01(uv.z);

		return uv;
	}

	public void MarkInstantRender() {
		m_instant = true;
	}

	public void SetLightAccum(int kernel, bool read) {
		if (read) {
			m_vaporCompute.SetTexture(kernel, "_LightReadR", m_localLightTexR);
			m_vaporCompute.SetTexture(kernel, "_LightReadG", m_localLightTexG);
			m_vaporCompute.SetTexture(kernel, "_LightReadB", m_localLightTexB);
		}
		else {
			m_vaporCompute.SetTexture(kernel, "_LightAccumR", m_localLightTexR);
			m_vaporCompute.SetTexture(kernel, "_LightAccumG", m_localLightTexG);
			m_vaporCompute.SetTexture(kernel, "_LightAccumB", m_localLightTexB);
		}
	}

	internal void InjectObject(Matrix4x4 viewProj, int kernel, VaporObject obj) {
		if (kernel == -1) {
			return;
		}

		m_worldBounds.Clear();
		obj.GetBounds(transform, m_worldBounds);
		Bounds uvBounds = new Bounds(GetUvFromWorld(obj.transform.position, viewProj), Vector3.one * 0.05f);

		foreach (Vector3 bound in m_worldBounds) {
			Vector3 uv = GetUvFromWorld(bound, viewProj);
			uvBounds.Encapsulate(uv);
		}

		Vector3 min = uvBounds.min;
		Vector3 max = uvBounds.max;

		m_offset[0] = Mathf.FloorToInt(min.x * m_densityTex.width);
		m_offset[1] = Mathf.FloorToInt(min.y * m_densityTex.height);
		m_offset[2] = Mathf.FloorToInt(min.z * m_densityTex.volumeDepth);

		m_vaporCompute.SetInts("_LightWriteLower", m_offset);

		int maxX = Mathf.CeilToInt(max.x * m_densityTex.width);
		int maxY = Mathf.CeilToInt(max.y * m_densityTex.height);
		int maxZ = Mathf.CeilToInt(max.z * m_densityTex.volumeDepth);

		Profiler.BeginSample("Object pass");
		m_vaporCompute.DispatchScaled(kernel, maxX - m_offset[0], maxY - m_offset[1], maxZ - m_offset[2]);
		Profiler.EndSample();
	}

	static Vector4 GetPlaneSettings(float near, float far) {
		return new Vector4(near, far, far - near, near * far);
	}

	static Matrix4x4 GetJitteredMatrix(Camera camera, Vector2 offset) {
		float vertical = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView);
		float horizontal = vertical * camera.aspect;
		float near = camera.nearClipPlane;
		float far = camera.farClipPlane;

		offset.x *= horizontal / (0.5f * camera.pixelWidth);
		offset.y *= vertical / (0.5f * camera.pixelHeight);

		float left = (offset.x - horizontal) * near;
		float right = (offset.x + horizontal) * near;
		float top = (offset.y + vertical) * near;
		float bottom = (offset.y - vertical) * near;

		var matrix = new Matrix4x4();
		matrix[0, 0] = 2f * near / (right - left);
		matrix[0, 1] = 0f;
		matrix[0, 2] = (right + left) / (right - left);
		matrix[0, 3] = 0f;

		matrix[1, 0] = 0f;
		matrix[1, 1] = 2f * near / (top - bottom);
		matrix[1, 2] = (top + bottom) / (top - bottom);
		matrix[1, 3] = 0f;

		matrix[2, 0] = 0f;
		matrix[2, 1] = 0f;
		matrix[2, 2] = -(far + near) / (far - near);
		matrix[2, 3] = -(2f * far * near) / (far - near);

		matrix[3, 0] = 0f;
		matrix[3, 1] = 0f;
		matrix[3, 2] = -1f;
		matrix[3, 3] = 0f;

		return matrix;
	}

	void DoComputeSteps() {
		//TODO: Could switch between eyes? Would slightly blur the fog -> Probably nice
		if (Camera.current.stereoEnabled && Camera.current.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right) {
			m_setting = Setting; //Do update default setting if it's null
			return;
		}

		Graphics.ClearRandomWriteTargets();

		if (m_instant) {
			m_vaporCompute.SetFloat("_ExponentialWeight", 1.0f);
			m_instant = false;
		}
		else {
			m_vaporCompute.SetFloat("_ExponentialWeight", AveragingSpeed);
		}

		m_vaporCompute.SetFloat("_TemporalStrength", TemporalStrength);
		m_vaporCompute.SetInt("_Frame", Random.Range(0, m_blueNoiseTex.width * m_blueNoiseTex.height));
		m_vaporCompute.SetVector("_CameraPos", Camera.current.transform.position);

		bool lowCascade = QualitySettings.shadowCascades == 2;
		m_vaporCompute.SetVector("_ShadowRange", lowCascade ? new Vector4(0.0f, 0.5f, 0.0f, 1.0f) : new Vector4(0.5f, 0.5f));

		Matrix4x4 v = Camera.current.stereoEnabled ? m_camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left) : m_camera.worldToCameraMatrix;
		Vector2 jitter = GenerateRandomOffset();
		jitter *= TemporalStrength * 10;

		Matrix4x4 p = GetJitteredMatrix(m_camera, jitter);
		Matrix4x4 vp = p * v;

		//Set VP from old frame for reprojection
		Matrix4x4 vpi = vp.inverse;

		m_vaporCompute.SetMatrix("_VAPOR_REPROJECT", m_vpMatrixOld * vpi);
		m_vaporCompute.SetMatrix("_VAPOR_I_VP", vpi);
		m_vaporCompute.SetMatrix("_VAPOR_VP", vp);
		m_vpMatrixOld = vp;

		//Bind system settings
		{
			m_res[0] = m_densityTex.width;
			m_res[1] = m_densityTex.height;
			m_res[2] = m_densityTex.volumeDepth;

			m_vaporCompute.SetInts("_VaporResolution", m_res);

			m_vaporCompute.SetFloat("_VaporDepthPow", DepthCurvePower);
			Shader.SetGlobalFloat("_VaporDepthPow", DepthCurvePower);

			float near = m_camera.nearClipPlane;
			float far = m_camera.farClipPlane;
			Vector4 planeSettings = GetPlaneSettings(near, far);

			m_vaporCompute.SetVector("_VaporPlaneSettings", planeSettings);
			Shader.SetGlobalVector("_VaporPlaneSettings", planeSettings);
			Shader.SetGlobalTexture("_VaporFogTexture", m_integratedTexture);
			Shader.SetGlobalMatrix("_VAPOR_I_VP", vpi);
			Shader.SetGlobalMatrix("_VAPOR_VP", vp);


			float zc0 = 1.0f - far / near;
			float zc1 = far / near;
			m_vaporCompute.SetVector("_ZBufferParams", new Vector4(zc0, zc1, zc0 / far, zc1 / far));

			for (int i = 0; i < 11; ++i) {
				m_vaporCompute.SetTexture(i, "_BlueNoise", m_blueNoiseTex);
			}
		}

		//Bind noise settings
		{
			Vector4 scale = new Vector4(2.5f, 1.0f, 2.5f);
			m_vaporCompute.SetVector("_NoiseWeights", NoiseWeights / (NoiseWeights.x + NoiseWeights.y + NoiseWeights.z));
			float colMin = 1.0f - NoiseColorStrength;
			float extinctMin = 1.0f - NoiseExtinctionStrength;

			m_vaporCompute.SetVector("_NoiseMin", new Vector4(colMin, colMin, colMin, extinctMin));

			m_vaporCompute.SetVector("_NoiseFrequency", Vector4.Scale(NoiseFrequency, scale));
			m_vaporCompute.SetVector("_NoiseSpeed", Vector4.Scale(Vector4.Scale(NoiseSpeed, scale), NoiseFrequency) * 0.01f);
			m_vaporCompute.SetFloat("_NoisePower", NoisePower);
		}

		//Bind scattering settings
		{
			//refractive index of nitrogen
			const double indexSqr = 1.0002772 * 1.0002772;
			const double r = (indexSqr - 1) * (indexSqr - 1) / ((indexSqr + 2) * (indexSqr + 2));

			double size = Mathf.Pow(ScatteringIntensity * 1e3f, 1.0f / 6.0f);

			const double scatteringSize = 1e-18 * 2.5e25;

			float rSize = (float) (r * Math.Pow(size * ScatteringColor.r / 200.0f, 4.0f) * size * size * scatteringSize);
			float gSize = (float) (r * Math.Pow(size * ScatteringColor.g / 200.0f, 4.0f) * size * size * scatteringSize);
			float bSize = (float) (r * Math.Pow(size * ScatteringColor.b / 200.0f, 4.0f) * size * size * scatteringSize);

			Vector3 rayleighBase = new Vector3(rSize, gSize, bSize);
			Vector3 rayleighWeight = rayleighBase * Mathf.Pow(2.0f * Mathf.PI, 4.0f) / Mathf.Pow(2.0f, 6.0f);
			Vector3 rayleighCross = rayleighBase * 24 * Mathf.Pow(Mathf.PI, 3.0f);

			rayleighCross.x = (float) Math.Pow(1.0 - rayleighCross.x, 1000);
			rayleighCross.y = (float) Math.Pow(1.0 - rayleighCross.y, 1000);
			rayleighCross.z = (float) Math.Pow(1.0 - rayleighCross.z, 1000);

			m_vaporCompute.SetVector("_Rayleigh", rayleighWeight * 1e5f);
			m_vaporCompute.SetVector("_RayleighCross", rayleighCross);

			var mieScatter = new Vector4(DirectionalScatteringColor.r,
				DirectionalScatteringColor.g,
				DirectionalScatteringColor.b,
				DirectionalScattering * 0.999f);

			m_vaporCompute.SetVector("_MieScatter", mieScatter);
			m_vaporCompute.SetFloat("_LambertBeerDensity", Setting.Extinction * 0.1f);

			const float planetSize = 8000.0f;
			float atmosphereRadius = planetSize + AtmosphereThickness;
			var atmosphereSettings = new Vector4(AtmosphereRingPower, AtmosphereRingSize, atmosphereRadius * atmosphereRadius, planetSize);
			m_vaporCompute.SetVector("_Atmosphere", atmosphereSettings);
		}

		Profiler.BeginSample("Write global density");
		Setting.Bind(m_vaporCompute, m_densityKernel, BlendToSetting, m_blendTime);
		m_vaporCompute.SetTexture(m_densityKernel, "_DensityTextureWrite", m_densityTex);
		m_vaporCompute.SetTexture(m_densityKernel, "_NoiseTex", NoiseTexture);
		m_vaporCompute.DispatchScaled(m_densityKernel, m_densityTex.width, m_densityTex.height, m_densityTex.volumeDepth);
		Profiler.EndSample();

		Profiler.BeginSample("Vapor Object Passes");
		//If there's no directional -> manual clear light buffer
		if (VaporObject.All.Count == 0 || VaporObject.All[0] as VaporLight == null ||
			((VaporLight) VaporObject.All[0]).LightType != LightType.Directional) {
			SetLightAccum(m_lightClearKernel, false);
			m_vaporCompute.DispatchScaled(m_lightClearKernel, m_scatterTex.width, m_scatterTex.height, m_scatterTex.volumeDepth);
		}

		//Inject vapor objects
		foreach (VaporObject vap in VaporObject.All) {
			vap.Inject(this, m_vaporCompute, vp);
		}

		Profiler.EndSample();

		Setting.Bind(m_vaporCompute, m_densityKernel, BlendToSetting, m_blendTime);

		Profiler.BeginSample("Scattering");
		SetLightAccum(m_scatterKernel, true);
		m_vaporCompute.SetTexture(m_scatterKernel, "_DensityTexture", m_densityTex);
		m_vaporCompute.SetTexture(m_scatterKernel, "_ScatterTextureOld", m_scatterTexOld);
		m_vaporCompute.SetTexture(m_scatterKernel, "_ScatterTexture", m_scatterTex);
		m_vaporCompute.DispatchScaled(m_scatterKernel, m_scatterTex.width, m_scatterTex.height, m_scatterTex.volumeDepth);
		Profiler.EndSample();

		Profiler.BeginSample("Integration");
		m_vaporCompute.SetTexture(m_integrateKernel, "_IntegratedTexture", m_integratedTexture);
		m_vaporCompute.SetTexture(m_integrateKernel, "_ScatterTextureOld", m_scatterTex);
		m_vaporCompute.DispatchScaled(m_integrateKernel, m_scatterTex.width, m_scatterTex.height, 1);
		Profiler.EndSample();

		Profiler.BeginSample("Blit properties");
		var temp = m_scatterTex;
		m_scatterTex = m_scatterTexOld;
		m_scatterTexOld = temp;
		Profiler.EndSample();
	}

	[ImageEffectOpaque]
	void OnRenderImage(RenderTexture source, RenderTexture destination) {
		if (IsEditorCamera && !DisplayInSceneView) {
			Profiler.BeginSample("Vapor Direct Blit");
			Graphics.Blit(source, destination);
			Profiler.EndSample();
			return;
		}

	#if UNITY_EDITOR
		if (!Application.isPlaying) {
			Profiler.BeginSample("Editor Only Repaint");

			if (Selection.Contains(gameObject)) {
				HandleUtility.Repaint();
			}

			Profiler.EndSample();
		}
	#endif

		DoComputeSteps();

		var path = Camera.current.actualRenderingPath;
		if (path == RenderingPath.DeferredShading || path == RenderingPath.DeferredLighting || !ShadersHaveVaporIntegrated) {
			Profiler.BeginSample("Blit to screen");
			Graphics.Blit(source, destination, m_fogMat, 0);
			Profiler.EndSample();
		}
		else {
			Profiler.BeginSample("Blit to screen");
			Graphics.Blit(source, destination);
			Profiler.EndSample();
		}
	}

	public RenderTexture GetDensityTex() {
		return m_densityTex;
	}
}