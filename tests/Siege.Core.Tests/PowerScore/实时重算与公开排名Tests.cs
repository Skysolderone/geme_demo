using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PowerScore;

/// <summary>规格：power-score —— Requirement: 实时重算与公开排名</summary>
public class 实时重算与公开排名Tests
{
    private static readonly IReadOnlyDictionary<PlayerId, PlayerStatus> Roster = ScoringFixtures.Roster(
        (TestMaps.P0, PlayerStatus.Active), (TestMaps.P1, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned));

    [Fact]
    public void 每次结算后更新()
    {
        // P0 确认一个提走 P1 D4 的批次 → 第 5 步回调内全部玩家（含已弃赛 P3）的势力与排名都按提子后的盘面重算。
        // 变异验证：M5（覆盖按棋子计数）、M6（弃赛者被排除出覆盖）、M9（弃赛者参与名次）都让本测试红。
        // 第 5 步回调发生在提子之后由 batch-deployment 的「正式结算顺序」守门。
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("D4", TestMaps.P1)
            .Place("C4", TestMaps.P0).Place("E4", TestMaps.P0).Place("D3", TestMaps.P0)
            .Place("H8", ScoringFixtures.P3);
        var hooks = new ScoreboardHooks(Roster);
        var driver = new SettlementDriver(board, new BoardHistory(), hooks);

        Assert.True(driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), [BatchFixtures.P("D5")]).Confirmed);

        Assert.Equal(["DeductHand", "OnRevealRelics", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.Equal(1, hooks.Scoreboard.Version);
        PowerSnapshot latest = hooks.Scoreboard.Latest!;
        Assert.Equal(3, latest.Players.Length);
        Assert.Empty(latest.Of(TestMaps.P1).Groups);
        Assert.Equal(0, latest.Of(TestMaps.P1).Total);
        // 段 A 重算（总势力 = 领地 + 军势）：P0 原 4 → 13 = 军势 4（四枚互不相连的普通子）+ 领地 9（提子后空出的 D4 + 外圈 B4 / F4 / C3 / E3 / C5 / E5 / D2 / D6）；
        // P3 原 1 → 5 = 军势 1 + H8 四邻 4。与 territory-power 时期同值。
        Assert.Equal(13, latest.Of(TestMaps.P0).Total);
        Assert.Equal(PlayerStatus.Resigned, latest.Of(ScoringFixtures.P3).Status);
        Assert.Equal(5, latest.Of(ScoringFixtures.P3).Total);
        Assert.Equal([1, 2], latest.Ranking.Select(r => r.Rank));
        Assert.Equal(1, latest.RankOf(TestMaps.P0));
        Assert.Equal(2, latest.RankOf(TestMaps.P1));
        Assert.Null(latest.RankOf(ScoringFixtures.P3));
    }

    [Fact]
    public void Pass也触发更新()
    {
        // 玩家确认 0 落子 → 仍执行一次势力重算与排名更新（盘面不变，结果与当前盘面一致）。
        // 变异验证：M9（弃赛者参与名次）、M10（并列被打破）让本测试红；Pass 路径本身到达第 5 步由 batch-deployment 的 PassTests 守门。
        GameBoard board = TestMaps.Blank(size: 9).Place("D4", TestMaps.P0).Place("H8", TestMaps.P1);
        var hooks = new ScoreboardHooks(Roster);
        var driver = new SettlementDriver(board, new BoardHistory(), hooks);
        Assert.Null(hooks.Scoreboard.Latest);

        SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, TestMaps.P0), []);

        Assert.True(outcome.IsPass);
        Assert.Equal(["OnPass", "OnRecalculatePower", "OnCheckEndConditions"], hooks.Steps);
        Assert.Equal(1, hooks.Scoreboard.Version);
        PowerSnapshot latest = hooks.Scoreboard.Latest!;
        Assert.Equal(5, latest.Of(TestMaps.P0).Total);   // 段 A 重算：原 1 → 5 = 军势 1 + 四邻独占 4（D4 与 H8 相距甚远，无争议格）
        Assert.Equal(5, latest.Of(TestMaps.P1).Total);
        RankGroup tied = Assert.Single(latest.Ranking);
        Assert.Equal([TestMaps.P0, TestMaps.P1], tied.Players);
    }

    [Fact]
    public void 弃赛者势力可见但不参与()
    {
        // 设计文档 §10.2 / §12.2：已弃赛 D（P3）势力 45 高于参赛 A（P0）的 30 → D 照常显示并标记，但名次中不出现 D。
        // 本盘恰为规格算例 45 / 30：D 军势 31 = 堡垒子×5 + 倍增子×1（⌊21 × 1.5⌋）+ 领地 14（第 9 行 B–G 六子：上 6 + 下 6 + 两端 A9 / H9）；
        // A 军势 18 = 堡垒子×3 + 普通子 + 协同子（⌊(14 + 协同 4) × 1⌋）+ 领地 12（第 2 行 B–F 五子：上 5 + 下 5 + 两端 A2 / G2）。
        // 段 A 重算：原 31 / 18（scoring-sites：独占空格不计分）→ 45 / 30。
        // 变异验证 M9：IsRanked 改为 Status != Eliminated（弃赛者参与名次）→ 红 4，含本测试；M6（弃赛者棋子被清掉）→ 红 5，含本测试。
        GameBoard board = TestMaps.Blank(size: 11)
            .Place("B2", TestMaps.P0, PieceType.Fortress).Place("C2", TestMaps.P0, PieceType.Fortress).Place("D2", TestMaps.P0, PieceType.Fortress)
            .Place("E2", TestMaps.P0, PieceType.Basic).Place("F2", TestMaps.P0, PieceType.Synergy);
        foreach (char col in "BCDEF")
        {
            board.Place($"{col}9", ScoringFixtures.P3, PieceType.Fortress);
        }

        board.Place("G9", ScoringFixtures.P3, PieceType.Multiplier);

        PowerSnapshot snapshot = PowerCalculator.Compute(
            board, ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active), (ScoringFixtures.P3, PlayerStatus.Resigned)));

        PlayerPower d = snapshot.Of(ScoringFixtures.P3);
        Assert.Equal((14, (BigInteger)45), (d.TerritoryScore, d.Total));
        Assert.Equal(PlayerStatus.Resigned, d.Status);
        Assert.False(d.IsRanked);
        Assert.Equal((12, (BigInteger)30), (snapshot.Of(TestMaps.P0).TerritoryScore, snapshot.Of(TestMaps.P0).Total));
        RankGroup only = Assert.Single(snapshot.Ranking);
        Assert.Equal((1, (BigInteger)30), (only.Rank, only.Power));
        Assert.Equal([TestMaps.P0], only.Players);
        Assert.Null(snapshot.RankOf(ScoringFixtures.P3));
    }

    [Fact]
    public void 遥测记录倍率峰值与出现轮次()
    {
        // 设计文档 §17：记录高倍率棋串的形成轮次与峰值。第 1 轮 n=1，第 2 轮 n=2，第 3 轮仍 n=2，第 4 轮回落 n=1 → 峰值 2、首次出现于第 2 轮。
        // 变异验证 M13：TrackPeak 的 `>` 改为 `>=` → 红 1（本测试，轮次变 3）。
        GameBoard board = TestMaps.Blank(size: 9).Place("B2", TestMaps.P0, PieceType.Multiplier);
        var scoreboard = new PowerScoreboard();
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = ScoringFixtures.Roster((TestMaps.P0, PlayerStatus.Active));

        scoreboard.Recalculate(board, roster, majorRound: 1);
        Assert.Equal((1, 1), (scoreboard.Peak!.MultiplierCount, scoreboard.Peak.MajorRound));

        board.Place("C2", TestMaps.P0, PieceType.Multiplier);
        scoreboard.Recalculate(board, roster, majorRound: 2);
        board.Place("G7", TestMaps.P0, PieceType.Multiplier).Place("G8", TestMaps.P0, PieceType.Multiplier);
        scoreboard.Recalculate(board, roster, majorRound: 3);
        board.RemoveStones([TestMaps.At("C2"), TestMaps.At("G8")]);
        scoreboard.Recalculate(board, roster, majorRound: 4);

        MultiplierPeak peak = scoreboard.Peak!;
        Assert.Equal(2, peak.MultiplierCount);
        Assert.Equal(2, peak.MajorRound);
        Assert.Equal(TestMaps.P0, peak.Player);
        Assert.Equal(["B2", "C2"], peak.Stones.Notations());
        Assert.Equal("2.25", peak.Multiplier.ToString());
        // 遥测不回流到势力：当前快照只反映当前盘面
        Assert.All(scoreboard.Latest!.Of(TestMaps.P0).Groups, g => Assert.Equal(1, g.MultiplierCount));
    }
}
