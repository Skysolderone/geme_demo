# 09-22-life-shape

> 规格权威：`openspec/changes/life-shape/`（design.md 含裁决记录）。本文件只承载目标、分段计划与范围边界。

## Goal

引入基准文档的活形判定与活棋禁入，并接到本仓库的地形与匠人改造上：

- 空区沿气边划分；封闭眼空间（无气边即墙、≤ `EYE_SPACE_MAX` = 12）；眼值按 design D2 表、相加得三态 死 / 未定 / 活；
- 活棋禁入：已活棋串的眼空间对非所有者禁入（预演第 1 步）；所有者可自拆；
- 不得破坏他人活形：预演提子后、自杀手前做结果导向复查，原棋子不在副本上视为失活；
- 合法落子范围扣除禁入格；公开视图、预演提示、表现层、终端、Godot 标示；遥测与分析；200 局基线。

## 分段执行计划（顺序、每段一个 implement；段末主会话前台复核并提交）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A | 0 + 1 | 前置核对；活形分析（Siege.Core/Board 纯计算）、禁入格、查询、性能基线 | `dotnet build` 零警告、`dotnet test -c Release` 全绿 |
| B | 2 | 预演两步检查与原因码、性质守门、合法落子范围、公开视图、AI 最小适配 | 同上 |
| C | 3 | BatchPreview 文案高亮、Presentation、终端、Godot | 同上 + Godot 自检命令退出码 0 |
| D | 4 | 遥测、分析、设计文档、200 局基线、`.trellis/spec/core` 约定、全量回归 | 可归档 |

## Acceptance Criteria

- [ ] `openspec/changes/life-shape/specs/` 下全部 Requirement 与 Scenario 有对应测试（测试类名 = Requirement 名，方法名 = Scenario 名）
- [ ] 每个新守门测试做过变异验证并记录（还原逐字节校验 + 刷新 mtime）
- [ ] 邻接 / 气只有一处实现；活形 / 禁入只有一处实现，预演、契约、公开视图共用同一查询
- [ ] 确定性：不直接遍历 `HashSet` / `Dictionary`；活形状态不入存档
- [ ] 全量测试全绿、零警告；不得改期望凑绿
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写 / 删除逐条、变异逐条、性能数字、待决
- [ ] `openspec validate life-shape --strict` 通过

## Out of Scope

AI 评价 / 停手 / 权重校准、`GroupSafety.EyePoints` 去留（→ `ai-eye`）；调 `EYE_SPACE_MAX` 与分类表；假眼 / 双活；活形缓存（除非裁决）。

## 子 agent 约束

- 不得 `git commit`；不得读取大文件 / 媒体并回传。
- 理解代码优先用 codegraph（`codegraph_explore` / `codegraph_callers` / `codegraph_impact`，`projectPath = E:\wws\geme_demo`），不用 grep/read 遍历大文件；只回传精炼结论。
- Windows 环境：用 `python` 不用 `python3`。
- Godot 在编辑器外运行读 **Debug** 程序集，Godot 相关验证前须 Debug 构建。
