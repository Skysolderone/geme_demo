# 遥测报告哈希红测：钉值含 Windows 行尾，跨平台归一

## Goal

`MatchTelemetry.平衡分析方向Tests.内容集v1的报告除第12项外钉值` 在 Mac 上一直是红的，要让它在 Windows 和 Mac 上算出同一个哈希。

## 已查明（主会话 2026-11-21）

- 钉值写于 405c16e（map-coastline 段 F，Windows）。
- 在 Mac 上，这次合并 1828f03、之后的 d5b4e8a / a3f356a、以及现在的 main 都是红的，算出的哈希完全相同（Sample `F387CA25…1CF906`，RankedSample `0532B97E…CAA3F9`）。所以不是之后的代码改动造成的漂移，而是平台差异。
- 根因：`src/Siege.Sim/Analysis/ReportWriter.cs` 用 `StringBuilder.AppendLine`，换行符取 `Environment.NewLine`（Windows 上是 `\r\n`）。测试的 `Hash` 按 `'\n'` 切行，只在判空时 `TrimEnd('\r')`，最后用 `'\n'` 拼回去再求 SHA-256，所以 Windows 上的钉值里带着每行末尾的 `\r`。

## Requirements

1. 在测试侧把要哈希的文本归一成 LF：每行去掉末尾的 `\r` 后再拼。**不改 `ReportWriter`**，报告文件的行尾行为保持不变。
2. 按归一后的值重新钉。钉值注释沿用该文件"旧值 … 重钉原因"的写法，写明：本次是行尾归一，走法和报告内容都不变。
3. 排查同类问题：仓库里其他对报告、日志文本求哈希或逐字节比对，且可能带上 `Environment.NewLine` 的测试，逐个确认，同样有问题的一并修，没有就在报告里说明查过哪些。
4. 测试代码和注释遵守 `.trellis/spec/core/testing.md` 里关于行尾的约定；测试文件保持原有行尾。

## Acceptance Criteria

- [ ] 这条用例在 Mac 上通过。
- [ ] 变异验证：把 `\r` 归一临时去掉、并在拼接时人为加上 `\r`（模拟 Windows），确认此时用例会红；还原后变绿。用来证明归一确实生效。
- [ ] 全量 `dotnet test siege.sln` 退出码 0。
- [ ] 遗留风险写进 journal 或 HANDOFF：Windows 机器上还需要复跑确认。
