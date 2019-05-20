using UnityEditor;
using UnityEngine;

namespace VaporAPI {
	[CustomEditor(typeof(VaporCustomLight))]
	[CanEditMultipleObjects]
	public class VaporCustomLightEditor : VaporBaseEditor {
		Editor m_settingEditor;
		static Vector3 customLightGizmoPosition = Vector3.zero;

		[DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Active, typeof(VaporCustomLight))]
		static void DrawCustomLightGizmo(VaporCustomLight customLight, GizmoType type) {
			Gizmos.matrix = customLight.transform.localToWorldMatrix;
			customLightGizmoPosition.z = customLight.Size.z / 2f;
			Gizmos.DrawWireCube(customLightGizmoPosition, customLight.Size);
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			SettingsField("m_setting", "Physical properties of the fog in this light (Shaft)", ref m_settingEditor);
			PropertyField("Size", "");
			PropertyField("Intensity", "Light(Shaft) Intensity");
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