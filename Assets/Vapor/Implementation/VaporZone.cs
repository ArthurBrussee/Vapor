using UnityEngine;
using System.Collections;

namespace Vapor {
	[ExecuteInEditMode]
	public class VaporZone : VaporObject {
		[SerializeField]
		private VaporSetting m_setting;

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

		public override void Bind(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj) {
			compute.SetMatrix("_ZoneWorldToLocal", transform.worldToLocalMatrix);
			vapor.BindSetting(Setting);
			compute.SetTexture(vapor.ZoneKernel, "_DensityTextureWrite", vapor.DensityTex);
			vapor.InjectObject(viewProj, vapor.ZoneKernel, this);
		}

		public override float Range {
			get { return 1.0f; }
		}
	}
}