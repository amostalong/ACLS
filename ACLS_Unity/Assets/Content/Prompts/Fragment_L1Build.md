## 当前任务：构建初始 L1 场景

这是世界构建的最后一步。基于已构建好的完整上下文，你直接生成当前场景。

## 已构建的世界上下文

### 世界观设定（World）
{world_build_context}

### 宏观势力格局（L4）
{l4_context}

### 区域势力格局（L3）
{l3_context}

### 近域网络（L2）
{l2_arena}

### 角色丰富化与故事线
{l2_expansion}

### 主角信息
{player_context}

## 输出要求

只输出一个 JSON 对象，包含以下顶层字段（尽量先输出 thinking 字段）：

- thinking: 你的构建推理过程
- narration: 2~4 句叙事文本，以 DM 口吻描写场景如何在你眼前展开
- l1_stage（当前场景）:
  - location: 当前地点全称
  - scene_description: 3-4 句场景描写，感官先行
  - active_npcs: 在场或附近可直接接触的角色，2-5 人，每项含 name, role, relation_value(-50~50), stance
  - immediate_situation: 主角此刻面临的即时处境，1-2 句
  - exits: 可前往的地方列表，2-4 条
- l2_arena（近域层更新——基于已有 L2 数据微调当前状态，可省略部分字段）
- memory_entries（可选）：要记录的记忆条目 [{{date, event}}]
- chars（可选）：本层涉及的人物列表，每项含 name, role, location, relation(-50~50), reachable_in_days
- factions（可选）：本层涉及的势力列表，每项含 name, type, stance
- places（可选）：本层涉及的地点列表，每项含 name, type, description

## 重要规则

- 所有内容必须与已构建的世界上下文一致，不可凭空编造。
- active_npcs 中的角色必须从已有 L2 chars 或上下文推导得出。
- 不要 JSON 之外的任何文字（包括 ``` 围栏）。
