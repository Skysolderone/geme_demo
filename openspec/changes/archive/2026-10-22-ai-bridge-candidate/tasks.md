## 1. 实现（trellis-implement）

- [x] 1.1 先红：「搭桥候选」各 Scenario 与两处 MODIFIED 的测试，对改前代码确认红
- [x] 1.2 Core：`AiSearchConfig.EditSeed`；`ChooseByCandidates` 追加搭桥候选（design D1）；简单与模拟对手不生成
- [x] 1.3 Sim：`RunConfig.EditSeed`、`--edit-seed`、派生、首部读写、缺字段按 0 回放、PrintUsage
- [x] 1.4 `EvaluationBreakdown.Version` 10 → 11 与只因版本号的字面量
- [x] 1.5 守门：开关 0 全部基线与黄金值不变；简单基线开关 1 不变
- [x] 1.6 重钉，`rebaseline.txt` 逐份写首个分歧（须为搭桥候选被选中）
- [x] 1.7 变异验证（去掉搭桥候选 / 改成经预筛 / 阈值改 ≥）
- [x] 1.8 门控：专家前瞻代理与计时 ≤ 4

## 2. 质检与验证

- [x] 2.1 主会话前台复核：sln Release 0 警告、全量测试退出码 0、Godot 构建 0 警告
- [x] 2.2 trellis-check
- [x] 2.3 跑 E0 / E1 各 20 局并汇总，按预登记准则判定，写 `verify.md`

## 3. 收尾

- [x] 3.1 设计文档 v1.39（§15.2、§19 第 22 条、变更记录）；HANDOFF 第 10e 项与命令行；ROADMAP
- [x] 3.2 `openspec archive ai-bridge-candidate`，`openspec validate --specs --strict`
- [x] 3.3 归档诊断任务与实施任务，journal，按路径提交
