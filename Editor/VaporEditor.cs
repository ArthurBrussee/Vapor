using System;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace VaporAPI {
	public class VaporBaseEditor : Editor {


		public void SettingsField(string propName, string tooltip, ref Editor editor) {
			var prop = serializedObject.FindProperty(propName);

			if (prop == null) {
				Debug.LogError(propName);
				return;
			}

			if (prop.objectReferenceValue == null) {
				prop.objectReferenceValue = Vapor.DefaultSetting;
			}

			if (editor == null) {
				editor = CreateEditor(prop.objectReferenceValue);
			}

			using (new GUILayout.HorizontalScope()) {
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(prop, new GUIContent(prop.displayName, tooltip));
				if (EditorGUI.EndChangeCheck()) {
					serializedObject.ApplyModifiedProperties();
					DestroyImmediate(editor);
					editor = CreateEditor(prop.objectReferenceValue);

					if (target is Vapor) {
						(target as Vapor).MarkInstantRender();
					}

					serializedObject.Update();
				}

				var rect = GUILayoutUtility.GetLastRect();
				rect.width = 18.0f;
				prop.isExpanded = EditorGUI.Foldout(rect, prop.isExpanded, "");
			}

			if (prop.isExpanded) {
				EditorGUILayout.BeginVertical("Box");
				editor.OnInspectorGUI();
				EditorGUILayout.EndVertical();
			}
		}

		public void PropertyField(string propName, string tooltip) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
				return;
			}

			EditorGUILayout.PropertyField(prop, new GUIContent(prop.displayName, tooltip), true);
		}

		public void PropertyField(string propName, string label, string tooltip) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
				return;
			}
			EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip), true, null);
		}

		public void PropertyField(string propName, string label, params GUILayoutOption[] options) {
			var prop = serializedObject.FindProperty(propName);
			if (prop == null) {
				Debug.LogError(propName);
			}

			EditorGUILayout.PropertyField(prop, new GUIContent(label), true, options);
		}
	}

	[CustomEditor(typeof(Vapor))]
	public class VaporEditor : VaporBaseEditor {
		Editor m_settingEditor;
		Vapor.QualitySetting m_quality;

		VaporTabGroup m_group;


		AnimBool[] m_animBools = new AnimBool[6];



		void OnEnable() {
			m_group = VaporTabGroup.GetTabGroup();

			var xyMult = serializedObject.FindProperty("GlobalResolutionMult");
			var zMult = serializedObject.FindProperty("DepthResolutionMult");

			float xy = xyMult.floatValue;
			float z = zMult.floatValue;

			if (xyMult.hasMultipleDifferentValues || zMult.hasMultipleDifferentValues) {
				m_quality = Vapor.QualitySetting.Custom;
			}
			else if (Mathf.Approximately(xy, 0.8f) && Mathf.Approximately(z, 0.8f)) {
				m_quality = Vapor.QualitySetting.Low;
			}
			else if (Mathf.Approximately(xy, 1.0f) && Mathf.Approximately(z, 1.0f)) {
				m_quality = Vapor.QualitySetting.Medium;
			}
			else if (Mathf.Approximately(xy, 1.2f) && Mathf.Approximately(z, 1.2f)) {
				m_quality = Vapor.QualitySetting.High;
			}
			else {
				m_quality = Vapor.QualitySetting.Custom;
			}
		}

		void OnDisable() {
			if (m_settingEditor != null) {
				DestroyImmediate(m_settingEditor);
			}
		}

		public override bool RequiresConstantRepaint() {
			return true;
		}

		public Color GetTabColor(int index) {
			return Color.HSVToRGB((index / 18.0f) * 0.8f, 0.75f, 0.5f);

		}

		int m_tabIndex;

		bool Tab(string tabName) {
			if (m_animBools[m_tabIndex] == null) {
				m_animBools[m_tabIndex] = new AnimBool(m_group.IsOpen(tabName));
				m_animBools[m_tabIndex].speed *= 3.0f;
			}

			m_animBools[m_tabIndex].target = m_group.TabArea(tabName, GetTabColor(m_tabIndex));

			bool ret = EditorGUILayout.BeginFadeGroup(m_animBools[m_tabIndex].faded);
			m_tabIndex++;
			return ret;
		}

		void EndTab() {
			EditorGUILayout.EndFadeGroup();
		}


		public override void OnInspectorGUI() {
			serializedObject.Update();
			m_tabIndex = 0;

			var vap = target as Vapor;

			if (Tab("Base Setting")) {
				SettingsField("m_setting", "Physical properties of fog", ref m_settingEditor);
			}
			EndTab();

			if (Tab("Scattering Settings")) {
				PropertyField("ScatteringIntensity", "Intensity of scattering that causes the sky color");

				using (new EditorGUILayout.HorizontalScope()) {
					PropertyField("ScatteringColor", "Color of the sky caused by scattering. Default value is physical truth. Other values break physicality but can be used to create alien skies.");
					if (GUILayout.Button(new GUIContent("↺", "Reset to physical value"), GUILayout.Width(30.0f))) {
						serializedObject.FindProperty("ScatteringColor").colorValue = Vapor.DefaultScatteringColor;
					}
				}
				PropertyField("DirectionalScattering", "The directionality of the scattering - creates the appearance of a 'sun'");
				PropertyField("DirectionalScatteringColor", "The color of the directional scattering (color of the sun, multiplicative");

				PropertyField("AtmosphereThickness", "KM of Atmosphere on the planet. A thinner atmosphere causes a stronger 'sunset' effect.");
			}
			EndTab();

			if (Tab("Noise Settings")) {
				PropertyField("NoiseColorStrength", "Amount the noise influences the albedo of the fog");
				PropertyField("NoiseExtinctionStrength", "Amount the noise influences the extinction of the fog");
				PropertyField("NoiseWeights", "Weights of the different noise layers");
				PropertyField("NoiseFrequency", "Frequencies of the different noise layers");
				PropertyField("NoiseSpeed", "Movement speed of the noise");
				PropertyField("NoisePower", "'Sharpness' of the noise (low = soft, high = sharp");
			}
			EndTab();

			if (Tab("Quality Settings")) {
				using (var change = new EditorGUI.ChangeCheckScope()) {
					m_quality = (Vapor.QualitySetting) EditorGUILayout.EnumPopup("Quality", m_quality);

					if (change.changed) {
						var xyMult = serializedObject.FindProperty("GlobalResolutionMult");
						var zMult = serializedObject.FindProperty("DepthResolutionMult");

						switch (m_quality) {
							case Vapor.QualitySetting.Low:
								xyMult.floatValue = 0.8f;
								zMult.floatValue = 0.8f;
								break;

							case Vapor.QualitySetting.Medium:
								xyMult.floatValue = 1.0f;
								zMult.floatValue = 1.0f;
								break;

							case Vapor.QualitySetting.High:
								xyMult.floatValue = 1.2f;
								zMult.floatValue = 1.2f;
								break;
						}
					}

					if (m_quality == Vapor.QualitySetting.Custom) {
						PropertyField("GlobalResolutionMult", "Pixels to use in the z direction");
						PropertyField("DepthResolutionMult", "Pixels to use in the z direction");
					}

					int horizRes = vap.HorizontalRes;
					int vertRes = vap.VerticalRes;
					int depthRes = vap.DepthRes;
					int total = horizRes * vertRes * depthRes;

					GUILayout.Label("Res: " + horizRes + ", " + vertRes + ", " + depthRes + ", Froxels: " + total + " VRAM: " +
					                Mathf.CeilToInt(total * Vapor.BytesPerFroxel / (1024.0f * 1024.0f)) + "MB");
				}
			}
			EndTab();

			if (Tab("Advanced")) {
				PropertyField("DisplayInSceneView", "Enable/Disable Vapor in the Scene View");

				PropertyField("TemporalStrength", "Strength of jitter to be applied for the temporal anti aliasing");
				PropertyField("AveragingSpeed", "Temporal integration speed");

				PropertyField("AtmosphereRingPower", "Sharpness of atmosphere ring around the sun");
				PropertyField("AtmosphereRingSize", "Size of the atmosphere ring around the sun");

				PropertyField("DepthCurvePower", "Distribution of voxels. Lower -> more voxels far away, higher -> more voxels nearby");
			}
			EndTab();


			serializedObject.ApplyModifiedProperties();

		}
	}
}