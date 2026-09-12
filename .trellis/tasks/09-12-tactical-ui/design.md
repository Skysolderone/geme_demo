# 09-12-tactical-ui — 技术设计

## 权威来源

本任务的架构决策、接口契约、风险取舍与已确认裁决，全部以 **`openspec/changes/add-tactical-ui/design.md`** 为准。实现前 MUST 完整阅读该文件，重点是：

- **Decisions** — 每条决策的理由与代价，不要绕过
- **接口契约** — 本任务对上游的输入依赖与对下游的输出承诺
- **Risks / Trade-offs** — 已识别的实现陷阱与对应的强制回归
- **裁决记录（已确认）** — 设计文档未覆盖、已由项目负责人逐条确认的判定

本文件只记录 openspec 设计之外、属于本次实现的技术细节（技术栈选型落地、目录结构、测试组织方式等），随实现推进补充。

## 实现侧补充

**技术栈**：Godot 4 / C#。规则内核为零 Godot 依赖的 net8.0 类库，详见父任务 `.trellis/tasks/09-12-siege-core-prototype/design.md`。

**代码位置**：`src/godot/` —— Godot 4 .NET 项目，单向引用 Siege.Core，仅表现层

**测试位置**：`tests/Siege.Core.Tests/` —— 每条 Requirement 一个测试类，每个 Scenario 一个测试方法，测试名直接用 Scenario 名，便于与 openspec 逐条对账。

**本任务是唯一允许出现 `Godot.*` 的地方。** 引用方向单向：`godot/` → `Siege.Core`。表现层 MUST NOT 包含任何规则计算，只消费内核的预演结果与只读视图。

环境前提：需 **Godot 4.5 .NET 版**编辑器。`~/Desktop/Godot.app` 当前是 4.5 标准版，不含 C# 支持，开工前必须替换。

**必读规范**：`.trellis/spec/core/index.md`，尤其是 [边界与依赖](../../spec/core/boundaries.md)、[确定性](../../spec/core/determinism.md)、[坐标](../../spec/core/coordinates.md)、[测试组织](../../spec/core/testing.md)。

## 兼容性与回滚

- 本任务尚无线上形态，回滚即回退提交。
- 若本任务修改了已被下游任务依赖的契约，MUST 同步更新 `openspec/changes/add-tactical-ui/design.md` 的接口契约表，并在受影响的子任务 `prd.md` 中记录。
