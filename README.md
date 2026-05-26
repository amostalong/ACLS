# ACLS — 三国角色模拟

CK3-like 纯文本角色/家族模拟，设定于 **184 CE 黄巾之乱**。由 LLM 驱动叙事，玩家以架空小士族子弟视角穿过三国历史。

## 快速开始

| 步骤 | 操作 |
|------|------|
| 1 | 用 **Unity 2022.3.62f2** 打开 `ACLS_Unity/` 目录 |
| 2 | 等待编译通过（Console 无红色报错） |
| 3 | 在 `Assets/Resources/` 右键 → Create → ACLS → LLM Config，命名 `LlmConfig` |
| 4 | Inspector 里填写 Provider / ApiKey / Model |
| 5 | 打开任意 Scene，按 **Play** |

> **注意：** `LlmConfig.asset` 含 API Key，已被 `.gitignore` 强制排除，请勿手动 commit。

## 游戏流程

```
选择世界  →  LLM 构建世界  →  创建角色  →  LLM 拓展角色背景
→  LLM 生成舞台  →  第一幕叙事 + 选项  →  循环推进
```

死亡后自动切换至嫡长子继承；无继承人则 Game Over。

## 项目结构

```
ACLS/
├── ACLS_Unity/                  # Unity 工程根目录
│   ├── Assets/
│   │   ├── Resources/
│   │   │   ├── Prompts/         # LLM prompt 片段 (.md)
│   │   │   └── LlmConfig.asset  # ⚠ 本地，已 gitignore
│   │   └── Scripts/
│   │       ├── Sim/             # 纯 C# 模拟核心（无 UnityEngine）
│   │       ├── Data/            # ScriptableObject 定义
│   │       ├── Llm/             # LLM 客户端 + 消息解析
│   │       ├── Authoring/       # MonoBehaviour 胶水层
│   │       │   └── Dialogue/    # 对话状态机
│   │       ├── UI/              # uGUI 视图
│   │       └── Loc/             # 本地化
│   └── Packages/manifest.json
└── README.md
```

## 世界分层（L1-L4，参考 AIGame 架构）

| 层级 | 内容 | 更新频率 |
|------|------|----------|
| L4_World | 时代背景·大势力·历史锚点 | 极少 |
| L3_Expanse | 区域势力·间接情报 | 每月 |
| L2_Arena | 3-14 天人脉·当前压力·机遇 | 每 3-7 天 |
| L1_Stage | 当前场景·在场 NPC·出口 | 每场景 |

## LLM 配置

支持两种提供商：

- **Anthropic** — 填写 ApiKey，Model 推荐 `claude-haiku-4-5`
- **OpenAI 兼容** — 填写 BaseUrl + ApiKey，兼容中转 API

## 技术栈

- Unity 2022.3.62f2 LTS · C# 9 · uGUI · TextMesh Pro
- Newtonsoft.Json（`com.unity.nuget.newtonsoft-json 3.2.1`）
- `System.Net.Http.HttpClient`（async/await，无协程）
