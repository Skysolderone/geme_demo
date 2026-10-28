using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;
using Siege.Core.Preview;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>
/// 规格：more-pieces-relics batch-preview —— Requirement: 计分信物与新棋子的预演（D3）。
/// 预演的棋串军势与预计势力含四种新棋子的位置加值。relic-redesign W1：旧计分信物（连营 / 犄角）与"将揭示"随旧信物删除，
/// 改为挂在棋子上的信物——预演副本带信物层，本批落在地图信物上即拾取，预演势力与正式结算逐项相同，并列出"将拾取"。
/// </summary>
public class 计分信物与新棋子的预演Tests
{
    private static readonly PlayerId Me = TestMaps.P0;

    /// <summary>v2、第 5 大回合、P0 先行；P0 已有连珠子 <paramref name="lineCells"/>；<paramref name="mapRelic"/> 非空时该格空格上有一件地图刷出的号令。</summary>
    private static MatchFlow Match(string? mapRelic, params string[] lineCells)
    {
        MatchFlow match = LinkModeFixtures.PlainStarted().AtRound(5, TestMaps.All);
        foreach (string cell in lineCells)
        {
            match.Board.Place(TestMaps.At(cell), Me, PieceType.Line);
        }

        if (mapRelic is not null)
        {
            match.Board.AttachRelic(TestMaps.At(mapRelic), RelicToken.Spawned(0, RelicKind.Command));
        }

        match.Debug.Recalculate();
        match.Debug.SeedHand(Me, (PieceType.Line, 3), (PieceType.Basic, 3), (PieceType.Sentry, 3));
        return match;
    }

    [Fact]
    public void 拾取的信物计入预演()
    {
        // relic-redesign D1 / D3：A 暂放一枚连珠子 F7，把长度 2 的线（D7–E7）延长为 3；F7 空格上有一件地图信物号令 → 落子即拾取。
        // 预演中该棋串含号令的信物位置加值 ⌊3 / 2⌋ = 1（D3，未校准初值），"将拾取"列出 F7；正式结算后势力与预演逐项相同。
        // 变异 MC-P1'（BatchPreviewBuilder 用不带信物层的盘面算预演势力）→ 本测试红。
        MatchFlow match = Match("F7", "D7", "E7");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("F7"), PieceType.Line));

        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();

        GroupOutlook line = preview.OwnGroups.Single(g => g.Stones.Contains(TestMaps.At("D7")));
        Assert.Equal((6, 1), (line.Power!.LineBonus, line.Power.RelicPositionBonus));
        Assert.Equal([TestMaps.At("F7")], preview.WillPickUp.Select(r => r.Cell));
        Assert.Equal(RelicKind.Command, preview.WillPickUp.Single().Token.Kind);
        PowerChange mine = preview.PowerChanges.Single(c => c.Player == Me);
        Assert.Equal(match.Scoreboard.Latest!.Of(Me).Total, mine.Before);

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(mine.After, match.Scoreboard.Latest!.Of(Me).Total);
        Assert.Equal(1, match.Scoreboard.Latest!.GroupContaining(Me, "D7").RelicPositionBonus);
    }

    [Fact]
    public void 不落在信物上时没有将拾取()
    {
        // 反面：同一局面暂放 G8（不在信物格上）→ "将拾取"为空，预演棋串没有信物位置加值。
        MatchFlow match = Match("F7", "D7", "E7");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("G8"), PieceType.Line));

        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();

        Assert.Empty(preview.WillPickUp);
        Assert.All(preview.OwnGroups, g => Assert.Equal(0, g.Power!.RelicPositionBonus));
    }

    [Fact]
    public void 新棋子的位置加值计入预演()
    {
        // Requirement 正文：预演的棋串军势含四种新棋子的位置加值。暂放哨兵子 H7，气边邻接 P1 的 H6、H8 → 预演棋串的哨兵加值 4，与结算后一致。
        MatchFlow match = Match(null).Stones(TestMaps.P1, "H6", "H8");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("H7"), PieceType.Sentry));

        GroupOutlook sentry = match.PreviewCurrentBatch().OwnGroups.Single(g => g.Stones.Contains(TestMaps.At("H7")));
        Assert.Equal(4, sentry.Power!.SentryBonus);

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(4, match.Scoreboard.Latest!.GroupContaining(Me, "H7").SentryBonus);
    }
}
