using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Sim.Running;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// expert-lookahead tasks 1.3（design D7）：合法落子范围、出局判据、公开部署上限、公开先锋修正四个纯函数从 <c>MatchFlow</c> / <c>RelicLedger</c> 抽出，
/// 原处改为委托、行为零变化。等价测试把<b>改动前的实现</b>原样抄进测试（"旧路径"，下方 Old* 方法），与委托后的对局路径、读公开快照的纯函数路径逐项比对。
/// </summary>
public class 公开规则纯函数Tests
{
    private static RelicCellSpec Spec => new(RelicZone.Contested, BudgetTier.Standard);

    private static RelicPublicState Revealed(string cell, RelicContent content, RelicControl control) =>
        new(TestMaps.At(cell), Spec, IsRevealed: true, content, RevealedInMajorRound: 1, control);

    [Fact]
    public void 公开部署上限算例()
    {
        // 设计文档 §5.4 / growth-pass-1：第 5 大回合（分阶段基础 4），控制 1 枚已揭示的 +2 军令、另 1 枚普通军令处于争议 → 4 + 2 = 6；
        // 第 2 大回合无军令 → 3。争议、封锁、他人控制与未揭示（内容为空）一律不给效果。
        // 变异 M-P1（部署上限计入争议军令）→ 见段 A 实施记录。
        var mine = new RelicControl(RelicControlKind.Controlled, P1);
        ImmutableArray<RelicPublicState> relics =
        [
            Revealed("E5", RelicFixtures.Command(2), mine),
            Revealed("G5", RelicFixtures.Command(), RelicControl.Contested),
            Revealed("C3", RelicFixtures.Command(2), new RelicControl(RelicControlKind.Blocked, P1)),
            Revealed("H8", RelicFixtures.Command(2), new RelicControl(RelicControlKind.Controlled, P2)),
            new(TestMaps.At("B8"), Spec, IsRevealed: false, Content: null, RevealedInMajorRound: null, mine),
        ];

        PublicDeployEffects effects = PublicRelicEffects.Deploy(P1, 5, relics);
        Assert.Equal(6, effects.DeployLimit);
        Assert.Equal(4, effects.BaseLimit);
        Assert.Equal([(TestMaps.At("E5"), 2)], effects.CommandSources.Select(kv => (kv.Key, kv.Value)));
        Assert.False(effects.WorkshopActive);
        Assert.Equal(3, PublicRelicEffects.Deploy(P1, 2, []).DeployLimit);
        Assert.True(PublicRelicEffects.Deploy(P1, 2, [Revealed("J1", RelicFixtures.Workshop(), mine)]).WorkshopActive);
    }

    [Fact]
    public void 公开先锋修正不计已弃赛者()
    {
        // 大回合结束读先锋（设计文档 §11.2）：只输出参赛中的玩家；已弃赛者即使盘上留子占着先锋也不在输出里（封锁，不给任何人）。
        // 账本委托后的结果与读公开快照的纯函数逐项相等。变异 M-P2（先锋修正计入已弃赛者）→ 见段 A 实施记录。
        MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Vanguard(2)), ("C3", RelicFixtures.Vanguard())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "C3").Stones(P1, "E5");
        match.PlayTurn("B8");
        match.Resign(P1);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = match.Roster;

        ImmutableSortedDictionary<PlayerId, int> ledger =
            RelicLedger.Restore(match.Relics.Generation, match.Relics.ExportState()).ReadInitiativeBonuses(match.Board, roster);
        MatchPublicView view = match.Publish();
        ImmutableSortedDictionary<PlayerId, int> pure =
            PublicRelicEffects.InitiativeBonuses(view.Players.Where(p => p.IsActive).Select(p => p.Player), view.Relics);

        Assert.Equal([P0, P2, P3], ledger.Keys);
        Assert.Equal(1, ledger[P0]);
        Assert.Equal(0, ledger[P2]);
        Assert.Equal(ledger, pure);
    }

    [Fact]
    public void 出局判据()
    {
        // elimination-endgame「出局判定」：曾建立正势力且总势力为 0；开局空盘的 0 势力不算清零；非参赛者不再出局。
        Assert.True(PublicRules.IsEliminated(hasEstablishedPower: true, BigInteger.Zero));
        Assert.False(PublicRules.IsEliminated(hasEstablishedPower: false, BigInteger.Zero));
        Assert.False(PublicRules.IsEliminated(hasEstablishedPower: true, BigInteger.One));

        MatchFlow match = AiFixtures.Round5().Stones(P1, "J9").Stones(P2, "B8");
        match.Resign(P2);
        match.Board.RemoveStones([TestMaps.At("J9"), TestMaps.At("B8")]);
        PowerSnapshot power = PowerCalculator.Compute(match.Board, match.Roster);
        Assert.Equal([P1], PublicRules.Eliminated(match.PlayerStates, power));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void v5真实对局每个小回合新旧两条路径逐项相等(ulong seed)
    {
        // 4 人棋盘图（retire-legacy-maps 段 A；原为 v5 整局，测试名沿用规格 Scenario 名）、种子 1–3、4 名标准 AI、20 个小回合，每个小回合（部署阶段）比对：
        // ① 合法落子范围：旧实现（抄自改动前的 MatchFlow.LegalRangeFor）＝ 对局下发的批次上下文 ＝ 纯函数读公开快照；
        // ② 部署上限与工坊：旧实现（抄自改动前 RelicLedger.BuildSnapshot 的军令 / 工坊部分，读真实内容）＝ 批次上下文 ＝ 纯函数读公开快照；
        // ③ 先锋修正：旧实现（抄自改动前的 SumVanguard）＝ 账本（委托后）＝ 纯函数读公开快照；
        // ④ 出局：此刻没有"应出局而仍参赛"的玩家，每个已出局者都满足判据（旧判据）。
        int checkedTurns = 0;
        // retire-legacy-maps 段 A：4 人棋盘图（原 v5 整局）、截断 20 个小回合——每个小回合比对一次，恰够下面"至少 20 个小回合"的下界。
        MatchSession session = MatchSession.Create(BoardConfig(Standard, Standard, Standard, Standard) with { TurnLimit = 20 }, seed);
        foreach (PlayerId player in session.Match.Players)
        {
            HeuristicTurnController inner = session.AiOf(player)!;
            session.SetController(player, new ProbeController(inner, batch =>
            {
                CompareAll(session.Match, batch);
                checkedTurns++;
            }));
        }

        Assert.Null(session.Run().Failure);
        Assert.True(checkedTurns >= 20, $"只比对了 {checkedTurns} 个小回合");
    }

    [Fact]
    public void 夹具局面新旧两条路径逐项相等()
    {
        // 9×9 夹具：保护期（第 2 大回合）与全图（第 5 大回合），含活棋禁入（P1 在 P0 出生区角上的两眼活棋，眼 A1 / C1）、
        // 已揭示军令 / 工坊 / 先锋与弃赛者。
        foreach (int round in new[] { 2, 5 })
        {
            MatchFlow match = MatchFixtures.Started(relics: [("E5", RelicFixtures.Command(2)), ("G5", RelicFixtures.Workshop()), ("C5", RelicFixtures.Vanguard())])
                .AtRound(round, [P2, P0, P1, P3])
                .Stones(P0, "C5")
                .Stones(P1, "B1", "D1", "A2", "B2", "C2", "D2", "E5", "G4").Stones(P3, "J9");
            match.PlayTurn("B8");
            match.Resign(P3);
            StagedBatch batch = match.OpenDeploy();
            CompareAll(match, batch);
            Assert.Equal(P0, batch.Context.Player);
        }
    }

    private static void CompareAll(MatchFlow match, StagedBatch batch)
    {
        PlayerId player = batch.Context.Player;
        MatchPublicView view = match.Publish();
        PlayerFlowState state = view.Players.Single(p => p.Player == player);

        // ① 合法落子范围
        Coord[] old = [.. OldLegalRange(match, player).Order()];
        Assert.Equal(old, batch.Context.LegalRange.Order());
        Assert.Equal(old, match.LegalRangeFor(player).Order());
        Assert.Equal(old, PublicRules.LegalRange(view.Board, view.MajorRound, state.BirthZone!.Value, player, view.LifeShape).Order());

        // ② 部署上限与工坊
        (int oldLimit, bool oldWorkshop) = OldDeployAndWorkshop(match, player);
        PublicDeployEffects pure = PublicRelicEffects.Deploy(player, view.MajorRound, view.Relics);
        Assert.Equal(oldLimit, batch.Context.DeployLimit);
        Assert.Equal(oldLimit, pure.DeployLimit);
        Assert.Equal(oldWorkshop, batch.Context.WorkshopActive);
        Assert.Equal(oldWorkshop, pure.WorkshopActive);

        // ③ 先锋修正（在账本副本上读，不动正式账本）
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = match.Roster;
        RelicLedger copy = RelicLedger.Restore(match.Relics.Generation, match.Relics.ExportState());
        ImmutableSortedDictionary<PlayerId, int> ledger = copy.ReadInitiativeBonuses(match.Board, roster);
        Assert.Equal(OldVanguard(copy, roster), ledger);
        Assert.Equal(ledger, PublicRelicEffects.InitiativeBonuses(roster.Where(kv => kv.Value == PlayerStatus.Active).Select(kv => kv.Key), copy.PublicStates()));

        // ④ 出局
        PowerSnapshot power = match.Scoreboard.Latest!;
        foreach (PlayerFlowState s in view.Players)
        {
            bool old4 = s.HasEstablishedPower && power.Of(s.Player).Total == BigInteger.Zero;
            if (s.Status == PlayerStatus.Active)
            {
                Assert.False(old4, $"{s.Player} 满足出局判据却仍参赛");
            }
        }

        Assert.Empty(PublicRules.Eliminated(view.Players, power));
    }

    /// <summary>改动前的 <c>MatchFlow.LegalRangeFor</c>（HEAD 03d45f6，原样抄录）。</summary>
    private static IReadOnlySet<Coord> OldLegalRange(MatchFlow match, PlayerId player)
    {
        int zone = match.StateOf(player).BirthZone!.Value;
        IEnumerable<Coord> range = match.MajorRound <= 3
            ? match.Map.BirthZones[zone]
            : match.Board.AllCoords().Where(c => match.Board[c].Terrain == Terrain.Playable);
        return range.Except(LifeShapeReport.Analyze(match.Board).ForbiddenCellsFor(player)).ToImmutableHashSet();
    }

    /// <summary>改动前 <c>RelicLedger.BuildSnapshot</c> 的军令 / 工坊部分（读真实内容与账本此刻的控制）。</summary>
    private static (int DeployLimit, bool Workshop) OldDeployAndWorkshop(MatchFlow match, PlayerId player)
    {
        int deploy = EffectSnapshot.BaseDeployLimitFor(match.MajorRound);
        bool workshop = false;
        ImmutableSortedDictionary<Coord, RelicType> contents = match.Relics.TrueContents();
        foreach (Coord coord in match.Relics.Coords)
        {
            if (!match.Relics.ControlOf(coord).GrantsEffectTo(player))
            {
                continue;
            }

            RelicContent content = match.Relics.Generation.At(coord).Content;
            Assert.Equal(contents[coord], content.Type);
            if (content.Type == RelicType.Command)
            {
                deploy += content.Magnitude;
            }
            else if (content.Type == RelicType.Workshop)
            {
                workshop = true;
            }
        }

        return (deploy, workshop);
    }

    /// <summary>改动前的 <c>RelicLedger.SumVanguard</c>（对参赛中的玩家；读真实内容与账本副本此刻的控制）。</summary>
    private static ImmutableSortedDictionary<PlayerId, int> OldVanguard(RelicLedger ledger, IReadOnlyDictionary<PlayerId, PlayerStatus> roster)
    {
        ImmutableSortedDictionary<PlayerId, int>.Builder bonuses = ImmutableSortedDictionary.CreateBuilder<PlayerId, int>();
        foreach (PlayerId player in roster.Where(kv => kv.Value == PlayerStatus.Active).Select(kv => kv.Key))
        {
            int bonus = 0;
            foreach (Coord coord in ledger.Coords)
            {
                RelicContent content = ledger.Generation.At(coord).Content;
                if (content.Type == RelicType.Vanguard && ledger.ControlOf(coord).GrantsEffectTo(player))
                {
                    bonus += content.Magnitude;
                }
            }

            bonuses[player] = bonus;
        }

        return bonuses.ToImmutable();
    }
}
