using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Presentation.Diagnostics;
using Siege.Presentation.Show;
using Siege.Presentation.Visibility;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// open-map D7（原 board-link-mode tasks 4.6）：`--auto-demo` 全流程自检的判定（<see cref="LinkDemoAudit"/>）——插旗锁定 → 构筑期 → 全图解禁 → 终局，
/// 每步从公开记录与按座位视图模型里可断言地核对；Godot 侧只喂数据与打印。正例走真对局，反例手工造记录。
/// 配对 / 链接 / 战场结算三步的用例随 open-map 整体 Skip（段 B 删除）。
/// </summary>
/// <remarks>
/// 变异验证（段 D-d 实跑）：M-G5 <c>Completeness</c> 恒通过 → 「完整性缺一步即失败」红 1。
/// open-map 1.6 变异（只跑本类）：M-O6 <c>Unlocked</c> 不比"范围数 &gt; 自己棋盘可落子数" → 「全图解禁」红 1；M-O7 <c>FlagsLocked</c> 仍要求已展开 == 1 → 「真对局走一遍_四步全通过」「插旗锁定与构筑期」红 2。
/// </remarks>
public class 自动演示全流程自检Tests
{
    /// <summary>
    /// 插旗阶段之后应可见的格数，与图形版 <c>MatchSession.MyBattlefieldCells</c> 同口径：map-expand-25 段 B 起为整张图的格数（宽 × 高，含平台之间的低地与画布留白；
    /// 原 G1 为本战场各棋盘外接矩形的格数之和——浮空岛的低地不在任何棋盘里，按棋盘算会让「全图解禁」的全图可落子格漏掉低地）。
    /// </summary>
    private static int BattlefieldCellsOf(MatchFlow match, PlayerId me)
    {
        Assert.Equal(0, match.BattlefieldOf(me));   // 样本口径：唯一战场
        return match.Map.Width * match.Map.Height;
    }

    /// <summary>全图解禁一步的三项输入（与 Godot <c>GameRoot</c> 同口径，都读按座位视图模型）：全图可落子格、本机禁入格、自己出生棋盘的可落子格数。</summary>
    private static (HashSet<Coord> Playable, HashSet<Coord> Forbidden, int OwnPlayable) UnlockInputs(MatchFlow match, PlayerId me, int zone)
    {
        DefaultBoardView board = match.World(me).Board();
        HashSet<Coord> playable = [.. board.Cells.Where(c => c.Terrain == Terrain.Playable).Select(c => c.Coord)];
        HashSet<Coord> forbidden = [.. board.Cells.Where(c => c.LifeForbiddenBy is not null).Select(c => c.Coord)];
        int own = board.Placeholders.Where(p => p.Zone == zone).SelectMany(p => p.Cells()).Count(playable.Contains);
        return (playable, forbidden, own);
    }

    [Fact]
    public void 真对局走一遍_四步全通过()
    {
        // Scenario（simulation-harness「各入口支持生成图」自检四步）：插旗锁定（5 块占位全部展开、可见格 = 全图）→ 构筑期 → 全图解禁 → 终局。
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4]);

        // 插旗锁定：全部占位展开（open-map 裁决 O4；map-expand-25：link:1:4 为 10 块平台），可见格 = 整张图（段 B：含平台之间的低地）。
        DefaultBoardView locked = match.World(P0).Board();
        Assert.Equal(10, locked.Placeholders.Count(p => p.Expanded));
        Assert.True(audit.FlagsLocked(locked, 0, BattlefieldCellsOf(match, P0)).Ok);
        Assert.Equal(match.Map.Width * match.Map.Height, locked.Cells.Length);

        // 构筑期：第 1–3 大回合合法范围非空且 ⊆ 自己的出生棋盘（P0 每个大回合落一子，其余 Pass，不触发整轮 Pass 结算）。
        for (int round = 1; round <= 3; round++)
        {
            Assert.Equal(round, match.MajorRound);
            Assert.True(audit.BuildingRange(match.MajorRound, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok);
            match.PlayRound(P0);
        }

        // 全图解禁：第 4 大回合范围 = 全图可落子格 − 本人禁入格，且大于自己棋盘的可落子格数；没有配对 / 链接事件。
        Assert.Equal(4, match.MajorRound);
        (HashSet<Coord> playable, HashSet<Coord> forbidden, int own) = UnlockInputs(match, P0, 0);
        Assert.True(audit.Unlocked(match.MajorRound, match.LegalRangeFor(P0), playable, forbidden, own).Ok);

        // 终局：全员 Pass → 唯一战场结算即终局（最后战场结算）。
        while (match.Phase == MatchPhase.InProgress)
        {
            match.PassRound();
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
        Assert.True(audit.Ended(match.Result!).Ok);
        Assert.True(audit.Completeness().Ok);
        Assert.True(audit.AllOk);
        Assert.Equal(LinkDemoAudit.FullFlow, audit.Seen);
        Assert.Equal(["插旗锁定", "构筑期", "全图解禁", "终局"], LinkDemoAudit.FullFlow);
    }

    [Fact]
    public void 全图解禁()
    {
        // 反例：第 3 大回合不算解禁；范围漏一格 / 多一格场景格 / 含禁入格 / 不大于自己棋盘可落子数 → 失败。
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4]).PlayRounds(3, P0);
        Assert.Equal(4, match.MajorRound);
        (HashSet<Coord> playable, HashSet<Coord> forbidden, int own) = UnlockInputs(match, P0, 0);
        IReadOnlySet<Coord> range = match.LegalRangeFor(P0);
        Assert.True(range.Count > own);

        Assert.True(audit.Unlocked(4, range, playable, forbidden, own).Ok);
        Assert.False(audit.Unlocked(3, range, playable, forbidden, own).Ok, "第 3 大回合不是解禁");
        Assert.False(audit.Unlocked(4, range.Except([range.Order().First()]).ToHashSet(), playable, forbidden, own).Ok, "范围漏一格");
        Assert.False(audit.Unlocked(4, range.Append(new Coord(0, 0)).ToHashSet(), playable, forbidden, own).Ok, "范围含场景格");
        Assert.False(audit.Unlocked(4, range, playable, [range.Order().First()], own).Ok, "范围含禁入格");
        Assert.False(audit.Unlocked(4, range, playable, forbidden, range.Count).Ok, "范围不大于自己棋盘可落子数");

        // 构筑期的范围当作解禁喂进去：漏掉其他棋盘的格 → 失败。
        MatchFlow building = Started([0, 1, 3, 4]);
        (HashSet<Coord> p2, HashSet<Coord> f2, int own2) = UnlockInputs(building, P0, 0);
        Assert.False(audit.Unlocked(4, building.LegalRangeFor(P0), p2, f2, own2).Ok);
    }

    [Fact]
    public void 插旗锁定与构筑期()
    {
        var audit = new LinkDemoAudit();
        MatchFlow match = Started([0, 1, 3, 4]);
        DefaultBoardView board = match.World(P0).Board();
        int cells = BattlefieldCellsOf(match, P0);
        Assert.True(audit.FlagsLocked(board, 0, cells).Ok);
        Assert.False(audit.FlagsLocked(board, 1, cells).Ok, "本机那块不是标为本机的那块");
        Assert.False(audit.FlagsLocked(board, 0, cells + 1).Ok);
        Assert.False(audit.FlagsLocked(match.World(P0).Board() with { Placeholders = board.Placeholders[1..] }, 0, cells).Ok, "占位少于棋盘清单块数");
        Assert.False(audit.FlagsLocked(board with { Placeholders = [.. board.Placeholders.Select(p => p.Own ? p : p with { Expanded = false })] }, 0, cells).Ok, "锁定后仍有占位未展开（open-map O4）");

        Assert.True(audit.BuildingRange(1, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok);
        Assert.False(audit.BuildingRange(1, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(1).Cells()]).Ok, "范围落在别人的棋盘上");
        Assert.False(audit.BuildingRange(4, match.LegalRangeFor(P0), [.. match.Map.BirthBoard(0).Cells()]).Ok, "第 4 大回合不是构筑期");
        Assert.False(audit.BuildingRange(1, ImmutableHashSet<Coord>.Empty, [.. match.Map.BirthBoard(0).Cells()]).Ok, "范围为空");
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
        Assert.Contains("全图解禁", completeness.Detail, StringComparison.Ordinal);
        Assert.False(audit.AllOk);
        Assert.Equal(["终局"], audit.Seen);
    }
}
