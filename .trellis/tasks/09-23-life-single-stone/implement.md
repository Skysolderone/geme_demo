# implement 记录：life-single-stone（1.1–2.4）

> 规格：`openspec/changes/life-single-stone/`。未提交、未 archive。

## 改了什么

- `src/Siege.Core/Board/LifeShape.cs`：只改三态判定一处。`StateOf(int eyeValueSum)` → `StateOf(int eyeValueSum, int stoneCount)`，第 ⑤ 步传 `groups[g].Size`；
  分支 `>= 2 when stoneCount > 1 => Alive; >= 1 => Undetermined; _ => Dead`。眼空间、眼值、`IsClosed`、禁入 / 保护查询都没动（D1 / D2 / D3）。
  同步了 `LifeState.Alive` / `Undetermined` 的 doc 注释与类 remarks 的规格引用。预演、合法落子范围、公开视图、表现层、AI 都没改。
- 新增测试（1.1，5 条）：
  - `LifeShape/活形三态Tests`：`单子有两个单格眼为未定`、`两子棋串有两个单格眼为活`、`单子与活形棋串共享眼空间仍禁入`、`切出单子即不再是活形_所有者自切`。
  - `CaptureResolution/以整批最终状态判定合法性Tests.立栅切出单子被拒`（非所有者的一半）。
  - 共用夹具 `LifeShapeFixtures.SingleStoneCut()`：两子串 B1–C1 分别贴竖直直四 A1–A4 和水平直四 D1–G1，C1–C2 之间原本就有栅栏；切开后两枚单子的眼值之和仍各为 2，所以没有单子上限时测试会红（不是假红）。
- 先红：实现前跑这两个类，33 条里红 4 条，都红在预期断言上（3 条 `Expected Undetermined / Actual Alive`，他切那条 `IsLegal` 为 True）；`两子棋串有两个单格眼为活` 本来就是绿的，只作 M2 的靶子。

## 1.3 夹具逐条改动（只改盘面 / 样本，不改期望）

| 测试 | 原因 | 改动 |
|---|---|---|
| `LifeShape/活形查询Tests.查询确定` | A 的 A1、B2、C1 和 C 的 A9、B8 都是单子活形，`forbid P1 B1,A2,C2,E5,G5,A8,B9` / `forbid P0 A8,B9` 靠它们成立 | 盘面补 A 子 B3（第 3 行 `0........` → `00.......`，A3–B3–B2 连成三子串，眼 A2、B1、C2）和 C 子 C8（第 8 行 `.2...1.1.` → `.22..1.1.`，B8–C8–C9–C7 连成四子串，眼 A8、B9）。两条禁入期望原样保留；注释的手算说明同步更新 |
| `MatchTelemetry/地形改造日志与分析Tests.地形可离线重建` | 样本下界：AI 走法变了，种子 53 一次改造都没有（53–55：改造 0/4/5，致提子 0/0/1） | 种子 53–55 → 1–3。临时探针用同一份写死权重和阈值 20，重扫种子 1–200；探针先在去掉单子上限的二进制上复现了旧的 53–55（改造 1/5/3，致提子 1/1/1）。新规则下有 58 局出现致提子，取最小的一组连续三局且每局都有改造：1–3，改造 3/3/6，致提子 1/0/0。断言与期望不变 |

`活形保护性质Tests` 不用改。`SIEGE_SLOW=1 dotnet test tests/Siege.Core.Tests -c Release --filter "Category=Slow"`：3 条全过。
其中性质测试跑种子 1–200，无反例：批次 8000，确认 4796，受保护棋串核对 8702，拒绝 Suicide=620 / LifeForbidden=2427 / BreaksLife=157，**耗时 15.0 s**。
同一次运行里另外两条慢测试：`缓存开关不改变决策序列_标准图种子1至20` 38 s，`_边疆图种子1至20` 21 min 47 s。整次运行 22.4 min。

## 1.2 变异（`mutate.py`：二进制读写，按 LF 锚点，断言命中 1 次；带时间戳备份，finally 还原，还原后与原字节逐字节比对，再 `os.utime`）

| 编号 | 实现改动 | 结果 |
|---|---|---|
| M1 | 去掉单子上限：`>= 2 when stoneCount > 1` → `>= 2` | **红 7**：新增的 4 条（单子两眼、共享眼空间、所有者自切、他人立栅切出单子）+ `候选格上限` 黄金哈希 + `阈值为0时零变化` + `地形可离线重建`（种子 1–3 在旧规则下没有致提子） |
| M2 | 上限误用于 2 子：`stoneCount > 1` → `stoneCount > 2` | **红 7**：`两子棋串有两个单格眼为活`、所有者自切、他人立栅（这两条的前提断言"两子串为活"红）、`活形记录与统计Tests.真实跑局的活形字段自洽` + 两份黄金哈希 + `地形可离线重建` |

两条变异还原后 `restored_bytes_equal True`。确认跑：全绿 1310 / 跳过 4，失败数是 0，不是 7，说明还原后确实用的是新二进制。

## 2.1 黄金哈希重建与探针

- 探针（当时尚未改黄金常量和夹具）：工作树只去掉 `when stoneCount > 1`，其余改动全部保留，跑全量测试。**只有新增的 4 条红**；`候选格上限`、`阈值为0时零变化`、`查询确定`、`地形可离线重建` 全部回到绿，即旧哈希 D1481DD5… / 8EEC49A7… 被逐字节复现。结论：分叉只来自单子上限。
- 逐条比对种子 31、24 个小回合（两份二进制各导出快照，去掉耗时）：
  - 第 1 个小回合（P3）落点 B10 / B11 / C12 不变。快照只差活形记录：单子 C12 与 B10-B11 共享平台角 8 格眼空间（眼值 2），它的"确立"事件没有了。
  - 第 2 个小回合（P0）起走法分叉。旧：C2 连珠 + D3 匠人（立 D2–E2），两枚单子共享同一块 11 格眼空间、各自"确立"活形。新：C2 连珠 + B3 匠人（立 A2–B2）。
  - 旧日志每个小回合快照里的单子活形条数由 1 增到 26，新日志一直是 0。
- 新值：`V4GoldenTurnHash` D1481DD5…772968BD → **35E25329…8B88D3D7**；`StrictImprovementTurnHash` 8EEC49A7…04679FC8 → **DCB7CC7C…CD39033E**。归因写在常量注释里。之后连续两次全量运行都一致。

## 2.2 v5 50 局复核（`sim-out/life-single-stone/`）

命令：`run --seed 1 --count 50 --map siege-4p-base-v5 --players 4 --difficulty Standard --out sim-out/life-single-stone`，之后 `analyze --dir sim-out/life-single-stone`。
`config.json` 与 `sim-out/r8-explore/K1/config.json` 逐字节相同。`report.txt` 与 K1 只差 3 行耗时。50 份对局日志去掉耗时字段后逐局逐行相同，只有末行 `MajorRoundMs` 数组不同。结论：正式实现与实验开关语义一致。

| 项 | 实验 K1 | 正式实现 |
|---|---|---|
| 整局无提子 | 22/50 | 22/50 |
| 结束大回合 平均 / 最长 | 8.02 / 13 | 8.02 / 13 |
| 终局禁入格占可落子格 | 25.1% | 25.1% |
| 第 3 大回合领先者胜率（口径 B） | 76.0%（38/50） | 76.0%（38/50） |
| 单子活形棋串（终局 / 确立事件） | 0/557 / 0/542 | 0/557 / 0/542 |
| 他人致失活 | 0 | 0 |
| 截断 | 0 | 0 |
| 终局每人已确定活形棋串 | 2.79 | 2.79 |
| 已确认批次 / 提子 | 1215 / 71 | 1215 / 71 |

## 2.3 设计文档

`2026-09-10-siege-core-gameplay-design-v1.md`：§6.4 三态段落之后加了一段"单子不成活"；文末变更记录加了 2026-09-23 `life-single-stone` 一行，写明依据和 50 局复核数字。

## 2.4 回归

- `dotnet build siege.sln`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：通过 1310，跳过 4（慢测试 / 计时测试），失败 0。基线是 1305，新增 5 条。
- `openspec validate life-single-stone --strict`：valid。
- 临时探针文件 `tests/Siege.Core.Tests/ZzProbeLifeSingleStone.cs` 已删除。

## 待决项

1. **设计文档版本号**：以往改规则的 change 都会升版本（life-shape 是 v1.5 → v1.6）。本次按指示只加一句和一行，没有改文首的"版本：v1.6"，也没有在变更记录里写 v1.6 → v1.7。是否升版本，请主会话裁决。
2. **第 3 大回合领先者胜率 76%（n=50）**：按 design 的 Risks，只报告不拦截，留待 `ai-eye` 段 D 在 200 局上观察。
3. **贴地形小空区眼仍占 80%**：属于 Non-goal，本 change 不处理。
4. `terrain-surfaces` 分支也改 `LifeShape.cs`。本 change 的改动集中在 `StateOf` 及其调用处（第 ⑤ 步），合并时由主会话解决冲突并复跑活形测试。
