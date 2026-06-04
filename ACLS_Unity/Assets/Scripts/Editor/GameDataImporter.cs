using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACLS.Authoring;
using ACLS.Data;
using ACLS.Logging;
using UnityEditor;
using UnityEngine;

namespace ACLS.Editor
{
    /// <summary>
    /// 从 AIGame memory/ 目录导入三国数据到三个独立的 SO。
    /// 生成 Assets/Resources/Content/CharacterDB.asset / FactionDB.asset / LocationDB.asset
    ///
    /// 用法：菜单 ACLS > Import Game Data from AIGame
    /// </summary>
    public static class GameDataImporter
    {
        private const string AIGameMemoryPath = "../../../OneDrive/WorkingWithAI/AIGame/memory";
        private const string OutputDir = "Assets/Resources/Content/";

        [MenuItem("ACLS/删除存档", false, 200)]
        public static void DeleteSave()
        {
            if (EditorUtility.DisplayDialog("删除存档", "确定删除所有存档数据？", "删除", "取消"))
            {
                Authoring.SaveManager.DeleteSlot("slot0");
                Debug.Log("存档已删除");
            }
        }

        [MenuItem("ACLS/Import Game Data from AIGame", false, 100)]
        public static void Import()
        {
            string dataPath = Application.dataPath;
            string aigameMemory = Path.GetFullPath(Path.Combine(dataPath, AIGameMemoryPath));

            if (!Directory.Exists(aigameMemory))
            {
                EditorUtility.DisplayDialog("导入失败",
                    $"未找到 AIGame memory 目录：\n{aigameMemory}\n\n请确认 OneDrive 路径是否正确。", "确定");
                return;
            }

            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);

            int chars = ImportOne<CharacterDB>("CharacterDB", Path.Combine(aigameMemory, "CHARS"), "C");
            int facs  = ImportOne<FactionDB>("FactionDB", Path.Combine(aigameMemory, "FACTIONS"), "F");
            int locs  = ImportOne<LocationDB>("LocationDB", Path.Combine(aigameMemory, "PLACES"), "P");

            AssetDatabase.SaveAssets();
            Log.Info(Log.Channels.Content, "导入完成: 人物={0} 势力={1} 地点={2}", chars, facs, locs);
        }

        private static int ImportOne<T>(string assetName, string mdDir, string prefix) where T : ScriptableObject
        {
            string path = OutputDir + assetName + ".asset";

            // 加载或创建 SO
            var db = AssetDatabase.LoadAssetAtPath<T>(path);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(db, path);
            }

            // 取 Entries 字段
            var field = typeof(T).GetField("Entries");
            var entries = field?.GetValue(db) as List<GameDataEntry>;
            if (entries == null) return 0;

            if (!Directory.Exists(mdDir))
            {
                Log.Warn(Log.Channels.Content, "目录不存在: {0}", mdDir);
                return 0;
            }

            // 读 .md 文件
            var files = Directory.GetFiles(mdDir, "*.md")
                .Where(f =>
                {
                    string name = Path.GetFileName(f);
                    return name.StartsWith(prefix) && !name.Equals("INDEX.md", System.StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(f => f)
                .ToList();

            // 去重
            var existing = new Dictionary<string, GameDataEntry>();
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.Id))
                    existing[e.Id] = e;

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string content = File.ReadAllText(file).Trim();

                string id = "";
                string name = fileName;
                int underscore = fileName.IndexOf('_');
                if (underscore > 0)
                {
                    id = fileName.Substring(0, underscore);
                    name = fileName.Substring(underscore + 1);
                }

                if (existing.TryGetValue(id, out var entry))
                {
                    entry.Name = name;
                    entry.Content = content;
                }
                else
                {
                    entries.Add(new GameDataEntry { Id = id, Name = name, Content = content });
                    existing[id] = entry;
                }
            }

            EditorUtility.SetDirty(db);
            return files.Count;
        }
    }
}
