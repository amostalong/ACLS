using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ACLS.Llm;
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

        public static void Save(World world, ChatHistory history, string slot = "slot0")
        {
            var data = new SaveData
            {
                World = world,
                History = new List<ChatMessage>(history.All),
            };
            string json = JsonConvert.SerializeObject(data, Settings);
            string path = SlotPath(slot);
            File.WriteAllText(path, json, Encoding.UTF8);
            Debug.Log($"[SaveManager] saved → {path}");
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
                return data?.World != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] load failed: {ex.Message}");
                return false;
            }
        }

        public static bool SlotExists(string slot = "slot0") => File.Exists(SlotPath(slot));

        public static void DeleteSlot(string slot = "slot0")
        {
            string path = SlotPath(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string SlotPath(string slot) =>
            Path.Combine(Application.persistentDataPath, $"save_{slot}.json");
    }
}
