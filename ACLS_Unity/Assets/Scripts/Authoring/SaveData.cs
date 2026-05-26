using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    public sealed class SaveData
    {
        public string Version = "1";
        public World World;
        public List<ChatMessage> History;
    }
}
