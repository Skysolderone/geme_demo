using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Diagnostics;
using Siege.Presentation.Show;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// board-link-mode tasks 4.6：`--auto-demo` 全流程自检的判定（<see cref="LinkDemoAudit"/>）——插旗锁定 → 构筑期 → 首轮配对 → 链接 → 战场结算 → 决赛链接 → 终局，
/// 每步从公开记录与按座位视图模型里可断言地核对；Godot 侧只喂数据与打印。正例走真对局（脚本配对），反例手工造记录。
/// </summary>
/// <remarks>变异验证（段 D-d 实跑）：M-G3 <c>Pairing</c> 不看 <c>Source</c> → 「配对记录来源必须是脚本」红 1；M-G4 <c>Linked</c> 本机参与时不比可见格数 → 「链接后可见格数等于本战场格数」红 1；M-G5 <c>Completeness</c> 恒通过 → 「完整性缺一步即失败」红 1。</remarks>
public class 自动演示全流程自检Tests
{
    /// <summary>本机战场应可见的格数，与图形版 <c>MatchSession.MyBattlefieldCells</c> 同口径：G1（link-map-terrain）起为本战场各棋盘外接矩形的格数之和（含岩石与未架桥深水）。</summary>
    private static int BattlefieldCellsOf(MatchFlow match, PlayerId me) =>
        Battlefields.Of(match.Map).ById(match.BattlefieldOf(me))!.Boards.Sum(b => match.Map.Boards[b].Width * match.Map.Boards[b].Height);

    [Fact]
    public void 真对局走一遍_七步全通过()
    {
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource());

        // 插旗锁定：本机 P0 的占位展开、其余 4 块占位。
        DefaultBoardView locked = match.World(P0).Board();
        Assert.True(audit.FlagsLocked(locked, 0, BattlefieldCellsOf(match, P0)).Ok);

        // 构筑期：第 1 大回合合法范围 ⊆ 自己的出生棋盘。
        Assert.True(audit.BuildingRange(match.MajorRound, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok);

        // 第 3 大回合结束：首轮配对 (1,2)(4,5)，两次链接；P0 参与链接 #1。
        match.PassRounds(3);
        Assert.Equal(2, match.Links.Count);
        PairingRecord first = Assert.Single(match.Pairings);
        Assert.True(audit.Pairing(first).Ok);
        DefaultBoardView after = match.World(P0).Board();
        // board-link-mode F.4：拼接核对"可见格 = 本战场 = 拼接前可见格 + 拼入的对方出生棋盘格数"，需给出拼接前已展开的出生棋盘。
        int[] expandedBefore = [.. locked.Placeholders.Where(p => p.Expanded).Select(p => p.Zone)];
        Assert.True(audit.Linked(match.Links[0], mine: true, locked.Cells.Length, after, BattlefieldCellsOf(match, P0), expandedBefore).Ok);
        Assert.True(audit.Linked(match.Links[1], mine: false, after.Cells.Length, after, BattlefieldCellsOf(match, P0), expandedBefore).Ok);
        Assert.True(after.Cells.Length > locked.Cells.Length);

        // 第 4 大回合全员 Pass：两个合并战场都结算；P0 在战场 1 的名单里，P2 的战场 4 对 P0 是他场。
        ImmutableArray<BattlefieldSettlementRecord> before = [.. match.Settlements];
        match.PassRound();
        Assert.Equal(2, match.Settlements.Count);
        (ImmutableArray<SettlementBeat> settled, ImmutableArray<string> notices) = LinkShowBeats.Settlements(before, match.Settlements, P0, matchEnded: match.Phase == MatchPhase.Ended);
        ImmutableArray<string> banners = [.. settled.OfType<BannerBeat>().SelectMany(b => b.Banners)];
        foreach (BattlefieldSettlementRecord record in match.Settlements)
        {
            Assert.True(audit.Settled(record, P0, match.Phase == MatchPhase.Ended, banners, notices).Ok);
        }

        // 决赛链接：两个胜者所在的战场链接为一个；随后全员 Pass 到终局（最后一个战场已结算）。
        Assert.Equal(2, match.Pairings.Count);
        Assert.Equal(PairingKind.Final, match.Pairings[1].Kind);
        Assert.True(audit.Pairing(match.Pairings[1]).Ok);
        while (match.Phase == MatchPhase.InProgress)
        {
            match.PassRound();
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.True(audit.Ended(match.Result!).Ok);
        Assert.True(audit.Completeness().Ok);
        Assert.True(audit.AllOk);
        Assert.Equal(LinkDemoAudit.FullFlow, audit.Seen);
    }

    [Fact]
    public void 配对记录来源必须是脚本()
    {
        var audit = new LinkDemoAudit();
        var scripted = new PairingRecord(1, PairingKind.FirstRound, 4, [0, 1, 3, 4], [(0, 1), (3, 4)], null, ScriptedPairingSource.ScriptSource);
        Assert.True(audit.Pairing(scripted).Ok);

        Assert.False(audit.Pairing(scripted with { Source = PairingSourceNames.Random }).Ok);
        Assert.False(audit.Pairing(scripted with { Pairs = [(0, 2)] }).Ok, "对里出现不在候选里的战场");
        Assert.False(audit.Pairing(scripted with { Candidates = [0, 1, 3, 4, 2], Waiter = null }).Ok, "候选奇数却没有等待者");
        Assert.True(audit.Pairing(scripted with { Candidates = [0, 1, 2, 3, 4], Waiter = 2 }).Ok);
        Assert.False(audit.Pairing(scripted with { Candidates = [0, 1, 2, 3, 4], Waiter = 1 }).Ok, "等待者出现在对里");
    }

    [Fact]
    public void 决赛链接须在首轮之后_且把全部候选链成一个()
    {
        var audit = new LinkDemoAudit();
        var final = new PairingRecord(2, PairingKind.Final, 10, [0, 3], [(0, 3)], null, ScriptedPairingSource.ScriptSource);
        Assert.False(audit.Pairing(final).Ok, "还没见过首轮配对");

        Assert.True(audit.Pairing(new PairingRecord(1, PairingKind.FirstRound, 4, [0, 1, 3, 4], [(0, 1), (3, 4)], null, ScriptedPairingSource.ScriptSource)).Ok);
        Assert.True(audit.Pairing(final).Ok);
        Assert.False(audit.Pairing(final with { Candidates = [0, 2, 3], Pairs = [(0, 3)] }).Ok, "决赛漏了一个候选");
        Assert.False(audit.Pairing(final with { Waiter = 3 }).Ok, "决赛不该有等待者");
    }

    [Fact]
    public void 链接后可见格数等于本战场格数()
    {
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource());
        DefaultBoardView before = match.World(P0).Board();
        match.PassRounds(3);
        DefaultBoardView after = match.World(P0).Board();
        int cells = BattlefieldCellsOf(match, P0);
        Assert.Equal(cells, after.Cells.Length);   // 按座位过滤的格 = 战场导出的格（独立口径一致）

        // board-link-mode F.4：拼接口径——可见格 = 本战场 = 拼接前可见格 + 拼入的对方出生棋盘格数（两战场格数之和守恒）。
        int[] expandedBefore = [.. before.Placeholders.Where(p => p.Expanded).Select(p => p.Zone)];
        Assert.Equal(before.Cells.Length + (Map.BirthBoard(1).Width * Map.BirthBoard(1).Height), cells);
        Assert.True(audit.Linked(match.Links[0], mine: true, before.Cells.Length, after, cells, expandedBefore).Ok);
        Assert.False(audit.Linked(match.Links[0], mine: true, before.Cells.Length, after, cells + 1, expandedBefore).Ok, "可见格数与本战场格数不等");
        Assert.False(audit.Linked(match.Links[0], mine: true, before.Cells.Length, before, before.Cells.Length, expandedBefore).Ok, "参与了链接可见格却没增加");
        Assert.False(audit.Linked(match.Links[0], mine: true, before.Cells.Length - 1, after, cells, expandedBefore).Ok, "拼接前后格数不守恒");
        Assert.False(audit.Linked(match.Links[1], mine: false, before.Cells.Length, after, cells, expandedBefore).Ok, "他人链接本机可见格却变了");
    }

    [Fact]
    public void 插旗锁定与构筑期()
    {
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4]);
        DefaultBoardView board = match.World(P0).Board();
        int cells = BattlefieldCellsOf(match, P0);
        Assert.True(audit.FlagsLocked(board, 0, cells).Ok);
        Assert.False(audit.FlagsLocked(board, 1, cells).Ok, "本机那块不是已展开的那块");
        Assert.False(audit.FlagsLocked(board, 0, cells + 1).Ok);
        Assert.False(audit.FlagsLocked(match.World(P0).Board() with { Placeholders = board.Placeholders[1..] }, 0, cells).Ok, "占位不足 5 块");

        Assert.True(audit.BuildingRange(1, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok);
        Assert.False(audit.BuildingRange(1, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(1).Cells()]).Ok, "范围落在别人的棋盘上");
        Assert.False(audit.BuildingRange(4, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok, "第 4 大回合不是构筑期");
        Assert.False(audit.BuildingRange(1, ImmutableHashSet<Coord>.Empty, [.. match.Map.BirthBoard(0).Cells()]).Ok, "范围为空");
    }

    [Fact]
    public void 战场结算三种口径()
    {
        var audit = new LinkDemoAudit();
        var record = new BattlefieldSettlementRecord(1, 5, 0, SettlementReason.AllPassed, P0,
            [new SettlementEntry(P0, 1, 100, 0, 0, 10), new SettlementEntry(P1, 2, 50, 0, 0, 8)], 1);

        Assert.True(audit.Settled(record, P0, matchEnded: false, [LinkShowBeats.BattlefieldWonText], []).Ok);
        Assert.True(audit.Settled(record, P1, matchEnded: false, ["红方出局", LinkShowBeats.BattlefieldLostText(2)], []).Ok);
        Assert.False(audit.Settled(record, P1, matchEnded: false, ["红方出局"], []).Ok, "本机落败却没有战场落败横幅");
        Assert.True(audit.Settled(record, P2, matchEnded: false, [], [LinkShowBeats.SettledElsewhereText(0, P0)]).Ok);
        Assert.False(audit.Settled(record, P2, matchEnded: false, [], []).Ok, "他场结算却没有提示条");
        Assert.True(audit.Settled(record, P0, matchEnded: true, ["对局结束"], []).Ok);
        Assert.False(audit.Settled(record, P0, matchEnded: true, [LinkShowBeats.BattlefieldWonText], []).Ok, "终局重复了战场横幅");
    }

    [Fact]
    public void 完整性缺一步即失败()
    {
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 0, 0, 0]).PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.True(audit.Ended(match.Result!).Ok);

        AuditLine completeness = audit.Completeness();
        Assert.False(completeness.Ok);
        Assert.Contains("首轮配对", completeness.Detail, StringComparison.Ordinal);
        Assert.False(audit.AllOk);
        Assert.Equal(["终局"], audit.Seen);
    }
}
