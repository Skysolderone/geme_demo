using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.EliminationEndgame;

/// <summary>规格：elimination-endgame —— Requirement: 势力碾压（dominance-victory ADDED，候选制，裁决 7 / 8）</summary>
/// <remarks>
/// 规格里的精确势力值（210 / 80 / 70 / 55 等）在 9×9 合成图上无法用真实棋串逐位摆出，因此每个 Scenario 分两条腿：
/// ① 规格算例原样喂给碾压式的唯一实现 <see cref="DominanceCheck.Satisfying"/>；
/// ② 用真实摆盘走 <see cref="MatchFlow"/> 的结算 / Pass 检查点，先用断言钉住实际势力值满足同一不等式关系，再断言候选状态机的行为。
/// </remarks>
public class 势力碾压Tests
{
    private static readonly PlayerId P0 = MatchFixtures.P0;
    private static readonly PlayerId P1 = MatchFixtures.P1;
    private static readonly PlayerId P2 = MatchFixtures.P2;
    private static readonly PlayerId P3 = MatchFixtures.P3;

    private static long Power(MatchFlow match, PlayerId player) => match.Scoreboard.Latest!.Of(player).Total;

    private static string Powers(MatchFlow match) => string.Join(" ", MatchFixtures.All.Select(p => $"{p}={Power(match, p)}"));

    private static DominanceEntry Active(PlayerId player, long power) => new(player, PlayerStatus.Active, power);

    /// <summary>
    /// 第 6 大回合、顺序 P0 &gt; P1 &gt; P2 &gt; P3 的碾压局面：P0 角落堡垒 + 倍增串（势力 31），P1 从外侧三面贴住该串（7），P2、P3 各两子（7、7）。
    /// P0 串 A1-B1-C1-A2-B2 的最后一口气是 B3，P1 在 B3 落子即整串提走。
    /// </summary>
    private static MatchFlow Crushing(MatchOptions options)
    {
        MatchFlow match = MatchFixtures.Started(options: options).AtRound(6, [P0, P1, P2, P3]);
        foreach ((string cell, PieceType type) in new[] { ("A1", PieceType.Fortress), ("B1", PieceType.Fortress), ("A2", PieceType.Fortress), ("B2", PieceType.Multiplier), ("C1", PieceType.Multiplier) })
        {
            match.Board.Place(Coord.Parse(cell), P0, type);
        }

        match.Stones(P1, "D1", "C2", "A3").Stones(P2, "A8", "B8").Stones(P3, "H8", "J8");
        Assert.Equal((31L, 7L, 7L, 7L), (Power(match, P0), Power(match, P1), Power(match, P2), Power(match, P3)));
        return match;
    }

    [Fact]
    public void 开局不触发()
    {
        // 规格算例：起始大回合 4，第 1 大回合首位玩家落子后势力 5、其余 0 → 不产生候选，对局继续。
        // 前提断言：此刻碾压式本身是成立的（5 ≥ 0），不产生候选完全是起始大回合门槛的作用。
        // 变异验证 M-DV3：UpdateDominance 去掉 `MajorRound >= DominanceStartRound` 门槛 → 红 2（本测试、旧存档回填）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(1, [P0, P1, P2, P3]);
        match.PlayTurn("B2");
        Assert.Equal(5, Power(match, P0));
        Assert.All([P1, P2, P3], p => Assert.Equal(0, Power(match, p)));
        Assert.Equal([P0], DominanceCheck.Satisfying(MatchFixtures.All.Select(p => Active(p, Power(match, p)))));

        Assert.Equal(4, match.DominanceStartRound);
        Assert.Null(match.Dominance);
        Assert.Null(match.Publish().Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 全员势力为0不产生候选()
    {
        // 规格算例：起始大回合 4，第 5 大回合全员势力 0 且首位玩家 Pass → 每人都满足 0 ≥ 0，属于多人同时满足，不产生候选。
        // 变异验证 M-DV1：DominanceCheck 的 `>=` 改为 `>` → 红 2（本测试、同时满足不产生候选）。
        // 变异验证 M-DV6：建立条件 `is [PlayerId sole]` 改为 `is [PlayerId sole, ..]`（多人满足也建候选）→ 红 5（本测试、同时满足不产生候选及 3 条既有测试）。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(5, [P0, P1, P2, P3]);
        match.PassTurn();
        Assert.All(MatchFixtures.All, p => Assert.Equal(0, Power(match, p)));
        Assert.Equal(MatchFixtures.All, DominanceCheck.Satisfying(MatchFixtures.All.Select(p => Active(p, 0))));

        Assert.Null(match.Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 同时满足不产生候选()
    {
        // 规格算例：A=10、B=10、C=0、D=0 → A、B 同时满足（10 ≥ 10+0+0），不产生候选。
        // 变异验证 M-DV1（`>=` 改 `>`）、M-DV6（多人满足也建候选）均红，见「全员势力为0不产生候选」。
        Assert.Equal([P0, P1], DominanceCheck.Satisfying([Active(P0, 10), Active(P1, 10), Active(P2, 0), Active(P3, 0)]));

        // 真实摆盘：P0 与 P1 在各自角落摆出镜像棋形，势力相等且 P2、P3 为 0。
        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(5, [P0, P1, P2, P3])
            .Stones(P0, "A1", "B1")
            .Stones(P1, "J1", "H1");
        match.PassTurn();
        Assert.True(Power(match, P0) > 0);
        Assert.Equal(Power(match, P0), Power(match, P1));
        Assert.Equal((0L, 0L), (Power(match, P2), Power(match, P3)));

        Assert.Null(match.Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 成为候选()
    {
        // 规格算例：第 6 大回合 A 结算后 A=210、B=80、C=70、D=55 → 210 ≥ 205，A 成为候选，名单 B、C、D，对局继续。
        Assert.Equal([P0], DominanceCheck.Satisfying([Active(P0, 210), Active(P1, 80), Active(P2, 70), Active(P3, 55)]));

        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        Assert.Null(match.Dominance);   // 摆盘本身不是检查点
        match.PassTurn();               // P0 的 Pass 完成：31 ≥ 7+7+7

        Assert.Equal(P0, match.Dominance!.Candidate);
        Assert.Equal([P1, P2, P3], match.Dominance.Pending);
        Assert.Equal(P0, match.Publish().Dominance!.Candidate);
        Assert.Equal([P1, P2, P3], match.Publish().Dominance!.Pending);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(P1, match.CurrentPlayer);
    }

    [Fact]
    public void 差一点不成为候选()
    {
        // 规格算例：同上局面但 A=204 → 204 < 205，不产生候选。
        Assert.Empty(DominanceCheck.Satisfying([Active(P0, 204), Active(P1, 80), Active(P2, 70), Active(P3, 55)]));

        // 真实摆盘：在碾压局面上给 P3 加三枚堡垒，使其余之和超过 P0。
        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        foreach (string cell in new[] { "G8", "G9", "H9" })
        {
            match.Board.Place(Coord.Parse(cell), P3, PieceType.Fortress);
        }

        match.Debug.Recalculate();
        match.PassTurn();
        Assert.True(Power(match, P0) < Power(match, P1) + Power(match, P2) + Power(match, P3), Powers(match));

        Assert.Null(match.Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 其余玩家各行动一次后仍满足则获胜()
    {
        // 规格：A 成为候选后 B、C、D 依次完成小回合，每次结算后 A 仍满足 → D 的小回合结算后名单为空，A 获胜，原因为势力碾压。
        // P1、P2 落子而非 Pass，避免同一时刻也凑成整轮 Pass 而混淆终局原因。
        // 变异验证 M-DV5：成立条件去掉 `_dominancePending.Count == 0`（名单不清空就判胜）→ 红 23（含本测试与 6 条碾压新测试）。
        // 变异验证 M-DV13：名单不移除刚完成小回合的玩家 → 红 7（含本测试）。
        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        match.PassTurn();
        Assert.Equal([P1, P2, P3], match.Dominance!.Pending);

        match.PlayTurn("E1");
        Assert.Equal([P2, P3], match.Dominance!.Pending);
        match.PlayTurn("C8");
        Assert.Equal([P3], match.Dominance!.Pending);
        Assert.True(Power(match, P0) >= Power(match, P1) + Power(match, P2) + Power(match, P3), Powers(match));
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.PassTurn();
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.PowerDominance, match.Result!.Reason);
        Assert.Equal([P0], match.Result.Winners);
        Assert.Equal(6, match.Result.MajorRound);
        Assert.Equal(6, match.MajorRound);   // 结果对象与活对象两处都钉住：成立在 D 的结算瞬间，不推进大回合
        Assert.Equal(1, match.PassStreak);
    }

    [Fact]
    public void 被拉下来则取消候选()
    {
        // 规格算例：B 提走 A 的一条棋串后 A=150、B=95、C=70、D=55 → 150 < 220，候选立即取消，名单清空，对局继续。
        // 变异验证 M-DV4：复查去掉 `!DominanceSatisfying().Contains(candidate)`（跌破不取消）→ 红 13（本测试、取消后可再次成为候选及 11 条默认开启碾压的既有测试）。
        Assert.Empty(DominanceCheck.Satisfying([Active(P0, 150), Active(P1, 95), Active(P2, 70), Active(P3, 55)]));

        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        match.PassTurn();
        Assert.Equal(P0, match.Dominance!.Candidate);

        match.PlayTurn("B3");   // P1 提走 P0 的整串
        Assert.Equal(0, match.StoneCount(P0));
        Assert.Equal((0L, 12L, 7L, 7L), (Power(match, P0), Power(match, P1), Power(match, P2), Power(match, P3)));
        Assert.Empty(DominanceCheck.Satisfying(MatchFixtures.All.Select(p => Active(p, Power(match, p)))));   // 也没有人顶上来成为新候选

        Assert.Null(match.Dominance);
        Assert.Null(match.Publish().Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 取消后可再次成为候选()
    {
        // 规格：A 的候选取消后再次满足 → 重新成为候选，名单为此刻其余全部参赛玩家，此前的回应进度不保留。
        // 用测试接缝在两个小回合之间改盘面制造"跌破 / 回升"，检查点本身仍是真实的 Pass 结算。
        // 变异验证 M-DV4（跌破不取消）→ 红，见「被拉下来则取消候选」。
        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        match.PassTurn();                   // P0：候选，名单 P1 P2 P3
        match.PassTurn();                   // P1 回应
        Assert.Equal([P2, P3], match.Dominance!.Pending);

        Coord[] reinforcement = [Coord.Parse("C9"), Coord.Parse("D9"), Coord.Parse("E9")];
        foreach (Coord c in reinforcement)
        {
            match.Board.Place(c, P2, PieceType.Fortress);
        }

        match.Debug.Recalculate();
        match.PassTurn();                   // P2 的 Pass 检查点：P0 跌破 → 取消
        Assert.True(Power(match, P0) < Power(match, P1) + Power(match, P2) + Power(match, P3), Powers(match));
        Assert.Null(match.Dominance);

        match.Board.RemoveStones(reinforcement);
        match.Debug.Recalculate();
        match.PlayTurn("G8");               // P3 的结算检查点：P0 再次满足 → 重新成为候选
        Assert.True(Power(match, P0) >= Power(match, P1) + Power(match, P2) + Power(match, P3), Powers(match));
        Assert.Equal(P0, match.Dominance!.Candidate);
        Assert.Equal([P1, P2, P3], match.Dominance.Pending);   // P1 之前的回应不保留；刚行动的 P3 也在名单里
        Assert.Equal(MatchPhase.InProgress, match.Phase);
    }

    [Fact]
    public void 碾压只统计参赛玩家()
    {
        // 规格算例：A=120、B=60、C 已弃赛 90、D 已出局 0 → 只比较 A 与 B：120 ≥ 60，A 成为候选，名单只有 B。
        // 变异验证 M-DV2：DominanceCheck 的其余之和改为对全部玩家（含弃赛 / 出局）求和 → 红 1（本测试）。
        // 变异验证 M-DV14：DominanceSatisfying 的参赛状态改读快照 `power.Of(p).Status` → 红 0。等价变异：快照过期只发生在同一检查点刚出局的玩家身上，而出局者盘面为空、势力必为 0，不改变任何满足者集合；弃赛路径先重算快照。不补测试，实现仍读权威名册。
        Assert.Equal([P0], DominanceCheck.Satisfying([Active(P0, 120), Active(P1, 60), new(P2, PlayerStatus.Resigned, 90), new(P3, PlayerStatus.Eliminated, 0)]));
        Assert.Empty(DominanceCheck.Satisfying([Active(P0, 120), Active(P1, 60), Active(P2, 90), new(P3, PlayerStatus.Eliminated, 0)]));   // 若把 C 计入：120 < 150

        MatchFlow match = MatchFixtures.Started(options: MatchFixtures.DominanceOn).AtRound(6, [P0, P1, P2, P3]);
        foreach ((string cell, PieceType type) in new[] { ("A1", PieceType.Fortress), ("B1", PieceType.Fortress), ("A2", PieceType.Fortress), ("B2", PieceType.Multiplier), ("C1", PieceType.Multiplier) })
        {
            match.Board.Place(Coord.Parse(cell), P0, type);
        }

        match.Stones(P1, "H1");
        foreach (string cell in new[] { "A9", "B9", "C9", "D9", "A8", "B8", "C8" })
        {
            match.Board.Place(Coord.Parse(cell), P2, PieceType.Fortress);
        }

        match.Debug.Recalculate();
        match.Debug.SeedHand(P3);           // P3 盘面与手牌皆空，保护已解除 → 在 P0 的检查点出局
        match.Resign(P2);
        Assert.Equal(PlayerStatus.Resigned, match.StateOf(P2).Status);
        Assert.Null(match.Dominance);       // 弃赛不是建立候选的检查点

        match.PassTurn();
        Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P3).Status);
        Assert.True(Power(match, P2) > 0);  // 弃赛者的势力仍在快照里显示
        Assert.True(Power(match, P0) >= Power(match, P1), Powers(match));
        Assert.True(Power(match, P0) < Power(match, P1) + Power(match, P2), Powers(match));   // 若把弃赛者计入则不满足

        Assert.Equal(P0, match.Dominance!.Candidate);
        Assert.Equal([P1], match.Dominance.Pending);
    }

    [Fact]
    public void 待回应者出局或弃赛()
    {
        // 规格：A 为候选、名单 B、C，C 在轮到自己之前弃赛 → C 移出名单且其势力不再计入；B 完成小回合后若 A 仍满足则 A 获胜。
        // 变异验证 M-DV15：Resign 路径去掉 UpdateDominance（弃赛不移出名单）→ 红 2（本测试、只剩一人优先于碾压）。
        MatchFlow match = Crushing(MatchFixtures.DominanceOn);
        match.Resign(P3);                   // 先让 D 离场，名单只剩 B、C 两人
        match.PassTurn();
        Assert.Equal(P0, match.Dominance!.Candidate);
        Assert.Equal([P1, P2], match.Dominance.Pending);

        match.Resign(P2);                   // C 在轮到自己之前弃赛
        Assert.Equal([P1], match.Dominance!.Pending);
        Assert.Equal(MatchPhase.InProgress, match.Phase);

        match.PlayTurn("E1");               // B 落子（不 Pass，避免与整轮 Pass 同时成立）
        Assert.True(Power(match, P0) >= Power(match, P1), Powers(match));
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(EndReason.PowerDominance, match.Result!.Reason);
        Assert.Equal([P0], match.Result.Winners);
    }

    [Fact]
    public void 关闭势力碾压()
    {
        // 规格：碾压起始大回合为 0 → 任何局面下都不产生候选，对局只受其余终局条件约束。
        MatchFlow match = Crushing(MatchFixtures.DominanceOff);
        Assert.Equal(0, match.DominanceStartRound);
        Assert.Equal(0, match.Publish().DominanceStartRound);

        match.PassTurn();
        match.PlayTurn("E1");
        match.PlayTurn("C8");
        match.PassTurn();
        Assert.True(Power(match, P0) >= Power(match, P1) + Power(match, P2) + Power(match, P3), Powers(match));
        Assert.Null(match.Dominance);
        Assert.Equal(MatchPhase.InProgress, match.Phase);
        Assert.Equal(7, match.MajorRound);
    }

    [Fact]
    public void 候选状态公开并随存档往返()
    {
        // 规格：A 为候选、名单 C、D 时存档并恢复 → 恢复后公开视图可读到候选 A 与名单 C、D，D 完成小回合后若 A 仍满足则 A 获胜。
        // 起始大回合用非回填值 6（testing.md「回填字段用非回填值证伪」）；活对象逐字段 + 逐字节两条腿，恢复后续跑同样决策再比。
        // 变异验证 M-DV7：Serialize 不写 DominanceCandidate → 红 1（本测试）。
        MatchFlow match = Crushing(MatchFixtures.DominanceOn with { DominanceStartRound = 6 });
        match.PassTurn();
        match.PlayTurn("E1");
        Assert.Equal([P2, P3], match.Dominance!.Pending);

        string json = match.Serialize();
        MatchFlow restored = MatchFlow.RestoreUnvalidated(match.Map, match.Relics.Generation, json);
        Assert.Equal(6, restored.DominanceStartRound);
        Assert.False(restored.DominanceStartRoundBackfilled);
        Assert.Equal(match.Dominance.Candidate, restored.Dominance!.Candidate);
        Assert.Equal(match.Dominance.Pending, restored.Dominance.Pending);
        Assert.Equal(P0, restored.Publish().Dominance!.Candidate);
        Assert.Equal([P2, P3], restored.Publish().Dominance!.Pending);
        Assert.Equal(6, restored.Publish().DominanceStartRound);
        Assert.Equal(json, restored.Serialize());

        foreach (MatchFlow m in new[] { match, restored })
        {
            m.PlayTurn("C8");
            Assert.Equal([P3], m.Dominance!.Pending);
            m.PassTurn();
            Assert.Equal(MatchPhase.Ended, m.Phase);
            Assert.Equal(EndReason.PowerDominance, m.Result!.Reason);
            Assert.Equal([P0], m.Result.Winners);
        }

        Assert.Equal(match.Serialize(), restored.Serialize());
    }
}
