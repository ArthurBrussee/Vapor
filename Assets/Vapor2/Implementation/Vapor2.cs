using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Vapor {
	public struct VaporLightData {
		public Vector4 PosRange;
		public Vector4 Intenisty;
		public const int Stride = 32;
	}

	[ExecuteInEditMode]
	public class Vapor2 : MonoBehaviour {
#if UNITY_EDITOR
		[NonSerialized] public bool NeedsRebake;
#endif

		[Header("Global settings")]
		public float FogDensity = 0.1f;
		public float InscatterIntensity = 0.1f;
		
		[Range(-1.0f, 1.0f)] public float Anisotropy;

		public Color AmbientLight = Color.white;
		public float AmbientIntensity = 0.1f;

		public Light Sun;
        public RenderTexture ShadowMap;
        

        [SerializeField] private NoiseLayer m_baseLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_secondaryLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_detailLayer = new NoiseLayer();
		[SerializeField] private ComputeShader m_scatteringShader;

		private const int c_volumeDepth = 128;
		private const int c_horizontalTextureRes = 240;
		private const int c_verticalTextureRes = 136;

		//private const int c_volumeDepth = 64;
		//private const int c_horizontalTextureRes = 160;
		//private const int c_verticalTextureRes = 88;



		private RenderTexture m_densityTex;
		private RenderTexture m_densityTexOld;
		private RenderTexture m_scatterTex;

		private int m_scatterKernel;
		private int m_densityKernel;

		private Camera m_camera;
		private Material m_fogMat;

		//Point light data
		private const int c_maxLightCount = 8;
		private ComputeBuffer m_lightComputeBuffer;
		private VaporLightData[] m_lightDataBuffer = new VaporLightData[c_maxLightCount];
		//Shadowing data
		private CommandBuffer m_cmdAfterShadow;
		private CommandBuffer m_cmdAfterScreenMask;

		private Material m_screenShadowMaterial;
		private Material m_shadowFilterMaterial;
		private Matrix4x4 m_vpMatrixOld;

		//Matrix info
		private RenderTexture m_shadowMatrixTexture;
		private Texture2D m_matrixTextureRead;



		private const float c_denseMult = 0.0005f;

		private static string[] s_matrixNames = {
													"unity_World2Shadow0", "unity_World2Shadow1", "unity_World2Shadow2",
													"unity_World2Shadow3"
												};

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
#if UNITY_EDITOR
			if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) {
				return;
			}
#endif

			CreateResources();
		}


		//TODO: for 4K maps we're missing a dowwn res - for super low res this is too much :(
		private int GetShadowMapResolution() {
			return 1024;
		}

		private void CreateResources() {
			//Break dependance on Resources? Could cause stalls for people grmbl
			m_scatteringShader = Resources.Load<ComputeShader>("VaporSim");
			m_scatterKernel = m_scatteringShader.FindKernel("Scatter");
			m_densityKernel = m_scatteringShader.FindKernel("FogDensity");

			m_screenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));
			m_shadowFilterMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowFilterESM"));
			m_shadowMatrixTexture = new RenderTexture(4, 5, 0, RenderTextureFormat.ARGBFloat);

			//TODO: Report a bug on unity - this fails for whatever reason, though it works fine
			m_matrixTextureRead =
				new Texture2D(m_shadowMatrixTexture.width, m_shadowMatrixTexture.height, TextureFormat.RGBAFloat, false);

			int res = GetShadowMapResolution();
			ShadowMap = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat);
			m_fogMat = new Material(Shader.Find("Hidden/VaporPost2"));

			m_lightComputeBuffer = new ComputeBuffer(c_maxLightCount, VaporLightData.Stride);

			CreateTexture(ref m_scatterTex);
			CreateTexture(ref m_densityTex);
			CreateTexture(ref m_densityTexOld);

			if (m_baseLayer.NeedsBuild() || m_secondaryLayer.NeedsBuild() || m_detailLayer.NeedsBuild()) {
				BakeNoiseLayers();
			}

			NeedsRebake = false;
			m_camera = GetComponent<Camera>();
		}

		public void BakeNoiseLayers() {
			m_baseLayer.Bake();
			m_secondaryLayer.Bake();
			m_detailLayer.Bake();
		}

		private void CreateTexture(ref RenderTexture tex) {
			if (tex != null) {
				return;
			}

			if (tex != null) {
				DestroyImmediate(tex);
			}

			tex = new RenderTexture(c_horizontalTextureRes, c_verticalTextureRes, 0, RenderTextureFormat.ARGBHalf);
			tex.volumeDepth = c_volumeDepth;
			tex.isVolume = true;
			tex.enableRandomWrite = true;
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;

			tex.Create();

			RenderTexture.active = null;
		}

		private void BindCompute() {
			m_baseLayer.Bind(m_densityKernel, m_scatteringShader, 0);
			m_secondaryLayer.Bind(m_densityKernel, m_scatteringShader, 1);
			m_detailLayer.Bind(m_densityKernel, m_scatteringShader, 2);
			m_scatteringShader.SetVector("_NoiseStrength",
				new Vector4(m_baseLayer.Strength, m_secondaryLayer.Strength, m_detailLayer.Strength));



			m_scatteringShader.SetTexture(m_densityKernel, "_DensityTextureWrite", m_densityTex);
			m_scatteringShader.SetTexture(m_densityKernel, "_DensityTextureOld", m_densityTexOld);
			m_scatteringShader.SetTexture(m_scatterKernel, "_DensityTexture", m_densityTex);
			m_scatteringShader.SetTexture(m_scatterKernel, "_ScatterTexture", m_scatterTex);

			float near = m_camera.nearClipPlane;
			float far = m_camera.farClipPlane;
			Vector4 planeSettings = new Vector4(near, far, (far + near) / (2 * (far - near)) + 0.5f, (-far * near) / (far - near));
			m_scatteringShader.SetVector("_PlaneSettings", planeSettings);

            m_scatteringShader.SetFloat("_AnisotropyK", Anisotropy);
			m_scatteringShader.SetFloat("_InscatterIntensity", InscatterIntensity / c_denseMult * 0.25f);
			m_scatteringShader.SetFloat("_FogDensity", FogDensity * c_denseMult);

			Color ambientSet = AmbientLight;
			ambientSet.a = AmbientIntensity * 0.01f;
			m_scatteringShader.SetVector("_Ambient", ambientSet);

			m_scatteringShader.SetInt("_Frame", (int)((uint)Time.frameCount));
			
			m_scatteringShader.SetVector("_CameraPos", Camera.current.transform.position);


			if (HasValidSun) {
				m_scatteringShader.SetVector("_LightDirection", Sun.transform.forward);
				m_scatteringShader.SetVector("_LightColor", Sun.color * Sun.intensity);

				if (Sun.shadows != LightShadows.None) {
					m_scatteringShader.SetTexture(m_densityKernel, "_ShadowMapTexture", ShadowMap);
					m_scatteringShader.SetTexture(m_scatterKernel, "_ShadowMapTexture", ShadowMap);
				} else {
					m_scatteringShader.SetTexture(m_densityKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
				}
			} else {
				m_scatteringShader.SetVector("_LightColor", Color.black);
			}

			//TODO: Need to measure impact of this carefully
			Graphics.SetRenderTarget(m_shadowMatrixTexture);
			m_matrixTextureRead.ReadPixels(new Rect(0, 0, m_shadowMatrixTexture.width, m_shadowMatrixTexture.height), 0, 0, false);
			Graphics.SetRenderTarget(null);

			//Grab cascaded shadow matrices
			for (int j = 0; j < 4; ++j) {
				Matrix4x4 set = Matrix4x4.zero;

				for (int i = 0; i < 4; ++i) {
					var col = m_matrixTextureRead.GetPixel(i, j);
					set[i, 0] = col.r;
					set[i, 1] = col.g;
					set[i, 2] = col.b;
					set[i, 3] = col.a;
				}

				m_scatteringShader.SetMatrix(s_matrixNames[j], set);
			}

			//Grab other info from texture
			Color nearSplit = m_matrixTextureRead.GetPixel(0, 4);
			Color farSplit = m_matrixTextureRead.GetPixel(1, 4);
			//Color lightShadowData = m_matrixTextureRead.GetPixel(2, 4);

			m_scatteringShader.SetVector("_LightSplitsNear", nearSplit);
			m_scatteringShader.SetVector("_LightSplitsFar", farSplit);

			//Setup range for clamping
			int cascadeCount = QualitySettings.shadowCascades;
			int cascX = cascadeCount > 1 ? 2 : 1;
			int cascY = cascadeCount > 2 ? 2 : 1;
			var rangeVec = new Vector4(1.0f / cascX, 1.0f / cascY);

			m_scatteringShader.SetVector("_Range", rangeVec);

			Matrix4x4 v = Camera.current.worldToCameraMatrix;
			Matrix4x4 p = Camera.current.projectionMatrix;
			p = GL.GetGPUProjectionMatrix(p, false);

			Matrix4x4 vp = p * v;

			//Set VP from old frame for reprojection
			m_scatteringShader.SetMatrix("_VAPOR_VP_OLD", m_vpMatrixOld);
			m_scatteringShader.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_vpMatrixOld = vp;

			UpdateLightBind();

		}

		private bool HasValidSun { get { return Sun != null && Sun.enabled && Sun.gameObject.activeInHierarchy; } }

		private void UpdateLightBind() {
			//TODO: Get point lights actually in range, base on some kind of priority
			//TODO: Make max lights configurable? re-allocate buffer when full

			//TODO: Make vapor light with extra scatter intensity... if that makes sense physically? 
			//Or just a constant mult? 1.0 is def too dark
			var pointLights = Light.GetLights(LightType.Point, -1);

			for (int i = 0; i < pointLights.Length; ++i) {
				if (i < pointLights.Length) {
					//TODO: Check this with alloy area lights...
					Vector4 posRange = pointLights[i].transform.position;
					posRange.w = 1.0f / (pointLights[i].range * pointLights[i].range);

					m_lightDataBuffer[i].PosRange = posRange;
					m_lightDataBuffer[i].Intenisty = pointLights[i].color * pointLights[i].intensity;
				}
			}

			m_scatteringShader.SetInt("_LightCount", pointLights.Length);
			//TODO: Is this needed every frame?
			m_lightComputeBuffer.SetData(m_lightDataBuffer);
			m_scatteringShader.SetBuffer(m_densityKernel, "_LightBuffer", m_lightComputeBuffer);
		}

		private void BindMaterial() {
			m_fogMat.SetFloat("_NearPlane", m_camera.nearClipPlane);
			m_fogMat.SetFloat("_FarPlane", m_camera.farClipPlane);
			m_fogMat.SetTexture("_ScatterTex", m_scatterTex);
		}


		private void OnPreRender() {
			UpdateCommandBuffer();
		}

		private void CleanCommandBuffers() {
			Sun.RemoveCommandBuffers(LightEvent.AfterShadowMap);
			Sun.RemoveCommandBuffers(LightEvent.AfterScreenspaceMask);

			if (m_cmdAfterShadow != null) {
				m_cmdAfterShadow.Dispose();
				m_cmdAfterShadow = null;
			}

			if (m_cmdAfterScreenMask != null) {
				m_cmdAfterScreenMask.Dispose();
				m_cmdAfterScreenMask = null;
			}
		}


		private void UpdateCommandBuffer() {
			if (Sun == null) {
				return;
			}

			bool initialized = m_cmdAfterShadow != null;

			if (Sun.shadows == LightShadows.None) {
				if (initialized) {
					CleanCommandBuffers();
				}
				return;
			}

			if (initialized && Sun.commandBufferCount != 0) {
				return;
			}

			CleanCommandBuffers();


			//Set the shadow map as a global texture
			m_cmdAfterShadow = new CommandBuffer();
			m_cmdAfterShadow.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive, ShadowSamplingMode.RawDepth);
			m_cmdAfterShadow.Blit(BuiltinRenderTextureType.CurrentActive, ShadowMap);
			m_cmdAfterShadow.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive, ShadowSamplingMode.CompareDepths);

			Sun.AddCommandBuffer(LightEvent.AfterShadowMap, m_cmdAfterShadow);

			//After mask, grab the matrices we need and such, which are still set at that point
			m_cmdAfterScreenMask = new CommandBuffer();
			m_cmdAfterScreenMask.Blit(Texture2D.whiteTexture, m_shadowMatrixTexture, m_screenShadowMaterial);
			Sun.AddCommandBuffer(LightEvent.AfterScreenspaceMask, m_cmdAfterScreenMask);
		}

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			//Ideally would do this a little earlier
			BindCompute();
			BindMaterial();

			Profiler.BeginSample("Density");
			m_scatteringShader.Dispatch(m_densityKernel, m_densityTex.width / 4, m_densityTex.height / 4, c_volumeDepth / 4);
			Profiler.EndSample();

			Profiler.BeginSample("Scattering");
			m_scatteringShader.Dispatch(m_scatterKernel, m_densityTex.width / 8, m_densityTex.height / 8, 1);
			Profiler.EndSample();

			var temp = m_densityTex;
			m_densityTex = m_densityTexOld;
			m_densityTexOld = temp;

			//Apply fog to image
			Graphics.Blit(source, destination, m_fogMat, 0);
		}

		private void OnDisable() {
			DestroyImmediate(m_densityTex);
			DestroyImmediate(m_scatterTex);
			DestroyImmediate(m_matrixTextureRead);
			DestroyImmediate(ShadowMap);
			DestroyImmediate(m_shadowMatrixTexture);

			if (m_lightComputeBuffer != null) {
				m_lightComputeBuffer.Dispose();
			}

			//Destroy all noises
			m_baseLayer.Destroy();
			m_secondaryLayer.Destroy();
			m_detailLayer.Destroy();
		}
	}
}