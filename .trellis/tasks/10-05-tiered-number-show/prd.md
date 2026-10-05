# 10-05-tiered-number-show

> 规格权威：`openspec/changes/tiered-number-show/`（proposal / design / specs / tasks）。

数值呈现五档递增（负责人 2026-10-05）：按数值大小分五档（阈值 4 / 8 / 16 / 32）；新增军势揭示节拍（逐串依次、逐步揭示算式）；五档字形、四五档冲击环、五档镜头轻震；势力栏到账与棋串常驻标注按档；音效按档升调。只改 `Siege.Presentation` 与 `src/godot`，`Siege.Core` / `Siege.Sim` 与规则数值不动。

验收：`openspec/changes/tiered-number-show/tasks.md` 各条的验证项。

约束：同一时间只跑一个 dotnet / Godot；不跑批量对局；纯逻辑放 `Siege.Presentation`（整数运算、零 Godot 依赖）并配规格场景测试与变异验证；Godot 层不算军势、不取档；无人值守（零时长）帧数与截图基线不变；本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。

分段：A = tasks.md 第 1 组；B = 第 2 组；C = 第 3 组（主会话）。

## 实现记录

### 段 A（2026-10-05，呈现层）

- 新增 `Style/NumberTiers.cs`（`NumberTier.Of` 唯一取档入口、`NumberTierStyle` 五档样式表）；`SettlementBeats`（`PowerRevealBeat` / `RevealEntry` / `RevealStep`，落子飘字只剩类型名，`PowerChange` 带三个档位）；`ShowTimeline`（遮罩新增 `Reveals` / `Rings` / `ShakePermille` / `RevealStepTiers`）；`SoundCues`（`SoundCueKind` + 带档位的 `SoundCue`，新增揭示）；`GroupPowerLabels`（标注带档位，落点公开为 `AnchorOf`，揭示共用）。
- Godot 仅为继续编译改了 `ShowSounds`：参数表里暂时没有揭示音，`Play` 点名跳过 `Reveal`，段 B 补上后删掉该分支。
- 测试：2134 条 = 2114 通过 / 11 失败 / 9 跳过（改动前 2049 / 11 / 9）；失败集合与本机基线逐条相同。新增 `数值档位Tests`、`军势揭示节拍Tests`、`高档冲击环与镜头轻震Tests`，`UI风格约束Tests` 加两条。
- 变异：实现方 46 条 + 检查方 10 条 + 主会话 1 条（五档阈值 32 → 33，红 3），全部变红，还原后逐字节一致。各条记录在对应测试类的备注里。
- 检查补上的三处缺口：军势原样转录不重算；压缩后遮罩与音效按压缩后的时刻；`ShowSounds` 其他种类缺表仍然抛异常。
- 偏离与自定：音效提示改成"种类 + 档位"的结构体；遮罩多一个只供音效用的 `RevealStepTiers`（条目停留期满会从列表消失，无法靠它数出本帧新开始的步）；压缩只缩各步的开始时刻，亮环 / 轻震 / 停留仍按原时长；颜色初值 白 (244,240,230) / 淡金 (244,226,164) / 金 (240,196,72) / 橙 (244,146,48) / 红 (236,72,52)。
- 留给段 B：`src/godot/scripts` 下不得出现英文 particle（`UI风格约束Tests` 的源码扫描，含注释）；引擎层不得调用 `NumberTier.Of`（`数值档位Tests.引擎层不取档`）。
