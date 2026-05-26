using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ACLS.Llm
{
    public static class LlmDebugLog
    {
        public sealed class Entry
        {
            public int Index;
            public string Timestamp;
            public string Provider;
            public string RequestJson;
            public string ResponseRaw;

            private string _prettyRequest;
            private string _prettyResponse;

            public string PrettyRequest  => _prettyRequest  ??= TryPretty(RequestJson);
            public string PrettyResponse => _prettyResponse ??= TryPretty(ResponseRaw);

            private static string TryPretty(string json)
            {
                if (string.IsNullOrEmpty(json)) return json ?? "";
                try { return JToken.Parse(json).ToString(Formatting.Indented); }
                catch { return json; }
            }
        }

        private const int MaxEntries = 30;
        private static readonly List<Entry> _entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => _entries;
        public static event Action<Entry> OnEntry;

        public static void Add(string provider, string requestJson, string responseRaw)
        {
            var e = new Entry
            {
                Index     = _entries.Count,
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Provider  = provider,
                RequestJson  = requestJson,
                ResponseRaw  = responseRaw,
            };
            if (_entries.Count >= MaxEntries) _entries.RemoveAt(0);
            _entries.Add(e);
            OnEntry?.Invoke(e);
        }
    }
}
