using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vapor {
	[ExecuteInEditMode]
//#if UNITY_5_4_OR_NEWER
	//[ImageEffectAllowedInSceneView]
//#endif
	public class Vapor : MonoBehaviour {

		public static List<Vapor> ActiveVapors = new List<Vapor>();

		private List<VaporLight> m_lights = new List<VaporLight>();

		private CullingGroup m_cullGroup;
		private BoundingSphere[] m_spheres = new BoundingSphere[1024];

		//Static resources
		//public Material ShadowBlurMaterial;
		public static Material ScreenShadowMaterial;
		public static Material ShadowFilterMaterial;
		public static Mesh QuadMesh;

		private Camera m_camera;


		[Header("Global settings")] public Color Albedo = new Color(0.1f, 0.1f, 0.1f); //sig_s / sig_t
		public float Extinction = 0.15f; //sig_t

		[Range(0.0f, 1.0f)] public float TemporalStrength = 1.0f;
		[Range(-1.0f, 1.0f)] public float Phase;

		public Color Emissive = Color.black;
		public Color AmbientLight = Color.black;

		public float BlurSize;

		public float AveragingSpeed = 0.1f;
		public float ShadowHardness = 70.0f;

		[Range(0.0f, 1.0f)] public float ShadowBias = 0.05f;

		//TODO: Per light
		[HideInInspector] public Texture2D SpotCookie;


		//TODO: Noise layer parent
		[SerializeField] private NoiseLayer m_baseLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_secondaryLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_detailLayer = new NoiseLayer();


		//These must be multiples of 4!
		private const int c_horizontalTextureRes = 240;
		private const int c_verticalTextureRes = 136; //240 * 9/16 == 135 -> 136 - rounded to 4
		private const int c_volumeDepth = 128;

		private ComputeShader m_vaporCompute;

		private RenderTexture m_densityTex;
		private RenderTexture m_densityTexOld;


		private RenderTexture m_scatterTex;
		private RenderTexture m_lightTex;




		private int m_scatterKernel;
		private int m_densityKernel;

		//private int m_lightDirKernel;
		//private int m_lightSpotKernel;
		private int m_lightPointKernel;
		private int m_lightClearKernel;


		private Material m_fogMat;

		//TODO: Use injection passes
		//Point light data
		private const int c_defaultPointCount = 8;
		private ComputeBuffer m_pointLightBuffer;
		private VaporPointLight[] m_pointLightDataBuffer = new VaporPointLight[c_defaultPointCount];
		//Spot light data
		private const int c_defualtSpotCount = 8;
		private ComputeBuffer m_spotLightBuffer;
		private VaporSpotLight[] m_spotLightDataBuffer = new VaporSpotLight[c_defualtSpotCount];

		//TAA
		private Matrix4x4 m_vpMatrixOld;
		private int m_frameCount;

		private RenderTexture m_fogFilterTexture;

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

		private void OnEnable() {
			ActiveVapors.Add(this);
			m_camera = GetComponent<Camera>();
			CreateResources();
		}


		private void OnDisable() {
			DestroyImmediate(m_densityTex);
			DestroyImmediate(m_scatterTex);

			if (m_pointLightBuffer != null) {
				m_pointLightBuffer.Dispose();
			}

			if (m_spotLightBuffer != null) {
				m_spotLightBuffer.Dispose();
			}

			//Destroy all noises
			m_baseLayer.Destroy();
			m_secondaryLayer.Destroy();
			m_detailLayer.Destroy();

			m_cullGroup.Dispose();
			m_cullGroup = null;

			ActiveVapors.Remove(this);
		}


		private void CreateResources() {
			//ShadowBlurMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowBlur"));
			ScreenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));
			ShadowFilterMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowFilterESM"));

			//TODO: Can we just get the friggin quad 
			var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
			QuadMesh = go.GetComponent<MeshFilter>().sharedMesh;
			DestroyImmediate(go);

			m_cullGroup = new CullingGroup();
			m_cullGroup.SetBoundingSpheres(m_spheres);
			m_cullGroup.SetBoundingSphereCount(m_lights.Count);

			//Break dependance on Resources? Could cause stalls for people grmbl
			m_vaporCompute = Resources.Load<ComputeShader>("VaporSim");
			m_scatterKernel = m_vaporCompute.FindKernel("Scatter");
			m_densityKernel = m_vaporCompute.FindKernel("FogDensity");
			m_lightPointKernel = m_vaporCompute.FindKernel("LightPoint");
			m_lightClearKernel = m_vaporCompute.FindKernel("LightClear");

			//m_lightDirKernel = m_vaporCompute.FindKernel("LightDirectional");
			//m_lightSpotKernel = m_vaporCompute.FindKernel("LightSpot");

			m_fogMat = new Material(Shader.Find("Hidden/VaporPost"));

			CreateComputeBuffers();

			CreateTexture(ref m_scatterTex);
			CreateTexture(ref m_densityTex);
			CreateTexture(ref m_densityTexOld);
			CreateTexture(ref m_lightTex, RenderTextureFormat.RHalf, 3);


			if (m_baseLayer.NeedsBuild() || m_secondaryLayer.NeedsBuild() || m_detailLayer.NeedsBuild()) {
				BakeNoiseLayers();
			}

		}

		private void CreateComputeBuffers() {
			if (m_pointLightBuffer != null) {
				m_pointLightBuffer.Dispose();
			}

			if (m_spotLightBuffer != null) {
				m_spotLightBuffer.Dispose();
			}

			m_pointLightBuffer = new ComputeBuffer(m_pointLightDataBuffer.Length, VaporPointLight.Stride);
			m_spotLightBuffer = new ComputeBuffer(m_spotLightDataBuffer.Length, VaporSpotLight.Stride);
		}

		public void BakeNoiseLayers() {
			m_baseLayer.Bake();
			m_secondaryLayer.Bake();
			m_detailLayer.Bake();
		}

		private void CreateTexture(ref RenderTexture tex, RenderTextureFormat format = RenderTextureFormat.ARGBHalf, int widthMult = 1) {
			if (tex != null) {
				return;
			}

			if (tex != null) {
				DestroyImmediate(tex);
			}

			tex = new RenderTexture(c_horizontalTextureRes * widthMult, c_verticalTextureRes, 0, format) {
				volumeDepth = c_volumeDepth,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear
			};

			tex.Create();
		}


		void Update() {
			for (int i = 0; i < m_lights.Count; i++) {
				var vaporLight = m_lights[i];
				m_spheres[i].position = vaporLight.transform.position;
				m_spheres[i].radius = vaporLight.Light.range;
			}
		}

		void OnPreRender() {
			for (int i = 0; i < m_lights.Count; i++) {
				var vaporLight = m_lights[i];
				
				if (!vaporLight.HasShadow || vaporLight.LightType != LightType.Directional) {
					continue;
				}

				vaporLight.CreateShadowResources();
				Graphics.SetRandomWriteTarget(1, vaporLight.MatrixBuffer);
				Graphics.SetRandomWriteTarget(2, vaporLight.LightSplitsBuffer);
			}
		}


		private void UpdateLightBind() {
			//render lights into light texture
			m_vaporCompute.SetTexture(m_lightClearKernel, "_LightAccum", m_lightTex);
			m_vaporCompute.Dispatch(m_lightClearKernel, c_horizontalTextureRes / 4 * 3, c_verticalTextureRes / 4, c_volumeDepth / 4);

			//Bind each light
			for (int index = 0; index < m_lights.Count; index++) {
				var vaporLight = m_lights[index];
				var l = vaporLight.Light;

				//Main fog light, special handling
				if (vaporLight.LightType == LightType.Directional) {
					continue;
				}
				Vector4 posRange = l.transform.position;
				posRange.w = 1.0f / (l.range * l.range);

				m_vaporCompute.SetVector("_LightPosRange", posRange);
				m_vaporCompute.SetVector("_LightColor", l.color * l.intensity * vaporLight.FogScatterIntensity);



				switch (vaporLight.LightType) {
					case LightType.Point:
						m_vaporCompute.SetTexture(m_lightPointKernel, "_LightAccum", m_lightTex);
						m_vaporCompute.Dispatch(m_lightPointKernel, c_horizontalTextureRes / 4, c_verticalTextureRes / 4, c_volumeDepth / 4);
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
							m_vaporCompute.SetTexture(m_densityKernel, "_SpotShadow", vaporLight.ShadowMap);
						}

						var lightProjMatrix = Matrix4x4.identity;
						float d = Mathf.Deg2Rad * l.spotAngle * 0.5f;
						d = Mathf.Cos(d) / Mathf.Sin(d);
						lightProjMatrix[3, 2] = 2f / d;
						lightProjMatrix[3, 3] = 0.1f;
						var mat = lightProjMatrix * l.transform.worldToLocalMatrix;
						m_vaporCompute.SetMatrix("_SpotMatrix", mat);

						//TODO: Per light
						if (l.cookie != null) {
							m_vaporCompute.SetTexture(m_densityKernel, "_SpotCookie", l.cookie);
						}
						else {
							m_vaporCompute.SetTexture(m_densityKernel, "_SpotCookie", SpotCookie);
						}
						break;
				}
			}


			//Finally bind directional light for main pass (if it's there)
			//TODO: Empty bind if there's no main light
			//TODO: Use a light pass for this too? Not sure if that's better or worse perf
			for (int index = 0; index < m_lights.Count; index++) {
				var vaporLight = m_lights[index];
				var l = vaporLight.Light;

				if (vaporLight.LightType != LightType.Directional) {
					continue;
				}

				m_vaporCompute.SetVector("_LightPosRange", l.transform.forward);
				m_vaporCompute.SetVector("_LightColor", l.color * l.intensity * vaporLight.FogScatterIntensity);

				if (vaporLight.HasShadow) {
					m_vaporCompute.SetBuffer(m_densityKernel, "_MatrixBuf", vaporLight.MatrixBuffer);
					m_vaporCompute.SetBuffer(m_densityKernel, "_LightSplits", vaporLight.LightSplitsBuffer);
					m_vaporCompute.SetTexture(m_densityKernel, "_ShadowMapTexture", vaporLight.ShadowMap);
				}
				else {
					m_vaporCompute.SetTexture(m_densityKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
				}
			}

			m_vaporCompute.SetTexture(m_densityKernel, "_LightAccum", m_lightTex);
		}

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			Graphics.ClearRandomWriteTargets();

			m_baseLayer.Bind(m_densityKernel, m_vaporCompute, 0);
			m_secondaryLayer.Bind(m_densityKernel, m_vaporCompute, 1);
			m_detailLayer.Bind(m_densityKernel, m_vaporCompute, 2);

			m_vaporCompute.SetVector("_NoiseStrength", new Vector4(m_baseLayer.Strength, m_secondaryLayer.Strength, m_detailLayer.Strength));
			m_vaporCompute.SetTexture(m_densityKernel, "_DensityTextureWrite", m_densityTex);
			m_vaporCompute.SetTexture(m_densityKernel, "_DensityTextureOld", m_densityTexOld);
			m_vaporCompute.SetTexture(m_scatterKernel, "_DensityTexture", m_densityTex);

			m_vaporCompute.SetTexture(m_scatterKernel, "_ScatterTexture", m_scatterTex);


			float near = m_camera.nearClipPlane;
			float far = m_camera.farClipPlane;
			Vector4 planeSettings = new Vector4(near, far - near, (far + near) / (2 * (far - near)) + 0.5f,
				(-far * near) / (far - near));
			m_vaporCompute.SetVector("_PlaneSettings", planeSettings);

			Vector4 zBuffer = new Vector4(1.0f - far / near, far / near);
			m_vaporCompute.SetVector("_ZBufferParams", new Vector4(zBuffer.x, zBuffer.y, zBuffer.x / far, zBuffer.y / far));


			float delt = Application.isPlaying ? Time.deltaTime : Time.fixedDeltaTime;

			m_vaporCompute.SetFloat("_ExponentialWeight", Mathf.Pow(AveragingSpeed, 1.0f / (60.0f * delt)));
			m_vaporCompute.SetFloat("_TemporalStrength", TemporalStrength);

			m_vaporCompute.SetVector("_AlbedoExt", new Vector4(Albedo.r * 0.1f, Albedo.g * 0.1f, Albedo.b * 0.1f, Extinction));
			m_vaporCompute.SetFloat("_Extinction", Extinction);

			m_vaporCompute.SetVector("_EmissivePhase",
				new Vector4(Emissive.r * 0.2f, Emissive.g * 0.2f, Emissive.b * 0.2f, Phase));
			m_vaporCompute.SetVector("_AmbientLight", AmbientLight * AmbientLight.a);

			m_vaporCompute.SetInt("_Frame", m_frameCount);
			m_vaporCompute.SetVector("_CameraPos", m_camera.transform.position);


			//Setup range for clamping
			int cascadeCount = QualitySettings.shadowCascades;
			int cascX = cascadeCount > 1 ? 2 : 1;
			int cascY = cascadeCount > 2 ? 2 : 1;
			var rangeVec = new Vector4(1.0f / cascX, 1.0f / cascY);

			m_vaporCompute.SetVector("_Range", rangeVec);

			Matrix4x4 v = m_camera.worldToCameraMatrix;
			Matrix4x4 p = m_camera.projectionMatrix;
			p = GL.GetGPUProjectionMatrix(p, false);

			Matrix4x4 vp = p * v;

			//Set VP from old frame for reprojection
			m_vaporCompute.SetMatrix("_VAPOR_VP_OLD", m_vpMatrixOld);
			m_vaporCompute.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_vpMatrixOld = vp;


			//Globals
			m_vaporCompute.SetFloat("_ShadowSoft", ShadowHardness);
			m_vaporCompute.SetFloat("_ShadowBias", ShadowBias * 0.1f);
			ShadowFilterMaterial.SetFloat("_ShadowSoft", ShadowHardness);


			UpdateLightBind();

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

			//Apply fog to image
			Graphics.Blit(source, m_fogFilterTexture, m_fogMat, 0);
			RenderTexture blurTemp = RenderTexture.GetTemporary(m_fogFilterTexture.width, m_fogFilterTexture.height, 0,
				RenderTextureFormat.ARGBHalf);

			m_fogMat.SetVector("_BlurSize", new Vector4(BlurSize * 0.5f + 0, 0.0f, 0.0f, 0.0f));
			Graphics.Blit(m_fogFilterTexture, blurTemp, m_fogMat, 2);

			m_fogMat.SetVector("_BlurSize", new Vector4(0.0f, BlurSize * 0.5f + 0, 0.0f, 0.0f));
			Graphics.Blit(blurTemp, m_fogFilterTexture, m_fogMat, 2);

			RenderTexture.ReleaseTemporary(blurTemp);

			//Gaussian
			m_fogMat.SetTexture("_FogTex", m_fogFilterTexture);
			Graphics.Blit(source, destination, m_fogMat, 1);
		}

		public void Register(VaporLight vaporLight) {
			m_lights.Add(vaporLight);

			if (m_cullGroup != null) {
				m_cullGroup.SetBoundingSphereCount(m_lights.Count);
			}
		}

		public void Deregister(VaporLight vaporLight) {
			int index = m_lights.IndexOf(vaporLight);

			if (index >= 0) {
				m_lights[index] = m_lights[m_lights.Count - 1];
				m_lights.RemoveAt(m_lights.Count - 1);

				if (m_cullGroup != null) {
					m_cullGroup.SetBoundingSphereCount(m_lights.Count);
				}
			}
		}
	}
}