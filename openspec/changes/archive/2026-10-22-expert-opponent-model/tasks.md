## 0. 改前对照（主会话）

- [x] 0.1 改前 HEAD 构建 Release，按 design D5 串行跑 H、E0 各 20 局（`sim-out/expert-opponent-model/{H,E0}`，配置 `configs/`），冒烟核对两臂首部只差座位 1 难度

## 1. 实现（trellis-implement）

- [x] 1.1 先红：新增守门测试——「模拟对手取专家本人的口径」「模拟对手随专家显式配置」（含带宽 > 0 不被拒）「模拟回应等于专家口径的不扰动贪心」「被模拟玩家的难度不影响模拟」，对改前代码确认红
- [x] 1.2 `ExpertLookahead.SimulatedConfig` 改为由专家本人配置派生（design D1），注释同步；核对 `ForSimulation` 的难度标签是否进入确定性文本
- [x] 1.3 `EvaluationBreakdown.Version` 8 → 9；只因版本号的字面量随改（design D3）
- [x] 1.4 全量测试：5 份决策序列基线、链接图 / 严格提高 / 小图黄金哈希、`构筑期专家与高难逐步相同`、`前瞻宽度为1且关闭多样候选时与高难逐步相同` 不变
- [x] 1.5 重钉专家 G1（种子 1–3）与前瞻记录黄金值（种子 6），逐种子写首个分歧到任务目录 `rebaseline.txt`
- [x] 1.6 变异验证：把 1.2 的派生改回标准预设，新守门全红；还原后逐字节校验并刷新 mtime
- [x] 1.7 门控重测：预演次数代理（默认套件）与计时（`SIEGE_PERF=1`），数字记入 `implement.md`

## 2. 质检与验证

- [x] 2.1 主会话前台复核：`dotnet build siege.sln -c Release` 0 警告、全量 `dotnet test` 退出码 0
- [x] 2.2 trellis-check 质检
- [x] 2.3 跑 EB 20 局并汇总三臂（design D5），按预登记准则判定；记录写入任务目录 `verify.md`

## 3. 收尾

- [x] 3.1 设计文档 v1.36 → v1.37（§15.2 第 1 条、公平信息段、变更记录）；`HANDOFF.md` 第 15 项、`openspec/ROADMAP.md` 第 2 项
- [x] 3.2 `openspec archive expert-opponent-model`，`openspec validate --specs --strict`
- [x] 3.3 `task.py archive`，journal 记一节，按路径提交（不加署名）
