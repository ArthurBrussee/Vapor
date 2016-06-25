using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vapor {
	[ExecuteInEditMode]
#if UNITY_5_4_OR_NEWER
	//[ImageEffectAllowedInSceneView]
#endif
	public class Vapor : MonoBehaviour {
		[SerializeField] private VaporSetting m_setting;

		public VaporSetting Setting {
			get {
				if (m_setting == null) {
					m_setting = Resources.Load<VaporSetting>("DefaultVaporSetting");
				}
				return m_setting;
			}
		}

		[Range(-1.0f, 1.0f)]
		public float Phase;

		public VaporGradient HeightGradient = new VaporGradient();
		public VaporGradient DistanceGradient = new VaporGradient();


		public float ShadowHardness = 0.1f;
		[Range(0.0f, 1.0f)]
		public float ShadowBias = 0.02f;
		public float AveragingSpeed = 0.05f;
		[Range(0.0f, 1.0f)] public float TemporalStrength = 0.4f;
		public float DepthCurvePower = 4.0f;
		public float BlurSize;


		public Vector2 NoiseBlend = Vector2.one;
		public Vector3 NoiseWeights = new Vector3(5.0f, 2.0f, 1.0f);
		public Vector3 NoiseFrequency = Vector3.one;
		public Vector3 NoiseSpeed = Vector3.one;
		public float NoisePower = 3.5f;


		public Texture2D NoiseTexture;

		//[SerializeField] private NoiseLayer m_baseLayer = new NoiseLayer();
		//[SerializeField] private NoiseLayer m_secondaryLayer = new NoiseLayer();
		//[SerializeField] private NoiseLayer m_detailLayer = new NoiseLayer();

		/*
		public NoiseLayer GetNoiseLayer(int index) {
			switch (index) {
				case 0:
					return m_baseLayer;
				case 1:
					return m_secondaryLayer;
				case 2:
					return m_detailLayer;
				default:
					return null;
			}
		}*/

		private CullingGroup m_cullGroup;
		private BoundingSphere[] m_spheres = new BoundingSphere[64];
		private Camera m_camera;

		[HideInInspector] public Texture2D SpotCookie;

		//These must be multiples of 4!
		public const int HorizontalTextureRes = 160;
		public const int VerticalTextureRes = 88; //160 * 9/16 == 90 -> 88 - rounded to 8
		public const int VolumeDepth = 256;

		private ComputeShader m_vaporCompute;

		[NonSerialized]
		public RenderTexture DensityTex;
		private RenderTexture m_localLightTexR;
		private RenderTexture m_localLightTexG;
		private RenderTexture m_localLightTexB;

		private RenderTexture m_scatterTex;
		private RenderTexture m_scatterTexOld;
		private RenderTexture m_integratedTexture;

		public int DensityKernel;
		public int LightPointKernel;
		public VaporKernel LightSpotKernel;
		public VaporKernel LightDirKernel;

		public int ZoneKernel;
		public int ScatterKernel;
		public int IntegrateKernel;

		private Material m_fogMat;

		private Texture2D m_gradientTex;
		public Texture2D GradientTex { get { return m_gradientTex; } }
		//TAA
		private Matrix4x4 m_vpMatrixOld;
		private int m_frameCount;
		private int[] m_offset = new int[3];

		private RenderTexture m_fogFilterTexture;

		private void OnEnable() {
			m_camera = GetComponent<Camera>();
			m_cullGroup = new CullingGroup();
			m_cullGroup.SetBoundingSpheres(m_spheres);

			//Break dependance on Resources? Could cause stalls for people grmbl
			m_vaporCompute = Resources.Load<ComputeShader>("VaporSim");

			DensityKernel = m_vaporCompute.FindKernel("FogDensity");
			ZoneKernel = m_vaporCompute.FindKernel("ZoneWrite");
			LightPointKernel = m_vaporCompute.FindKernel("LightPoint");
			LightSpotKernel = new VaporKernel(m_vaporCompute, "LightSpot");
			LightDirKernel = new VaporKernel(m_vaporCompute, "LightDirectional");
			ScatterKernel = m_vaporCompute.FindKernel("Scatter");
			IntegrateKernel = m_vaporCompute.FindKernel("Integrate");

			m_fogMat = new Material(Shader.Find("Hidden/VaporPost"));

			CreateTexture(ref DensityTex);
			CreateTexture(ref m_localLightTexR, RenderTextureFormat.RHalf);
			CreateTexture(ref m_localLightTexG, RenderTextureFormat.RHalf);
			CreateTexture(ref m_localLightTexB, RenderTextureFormat.RHalf);
			CreateTexture(ref m_scatterTex);
			CreateTexture(ref m_scatterTexOld);

			CreateTexture(ref m_integratedTexture);

			UpdateGradients();
			/*
			for (int i = 0; i < 3; ++i) {
				if (GetNoiseLayer(i).NeedsBuild()) {
					BakeNoiseLayers();
					break;
				}
			}*/
		}

		private void OnDisable() {
			DestroyImmediate(DensityTex);
			DestroyImmediate(m_localLightTexR);
			DestroyImmediate(m_localLightTexG);
			DestroyImmediate(m_localLightTexB);
			
			DestroyImmediate(m_scatterTex);
			DestroyImmediate(m_scatterTexOld);

			DestroyImmediate(m_integratedTexture);

			DestroyImmediate(m_gradientTex);

			//for (int i = 0; i < 3; ++i) {
				//GetNoiseLayer(i).DestroyTex();
			//}

			if (m_cullGroup != null) {
				m_cullGroup.Dispose();
			}
		}

		/*
		public void BakeNoiseLayers() {
			for (int i = 0; i < 3; ++i) {
				GetNoiseLayer(i).Bake();
			}
		}*/

		public void UpdateGradients() {
			const int res = 128;

			if (m_gradientTex == null) {
				m_gradientTex = new Texture2D(res, res, TextureFormat.ARGB32, false) {wrapMode = TextureWrapMode.Clamp};
			}

			Color[] texColors = new Color[res * res];
			for (int i = 0; i < res; i++) {
				for (int j = 0; j < res; j++) {
					float ti = (float) i / (res - 1);
					float tj = (float) j / (res - 1);

					Color colx = DistanceGradient.Gradient.Evaluate(ti);
					Color coly = HeightGradient.Gradient.Evaluate(tj);

					float size = ti + tj + 0.0001f;

					texColors[i + j * res] = colx * ti / size + coly * tj / size;
				}
			}

			m_gradientTex.SetPixels(texColors);
			m_gradientTex.Apply(false, false);
		}

		private void CreateTexture(ref RenderTexture tex, RenderTextureFormat format = RenderTextureFormat.ARGBHalf) {
			if (tex != null) {
				Debug.LogError("Old texture floating around?" + tex.name);
				return;
			}

			tex = new RenderTexture(HorizontalTextureRes, VerticalTextureRes, 0, format) {
				volumeDepth = VolumeDepth,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear
			};

			tex.Create();
		}

		void Update() {
			while (VaporObject.All.Count >= m_spheres.Length) {
				Array.Resize(ref m_spheres, m_spheres.Length * 2);
			}

			for (int i = 0; i < VaporObject.All.Count; i++) {
				var vaporLight = VaporObject.All[i];
				m_spheres[i].position = vaporLight.transform.position;
				m_spheres[i].radius = vaporLight.Range;
			}
		}

		void OnPreRender() {
			m_cullGroup.SetBoundingSphereCount(VaporObject.All.Count);

			for (int i = 0; i < VaporObject.All.Count; i++) {
				var vaporLight = VaporObject.All[i] as VaporLight;

				if (vaporLight == null || !vaporLight.HasShadow || vaporLight.LightType != LightType.Directional) {
					continue;
				}

				vaporLight.CreateShadowResources();
				Graphics.SetRandomWriteTarget(1, vaporLight.MatrixBuffer);
				Graphics.SetRandomWriteTarget(2, vaporLight.LightSplitsBuffer);
			}
		}

		float DeviceToLinearDepth(float device) {
			return (2 * m_camera.nearClipPlane) / (m_camera.nearClipPlane + m_camera.farClipPlane - device * (m_camera.farClipPlane - m_camera.nearClipPlane));
		}

		Vector3 GetUvFromWorld(Vector3 world, Matrix4x4 viewProj) {
			Vector3 device = viewProj.MultiplyPoint(world);



			Vector3 uv;
			uv.z = DeviceToLinearDepth(device.z);
			uv.z = Mathf.Pow(Mathf.Clamp01(uv.z), 1.0f / DepthCurvePower);
			uv.x = (device.x + 1.0f) * 0.5f;
			uv.y = (device.y + 1.0f) * 0.5f;
				
			uv.x = Mathf.Clamp01(uv.x);
			uv.y = Mathf.Clamp01(uv.y);
			uv.z = Mathf.Clamp01(uv.z);

			return uv;
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

		public void InjectObject(Matrix4x4 viewProj, int kernel, VaporObject vaporLight) {
			if (kernel == -1) {
				return;
			}

			//TODO: Spot could have tighter bounds than point light
			//TODO: This code feels like a mess
			Vector3 dir = transform.forward;

			float dist = Vector3.Dot(dir, vaporLight.transform.position - transform.position);
			dist -= m_camera.nearClipPlane;

			float range = vaporLight.Range;
			Vector3 near = vaporLight.transform.position - dir * Mathf.Min(dist, range);
			Vector3 far = vaporLight.transform.position + dir * Mathf.Min(dist, range);

			Vector3 lower = near - transform.right * range - transform.up * range;
			Vector3 upper = near + transform.right * range + transform.up * range;

			Vector3 lowerV = m_camera.WorldToViewportPoint(lower);
			Vector3 upperV = m_camera.WorldToViewportPoint(upper);

			float xWidth = upperV.x - lowerV.x;
			float yWidth = upperV.y - lowerV.y;


			Vector3 startUv = GetUvFromWorld(lower, viewProj);

			m_offset[0] = Mathf.FloorToInt(startUv.x * HorizontalTextureRes);
			m_offset[1] = Mathf.FloorToInt(startUv.y * VerticalTextureRes);
			m_offset[2] = Mathf.FloorToInt(startUv.z * VolumeDepth);

			m_vaporCompute.SetInts("_LightWriteLower", m_offset);

			Vector3 endUv = GetUvFromWorld(far, viewProj);
	
			float zWidth = endUv.z - startUv.z;

			int xCount = Mathf.CeilToInt(Mathf.Clamp01(xWidth) * HorizontalTextureRes);
			int yCount = Mathf.CeilToInt(Mathf.Clamp01(yWidth) * VerticalTextureRes);
			int zCount = Mathf.CeilToInt(Mathf.Clamp01(zWidth) * VolumeDepth);
			Profiler.BeginSample("Object pass");
			m_vaporCompute.DispatchScaled(kernel, xCount, yCount, zCount);
			Profiler.EndSample();
		}

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			Graphics.ClearRandomWriteTargets();

			//TODO: Ideally do the compute part earlier in the frame. If we one day have Async Compute would be huge savings!
			//Still do need to wait on shadow maps though :/ Or accept 1 frame lag and start at very start of frame

			//Bind globals
			Profiler.BeginSample("Bind globals");
			m_vaporCompute.SetFloat("_DepthPow", DepthCurvePower);
			float near = m_camera.nearClipPlane;
			float far = m_camera.farClipPlane;
			Vector4 planeSettings = new Vector4(near, far, far - near, near * far);
			m_vaporCompute.SetVector("_PlaneSettings", planeSettings);
			Vector4 zBuffer = new Vector4(1.0f - far / near, far / near);
			m_vaporCompute.SetVector("_ZBufferParams", new Vector4(zBuffer.x, zBuffer.y, zBuffer.x / far, zBuffer.y / far));
			m_vaporCompute.SetFloat("_ExponentialWeight", AveragingSpeed);
			m_vaporCompute.SetFloat("_TemporalStrength", TemporalStrength);
			m_vaporCompute.SetVector("_CameraPos", m_camera.transform.position);
			m_vaporCompute.SetInt("_Frame", m_frameCount);
			++m_frameCount;

			var vp = GetViewProjectionMatrix();


			//Set VP from old frame for reprojection
			m_vaporCompute.SetMatrix("_VAPOR_REPROJECT", m_vpMatrixOld * vp.inverse);

			m_vaporCompute.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_vpMatrixOld = vp;
			Profiler.EndSample();


			Profiler.BeginSample("Write global density");
			BindSetting(Setting);

			//for (int i = 0; i < 3; ++i) {
				//GetNoiseLayer(i).Bind(DensityKernel, m_vaporCompute, i);
			//}

			//m_vaporCompute.SetVector("_NoiseStrength",
				//new Vector4(GetNoiseLayer(0).Strength, GetNoiseLayer(1).Strength, GetNoiseLayer(2).Strength));

			float total = NoiseWeights.x + NoiseWeights.y+ NoiseWeights.z;

			Vector4 scale = new Vector4(25.0f, 1.0f, 25.0f);

			
			m_vaporCompute.SetVector("_NoiseWeights", NoiseWeights / total);
			m_vaporCompute.SetVector("_NoiseVal", NoiseBlend);
			m_vaporCompute.SetVector("_NoiseFrequency", Vector4.Scale(NoiseFrequency, scale));
			m_vaporCompute.SetVector("_NoiseSpeed", Vector4.Scale(NoiseSpeed, scale) * 0.01f);
			m_vaporCompute.SetFloat("_NoisePower", NoisePower);

			float heightSize = Mathf.Max(0, HeightGradient.End - HeightGradient.Start);
			float distSize = Mathf.Max(0, DistanceGradient.End - DistanceGradient.Start);
			m_vaporCompute.SetVector("_GradientSettings", 
				new Vector4(1.0f / heightSize, -HeightGradient.Start / heightSize, 1.0f / distSize, -DistanceGradient.Start / distSize));
			m_vaporCompute.SetTexture(DensityKernel, "_DensityTextureWrite", DensityTex);
			m_vaporCompute.SetTexture(DensityKernel, "_GradientTexture", m_gradientTex);
			m_vaporCompute.SetTexture(DensityKernel, "_NoiseTex", NoiseTexture);
			m_vaporCompute.DispatchScaled(DensityKernel, DensityTex.width, DensityTex.height, DensityTex.volumeDepth);
			Profiler.EndSample();


		
			//First inject zones and write albedos
			for (int index = 0; index < VaporObject.All.Count; index++) {
				var vaporZone = VaporObject.All[index] as VaporZone;
				if (vaporZone != null) {
					vaporZone.Bind(this, m_vaporCompute, vp);
				}
			}

			//Lighting, multiplies directly with this albedo!
			Profiler.BeginSample("Setup lighting");
			//Setup range for clamping
			int cascadeCount = QualitySettings.shadowCascades;
			int cascX = cascadeCount > 1 ? 2 : 1;
			int cascY = cascadeCount > 2 ? 2 : 1;
			var rangeVec = new Vector4(1.0f / cascX, 1.0f / cascY);
			m_vaporCompute.SetVector("_ShadowRange", rangeVec);
			m_vaporCompute.SetFloat("_ShadowSoft", ShadowHardness);
			m_vaporCompute.SetFloat("_ShadowBias", ShadowBias * 0.1f);
			VaporLight.ShadowFilterMaterial.SetFloat("_ShadowSoft", ShadowHardness);
			Profiler.EndSample();

			Profiler.BeginSample("Vapor Light Passes");

			//TODO: Directional light should probably not do it's thing here but just work in the scattering pass
			for (int index = 0; index < VaporObject.All.Count; index++) {
				var vaporLight = VaporObject.All[index] as VaporLight;
				if (vaporLight != null) {
					vaporLight.Bind(this, m_vaporCompute, vp);
				}
			}
			Profiler.EndSample();


			Profiler.BeginSample("Scattering");

			//Read from local lights
			SetLightAccum(ScatterKernel, true);
			m_vaporCompute.SetTexture(ScatterKernel, "_DensityTexture", DensityTex);
			m_vaporCompute.SetTexture(ScatterKernel, "_ScatterTextureOld", m_scatterTexOld);
			m_vaporCompute.SetTexture(ScatterKernel, "_ScatterTexture", m_scatterTex);
			m_vaporCompute.DispatchScaled(ScatterKernel, m_scatterTex.width, m_scatterTex.height, m_scatterTex.volumeDepth);
			Profiler.EndSample();


			Profiler.BeginSample("Integration");
			m_vaporCompute.SetTexture(IntegrateKernel, "_IntegratedTexture", m_integratedTexture);
			m_vaporCompute.SetTexture(IntegrateKernel, "_ScatterTextureOld", m_scatterTex);

			m_vaporCompute.DispatchScaled(IntegrateKernel, m_scatterTex.width, m_scatterTex.height, 1);
			Profiler.EndSample();
			


			Profiler.BeginSample("Blit properties");
			m_fogMat.SetTexture("_IntegratedTexture", m_integratedTexture);
			var temp = m_scatterTex;
			m_scatterTex = m_scatterTexOld;
			m_scatterTexOld = temp;


			m_fogMat.SetVector("_PlaneSettings", planeSettings);
			m_fogMat.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_fogMat.SetFloat("_DepthPow", DepthCurvePower);


			if (m_fogFilterTexture == null || m_fogFilterTexture.width != source.width ||
			    m_fogFilterTexture.height != source.height) {
				if (m_fogFilterTexture != null) {
					DestroyImmediate(m_fogFilterTexture);
				}
				m_fogFilterTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			}
			Profiler.EndSample();

			Profiler.BeginSample("Blit to screen");
			//Apply fog to image
			Graphics.Blit(source, m_fogFilterTexture, m_fogMat, 0);

			if (BlurSize > 0.0f) {
				Profiler.BeginSample("Blur");
				RenderTexture blurTemp = RenderTexture.GetTemporary(m_fogFilterTexture.width, m_fogFilterTexture.height, 0,
					RenderTextureFormat.ARGBHalf);

				m_fogMat.SetVector("_BlurSize", new Vector4(BlurSize * 0.5f + 0, 0.0f, 0.0f, 0.0f));
				Graphics.Blit(m_fogFilterTexture, blurTemp, m_fogMat, 2);

				m_fogMat.SetVector("_BlurSize", new Vector4(0.0f, BlurSize * 0.5f + 0, 0.0f, 0.0f));
				Graphics.Blit(blurTemp, m_fogFilterTexture, m_fogMat, 2);

				RenderTexture.ReleaseTemporary(blurTemp);
				Profiler.EndSample();
			}

			m_fogMat.SetTexture("_FogTex", m_fogFilterTexture);
			Graphics.Blit(source, destination, m_fogMat, 1);
			Profiler.EndSample();
		}
		
		public void BindSetting(VaporSetting setting) {
			var albedo = setting.Albedo;
			m_vaporCompute.SetVector("_AlbedoExt",
				new Vector4(albedo.r, albedo.g, albedo.b, setting.Extinction));
			m_vaporCompute.SetFloat("_Extinction", setting.Extinction);
			var emissive = setting.Emissive * 0.2f;


			var ambientEmissive = setting.AmbientLight * setting.AmbientLight.a;
			m_vaporCompute.SetVector("_EmissivePhase", new Vector4(emissive.r + ambientEmissive.r, emissive.g + ambientEmissive.g, emissive.b + ambientEmissive.b, Phase));
		}

		private Matrix4x4 GetViewProjectionMatrix() {
			Matrix4x4 v = m_camera.worldToCameraMatrix;
			Matrix4x4 p = m_camera.projectionMatrix;
			p = GL.GetGPUProjectionMatrix(p, false);

			Matrix4x4 vp = p * v;
			return vp;
		}
	}
}