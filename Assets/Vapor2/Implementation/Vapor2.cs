using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Vapor {
	public struct VaporPointLight {
		public Vector4 PosRange;
		public Vector4 Intensity;
		public const int Stride = 32;
	}

	public struct VaporSpotLight {
		public Vector4 PosRange;
		public Vector4 Intensity;

		public Matrix4x4 LightMatrix;
		public Matrix4x4 ShadowMatrix;

		public const int Stride = 160;
	}

	[ExecuteInEditMode]
	public class Vapor2 : MonoBehaviour {
		public static Vapor2 Instance;
		public static Material ShadowFilterMaterial;


		[Header("Global settings")]
		public Color Albedo = new Color(0.1f, 0.1f, 0.1f); //sig_s / sig_t
		public float Extinction = 0.15f; //sig_t
	
		[Range(0.0f, 1.0f)] public float TemporalStrength = 1.0f;
		[Range(-1.0f, 1.0f)] public float Phase;

		public Color Emissive = Color.black;
		public Color AmbientLight = Color.black;

		public float BlurSize;
		
		public float AveragingSpeed = 0.1f;
		public float ShadowHardness = 70.0f;
		public float ReprojectionSmoothing = 0.075f;

		[Range(0.0f, 1.0f)]
		public float ShadowBias = 0.05f;

		public Texture2D SpotCookie;
		
		
		[SerializeField] private NoiseLayer m_baseLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_secondaryLayer = new NoiseLayer();
		[SerializeField] private NoiseLayer m_detailLayer = new NoiseLayer();

		private const int c_horizontalTextureRes = 240;
		private const int c_verticalTextureRes = 136;
		private const int c_volumeDepth = 256;

		private ComputeShader m_vaporCompute;
		

		private RenderTexture m_densityTex;
		private RenderTexture m_densityTexOld;
		private RenderTexture m_scatterTex;

		private int m_scatterKernel;
		private int m_densityKernel;

		private Camera m_camera;
		private Material m_fogMat;

		//Point light data
		private const int c_defaultPointCount = 8;
		private ComputeBuffer m_pointLightBuffer;
		private VaporPointLight[] m_pointLightDataBuffer = new VaporPointLight[c_defaultPointCount];
		
		//Spot light data
		private const int c_defualtSpotCount = 8;
		private ComputeBuffer m_spotLightBuffer;
		private VaporSpotLight[] m_spotLightDataBuffer = new VaporSpotLight[c_defualtSpotCount];
		

		private Matrix4x4 m_vpMatrixOld;

		//Matrix info
		private Texture2D m_matrixTextureRead;

		private RenderTexture m_fogFilterTexture;


		private List<VaporLight> m_vaporLights = new List<VaporLight>(); 

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

			if (Instance != null) {
				Debug.LogError("Two vapors in the same scene!");
			}

			ShadowFilterMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowFilterESM"));


			Instance = this;
			RegisterCurrentLights();

			CreateResources();
		}

		private void RegisterCurrentLights() {
			var allLights = Object.FindObjectsOfType<VaporLight>();

			foreach (var vaporLight in allLights) {
				vaporLight.Register();
			}
		}

		public bool RegisterLight(VaporLight l) {
			if (!m_vaporLights.Contains(l)) {
				m_vaporLights.Add(l);
				return true;
			}

			return false;
		}


		public void DeregisterLight(VaporLight vaporLight) {
			m_vaporLights.Remove(vaporLight);
		}

		private void CreateResources() {
			//Break dependance on Resources? Could cause stalls for people grmbl
			m_vaporCompute = Resources.Load<ComputeShader>("VaporSim");
			m_scatterKernel = m_vaporCompute.FindKernel("Scatter");
			m_densityKernel = m_vaporCompute.FindKernel("FogDensity");
			
			m_fogMat = new Material(Shader.Find("Hidden/VaporPost"));
			
			//TODO: Report a bug on unity - this fails for whatever reason, though it works fine
			m_matrixTextureRead = new Texture2D(4, 5, TextureFormat.RGBAFloat, false);

			//SpotShadow = new RenderTexture(512, 512, 0, RenderTextureFormat.RFloat);
			//m_spotMatrixTex = new RenderTexture(4, 6, 0, RenderTextureFormat.ARGBFloat);

			CreateComputeBuffers();

			CreateTexture(ref m_scatterTex);
			CreateTexture(ref m_densityTex);
			CreateTexture(ref m_densityTexOld);

			if (m_baseLayer.NeedsBuild() || m_secondaryLayer.NeedsBuild() || m_detailLayer.NeedsBuild()) {
				BakeNoiseLayers();
			}

			m_camera = GetComponent<Camera>();
			
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
		}

		private void BindCompute() {
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
			Vector4 planeSettings = new Vector4(near, far - near, (far + near) / (2 * (far - near)) + 0.5f, (-far * near) / (far - near));
			m_vaporCompute.SetVector("_PlaneSettings", planeSettings);

			Vector4 zBuffer = new Vector4(1.0f - far / near, far / near);
			m_vaporCompute.SetVector("_ZBufferParams", new Vector4(zBuffer.x, zBuffer.y, zBuffer.x / far, zBuffer.y / far));


			float delt = Application.isPlaying ? Time.deltaTime : Time.fixedDeltaTime;

            m_vaporCompute.SetFloat("_ExponentialWeight", Mathf.Pow(AveragingSpeed, 1.0f / (60.0f * delt)));
            m_vaporCompute.SetFloat("_ReprojectionSmoothing", ReprojectionSmoothing);
            m_vaporCompute.SetFloat("_TemporalStrength", TemporalStrength);

			m_vaporCompute.SetVector("_AlbedoExt", new Vector4(Albedo.r * 0.1f, Albedo.g * 0.1f, Albedo.b * 0.1f, Extinction));
			m_vaporCompute.SetFloat("_Extinction", Extinction);

			m_vaporCompute.SetVector("_EmissivePhase", new Vector4(Emissive.r * 0.2f, Emissive.g * 0.2f, Emissive.b * 0.2f, Phase));
			m_vaporCompute.SetVector("_AmbientLight", AmbientLight);

			m_vaporCompute.SetInt("_Frame", Time.frameCount);
			m_vaporCompute.SetVector("_CameraPos", Camera.current.transform.position);
					

			//Grab other info from texture
			Color nearSplit = m_matrixTextureRead.GetPixel(0, 4);
			Color farSplit = m_matrixTextureRead.GetPixel(1, 4);
			//Color lightShadowData = m_matrixTextureRead.GetPixel(2, 4);

			m_vaporCompute.SetVector("_LightSplitsNear", nearSplit);
			m_vaporCompute.SetVector("_LightSplitsFar", farSplit);

			//Setup range for clamping
			int cascadeCount = QualitySettings.shadowCascades;
			int cascX = cascadeCount > 1 ? 2 : 1;
			int cascY = cascadeCount > 2 ? 2 : 1;
			var rangeVec = new Vector4(1.0f / cascX, 1.0f / cascY);

			m_vaporCompute.SetVector("_Range", rangeVec);

			Matrix4x4 v = Camera.current.worldToCameraMatrix;
			Matrix4x4 p = Camera.current.projectionMatrix;
			p = GL.GetGPUProjectionMatrix(p, false);

			Matrix4x4 vp = p * v;

			//Set VP from old frame for reprojection
			m_vaporCompute.SetMatrix("_VAPOR_VP_OLD", m_vpMatrixOld);
			m_vaporCompute.SetMatrix("_VAPOR_I_VP", vp.inverse);
			m_vpMatrixOld = vp;

			UpdateLightBind();

		}

		private void UpdateLightBind() {
			//TODO: Get lights actually in range, base on some kind of priority
			//TODO: Unify some code here

			//Globals
			m_vaporCompute.SetFloat("_ShadowSoft", ShadowHardness);
			m_vaporCompute.SetFloat("_ShadowBias", ShadowBias * 0.1f);

			if (SpotCookie != null) {
				m_vaporCompute.SetTexture(m_densityKernel, "_SpotCookie", SpotCookie);
			}

			ShadowFilterMaterial.SetFloat("_ShadowSoft", ShadowHardness);

			//Bind each light
			int pointLightCount = 0;
			int spotLightCount = 0;

			foreach (var vaporLight in m_vaporLights) {
				if (vaporLight.LightType == LightType.Directional) {
					var l = vaporLight.Light;

					m_vaporCompute.SetVector("_LightDirection", l.transform.forward);
					m_vaporCompute.SetVector("_LightColor", l.color * l.intensity * vaporLight.FogScatterIntensity);


					if (vaporLight.HasShadow) {
						m_vaporCompute.SetTexture(m_densityKernel, "_ShadowMapTexture", vaporLight.ShadowMap);



						//TODO: Measured, it's awful. Need to come up with a ComputeBuffer solution
						Profiler.BeginSample("Get matrix");
						Graphics.SetRenderTarget(vaporLight.MatrixTexture);
						m_matrixTextureRead.ReadPixels(new Rect(0, 0, vaporLight.MatrixTexture.width, vaporLight.MatrixTexture.height), 0, 0, false);
						m_vaporCompute.SetTexture(m_densityKernel, "_MatrixTex", vaporLight.MatrixTexture);

						Graphics.SetRenderTarget(null);
						Profiler.EndSample();

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


							m_vaporCompute.SetMatrix(s_matrixNames[j], set);
						}
					} else {
						m_vaporCompute.SetTexture(m_densityKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
					}


				}
				else if (vaporLight.LightType == LightType.Point) {
					var l = vaporLight.Light;
					Vector4 posRange = l.transform.position;
					posRange.w = 1.0f / (l.range * l.range);

					//TODO: Arbitrary * 8
					m_pointLightDataBuffer[pointLightCount].PosRange = posRange;
					m_pointLightDataBuffer[pointLightCount].Intensity = l.color * l.intensity * vaporLight.FogScatterIntensity * 8.0f;

					++pointLightCount;
				}
				else if (vaporLight.LightType == LightType.Spot) {
					var l = vaporLight.Light;

					if (vaporLight.HasShadow) {
						Profiler.BeginSample("Get spot matrix");
						Graphics.SetRenderTarget(vaporLight.MatrixTexture);
						m_matrixTextureRead.ReadPixels(new Rect(0, 0, vaporLight.MatrixTexture.width, vaporLight.MatrixTexture.height), 0,
							0, false);
						Graphics.SetRenderTarget(null);
						Profiler.EndSample();
						m_vaporCompute.SetTexture(m_densityKernel, "_SpotShadow", vaporLight.ShadowMap);

						//Grab cascaded shadow matrices
						Matrix4x4 set = Matrix4x4.zero;
						for (int i = 0; i < 4; ++i) {
							var col = m_matrixTextureRead.GetPixel(i, 0);
							set[i, 0] = col.r;
							set[i, 1] = col.g;
							set[i, 2] = col.b;
							set[i, 3] = col.a;
						}

						m_spotLightDataBuffer[spotLightCount].ShadowMatrix = set;
					}

					Vector4 posRange = l.transform.position;
					posRange.w = 1.0f / (l.range * l.range);

					//TODO: Arbitrary * 8
					m_spotLightDataBuffer[spotLightCount].PosRange = posRange;
					m_spotLightDataBuffer[spotLightCount].Intensity = l.color * l.intensity * vaporLight.FogScatterIntensity * 16.0f;

					var lightProjMatrix = Matrix4x4.identity;
					float d = Mathf.Deg2Rad * l.spotAngle * 0.5f;
					d = Mathf.Cos(d) / Mathf.Sin(d);
					lightProjMatrix[3, 2] = 2f / d;
					lightProjMatrix[3, 3] = 0.1f;
		
					var mat = lightProjMatrix * l.transform.worldToLocalMatrix;
					m_spotLightDataBuffer[spotLightCount].LightMatrix = mat;

					++spotLightCount;
				}
			}
			
			m_vaporCompute.SetInt("_PointLightCount", pointLightCount);
			m_vaporCompute.SetInt("_SpotLightCount", spotLightCount);


			if (m_pointLightDataBuffer.Length < pointLightCount) {
				Array.Resize(ref m_pointLightDataBuffer, pointLightCount);
				CreateComputeBuffers();
			}

			m_pointLightBuffer.SetData(m_pointLightDataBuffer);
			m_vaporCompute.SetBuffer(m_densityKernel, "_PointLightBuffer", m_pointLightBuffer);



			if (m_pointLightDataBuffer.Length < spotLightCount) {
				Array.Resize(ref m_pointLightDataBuffer, pointLightCount);
				CreateComputeBuffers();
			}

			m_spotLightBuffer.SetData(m_spotLightDataBuffer);
			m_vaporCompute.SetBuffer(m_densityKernel, "_SpotLightBuffer", m_spotLightBuffer);
		}

		/*
		private void SetupSpotShadow(Light l) {
			//TODO: Merge with directional
			if (l.type != LightType.Spot) {
				return;
			}
			l.RemoveAllCommandBuffers();
			l.RemoveCommandBuffers(LightEvent.AfterShadowMap);

			var cmd = new CommandBuffer();
			var cmd2 = new CommandBuffer();

			//Create shadow command buffer
			cmd.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive, ShadowSamplingMode.RawDepth);
			cmd.Blit(BuiltinRenderTextureType.CurrentActive, SpotShadow, m_shadowFilterMaterial);

			cmd2.Blit(Texture2D.whiteTexture, m_spotMatrixTex, m_screenShadowMaterial);


			l.AddCommandBuffer(LightEvent.AfterShadowMap, cmd);
			l.AddCommandBuffer(LightEvent.AfterShadowMap, cmd2);

			m_lightToBuf.Add(l, new LightShadowData { Buf = cmd, Tex = SpotShadow, MatrixTex = m_spotMatrixTex } );
		}
*/

		private int m_frameCount;

		private void BindMaterial() {
			m_fogMat.SetTexture("_ScatterTex", m_scatterTex);
			m_fogMat.SetInt("_Frame", ++m_frameCount);

		}
		
		private float Noise(float channel) {
			return Random.value *2.0f - 1.0f;
		}

		private void OnPreRender() {
			foreach (var vaporLight in m_vaporLights) {
				vaporLight.UpdateCommandBuffer();
			}	
		}



		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			//Ideally would do this a little earlier
			BindCompute();
			BindMaterial();

			Profiler.BeginSample("Density");
			m_vaporCompute.Dispatch(m_densityKernel, m_densityTex.width / 4 , m_densityTex.height / 4, m_densityTex.volumeDepth / 4);
			Profiler.EndSample();

			Profiler.BeginSample("Scattering");
			m_vaporCompute.Dispatch(m_scatterKernel, m_densityTex.width / 8, m_densityTex.height / 8, 1);
			Profiler.EndSample();

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
			RenderTexture blurTemp = RenderTexture.GetTemporary(m_fogFilterTexture.width, m_fogFilterTexture.height, 0, RenderTextureFormat.ARGBHalf);

			m_fogMat.SetVector("_BlurSize", new Vector4(BlurSize * 0.5f + 0, 0.0f, 0.0f, 0.0f));
			Graphics.Blit(m_fogFilterTexture, blurTemp, m_fogMat, 2);

			m_fogMat.SetVector("_BlurSize", new Vector4(0.0f, BlurSize * 0.5f + 0, 0.0f, 0.0f));
			Graphics.Blit(blurTemp, m_fogFilterTexture, m_fogMat, 2);

			RenderTexture.ReleaseTemporary(blurTemp);

			//Gaussian
			m_fogMat.SetTexture("_FogTex", m_fogFilterTexture);
            Graphics.Blit(source, destination, m_fogMat, 1);
		}


		private void OnDisable() {
			DestroyImmediate(m_densityTex);
			DestroyImmediate(m_scatterTex);
			DestroyImmediate(m_matrixTextureRead);

			if (m_pointLightBuffer != null) {
				m_pointLightBuffer.Dispose();
			}

			if (m_spotLightBuffer != null) {
				m_spotLightBuffer.Dispose();
			}

			/*
			foreach (var data in m_lightToBuf) {
				data.Value.Buf.Dispose();
				DestroyImmediate(data.Value.Tex);
				DestroyImmediate(data.Value.MatrixTex);
			}
			m_lightToBuf.Clear();
			*/
			
			//Destroy all noises
			m_baseLayer.Destroy();
			m_secondaryLayer.Destroy();
			m_detailLayer.Destroy();
		}

	}
}