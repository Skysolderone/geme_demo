## 1. 留痕与统计

- [x] 1.1 `D:stall` 留痕（design D1 / S1）：停滞成立且最终 Pass 时决策条目记 `D:stall`，决胜一手接管照常记落点；核实决策条目是否随对局日志写出，否则按 S1 加最小字段。算例：`ai-decision`「循环提子停手」Scenario「停滞停手留痕」——K = 3、停滞成立且最终 Pass → `D:stall`；同局面 K = 0 → `D:pass`，此后逐步相同。验收：单元测试（含决胜一手接管时不记 `D:stall`）；变异验证；AI 评价版本不变
- [x] 1.2 统计（design D2 / S2）：`analyze --review-cycles` 每局与汇总停滞停手次数，旧日志计 0，缺省报告逐字节不变。算例：`match-telemetry`「停滞停手次数可查」。验收：单元测试；对 `sim-out/cycle-stop/easy-k6/` 或新跑 ≤ 8 局简单局实测一次，报告停手次数 > 0

## 2. 基线首部

- [x] 2.1 基线首部（design D3）：基线生成器首部删配对 / 链接旧字样，重建 5 份基线；脚本核对与 HEAD 相比只有首部两行不同（若第 12 大回合内出现 `D:stall`，单列记录）。验收：核对输出存任务目录；全量 0 失败

## 3. 文档与归档

- [x] 3.1 ROADMAP 按现状重写；HANDOFF 第 8 项、1b、测试数；设计文档 v1.31（§15.2、§17、§19、变更记录）。验收：人工检查
- [x] 3.2 `openspec validate stagnation-trace --strict` 通过后归档。验收：命令退出 0
