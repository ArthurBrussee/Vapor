using UnityEngine;
using System.Collections.Generic;

namespace Vapor {
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

		public abstract void Bind(Vapor vapor, ComputeShader compute, Matrix4x4 viewProj);


		public abstract float Range { get; }
	}
}