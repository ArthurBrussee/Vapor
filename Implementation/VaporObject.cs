using System.Collections.Generic;
using UnityEngine;

namespace VaporAPI {
	public abstract class VaporObject : MonoBehaviour {
		public static List<VaporObject> All = new List<VaporObject>();

		protected void Register(bool first) {
			if (!first) {
				All.Add(this);
			}
			else {
				All.Insert(0, this);
			}
		}

		protected void Deregister() {
			int index = All.IndexOf(this);

			if (index != -1) {
				All[index] = All[All.Count - 1];
				All.RemoveAt(All.Count - 1);
			}
		}

		public abstract void Inject(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj);
		public abstract void GetBounds(Transform space, List<Vector3> cameraWorldBounds);
		public abstract float CullRange { get; }
	}
}