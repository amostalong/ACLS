namespace ACLS.Sim.Narrative
{
    /// <summary>
    /// NPC 对当前玩家行动的行为倾向。
    /// 由 BehaviorHintCalculator 纯本地计算（不调 LLM），
    /// 序列化为文本后注入 Prompt，让 LLM 生成对话时遵循。
    /// </summary>
    public sealed class BehaviorHint
    {
        public string NpcName;
        public int CooperationScore;     // -100 ~ +100，越高越配合
        public string Tendency;          // 配合 / 中立偏向配合 / 中立 / 警惕 / 拒绝 / 翻脸
        public string Emotion;           // 平静 / 愤怒 / 警惕 / 警觉 / 轻蔑
        public string Reason;            // 计算依据，让 LLM 理解为什么
        public bool HardRefuse;          // true = LLM 不允许让此 NPC 配合玩家

        /// <summary>
        /// 序列化为注入 prompt 的单行文本。
        /// </summary>
        public string ToPromptLine()
        {
            var line = $"  - {NpcName}: {Tendency}（{Emotion}）, 配合度={CooperationScore}, 依据={Reason}";
            if (HardRefuse)
                line += " [硬约束：此 NPC 此刻绝对不配合]";
            return line;
        }
    }
}