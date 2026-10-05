# 引入 Godot 相关 agent skill（挑选自 awesome-gamedev-agent-skills）

## Goal

让 AI 在写本项目 Godot 表现层（`src/godot`）时少犯引擎层面的错（4.x 改名、API 误用），同时不引入与项目规范冲突的通用建议。

## Background

- 来源、固定提交、文件清单与校验和、审读结果见 `research/source-review.md`。
- 项目的 skill 放在 `.claude/skills/`，已入库，现有 14 个全是 trellis / openspec 的。
- 2026-10-05 通读候选 skill 正文后的评估：

| skill | 对本项目的价值 | 与项目的冲突 | 裁决 |
|---|---|---|---|
| `godot-shaders` | 中：项目里 shader 只有 4 处用法，先例最少 | 无 | 装 |
| `godot-csharp` | 低：入门内容，项目已有约 25 个 C# 节点脚本作先例 | 无 | 装 |
| `godot-ui-control` | 低：`Ui.cs` / `Hud*.cs` 已有近百处 Theme / StyleBox 用法；示例全是 GDScript | 无 | 装 |
| `godot-export` | 低：`tools/export-release.sh` 已固化 | 无 | 不装 |
| `game-feel` | 低：只有"按重要性分档"可用，`tiered-number-show` 提案已采纳 | 有：正文 24 处提到 Tween / 时间缩放 / 粒子，与"演出由 `Siege.Presentation` 按时间算出、禁过量粒子"相反 | 不装 |

- 负责人 2026-10-05 裁决：装三个引擎向的（`godot-shaders`、`godot-csharp`、`godot-ui-control`）。主会话推荐的是只装 `godot-shaders`；多装两个的代价是多占两条 skill 描述的上下文，内容与项目先例重复。

## Requirements

- R1 把上游固定提交里的 `godot-shaders`、`godot-csharp`、`godot-ui-control` 三个目录原样复制到 `.claude/skills/<名字>/`，每个目录含 `SKILL.md` 与 `references/` 下的一份文档，内容不做任何修改。
- R2 在 `.claude/third-party/awesome-gamedev-agent-skills/` 放上游的 `LICENSE` 与 `NOTICE`（原样），以及一份 `README.md`，写明上游地址、固定提交、取得日期、装了哪三个、内容未修改、升级时怎么做（重新取上游、对照校验和、更新本文件）。
- R3 不安装 router 与其余 skill；不使用 `npx skills add` 或插件市场。

## Acceptance Criteria

- [x] A1 `research/source-review.md` 表中八个目标路径全部存在，SHA-256 与表中逐个相同。
- [x] A2 `.claude/skills/` 下新增的只有这三个目录，每个目录只有两个文件；三个 `SKILL.md` 的 frontmatter `name` 与目录名一致。
- [x] A3 `.claude/third-party/awesome-gamedev-agent-skills/README.md` 含 R2 列出的六项信息，提交号与 `research/source-review.md` 一致。
- [x] A4 `git status --porcelain` 中，除本任务目录、`.claude/skills/godot-*`、`.claude/third-party/` 外没有其他变化；`src/`、`tests/`、`openspec/`、现有 14 个 skill 无改动。（规划时工作树里还有未跟踪的 `openspec/changes/tiered-number-show/`，任务期间已由另一会话提交并合入 main，不再出现。）

没有代码改动，不跑 `dotnet test`。

## Out of Scope

- `godot-export`、`game-feel` 与上游其余 skill、router。
- 任何 Godot addon（Juicee、Game Feel Flow、Phantom Camera、ThemeGen、粒子库）与 godot-mcp——2026-10-05 已评估为不加。
- 不改 `AGENTS.md`、`.trellis/spec/`：三个 skill 没有与现行规范相反的正文，不需要加优先级注记。`godot-ui-control/SKILL.md` 第 23、123 行各有一句把 UI 过渡指向未安装的 `godot-animation`（Tween），只是指路、没有用法；本项目演出不用 Tween，遇到时以 `openspec/specs/settlement-show` 为准。

## 验收记录（2026-10-05）

实现子 agent、检查子 agent、主会话各自独立克隆上游固定提交，三次 `diff -r` / `cmp` 均为逐字节相同；八个 SHA-256 与 `research/source-review.md` 相同。来源说明 `README.md` 比 R2 多带一张校验和表（任务归档后升级时仍能对照），检查方已逐值核对，保留。
