using System.Linq;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace Vapor {
	public class VaporBaseEditor : Editor {
		public void PropertyField(string propName) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
				return;
			}
			EditorGUILayout.PropertyField(prop, true);
		}

		public void PropertyField(string propName, string label) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
				return;
			}
			EditorGUILayout.PropertyField(prop, new GUIContent(label), true, null);
		}


		public void PropertyField(string propName, string label, params GUILayoutOption[] options) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
			}

			EditorGUILayout.PropertyField(prop, new GUIContent(label), true, options);
		}

	}


	[CustomEditor(typeof(VaporSetting))]
	[CanEditMultipleObjects]
	public class VaporSettingEditor : VaporBaseEditor {
	
	}

	[CustomEditor(typeof (Vapor))]
    public class VaporEditor : VaporBaseEditor {
	    private Editor m_settingEditor;

		public enum VisualizeMode {
			None,
			Layers,
			Total
		}


		private static Material s_noiseVisualizeMaterial;
		private static Mesh s_planeMesh;
		private static VisualizeMode s_visualizeMode;

		private static Color s_base = new Color(126 / 255.0f, 41 / 255.0f, 41 / 255.0f);
		private static Color s_secondary = new Color(126 / 255.0f, 66 / 255.0f, 41 / 255.0f);
		private static Color s_detail = new Color(57 / 255.0f, 126 / 255.0f, 41 / 255.0f);


		private const float c_period = 9.0f;
		private const float c_fade = 1.0f;
		private const string c_baseLayerName = "Base Layer";
		private const string c_secondaryLayerName = "Secondary Layer";
		private const string c_detailLayerName = "Detail Layer";

		private AnimBool m_baseAnim = new AnimBool();
		private AnimBool m_secondaryAnim = new AnimBool();
		private AnimBool m_detailAnim = new AnimBool();

	
		private void OnEnable() {
			CreateSettingsEditor();
			var tab = VaporTabGroup.GetTabGroup();
			m_baseAnim.value = tab.IsOpen(c_baseLayerName);
			m_secondaryAnim.value = tab.IsOpen(c_secondaryLayerName);
			m_detailAnim.value = tab.IsOpen(c_detailLayerName);
		}

		private void CreateSettingsEditor() {
			if (m_settingEditor != null) {
				DestroyImmediate(m_settingEditor);
			}
			m_settingEditor = CreateEditor(targets.Select(t => (t as Vapor).Setting).ToArray());
		}

		private void OnDisable() {
			s_visualizeMode = VisualizeMode.None;
			DestroyImmediate(m_settingEditor);
        }

	    public override bool RequiresConstantRepaint() {
		    return true;
	    }


		private bool NoiseFields(string layerName) {
			EditorGUI.BeginChangeCheck();
			PropertyField(layerName + ".Frequency");
			PropertyField(layerName + ".Persistence");
			PropertyField(layerName + ".Lacunarity");
			PropertyField(layerName + ".PerlinOctaves");
			PropertyField(layerName + ".Seed");
			bool change = EditorGUI.EndChangeCheck();

			PropertyField(layerName + ".ScrollSpeed");
			PropertyField(layerName + ".Scale");
			PropertyField(layerName + ".Strength");

			return change;
		}

		[DrawGizmo(GizmoType.Selected)]
		private static void RenderNoiseLayers(Vapor vapor, GizmoType gizmoType) {
			if (s_visualizeMode == VisualizeMode.None) {
				return;
			}

			if (s_noiseVisualizeMaterial == null) {
				s_noiseVisualizeMaterial = new Material(Shader.Find("Hidden/VaporNoiseVisualize"));
				var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
				s_planeMesh = go.GetComponent<MeshFilter>().sharedMesh;
				DestroyImmediate(go);
			}

			Vector3 sc1 = vapor.GetNoiseLayer(0).SetScale;
			Vector3 sc2 = vapor.GetNoiseLayer(1).SetScale;
			Vector3 sc3 = vapor.GetNoiseLayer(2).SetScale;

			Vector3 scroll1 = vapor.GetNoiseLayer(0).ScrollSpeed;
			Vector3 scroll2 = vapor.GetNoiseLayer(1).ScrollSpeed;
			Vector3 scroll3 = vapor.GetNoiseLayer(2).ScrollSpeed;

			float str1 = vapor.GetNoiseLayer(0).Strength;
			float str2 = vapor.GetNoiseLayer(2).Strength;
			float str3 = vapor.GetNoiseLayer(2).Strength;
			var position = vapor.transform.position;

			if (s_visualizeMode == VisualizeMode.Layers) {
				float time = (float)EditorApplication.timeSinceStartup;


				time = Mathf.Repeat(time, c_period);
				float alph;
				if (time < c_fade) {
					alph = Mathf.SmoothStep(0, 1, time / c_fade);
				} else if (time > c_period - c_fade) {
					alph = Mathf.SmoothStep(1, 0, (time - (c_period - c_fade)) / c_fade);
				} else {
					alph = 1.0f;
				}

				Gizmos.color = new Color(0.2f, 0.3f, 0.6f, alph * 0.4f * str1);
				Gizmos.DrawWireCube(position - scroll1 * time, sc1);

				Gizmos.color = new Color(0.2f, 0.3f, 0.6f, alph * 0.4f * str2);
				Gizmos.DrawWireCube(position - scroll2 * time, sc2);

				Gizmos.color = new Color(0.2f, 0.3f, 0.6f, alph * 0.4f * str3);
				Gizmos.DrawWireCube(position - scroll3 * time, sc3);

				Gizmos.color = new Color(0.1f, 0.15f, 0.5f, alph * 0.2f);
				Gizmos.DrawCube(position - scroll1 * time, sc1);
				Gizmos.DrawCube(position - scroll2 * time, sc2);
				Gizmos.DrawCube(position - scroll3 * time, sc3);

				DrawNoiseVisualize(vapor.GetNoiseLayer(0), position - scroll1 * time);
				DrawNoiseVisualize(vapor.GetNoiseLayer(1), position - scroll2 * time);
				DrawNoiseVisualize(vapor.GetNoiseLayer(2), position - scroll3 * time);
			} else if (s_visualizeMode == VisualizeMode.Total) {
				for (int i = 0; i < 3; ++i) {
					s_noiseVisualizeMaterial.SetTexture("_NoiseTex" + i, vapor.GetNoiseLayer(i).NoiseTexture);
					s_noiseVisualizeMaterial.SetVector("_NoiseScale" + i, vapor.GetNoiseLayer(i).SetInvScale);

					float time = (float)EditorApplication.timeSinceStartup;
					s_noiseVisualizeMaterial.SetVector("_NoiseScroll" + i, vapor.GetNoiseLayer(i).SetScaledScrollSpeed * time);
				}

				s_noiseVisualizeMaterial.SetVector("_NoiseStrength",
				new Vector4(vapor.GetNoiseLayer(0).Strength, vapor.GetNoiseLayer(1).Strength, vapor.GetNoiseLayer(2).Strength));

				s_noiseVisualizeMaterial.SetPass(1);
				Graphics.DrawMeshNow(s_planeMesh, Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * 100.0f));
			}

			Gizmos.color = Color.white;
		}

		private static void DrawNoiseVisualize(NoiseLayer vapor, Vector3 position) {
			s_noiseVisualizeMaterial.SetTexture("_NoiseTex0", vapor.NoiseTexture);
			s_noiseVisualizeMaterial.SetColor("_Color", Gizmos.color);
			s_noiseVisualizeMaterial.SetPass(0);

			Graphics.DrawMeshNow(s_planeMesh, Matrix4x4.TRS(position, Quaternion.identity, vapor.SetScale));
		}


		public override void OnInspectorGUI() {
			serializedObject.Update();

			EditorGUI.BeginChangeCheck();
			PropertyField("m_setting");
			if (EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
				CreateSettingsEditor();
				serializedObject.Update();
			}

			EditorGUILayout.BeginVertical("Box");
			m_settingEditor.OnInspectorGUI();
			EditorGUILayout.EndVertical();


			PropertyField("Phase");

			EditorGUILayout.Space();

            PropertyField("ShadowHardness");
            PropertyField("ShadowBias");
            PropertyField("AveragingSpeed");
            PropertyField("TemporalStrength");
            PropertyField("BlurSize");
			
          
            serializedObject.ApplyModifiedProperties();

			var tab = VaporTabGroup.GetTabGroup();
			GUILayout.Label("Noise", EditorStyles.boldLabel);

			bool removed;
			bool noiseDirty = false;
			m_baseAnim.target = tab.TabArea(c_baseLayerName, s_base, false, out removed);
			using (var group = new EditorGUILayout.FadeGroupScope(m_baseAnim.faded)) {
				if (group.visible) {
					noiseDirty |= NoiseFields("m_baseLayer");
				}
			}

			m_secondaryAnim.target = tab.TabArea(c_secondaryLayerName, s_secondary, false, out removed);
			using (var group = new EditorGUILayout.FadeGroupScope(m_secondaryAnim.faded)) {
				if (group.visible) {
					noiseDirty |= NoiseFields("m_secondaryLayer");
				}
			}

			m_detailAnim.target = tab.TabArea(c_detailLayerName, s_detail, false, out removed);
			using (var group = new EditorGUILayout.FadeGroupScope(m_detailAnim.faded)) {
				if (group.visible) {
					noiseDirty |= NoiseFields("m_detailLayer");
				}
			}

			s_visualizeMode = (VisualizeMode)EditorGUILayout.EnumPopup("Visualize Mode", s_visualizeMode);

			serializedObject.ApplyModifiedProperties();

			//Temporal needs to repaint game view
			UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

			if (GUILayout.Button("Rebake noise", EditorStyles.toolbarButton)) {
				foreach (Vapor targ in targets) {
					targ.BakeNoiseLayers();
				}
			}
		}

	}
}






/*
if (GUI.changed) {
	for (int index = 0; index < targets.Length; index++) {
		var o = targets[index];
		var t = (Vapor2) o;

		t.UpdateGradientTex();
	}
}
*/

/*
if (tab.Foldout("Gradients", "Gradients", EditorStyles.boldLabel, GUILayout.Width(18.0f))) {
	//GUILayout.Label("Gradients", EditorStyles.boldLabel);
	PropertyField("HeightGradient.End", "", GUILayout.Width(28.0f));

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

		EditorGUI.PropertyField(new Rect(rect.xMin, rect.yMin, rect.height, rect.width),
			serializedObject.FindProperty("HeightGradient.Gradient"), new GUIContent(), true);

		rect = GUILayoutUtility.GetRect(0, float.MaxValue, 0, 120);
		rect.xMax -= 25.0f;

		GUI.matrix = Matrix4x4.identity;
		GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, rect.height),
			(target as Vapor).GradientTex);
	}

	PropertyField("HeightGradient.Start", "", GUILayout.Width(28.0f));

	using (new EditorGUILayout.HorizontalScope()) {
		PropertyField("DistanceGradient.Start", "", GUILayout.Width(28.0f));
		PropertyField("DistanceGradient.Gradient", "");
		PropertyField("DistanceGradient.End", "", GUILayout.Width(28.0f));
	}
}
else {
	GUILayout.Label("Height gradient");
	using (new EditorGUILayout.HorizontalScope()) {

		PropertyField("HeightGradient.Start", "", GUILayout.Width(28.0f));
		PropertyField("HeightGradient.Gradient", "");
		PropertyField("HeightGradient.End", "", GUILayout.Width(28.0f));
	}

	GUILayout.Label("Distance gradient");

	using (new EditorGUILayout.HorizontalScope()) {
		PropertyField("DistanceGradient.Start", "", GUILayout.Width(28.0f));
		PropertyField("DistanceGradient.Gradient", "");
		PropertyField("DistanceGradient.End", "", GUILayout.Width(28.0f));
	}
}
*/