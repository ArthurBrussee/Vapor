using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VaporAPI {
	[CustomEditor(typeof(VaporLight))]
	[CanEditMultipleObjects]
	public class VaporLightEditor : VaporBaseEditor {
		public override void OnInspectorGUI() {
			serializedObject.Update();
			PropertyField("FogScatterIntensity", "Multiplier of light intensity in the fog");

			if (targets.All(t => (t as VaporLight).LightType == LightType.Spot)) {
				PropertyField("SpotBaseSize", "");
			}

			serializedObject.ApplyModifiedProperties();
		}
	}
}