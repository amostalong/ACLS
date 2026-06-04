using UnityEditor;
using UnityEngine;

namespace ACLS.Editor
{
    public static class SaveTools
    {
        private const string PrefKey = "ACLS_AutoDeleteSave";

        [MenuItem("ACLS/删除存档", false, 100)]
        private static void DeleteSave()
        {
            var path = System.IO.Path.Combine(Application.persistentDataPath, "save_slot0.json");
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog("删除存档", "没有找到存档文件。", "确定");
                return;
            }

            if (!EditorUtility.DisplayDialog("删除存档", $"确定要删除存档吗？\n\n路径：{path}", "确定删除", "取消"))
                return;

            System.IO.File.Delete(path);
            Debug.Log($"[SaveTools] 存档已删除: {path}");
            EditorUtility.DisplayDialog("删除存档", "存档已删除。", "确定");
        }

        [MenuItem("ACLS/每次启动自动删除存档", false, 110)]
        private static void ToggleAutoDelete()
        {
            bool current = EditorPrefs.GetBool(PrefKey, false);
            EditorPrefs.SetBool(PrefKey, !current);
            Debug.Log($"[SaveTools] 自动删除存档: {(!current ? "开启" : "关闭")}");
        }

        [MenuItem("ACLS/每次启动自动删除存档", true)]
        private static bool ToggleAutoDeleteValidate()
        {
            Menu.SetChecked("ACLS/每次启动自动删除存档", EditorPrefs.GetBool(PrefKey, false));
            return true;
        }
    }
}
