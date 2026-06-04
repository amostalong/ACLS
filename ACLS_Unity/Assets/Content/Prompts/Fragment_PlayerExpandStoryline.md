## 当前任务：角色丰富化 + 故事线生成

基于已有的世界观设定、宏观势力格局、区域背景和近域网络数据，你的任务是：
1. **丰富主角设定**——生成更深入的角色背景、价值观、目标、秘密、人脉、已知情报和随身物品
2. **生成 1-2 条故事线**——这些故事线将作为后续 L1（当前场景）构建的叙事根基

## 输出要求（严格）

只输出一个 JSON 对象，包含以下顶层字段（尽量先输出 thinking 字段）：

```json
{
  "thinking": "你的推理过程，尽量先输出",

  "narration": "2~3 句叙事文本，以 DM 口吻向玩家描述角色的丰富背景如何浮现，",
  "如「随着你静下心来审视自己的处境，一些念头渐渐清晰…」",

  "player_expansion": {
    "background_story": "2-4 句角色背景叙事，应与此前已确定的角色设定一致",
    "values": "角色的核心价值观，1 句",
    "current_goal": "角色当前最想达成的目标，1 句",
    "secret": "角色的一个秘密或软肋，1 句",
    "connections": ["人脉1", "人脉2"],
    "known_facts": ["角色知道的情报1", "情报2"],
    "owned_items": ["随身物品1", "物品2"]
  },

  "storylines": [
    {
      "title": "故事线标题，10 字以内",
      "summary": "故事线概述，1-2 句，交代核心冲突与走向",
      "involved_npcs": ["涉及的主要NPC名字"],
      "involved_items": ["涉及的关键物品"],
      "involved_locations": ["涉及的重要地点"],
      "key_time_point": "关键时间点（如184年3月，或「三日后的月圆之夜」）",
      "hook": "当前可直接切入故事线的具体切入点/机会"
    }
  ],

  "npc_expansions": [
    {
      "name": "NPC 姓名",
      "background_story": "该 NPC 的背景叙事，1-2 句",
      "values": "该 NPC 的核心价值观，1 句",
      "current_goal": "该 NPC 当前最想达成的目标，1 句",
      "secret": "该 NPC 的一个秘密或软肋，1 句"
    }
  ]
}
```

不要 JSON 之外的任何文字（包括 ``` 围栏）。

---

## 已构建的世界上下文

### 世界观设定
{world_setting_context}

### 宏观势力格局（L4）
{l4_context}

### 区域格局（L3）
{l3_context}

### 近域网络（L2）
{l2_context}

### 主角信息
{player_context}

## 规则

- 所有内容必须与已有上下文一致，不可编造与已有设定矛盾的内容
- player_expansion 的 connections/known_facts/owned_items 应结合 L2 数据（chars/factions/places）生成，体现角色在当前环境中的真实位置
- storylines 每条的 involved_npcs 必须来自已有的 L2 chars 或宏观/区域势力中的人物
- storylines 的 involved_items / involved_locations 需结合上下文推导，确保与世界观一致
- storylines 的 key_time_point 应给出具体时间（如「184年3月」「三日后」），便于后续 L1 场景编排时间线
- storylines 的 hook 要具体，能直接用于引导 L1 的初始场景设计
- 1-2 条故事线，每条要有不同的冲突方向
- npc_expansions 应覆盖 players 周围的核心人物（L2 chars 中关系密切的角色），2-4 人为宜
- npc_expansions 的内容需与已有的 L2 数据和 player_expansion 的内容一致，不可凭空编造
