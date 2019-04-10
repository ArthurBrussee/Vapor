using UnityEngine;
using UnityEditor;

namespace VaporAPI {
	[CustomEditor(typeof(VaporLightShaft))]
	[CanEditMultipleObjects]
	public class VaporLightShaftEditor : VaporBaseEditor {
		Editor m_settingEditor;
        static Vector3 lightShaftGizmoPosition = Vector3.zero;
		[DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Active, typeof(VaporLightShaft))]
		static void DrawLightShaftGizmo(VaporLightShaft lightShaft, GizmoType type) {
			Gizmos.matrix = lightShaft.transform.localToWorldMatrix;
            lightShaftGizmoPosition.z = lightShaft.Size.z / 2f;
            Gizmos.DrawWireCube(lightShaftGizmoPosition, lightShaft.Size);
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			SettingsField("m_setting", "Physical properties of the fog in this light shaft", ref m_settingEditor);
			PropertyField("Size", "");
            PropertyField("Intensity", "Light Shaft Intensity");
            PropertyField("m_light", "Light Component");
            PropertyField("SpotBaseSize", "Base of the Spotlight");
            PropertyField("ShadowValue", "Shadow Map Multiplier");
            PropertyField("CustomShadowMap", "Custom Map/Texture");

            serializedObject.ApplyModifiedProperties();
		}

		void OnDisable() {
			if (m_settingEditor != null) {
				DestroyImmediate(m_settingEditor);
			}
		}
	}
}