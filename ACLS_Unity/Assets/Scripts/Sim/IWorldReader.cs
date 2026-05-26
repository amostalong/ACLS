using System.Collections.Generic;

namespace ACLS.Sim
{
    // Read-only view of the world. Skills receive this instead of World directly
    // so they cannot mutate state outside the effect system.
    public interface IWorldReader
    {
        GameDate CurrentDate { get; }
        int Gold { get; }
        Character GetCharacter(int id);
        Character GetPlayerCharacter();
        Location GetLocation(int id);
        Faction GetFaction(int id);
        bool HasFlag(string flag);
        int GetOpinion(int fromId, int toId);
        IEnumerable<Character> AliveCharacters();
    }
}
