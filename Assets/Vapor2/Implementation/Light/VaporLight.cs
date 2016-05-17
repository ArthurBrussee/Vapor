using UnityEngine;
using UnityEngine.Rendering;

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
	public class VaporLight : MonoBehaviour {
		public float FogScatterIntensity = 1.0f;

		//TODO: Add some culling in here.

		//TODO: Handle change at runtime
		[Range(0.0f, 2.0f)] public float ShadowBlurSize;

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
			Vapor2.Instance.Register(this);


			if (HasShadow) {

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
				m_shadowCmd.Blit(blurTemp, ShadowMap, Vapor2.ShadowFilterMaterial);

				//Blur the shadow map. //TODO: Set offsets:
				//m_shadowCmd.Blit((RenderTargetIdentifier) ShadowMap, blurTemp, Vapor2.ShadowBlurMaterial, 0);
				//m_shadowCmd.Blit(blurTemp, ShadowMap, Vapor2.ShadowBlurMaterial, 1);
				//m_shadowCmd.ReleaseTemporaryRT(blurTemp);

				m_light.AddCommandBuffer(LightEvent.AfterShadowMap, m_shadowCmd);


				if (m_light.type == LightType.Directional) {
					MatrixBuffer = new ComputeBuffer(4, 4 * 16);
					LightSplitsBuffer = new ComputeBuffer(1, 4 * 4);

					int matrixTemp = Shader.PropertyToID("_MatrixTemp");

					m_matrixCmdBuffer = new CommandBuffer();

					m_matrixCmdBuffer.GetTemporaryRT(matrixTemp, 1, 1, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
					m_matrixCmdBuffer.SetRenderTarget(matrixTemp);

					m_matrixCmdBuffer.DrawMesh(Vapor2.QuadMesh, Matrix4x4.identity, Vapor2.ScreenShadowMaterial, 0);
	
					m_matrixCmdBuffer.ReleaseTemporaryRT(matrixTemp);
					m_light.AddCommandBuffer(LightEvent.AfterScreenspaceMask, m_matrixCmdBuffer);
				}
			}
		}

		private void OnDisable() {
			Vapor2.Instance.Deregister(this);



			if (HasShadow) {
				m_shadowCmd.Dispose();


				if (LightType == LightType.Directional) {
					m_matrixCmdBuffer.Dispose();
					MatrixBuffer.Dispose();
					LightSplitsBuffer.Dispose();
				}


				m_light.RemoveCommandBuffers(LightEvent.AfterShadowMap);
				m_light.RemoveCommandBuffers(LightEvent.AfterScreenspaceMask);
				
				DestroyImmediate(ShadowMap);
			}
		}
		

		//TODO: Better formula here.. this assumes 4K
		private int GetShadowMapResolution() {
			return 2048;
		}
	}
}