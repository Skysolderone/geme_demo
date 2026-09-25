using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>
/// 规格：more-pieces-relics batch-preview —— Requirement: 计分信物与新棋子的预演（D3）。
/// 预演的棋串军势与预计势力含四种新棋子的位置加值，以及<b>批次开始前已揭示</b>、按预演后盘面由相应玩家控制的连营 / 犄角；
/// 本批将首次揭示的信物不计入任何人的预演势力，预演与正式结算的差异只来自它。
/// 同 Requirement 的「工坊下列出隔一格目标」在 <see cref="改造在预演中的呈现Tests.工坊下列出隔一格目标"/>（段 B 2.6 的验证）。
/// </summary>
public class 计分信物与新棋子的预演Tests
{
    private static readonly PlayerId Me = MatchFixtures.P0;

    /// <summary>v2、第 5 大回合、P0 先行；H5 是连营格；P0 已有连珠子 <paramref name="lineCells"/>。<paramref name="revealed"/> 为真时 P0 已占据 H5 且连营已揭示。</summary>
    private static MatchFlow Match(bool revealed, params string[] lineCells)
    {
        MatchFlow match = MatchFixtures.Started(relics: [("H5", RelicFixtures.Encampment())]).AtRound(5, MatchFixtures.All);
        foreach (string cell in lineCells)
        {
            match.Board.Place(TestMaps.At(cell), Me, PieceType.Line);
        }

        if (revealed)
        {
            match.Board.Place(TestMaps.At("H5"), Me, PieceType.Basic);
            match.Relics.Reveal(match.Board, 5);
        }

        match.Debug.Recalculate();
        match.Debug.SeedHand(Me, (PieceType.Line, 3), (PieceType.Basic, 3), (PieceType.Sentry, 3));
        Assert.Equal(revealed, match.Relics.IsRevealed(TestMaps.At("H5")));
        return match;
    }

    [Fact]
    public void 已揭示连营计入预演()
    {
        // 规格 Scenario：A 已控制一枚已揭示的连营，暂放一枚连珠子把长度 2 的线（C5–D5）延长为 3（E5）→ 预演显示该线提供 6 + 3 = 9 点连珠加值。
        // 预演前后势力都按同一份"已揭示内容"算：势力变化 = 连珠 (2 + 2) → (6 + 3) 的 +5、基础 +1，另加领地变化（与结算后势力独立相减比对）。
        // 变异 MC-P1（预演不传已揭示内容）应红。
        MatchFlow match = Match(revealed: true, "C5", "D5");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E5"), PieceType.Line));

        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();

        GroupOutlook line = preview.OwnGroups.Single(g => g.Stones.Contains(TestMaps.At("C5")));
        Assert.Equal(9, line.Power!.LineBonus);
        Assert.Empty(preview.WillReveal);
        PowerChange mine = preview.PowerChanges.Single(c => c.Player == Me);
        Assert.Equal(match.Scoreboard.Latest!.Of(Me).Total, mine.Before);   // 预演的"之前"与正式势力榜一致（已结算盘面上已揭示 = 真实）

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(mine.After, match.Scoreboard.Latest!.Of(Me).Total);   // 已揭示的计分信物：预演与结算相同
    }

    [Fact]
    public void 将揭示的连营不计入预演()
    {
        // 规格 Scenario：A 的暂放（H4 普通子）将首次覆盖一枚未揭示的连营 H5 并唯一覆盖它 → 预演中 A 的势力不含连营加成（C5–E5 长 3 的线仍是 6），
        // 该信物只显示为"将揭示"；正式结算后 A 的势力含连营加成（6 + 3 = 9），二者之差恰是这 3 点。
        // 变异 MC-P2（预演改传真实内容 TrueContents()）应红。
        MatchFlow match = Match(revealed: false, "C5", "D5", "E5");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("H4"), PieceType.Basic));

        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();

        Assert.Equal([TestMaps.At("H5")], preview.WillReveal);
        Assert.Equal(6, preview.OwnGroups.Single(g => g.Stones.Contains(TestMaps.At("C5"))).Power!.LineBonus);
        PowerChange mine = preview.PowerChanges.Single(c => c.Player == Me);

        Assert.True(match.Confirm().Confirmed);
        Assert.True(match.Relics.IsRevealed(TestMaps.At("H5")));
        PowerSnapshot settled = match.Scoreboard.Latest!;
        Assert.Equal(9, settled.GroupContaining(Me, "C5").LineBonus);
        Assert.Equal(3, settled.Of(Me).Total - mine.After);
    }

    [Fact]
    public void 新棋子的位置加值计入预演()
    {
        // Requirement 正文：预演的棋串军势含四种新棋子的位置加值。暂放哨兵子 G5，气边邻接 P1 的 G4、G6 → 预演棋串的哨兵加值 4，与结算后一致。
        MatchFlow match = Match(revealed: false).Stones(MatchFixtures.P1, "G4", "G6");
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("G5"), PieceType.Sentry));

        GroupOutlook sentry = match.PreviewCurrentBatch().OwnGroups.Single(g => g.Stones.Contains(TestMaps.At("G5")));
        Assert.Equal(4, sentry.Power!.SentryBonus);

        Assert.True(match.Confirm().Confirmed);
        Assert.Equal(4, match.Scoreboard.Latest!.GroupContaining(Me, "G5").SentryBonus);
    }
}
