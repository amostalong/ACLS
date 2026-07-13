# 双层 LLM 叙事/记账 —— 落地计划

> 状态：Phase 0 已完成（本会话）。其余待执行。
> 范围：ACLS 对话推进的「叙事官 + 记账员」双 LLM 架构。

## 0. 关键发现（先读这条）

**这套架构在代码里其实已经实现了大半，不是从零开发。** 之前白板上聊的"叙事不发 JSON / 记账员单独抽取 / 按名字落地 / L1-L4 快照 / 触发门控"几乎都已存在：

| 设计点 | 已有实现 |
|---|---|
| 叙事官（纯文本） | `LlmDialogueOrchestrator.CompleteNarrationAndChoicesStream` → 文本流式，格式 `旁白 + 单行 --- + 1./2. 编号选项 + @effect yes/no`，由 `NarrationChoicesTextParser` 解析 |
| 记账员（JSON） | `CompleteEffectsOnly` → `LlmClient.CompleteAsync`（JSON）→ `LlmReply.TryParseEffectsOnly` |
| 触发门控 | `ShouldRequestEffectsForOpening` / `ShouldRequestEffectsForStagePlay` + `LocalEffectsHeuristic` + 叙事官回传的 `@effect yes/no` 标记 |
| World 快照 | `PromptAssembler.AssembleEffectsOnly` 注入 L1–L4 分层 + 主角信息 + `[玩家动作]` + `[本轮完整旁白]` |
| 按名字落地 | `EffectParser.ResolveCharacterId`（精确 → 包含匹配）→ `EffectOp` → `Effects.ApplyOne` |
| 对话窗口 | `History.Recent(RecentMessages)`，`RecentMessages = 20` |
| 落地管线 | `EffectRouter.Apply` → 应用 effects + 推进天数/日期 |

**真正缺的那块**，恰好就是本会话 Phase 0 加的开关：叙事官的 `CompleteNarrationAndChoicesStream` 调用 `CompleteStreamAsync` 时**没传 `jsonObject:false`**，所以它一边要求模型输出 `---` 纯文本、一边又被强制 `response_format=json_object`。这俩是打架的——文本叙事一直在被强制 JSON 拗着。

## 1. 当前一轮的真实流程（StagePlay，orchestrator ~725–789）

1. `messages = History.Recent(20)`
2. `CompleteNarrationAndChoicesStream` → 文本叙事 + 选项 + `@effect` 标记
3. 门控 `ShouldRequestEffectsForStagePlay(userInput, effectTag)` 决定是否记账
4. 若是：`CompleteEffectsOnly`（JSON）→ `TryParseEffectsOnly`
5. `BuildNarrationTextResult` 合并 → `HandleResult` → `ApplyEffects` → `EffectRouter.Apply`

## 2. 待办（分阶段）

### Phase 0 ✅（本会话已做）
`ILlmClient` 三方法加 `bool jsonObject = true`；`OpenAiCompatibleClient` 条件发射 `response_format`；`AnthropicClient` 签名同步但暂不接线。默认 true，零行为变化。

### Phase 1 —— 把叙事官切到非 JSON（一行级改动）
- 在 `CompleteNarrationAndChoicesStream`（orchestrator:859）的 `CompleteStreamAsync(...)` 调用传 `jsonObject: false`。
- 确认其余调用维持 JSON：`CompleteEffectsOnly`(896，记账)、`CompleteStreamWithThinking`(1001，世界构建)、`CompleteStreamWithTools`(1099)、`ChatBridge`(304，起名) —— 都走默认 true。
- **验证**：Editor 里跑一轮 StagePlay，叙事以纯文本流式滚出、选项正常解析、OpenAI 端不再报 json_object 相关错误。

### Phase 2 —— 重复记账（double-count）审计
- 风险点：记账员拿到的是 `Recent(20)` 条历史 + `[本轮完整旁白]`。若 prompt 不约束"只抽本轮"，同一事件会在连续多轮被重复记账，数值翻倍。
- 现状：`AssembleEffectsOnly` 已把本轮单独标成 `[玩家动作]` + `[本轮完整旁白]`，方向是对的。需核对 `EffectsOnlyFormatFor(stateType)` 这段格式 prompt（在 `Resources/Prompts/` 或 LlmPromptConfig）是否明确写了"**只针对本轮抽取，不要为历史消息生成 delta**"。
- **决策点（见 §3）**：记账调用要不要继续塞 20 条历史。

### Phase 3 —— 端到端验证
- 连玩若干轮，看日志：effects 是否被 `EffectParser` 丢弃（unsupported/malformed）、是否重复计数、名字解析未命中（`ResolveCharacterId` 返回 0）。
- 确认 `SaveManager.Save` 后 `World` JsonUtility 往返无损。
- 验收线：叙事是纯散文（无 JSON 包裹）/ 选项 1–4 可点 / effects 只记一次 / 存档往返正常 / Console 无红。

### 旁支 —— 工程文件漂移（非本次改动）
`git diff` 里出现了我没碰过的 `ProjectSettings/ProjectVersion.txt`(6 行)、`Packages/manifest.json`、`packages-lock.json`。AGENTS.md 明确 pin 了 `2022.3.62f2` 并警告新编辑器会静默升级。需确认是否误升级；若是，趁早 revert。

## 3. 需要你拍板的决策

1. **记账员的历史窗口**：现在记账调用复用 `Recent(20)`。我倾向**记账调用只发 `[本轮完整旁白]`+`[玩家动作]`+L1-L4 快照，不塞对话历史**——因为分层快照已经给了状态上下文，丢掉历史能**从结构上消除重复记账**，也更省。叙事官那条仍保留 20 条窗口不动。
2. **窗口大小 20 vs 5**：叙事官窗口维持 20，先不动（白板上提过 5，但门控+去重比窗口大小更关键）。

## 4. 下一步

执行 **Phase 1**（叙事官切 `jsonObject:false` 一行 + 验证）。这是让已有文本叙事真正生效的关键一刀，风险最低。Phase 2 的去重需要先看一眼 effects 格式 prompt 再定。
