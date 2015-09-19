using System;
using UnityEngine;
using UnityEngine.Rendering;
using Vapor;

[ExecuteInEditMode]
public class VaporLight : MonoBehaviour {
	private static Material s_shadowBlurMaterial;
	private static Material s_screenShadowMaterial;
	private static Mesh s_quadMesh;

	public float FogScatterIntensity = 1.0f;

	//TODO: Handle change at runtime
	[Range(0.0f, 2.0f)]
	public float ShadowBlurSize;

	private CommandBuffer m_cmdBuffer;
	private CommandBuffer m_matrixCmdBuffer;

	[NonSerialized]
	public RenderTexture ShadowMap;

	[NonSerialized]
	public RenderTexture MatrixTexture;

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

		Register();
	}

	public void Register() {
		if (Vapor2.Instance != null) {
			m_light = GetComponent<Light>();

			if (s_shadowBlurMaterial == null) {
				s_shadowBlurMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowBlur"));
				s_screenShadowMaterial = new Material(Shader.Find("Hidden/Vapor/ShadowProperties"));

				//TODO: Can we just get the friggin quad 
				var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
				s_quadMesh = go.GetComponent<MeshFilter>().sharedMesh;
				DestroyImmediate(go);
			}


			if (Vapor2.Instance.RegisterLight(this)) {
				CreateResources();
			}
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


			m_cmdBuffer = new CommandBuffer();
			int blurTemp = Shader.PropertyToID("_ShadowBlurTemp");

			//Create shadow command buffer
			m_cmdBuffer.SetShadowSamplingMode(BuiltinRenderTextureType.CurrentActive, ShadowSamplingMode.RawDepth);


			m_cmdBuffer.Blit(BuiltinRenderTextureType.CurrentActive, ShadowMap, Vapor2.ShadowFilterMaterial);

			m_cmdBuffer.GetTemporaryRT(blurTemp, ShadowMap.width, ShadowMap.height, 0, FilterMode.Bilinear, RenderTextureFormat.RFloat);
			//Blur the shadow map. //TODO: Set offsets:
			m_cmdBuffer.Blit((RenderTargetIdentifier)ShadowMap, blurTemp, s_shadowBlurMaterial, 0);
			m_cmdBuffer.Blit(blurTemp, ShadowMap, s_shadowBlurMaterial, 1);
			m_cmdBuffer.ReleaseTemporaryRT(blurTemp);

			m_light.AddCommandBuffer(LightEvent.AfterShadowMap, m_cmdBuffer);

			if (HasMatrixTex) {
				MatrixTexture = new RenderTexture(4, 5, 0, RenderTextureFormat.ARGBFloat);

				m_matrixCmdBuffer = new CommandBuffer();
				m_matrixCmdBuffer.SetRenderTarget(MatrixTexture);

				var pass = m_light.type == LightType.Directional ? 0 : 1;
                m_matrixCmdBuffer.DrawMesh(s_quadMesh, Matrix4x4.identity, s_screenShadowMaterial, 0, pass);
				var ev = m_light.type == LightType.Directional ? LightEvent.AfterScreenspaceMask : LightEvent.AfterShadowMap;
				m_light.AddCommandBuffer(ev, m_matrixCmdBuffer);
			}
		}
	}

	public void UpdateCommandBuffer() {
		if (!HasShadow) {
			return;
		}
	}


	private void OnDisable() {
		if (Vapor2.Instance != null) {
			Vapor2.Instance.DeregisterLight(this);
		}
		
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
}
