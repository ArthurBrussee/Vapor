using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vapor {
	[ExecuteInEditMode]
	public class VaporLight : VaporObject {
		private static Mesh s_quadMesh;
		public static Mesh QuadMesh {
			get {
				if (s_quadMesh == null) {
					//TODO: Can we just get the friggin quad 
					var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
					s_quadMesh = go.GetComponent<MeshFilter>().sharedMesh;
					DestroyImmediate(go);
				}

				return s_quadMesh;
			}
		}
		
		private static Material s_shadowFilterMaterial;
		public static Material ShadowFilterMaterial {
			get {
				if (s_shadowFilterMaterial == null) {
					s_shadowFilterMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowFilterESM"));
				}

				return s_shadowFilterMaterial;
			}
		}

		private static Material s_screenShadowMaterial;
		public static Material ScreenShadowMaterial {
			get {
				if (s_screenShadowMaterial == null) {
					s_screenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));
				}

				return s_screenShadowMaterial;
			}
		}

		public float FogScatterIntensity = 1.0f;
		[NonSerialized]
		public RenderTexture ShadowMap;

		private CommandBuffer m_shadowCmd;
		private CommandBuffer m_matrixCmdBuffer;

		public ComputeBuffer MatrixBuffer;
		public ComputeBuffer LightSplitsBuffer;

		private Light m_light;

		public Light Light {
			get { return m_light; }
		}

		public LightType LightType {
			get { return m_light.type; }
		}

		private bool ShadowSupported() {
			return m_light.type == LightType.Directional || m_light.type == LightType.Spot;
		}

		public bool HasShadow {
			get {
				if (ShadowSupported()) {
					if (m_light.shadows == LightShadows.Hard || m_light.shadows == LightShadows.Soft) {
						return true;
					}
				}

				return false;
			}
		}

		private void OnEnable() {
			m_light = GetComponent<Light>();
			Register(LightType == LightType.Directional);
			CreateShadowResources();
		}

		private void OnDisable() {
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
				LightSplitsBuffer.Dispose();
			}

			DestroyImmediate(ShadowMap);
		}


		//TODO: Better formula here.. this assumes 4K
		private int GetShadowMapResolution() {
			return 2048;
		}

		public void CreateShadowResources() {
			if (ShadowMap != null || !HasShadow) {
				return;
			}


			m_light.RemoveAllCommandBuffers();


			int res = GetShadowMapResolution();
			ShadowMap = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat) {name = "VaporShadowMap"};
			
			RenderTargetIdentifier shadowId = BuiltinRenderTextureType.CurrentActive;
			int blurTemp = Shader.PropertyToID("_ShadowBlurTemp");

			m_shadowCmd = new CommandBuffer();

			//Create shadow command buffer
			m_shadowCmd.SetShadowSamplingMode(shadowId, ShadowSamplingMode.RawDepth);
			m_shadowCmd.GetTemporaryRT(blurTemp, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.RFloat);

			m_shadowCmd.Blit(shadowId, blurTemp);
			m_shadowCmd.Blit(blurTemp, ShadowMap, ShadowFilterMaterial);

			//Blur the shadow map. //TODO: Set offsets:
			//m_shadowCmd.Blit((RenderTargetIdentifier) ShadowMap, blurTemp, Vapor2.ShadowBlurMaterial, 0);
			//m_shadowCmd.Blit(blurTemp, ShadowMap, Vapor2.ShadowBlurMaterial, 1);
			//m_shadowCmd.ReleaseTemporaryRT(blurTemp);

			m_light.AddCommandBuffer(LightEvent.AfterShadowMap, m_shadowCmd);

			if (m_light.type != LightType.Directional) {
				return;
			}

			MatrixBuffer = new ComputeBuffer(4, 4 * 16);
			LightSplitsBuffer = new ComputeBuffer(1, 4 * 4);
			int matrixTemp = Shader.PropertyToID("_MatrixTemp");

			m_matrixCmdBuffer = new CommandBuffer();
			m_matrixCmdBuffer.GetTemporaryRT(matrixTemp, 1, 1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
			m_matrixCmdBuffer.SetRenderTarget(matrixTemp);
			m_matrixCmdBuffer.DrawMesh(QuadMesh, Matrix4x4.identity, ScreenShadowMaterial, 0);
			m_matrixCmdBuffer.ReleaseTemporaryRT(matrixTemp);

			m_light.AddCommandBuffer(LightEvent.AfterScreenspaceMask, m_matrixCmdBuffer);
		}

		public override void Bind(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
			var l = Light;

			Vector4 posRange = transform.position;
			posRange.w = 1.0f / (l.range * l.range);
			compute.SetVector("_LightPosRange", posRange);

			Vector4 lightStrength = l.color * l.intensity * FogScatterIntensity;

			if (LightType != LightType.Directional) {
				lightStrength *= 15;
			}

			compute.SetVector("_LightColor", lightStrength);


			switch (LightType) {
				case LightType.Directional:
					VaporKernel.ShadowMode mode;

					if (HasShadow) {
						if (QualitySettings.shadowCascades > 1) {
							mode = VaporKernel.ShadowMode.Cascaded;
						}
						else {
							mode = VaporKernel.ShadowMode.Shadowed;
						}
					}
					else {
						mode = VaporKernel.ShadowMode.None;
					}


					int dirKernel = vapor.LightDirKernel.GetKernel(mode);

					compute.SetVector("_LightPosRange", l.transform.forward);

					if (HasShadow) {
						compute.SetBuffer(dirKernel, "_MatrixBuf", MatrixBuffer);
						compute.SetBuffer(dirKernel, "_LightSplits", LightSplitsBuffer);
						compute.SetTexture(dirKernel, "_ShadowMapTexture", ShadowMap);
					} else {
						compute.SetTexture(dirKernel, "_ShadowMapTexture", Texture2D.whiteTexture);
					}

					vapor.SetLightAccum(dirKernel, false);
					Profiler.BeginSample("Dir Light pass");
					compute.DispatchScaled(dirKernel, Vapor.HorizontalTextureRes, Vapor.VerticalTextureRes, Vapor.VolumeDepth);
					Profiler.EndSample();
					break;

				case LightType.Point:
					vapor.SetLightAccum(vapor.LightPointKernel, false);
					vapor.InjectObject(viewProj, vapor.LightPointKernel, this);
					break;

				case LightType.Spot:
					int spotKernel = vapor.LightSpotKernel.GetKernel(HasShadow ? VaporKernel.ShadowMode.Shadowed : VaporKernel.ShadowMode.None);

					if (HasShadow) {
						Matrix4x4 v = transform.worldToLocalMatrix;
						Matrix4x4 p =
							GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(Light.spotAngle, 1.0f,
								Light.shadowNearPlane,
								Light.range), true);

						//For some reason z is flipped :(
						p *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

						compute.SetMatrix("_SpotShadowMatrix", p * v);
						compute.SetTexture(spotKernel, "_SpotShadow", ShadowMap);
					}

					var lightProjMatrix = Matrix4x4.identity;
					float d = Mathf.Deg2Rad * l.spotAngle * 0.5f;
					d = Mathf.Cos(d) / Mathf.Sin(d);
					lightProjMatrix[3, 2] = 2f / d;
					lightProjMatrix[3, 3] = 0.35f;
					var mat = lightProjMatrix * transform.worldToLocalMatrix;
					compute.SetMatrix("_SpotMatrix", mat);
					if (l.cookie != null) {
						compute.SetTexture(spotKernel, "_SpotCookie", l.cookie);
					} else {
						compute.SetTexture(spotKernel, "_SpotCookie", vapor.SpotCookie);
					}

					vapor.SetLightAccum(spotKernel, false);
					vapor.InjectObject(viewProj, spotKernel, this);
					break;
			}

		}




		public override float Range {
			get { return m_light.range; }
		}
	}
}