using System;
using System.Collections.Generic;
using ACLS.Llm;

namespace ACLS.Authoring
{
    // The unified result of an LLM dialogue turn.
    // Split cleanly into:
    //   - User-facing content (narration, choices, participants)
    //   - System-facing content (effects, state transitions, skill triggers)
    [Serializable]
    public sealed class DialogueResult
    {
        // -------- user-facing --------
        public string Narration = "";
        public List<LlmReply.Choice> Choices = new List<LlmReply.Choice>();
        public List<LlmReply.Participant> Participants = new List<LlmReply.Participant>();

        // -------- system-facing --------
        public List<LlmReply.EffectSpec> Effects = new List<LlmReply.EffectSpec>();
        public string SuggestedNextState = "";   // e.g. "Dialogue"
        public List<string> SkillTriggers = new List<string>();
        public int DaysPassed = 0;

        // -------- meta --------
        public bool IsError;
        public string ErrorMessage = "";
        public string RawResponse = "";
    }
}
