using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vapor {
	[ExecuteInEditMode]
#if UNITY_5_4_OR_NEWER
	//[ImageEffectAllowedInSceneView]
#endif
	public class Vapor : MonoBehaviour {

		public static List<Vapor> All = new List<Vapor>();

		[SerializeField]
		private VaporSetting m_setting;
		public VaporSetting Setting {
			get {
				if (m_setting == null) {
					m_setting = Resources.Load<VaporSetting>("DefaultVaporSetting");
				}
				return m_setting;
			}
		}

		//TODO: Noise layer parent
		[SerializeField]
		private NoiseLayer m_baseLayer = new NoiseLayer();
		[SerializeField]
		private NoiseLayer m_secondaryLayer = new NoiseLayer();
		[SerializeField]
		private NoiseLayer m_detailLayer = new NoiseLayer();

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
		}

		private CullingGroup m_cullGroup;
		private BoundingSphere[] m_spheres = new BoundingSphere[64];
		private Camera m_camera;

		public VaporGradient HeightGradient = new VaporGradient();
		public VaporGradient DistanceGradient = new VaporGradient();

		[Range(0.0f, 1.0f)] public float TemporalStrength = 1.0f;
		[Range(-1.0f, 1.0f)] public float Phase;

		public float BlurSize;
		public float AveragingSpeed = 0.1f;
		public float ShadowHardness = 70.0f;
		[Range(0.0f, 1.0f)] public float ShadowBias = 0.05f;

		[HideInInspector] public Texture2D SpotCookie;

		//These must be multiples of 4!
		private const int c_horizontalTextureRes = 240;
		private const int c_verticalTextureRes = 136; //240 * 9/16 == 135 -> 136 - rounded to 4
		private const int c_volumeDepth = 128;

		const float c_depthPow = 6;

		private ComputeShader m_vaporCompute;

		private RenderTexture m_densityTex;
		private RenderTexture m_densityTexOld;

		private RenderTexture m_scatterTex;
		private RenderTexture m_lightTex;

		private int m_scatterKernel;
		private int m_densityKernel;

		private int m_lightDirKernel;
		private int m_lightSpotKernel;
		private int m_lightPointKernel;

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
			CreateResources();

			All.Add(this);
		}

		public void BakeNoiseLayers() {
			for (int i = 0; i < 3; ++i) {
				GetNoiseLayer(i).Bake();
			}
		}


		private void OnDisable() {
			All.Remove(this);


			DestroyImmediate(m_densityTex);
			DestroyImmediate(m_scatterTex);
			DestroyImmediate(m_gradientTex);

			for (int i = 0; i < 3; ++i) {
				GetNoiseLayer(i).DestroyTex();
			}

			if (m_cullGroup != null) {
				m_cullGroup.Dispose();
			}
		}

		public void UpdateGradients() {
			const int res = 128;

			if (m_gradientTex == null) {
				m_gradientTex = new Texture2D(res, res, TextureFormat.ARGB32, false);
				m_gradientTex.wrapMode = TextureWrapMode.Clamp;
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

		private void CreateResources() {
			m_cullGroup = new CullingGroup();
			m_cullGroup.SetBoundingSpheres(m_spheres);

			//Break dependance on Resources? Could cause stalls for people grmbl
			m_vaporCompute = Resources.Load<ComputeShader>("VaporSim");
			m_scatterKernel = m_vaporCompute.FindKernel("Scatter");
			m_densityKernel = m_vaporCompute.FindKernel("FogDensity");
			m_lightPointKernel = m_vaporCompute.FindKernel("LightPoint");
			m_lightSpotKernel = m_vaporCompute.FindKernel("LightSpot");

			m_lightDirKernel = m_vaporCompute.FindKernel("LightDirectional");

			m_fogMat = new Material(Shader.Find("Hidden/VaporPost"));

			CreateTexture(ref m_scatterTex);
			CreateTexture(ref m_densityTex);
			CreateTexture(ref m_densityTexOld);
			CreateTexture(ref m_lightTex, RenderTextureFormat.RHalf, 3);

			UpdateGradients();

			for (int i = 0; i < 3; ++i) {
				if (GetNoiseLayer(i).NeedsBuild()) {
					BakeNoiseLayers();
					break;
				}
			}
		}

		private void CreateTexture(ref RenderTexture tex, RenderTextureFormat format = RenderTextureFormat.ARGBHalf,
			int sizeMult = 1) {
			if (tex != null) {
				return;
			}

			if (tex != null) {
				DestroyImmediate(tex);
			}

			tex = new RenderTexture(c_horizontalTextureRes, c_verticalTextureRes, 0, format) {
				volumeDepth = c_volumeDepth * sizeMult,
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

		float Linear01Depth(float z) {
			//x is (1-far/near), y is (far/near)
			float fn = m_camera.farClipPlane / m_camera.nearClipPlane;
			return 1.0f / ((1 - fn) * z + fn);
		}

		Vector3 GetUvFromWorld(Vector3 world, Matrix4x4 viewProj) {
			Vector3 frustum = viewProj.MultiplyPoint(world);


			frustum.x = Mathf.Clamp01((frustum.x + 1.0f) * 0.5f);
			frustum.y = Mathf.Clamp01((frustum.y + 1.0f) * 0.5f);

			frustum.z = Mathf.Clamp01(frustum.z);

			frustum.z = Linear01Depth(frustum.z);
			frustum.z = Mathf.Pow(frustum.z, 1.0f / c_depthPow);

			return frustum;
		}


		private void InjectObjects(Matrix4x4 viewProj) {
			//Bind each light
			for (int index = 0; index < VaporObject.All.Count; index++) {
				var vaporLight = VaporObject.All[index] as VaporLight;

				if (vaporLight == null) {
					//TODO: Inject zones
					continue;
				}

				var l = vaporLight.Light;

				//Main fog light, special handling
				Vector4 posRange = l.transform.position;
				posRange.w = 1.0f / (l.range * l.range);
				m_vaporCompute.SetVector("_LightPosRange", posRange);
				m_vaporCompute.SetVector("_LightColor", l.color * l.intensity * vaporLight.FogScatterIntensity);

				switch (vaporLight.LightType) {
					case LightType.Directional:
						m_vaporCompute.SetVector("_LightPosRange", l.transform.forward);

						if (vaporLight.HasShadow) {
							m_vaporCompute.SetBuffer(m_lightDirKernel, "_MatrixBuf", vaporLight.MatrixBuffer);
							m_vaporCompute.SetBuffer(m_lightDirKernel, "_LightSplits", vaporLight.LightSplitsBuffer);
							m_vaporCompute.SetTexture(m_lightDirKernel, "_ShadowMapTexture", vaporLight.ShadowMap);
						} else {
							m_vaporCompute.SetTexture(m_lightDirKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
						}

						m_vaporCompute.SetTexture(m_lightDirKernel, "_LightAccum", m_lightTex);
						Profiler.BeginSample("Dir Light pass");
						m_vaporCompute.Dispatch(m_lightDirKernel, c_horizontalTextureRes / 4, c_verticalTextureRes / 4, c_volumeDepth / 4);
						Profiler.EndSample();

						break;


					case LightType.Point:
						InjectLight(viewProj, m_lightPointKernel, vaporLight, l);
						break;

					case LightType.Spot:
						if (vaporLight.HasShadow) {
							Matrix4x4 v = vaporLight.transform.worldToLocalMatrix;
							Matrix4x4 p =
								GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(vaporLight.Light.spotAngle, 1.0f,
									vaporLight.Light.shadowNearPlane,
									vaporLight.Light.range), true);

							//For some reason z is flipped :(
							p *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

							m_vaporCompute.SetMatrix("_SpotShadowMatrix", p * v);
							m_vaporCompute.SetTexture(m_lightSpotKernel, "_SpotShadow", vaporLight.ShadowMap);
						}

						var lightProjMatrix = Matrix4x4.identity;
						float d = Mathf.Deg2Rad * l.spotAngle * 0.5f;
						d = Mathf.Cos(d) / Mathf.Sin(d);
						lightProjMatrix[3, 2] = 2f / d;
						lightProjMatrix[3, 3] = 0.15f;
						var mat = lightProjMatrix * l.transform.worldToLocalMatrix;
						m_vaporCompute.SetMatrix("_SpotMatrix", mat);
						if (l.cookie != null) {
							m_vaporCompute.SetTexture(m_lightSpotKernel, "_SpotCookie", l.cookie);
						}
						else {
							m_vaporCompute.SetTexture(m_lightSpotKernel, "_SpotCookie", SpotCookie);
						}
						InjectLight(viewProj, m_lightPointKernel, vaporLight, l);


						break;
				}


			}

			m_vaporCompute.SetTexture(m_densityKernel, "_LightAccum", m_lightTex);
		}

		private void InjectLight(Matrix4x4 viewProj, int kernel, VaporLight vaporLight, Light l) {
			if (kernel != -1) {
				//TODO: Spot could have tighter bounds than point light
				Vector3 dir = transform.forward;

				float dist = Vector3.Dot(dir, vaporLight.transform.position - transform.position);
				dist -= m_camera.nearClipPlane;

				Vector3 near = vaporLight.transform.position - dir * Mathf.Min(dist, l.range);
				Vector3 far = vaporLight.transform.position + dir * Mathf.Min(dist, l.range);


				Vector3 lower = near - transform.right * l.range - transform.up * l.range;
				Vector3 upper = near + transform.right * l.range + transform.up * l.range;

				Vector3 lowerV = m_camera.WorldToViewportPoint(lower);
				Vector3 upperV = m_camera.WorldToViewportPoint(upper);

				float xWidth = upperV.x - lowerV.x;
				float yWidth = upperV.y - lowerV.y;


				Vector3 startUv = GetUvFromWorld(lower, viewProj);

				m_offset[0] = Mathf.FloorToInt(startUv.x * c_horizontalTextureRes);
				m_offset[1] = Mathf.FloorToInt(startUv.y * c_verticalTextureRes);
				m_offset[2] = Mathf.FloorToInt(startUv.z * c_volumeDepth);

				m_vaporCompute.SetInts("_LightWriteLower", m_offset);


				float zUvNear = Mathf.Pow(Mathf.Max(0.0f, transform.InverseTransformPoint(near).z) / m_camera.farClipPlane, 1.0f / c_depthPow);
				float zUvFar = Mathf.Pow(Mathf.Max(0.0f, transform.InverseTransformPoint(far).z) / m_camera.farClipPlane,
					1.0f / c_depthPow);

				float zWidth = zUvFar - zUvNear;


				int xCount = Mathf.CeilToInt(Mathf.Clamp01(xWidth) * c_horizontalTextureRes / 4.0f);
				int yCount = Mathf.CeilToInt(Mathf.Clamp01(yWidth) * c_verticalTextureRes / 4.0f);
				int zCount = Mathf.CeilToInt(Mathf.Clamp01(zWidth) * c_volumeDepth / 4.0f);

				m_vaporCompute.SetTexture(kernel, "_LightAccum", m_lightTex);

				Profiler.BeginSample("Light pass");
				m_vaporCompute.Dispatch(kernel, xCount, yCount, zCount);
				Profiler.EndSample();
			}
		}

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			Graphics.ClearRandomWriteTargets();


			//TODO: Ideally do the compute part earlier in the frame. If we one day have Async Compute would be huge savings!
			//Still do need to wait on shadow maps though :/ Or accept 1 frame lag and start at very start of frame
			for (int i = 0; i < 3; ++i) {
				GetNoiseLayer(i).Bind(m_densityKernel, m_vaporCompute, i);
			}

			m_vaporCompute.SetVector("_NoiseStrength",
				new Vector4(GetNoiseLayer(0).Strength, GetNoiseLayer(1).Strength, GetNoiseLayer(2).Strength));
			m_vaporCompute.SetTexture(m_densityKernel, "_DensityTextureWrite", m_densityTex);
			m_vaporCompute.SetTexture(m_densityKernel, "_DensityTextureOld", m_densityTexOld);
			m_vaporCompute.SetTexture(m_scatterKernel, "_DensityTexture", m_densityTex);
			m_vaporCompute.SetTexture(m_densityKernel, "_GradientTexture", m_gradientTex);

			float heightSize = Mathf.Max(0, HeightGradient.End - HeightGradient.Start);
			float distSize = Mathf.Max(0, DistanceGradient.End - DistanceGradient.Start);

			m_vaporCompute.SetVector("_GradientSettings", new Vector4(1.0f / heightSize, -HeightGradient.Start / heightSize,
																		1.0f / distSize, -DistanceGradient.Start / distSize));

			m_vaporCompute.SetTexture(m_scatterKernel, "_ScatterTexture", m_scatterTex);

			float near = m_camera.nearClipPlane;
			float far = m_camera.farClipPlane;
			Vector4 planeSettings = new Vector4(near, far - near, (far + near) / (2 * (far - near)) + 0.5f,
				(-far * near) / (far - near));
			m_vaporCompute.SetVector("_PlaneSettings", planeSettings);

			Vector4 zBuffer = new Vector4(1.0f - far / near, far / near);
			m_vaporCompute.SetVector("_ZBufferParams", new Vector4(zBuffer.x, zBuffer.y, zBuffer.x / far, zBuffer.y / far));


			m_vaporCompute.SetFloat("_ExponentialWeight", AveragingSpeed);
			m_vaporCompute.SetFloat("_TemporalStrength", TemporalStrength);

			var albedo = Setting.Albedo;

			m_vaporCompute.SetVector("_AlbedoExt",
				new Vector4(albedo.r * 0.1f, albedo.g * 0.1f, albedo.b * 0.1f, Setting.Extinction));
			m_vaporCompute.SetFloat("_Extinction", Setting.Extinction);

			var emissive = Setting.Emissive;
			m_vaporCompute.SetVector("_EmissivePhase",
				new Vector4(emissive.r * 0.2f, emissive.g * 0.2f, emissive.b * 0.2f, Phase));
			m_vaporCompute.SetVector("_AmbientLight", Setting.AmbientLight * Setting.AmbientLight.a);

			m_vaporCompute.SetInt("_Frame", m_frameCount);
			m_vaporCompute.SetVector("_CameraPos", m_camera.transform.position);


			//Setup range for clamping
			int cascadeCount = QualitySettings.shadowCascades;
			int cascX = cascadeCount > 1 ? 2 : 1;
			int cascY = cascadeCount > 2 ? 2 : 1;
			var rangeVec = new Vector4(1.0f / cascX, 1.0f / cascY);

			m_vaporCompute.SetVector("_Range", rangeVec);

			var vp = GetViewProjectionMatrix();

			//Set VP from old frame for reprojection
			m_vaporCompute.SetMatrix("_VAPOR_VP_OLD", m_vpMatrixOld);
			m_vaporCompute.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_vpMatrixOld = vp;

			//Globals
			m_vaporCompute.SetFloat("_ShadowSoft", ShadowHardness);
			m_vaporCompute.SetFloat("_ShadowBias", ShadowBias * 0.1f);
			VaporLight.ShadowFilterMaterial.SetFloat("_ShadowSoft", ShadowHardness);

			InjectObjects(vp);

			Profiler.BeginSample("Density");
			m_vaporCompute.Dispatch(m_densityKernel, m_densityTex.width / 4, m_densityTex.height / 4,
				m_densityTex.volumeDepth / 4);
			Profiler.EndSample();

			Profiler.BeginSample("Scattering");
			m_vaporCompute.Dispatch(m_scatterKernel, m_densityTex.width / 8, m_densityTex.height / 8, 1);
			Profiler.EndSample();

			m_fogMat.SetTexture("_ScatterTex", m_scatterTex);

			++m_frameCount;

			var temp = m_densityTex;
			m_densityTex = m_densityTexOld;
			m_densityTexOld = temp;

			if (m_fogFilterTexture == null || m_fogFilterTexture.width != source.width ||
			    m_fogFilterTexture.height != source.height) {
				if (m_fogFilterTexture != null) {
					DestroyImmediate(m_fogFilterTexture);
				}
				m_fogFilterTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
			}


			Profiler.BeginSample("Blit to screen");
			//Apply fog to image
			Graphics.Blit(source, m_fogFilterTexture, m_fogMat, 0);

			if (BlurSize > 0.0f) {
				RenderTexture blurTemp = RenderTexture.GetTemporary(m_fogFilterTexture.width, m_fogFilterTexture.height, 0, RenderTextureFormat.ARGBHalf);

				m_fogMat.SetVector("_BlurSize", new Vector4(BlurSize * 0.5f + 0, 0.0f, 0.0f, 0.0f));
				Graphics.Blit(m_fogFilterTexture, blurTemp, m_fogMat, 2);

				m_fogMat.SetVector("_BlurSize", new Vector4(0.0f, BlurSize * 0.5f + 0, 0.0f, 0.0f));
				Graphics.Blit(blurTemp, m_fogFilterTexture, m_fogMat, 2);

				RenderTexture.ReleaseTemporary(blurTemp);
			}

			//Gaussian
			m_fogMat.SetTexture("_FogTex", m_fogFilterTexture);
			Graphics.Blit(source, destination, m_fogMat, 1);
			Profiler.EndSample();
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