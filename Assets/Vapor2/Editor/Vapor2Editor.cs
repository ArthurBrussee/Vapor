using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace Vapor {



    [CustomEditor(typeof (Vapor2))]
    public class Vapor2Editor : Editor {

        private void OnDisable() {
            s_visualizeMode = VisualizeMode.None;
        }

        public enum VisualizeMode {
            None,
            Layers,
            Total
        }

        private void PropertyField(string propName) {
            var prop = serializedObject.FindProperty(propName);
            if (prop == null) {
                Debug.LogError(propName);
                return;
            }
            EditorGUILayout.PropertyField(prop, true);
        }


        private void PropertyField(string propName, string label) {
            var prop = serializedObject.FindProperty(propName);
            if (prop == null) {
                Debug.LogError(propName);
                return;
               }
            EditorGUILayout.PropertyField(prop, new GUIContent(label), true, null);
        }


        private void PropertyField(string propName, string label, params GUILayoutOption[] options) {
            var prop = serializedObject.FindProperty(propName);
            if (prop == null) {
                Debug.LogError(propName);
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label), true, options);
        }

        private const float c_period = 9.0f;
        private const float c_fade = 1.0f;
        private const string c_baseLayerName = "Base Layer";
        private const string c_secondaryLayerName = "Secondary Layer";
        private const string c_detailLayerName = "Detail Layer";

        private static Material s_noiseVisualizeMaterial;
        private static Mesh s_planeMesh;
        private static VisualizeMode s_visualizeMode;

        private AnimBool m_baseAnim = new AnimBool();
        private AnimBool m_secondaryAnim = new AnimBool();
        private AnimBool m_detailAnim = new AnimBool();

        private void OnEnable() {
            var tab = VaporTabGroup.GetTabGroup();
            m_baseAnim.value = tab.IsOpen(c_baseLayerName);
            m_secondaryAnim.value = tab.IsOpen(c_secondaryLayerName);
            m_detailAnim.value = tab.IsOpen(c_detailLayerName);
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void RenderCustomLightGizmo(Vapor2 vapor, GizmoType gizmoType) {
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
                float time = (float) EditorApplication.timeSinceStartup;


                time = Mathf.Repeat(time, c_period);
                float alph;
                if (time < c_fade) {
                    alph = Mathf.SmoothStep(0, 1, time / c_fade);
                }
                else if (time > c_period - c_fade) {
                    alph = Mathf.SmoothStep(1, 0, (time - (c_period - c_fade)) / c_fade);
                }
                else {
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
            }
            else if (s_visualizeMode == VisualizeMode.Total) {
                for (int i = 0; i < 3; ++i) {
                    s_noiseVisualizeMaterial.SetTexture("_NoiseTex" + i, vapor.GetNoiseLayer(i).NoiseTexture);
                    s_noiseVisualizeMaterial.SetVector("_NoiseScale" + i, vapor.GetNoiseLayer(i).SetInvScale);

                    float time = (float) EditorApplication.timeSinceStartup;
                    s_noiseVisualizeMaterial.SetVector("_NoiseScroll" + i, vapor.GetNoiseLayer(i).SetScaledScrollSpeed * time);
                }

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

        private static Color s_base = new Color(126 / 255.0f, 41 / 255.0f, 41 / 255.0f);
        private static Color s_secondary = new Color(126 / 255.0f, 66 / 255.0f, 41 / 255.0f);
        private static Color s_detail = new Color(57 / 255.0f, 126 / 255.0f, 41 / 255.0f);


	    public override bool RequiresConstantRepaint() {
		    return true;
	    }

	    public override void OnInspectorGUI() {
            serializedObject.Update();

            PropertyField("FogDensity");
            PropertyField("InscatterIntensity");
            PropertyField("Anisotropy");
            PropertyField("AmbientLight");
            PropertyField("AmbientIntensity");
            PropertyField("Sun");

            
            var tab = VaporTabGroup.GetTabGroup();
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




            GUILayout.Label("Noise", EditorStyles.boldLabel);
            //TODO: Animate!
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


            s_visualizeMode = (VisualizeMode) EditorGUILayout.EnumPopup("Visualize Mode", s_visualizeMode);


            GUI.enabled = false;
            foreach (Vapor2 targ in targets) {
                if (targ.NeedsRebake) {
                    GUI.enabled = true;
                    break;
                }
            }
            if (GUILayout.Button("Rebake noise", EditorStyles.toolbarButton)) {
                foreach (Vapor2 targ in targets) {
                    if (targ.NeedsRebake) {
                        targ.BakeNoiseLayers();
                    }
                }
            }
            GUI.enabled = true;

            //PropertyField("MaskStrength");
            //PropertyField("BlurSize");
            
            serializedObject.ApplyModifiedProperties();


            if (noiseDirty) {

                EditorApplication.delayCall += () => {
                                                   foreach (Vapor2 targ in targets) {
                                                       targ.NeedsRebake = true;
                                                       EditorUtility.SetDirty(targ);
                                                   }
                                               };
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

            if (m_baseAnim.isAnimating || m_detailAnim.isAnimating || m_secondaryAnim.isAnimating) {
                Repaint();
            }

		    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
	    }

        private bool NoiseFields(string layerName) {
            EditorGUI.BeginChangeCheck();

            PropertyField(layerName + ".Frequency");
            PropertyField(layerName + ".Persistence");
            PropertyField(layerName + ".Lacunarity");
            PropertyField(layerName + ".Octaves");
            PropertyField(layerName + ".Seed");

            bool change = EditorGUI.EndChangeCheck();

            PropertyField(layerName + ".ScrollSpeed");
            PropertyField(layerName + ".Scale");
            PropertyField(layerName + ".Strength");

            return change;
        }
    }
}