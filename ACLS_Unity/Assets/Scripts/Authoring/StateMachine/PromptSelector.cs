using System.Text;
using UnityEngine;
using ACLS.Data;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Identifiers for optional prompt fragments that can be appended to the
    // base system prompt depending on the current game state.
    public enum PromptFragment
    {
        CharacterCreation, // Additional rules while on the creation modal
        FreeNarrative,     // Default narrative rules
        DeepDialogue,      // NPC conversation depth, address rules
        Combat,            // Battle pacing, casualty constraints
        Travel,            // Journey skipping, encounter rules
        Planning,          // Strategic depth, long-term thinking
    }

    // Assembles the final system prompt sent to the LLM.
    // Reads base prompt + fragment prompts from external .md files.
    public sealed class PromptSelector
    {
        private readonly LlmPromptConfig config;
        private World world;

        public PromptSelector(LlmPromptConfig config, World world = null)
        {
            this.config = config;
            this.world = world;
        }

        public void AttachWorld(World world) => this.world = world;

        // The base prompt loaded from SystemPrompt.md (or fallback).
        public string BasePrompt => config?.SystemPrompt ?? "";

        // Returns a StringBuilder seeded with the base prompt.
        public StringBuilder GetBasePrompt()
        {
            return new StringBuilder(BasePrompt);
        }

        // 拼接"时代大势"实时上下文段落。可在 ResolveSystemPrompt 中追加到 system prompt 末尾。
        // 始终返回空字符串表示"无内容"(便于上层直接 Append 而无需判空)。
        public string BuildEraTrendContext()
        {
            if (world?.EraTrend == null) return "";
            var era = world.EraTrend;
            var today = world.Date;
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("【时代大势·实时上下文】");
            sb.AppendLine($"- 当前日期：{today.ToLLMString()}");
            sb.AppendLine($"- 当前阶段：{(string.IsNullOrEmpty(era.CurrentStageName) ? "（无）" : era.CurrentStageName)}");

            // 列出 ≤6 月内未触发的硬锚点(最多 3 个)
            int upcoming = 0;
            var anchors = era.ActiveAnchors ?? EraTrendAnchors.EmptyList;
            for (int i = 0; i < anchors.Count && upcoming < 3; i++)
            {
                var a = anchors[i];
                if (era.TriggeredAnchorIds.Contains(a.Id)) continue;
                int days = DaysBetween(today, a.TriggerDate);
                if (days < 0 || days > 180) continue;
                sb.AppendLine($"- 即将发生：{a.TriggerDate.ToLLMString()}（{days} 天后）{a.Title}");
                upcoming++;
            }

            // 列出已注入的前兆(按 layer 分组;开局追补始终展示)
            if (era.ForeshadowingInjected != null && era.ForeshadowingInjected.Count > 0)
            {
                int shown = 0;
                for (int i = 0; i < era.ForeshadowingInjected.Count && shown < 6; i++)
                {
                    var f = era.ForeshadowingInjected[i];
                    if (!f.Template.StartsWith("[开局追补]") && era.TriggeredAnchorIds.Contains(f.AnchorId)) continue;
                    string body = f.Template.StartsWith("[开局追补]") ? f.Template.Substring("[开局追补]".Length) : f.Template;
                    string tag = f.Template.StartsWith("[开局追补]") ? "[开局追补]" : $"[前兆{f.TargetLayer}]";
                    sb.AppendLine($"- {tag} {body}");
                    shown++;
                }
            }

            return sb.ToString();
        }

        private static int DaysBetween(GameDate a, GameDate b)
        {
            int days = 0;
            var cur = a;
            while (cur < b)
            {
                cur = cur.AddDays(1);
                days++;
                if (days > 4000) break;
            }
            return days;
        }

        // Returns the markdown text for a specific fragment, or empty string
        // if the fragment file is missing.
        public string GetFragment(PromptFragment fragment)
        {
            string resourcesPath = ResourcePathFor(fragment);
            string name = resourcesPath.StartsWith("Prompts/") ? resourcesPath.Substring("Prompts/".Length) : resourcesPath;
            var asset = ContentLoader.LoadSync<TextAsset>($"Assets/Content/Prompts/{name}.md", resourcesPath);
            if (asset != null) return "\n\n" + asset.text;

            // Fallback: return built-in fragment text.
            return "\n\n" + BuiltInFragment(fragment);
        }

        private static string ResourcePathFor(PromptFragment fragment) => fragment switch
        {
            PromptFragment.CharacterCreation => "Prompts/Fragment_CharacterCreation",
            PromptFragment.FreeNarrative => "Prompts/Fragment_FreeNarrative",
            PromptFragment.DeepDialogue => "Prompts/Fragment_DeepDialogue",
            PromptFragment.Combat => "Prompts/Fragment_Combat",
            PromptFragment.Travel => "Prompts/Fragment_Travel",
            PromptFragment.Planning => "Prompts/Fragment_Planning",
            _ => "",
        };

        private static string BuiltInFragment(PromptFragment fragment) => fragment switch
        {
            PromptFragment.CharacterCreation =>
                "【角色创建阶段】\n" +
                "- 不要提前泄露剧情，只描写主角当前所处的环境与可选行动。\n" +
                "- 选项应反映不同性格倾向（谨慎/果决/好学）。",

            PromptFragment.FreeNarrative =>
                "【自由叙事阶段】\n" +
                "- 默认状态。玩家通过选项或自由输入推进。\n" +
                "- 保持事件密度合理：赶路日 0 事件，一般日 0-1 事件，关键日 1-3 事件。\n" +
                "- 琐事直接描写过掉不给选择；小事给 2-3 个选项快速收束；中事以上展开。",

            PromptFragment.DeepDialogue =>
                "【深度对话阶段】\n" +
                "- 玩家正在与特定 NPC 深入交流。对话优先于行动。\n" +
                "- 称呼必须合乎汉末礼制：县令称\"明府\"，郡守称\"使君\"，同级称\"足下\"，长辈称\"丈人\"。\n" +
                "- NPC 基于自身价值观和掌握的信息做出反应，不为剧情方便而扭曲。\n" +
                "- 当众与私下反应不同：当众失面子伤害×3。",

            PromptFragment.Combat =>
                "【战斗阶段】\n" +
                "- 快节奏，短句，动作驱动。感官描写优先。\n" +
                "- 汉代兵器：环首刀、矛、戟、弓弩。无马镫。\n" +
                "- 伤亡必须合理：守城方占优，野战伤亡 10-30%。\n" +
                "- 不给太多选择——战斗中反应时间有限，2 个关键选项即可。",

            PromptFragment.Travel =>
                "【赶路阶段】\n" +
                "- 无事件路程直接跳过，一句过渡。\n" +
                "- 住宿、吃饭、饮水默认处理，不询问。\n" +
                "- 每 2-3 天路程最多 1 个随机遭遇（除非在战区）。\n" +
                "- 距离/天数必须合理：1 汉里≈415m，单人骑马≈100 汉里/天。",

            PromptFragment.Planning =>
                "【谋划阶段】\n" +
                "- 玩家在做长期战略决策。允许更宏观的视角。\n" +
                "- 可引用势力动态、历史大势、人物近况等深层信息。\n" +
                "- 选项应体现不同策略方向的风险与收益。\n" +
                "- 可给出\"暂不出手\"的观望选项。",

            _ => "",
        };
    }
}
