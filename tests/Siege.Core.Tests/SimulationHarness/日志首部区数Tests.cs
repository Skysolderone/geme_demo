using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Sim.Analysis;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// frontier-map tasks 3.5（段 A 遗留）：对局日志首部记录地图出生区数 <see cref="LogHeader.ZoneCount"/>，各区胜率报告据此固定行数并单列"被选次数"。
/// 规格：simulation-harness「边疆图批量跑局」——批次报告给出各平台被选次数与胜率。
/// </summary>
public class 日志首部区数Tests
{
    [Fact]
    public void 真实跑局把区数写进首部并经文本往返()
    {
        // 走真实写入路径（合成日志是手填值，漏写照样绿）：链接图 5 块出生棋盘、4 人，首部 ZoneCount = 5 ≠ 参赛人数 4，
        // 确定性文本与完整文本两条往返都保留；共用样本同为 5。
        // retire-legacy-maps 1.8：原用边疆图（6 区 4 人）与标准图样本（4 区）；旧图删除，改用 link:1（5 区 4 人），"区数 ≠ 人数"这一非恒等口径不变。
        // map-expand-25 段 A2：link:7:4 与 link:1:4 都换成 10 块平台的浮空岛，区数期望由 5 改为 10（仍 ≠ 人数 4）。
        // seat-gap-25：出生区 = 出生座位 = 人数，区数期望由 10 改为 4——"区数 ≠ 人数"的非恒等口径随裁决 S2 失效：链接图的座位数恒等于人数（校验器守），
        // 且 Sim 各入口只接受 link: 标识，造不出"区数 ≠ 人数"的真实跑局，"首部写成人数"的变异本测试不再能区分（二者在现行地图上同值，不再是错误）。
        // 段 B 换成现行的非恒等口径：区数（出生座位 4）≠ 棋盘清单块数（座位 + 中立平台，样本口径断言 > 4）——守"首部把中立平台也算成出生区"这一现实的回归。
        // 变异 M-B5：MatchSession.BuildHeader 不写 ZoneCount → 本测试红。
        // 变异 MB2-3（段 B 实跑，只跑本类）：BuildHeader 的 ZoneCount 改为 Match.Map.Boards.Length（把中立平台算进去）→ 红 2（本测试与「回放缺区数字段的旧日志只在首部分歧对局逐步相同」）。
        MatchLog frontier = MatchSession.Create(SimFixtures.Config(turnLimit: 4) with { MapId = "link:7:4" }, seed: 7).Run();
        MapData map = MapCatalog.Resolve("link:7:4");
        Assert.True(map.Boards.Length > 4, $"样本口径：link:7:4 的棋盘清单（{map.Boards.Length} 块）须多于出生座位数 4，否则把中立平台算进区数测不出来");

        Assert.False(frontier.IsFailed, frontier.Failure?.Message);
        Assert.Equal("link:7:4", frontier.Header.MapId);
        Assert.Equal(4, frontier.Header.ZoneCount);
        Assert.Equal(4, frontier.Header.Zones.Count);
        Assert.Equal(4, MatchLog.Parse(frontier.DeterministicText()).Header.ZoneCount);
        Assert.Equal(4, MatchLog.Parse(frontier.FullText()).Header.ZoneCount);
        Assert.Contains("\"ZoneCount\":4", frontier.DeterministicText().Split('\n')[0], StringComparison.Ordinal);

        Assert.All(SimFixtures.Sample.Value, l => Assert.Equal(4, l.Header.ZoneCount));
    }

    [Fact]
    public void 旧日志缺区数字段按被选到的最大区号回填()
    {
        // 旧日志（frontier-map 之前）首部没有 ZoneCount：读入为 null，不抛；分析端回填为"被选到过的最大区号 + 1"——
        // 旧日志全部来自区数 = 人数的标准档图，回填值即真值，各区胜率段与加字段之前逐项相同。
        // retire-legacy-maps 1.8：原取共用样本（v5，区数 4）；样本改到 link:1 后区数为 5 而 4 人至多占 4 区，"回填值即真值"的前提只在旧图上成立，
        // 这里改用区数 = 人数、四区各被选一次的合成日志（经同一写出 / 解析路径），所守的分析端回填规则不变。
        MatchLog current = MatchLog.Parse(SimFixtures.Synthetic(
            11, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [2]), zones: [3, 1, 0, 2], zoneCount: 4).FullText());
        string text = current.FullText();
        Assert.Contains("\"ZoneCount\":4,", text, StringComparison.Ordinal);
        MatchLog old = MatchLog.Parse(text.Replace("\"ZoneCount\":4,", string.Empty, StringComparison.Ordinal));

        Assert.Null(old.Header.ZoneCount);
        Assert.Equal(current.Header.Zones, old.Header.Zones);

        BirthZoneSection fromOld = BalanceAnalyzer.Analyze([old]).BirthZones;
        BirthZoneSection fromNew = BalanceAnalyzer.Analyze([current]).BirthZones;
        Assert.Equal(4, fromOld.ZoneCount);
        Assert.Equal(fromNew.All.Select(z => (z.Zone, z.Picks, z.WinRate.Successes)), fromOld.All.Select(z => (z.Zone, z.Picks, z.WinRate.Successes)));
    }

    [Fact]
    public void 回放缺区数字段的旧日志只在首部分歧对局逐步相同()
    {
        // ZoneCount 进了确定性文本（它不是耗时字段）：回放 frontier-map 之前的旧日志时，重建的首部多出这一项，
        // 逐行比对在第 1 行（首部）报分歧——与历次加首部字段的效果相同，不静默放过、也不伪造一致；而对局本身逐步相同。
        // retire-legacy-maps 1.8：样本由 v5（区数 4）改到 link:1（区数 5），期望值随之由 4 改为 5，断言不变。
        MatchLog current = BatchRunner.Execute(SimFixtures.Config(turnLimit: 4), parallelism: 1)[0];   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
        string text = current.DeterministicText();
        Assert.Contains("\"ZoneCount\":4,", text, StringComparison.Ordinal);   // map-expand-25 段 A2：link:1:4 浮空岛区数 10；seat-gap-25：区数 = 座位数 4
        MatchLog old = MatchLog.Parse(text.Replace("\"ZoneCount\":4,", string.Empty, StringComparison.Ordinal));
        Assert.Null(old.Header.ZoneCount);

        ReplayResult replay = Replayer.Replay(old);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve

        Assert.False(replay.Identical);
        Assert.Equal(1, replay.FirstDivergentLine);
        Assert.Contains("\"ZoneCount\":4", replay.Actual, StringComparison.Ordinal);
        Assert.Equal(4, replay.Replayed.Header.ZoneCount);
        Assert.Equal(SimFixtures.TurnTexts(old.Turns), SimFixtures.TurnTexts(replay.Replayed.Turns));
        Assert.Equal(old.DeterministicText().Split('\n')[1..], replay.Replayed.DeterministicText().Split('\n')[1..]);

        // 对照：带该字段的新日志回放逐行一致。
        Assert.True(Replayer.Replay(MatchLog.Parse(text)).Identical);   // retire-legacy-maps：去掉 Legacy 地图解析（resolveMap），一律经 MapCatalog.Resolve
    }

    [Fact]
    public void 整批没人选的平台仍占一行且被选零次()
    {
        // 段 A 遗留：行数原先取"被选到过的最大区号 + 1"，6 号台整批没人选时只有 5 行。现在取首部的区数。
        // 合成 20 局 4 人日志，只在 1–5 号台里轮转（第 i 局占 i、i+1、i+2、i+3 模 5），6 号台从未被选；胜者恒为 0 号玩家。
        // 每个被选到的台：被选 16 次、胜 4 次。变异 M-B6：分析端忽略首部 ZoneCount（改回只看 Zones.Max()+1）→ 只剩 5 行，本测试红。
        MatchLog[] logs = [.. Enumerable.Range(0, 20).Select(i => SimFixtures.Synthetic(
            (ulong)i + 1,
            [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])],
            [],
            SimFixtures.ResultOf(6, [0]),
            zones: [i % 5, (i + 1) % 5, (i + 2) % 5, (i + 3) % 5],
            zoneCount: 6))];

        BirthZoneSection section = BalanceAnalyzer.Analyze(logs).BirthZones;

        Assert.Equal(6, section.ZoneCount);
        Assert.Equal([0, 1, 2, 3, 4, 5], section.All.Select(z => z.Zone));
        Assert.Equal([16, 16, 16, 16, 16, 0], section.All.Select(z => z.Picks));
        Assert.Equal([4, 4, 4, 4, 4, 0], section.All.Select(z => z.WinRate.Successes));
        Assert.False(section.All[5].Significant);
        Assert.Equal(0.25, section.Baseline);

        string[] lines = [.. ReportWriter.Render(BalanceAnalyzer.Analyze(logs)).Split('\n').Select(l => l.TrimEnd('\r'))];
        Assert.Contains(lines, l => l.StartsWith("- 区数 6", StringComparison.Ordinal));
        string[] zoneLines = [.. lines.Where(l => l.StartsWith("  - 出生区 ", StringComparison.Ordinal))];
        Assert.StartsWith("  - 出生区 1：胜率 25.0% (4/16", zoneLines[0], StringComparison.Ordinal);
        Assert.EndsWith("；被选 16 次", zoneLines[0], StringComparison.Ordinal);
        Assert.Equal("  - 出生区 6：胜率 无样本；被选 0 次", zoneLines[5]);
        Assert.Equal(6, zoneLines.Length);   // relic-redesign W1：原"收敛局 / 未收敛局"两段随旧信物生成删除，只剩「全部局」一段
    }

    [Fact]
    public void 区数取首部与被选区号的较大者不丢样本()
    {
        // 防御：首部区数比实际被选到的区号还小（手改日志 / 混了不同地图的批次）时，不得把越界区的样本静默丢掉。
        MatchLog log = SimFixtures.Synthetic(
            1, [SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"])], [], SimFixtures.ResultOf(6, [0]), zones: [0, 1, 2, 5], zoneCount: 4);

        BirthZoneSection section = BalanceAnalyzer.Analyze([log]).BirthZones;

        Assert.Equal(6, section.ZoneCount);
        Assert.Equal(4, section.All.Sum(z => z.Picks));
    }

    [Fact]
    public void 报告给出AI单步耗时的均值与最大值()
    {
        // 规格 Scenario「边疆图批量跑局」：批次报告给出 AI 单步决策耗时的均值与最大值。单步 = 一个小回合，取快照的墙钟耗时；
        // 没有耗时的快照（从确定性文本读回）不计入样本。100 / 200 / 900 → 均值 400、最大 900；测试内独立算式。
        // 变异 M-B7：最大值写成均值 → 本测试红。
        MatchLog log = SimFixtures.Synthetic(
            1,
            [
                SimFixtures.Turn(1, 1, 0, [1, 1, 1, 1], ["A1:Basic"]) with { ElapsedMs = 100 },
                SimFixtures.Turn(2, 1, 1, [1, 1, 1, 1], ["A2:Basic"]) with { ElapsedMs = 200 },
                SimFixtures.Turn(3, 1, 2, [1, 1, 1, 1], ["A3:Basic"]) with { ElapsedMs = 900 },
                SimFixtures.Turn(4, 1, 3, [1, 1, 1, 1], ["A4:Basic"]),
            ],
            [],
            SimFixtures.ResultOf(6, [0]));

        AiStepTiming step = BalanceAnalyzer.Analyze([log]).Targets.AiStep;

        Assert.Equal(3, step.Samples);
        Assert.Equal((100d + 200 + 900) / 3, step.MeanMs, 10);
        Assert.Equal(900, step.MaxMs);
        Assert.Contains("AI 单步决策耗时", ReportWriter.Render(BalanceAnalyzer.Analyze([log])), StringComparison.Ordinal);
        Assert.Contains("均值 400 ms，最大 900 ms（样本 3 个小回合", ReportWriter.Render(BalanceAnalyzer.Analyze([log])), StringComparison.Ordinal);

        // 真实跑局写入了耗时，但确定性文本不带：耗时不进可复现部分。
        MatchLog real = SimFixtures.Sample.Value[0];
        Assert.All(real.Turns, t => Assert.NotNull(t.ElapsedMs));
        Assert.DoesNotContain("ElapsedMs", real.DeterministicText(), StringComparison.Ordinal);
        Assert.Equal(0, BalanceAnalyzer.Analyze([MatchLog.Parse(real.DeterministicText())]).Targets.AiStep.Samples);
    }
}
