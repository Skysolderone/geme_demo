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
        // 直接摆子不经结算：被占据的信物格要先揭示，否则公开补充载荷会因"控制而未揭示"响亮失败（那是守门，不是本测试要验的）。
        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
    }

    [Fact]
    public void 质检局面_P0P1已Pass_预测为P2P0P3()
    {
        // 裁决 S10 的实测局面：开局 [0,0,3,4]（P0、P1 同在棋盘 1），第 1 大回合里 P0、P1 已 Pass、其余按原样 → 此刻结束战场 1 会整轮 Pass 结算，
        // 全零局面按种子兜底顺序 P0 胜、P1 落败出局；预测的下一轮顺序为 P2 > P0 > P3（三人先手值相同，首回合同值链走种子顺序），P1 不在其中。
        // 种子取 InspectionSeed（夹具缺省种子下 P0、P1 排在顺序末尾，Pass 完大回合即结束，取不到预测）。
        MatchFlow match = Started([0, 0, 3, 4], seed: InspectionSeed);
        Assert.Equal([P0, P1, P3, P2], match.ActionOrder);
        PassOnly(match, P0, P1);
        Assert.Equal(1, match.MajorRound);
        Assert.True(match.Debug.SeedRankOf(P0) < match.Debug.SeedRankOf(P1));
        Assert.Equal([P0, P1], match.PassedThisRound);

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));

        Assert.Equal([P2, P0, P3], forecast.NextOrder);
        Assert.Equal(3, forecast.ActiveCount);
        Assert.DoesNotContain(P1, forecast.Entries.Select(e => e.Player));
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
        // 「假设此刻结束」：等到只剩最后一名（单人战场的）玩家未行动时取预测，再让他 Pass 结束大回合——单人战场不因 Pass 结算、势力不变，
        // 所以预测的下一轮顺序 MUST 与第 2 大回合的实际行动顺序逐项相同，落败者已出局。
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
        BattlefieldSettlementRecord settled = Assert.Single(match.Settlements);
        Assert.Equal((0, P0), (settled.Battlefield, settled.Winner));
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
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
    public void 填满判定同样进预测()
    {
        // 判定的（b）条：战场无可落子空格也会在大回合结束时结算。把棋盘 1 填满（P0 占绝大多数、P1 一格），谁都没 Pass → 预测已剔除落败的 P1。
        MatchFlow match = Started([0, 0, 3, 4]);
        FillBoard(match, zone: 0, majority: P0, minority: P1);
        Assert.Empty(match.PassedThisRound);

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));

        Assert.Equal(3, forecast.ActiveCount);
        Assert.DoesNotContain(P1, forecast.NextOrder);
        Assert.Contains(P0, forecast.NextOrder);
        Assert.Empty(match.Settlements);   // 仍只是预测

        // 真跑到大回合结束：满盘上两人只能 Pass，执行时的原因记为整轮 Pass（判定里（a）先于（b）），但落败者与预测一致。
        match.PassRound();
        Assert.Equal((0, P0), (Assert.Single(match.Settlements).Battlefield, Assert.Single(match.Settlements).Winner));
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P1).Status);
        Assert.Equal(forecast.NextOrder.Order(), match.ActionOrder.Order());
    }

    [Fact]
    public void 此刻结束即终局时没有预测()
    {
        // 四人同区且棋盘填满：此刻结束 → 唯一战场结算 → 终局（「最后战场结算」），没有下一轮可预测。
        MatchFlow match = Started([0, 0, 0, 0]);
        FillBoard(match, zone: 0, majority: P0, minority: P1);

        Assert.Null(Forecast(match));
        Assert.Equal(MatchPhase.InProgress, match.Phase);   // 预测不结束对局

        match.PassRound();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.LastBattlefieldSettled, match.Result!.Reason);
    }

    [Fact]
    public void 期限大回合的预测剔除将到期落败者()
    {
        // final-countdown（design D2）：到期是 JudgeAtRoundEnd 的第三个触发条件，先手预测复用同一判定。四人四区 [0,1,3,4]、K = 2、脚本配对：
        // 战场 3 第 6 大回合整轮 Pass 结算 → 战场 0 期限第 8 大回合。第 8 大回合开始、尚无人行动时"假设此刻结束"：没有人 Pass（a 不成立）、战场未满（b 不成立），
        // 只因到期（c）结算 → 预测已剔除战场 0 的落败者；第 7 大回合开始时同一问法不剔除任何人。随后真把第 8 大回合打完（全员 Pass，势力不变），出局者与预测一致。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(5, All);
        match.PlayRound(P0, P1);
        Assert.Equal(8, match.DeadlineOf(0));
        PlayerId waiter = Assert.Single(match.Settlements).Winner;

        Assert.Equal(3, Forecast(match)!.ActiveCount);   // 第 7 大回合：未到期限
        match.PlayRound(P0, P1);
        Assert.Equal(8, match.MajorRound);
        Assert.Empty(match.PassedThisRound);

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));

        Assert.Equal(2, forecast.ActiveCount);
        Assert.Contains(waiter, forecast.NextOrder);
        PlayerId[] fighters = [.. new[] { P0, P1 }.Where(forecast.NextOrder.Contains)];
        PlayerId survivor = Assert.Single(fighters);
        Assert.Single(match.Settlements);   // 仍只是预测

        match.PassRound();
        BattlefieldSettlementRecord settled = match.Settlements[1];
        // 口径：预测时（无人行动）只有（c）成立；实际全员 Pass 后（a）先成立、原因记整轮 Pass——比较链相同、势力不变，落败者与顺序都与预测一致。
        // 实际以"到期"结算的对照见「期限大回合最后一人Pass前的预测与实际顺序一致」。
        Assert.Equal((8, 0, SettlementReason.AllPassed), (settled.MajorRound, settled.Battlefield, settled.Reason));
        Assert.Equal(survivor, settled.Winner);
        Assert.Equal(forecast.NextOrder, match.ActionOrder);
    }

    [Fact]
    public void 期限大回合最后一人Pass前的预测与实际顺序一致()
    {
        // final-countdown check 补测（design D2）：第 8 大回合（期限）里战场 0 的两人在前面各落 1 子、等待者 Pass，只剩顺序末位一人未行动时取预测；
        // 末位 Pass 不改势力、战场 0 本大回合并非全员 Pass → 只因到期（c）结算。预测的下一轮顺序 MUST 与实际逐项相同（不只是集合），落败者已剔除。
        MatchFlow match = Started([0, 1, 3, 4], new ScriptedPairingSource(), options: CountdownOptions(2));
        match.PlayRounds(5, All);
        match.PlayRound(P0, P1);
        match.PlayRound(P0, P1);
        Assert.Equal((8, 8), (match.MajorRound, match.DeadlineOf(0)));
        PlayerId waiter = Assert.Single(match.Settlements).Winner;
        Assert.Equal(3, match.ActionOrder.Length);

        while (match.ActionOrder.IndexOf(match.CurrentPlayer!.Value) < match.ActionOrder.Length - 1)
        {
            if (match.CurrentPlayer == waiter)
            {
                match.PassTurn();
            }
            else
            {
                match.PlaceOne();
            }
        }

        InitiativeReport forecast = Assert.IsType<InitiativeReport>(Forecast(match));
        Assert.Equal(8, match.MajorRound);
        Assert.Equal(2, forecast.ActiveCount);

        match.PassTurn();

        BattlefieldSettlementRecord settled = match.Settlements[^1];
        Assert.Equal((8, 0, SettlementReason.Expired), (settled.MajorRound, settled.Battlefield, settled.Reason));
        Assert.Contains(settled.Winner, forecast.NextOrder);
        Assert.DoesNotContain(settled.Entries[1].Player, forecast.NextOrder);
        Assert.Equal(forecast.NextOrder, match.ActionOrder);
    }
}
