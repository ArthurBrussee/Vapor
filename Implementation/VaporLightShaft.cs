using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VaporAPI;

[ExecuteInEditMode]
public class VaporLightShaft : VaporObject {
    
    public Vector3 Size = Vector3.one;
	public float Radius = 0.05f;
	[SerializeField] private VaporSetting m_setting;

    public float ZoneIntensity = 1.0f;
    public Light m_light;
    public float ShadowValue;
    public Texture2D CustomShadowMap;
    RenderTexture m_ShadowmapCopy;
    private Material m_ShadowMapMultiplierMaterial;

    public Material ShadowMapMultiplierMaterial
    {
        get
        {
            if (m_ShadowMapMultiplierMaterial == null)
            {
                m_ShadowMapMultiplierMaterial = new Material(Shader.Find("Hidden/Vapor/VaporShadowMultiplier"));
            }

            return m_ShadowMapMultiplierMaterial;
        }
    }

    [Range(0.05f, 0.4f)]
    public float SpotBaseSize = 0.3f;

    public float fallOffMultiplier;

    public VaporSetting Setting {
		get {
			if (m_setting == null) {
				m_setting = Resources.Load<VaporSetting>("DefaultVaporSetting");
			}

			return m_setting;
		}
	}

	void OnEnable()
    {
        Register(false);
        m_light = GetComponent<Light>();

        //shadowmap:
        RenderTargetIdentifier shadowmap = BuiltinRenderTextureType.CurrentActive;
        m_ShadowmapCopy = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
        m_ShadowmapCopy.filterMode = FilterMode.Bilinear;
        m_ShadowmapCopy.wrapMode = TextureWrapMode.Clamp;
        m_ShadowmapCopy.Create();

        CommandBuffer cb = new CommandBuffer();

        cb.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);


        //ShadowMapMultiplierMaterial = new Material(ShadowMultiplierShader);
        //ShadowMapMultiplierMaterial.hideFlags = HideFlags.HideAndDontSave;
        ShadowMapMultiplierMaterial.SetFloat("_Range", ShadowValue);

        if (ShadowMapMultiplierMaterial == null)
        {
            //This is a simple blit without manipulating the shadow:
            cb.Blit(shadowmap, new RenderTargetIdentifier(m_ShadowmapCopy));
        }
        else
        {
            //This blit helps to intensify the shadows by multiplying it with a number/multiplier in the material
            cb.SetGlobalTexture("_VaporLightShaftShadow", shadowmap);
            cb.Blit(shadowmap, new RenderTargetIdentifier(m_ShadowmapCopy), ShadowMapMultiplierMaterial);
        }

        Shader.SetGlobalTexture("_VaporLightShaftShadowMap", m_ShadowmapCopy);
        m_light.AddCommandBuffer(LightEvent.AfterShadowMap, cb);
    }

	void OnDisable() {
		Deregister();
        m_light.RemoveAllCommandBuffers();
	}

	public override void Inject(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
		compute.SetMatrix("_ZoneWorldToLocal", transform.worldToLocalMatrix);
		compute.SetVector("_ZoneSize", Size * 0.5f);
        compute.SetFloat("_ZoneIntensity", ZoneIntensity);
        ShadowMapMultiplierMaterial.SetFloat("_Range", ShadowValue);

        if (CustomShadowMap)
        {
            ShadowMapMultiplierMaterial.EnableKeyword("CustomMap");
            ShadowMapMultiplierMaterial.SetTexture("_VaporCustomShadowMap", CustomShadowMap);
        }
        else
        {
            ShadowMapMultiplierMaterial.DisableKeyword("CustomMap");
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
        posRange.w = 1.0f / (m_light.range * m_light.range);
        compute.SetVector("_LightPosRange", posRange);

        Vector4 lightStrength = m_light.color * m_light.intensity * ZoneIntensity;
        lightStrength *= 10;
        compute.SetVector("_LightColor", lightStrength);
        if (m_light.type == LightType.Spot)
        {
            //Setup the _SpotCookie:
            if (m_light.cookie != null)
            {
                compute.SetTexture(vapor.LightShaftKernel, "_SpotCookie", m_light.cookie);
            }
            else
            {
                compute.SetTexture(vapor.LightShaftKernel, "_SpotCookie", vapor.SpotCookie);
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
            compute.SetTexture(vapor.LightShaftKernel, "_SpotShadow", m_ShadowmapCopy);

            Setting.Bind(compute, vapor.LightShaftKernel, Setting, 0.0f);
            compute.SetTexture(vapor.LightShaftKernel, "_DensityTextureWrite", vapor.GetDensityTex());
            vapor.SetLightAccum(vapor.LightShaftKernel, false);
            vapor.InjectObject(viewProj, vapor.LightShaftKernel, this);
        }
	}

	public override void GetBounds(Transform space, List<Vector3> worldBounds) {
		Vector3 right = Radius * space.right;
		Vector3 up = Radius * space.up;
		Vector3 forward = Radius * space.forward;


		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, Size.y, -Size.z)) + right + up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, -Size.y, -Size.z)) + right - up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, Size.y, -Size.z)) - right + up - forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, -Size.y, -Size.z)) - right - up - forward);

		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, Size.y, Size.z)) + right + up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(Size.x, -Size.y, Size.z)) + right - up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, Size.y, Size.z)) - right + up + forward);
		worldBounds.Add(transform.TransformPoint(new Vector3(-Size.x, -Size.y, Size.z)) - right - up + forward);
	}

	public override float CullRange { get { return Size.magnitude + Radius; } }
}