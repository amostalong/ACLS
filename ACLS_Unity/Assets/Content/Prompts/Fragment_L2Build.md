## 当前任务：构建近域网络层（L2）

这是世界构建的第四步。基于世界观设定（World）、宏观势力（L4）和区域格局（L3），你的任务是生成**主角的个人生活圈**——短期触角所及的人脉、势力、地点、事件和机遇。

## 玩家的原始设定

[角色描述]
{role_description}

[世界描述]
{world_description}

当前的世界观设定：

{world_setting_context}

当前的宏观势力格局（L4）：

{l4_context}

当前的区域格局（L3）：

{l3_context}

你必须在以上设定框架内生成 L2 近域网络。

## 规则

- 所有内容必须与世界观设定和已构建的各层一致
- chars 是主角短期可接触到的人（邻居/同僚/乡绅/商贩/同行等），2~5 人
- factions 是主角生活范围内可见的势力/组织/家族，1~4 个
- places 是主角生活范围内的关键地点，2~5 个
- active_events 是当前正在发生的事件/压力，1~3 条
- opportunities 是当前可把握的机遇，1~3 条
- 各实体的位置（location）必须与 L3 的 region 一致

---

## 输出要求（严格）

只输出一个 JSON 对象，包含以下顶层字段（尽量先输出 thinking 字段）：

```json
{
  "thinking": "你的推理过程，尽量先输出",

  "narration": "2~3 句叙事文本，以 DM 口吻向玩家描述他身边的生活圈，如「你放眼望去，熟悉的面孔和地点映入眼帘…」",

  "chars": [
    {
      "name": "人物名称",
      "role": "职能/关系",
      "location": "所在位置",
      "relation": -50~50 的整数，0 为中立，
      "reachable_in_days": 按世界观时间单位的可联系距离，0 表示同处一地
    }
  ],

  "factions": [
    {
      "name": "势力名称",
      "type": "类型，如望族/行会/商帮/宗门等",
      "stance": "当前姿态，一句话"
    }
  ],

  "places": [
    {
      "name": "地点名称",
      "type": "类型，如县城/渡口/坞堡/关隘等",
      "description": "一句话描述"
    }
  ],

  "active_events": [
    {
      "title": "事件标题",
      "urgency": "high|medium|low",
      "deadline": "期限描述",
      "detail": "事件详情"
    }
  ],

  "opportunities": [
    "机遇1",
    "机遇2"
  ]
}
```

不要 JSON 之外的任何文字（包括 ``` 围栏）。
