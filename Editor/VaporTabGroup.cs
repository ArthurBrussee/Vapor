using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[Serializable]
public class VaporTabGroup : ScriptableObject {
	[SerializeField] List<bool> m_open;
	[SerializeField] List<string> m_names;
	Action<Rect> m_defaultTabFunction = r => GUI.Label(r, "-", EditorStyles.whiteLabel);

	public static VaporTabGroup GetTabGroup() {
		var o = Resources.FindObjectsOfTypeAll<VaporTabGroup>();
		VaporTabGroup tab;

		if (o.Length != 0) {
			tab = o[0];
		}
		else {
			tab = CreateInstance<VaporTabGroup>();
			tab.hideFlags = HideFlags.HideAndDontSave;
			tab.name = "AlloyTabGroup";
		}

		return tab;
	}

	void OnEnable() {
		if (m_open != null && m_names != null) {
			return;
		}

		m_open = new List<bool>();
		m_names = new List<string>();
	}

	int DeclOpen(string nameDecl) {
		string actual = nameDecl + GUI.depth;

		if (!m_names.Contains(actual)) {
			m_open.Add(false);
			m_names.Add(actual);
		}

		return m_names.IndexOf(actual);
	}

	public bool TabArea(string areaName, Color color, string saveAs = "") {
		bool removed;
		return TabArea(areaName, color, false, m_defaultTabFunction, out removed, saveAs);
	}

	public bool TabArea(string areaName,
		Color color,
		bool hasOptionalGui,
		Action<Rect> optionalGUI,
		out bool removed,
		string saveAs = "") {
		if (saveAs == "") {
			saveAs = areaName;
		}

		Color oldGuiColor = GUI.color;
		Color oldBackgroundColor = GUI.backgroundColor;

		GUI.color = Color.Lerp(color, Color.white, 0.8f);
		GUI.backgroundColor = color;

		bool ret = TabArea(areaName, hasOptionalGui, optionalGUI, out removed, saveAs);
		GUI.color = oldGuiColor;
		GUI.backgroundColor = oldBackgroundColor;

		return ret;
	}

	public bool TabArea(string areaName, bool hasOptionalGui, out bool removed, string saveAs = "") {
		return TabArea(areaName, hasOptionalGui, m_defaultTabFunction, out removed, saveAs);
	}

	public bool TabArea(string areaName,
		bool hasOptionalGui,
		Action<Rect> optionalGUI,
		out bool removed,
		string saveAs = "") {
		if (saveAs == "") {
			saveAs = areaName;
		}

		int i = DeclOpen(saveAs);
		var tabTextColor = EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.9f, 0.9f, 0.9f);
		var oldCol = GUI.color;
		GUI.color = oldCol * (m_open[i] ? Color.white : new Color(0.8f, 0.8f, 0.8f));

		GUILayout.Label("");

		var rect = GUILayoutUtility.GetLastRect();
		rect.width += hasOptionalGui ? 0.0f : 50.0f;
		rect.x -= 35.0f;

		m_open[i] = GUI.Toggle(rect, m_open[i], new GUIContent(""), "ShurikenModuleTitle");
		removed = false;

		if (hasOptionalGui) {
			var delRect = rect;
			delRect.xMin = rect.xMax;
			delRect.xMax += 40.0f;

			GUI.color = oldCol * (m_open[i] ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.5f, 0.5f, 0.5f));

			if (GUI.Button(delRect, "", "ShurikenModuleTitle")) {
				removed = true;
			}

			GUI.color = tabTextColor;
			GUI.backgroundColor = Color.white;
			delRect.x += 10.0f;
			optionalGUI(delRect);
		}

		rect.x += 35.0f;
		GUI.color = tabTextColor;
		GUI.Label(rect, areaName, EditorStyles.whiteLabel);
		GUI.color = oldCol;

		if (GUI.changed) {
			EditorUtility.SetDirty(this);
		}

		return m_open[i];
	}

	public bool Foldout(string areaName, string saveName, params GUILayoutOption[] options) {
		int i = DeclOpen(saveName);

		EditorGUILayout.BeginHorizontal();
		m_open[i] = EditorGUILayout.Toggle(new GUIContent(""), m_open[i], "foldout", options);

		if (areaName != "") {
			EditorGUILayout.LabelField(new GUIContent(areaName), GUILayout.ExpandWidth(false), GUILayout.Width(180.0f));
		}

		EditorGUILayout.EndHorizontal();

		if (GUI.changed) {
			EditorUtility.SetDirty(this);
		}

		return m_open[i];
	}

	public bool Foldout(string areaName, string saveName, GUIStyle labelStyle, params GUILayoutOption[] options) {
		int i = DeclOpen(saveName);

		EditorGUILayout.BeginHorizontal();
		m_open[i] = EditorGUILayout.Toggle(new GUIContent(""), m_open[i], "foldout", options);

		if (areaName != "") {
			EditorGUILayout.LabelField(new GUIContent(areaName), labelStyle);
		}

		EditorGUILayout.EndHorizontal();

		if (GUI.changed) {
			EditorUtility.SetDirty(this);
		}

		return m_open[i];
	}

	public bool IsOpen(string areaName) {
		int i = DeclOpen(areaName);
		return m_open[i];
	}

	public void SetOpen(string areaName, bool open) {
		int i = DeclOpen(areaName);
		m_open[i] = open;
	}

	public void Close(string areaName) {
		int i = DeclOpen(areaName);
		m_open[i] = false;
	}
}