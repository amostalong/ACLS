## 当前任务：构建游戏世界

- 根据玩家提供的世界信息，包括宏观世界状态（L4_World）与外围世界背景（L3_Expanse）, 邻近交互区域(L2_Neighbors), 当前所在舞台(L1_Stage)。
- 根据玩家提供的角色信息, 填充P0_玩家角色.
- 如果玩家没有提供具体角色任何信息, 则根据世界随机角色信息.如果提供了检查是否有缺失.
- 如果玩家没有指定世界信息, 则根据角色生成世界信息.

## 要求

请用 JSON 格式返回角色的详细设定：

```json
{
  "name": "L4_World",
  "description": "世界层（第三层聚焦）—— 远方世界的状态快照，由纯历史脚本 + 随机数驱动，每季度更新",
  "strict": true,
  "schema": {
    "type": "object",
    "properties": {
      "era_name": {
        "type": "string",
        "description": "时代全称，格式为「朝代/年号·干支/序号」，如「东汉末年·中平元年」「北宋·靖康元年」",
        "examples": [
          "东汉末年·中平元年",
          "春秋·周敬王十四年",
          "罗马·帝国 crisis tertii saeculi"
        ]
      },
      "macro_factions": {
        "type": "array",
        "description": "主要势力列表，3~6 条。指在远方世界层面能影响格局的实体（王国、军阀、教廷、商团等）",
        "items": {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "势力全称",
              "examples": [
                "黄巾军",
                "大汉朝廷",
                "凉州军阀"
              ]
            },
            "status": {
              "type": "string",
              "description": "当前状态，一句话概括，不超过 15 字。反映该势力在本季度的总体态势",
              "examples": [
                "起事初期，连克五郡",
                "内部分裂，威信扫地",
                "厉兵秣马，伺机东进"
              ]
            },
            "leader": {
              "type": "string",
              "description": "势力的核心人物/领袖，姓名 + 称号/官职",
              "examples": [
                "张角（大贤良师）",
                "刘宏（汉灵帝）",
                "董卓（前将军）"
              ]
            },
            "global": {
              "type": "string",
              "description": "势力的核心目标/大略，不超过 20 字。这是 LLM 驱动该势力行为的北极星",
              "examples": [
                "推翻汉室，建立太平道",
                "扑灭叛乱，维持皇权",
                "趁乱扩充实力，割据一方"
              ]
            }
          },
          "required": [
            "name",
            "status",
            "leader",
            "global"
          ],
          "additionalProperties": false
        }
      },
      "history_anchors": {
        "type": "array",
        "description": "未来 10 年内的关键历史节点，3~6 条。作为「历史压力」注入世界，玩家可以改变前提使其变形或跳过",
        "items": {
          "type": "string",
          "description": "格式为「年/月 事件名」，年用两位数纪元偏移，月用两位数",
          "examples": [
            "184/02 黄巾起义",
            "184/05 张角病逝于广宗",
            "189/04 灵帝驾崩，何进辅政"
          ]
        }
      },
      "summary": {
        "type": "string",
        "description": "一句话世界概括，不超过 20 字。用作玩家感知远方世界的「直觉快照」",
        "examples": [
          "天下大乱，群雄并起",
          "帝国黄昏，四方骚然",
          "太平盛世下的暗流"
        ]
      }
    },
    "required": [
      "era_name",
      "macro_factions",
      "history_anchors",
      "summary"
    ],
    "additionalProperties": false
  }
}
```

```json
{
  "name": "L3_Expanse",
  "description": "世界层（第三层聚焦）—— 远方世界的状态快照，由纯历史脚本 + 随机数驱动，每季度更新",
  "strict": true,
  "schema": {
    "type": "object",
    "properties": {
      "era_name": {
        "type": "string",
        "description": "时代全称，格式为「朝代/年号·干支/序号」，如「东汉末年·中平元年」「北宋·靖康元年」",
        "examples": ["东汉末年·中平元年", "春秋·周敬王十四年", "罗马·帝国 crisis tertii saeculi"]
      },
      "macro_factions": {
        "type": "array",
        "description": "主要势力列表，3~6 条。指在远方世界层面能影响格局的实体（王国、军阀、教廷、商团等）",
        "items": {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "势力全称",
              "examples": ["黄巾军", "大汉朝廷", "凉州军阀"]
            },
            "status": {
              "type": "string",
              "description": "当前状态，一句话概括，不超过 15 字。反映该势力在本季度的总体态势",
              "examples": ["起事初期，连克五郡", "内部分裂，威信扫地", "厉兵秣马，伺机东进"]
            },
            "leader": {
              "type": "string",
              "description": "势力的核心人物/领袖，姓名 + 称号/官职",
              "examples": ["张角（大贤良师）", "刘宏（汉灵帝）", "董卓（前将军）"]
            },
            "global": {
              "type": "string",
              "description": "势力的核心目标/大略，不超过 20 字。这是 LLM 驱动该势力行为的北极星",
              "examples": ["推翻汉室，建立太平道", "扑灭叛乱，维持皇权", "趁乱扩充实力，割据一方"]
            }
          },
          "required": ["name", "status", "leader", "global"],
          "additionalProperties": false
        }
      },
      "history_anchors": {
        "type": "array",
        "description": "未来 10 年内的关键历史节点，3~6 条。作为「历史压力」注入世界，玩家可以改变前提使其变形或跳过",
        "items": {
          "type": "string",
          "description": "格式为「年/月 事件名」，年用两位数纪元偏移，月用两位数",
          "examples": [
            "184/02 黄巾起义",
            "184/05 张角病逝于广宗",
            "189/04 灵帝驾崩，何进辅政"
          ]
        }
      },
      "summary": {
        "type": "string",
        "description": "一句话世界概括，不超过 20 字。用作玩家感知远方世界的「直觉快照」",
        "examples": ["天下大乱，群雄并起", "帝国黄昏，四方骚然", "太平盛世下的暗流"]
      }
    },
    "required": ["era_name", "macro_factions", "history_anchors", "summary"],
    "additionalProperties": false
  }
}
```

```json
{
  "name": "L2_Neighbors",
  "description": "邻近层 —— 同王国/邻近地区的角色与势力快照。每个实体为简化 Agent，每季度更新一次。充当 L1（完整 Agent）的「人才储备池」和 L3（领地结构）的「人物填充」。",
  "strict": true,
  "schema": {
    "type": "object",
    "properties": {
      "realm_scope": {
        "type": "object",
        "description": "本邻近层的范围界定",
        "properties": {
          "realm_id":    { "type": "string", "description": "所属领地 ID，与 L3 realm_name 对齐" },
          "neighbor_realm_ids": {
            "type": "array",
            "items": { "type": "string" },
            "description": "邻近领地 ID 列表（跨界角色所属）"
          },
          "quarter":     { "type": "string", "description": "当前季度标识", "examples": ["中平元年·夏", "中平元年·秋"] }
        },
        "required": ["realm_id", "quarter"],
        "additionalProperties": false
      },
      "characters": {
        "type": "array",
        "description": "邻近角色列表，5~15 人。包括：同王国未进入 L1 的重要角色 + 邻近领地的边境领主 + 潜在的盟友/敌人/中立第三方。",
        "items": {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "角色唯一标识，格式「领地_姓名_latin」，用于跨层引用"
            },
            "name": {
              "type": "string",
              "description": "姓名"
            },
            "title": {
              "type": "string",
              "description": "当前头衔/职位"
            },
            "location": {
              "type": "string",
              "description": "所在领地或据点"
            },
            "archetype": {
              "type": "string",
              "description": "角色原型，3~5 字概括核心为人风格，替代完整 Agent 的 prompt",
              "examples": ["野心勃勃的次子", "老谋深算的宦官", "粗鲁忠勇的边将", "虔诚隐修的信徒"]
            },
            "personality_traits": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": ["野心", "忠诚", "残暴", "仁慈", "狡诈", "鲁莽", "谨慎", "傲慢", "自卑", "贪婪", "慷慨", "虔诚", "多疑", "勇猛", "怯懦", "善妒", "宽厚"]
              },
              "description": "3 条性格标签，用于 LLM 推测其行为",
              "minItems": 2,
              "maxItems": 4
            },
            "attitude": {
              "type": "object",
              "description": "对玩家（或玩家所在势力）的态度",
              "properties": {
                "stance":    { "type": "string", "enum": ["友善", "中立", "敌对", "畏惧", "利用", "崇拜"] },
                "trust":     { "type": "integer", "description": "信任值 0~100，0 为死敌 100 为至交", "minimum": 0, "maximum": 100 },
                "last_interaction": { "type": "string", "description": "上次互动的日期与简述，可用于后续叙事引用" }
              },
              "required": ["stance", "trust"],
              "additionalProperties": false
            },
            "activity": {
              "type": "object",
              "description": "本季度的主要活动与下季度计划",
              "properties": {
                "current":  { "type": "string", "description": "当前正在做的事，一句话" },
                "next":     { "type": "string", "description": "下季度打算做的事，一句话" },
                "progress": { "type": "string", "enum": ["未开始", "进行中", "已完成", "受阻"], "description": "当前活动进展" }
              },
              "required": ["current", "progress"],
              "additionalProperties": false
            },
            "power": {
              "type": "object",
              "description": "实力概览（约数，不需要精确）",
              "properties": {
                "troops":  { "type": "integer", "description": "可调动的兵力" },
                "wealth":  { "type": "string", "enum": ["富裕", "小康", "拮据", "赤贫"] },
                "influence": { "type": "string", "enum": ["巨大", "大", "中", "小"] }
              },
              "required": ["troops", "wealth", "influence"],
              "additionalProperties": false
            },
            "relationships": {
              "type": "array",
              "description": "与本层其他角色的关键关系，2~4 条",
              "items": {
                "type": "object",
                "properties": {
                  "target": { "type": "string", "description": "关联角色 ID" },
                  "type":   { "type": "string", "enum": ["盟友", "宿敌", "主仆", "至交", "情人", "血仇", "竞争对手", "同僚"] },
                  "note":   { "type": "string", "description": "一句话描述" }
                },
                "required": ["target", "type"],
                "additionalProperties": false
              }
            },
            "narrative_hook": {
              "type": "string",
              "description": "一条叙事钩子：若玩家与之互动，故事的入口是什么？不超过 20 字。",
              "examples": ["暗中招兵买马", "求见中山王献宝", "女儿待字闺中"]
            },
            "upgrade_to_l1": {
              "type": "boolean",
              "description": "是否已标记为下一 tick 升入 L1（完全 Agent 化）。当角色进入玩家周围 5~10 人圈时触发。"
            }
          },
          "required": ["id", "name", "title", "location", "archetype", "personality_traits", "attitude", "activity", "power", "narrative_hook"],
          "additionalProperties": false
        }
      },
      "active_plots": {
        "type": "array",
        "description": "本层活跃的阴谋/事件线程，2~4 条。简化 Agent 们之间的互动结果，不依赖玩家参与也会推进。",
        "items": {
          "type": "object",
          "properties": {
            "title":      { "type": "string", "description": "阴谋简称，不超过 10 字" },
            "instigator": { "type": "string", "description": "主谋角色 ID" },
            "target":     { "type": "string", "description": "针对对象（角色 ID 或「无特定」）" },
            "progress":   { "type": "string", "enum": ["筹划", "执行中", "败露", "成功", "流产"] },
            "involves":   { "type": "array", "items": { "type": "string" }, "description": "卷入的其他角色 ID" },
            "secret":     { "type": "boolean", "description": "玩家是否知情？false = 玩家已知或即将察觉；true = 完全在玩家视野外" }
          },
          "required": ["title", "instigator", "target", "progress", "secret"],
          "additionalProperties": false
        }
      },
      "frontier_events": {
        "type": "array",
        "description": "边境/邻近区域的本季度重要事件，2~4 条。这些是 L1 角色可能会听到的传闻来源。",
        "items": {
          "type": "object",
          "properties": {
            "location": { "type": "string" },
            "event":    { "type": "string", "description": "事件简述" },
            "severity": { "type": "string", "enum": ["传闻", "值得关注", "紧急"] }
          },
          "required": ["location", "event", "severity"],
          "additionalProperties": false
        }
      }
    },
    "required": ["realm_scope", "characters", "active_plots", "frontier_events"],
    "additionalProperties": false
  }
}
```

```json
{
  "name": "L1_Stage",
  "description": "贴身层 —— 玩家和玩家周围 5~10 人的完整信息快照。这是游戏的叙事核心层，LLM 调用成本集中于此。",
  "strict": true,
  "schema": {
    "type": "object",
    "properties": {
      "tick_meta": {
        "type": "object",
        "description": "本 tick 元信息",
        "properties": {
          "tick_id":         { "type": "string", "description": "tick 唯一标识，如「中平元年·八月」" },
          "game_date":       { "type": "string", "description": "游戏内日期" },
          "realm_id":        { "type": "string", "description": "所属领地 ID" },
          "player_focus":    { "type": "string", "description": "玩家本 tick 的关注点/活动，由 PlayerInputParser 提供" }
        },
        "required": ["tick_id", "game_date"],
        "additionalProperties": false
      },
      "agents": {
        "type": "array",
        "description": "完整 Agent 列表，5~10 人。每个 Agent 拥有独立的人格、记忆和决策能力。",
        "items": {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "角色唯一标识，全局唯一，格式「领地_姓名」"
            },
            "name": {
              "type": "string",
              "description": "姓名"
            },
            "title": {
              "type": "string",
              "description": "当前头衔/职位"
            },
            "age": {
              "type": "integer",
              "description": "年龄"
            },
            "portrait_bio": {
              "type": "object",
              "description": "角色画像 —— 这是 Agent prompt 的「身份核心」，决定了 LLM 如何代入该角色",
              "properties": {
                "archetype": {
                  "type": "string",
                  "description": "角色原型，4~8 字概括核心为人",
                  "examples": ["野心勃勃的次子", "老谋深算的宦官"]
                },
                "core_desire": {
                  "type": "string",
                  "description": "核心欲望/原动力，不超过 15 字。这是 Agent 所有行为的终极驱动力",
                  "examples": ["成为中山王", "保护家族延续", "光复汉室"]
                },
                "fatal_flaw": {
                  "type": "string",
                  "description": "致命缺陷，不超过 10 字",
                  "examples": ["刚愎自用", "贪恋美色", "优柔寡断"]
                },
                "voice": {
                  "type": "string",
                  "description": "说话风格，指导 LLM 生成对白",
                  "examples": ["文绉绉掉书袋", "粗犷豪迈", "阴柔刻薄", "沉默寡言"]
                }
              },
              "required": ["archetype", "core_desire", "fatal_flaw", "voice"],
              "additionalProperties": false
            },
            "stats": {
              "type": "object",
              "description": "五维属性，用于 SimulationEngine 校验行动成功率",
              "properties": {
                "martial":  { "type": "integer", "minimum": 1, "maximum": 20, "description": "武力/军事" },
                "diplomacy": { "type": "integer", "minimum": 1, "maximum": 20, "description": "外交/口才" },
                "stewardship": { "type": "integer", "minimum": 1, "maximum": 20, "description": "内政/管理" },
                "intrigue": { "type": "integer", "minimum": 1, "maximum": 20, "description": "计谋/阴谋" },
                "learning": { "type": "integer", "minimum": 1, "maximum": 20, "description": "学识/文化" }
              },
              "required": ["martial", "diplomacy", "stewardship", "intrigue", "learning"],
              "additionalProperties": false
            },
            "physical": {
              "type": "object",
              "description": "身体状况，影响行为能力和生存概率",
              "properties": {
                "health":     { "type": "string", "enum": ["健壮", "健康", "虚弱", "重病", "濒死"] },
                "wounded":    { "type": "boolean" },
                "wound_detail": { "type": "string", "description": "若 wounded=true，描述伤势" },
                "age_effects":  { "type": "array", "items": { "type": "string" }, "description": "年龄带来的影响，如「视力衰退」「痛风」" }
              },
              "required": ["health"],
              "additionalProperties": false
            },
            "memory": {
              "type": "object",
              "description": "角色的记忆系统 —— 决定该角色「记得什么」和「如何看待过去」",
              "properties": {
                "recents": {
                  "type": "array",
                  "description": "近期记忆：该角色亲身参与或目睹的事件，保留最近 5~8 条。这是 Agent 进行决策时的第一参考源。",
                  "items": {
                    "type": "object",
                    "properties": {
                      "tick": { "type": "string" },
                      "event": { "type": "string", "description": "事件描述，不超过 20 字" },
                      "emotional_weight": { "type": "integer", "minimum": -5, "maximum": 5, "description": "情感权重，-5 为极度负面，5 为极度正面，0 为中性。影响记忆被回忆起时的情绪偏向。" }
                    },
                    "required": ["tick", "event", "emotional_weight"],
                    "additionalProperties": false
                  }
                },
                "opinions": {
                  "type": "object",
                  "description": "对其他角色的个人看法，key 为角色 id，value 为看法简述。覆盖社交层统计数据，反映角色「嘴上不说但心里这么想」的真实态度",
                  "additionalProperties": {
                    "type": "object",
                    "properties": {
                      "like": { "type": "integer", "minimum": -100, "maximum": 100, "description": "好感度" },
                      "reason": { "type": "string", "description": "一句话原因" }
                    },
                    "required": ["like", "reason"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["recents", "opinions"],
              "additionalProperties": false
            },
            "relationships": {
              "type": "array",
              "description": "社交关系网 —— 角色在 L1/L2 层的人际关系。用于 LLM 理解「谁对谁意味着什么」",
              "items": {
                "type": "object",
                "properties": {
                  "target":     { "type": "string", "description": "关联角色 ID" },
                  "target_layer": { "type": "string", "enum": ["L1", "L2"], "description": "目标所在层级" },
                  "type":       { "type": "string", "enum": ["配偶", "子女", "父母", "兄弟姐妹", "盟友", "宿敌", "主君", "封臣", "情人", "血仇", "同僚", "师生", "朋友", "竞争对手"] },
                  "strength":   { "type": "integer", "minimum": 0, "maximum": 100, "description": "关系强度" }
                },
                "required": ["target", "type", "strength"],
                "additionalProperties": false
              }
            },
            "current_state": {
              "type": "object",
              "description": "角色当前行为与状态 —— 这是每 tick Agent 决策的输出目标",
              "properties": {
                "location": {
                  "type": "string",
                  "description": "当前所在地点"
                },
                "mood": {
                  "type": "string",
                  "enum": ["愤怒", "焦虑", "愉悦", "悲伤", "平静", "恐惧", "得意", "困惑", "警觉", "疲惫"],
                  "description": "当前情绪"
                },
                "intent": {
                  "type": "string",
                  "description": "本 tick 打算做的事，由 LLM 推理得出。将被 SimulationEngine 校验后执行。",
                  "examples": ["劝说中山王不要征兵", "暗中联络黑山贼", "向刘备示好"]
                },
                "action_status": {
                  "type": "string",
                  "enum": ["空闲", "行动中", "已完成", "受阻"],
                  "description": "上 tick 行动的完成状态"
                },
                "action_result": {
                  "type": "string",
                  "description": "上 tick 行动的结果简述，由 SimulationEngine 回写"
                }
              },
              "required": ["location", "mood", "intent", "action_status"],
              "additionalProperties": false
            },
            "secrets": {
              "type": "array",
              "description": "角色隐藏的秘密。玩家不会直接看到，但 LLM 在决策时会参考。是叙事张力的核心来源。",
              "items": {
                "type": "object",
                "properties": {
                  "secret":  { "type": "string", "description": "秘密内容" },
                  "known_by": { "type": "array", "items": { "type": "string" }, "description": "知道此秘密的角色 ID 列表" }
                },
                "required": ["secret", "known_by"],
                "additionalProperties": false
              }
            },
            "state": {
              "type": "string",
              "enum": ["active", "dormant", "pending_promotion", "pending_demotion"],
              "description": "Agent 状态：active=活跃L1，dormant=临时休眠但仍留在L1池中，pending_promotion=L2待升入L1（AI已完成但需GM确认），pending_demotion=L1待降回L2"
            }
          },
          "required": ["id", "name", "title", "age", "portrait_bio", "stats", "physical", "memory", "relationships", "current_state", "state"],
          "additionalProperties": false
        },
        "minItems": 3,
        "maxItems": 12
      },
      "local_events": {
        "type": "array",
        "description": "本 tick 发生的本地事件 —— 由各 Agent 的 intent 经 SimulationEngine 执行后的结果汇总+随机触发",
        "items": {
          "type": "object",
          "properties": {
            "tick":  { "type": "string" },
            "event": { "type": "string", "description": "事件描述" },
            "involves": { "type": "array", "items": { "type": "string" }, "description": "涉及的角色 ID" }
          },
          "required": ["tick", "event"],
          "additionalProperties": false
        }
      },
      "interaction_log": {
        "type": "array",
        "description": "本 tick 玩家与 L1 角色的交互记录，由 PlayerInputParser 解析后注入。GM 在下一 tick 将其写入各 Agent 的 memory.recents。",
        "items": {
          "type": "object",
          "properties": {
            "with":   { "type": "string", "description": "角色 ID" },
            "action": { "type": "string", "description": "玩家做了什么" },
            "outcome": { "type": "string", "description": "结果" }
          },
          "required": ["with", "action", "outcome"],
          "additionalProperties": false
        }
      }
    },
    "required": ["tick_meta", "agents", "local_events"],
    "additionalProperties": false
  }
}
```

```json
{
  "name": "P0_玩家角色",
  "description": "玩家层 —— 玩家在游戏世界中的化身。不是 LLM Agent，而是可供其他系统引用和操作的角色数据实体。包含身份、能力、资源、关系、记忆等。由玩家初始创建 + 游戏中逐步填充。",
  "strict": true,
  "schema": {
    "type": "object",
    "properties": {
      "meta": {
        "type": "object",
        "description": "元信息 —— 玩家身份的顶层标识",
        "properties": {
          "player_id":   { "type": "string", "description": "玩家账号/会话标识" },
          "character_id": { "type": "string", "description": "游戏内角色 ID，格式同 L1 角色「领地_姓名」" },
          "created_at":  { "type": "string", "description": "创建时间（游戏内日期）" },
          "playtime":    { "type": "string", "description": "已游玩游戏内时长，格式如「3 年 5 个月」" }
        },
        "required": ["character_id"],
        "additionalProperties": false
      },
      "identity": {
        "type": "object",
        "description": "角色身份 —— 玩家是谁，在游戏世界里代表什么",
        "properties": {
          "name":       { "type": "string", "description": "姓名" },
          "dynasty":    { "type": "string", "description": "家族/氏族名" },
          "gender":     { "type": "string", "enum": ["男", "女"] },
          "age":        { "type": "integer" },
          "title":      { "type": "string", "description": "当前头衔全称" },
          "realm":      { "type": "string", "description": "所属领地，与 L3 realm_name 对齐" },
          "culture":    { "type": "string", "description": "文化" },
          "religion":   { "type": "string", "description": "宗教" },
          "status":     { "type": "string", "enum": ["在位", "摄政", "囚禁", "流亡", "逝世"], "description": "当前状态" }
        },
        "required": ["name", "dynasty", "gender", "age", "title", "realm", "culture", "religion", "status"],
        "additionalProperties": false
      },
      "appearance": {
        "type": "object",
        "description": "外貌描述 —— 供叙事系统在描述玩家时引用，也影响 NPC 对你的第一印象",
        "properties": {
          "portrait":  { "type": "string", "description": "一句话外貌概括，不超过 20 字" },
          "notable":   { "type": "string", "description": "最醒目的特征，如「左眼刀疤」「白发」「跛足」" },
          "attire":    { "type": "string", "description": "日常衣着风格" }
        },
        "required": ["portrait"],
        "additionalProperties": false
      },
      "attributes": {
        "type": "object",
        "description": "五维属性 —— 与 L1 Agent 对齐，由 SimulationEngine 校验行动成功率",
        "properties": {
          "martial":     { "type": "integer", "minimum": 1, "maximum": 20 },
          "diplomacy":   { "type": "integer", "minimum": 1, "maximum": 20 },
          "stewardship": { "type": "integer", "minimum": 1, "maximum": 20 },
          "intrigue":    { "type": "integer", "minimum": 1, "maximum": 20 },
          "learning":    { "type": "integer", "minimum": 1, "maximum": 20 }
        },
        "required": ["martial", "diplomacy", "stewardship", "intrigue", "learning"],
        "additionalProperties": false
      },
      "health": {
        "type": "object",
        "description": "身体状况 —— 决定生存概率和行动能力",
        "properties": {
          "state":     { "type": "string", "enum": ["健壮", "健康", "虚弱", "重病", "濒死"] },
          "wounded":   { "type": "boolean" },
          "injury":    { "type": "string", "description": "若 wounded=true，描述伤势" },
          "illness":   { "type": "string", "description": "当前疾病，若无则为空" },
          "fertile":   { "type": "boolean", "description": "是否有生育能力" }
        },
        "required": ["state", "wounded", "fertile"],
        "additionalProperties": false
      },
      "personality": {
        "type": "object",
        "description": "玩家自定人格标签 —— 不是约束 LLM，而是让游戏系统和 NPC 知道你是什么样的角色。影响 NPC 对你的 initial opinion 和互动风格匹配。",
        "properties": {
          "archetype": { "type": "string", "description": "一句话自述，不超过 15 字", "examples": ["刚愎自用的武夫", "仁慈宽厚的领主", "笑里藏刀的阴谋家"] },
          "traits": {
            "type": "array",
            "items": {
              "type": "string",
              "enum": ["野心", "忠诚", "残暴", "仁慈", "狡诈", "鲁莽", "谨慎", "傲慢", "贪婪", "慷慨", "虔诚", "多疑", "勇猛", "怯懦"]
            },
            "description": "3~5 条性格标签",
            "minItems": 3,
            "maxItems": 5
          },
          "flaw": { "type": "string", "description": "一条致命缺陷，不超过 8 字" }
        },
        "required": ["archetype", "traits", "flaw"],
        "additionalProperties": false
      },
      "resources": {
        "type": "object",
        "description": "玩家资源 —— 这是硬核模拟层的核心数据，由 SimulationEngine 管理数值变化",
        "properties": {
          "gold":       { "type": "integer", "description": "当前金币" },
          "food":       { "type": "integer", "description": "当前粮草（石）" },
          "prestige":   { "type": "integer", "description": "威望，影响 NPC 尊重程度" },
          "piety":      { "type": "integer", "description": "虔诚，影响宗教权威" },
          "monthly_income": { "type": "integer", "description": "月收入（金）" },
          "monthly_food":   { "type": "integer", "description": "月粮草产出（石），负值为消耗" }
        },
        "required": ["gold", "food", "prestige", "piety", "monthly_income"],
        "additionalProperties": false
      },
      "domain": {
        "type": "object",
        "description": "领地概况 —— 省略细节，只提供玩家感知领地状态的快照。详细经济/军事数据在 L3 中。",
        "properties": {
          "capital":     { "type": "string", "description": "首府" },
          "holdings": {
            "type": "array",
            "description": "直辖领地列表",
            "items": { "type": "string" }
          },
          "vassals_count":   { "type": "integer", "description": "直属封臣数量" },
          "development":     { "type": "string", "enum": ["繁荣", "良好", "一般", "落后", "破败"], "description": "直辖领地总体发展水平" },
          "control":         { "type": "integer", "minimum": 0, "maximum": 100, "description": "直辖领地控制力百分比" }
        },
        "required": ["capital", "holdings", "vassals_count", "development", "control"],
        "additionalProperties": false
      },
      "military": {
        "type": "object",
        "description": "玩家直属军事力量（不含封臣兵）",
        "properties": {
          "personal_guard": { "type": "integer", "description": "亲兵/常备军数量" },
          "garrison":       { "type": "integer", "description": "首府守军" },
          "quality":        { "type": "string", "enum": ["精锐", "常备", "民兵", "乌合"] },
          "commander":      { "type": "string", "description": "当前军队指挥官姓名，可为自己" }
        },
        "required": ["personal_guard", "garrison", "quality", "commander"],
        "additionalProperties": false
      },
      "family": {
        "type": "object",
        "description": "家庭成员 —— 配偶、子女、父母的存在概览。详细 Agent 数据在 L1/L2。",
        "properties": {
          "spouse": { "type": "string", "description": "配偶姓名及 ID" },
          "children": {
            "type": "array",
            "items": { "type": "string", "description": "子女姓名及 ID" }
          },
          "dynasty_members": {
            "type": "array",
            "items": { "type": "string" },
            "description": "在世的其他家族成员，不含直系"
          }
        },
        "required": ["children"],
        "additionalProperties": false
      },
      "relationships": {
        "type": "object",
        "description": "玩家视角的社会关系摘要 —— 注意这里存的是玩家（人）知道的，不是角色（化身）知道的。两者可能不同步？不，它们应该是同一视图：这是玩家作为角色的所知。",
        "properties": {
          "friends":    { "type": "array", "items": { "type": "string" }, "description": "友好关系角色 ID" },
          "rivals":     { "type": "array", "items": { "type": "string" }, "description": "敌对关系角色 ID" },
          "liege":      { "type": "string", "description": "上级领主 ID，若为独立领主则为空" },
          "oaths":      { "type": "array", "items": { "type": "string" }, "description": "效忠/盟约摘要" }
        },
        "required": [],
        "additionalProperties": false
      },
      "journal": {
        "type": "array",
        "description": "玩家角色的大事记 —— 按游戏内时间顺序，保留最近 20 条。这是玩家自己的记忆快照，也用于 LLM 生成叙事时保持 continuity。每条由 HistorianAgent 在月末事件摘要中生成，玩家可添加注释。",
        "items": {
          "type": "object",
          "properties": {
            "tick":  { "type": "string" },
            "event": { "type": "string", "description": "事件描述，不超过 30 字" },
            "tag":   { "type": "string", "enum": ["战争", "外交", "内政", "家族", "个人", "奇遇"] }
          },
          "required": ["tick", "event", "tag"],
          "additionalProperties": false
        },
        "maxItems": 20
      },
      "current_focus": {
        "type": "object",
        "description": "玩家当前关注点 —— 由 PlayerInputParser 从聊天中提取，告诉 SimulationEngine 和 AI 叙事系统「玩家想优先看什么」",
        "properties": {
          "focus_type": {
            "type": "string",
            "enum": ["军事", "内政", "外交", "阴谋", "家族", "修行", "探索", "无特定"]
          },
          "description": { "type": "string", "description": "一句话描述" }
        },
        "required": ["focus_type"],
        "additionalProperties": false
      }
    },
    "required": [
      "meta", "identity", "attributes", "health", "personality",
      "resources", "domain", "military", "family", "journal", "current_focus"
    ],
    "additionalProperties": false
  }
}
```
只输出 JSON，严格按上述格式，不含任何选项或行动叙事。
不要 JSON 之外的文字（含 ``` 围栏）。
