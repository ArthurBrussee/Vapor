using System.Collections.Generic;
using UnityEngine;
using VaporAPI;

[ExecuteInEditMode]
public class VaporZone : VaporObject {
	public Vector3 Size = Vector3.one;
	public float Radius = 0.05f;
	[SerializeField] VaporSetting m_setting;

	public override float CullRange => Size.magnitude + Radius;

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
	}

	void OnDisable() {
		Deregister();
	}

	public override void Inject(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
		compute.SetMatrix("_ZoneWorldToLocal", transform.worldToLocalMatrix);
		compute.SetFloat("_ZoneRadiusSqr", Radius * Radius);
		compute.SetVector("_ZoneSize", Size * 0.5f);

		Setting.Bind(compute, vapor.ZoneKernel, Setting, 0.0f);
		compute.SetTexture(vapor.ZoneKernel, "_DensityTextureWrite", vapor.GetDensityTex());
		vapor.SetLightAccum(vapor.ZoneKernel, false);
		vapor.InjectObject(viewProj, vapor.ZoneKernel, this);
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
}