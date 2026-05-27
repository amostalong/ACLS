using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // State: after the player clicks "开始游戏" on the creation modal.
    // We send the expansion prompt to the LLM and write the result back
    // into the player Character before moving on to the opening scene.
    public sealed class CharacterExpansionState : DialogueState
    {
        private readonly CharacterPresets.Preset preset;
        private readonly Character player;

        public CharacterExpansionState(LlmDialogueOrchestrator orchestrator,
            CharacterPresets.Preset preset, Character player)
            : base(orchestrator, DialogueStateType.ActorCreation)
        {
            this.preset = preset;
            this.player = player;
        }

        public override string AssemblePrompt(string userInput = null)
        {
            return Orchestrator?.PromptAssembler?.AssembleCharacterExpansion(preset, player) ?? "";
        }

        public override DialogueResult ParseResponse(string rawResponse)
        {
            var result = new DialogueResult { RawResponse = rawResponse };

            if (!CharacterExpansionReply.TryParse(rawResponse, out var expansion, out var err))
            {
                result.IsError = true;
                result.ErrorMessage = "角色拓展解析失败：" + err;
                return result;
            }

            result.Thinking = expansion.Thinking ?? "";

            // Apply expansion to the player character immediately.
            if (player != null)
            {
                player.BackgroundStory = expansion.FamilyBackground ?? "";
                player.Values = expansion.Values ?? "";
                player.CurrentGoal = expansion.RecentGoal ?? "";
                player.Secret = expansion.Secret ?? "";
                if (expansion.StartingAssets != null)
                {
                    player.Connections = expansion.StartingAssets.Connections ?? new List<string>();
                    player.KnownFacts = expansion.StartingAssets.Knowledge ?? new List<string>();
                    player.OwnedItems = expansion.StartingAssets.Items ?? new List<string>();
                }
            }

            result.Narration = $"【角色背景已生成】{player?.Name} 的家族与社交网络已建立。";
            return result;
        }

        // After expansion we always move to the opening scene.
        public override DialogueStateType? GetNextState(DialogueResult result) => DialogueStateType.StagePlay;
    }
}
