using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Vapor {
	public abstract class VaporObject : MonoBehaviour {
		public static List<VaporObject> All = new List<VaporObject>();

		protected void Register() {
			All.Add(this);
		}

		protected void Deregister() {
			int index = All.IndexOf(this);

			if (index != -1) {
				All[index] = All[All.Count - 1];
				All.RemoveAt(All.Count - 1);
			}
		}

		public abstract float Range { get; }
	}
}