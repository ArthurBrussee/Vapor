using UnityEditor;
using UnityEngine;

namespace VaporAPI {
	[CustomEditor(typeof(VaporSetting))]
	[CanEditMultipleObjects]
	public class VaporSettingsEditor : VaporBaseEditor {
		static readonly string[] DontIncludeMe = {"m_Script", "HeightGradient", "DistanceGradient"};

		void OnEnable() {
			Undo.undoRedoPerformed += BakeGradients;
		}

		void OnDisable() {
			Undo.undoRedoPerformed -= BakeGradients;
		}

		void BakeGradients() {
			foreach (VaporSetting vap in targets) {
				vap.UpdateGradients();
			}
		}

		void GradientField(string prop) {
			EditorGUI.BeginChangeCheck();
			PropertyField(prop, "");
			if (EditorGUI.EndChangeCheck()) {
				BakeGradients();
			}
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, DontIncludeMe);

			var tab = VaporTabGroup.GetTabGroup();

			if (tab.Foldout("Albedo (RGB) Extinction (A) Gradient", "Gradients", EditorStyles.boldLabel, GUILayout.Width(18.0f))) {
				float w = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 35.0f;
				PropertyField("HeightGradient.End", "End", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));

				using (new EditorGUILayout.HorizontalScope()) {
					Rect rect;
					using (new EditorGUILayout.VerticalScope()) {
						//Just for fill
						GUILayoutUtility.GetRect(30.0f, 100.0f);
						rect = GUILayoutUtility.GetRect(30.0f, 20.0f);
						rect.height = 140.0f;
						rect.yMin += 20;
					}


					GUIUtility.RotateAroundPivot(-90.0f, new Vector2(rect.xMin, rect.yMin));

					EditorGUI.BeginChangeCheck();
					EditorGUI.PropertyField(new Rect(rect.xMin, rect.yMin, rect.height, rect.width),
						serializedObject.FindProperty("HeightGradient.Gradient"), new GUIContent(), true);
					if (EditorGUI.EndChangeCheck()) {
						BakeGradients();
					}

					rect = GUILayoutUtility.GetRect(0, float.MaxValue, 0, 120);
					rect.xMax -= 25.0f;

					GUI.matrix = Matrix4x4.identity;
					GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, rect.height), (target as VaporSetting).GradientTex);
				}

				using (new EditorGUILayout.HorizontalScope()) {
					PropertyField("HeightGradient.Start", "Start", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));
					GUILayout.FlexibleSpace();
					GUILayout.Label("End");
				}

				using (new EditorGUILayout.HorizontalScope()) {
					PropertyField("DistanceGradient.Start", "", GUILayout.Width(28.0f));
					GradientField("DistanceGradient.Gradient");
					PropertyField("DistanceGradient.End", "", GUILayout.Width(28.0f));
				}

				EditorGUIUtility.labelWidth = w;
			}
			else {
				float w = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 35.0f;

				GUILayout.Label("Height gradient");
				using (new EditorGUILayout.HorizontalScope()) {
					PropertyField("HeightGradient.Start", "Start", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));
					GradientField("HeightGradient.Gradient");
					PropertyField("HeightGradient.End", "End", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));
				}

				GUILayout.Label("Distance gradient");

				using (new EditorGUILayout.HorizontalScope()) {
					PropertyField("DistanceGradient.Start", "Start", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));
					GradientField("DistanceGradient.Gradient");
					PropertyField("DistanceGradient.End", "End", GUILayout.Width(EditorGUIUtility.labelWidth + 28.0f));
				}

				EditorGUIUtility.labelWidth = w;
			}


			serializedObject.ApplyModifiedProperties();
		}
	}
}