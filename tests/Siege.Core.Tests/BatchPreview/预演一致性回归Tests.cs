using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Preview;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>强制回归（implement 6.2 / 2.10）：随机局面上富预演与正式结算逐项一致，且预演零副作用。</summary>
public class 预演一致性回归Tests
{
    private const int Cases = 1000;

    [Fact]
    public void 千例随机局面预演与结算一致()
    {
        // 每例：9×9 夹具地图 + 5 个信物格，第 5 大回合（全图可落子）；种子驱动的 RandomStream 随机铺子（禁止 System.Random），
        // 清掉无气棋串、按盘面揭示信物，使局面满足"每条棋串 ≥1 气、被覆盖的信物已揭示"的正式对局不变量；
        // P0 暂放 1–5 枚（部署上限随机，六成落点取自敌方 ≤2 气棋串的气位以提高提子率），预演后确认。
        // 断言：预演前后指纹不变；合法 ⇔ 确认被接受；预计提子 = 实际提子；将揭示 = 实际新揭示；预计势力与名次 = 结算后势力榜；
        // 结算后己方棋串与气 = 预演的己方棋串与气；非法时失败类别与坐标一致。
        // 口径守门：提子、将揭示、自杀手、合法无提子四类样本各有下界，防止随机局面退化成从不触发被测路径。
        // 变异验证 M-C1：BatchPreviewBuilder.GroupCaptures 丢弃每串最后一枚（stones 少加一枚、remaining 照删）→ 本测试红 1。
        // 变异验证 M-C2：将揭示改为按正式盘面（board）而非结算后盘面（projected）调用 Reveal → 本测试红 1。
        // 变异验证 M-C3：PowerChange 的 After 改用 before 快照 → 本测试红 1。
        int captures = 0, reveals = 0, suicides = 0, quiet = 0;
        for (int i = 0; i < Cases; i++)
        {
            var seed = new GameSeed(0x7AC7_0000UL + (ulong)i);
            RandomStream rng = seed.Stream("preview-regression");
            MatchFlow match = MatchFixtures.Started(seed, relics:
                [("C3", RelicFixtures.Command()), ("G3", RelicFixtures.Conscription()), ("E5", RelicFixtures.Prospecting()),
                 ("C7", RelicFixtures.Vanguard()), ("G7", RelicFixtures.Depot(2))]).AtRound(5);
            Scatter(match, rng);
            match.Debug.SeedHand(P0, (PieceType.Basic, 3), (PieceType.Fortress, 2), (PieceType.Line, 2), (PieceType.Multiplier, 2), (PieceType.Synergy, 2));
            match.SetDeployLimit(1 + rng.NextInt(5));
            StagedBatch batch = match.OpenDeploy();
            StageRandom(match, batch, rng);

            string before = Fingerprint(match);
            Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();
            Assert.Equal(before, Fingerprint(match));

            int revealedBefore = match.Relics.RevealEvents.Count;
            SettlementOutcome outcome = match.Confirm();
            string label = $"第 {i} 例 批次 {string.Join(" ", preview.Placements)}";
            Assert.True(preview.IsLegal == outcome.Confirmed, $"{label}：预演合法 {preview.IsLegal}，确认 {outcome.Confirmed}（{outcome.Failure?.Message}）");

            if (!outcome.Confirmed)
            {
                Assert.Equal(outcome.Failure!.Kind, preview.Failure!.Kind);
                Assert.Equal(outcome.Failure.Coords.Notations(), preview.Failure.Coords.Notations());
                suicides += outcome.Failure.Kind == BatchFailureKind.Suicide ? 1 : 0;
                continue;
            }

            Assert.Equal(outcome.CaptureRecord!.Captured.Select(s => s.ToString()), preview.Captures.SelectMany(g => g.Stones).OrderBy(s => s.Coord).Select(s => s.ToString()));
            Assert.Equal(
                match.Relics.RevealEvents.Skip(revealedBefore).Select(e => e.Coord).Order().Notations(),
                preview.WillReveal.Notations());

            Scoring.PowerSnapshot settled = match.Scoreboard.Latest!;
            Assert.Equal(
                settled.Players.Select(p => $"{p.Player}:{p.Total}:{settled.RankOf(p.Player)}"),
                preview.PowerChanges.Select(c => $"{c.Player}:{c.After}:{c.RankAfter}"));
            Assert.Equal(
                LibertySnapshot.Compute(match.Board).Where(g => g.Owner == P0).Select(g => $"{g.Stones.Notations().Aggregate((a, b) => a + b)}/{string.Join("", g.Liberties.Notations())}"),
                preview.OwnGroups.Select(g => $"{g.Stones.Notations().Aggregate((a, b) => a + b)}/{string.Join("", g.Liberties.Notations())}"));
            Assert.Equal(
                settled.Of(P0).Groups.Select(g => g.ToString()),
                preview.OwnGroups.Select(g => g.Power!.ToString()));

            captures += preview.Captures.IsEmpty ? 0 : 1;
            reveals += preview.WillReveal.IsEmpty ? 0 : 1;
            quiet += preview.Captures.IsEmpty ? 1 : 0;
        }

        string samples = $"提子 {captures} / 将揭示 {reveals} / 自杀手 {suicides} / 合法无提子 {quiet}";
        // 本种子区间实测：提子 281 / 将揭示 35 / 自杀手 85 / 合法无提子 634。下界取约七成，种子或局面生成改动后样本塌缩即红。
        Assert.True(captures >= 200 && reveals >= 25 && suicides >= 50 && quiet >= 400, samples);
    }

    /// <summary>每格 45% 落子，所有者与类型均匀；再反复清掉无气棋串，最后按盘面揭示信物并重算。</summary>
    private static void Scatter(MatchFlow match, RandomStream rng)
    {
        PieceType[] types = Enum.GetValues<PieceType>();
        foreach (Coord c in match.Board.AllCoords())
        {
            if (rng.NextInt(100) < 45)
            {
                match.Board.Place(c, MatchFixtures.All[rng.NextInt(4)], types[rng.NextInt(types.Length)]);
            }
        }

        while (true)
        {
            ImmutableArray<Coord> dead = [.. match.Board.AllGroups().Where(match.Board.IsCaptured).SelectMany(g => g.Stones)];
            if (dead.IsEmpty)
            {
                break;
            }

            match.Board.RemoveStones(dead);
        }

        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
    }

    private static void StageRandom(MatchFlow match, StagedBatch batch, RandomStream rng)
    {
        Coord[] empty = [.. match.Board.AllCoords().Where(c => match.Board[c].IsPlayableEmpty)];
        Coord[] pressure = [.. LibertySnapshot.Compute(match.Board).Where(g => g.Owner != P0 && g.Count <= 2).SelectMany(g => g.Liberties).Distinct().Order()];
        PieceType[] types = Enum.GetValues<PieceType>();
        int want = 1 + rng.NextInt(batch.Context.DeployLimit);
        for (int attempt = 0; attempt < 20 && batch.Count < want && empty.Length > 0; attempt++)
        {
            Coord cell = pressure.Length > 0 && rng.NextInt(10) < 6 ? pressure[rng.NextInt(pressure.Length)] : empty[rng.NextInt(empty.Length)];
            _ = batch.Stage(cell, types[rng.NextInt(types.Length)]);
        }
    }
}
