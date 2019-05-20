using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VaporAPI;

[ExecuteInEditMode]
public class VaporCustomLight : VaporObject {
	public Vector3 Size = Vector3.one;
	[SerializeField] VaporSetting m_setting;

	public float Intensity = 1.0f;
	public Light m_light;
	public float ShadowValue = 1.0f;
	public Texture2D CustomShadowMap;
	public override float CullRange => Size.magnitude;
	
	static Material s_shadowMapMultiplierMaterial;
	
	RenderTexture m_shadowmapCopy;

	[Range(0.05f, 0.4f)] public float SpotBaseSize = 0.3f;

	public VaporSetting Setting {
		get {
			if (m_setting == null) {
				m_setting = Resources.Load<VaporSetting>("DefaultVaporSetting");
			}

			return m_setting;
		}
	}

	void OnEnable() {
		Register(false);
		m_light = GetComponent<Light>();

		//shadowmap:
		RenderTargetIdentifier shadowmap = BuiltinRenderTextureType.CurrentActive;
		
		m_shadowmapCopy = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
		m_shadowmapCopy.filterMode = FilterMode.Bilinear;
		m_shadowmapCopy.wrapMode = TextureWrapMode.Clamp;
		m_shadowmapCopy.Create();

		CommandBuffer cb = new CommandBuffer();
		cb.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);

		if (s_shadowMapMultiplierMaterial == null) {
			s_shadowMapMultiplierMaterial = new Material(Shader.Find("Hidden/Vapor/VaporShadowMultiplier")) {
				hideFlags =  HideFlags.HideAndDontSave
			};
		}

		s_shadowMapMultiplierMaterial.SetFloat("_Range", ShadowValue);

		//This blit helps to intensify the shadows by multiplying it with a number/multiplier in the material
		cb.SetGlobalTexture("_VaporCustomLightShadow", shadowmap);
		cb.Blit(shadowmap, new RenderTargetIdentifier(m_shadowmapCopy), s_shadowMapMultiplierMaterial);
		m_light.AddCommandBuffer(LightEvent.AfterShadowMap, cb);
	}

	void OnDisable() {
		Deregister();
		m_light.RemoveAllCommandBuffers();
	}

	public override void Inject(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
		if (m_light.type != LightType.Spot) {
			Debug.LogError("Custom lights only work for spot lights!");
			return;
		}
		
		compute.SetMatrix("_ZoneWorldToLocal", transform.worldToLocalMatrix);
		compute.SetVector("_ZoneSize", Size * 0.5f);

		s_shadowMapMultiplierMaterial.SetFloat("_Range", ShadowValue);

		if (CustomShadowMap) {
			s_shadowMapMultiplierMaterial.EnableKeyword("CustomMap");
			s_shadowMapMultiplierMaterial.SetTexture("_VaporCustomShadowMap", CustomShadowMap);
		}
		else {
			s_shadowMapMultiplierMaterial.DisableKeyword("CustomMap");
			s_shadowMapMultiplierMaterial.SetTexture("_VaporCustomShadowMap", Texture2D.blackTexture);
		}

		//setup the light properties here so that we can use the light-shafts
		//make such that we do not require the vapor light component
		var lightProjMatrix = Matrix4x4.identity;
		float d = Mathf.Deg2Rad * m_light.spotAngle * 0.5f;
		d = Mathf.Cos(d) / Mathf.Sin(d);
		lightProjMatrix[3, 2] = 2f / d;
		lightProjMatrix[3, 3] = SpotBaseSize;
		var mat = lightProjMatrix * transform.worldToLocalMatrix;
		compute.SetMatrix("_SpotMatrix", mat);

		//Setup the lightPosRange etc params:
		//Setup basic params
		Vector4 posRange = transform.position;
		
		float range = m_light.range;
		posRange.w = 1.0f / (range * range);
		compute.SetVector("_LightPosRange", posRange);

		Vector4 lightStrength = m_light.color * m_light.intensity * Intensity;
		lightStrength *= 10;
		compute.SetVector("_LightColor", lightStrength);

		//Setup the _SpotCookie:
		if (m_light.cookie != null) {
			compute.SetTexture(vapor.CustomLightKernel, "_SpotCookie", m_light.cookie);
		}
		else {
			compute.SetTexture(vapor.CustomLightKernel, "_SpotCookie", vapor.SpotCookie);
		}

		//Handle the shadows:
		Matrix4x4 v = transform.worldToLocalMatrix;
		Matrix4x4 p =
			GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(m_light.spotAngle, 1.0f,
				m_light.shadowNearPlane,
				m_light.range), true);

		//For some reason z is flipped :(
		p *= Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

		compute.SetMatrix("_SpotShadowMatrix", p * v);
		compute.SetTexture(vapor.CustomLightKernel, "_SpotShadow", m_shadowmapCopy);

		Setting.Bind(compute, vapor.CustomLightKernel, Setting, 0.0f);
		compute.SetTexture(vapor.CustomLightKernel, "_DensityTextureWrite", vapor.GetDensityTex());
		vapor.SetLightAccum(vapor.CustomLightKernel, false);
		vapor.InjectObject(viewProj, vapor.CustomLightKernel, this);
	}

	public override void GetBounds(Transform space, List<Vector3> worldBounds) {
		Vector3 right = space.right;
		Vector3 up = space.up;
		Vector3 forward = space.forward;

		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, Size.y, 0f)) + right + up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, -Size.y, 0f)) + right - up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, Size.y, 0f)) - right + up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, -Size.y, 0f)) - right - up - forward);

		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, Size.y, Size.z)) + right + up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, -Size.y, Size.z)) + right - up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, Size.y, Size.z)) - right + up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, -Size.y, Size.z)) - right - up + forward);
	}
}