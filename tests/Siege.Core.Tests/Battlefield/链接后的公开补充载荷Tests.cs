using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 段 D-a 在图形版自检里实测到的缺陷：<see cref="MatchFlow.PublishSupplement"/>（表现层每帧重建观察者世界都调用）在账本副本上读结构参数与先手修正，
/// 副本原先只由开局生成记录 + 揭示记录恢复，链接追加的公共棋盘信物不在其中——链接后一旦有公共棋盘信物被揭示，副本恢复就抛"揭示记录指向非信物格"。
/// 现在副本经 <see cref="RelicLedger.Copy"/>（生成记录 + 链接信物 + 揭示记录，与链接模式存档恢复同一顺序）。Sim 不调公开补充载荷，所以段 C 冒烟没有暴露。
/// </summary>
/// <remarks>变异验证（段 D-a 实跑）：M-D9 <c>RelicLedger.Copy</c> 不追加 <c>LinkedPlacements</c> → 「链接后揭示公共棋盘信物_公开补充载荷照常」红 1。</remarks>
public class 链接后的公开补充载荷Tests
{
    [Fact]
    public void 链接后揭示公共棋盘信物_公开补充载荷照常()
    {
        // 四人四区、脚本固定配对：第 3 大回合结束链接 (1,2) 与 (4,5)；在第一块公共棋盘的信物格上落子并揭示，再发布公开补充载荷。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(3);
        Assert.Equal(2, match.Links.Count);
        Assert.NotEmpty(match.Relics.LinkedPlacements);
        RelicPlacement linked = match.Relics.LinkedPlacements[0];
        Assert.Contains(linked.Coord, match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells);

        match.Place(P0, linked.Coord);
        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
        Assert.Contains(match.Relics.PublicStates(), s => s.Coord == linked.Coord && s.IsRevealed);

        PublicSupplement supplement = match.PublishSupplement();

        Assert.NotNull(supplement.NextOrderForecast);
        Assert.Equal(4, supplement.NextOrderForecast!.ActiveCount);
        Assert.Contains(supplement.Structures, s => s.Player == P0 && s.Parameters is not null);

        // 副本与正式账本一致：链接信物与揭示都在，正式账本未被改动。
        RelicLedger copy = match.Relics.Copy();
        Assert.Equal(match.Relics.LinkedPlacements, copy.LinkedPlacements);
        Assert.Equal(match.Relics.PublicStates().Select(s => (s.Coord, s.IsRevealed)), copy.PublicStates().Select(s => (s.Coord, s.IsRevealed)));
        Assert.Equal(match.Relics.ExportState().Revealed, copy.ExportState().Revealed);
    }

    [Fact]
    public void 旧模式副本与按生成记录恢复逐项相同()
    {
        // Legacy 没有链接信物：Copy 与原先的 Restore(Generation, ExportState()) 逐项相同。
        MatchFlow legacy = MatchFixtures.Started();
        RelicLedger expected = RelicLedger.Restore(legacy.Relics.Generation, legacy.Relics.ExportState());
        RelicLedger copy = legacy.Relics.Copy();
        Assert.Empty(copy.LinkedPlacements);
        Assert.Equal(expected.PublicStates().Select(s => (s.Coord, s.IsRevealed, s.Control)), copy.PublicStates().Select(s => (s.Coord, s.IsRevealed, s.Control)));
    }
}
