## 1. 实现（trellis-implement）

- [x] 1.1 先红：「整批候选」各 Scenario、「停手阈值」改动的 Scenario、「高部署上限下的候选剪枝」候选数上限 + 2 的测试，对改前代码确认红
- [x] 1.2 Core：`AiSearchConfig.WholeBatch`（0 / 1、0 不写出、`Validated` 拒绝其他值）；`ChooseByCandidates` 追加剪枝候选与配对候选（design D1 / D2）；简单不生成；`ExpertLookahead.SimulatedConfig` 清零开关
- [x] 1.3 Sim：`RunConfig.WholeBatch`、`--whole-batch`（缺省 1）、`MatchSession` 预设派生、首部读写、缺字段按 0 回放；`PrintUsage` 与 HANDOFF 命令行一节对齐
- [x] 1.4 `EvaluationBreakdown.Version` 9 → 10；只因版本号而改的字面量
- [x] 1.5 守门：开关 0 时 5 份基线与各黄金值逐步相同；简单基线开关 1 时也不变
- [x] 1.6 重钉受影响的基线与黄金值，`rebaseline.txt` 逐份写首个分歧（design D5 判据）
- [x] 1.7 变异验证（去掉剪枝 / 去掉配对 / 阈值口径改成 ≥），还原后逐字节校验并刷新 mtime
- [x] 1.8 门控：专家前瞻代理与计时（`SIEGE_PERF=1`）≤ 4；数字记入 `implement.md`

## 2. 质检与验证

- [x] 2.1 主会话前台复核：`dotnet build siege.sln -c Release` 0 警告、全量 `dotnet test` 退出码 0、Godot 工程构建 0 警告
- [x] 2.2 trellis-check 质检
- [x] 2.3 跑 S0 / S1 / H0 / H1 各 20 局并汇总，按预登记准则判定，记录写 `verify.md`

## 3. 收尾

- [x] 3.1 设计文档 v1.37 → v1.38（§15.2 停手阈值与候选生成、变更记录）；`HANDOFF.md` 第 6 项与命令行一节、`openspec/ROADMAP.md` 第 4 项
- [x] 3.2 `openspec archive ai-whole-batch`，`openspec validate --specs --strict`
- [x] 3.3 `task.py archive`，journal 记一节，按路径提交（不加署名）
