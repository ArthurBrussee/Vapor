using UnityEditor;
using UnityEngine;

namespace VaporAPI {
	[CustomEditor(typeof(VaporZone))]
	[CanEditMultipleObjects]
	public class VaporZoneEditor : VaporBaseEditor {
		Editor m_settingEditor;

		[DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Active, typeof(VaporZone))]
		static void DrawZoneGizmo(VaporZone zone, GizmoType type) {
			Gizmos.matrix = zone.transform.localToWorldMatrix;
			Gizmos.DrawWireCube(Vector3.zero, zone.Size);
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			SettingsField("m_setting", "Physical properties of the fog in this zone", ref m_settingEditor);
			PropertyField("Size", "");
			PropertyField("Radius", "Softening radius");

			serializedObject.ApplyModifiedProperties();
		}

		void OnDisable() {
			if (m_settingEditor != null) {
				DestroyImmediate(m_settingEditor);
			}
		}
	}
}