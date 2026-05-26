using UnityEditor;
using UnityEngine;
using ACLS.Data;

namespace ACLS.Editor
{
    [CustomEditor(typeof(LlmConfig))]
    public sealed class LlmConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty profilesProp;

        private static readonly string[] FieldNames =
        {
            "ProfileName", "IsActive", "Provider", "ApiKey",
            "Model", "BaseUrl", "MaxTokens", "VerboseLogging",
        };

        private void OnEnable()
        {
            profilesProp = serializedObject.FindProperty("Profiles");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            int count = profilesProp.arraySize;

            // --- draw each profile manually so we can intercept IsActive ---
            for (int i = 0; i < count; i++)
            {
                var profile    = profilesProp.GetArrayElementAtIndex(i);
                var isActiveProp   = profile.FindPropertyRelative("IsActive");
                var profileNameProp = profile.FindPropertyRelative("ProfileName");

                string label = string.IsNullOrWhiteSpace(profileNameProp?.stringValue)
                    ? $"Profile {i}"
                    : profileNameProp.stringValue;
                if (isActiveProp.boolValue) label = "▶ " + label;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // foldout header with remove button on the right
                EditorGUILayout.BeginHorizontal();
                profile.isExpanded = EditorGUILayout.Foldout(profile.isExpanded, label, toggleOnLabelClick: true);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    profilesProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (profile.isExpanded)
                {
                    EditorGUI.indentLevel++;

                    foreach (var fieldName in FieldNames)
                    {
                        var prop = profile.FindPropertyRelative(fieldName);
                        if (prop == null) continue;

                        if (fieldName == "IsActive")
                        {
                            // Intercept: toggling on clears all others.
                            bool wasActive = prop.boolValue;
                            EditorGUI.BeginChangeCheck();
                            bool nowActive = EditorGUILayout.Toggle("激活", wasActive);
                            if (EditorGUI.EndChangeCheck())
                            {
                                if (nowActive && !wasActive)
                                {
                                    for (int j = 0; j < profilesProp.arraySize; j++)
                                    {
                                        if (j != i)
                                            profilesProp.GetArrayElementAtIndex(j)
                                                        .FindPropertyRelative("IsActive").boolValue = false;
                                    }
                                }
                                prop.boolValue = nowActive;
                            }
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(prop);
                        }
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            // --- add button ---
            if (GUILayout.Button("＋ Add Profile"))
            {
                profilesProp.arraySize++;
                var newProfile = profilesProp.GetArrayElementAtIndex(profilesProp.arraySize - 1);
                newProfile.FindPropertyRelative("ProfileName").stringValue = "New Profile";
                newProfile.FindPropertyRelative("IsActive").boolValue = false;
                newProfile.FindPropertyRelative("ApiKey").stringValue = "";
                newProfile.FindPropertyRelative("Model").stringValue = "claude-haiku-4-5-20251001";
                newProfile.FindPropertyRelative("BaseUrl").stringValue = "";
                newProfile.FindPropertyRelative("MaxTokens").intValue = 4000;
                newProfile.FindPropertyRelative("VerboseLogging").boolValue = true;
                newProfile.isExpanded = true;
            }

            // --- status bar ---
            EditorGUILayout.Space(4);
            var cfg = (LlmConfig)target;
            var active = cfg.Active;
            if (active != null)
                EditorGUILayout.HelpBox($"激活：{active.ProfileName}  ·  {active.Model}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("没有激活的 Profile，运行时 LLM 将不可用。", MessageType.Warning);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
