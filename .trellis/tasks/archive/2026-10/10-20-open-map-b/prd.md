# 10-20-open-map-b

> 规格权威：`openspec/changes/open-map/`（proposal 裁决 O1–O8；design D1–D10、A1–A8、**段 B 裁决 B1–B10**；tasks 段 B 2.1–2.5；specs 13 份差分）。段 A 已提交 `a2bb3a4`（任务 `10-19-open-map-a`，其 `notes.md` / `check.md` 记有 Skip 清单、活用例与 R-1–R-4）。本任务只做 **2.1–2.5**；2.6（`openspec validate --strict` + 归档）与 git 提交由负责人线做。

把段 A 留下的死路径删干净，并让规格 / 设计文档 / HANDOFF 与开放地图规则一致：删配对、链接、等待者、决赛倒计时（含 K）、`LinkMapGenerator.Link`、拼接演出与战场横幅、Sim 的配对 / 链接选项与分析段、AI 全知开关；按**用例名单**删 Skip 测试（活用例先搬走）；删 10 份副本基线；`map` 加一行连通分量；`ThemeAnalyzer` 改整局胜率；设计文档 v1.28；HANDOFF 整体改写；`.trellis/spec/core/boundaries.md` 同步。

## Acceptance Criteria
- [ ] tasks 2.1–2.5 逐条勾选，每条带验证证据（测试名 / 命令 / Grep 输出），记入本目录 `notes.md`
- [ ] Grep `Pairing|PairingKind|LinkRecord|LinkStage|FinalCountdown|Countdown|Waiter|WaitingRounds|ApplySplice|SeatVision|pairing-seed|--links` 在 `src/`、`tests/`、`src/godot/scripts/` 为零（只许历史注释里出现"已删除"字样）；`Battlefield` / `LastBattlefieldSettled` / 按座位投影保留（B1）
- [ ] 含配对 / 链接 / K 字段的旧存档、旧日志在恢复 / 回放 / 离线解析三处都明确拒绝并提示"拼接规则已移除"（B2），各有测试
- [ ] 段 A 的 140 条 `open-map 段 B 删除` Skip 全部消失：按名单删用例；`阶段推进Tests`、`链接模式端到端Tests`、`链接模式存档Tests`、`战场落败复盘与等待者评估Tests`、`可复现回放_链接模式Tests` 里的 26 条活用例先搬到新文件再删旧文件（B3）；删后通过数 = 段 A 通过数 2032 − 被删活用例 0 + 新增，逐名对上（清单存本目录）
- [ ] `*-omniscient.txt` / `*-k0.txt` 10 份与比对用例删除，5 份缺省基线逐字节不变（B4）
- [ ] `map` 输出含"连通分量 1"一行并有测试（B5）；`ThemeAnalyzer` 整局胜率、`主题表现可查` 恢复（B6）
- [ ] 设计文档 v1.28：§3.3 / §4.2 / 删 §4.3 / §12.3–12.4 / §13 / §15.2 / §17 / §19（登记 R-3、对局长度中位 24、3+2 布局公平三项观察）/ §21.3 / 文首版本与变更记录一行；其余章节最小同步，不整体重写（B7–B9）
- [ ] HANDOFF 整体改写：一句话、怎么玩、流程（插旗 → 构筑 → 交战 → 终局）、自检四步、基线（只剩 5 份）、观察项 / 后续 change 列表；`.trellis/spec/core/boundaries.md` 删链接 / 配对 / K 条目或改为"已删除"
- [ ] `dotnet build siege.sln -c Release` 与 `dotnet build src/godot/Siege.Godot.csproj` 0 警告；`dotnet test` 全量 0 失败、Skip 只剩 6 条既有 Perf-Slow（全知口径 PerfTheory 随 B1 / B4 删除）；Godot headless 四条自检退出 0

约束：子 agent 不得 git commit / archive；不跑批量、不跑超过 10 局；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改规则行为（5 份缺省基线逐字节不变是硬指标）、不改 AI 权重与阈值；不改 `openspec/` 规格正文（规格有问题写进回传）；不动飘字动效逻辑（`ShowTimeline.cs` / `VisualBaseline.cs` / SettlementShow 飘字测试）；设计文档只改相关章节，不改历史变更记录的旧行。
