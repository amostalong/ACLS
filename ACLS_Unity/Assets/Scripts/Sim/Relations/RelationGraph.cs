using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    // Opinion is an asymmetric directed value: how (from) feels about (to).
    // Range clamped to [-100, +100]. Family ties (parent/spouse/child) live on
    // Character itself, not in this graph.
    [Serializable]
    public sealed class RelationGraph
    {
        [Serializable]
        public struct Entry
        {
            public int From;
            public int To;
            public int Opinion;
        }

        public List<Entry> Entries = new List<Entry>();

        [NonSerialized] private Dictionary<long, int> cache;  // pair key → index in Entries

        private void EnsureCache()
        {
            if (cache != null && cache.Count == Entries.Count) return;
            cache = new Dictionary<long, int>(Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                cache[Key(Entries[i].From, Entries[i].To)] = i;
            }
        }

        private static long Key(int from, int to) => ((long)from << 32) | (uint)to;

        public int Opinion(int from, int to)
        {
            if (from == 0 || to == 0) return 0;
            EnsureCache();
            return cache.TryGetValue(Key(from, to), out int idx) ? Entries[idx].Opinion : 0;
        }

        public void Adjust(int from, int to, int delta)
        {
            if (from == 0 || to == 0 || delta == 0) return;
            EnsureCache();
            long k = Key(from, to);
            if (cache.TryGetValue(k, out int idx))
            {
                var e = Entries[idx];
                e.Opinion = Clamp(e.Opinion + delta);
                Entries[idx] = e;
            }
            else
            {
                Entries.Add(new Entry { From = from, To = to, Opinion = Clamp(delta) });
                cache[k] = Entries.Count - 1;
            }
        }

        public IEnumerable<Entry> OpinionsFrom(int from)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].From == from) yield return Entries[i];
        }

        private static int Clamp(int v) => v < -100 ? -100 : v > 100 ? 100 : v;
    }
}
