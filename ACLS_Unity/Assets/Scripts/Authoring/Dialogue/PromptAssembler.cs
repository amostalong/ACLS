using System.Text;
using ACLS.Data;
using ACLS.Sim;
using UnityEngine;

namespace ACLS.Authoring
{
    // Assembles the complete prompt for a specific dialogue state.
    // Layers: Base SystemPrompt → State Fragment → JSON Schema → World Snapshot
    //         → L4-L1 Stage Context → User Input.
    public sealed class PromptAssembler
    {
        private readonly LlmPromptConfig config;
        private readonly World world;
        private readonly WorldSnapshot snapshot;

        public PromptAssembler(LlmPromptConfig config, World world)
        {
            this.config = config;
            this.world = world;
            this.snapshot = new WorldSnapshot();
        }

        // Builds the full prompt for a given state.
        public string Assemble(DialogueStateType stateType, string userInput = null,
            SnapshotTiers snapshotTiers = SnapshotTiers.Default)
        {
            var sb = new StringBuilder();

            // 1. Base system prompt.
            sb.Append(config?.SystemPrompt ?? "");

            // 2. State-specific fragment.
            sb.Append("\n\n").Append(FragmentFor(stateType));

            // 3. JSON schema reminder (state-specific).
            sb.Append("\n\n").Append(JsonSchemaFor(stateType));

            // 4. World snapshot (T1~T4 layers).
            SnapshotBuilder.Refresh(world, snapshot);
            if ((snapshotTiers & SnapshotTiers.T1) != 0 && !string.IsNullOrEmpty(snapshot.Tier1))
                sb.Append("\n\n").Append(snapshot.Tier1);
            if ((snapshotTiers & SnapshotTiers.T2) != 0 && !string.IsNullOrEmpty(snapshot.Tier2))
                sb.Append("\n\n").Append(snapshot.Tier2);
            if ((snapshotTiers & SnapshotTiers.T3) != 0 && !string.IsNullOrEmpty(snapshot.Tier3))
                sb.Append("\n\n").Append(snapshot.Tier3);
            if ((snapshotTiers & SnapshotTiers.T4) != 0 && !string.IsNullOrEmpty(snapshot.Tier4))
                sb.Append("\n\n").Append(snapshot.Tier4);

            // 5. L4-L1 stage context (injected for StagePlay, skipped during build phases).
            if (stateType == DialogueStateType.StagePlay && world.Stage != null)
            {
                if (!string.IsNullOrEmpty(world.Stage.L4World))
                    sb.Append("\n\n[宏观背景]\n").Append(world.Stage.L4World);
                if (!string.IsNullOrEmpty(world.Stage.L3Expanse))
                    sb.Append("\n\n[区域背景]\n").Append(world.Stage.L3Expanse);
                if (!string.IsNullOrEmpty(world.Stage.L2Arena))
                    sb.Append("\n\n[近域层]\n").Append(world.Stage.L2Arena);
                if (!string.IsNullOrEmpty(world.Stage.L1Stage))
                    sb.Append("\n\n[贴身层]\n").Append(world.Stage.L1Stage);
            }

            // 6. User input / action.
            if (!string.IsNullOrWhiteSpace(userInput))
                sb.Append("\n\n").Append(userInput);

            return sb.ToString();
        }

        public string AssembleWorldBuild(string roleDescription, string worldDescription)
        {
            var sb = new StringBuilder();
            sb.Append(config?.SystemPrompt ?? "");
            string fragment = FragmentFor(DialogueStateType.WorldBuild) ?? "";
            fragment = fragment
                .Replace("{role_description}", roleDescription ?? "")
                .Replace("{world_description}", worldDescription ?? "");
            sb.Append("\n\n").Append(fragment);
            return sb.ToString();
        }

        // Builds the stage-create prompt using world L4/L3 context + character data.
        public string AssembleStageCreate(CharacterPresets.Preset preset)
        {
            var sb = new StringBuilder();
            sb.Append(config?.SystemPrompt ?? "");
            sb.Append("\n\n").Append(FragmentFor(DialogueStateType.StageCreate));
            sb.Append("\n\n").Append(JsonSchemaFor(DialogueStateType.StageCreate));

            // Inject already-built macro context so LLM grounds L1/L2 in the world.
            if (world.Stage != null)
            {
                if (!string.IsNullOrEmpty(world.Stage.L4World))
                    sb.Append("\n\n[宏观背景]\n").Append(world.Stage.L4World);
                if (!string.IsNullOrEmpty(world.Stage.L3Expanse))
                    sb.Append("\n\n[区域背景]\n").Append(world.Stage.L3Expanse);
            }

            // Character context.
            var player = world?.Player;
            string locationName = preset?.LocationName ?? "";
            if (player != null)
            {
                locationName = world.GetLocation(player.Identity?.LocationId ?? 0)?.Name ?? preset?.LocationName ?? "";
                string bg = !string.IsNullOrWhiteSpace(player.BackgroundStory)
                    ? player.BackgroundStory : preset?.Blurb ?? "";
                sb.Append("\n\n");
                sb.AppendLine($"[角色] {player.Name}，{player.AgeAt(world.Date)}岁，{(player.Sex == Sex.Male ? "男" : "女")}");
                sb.AppendLine($"[所在] {locationName}");
                sb.AppendLine($"[日期] {world.Date}");
                sb.Append($"[背景] {bg}");
            }
            else if (preset != null)
            {
                sb.Append("\n\n");
                sb.AppendLine($"[所在] {locationName}");
                sb.Append($"[背景] {preset.Blurb}");
            }

            return sb.ToString();
        }

        // Builds the character-expansion prompt with variable substitution.
        public string AssembleCharacterExpansion(CharacterPresets.Preset preset, Character player)
        {
            string template = config?.WorldCreatePrompt ?? "";
            return template
                .Replace("{name}", player.Name)
                .Replace("{courtesy}", player.Courtesy)
                .Replace("{sex}", player.Sex == Sex.Male ? "男" : "女")
                .Replace("{age}", player.AgeAt(world.Date).ToString())
                .Replace("{location}", world.GetLocation(player.Identity?.LocationId ?? 0)?.Name ?? "")
                .Replace("{blurb}", preset.Blurb ?? "")
                .Replace("{date}", world.Date.ToString())
                .Replace("{trait}", TraitLabel(preset.TraitId));
        }

        private static string FragmentFor(DialogueStateType type) => type switch
        {
            DialogueStateType.WorldBuild    => LoadFragment("Fragment_WorldBuild"),
            DialogueStateType.StageCreate   => LoadFragment("Fragment_StageCreate"),
            DialogueStateType.ActorCreation => LoadFragment("Fragment_CharacterCreation"),
            DialogueStateType.StagePlay     => LoadFragment("Fragment_FreeNarrative"),
            _ => "",
        };

        private static string JsonSchemaFor(DialogueStateType type) => type switch
        {
            DialogueStateType.WorldBuild => "",

            DialogueStateType.StageCreate =>
                "返回严格 JSON，顶层字段：" +
                "thinking（你的推理过程，原样输出，尽量先输出该字段），" +
                "l1_stage{location, scene_description, active_npcs:[{name,role,relation_value,stance}], immediate_situation, exits:[]}，" +
                "l2_arena{near_contacts:[{name,role,location,days_away}], active_pressures:[], opportunities:[]}。" +
                "不要 JSON 之外的文字（含 ``` 围栏）。",

            DialogueStateType.ActorCreation =>
                "返回严格的 JSON，字段：family_background, social_circle[], recent_goal, secret, values, starting_assets{connections[], knowledge[], items[]}",

            DialogueStateType.StagePlay =>
                "每次回复严格使用 JSON：\n" +
                "{\n" +
                "  \"thinking\": \"<你的推理过程，原样输出，尽量先输出该字段>\",\n" +
                "  \"narration\": \"<2-4 段中文叙事>\",\n" +
                "  \"scene_participants\": [ {\"name\": \"...\", \"role\": \"...\"} ],\n" +
                "  \"choices\": [\n" +
                "    {\n" +
                "      \"label\": \"<10 字以内>\",\n" +
                "      \"outcome_narration\": \"<2-3 段>\",\n" +
                "      \"days_passed\": <1..90>,\n" +
                "      \"effects\": [ {\"kind\": \"...\", ...} ]\n" +
                "    }\n" +
                "  ],\n" +
                "  \"_system\": {\n" +
                "    \"suggested_state\": \"Dialogue\",\n" +
                "    \"skill_triggers\": [\"npc-psychology\", \"relationship-tracker\"]\n" +
                "  }\n" +
                "}\n" +
                "_system 为可选。不要 JSON 之外的文字（含 ``` 围栏）。",

            _ => "",
        };

        private static string LoadFragment(string resourceName)
        {
            var asset = Resources.Load<UnityEngine.TextAsset>("Prompts/" + resourceName);
            return asset != null ? asset.text : "";
        }

        private static string TraitLabel(int traitId) => traitId switch
        {
            WorldFactory.TRAIT_CAUTIOUS => "谨慎",
            WorldFactory.TRAIT_DECISIVE => "果决",
            WorldFactory.TRAIT_STUDIOUS => "好学",
            _ => "",
        };
    }

    public enum DialogueStateType
    {
        /// <summary>
        /// 世界构建（L4+L3）
        /// </summary>
        WorldBuild,
        /// <summary>
        /// 舞台创建（L1+L2）
        /// </summary>
        StageCreate,
        /// <summary>
        /// 创角色
        /// </summary>
        ActorCreation,
        /// <summary>
        /// 表演
        /// </summary>
        StagePlay,
    }
}
