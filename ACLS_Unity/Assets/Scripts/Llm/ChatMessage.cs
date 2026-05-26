using System;

namespace ACLS.Llm
{
    public enum ChatRole { System, User, Assistant }

    [Serializable]
    public sealed class ChatMessage
    {
        public ChatRole Role;
        public string Content = "";
        public DateTime At;

        public ChatMessage() { }
        public ChatMessage(ChatRole role, string content)
        {
            Role = role;
            Content = content ?? "";
            At = DateTime.Now;
        }
    }
}
