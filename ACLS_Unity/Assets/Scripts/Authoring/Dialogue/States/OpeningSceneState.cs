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

        public string BuildOpeningAction()
        {
            var player = Orchestrator?.World?.Player;
            string bg = player != null && !string.IsNullOrWhiteSpace(player.BackgroundStory)
                ? $"\n[角色详细背景]：{player.BackgroundStory}"
                : "";
            return
                "[开场] 玩家已选定身份。\n" +
                $"[背景]：{preset?.Blurb}{bg}\n" +
                "请按当前世界状态描写主角的第一次登场场景，并给出 1-4 个开局选项。";
        }

        public override string AssemblePrompt(string userInput = null)
        {
            string action = string.IsNullOrWhiteSpace(userInput) ? BuildOpeningAction() : userInput;
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

        public DialogueResult BuildNarrationTextResult(string rawResponse, string narration,
            List<LlmReply.Choice> choices, LlmReply effectsReply)
        {
            var result = new DialogueResult
            {
                RawResponse = rawResponse,
                Narration = narration ?? "",
                Thinking = "",
                Choices = choices ?? new List<LlmReply.Choice>(),
                Participants = effectsReply?.System?.SceneParticipants ?? new List<LlmReply.Participant>(),
                Effects = effectsReply?.System?.Effects ?? new List<LlmReply.EffectSpec>(),
                SuggestedNextState = effectsReply?.System?.SuggestedState ?? "",
                SkillTriggers = effectsReply?.System?.SkillTriggers ?? new List<string>(),
                DaysPassed = effectsReply?.DaysPassed ?? 0,
            };

            if (!string.IsNullOrWhiteSpace(effectsReply?.Date))
                result.Date = TryParseDate(effectsReply.Date);

            return result;
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
            result.Thinking = reply.Thinking ?? "";
            result.Participants = reply.SceneParticipants ?? new List<LlmReply.Participant>();
            result.Choices = reply.Choices ?? new List<LlmReply.Choice>();

            // 日期：LLM 回复中若有则解析
            if (!string.IsNullOrWhiteSpace(reply.Date))
                result.Date = TryParseDate(reply.Date);

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

        // Try to parse LLM date format: "0184年01月08日"
        private static Sim.GameDate? TryParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                // Match 4-digit-year 年 2-digit-month 月 2-digit-day 日
                var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"(\d{1,4})年(\d{1,2})月(\d{1,2})日");
                if (m.Success)
                {
                    int y = int.Parse(m.Groups[1].Value);
                    int mo = int.Parse(m.Groups[2].Value);
                    int d = int.Parse(m.Groups[3].Value);
                    if (mo >= 1 && mo <= 12 && d >= 1 && d <= 31)
                        return new Sim.GameDate(y, mo, d);
                }
            }
            catch { }
            return null;
        }
    }
}
