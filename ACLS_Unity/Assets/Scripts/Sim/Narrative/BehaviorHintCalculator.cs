using System;
using System.Collections.Generic;
using ACLS.Data;

namespace ACLS.Sim.Narrative
{
    /// <summary>
    /// 根据 NPC schema (CharEntry) + 玩家行动语气，计算每个 NPC 的行为 hint。
    /// 纯本地 if-else，不调 LLM。
    ///
    /// v1 简化：
    ///   - 行动语气用关键词匹配分类
    ///   - 关系只取 CharEntry.relation 单值
    ///   - 目标匹配用 values / current_goal 的关键词
    /// </summary>
    public static class BehaviorHintCalculator
    {
        public enum ActionTone
        {
            Neutral,        // 中性对话
            AskHelp,        // 求助
            Bribe,          // 利诱
            Threat,         // 威胁
            MentionSecret,  // 提及 NPC 秘密/隐私
            Attack          // 攻击
        }

        // ──────────── 玩家输入语气分类 ────────────

        public static ActionTone ClassifyAction(string playerText)
        {
            if (string.IsNullOrEmpty(playerText)) return ActionTone.Neutral;
            var t = playerText;

            if (ContainsAny(t, "杀", "砍", "攻", "打", "斩", "刺")) return ActionTone.Attack;
            if (ContainsAny(t, "秘密", "你其实是", "你的底细", "你父亲", "你母亲", "你知道什么"))
                return ActionTone.MentionSecret;
            if (ContainsAny(t, "滚", "找死", "信不信我", "威胁", "敢动", "再不"))
                return ActionTone.Threat;
            if (ContainsAny(t, "钱", "金", "赏", "礼", "贿", "送", "酬"))
                return ActionTone.Bribe;
            if (ContainsAny(t, "帮", "求", "请", "救", "借", "能否", "可否", "望"))
                return ActionTone.AskHelp;

            return ActionTone.Neutral;
        }

        // ──────────── 单个 NPC 计算 ────────────

        public static BehaviorHint Compute(CharEntry npc, ActionTone tone)
        {
            var hint = new BehaviorHint { NpcName = npc?.name ?? "?" };

            if (npc == null)
            {
                hint.CooperationScore = 0;
                hint.Tendency = "中立";
                hint.Emotion = "平静";
                hint.Reason = "NPC 数据缺失";
                return hint;
            }

            // 1. 关系基线（CharEntry.relation 约定 -50~50，映射到 -100~+100）
            int relationValue = SafeInt(npc.relation);
            int baseScore = relationValue * 2;

            // 2. 行动修正
            int toneMod = tone switch
            {
                ActionTone.Neutral       =>  0,
                ActionTone.AskHelp       => +10,
                ActionTone.Bribe         =>  +5,
                ActionTone.Threat        => -25,
                ActionTone.MentionSecret => -40,
                ActionTone.Attack        => -60,
                _ => 0
            };

            // 3. 目标 / 价值观匹配修正
            int goalMod = 0;
            string values = npc.values ?? "";
            string goal = npc.current_goal ?? "";
            if (tone == ActionTone.AskHelp &&
                (ContainsAny(values, "利", "权", "势") || ContainsAny(goal, "利", "权", "势", "豪杰", "交游")))
                goalMod = +10;
            if (tone == ActionTone.Threat &&
                (ContainsAny(values, "义", "忠", "节") || ContainsAny(goal, "义", "忠", "节")))
                goalMod -= 15;
            if (tone == ActionTone.Bribe &&
                ContainsAny(values, "廉", "洁", "清"))
                goalMod -= 10;

            // 4. 总分（夹到 -100 ~ +100）
            int total = baseScore + toneMod + goalMod;
            if (total > 100) total = 100;
            if (total < -100) total = -100;

            hint.CooperationScore = total;
            hint.Tendency = total switch
            {
                >=  50 => "配合",
                >=  10 => "中立偏向配合",
                >= -10 => "中立",
                >= -30 => "警惕",
                >= -60 => "拒绝",
                _      => "翻脸"
            };
            hint.Emotion = tone switch
            {
                ActionTone.Attack        => "愤怒",
                ActionTone.Threat        => "警惕",
                ActionTone.MentionSecret => "警觉",
                ActionTone.Bribe         => total >= 0 ? "平静" : "轻蔑",
                ActionTone.AskHelp       => total >= 0 ? "平静" : "警惕",
                _ => "平静"
            };
            hint.Reason = $"关系={relationValue}(基线{baseScore}), 行动={tone}({toneMod}), 目标匹配={goalMod}";
            hint.HardRefuse = total < -60;
            return hint;
        }

        // ──────────── 批量计算（主入口） ────────────

        /// <summary>
        /// 从场景 NPC 名字列表计算所有 hint。找不到 CharEntry 的 NPC 跳过（不报错）。
        /// </summary>
        public static List<BehaviorHint> ComputeAll(
            IEnumerable<string> sceneNpcNames,
            string playerText)
        {
            var result = new List<BehaviorHint>();
            if (sceneNpcNames == null) return result;
            var tone = ClassifyAction(playerText);
            var mem = GameMemory.Instance;
            foreach (var name in sceneNpcNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var entry = FindChar(mem, name.Trim());
                if (entry == null) continue;
                result.Add(Compute(entry, tone));
            }
            return result;
        }

        /// <summary>
        /// 把 hint 列表拼成一段注入文本（不含标题，由调用方加）。
        /// </summary>
        public static string FormatHints(IEnumerable<BehaviorHint> hints)
        {
            if (hints == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var h in hints) sb.AppendLine(h.ToPromptLine());
            var text = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // ──────────── 内部工具 ────────────

        private static CharEntry FindChar(GameMemory mem, string name)
        {
            if (mem == null || mem.Chars == null) return null;
            for (int i = 0; i < mem.Chars.Count; i++)
            {
                if (string.Equals(mem.Chars[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return mem.Chars[i];
            }
            return null;
        }

        private static bool ContainsAny(string s, params string[] keys)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < keys.Length; i++)
                if (s.Contains(keys[i])) return true;
            return false;
        }

        private static int SafeInt(int? v) => v ?? 0;
    }
}