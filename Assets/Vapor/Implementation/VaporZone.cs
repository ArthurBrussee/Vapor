using UnityEngine;
using System.Collections;

namespace Vapor {
	public class VaporZone : VaporObject {
		void OnEnable() {
			Register();
		}

		void OnDisable() {

		}

		public override float Range {
			get { return 1.0f; }
		}
	}
}