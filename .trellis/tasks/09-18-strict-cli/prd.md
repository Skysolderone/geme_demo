# 09-18-strict-cli

> 规格权威：`openspec/changes/strict-cli/`。本文件只承载目标与验收。

## Goal

`CommandLine` 把任何 `--key value` 收进字典，没人认领的键被静默丢弃：`--matches 200` 曾被完全忽略、跑成 1 局却照常写出 summary，差点被当成 200 局的回归结论。本任务让各子命令在解析完参数后拒绝不认识的选项，非零退出、不执行任何跑局。它是去围棋化第三轮（`artisan-terrain-edit`）的段 O：本轮扫档的口径由它把关，不再靠人工核对 config.json。

## Acceptance Criteria

- [ ] `simulation-harness`「批量跑局」的未知选项 Scenario 有测试钉住
- [ ] tasks 1.1–1.4 的四条守门测试各有对应变异（删结算、建议恒空、已消费也算未知、`Has` 不计消费）
- [ ] `CommandLine` 记录消费、提供结算入口与最相近合法选项建议（编辑距离 ≤ 3，确定性）
- [ ] run / analyze / replay / play / map 等子命令在创建输出目录与执行之前调用结算
- [ ] 实跑 `--matches 200` 非零退出、提示 `--count`、`sim-out/` 无新目录；正常跑局不受影响
- [ ] 全量 `dotnet test -c Release` 绿（当前 808）

## Out of Scope

不引第三方解析库、不改现有选项名、不加别名、不处理位置参数误用。
