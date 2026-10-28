using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// expert-lookahead tasks 1.3（design D7）：合法落子范围、出局判据等纯函数从 <c>MatchFlow</c> 抽出，原处改为委托、行为零变化；对局路径与读公开快照的纯函数路径逐项比对。
/// relic-redesign 接线段 W1：公开部署上限与公开先锋修正两个纯函数随旧信物删除（部署上限只剩分阶段基础值、先手只按名次），对应的两条算例与"旧路径"抄本一并删除；
/// 部署上限改为与分阶段基础值（<see cref="EffectSnapshot.BaseDeployLimitFor"/>）比对。
/// </summary>
public class 公开规则纯函数Tests
{
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
        // ② 部署上限：批次上下文 ＝ 分阶段基础值（relic-redesign W1：没有信物加成）；
        // ④ 出局：此刻没有"应出局而仍参赛"的玩家，每个已出局者都满足判据（旧判据）。
        int checkedTurns = 0;
        // relic-redesign W1：旧信物系统删除，载体回到 link:1:4 生成图本身（原 LinkModeFixtures.RelicMap 手写信物夹具随旧信物删除）。
        MatchFlow match = LinkModeFixtures.Create(new Siege.Core.Determinism.GameSeed(seed));
        match.PlantPrototype();
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
        // 平坦 8×8 链接载体（retire-legacy-rules 1.4，原 9×9 夹具）：构筑期（第 2 大回合）与第 5 大回合，含活棋禁入（P1 在棋盘 0 角上的两眼活棋，眼 C3 / E3）
        // 与弃赛者（relic-redesign W1：原已揭示军令 / 工坊 / 先锋随旧信物删除）。
        foreach (int round in new[] { 2, 5 })
        {
            MatchFlow match = LinkModeFixtures.PlainStarted()
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

        // ② 部署上限：分阶段基础值
        Assert.Equal(EffectSnapshot.BaseDeployLimitFor(view.MajorRound), batch.Context.DeployLimit);

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
}
