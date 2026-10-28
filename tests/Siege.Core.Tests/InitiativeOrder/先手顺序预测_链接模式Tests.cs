using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LinkModeFixtures;

namespace Siege.Core.Tests.InitiativeOrder;

/// <summary>
/// 规格：initiative-order「行动顺序跨战场统一」（出局者出序）+ board-link-mode 负责人裁决 S10 / D6（tasks 4.0）：
/// 战场结算拆成「无副作用判定」与「执行」两步，先手顺序预测（<c>MatchFlow.ForecastInitiative</c>，经 <see cref="MatchFlow.PublishSupplement"/> 的 <c>NextOrderForecast</c> 读取）
/// 在链接模式下与大回合结束共用同一份判定，口径「假设此刻结束、未行动者按未 Pass 算」，剔除会在本大回合结束时战场落败的玩家。
/// </summary>
/// <remarks>
/// 变异验证（段 D-a 实跑）：M-D1 预测里不调判定（去掉 <c>Mode == Link</c> 分支）→ 「质检局面_P0P1已Pass_预测为P2P0P3」「预测与大回合结束后的实际顺序一致」「填满判定同样进预测」「此刻结束即终局时没有预测」红 4；
/// M-D2 判定把"未行动者"当作已 Pass（<c>members.All(passed.Contains)</c> 改为 <c>members.Length &gt;= 2</c>）→ 「未行动者按未Pass算」红 1，<c>战场结算Tests</c> 同批红 5（一方落子即不结算、整轮Pass结算、三人战场结算、填满结算、等待者Pass不结算），合计红 6。
/// final-countdown（design D2）：新增「期限大回合的预测剔除将到期落败者」——到期（c）走同一判定，预测自动随之；变异 M-FC1（到期不结算）/ M-FC2（R 错一回合）各使它红（实跑，见 Battlefield.决赛倒计时Tests）。
/// </remarks>
public class 先手顺序预测_链接模式Tests
{
    private static InitiativeReport? Forecast(MatchFlow match) => match.PublishSupplement().NextOrderForecast;

    /// <summary>质检局面用的对局种子：第 1 大回合顺序 P0 > P1 > P3 > P2，种子兜底顺序 P2 &lt; P0 &lt; P3 &lt; P1（P0、P1 Pass 之后 P3、P2 尚未行动）。</summary>
    private static readonly GameSeed InspectionSeed = new(9);

    /// <summary>按行动顺序推进：<paramref name="passers"/> 轮到时 Pass，其余玩家落 1 子；<paramref name="passers"/> 全部 Pass 过一次即停（此时序后的玩家尚未行动）。</summary>
    private static void PassOnly(MatchFlow match, params PlayerId[] passers)
    {
        var done = new HashSet<PlayerId>();
        while (done.Count < passers.Length)
        {
            PlayerId current = match.CurrentPlayer!.Value;
            if (passers.Contains(current))
            {
                match.PassTurn();
                done.Add(current);
            }
            else
            {
                match.PlaceOne();
            }
        }
    }

    /// <summary>把出生棋盘 <paramref name="zone"/> 填满：除 <paramref name="minority"/> 的一格外全部是 <paramref name="majority"/> 的棋子（绕过规则直接摆子）。</summary>
    private static void FillBoard(MatchFlow match, int zone, PlayerId majority, PlayerId minority)
    {
        Coord[] cells = [.. Map.BirthBoard(zone).Cells().Where(Map.IsPlayable).Order()];   // link-map-terrain：整块棋盘的可落子格（出生区已缩成核心区）
        match.Place(minority, cells[0]);
        match.Place(majority, cells[1..]);
        match.Debug.Recalculate();
    }

    [Fact]
    public void 质检局面_P0P1已Pass_预测为P2P0P3()
    {
        // 裁决 S10 的实测局面：开局 [0,0,3,4]（P0、P1 同在棋盘 1），第 1 大回合里 P0、P1 已 Pass、其余按原样 → 此刻结束战场 1 会整轮 Pass 结算，
        // 全零局面按种子兜底顺序 P0 胜、P1 落败出局；预测的下一轮顺序为 P2 > P0 > P3（三人先手值相同，首回合同值链走种子顺序），P1 不在其中。
        // 种子取 InspectionSeed（夹具缺省种子下 P0、P1 排在顺序末尾，Pass 完大回合即结束，取不到预测）。
        // open-map：唯一战场上 P3、P2 尚未行动（按未 Pass 算）→ 此刻结束不结算，预测含四人；P0、P1 全零并列按种子顺序 P0 在前。
        MatchFlow match = Started([0, 0, 3, 4], seed: InspectionSeed);
        Assert.Equal([P0, P1, P3, P2], match.ActionOrder);
        PassOnly(match, P0, P1);
        Assert.Equal(1, match.MajorRound);
        Assert.True(match.Debug.SeedRankOf(P0) < match.Debug.SeedRankOf(P1));
        Assert.Equal([P0, P1], match.PassedThisRound);

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));

        Assert.Equal(4, forecast.ActiveCount);
        Assert.Equal([P0, P1, P2, P3], forecast.Entries.Select(e => e.Player).Order());
        Assert.True(forecast.NextOrder.IndexOf(P0) < forecast.NextOrder.IndexOf(P1));
        Assert.Equal(1, forecast.CompletedMajorRound);

        // 判定无副作用：预测之后对局状态一点没变——没有结算记录、四人都在局、Pass 记录与盘面原样。
        string board = match.Board.Serialize();
        Assert.Empty(match.Settlements);
        Assert.Equal(4, match.ActiveCount);
        Assert.Equal(PlayerStatus.Active, match.StateOf(P1).Status);
        Assert.Equal([P0, P1], match.PassedThisRound);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(board, match.Board.Serialize());
        Assert.Equal(forecast.NextOrder, Forecast(match)!.NextOrder);   // 可重复读取
    }

    [Fact]
    public void 预测与大回合结束后的实际顺序一致()
    {
        // 「假设此刻结束」：等到只剩最后一名玩家未行动时取预测，再让他 Pass 结束大回合——open-map 下唯一战场里已有人落子（P3），不是整轮 Pass，不结算、势力不变，
        // 所以预测的下一轮顺序 MUST 与第 2 大回合的实际行动顺序逐项相同。
        MatchFlow match = Started([0, 0, 3, 4], seed: InspectionSeed);
        PassOnly(match, P0, P1);
        while (match.ActionOrder.IndexOf(match.CurrentPlayer!.Value) < 3)
        {
            Assert.True(match.CurrentPlayer is { } p && p != P0 && p != P1);
            match.PlaceOne();
        }

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));
        Assert.Equal(1, match.MajorRound);

        match.PassTurn();

        Assert.Equal(2, match.MajorRound);
        Assert.Empty(match.Settlements);
        Assert.Equal(4, forecast.ActiveCount);
        Assert.Equal(forecast.NextOrder, match.ActionOrder);
        Assert.Equal(forecast.Entries.Select(e => (e.Player, e.Rank, e.Value)), match.PublishSupplement().CurrentOrderReport!.Entries.Select(e => (e.Player, e.Rank, e.Value)));
    }

    [Fact]
    public void 未行动者按未Pass算()
    {
        // 同区两人里只有先行动的那位 Pass 了、另一位尚未行动 → 此刻结束战场不结算（未行动者不算 Pass），预测仍含四人。
        MatchFlow match = Started([0, 0, 3, 4]);
        PlayerId first = match.ActionOrder.First(p => p == P0 || p == P1);
        PlayerId other = first == P0 ? P1 : P0;
        PassOnly(match, first);
        Assert.Equal([first], match.PassedThisRound);
        Assert.True(match.ActionOrder.IndexOf(other) > match.ActionOrder.IndexOf(first));

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));

        Assert.Equal(4, forecast.ActiveCount);
        Assert.Equal([P0, P1, P2, P3], forecast.Entries.Select(e => e.Player).Order());
        Assert.Contains(other, forecast.NextOrder);
    }

    [Fact]
    public void 此刻结束即终局时没有预测()
    {
        // 四人同区且地图填满（open-map：唯一战场 = 全图，5 块棋盘都填满）：此刻结束 → 唯一战场结算 → 终局（「最后战场结算」），没有下一轮可预测。
        // map-expand-25 段 A2：浮空岛除平台（link:1:4 为 10 块）外还有平台之间的低地，"地图填满"另把低地的可落子格全部摆给 P0。
        MatchFlow match = Started([0, 0, 0, 0]);
        for (int zone = 0; zone < Map.Boards.Length; zone++)
        {
            FillBoard(match, zone, majority: P0, minority: P1);
        }

        match.Place(P0, [.. Map.AllCoords().Where(c => Map.IsPlayable(c) && match.Board[c].Occupant is null).Order()]);
        match.Debug.Recalculate();
        Assert.DoesNotContain(Map.AllCoords(), c => Map.IsPlayable(c) && match.Board[c].Occupant is null);   // 样本口径：全图确实填满

        Assert.Null(Forecast(match));
        Assert.Equal(MatchPhase.InProgress, match.Phase);   // 预测不结束对局

        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
    }

}
