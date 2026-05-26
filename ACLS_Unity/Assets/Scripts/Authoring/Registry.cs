using System.Collections.Generic;
using ACLS.Data;

namespace ACLS.Authoring
{
    // Lookup for ScriptableObject content at runtime. Init once at boot, then
    // any layer (UI / Authoring / Sim's effect site) can resolve an id back to
    // its definition.
    public static class Registry
    {
        private static readonly Dictionary<int, TraitDef> traits = new Dictionary<int, TraitDef>();
        private static readonly Dictionary<string, GameEventDef> events = new Dictionary<string, GameEventDef>();

        public static IEnumerable<GameEventDef> AllEvents => events.Values;

        public static void Clear()
        {
            traits.Clear();
            events.Clear();
        }

        public static void Register(TraitDef t)
        {
            if (t != null && t.Id != 0) traits[t.Id] = t;
        }

        public static void Register(GameEventDef e)
        {
            if (e != null && !string.IsNullOrEmpty(e.Id)) events[e.Id] = e;
        }

        public static TraitDef GetTrait(int id) => traits.TryGetValue(id, out var t) ? t : null;
        public static GameEventDef GetEvent(string id) =>
            !string.IsNullOrEmpty(id) && events.TryGetValue(id, out var e) ? e : null;
    }
}
