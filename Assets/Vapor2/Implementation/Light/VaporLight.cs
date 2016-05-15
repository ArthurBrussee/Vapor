using System;
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

		private CommandBuffer m_cmdBuffer;
		private CommandBuffer m_matrixCmdBuffer;

		[NonSerialized] public RenderTexture MatrixTexture;

		private Light m_light;


		public Light Light {
			get { return m_light; }
		}

		public LightType LightType {
			get { return m_light.type; }
		}

		public bool HasMatrixTex {
			get { return m_light.type == LightType.Directional || m_light.type == LightType.Spot; }
		}

		private void OnEnable() {
			m_light = GetComponent<Light>();

			Vapor2.Instance.Register(this);
			CreateResources();

		}

		private void OnDisable() {

			Vapor2.Instance.Deregister(this);




			m_light.RemoveCommandBuffers(LightEvent.AfterShadowMap);
			m_light.RemoveCommandBuffers(LightEvent.AfterScreenspaceMask);

			if (m_cmdBuffer != null) {
				m_cmdBuffer.Dispose();
			}

			if (m_matrixCmdBuffer != null) {
				m_matrixCmdBuffer.Dispose();
			}

			if (ShadowMap != null) {
				DestroyImmediate(ShadowMap);
			}

			if (MatrixTexture != null) {
				DestroyImmediate(MatrixTexture);
			}
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

		//TODO: Better formula here.. this assumes 4K
		private int GetShadowMapResolution() {
			return 2048;
		}

		private void CreateResources() {
			if (HasShadow) {
				m_light.RemoveAllCommandBuffers();

				int res = GetShadowMapResolution();

				ShadowMap = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat);
				ShadowMap.name = "VaporShadowMap";

				RenderTargetIdentifier shadowId = BuiltinRenderTextureType.CurrentActive;
				
				m_cmdBuffer = new CommandBuffer();

				int blurTemp = Shader.PropertyToID("_ShadowBlurTemp");

				//Create shadow command buffer
				m_cmdBuffer.SetShadowSamplingMode(shadowId, ShadowSamplingMode.RawDepth);
				m_cmdBuffer.GetTemporaryRT(blurTemp, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.RFloat);

				m_cmdBuffer.Blit(shadowId, blurTemp);
				m_cmdBuffer.Blit(blurTemp, ShadowMap, Vapor2.ShadowFilterMaterial);

				
				//Blur the shadow map. //TODO: Set offsets:
				m_cmdBuffer.Blit((RenderTargetIdentifier)ShadowMap, blurTemp, Vapor2.ShadowBlurMaterial, 0);
				m_cmdBuffer.Blit(blurTemp, ShadowMap, Vapor2.ShadowBlurMaterial, 1);
				m_cmdBuffer.ReleaseTemporaryRT(blurTemp);

				m_light.AddCommandBuffer(LightEvent.AfterShadowMap, m_cmdBuffer);

				if (HasMatrixTex) {
					MatrixTexture = new RenderTexture(4, 5, 0, RenderTextureFormat.ARGBFloat);

					m_matrixCmdBuffer = new CommandBuffer();
					m_matrixCmdBuffer.SetRenderTarget(MatrixTexture);

					var pass = m_light.type == LightType.Directional ? 0 : 1;
					m_matrixCmdBuffer.DrawMesh(Vapor2.QuadMesh, Matrix4x4.identity, Vapor2.ScreenShadowMaterial, 0, pass);
					var ev = m_light.type == LightType.Directional ? LightEvent.AfterScreenspaceMask : LightEvent.AfterShadowMap;
					m_light.AddCommandBuffer(ev, m_matrixCmdBuffer);
				}
			}
		}
	}
}