using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Core.Scoring;
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
        MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("G6", RelicFixtures.Vanguard(2)), ("E4", RelicFixtures.Vanguard())])
            .AtRound(5, [P2, P0, P1, P3])
            .Stones(P0, "E4").Stones(P1, "G6");
        match.PlayTurn("D9");
        match.Resign(P1);
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = match.Roster;

        ImmutableSortedDictionary<PlayerId, int> ledger = match.Relics.Copy().ReadInitiativeBonuses(match.Board, roster);   // retire-legacy-rules：副本改用 Copy()（Restore 只服务旧模式恢复，R12 删除）
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

        MatchFlow match = AiFixtures.Round5().Stones(P1, "K10").Stones(P2, "D9");
        match.Resign(P2);
        match.Board.RemoveStones([TestMaps.At("K10"), TestMaps.At("D9")]);
        PowerSnapshot power = PowerCalculator.Compute(match.Board, match.Roster);
        Assert.Equal([P1], PublicRules.Eliminated(match.PlayerStates, power));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void 真实对局每个小回合新旧两条路径逐项相等(ulong seed)
    {
        // retire-legacy-maps 1.5：原「v5真实对局每个小回合新旧两条路径逐项相等」（v5、种子 1–3、4 名标准 AI 整局）改到 9×9 手写夹具上的 4 名标准 AI 整局。
        // retire-legacy-rules 1.4：载体改为 link:1 探针（链接模式、内置固定配对脚本、生成的信物——含军令 / 工坊 / 先锋，② ③ 比对的不是恒为缺省的值）上的 4 名标准 AI 整局；
        // ① 里抄自改动前的"保护期限出生区、此后全图"旧路径只在旧模式成立，删去（清单"删反面段"），改为三方相等。② ③ ④ 不变。
        // 每个小回合（部署阶段）比对：
        // ① 合法落子范围：对局下发的批次上下文 ＝ MatchFlow.LegalRangeFor ＝ 纯函数读公开快照；
        // ② 部署上限与工坊：旧实现（抄自改动前 RelicLedger.BuildSnapshot 的军令 / 工坊部分，读真实内容）＝ 批次上下文 ＝ 纯函数读公开快照；
        // ③ 先锋修正：旧实现（抄自改动前的 SumVanguard）＝ 账本（委托后）＝ 纯函数读公开快照；
        // ④ 出局：此刻没有"应出局而仍参赛"的玩家，每个已出局者都满足判据（旧判据）。
        int checkedTurns = 0;
        // open-map 1.1：link:1 的信物格随 3+2 布局改变，种子 1 / 3 生成的信物里不再有军令 / 工坊 / 先锋；从给定种子起取第一个满足样本口径的对局种子。
        MatchFlow match = LinkModeFixtures.Create(new Siege.Core.Determinism.GameSeed(seed));
        for (ulong probe = seed; !match.Relics.Generation.Placements.Any(p => p.Content.Type is RelicType.Command or RelicType.Workshop or RelicType.Vanguard); probe++)
        {
            Assert.True(probe < seed + 50, "50 个对局种子里没有军令 / 工坊 / 先锋");
            match = LinkModeFixtures.Create(new Siege.Core.Determinism.GameSeed(probe + 1));
        }

        match.PlantPrototype();
        Assert.Contains(match.Relics.Generation.Placements, p => p.Content.Type is RelicType.Command or RelicType.Workshop or RelicType.Vanguard);   // 样本口径
        var runner = new MatchRunner(match);
        foreach (PlayerId player in match.Players)
        {
            HeuristicTurnController inner = HeuristicAi.Create(match, player, AiDifficulty.Standard, Weights, AiSearchConfig.Standard with { PassThreshold = PassThreshold });
            runner.SetController(player, new ProbeController(inner, batch =>
            {
                CompareAll(match, batch);
                checkedTurns++;
            }));
        }

        int guard = 0;
        // map-expand-25 段 A2：link:1:4 换成约 1500 格的浮空岛后，4 名标准 AI 的整局要跑到大回合上限 100（全量里单条半小时以上），
        // 改为截断到第 6 大回合结束（构筑期 3 个大回合 + 全图解禁后 3 个大回合）；所守的是"每个小回合三方逐项相等"，与跑多长无关，小回合下界 20 不变。
        const int Rounds = 6;
        while (match.Phase == MatchPhase.InProgress && match.MajorRound <= Rounds)
        {
            Assert.True(++guard < 2000, "跑局超过 2000 个小回合");
            runner.RunTurn();
        }

        Assert.True(checkedTurns >= 20, $"只比对了 {checkedTurns} 个小回合");
    }

    [Fact]
    public void 夹具局面新旧两条路径逐项相等()
    {
        // 平坦 8×8 链接载体（retire-legacy-rules 1.4，原 9×9 夹具）：构筑期（第 2 大回合）与第 5 大回合，含活棋禁入（P1 在棋盘 0 角上的两眼活棋，眼 C3 / E3）、
        // 已揭示军令 / 工坊 / 先锋与弃赛者。
        foreach (int round in new[] { 2, 5 })
        {
            MatchFlow match = LinkModeFixtures.PlainStarted(relics: [("G7", RelicFixtures.Command(2)), ("H7", RelicFixtures.Workshop()), ("E7", RelicFixtures.Vanguard())])
                .AtRound(round, [P2, P0, P1, P3])
                .Stones(P0, "E7")
                .Stones(P1, "D3", "F3", "C4", "D4", "E4", "F4", "G7", "H6").Stones(P3, "K10");
            match.PlayTurn("D9");
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

        // ① 合法落子范围（retire-legacy-rules：旧路径 OldLegalRange 只在旧模式成立，已删；三方相等）
        Coord[] range = [.. batch.Context.LegalRange.Order()];
        Assert.NotEmpty(range);
        Assert.Equal(range, match.LegalRangeFor(player).Order());
        Assert.Equal(range, PublicRules.LegalRange(view.Board, state.BirthZone!.Value, player, view.LifeShape, view.MajorRound).Order());

        // ② 部署上限与工坊
        (int oldLimit, bool oldWorkshop) = OldDeployAndWorkshop(match, player);
        PublicDeployEffects pure = PublicRelicEffects.Deploy(player, view.MajorRound, view.Relics);
        Assert.Equal(oldLimit, batch.Context.DeployLimit);
        Assert.Equal(oldLimit, pure.DeployLimit);
        Assert.Equal(oldWorkshop, batch.Context.WorkshopActive);
        Assert.Equal(oldWorkshop, pure.WorkshopActive);

        // ③ 先锋修正（在账本副本上读，不动正式账本）
        IReadOnlyDictionary<PlayerId, PlayerStatus> roster = match.Roster;
        RelicLedger copy = match.Relics.Copy();
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

            RelicContent content = match.Relics.ContentAt(coord);   // retire-legacy-rules：链接拼接后信物随格平移，按账本当前坐标取内容（开局生成记录的坐标不再适用）
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
                RelicContent content = ledger.ContentAt(coord);
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
