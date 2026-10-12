using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Relics;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.Battlefield;

/// <summary>
/// 段 D-a 在图形版自检里实测到的缺陷（段 F 拼接版改写）：<see cref="MatchFlow.PublishSupplement"/>（表现层每帧重建观察者世界都调用）在账本副本上读结构参数与先手修正，
/// 副本若从开局生成记录重建，拼接后被平移的信物（账本坐标已变）一旦被揭示，副本恢复就抛"揭示记录指向非信物格"。
/// 现在副本经 <see cref="RelicLedger.Copy"/> 逐枚复制活状态（当前坐标、揭示、控制）。
/// </summary>
/// <remarks>变异验证（段 F 实跑）：M-F14 <c>RelicLedger.Copy</c> 改回 <c>Restore(Generation, ExportState())</c> → 「拼接后揭示被平移的信物_公开补充载荷照常」「拼接后揭示被平移的信物_本人部署预演照常」红 2。</remarks>
public class 链接后的公开补充载荷Tests
{
    [Fact]
    public void 拼接后揭示被平移的信物_公开补充载荷照常()
    {
        // 四人四区、脚本固定配对（缺省升序两两配：(0,1) 与 (3,4)）：第 3 大回合结束拼接；在被平移方（1 号棋盘）的信物格上落子并揭示，再发布公开补充载荷。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(3);
        Assert.Equal(2, match.Links.Count);
        Coord moved = match.Map.RelicCells.Keys.Where(match.Map.BirthZones[1].Contains).Order().First();
        Assert.False(match.Links[0].ShiftOf(1)!.Value.IsZero);
        Assert.Contains(moved, match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells);

        match.Place(P0, moved);
        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
        Assert.Contains(match.Relics.PublicStates(), s => s.Coord == moved && s.IsRevealed);

        PublicSupplement supplement = match.PublishSupplement();

        Assert.NotNull(supplement.NextOrderForecast);
        Assert.Equal(4, supplement.NextOrderForecast!.ActiveCount);
        Assert.Contains(supplement.Structures, s => s.Player == P0 && s.Parameters is not null);

        // 副本与正式账本一致：平移后的坐标、揭示与控制都在，正式账本未被改动。
        RelicLedger copy = match.Relics.Copy();
        Assert.Equal(match.Relics.PublicStates().Select(s => s.ToString()), copy.PublicStates().Select(s => s.ToString()));
        Assert.Equal(match.Relics.ExportState().Revealed, copy.ExportState().Revealed);
        Assert.Equal(match.Relics.Coords, copy.Coords);
    }

    [Fact]
    public void 拼接后揭示被平移的信物_本人部署预演照常()
    {
        // 富预演 BatchPreviewBuilder 同样在账本副本上跑"将揭示"。他人战场里被平移的信物被揭示即可触发（P0 自己是否链接无关）。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource()).PassRounds(3);
        Coord moved = match.Map.RelicCells.Keys.Where(match.Map.BirthZones[4].Contains).Order().First();
        Assert.False(match.Links[1].ShiftOf(4)!.Value.IsZero);
        Assert.DoesNotContain(moved, match.Board.Battlefields.ById(match.BattlefieldOf(P0))!.Cells);
        match.Place(P3, moved);
        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();

        while (match.CurrentPlayer != P0)
        {
            match.PassTurn();
        }

        Siege.Core.Batch.StagedBatch batch = match.ToDeploy();
        Coord cell = match.LegalRangeFor(P0).Order().First(c => match.Board[c].Occupant is null);
        Assert.Null(batch.Stage(cell, PieceType.Basic));

        Siege.Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();

        Assert.Equal(P0, preview.Player);
        Assert.True(preview.IsLegal);
        Assert.Equal(1, match.Relics.PublicStates().Count(s => s.IsRevealed && s.Coord == moved));   // 正式账本未被预演改动
    }

    [Fact]
    public void 旧模式副本与按生成记录恢复逐项相同()
    {
        // Legacy 没有链接：Copy 与原先的 Restore(Generation, ExportState()) 逐项相同。
        MatchFlow legacy = MatchFixtures.Started();
        RelicLedger expected = RelicLedger.Restore(legacy.Relics.Generation, legacy.Relics.ExportState());
        RelicLedger copy = legacy.Relics.Copy();
        Assert.Equal(expected.PublicStates().Select(s => (s.Coord, s.IsRevealed, s.Control)), copy.PublicStates().Select(s => (s.Coord, s.IsRevealed, s.Control)));
        Assert.Equal(expected.Coords, copy.Coords);
    }
}
