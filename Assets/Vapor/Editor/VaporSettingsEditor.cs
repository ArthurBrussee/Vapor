using UnityEditor;

[CustomEditor(typeof(VaporSetting))]
[CanEditMultipleObjects]
public class VaporSettingsEditor : Editor {
	private static readonly string[] DontIncludeMe = {"m_Script"};

	public override void OnInspectorGUI() {
		serializedObject.Update();
		DrawPropertiesExcluding(serializedObject, DontIncludeMe);
		serializedObject.ApplyModifiedProperties();
	}
}
