## 当前任务：构建宏观势力层（L4）

这是世界构建的第二步。基于第一步的世界观设定，你的任务是生成**宏观层面的主要势力**——帝国/王朝级别的权力格局。

## 输出要求（严格）

只输出一个 JSON 对象，包含以下顶层字段（尽量先输出 thinking 字段）：

```json
{
  "thinking": "你的推理过程，尽量先输出",

  "narration": "2~3 句叙事文本，以 DM 口吻向玩家描述宏观势力格局如何浮现，如「在这天下大势之中，几股力量正在悄然成型…」",

  "macro_factions": [
    {
      "name": "势力名称",
      "status": "一句话态势，不超过 15 字"
    }
  ],

  "summary": "宏观格局一句话概括，不超过 20 字",

  "chars": [
    {
      "name": "人物名称",
      "role": "身份/职能",
      "location": "所在地区",
      "relation": 0,
      "reachable_in_days": 0,

      // 社会关系（可选）
      "father": "父亲姓名",
      "mother": "母亲姓名",
      "siblings": ["兄/弟/姐/妹姓名"],
      "other_relatives": ["其他亲戚姓名"],
      "core_friends": ["核心朋友姓名"],

      // 是否重要人物（可选）
      "is_important": false
    }
  ],

  "factions": [
    {
      "name": "势力名称",
      "type": "类型",
      "stance": "当前姿态"
    }
  ],

  "places": [
    {
      "name": "地点名称",
      "type": "类型",
      "description": "一句话描述"
    }
  ]
}
```

不要 JSON 之外的任何文字（包括 ``` 围栏）。

---

## 玩家的原始设定

[角色描述]
{role_description}

[世界描述]
{world_description}

当前的世界观设定如下：

{world_setting_context}

## 主角信息

{player_context}

你必须在以上设定框架内生成 L4 宏观势力。

## 规则

- 所有势力必须符合世界观设定的时代、风格和认知边界
- 势力描述需反映其在宏观格局中的真实位置和态势
- 3~6 个主要势力，覆盖不同方向和利益集团
- 不要包含后续 L3/L2 才应该出现的区域性/地方性势力
