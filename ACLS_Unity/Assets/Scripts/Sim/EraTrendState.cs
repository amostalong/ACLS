using System;
using System.Collections.Generic;

namespace ACLS.Sim
{
    // 时代大势(主线)运行时状态。
    //
    // 与 World.Stage 关系:
    //   - World.Stage.L4World  = LLM 一次性生成的大段文本(冷)
    //   - World.EraTrend       = 结构化锚点表 + 推进状态(可序列化、机器可读)
    //
    // 设计原则:
    //   - 史实锚点只读,LLM 不可改写 (符合 game-design.md 10.2 历史真实性底线)
    //   - 前兆渗透走规则表驱动,不调 LLM 生成
    [Serializable]
    public sealed class EraTrendState
    {
        // 当前所处历史阶段名(中平X年·XX 期)。由 EraTrendService 根据日期推断。
        public string CurrentStageName = "";

        // 本剧本的硬锚点表（启动时由 EraTrendInjector 写入；非序列化，运行时从 EraTrendAnchors 取）。
        [NonSerialized] public List<EraAnchorDef> ActiveAnchors = new List<EraAnchorDef>();

        // 已触发的硬锚点 id(去重记录)
        public List<string> TriggeredAnchorIds = new List<string>();

        // 已注入但未触发的所有前兆。注入一次后即写盘,避免重复注入。
        public List<ForeshadowingEntry> ForeshadowingInjected = new List<ForeshadowingEntry>();

        // 调试/追溯:记录所有触发事件(锚点 + 前兆 → 日期)
        public List<TimelineLog> Timeline = new List<TimelineLog>();
    }

    // 注入的前兆条目。TargetLayer 指渗入哪一层(L1=现场、L2=近域、L3=区域),
    // 对应到 World.Stage 的 L1/L2/L3 文本,以及 LLM prompt 中的"近期"片段。
    [Serializable]
    public struct ForeshadowingEntry
    {
        public string AnchorId;          // 来源硬锚点 id
        public string TargetLayer;       // "L1" / "L2" / "L3"
        public int DaysBeforeAnchor;     // 距锚点还有多少天注入
        public string Template;          // 模板文案(规则表里的)
        public GameDate InjectedAt;      // 注入日
    }

    [Serializable]
    public struct TimelineLog
    {
        public GameDate Date;
        public string Kind;              // "anchor_triggered" / "foreshadowing_injected"
        public string AnchorId;
        public string Detail;            // 自由文本(锚点标题 / 前兆模板)
    }
}
