## 当前任务：生成角色舞台

根据角色所在地点、个人背景、以及已构建的世界状态，生成角色的即时场景（L1）与近域网络（L2）。

**顶层要求：**
- thinking：你的推理过程，原样输出，尽量先输出该字段

**L1 要求（l1_stage）：**
- location：当前地点全称（按世界观设定格式）
- scene_description：当前场景描述，3-4 句，感官先行，符合世界观风格
- active_npcs：在场或附近可直接接触的角色，每项含 name、role（职能/关系）、relation_value（初始关系值，范围 -50~50，0 为中立）、stance（当前立场，一句话）。2-5 人
- immediate_situation：角色当前面临的即时处境，1-2 句
- exits：可前往的地方列表（字符串数组），2-4 条，每条含地名和预估时间

**L2 要求（l2_arena——玩家当前的生活圈）：**
围绕主角当前位置，构建玩家短期触角所及的生活圈：
- chars：短期可接触到的人，每项含 name、role、location、relation（-50~50）、reachable_in_days
- factions：可见的势力/组织/家族，每项含 name、type、stance（一句话）。可按以下方向判断：
  - 三国/古风：望族、官府、流寇、军阀、行会
  - 科幻：星际企业、殖民政府、海盗团、科研联盟
  - 奇幻：王室、法师协会、盗贼工会、教会
  - 现代/架空：根据设定灵活判断
- places：关键地点，每项含 name、type（如"郡治""关隘"或"空间站""矿区"）、description
- active_events：当前活跃的事件/压力，1-3 条，每项含 title、urgency（high/medium/low）、deadline、detail
- opportunities：1-3 条当前可把握的机遇（字符串数组）

只输出 JSON，严格按上述格式，不含任何玩家可点击的选项。
不要 JSON 之外的文字（含 ``` 围栏）。
