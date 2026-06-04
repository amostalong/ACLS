using System;
using System.IO;
using System.Text;
using ACLS.Data;
using ACLS.Logging;
using ACLS.Sim;
using Newtonsoft.Json;
using UnityEngine;

namespace ACLS.Authoring
{
    public static class SaveManager
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static void Save(World world, GameMemory memory, string slot = "slot0")
        {
            var data = new SaveData
            {
                World = world,
                Memory = memory,
            };
            string json = JsonConvert.SerializeObject(data, Settings);
            string path = SlotPath(slot);
            File.WriteAllText(path, json, Encoding.UTF8);
            Log.Info(Log.Channels.Save, "saved → {0}", path);
        }

        public static bool TryLoad(string slot, out SaveData data)
        {
            data = null;
            string path = SlotPath(slot);
            if (!File.Exists(path)) return false;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                data = JsonConvert.DeserializeObject<SaveData>(json, Settings);
                if (data?.World != null)
                {
                    // 恢复运行时单例
                    GameMemory.Instance = data.Memory ?? new GameMemory();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(Log.Channels.Save, "load failed: {0}", ex.Message);
                return false;
            }
        }

        public static bool SlotExists(string slot = "slot0")
        {
            string path = SlotPath(slot);
            bool exists = File.Exists(path);
            Log.Info(Log.Channels.Save, "存档检查: slot={0} path={1} exists={2}", slot, path, exists);
            return exists;
        }

        public static void DeleteSlot(string slot = "slot0")
        {
            string path = SlotPath(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string SlotPath(string slot) =>
            Path.Combine(Application.persistentDataPath, $"save_{slot}.json");
    }
}
