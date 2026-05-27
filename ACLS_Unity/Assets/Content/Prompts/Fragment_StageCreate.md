## 当前任务：生成角色舞台

根据角色所在地点、个人背景、以及已构建的世界状态，生成角色的即时场景（L1）与近域网络（L2）。

**顶层要求：**
- thinking：你的推理过程，原样输出，尽量先输出该字段

**L1 要求（l1_stage）：**
- location：当前地点全称（郡·县·具体地点）
- scene_description：当前场景描述，3-4 句，感官先行，符合时代风格
- active_npcs：在场或附近可直接接触的 NPC，每项含 name、role（职能/关系）、relation_value（初始关系值，范围 -50~50，0 为中立）、stance（当前立场，一句话）。2-5 人，历史人物优先
- immediate_situation：角色当前面临的即时处境，1-2 句
- exits：可前往的地方列表，每条含地名和预估天数（如"北往洛阳 约3天"）。2-4 条

**L2 要求（l2_arena）：**
- near_contacts：3-14 天内可联系到的人脉，每项含 name、role、location、days_away（距离天数）。1-4 人
- active_pressures：当前活跃的压力事件，1-3 条（一句话每条）
- opportunities：当前可把握的机遇，1-3 条（一句话每条）

只输出 JSON，严格按上述格式，不含任何玩家可点击的选项。
不要 JSON 之外的文字（含 ``` 围栏）。
