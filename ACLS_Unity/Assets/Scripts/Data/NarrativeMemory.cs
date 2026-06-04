using System;
using System.Text;
using ACLS.Sim;
using Newtonsoft.Json.Linq;

namespace ACLS.Data
{
    /// <summary>
    /// 叙事记忆系统。以 string-JSON 格式存储在 World.MemoryJson 上。
    /// 格式：{"entries":[{"date":"中平二年·七月","event":"玩家抵达武阳..."},...]}
    ///
    /// 当前为纯文本 JSON，后续可升级为结构化存储。
    /// </summary>
    public static class NarrativeMemory
    {
        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return EmptyObj();
            try
            {
                var obj = JObject.Parse(json);
                if (obj["entries"] == null)
                    obj["entries"] = new JArray();
                return obj;
            }
            catch { return EmptyObj(); }
        }

        private static JObject EmptyObj()
        {
            var o = new JObject();
            o["entries"] = new JArray();
            return o;
        }

        /// <summary>追加一条记忆事件。</summary>
        public static void Append(World world, string date, string eventText)
        {
            if (world == null) return;
            var obj = Parse(world.MemoryJson);
            var entries = obj["entries"] as JArray;
            if (entries == null)
            {
                entries = new JArray();
                obj["entries"] = entries;
            }
            entries.Add(new JObject
            {
                ["date"] = date ?? "",
                ["event"] = eventText ?? "",
            });
            world.MemoryJson = obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>读取最近 N 条记忆，返回格式化文本供 LLM 使用。</summary>
        public static string ReadRecent(World world, int count = 10)
        {
            if (world == null) return "(无记忆)";
            var obj = Parse(world.MemoryJson);
            var entries = obj["entries"] as JArray;
            if (entries == null || entries.Count == 0) return "(无记忆)";

            var sb = new StringBuilder();
            int start = Math.Max(0, entries.Count - count);
            for (int i = start; i < entries.Count; i++)
            {
                var e = entries[i];
                string d = (string)e["date"] ?? "";
                string ev = (string)e["event"] ?? "";
                sb.AppendLine($"· [{d}] {ev}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>获取所有记忆的原始 JSON。</summary>
        public static string GetAllRaw(World world)
        {
            if (world == null) return "{}";
            var obj = Parse(world.MemoryJson);
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
