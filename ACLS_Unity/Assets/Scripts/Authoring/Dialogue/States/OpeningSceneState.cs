using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // State: the very first narrative turn after character creation.
    // The LLM writes the opening scene narration + 3-4 choices.
    public sealed class OpeningSceneState : DialogueState
    {
        private readonly CharacterPresets.Preset preset;

        public OpeningSceneState(LlmDialogueOrchestrator orchestrator, CharacterPresets.Preset preset)
            : base(orchestrator, DialogueStateType.StagePlay)
        {
            this.preset = preset;
        }

        public override string AssemblePrompt(string userInput = null)
        {
            var player = Orchestrator?.World?.Player;
            string bg = player != null && !string.IsNullOrWhiteSpace(player.BackgroundStory)
                ? $"\n[角色详细背景]：{player.BackgroundStory}"
                : "";
            string action =
                "[开场] 玩家已选定身份。\n" +
                $"[背景]：{preset?.Blurb}{bg}\n" +
                "请按当前世界状态描写主角的第一次登场场景，并给出 3-4 个开局选项。";
            return Orchestrator?.PromptAssembler?.Assemble(StateType, action) ?? "";
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            return ParseStandardReply(rawResponse);
        }

        // After the opening we transition to free narrative.
        public override DialogueStateType? GetNextState(DialogueResult result)
        {
            return DialogueStateType.StagePlay;
        }

        // Shared helper for all narrative states.
        internal static DialogueResult ParseStandardReply(string rawResponse)
        {
            var result = new DialogueResult { RawResponse = rawResponse };

            if (!LlmReply.TryParse(rawResponse, out var reply, out var err))
            {
                result.IsError = true;
                result.ErrorMessage = "LLM 输出解析失败：" + err;
                return result;
            }

            result.Narration = reply.Narration ?? "";
            result.Participants = reply.SceneParticipants ?? new List<LlmReply.Participant>();
            result.Choices = reply.Choices ?? new List<LlmReply.Choice>();

            // Extract system-level fields.
            // Priority: _system block (new) > choice-level fields (legacy).
            if (reply.System != null)
            {
                result.SuggestedNextState = reply.System.SuggestedState ?? "";
                result.SkillTriggers = reply.System.SkillTriggers ?? new List<string>();
                result.Effects = reply.System.Effects ?? new List<LlmReply.EffectSpec>();
            }

            if (reply.Choices != null && reply.Choices.Count > 0)
            {
                var first = reply.Choices[0];
                result.DaysPassed = first.DaysPassed;
                // If _system didn't carry effects, fall back to choice-level effects.
                if (result.Effects == null || result.Effects.Count == 0)
                    result.Effects = first.Effects ?? new List<LlmReply.EffectSpec>();
            }

            return result;
        }
    }
}
