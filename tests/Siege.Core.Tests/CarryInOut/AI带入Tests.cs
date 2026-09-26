using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Core.Tests.CarryInOut;

/// <summary>规格：carry-in-out —— Requirement: AI 带入</summary>
public class AI带入Tests
{
    private static readonly PlayerId[] Four = CarryFixtures.Four;
    private static readonly PlayerId Me = Four[0];

    private static MapData Map => MapCatalog.Resolve(MapCatalog.DefaultId);

    /// <summary>人机对局的建局：本机玩家 P0 的选择 + AI 的抽取，开启带入带出。</summary>
    private static MatchFlow Create(GameSeed seed, CarryIn? mine, ContentSet set = ContentSet.V2) =>
        MatchFlow.Create(Map, seed, Four, MatchOptions.Immediate with
        {
            ContentSet = set,
            CarryInOut = true,
            CarryIns = CarryAi.ForHumanMatch(seed, Four, Me, mine, set),
        });

    private static string Relics(MatchFlow match) =>
        string.Join(" ", match.Relics.Generation.Placements.Select(p => $"{p.Coord.ToNotation()}:{p.Content}"));

    private static string AiCarries(IReadOnlyDictionary<PlayerId, CarryIn> carries) =>
        CarryFixtures.CarryText(carries.Where(kv => kv.Key != Me).ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value));

    [Fact]
    public void 同等数量()
    {
        // 规格 Scenario：4 人局本机玩家带入 1 件 → 3 名 AI 各带入 1 件，对局公开视图列出全部 4 名玩家的带入。
        for (ulong s = 1; s <= 8; s++)
        {
            var seed = new GameSeed(s);
            ImmutableSortedDictionary<PlayerId, CarryIn> carries = CarryAi.ForHumanMatch(seed, Four, Me, CarryFixtures.Spare, ContentSet.V2);
            Assert.Equal(Four, carries.Keys);
            Assert.Equal(CarryFixtures.Spare, carries[Me]);

            MatchFlow match = Create(seed, CarryFixtures.Spare);
            Assert.Equal(Four, match.Publish().CarryIns.Keys);
            Assert.All(match.Publish().CarryIns.Values, c => Assert.True(c.Kind == SupplyKind.SpareStone || c.Type is not null, "征召签 / 换型令在公开视图里须带类型"));
        }
    }

    [Fact]
    public void 本机不带则AI不带()
    {
        // 规格 Scenario：4 人局本机玩家不带入 → 3 名 AI 都不带入，carry-ai 与任何 carry-draft 子流都不被消费。
        var seed = new GameSeed(11);
        Assert.Empty(CarryAi.ForHumanMatch(seed, Four, Me, null, ContentSet.V2));
        Assert.Empty(CarryAi.Draw(seed, Four[1..], 0, ContentSet.V2));

        RandomStream stream = seed.Stream(GameSeed.CarryAi);
        Assert.Empty(CarryAi.Draw(stream, Four[1..], 0, ContentSet.V2));
        Assert.Equal(0, stream.Consumed);

        // 建局：全员无带入 → 开局手牌与关闭时逐项相同（carry-draft 没有被派生去解析任何征召签）。
        MatchFlow match = Create(seed, null);
        Assert.Empty(match.CarryIns);
        Assert.All(Four, p => Assert.Equal("Basic×5+0", CarryFixtures.HandText(match, p)));
    }

    [Fact]
    public void AI带入可复现()
    {
        // 规格 Scenario：同一种子、同一内容集、本机玩家各带 1 件，分别开两局 → 两局中每名 AI 的补给与类型逐项相同。
        for (ulong s = 1; s <= 8; s++)
        {
            var seed = new GameSeed(s);
            Assert.Equal(AiCarries(Create(seed, CarryFixtures.Draft).CarryIns), AiCarries(Create(seed, CarryFixtures.Draft).CarryIns));
        }
    }

    [Fact]
    public void AI带入用独立子流carry_ai按编号升序等概率抽取()
    {
        // 规格正文：按玩家编号升序，每名 AI 先从（备用子、征召签、换型令）中等概率抽一种；抽到换型令再从候选类型中按固定次序等概率抽一种。子流为 carry-ai。
        // 期望用测试内独立算式重算（testing.md「可复算守门必须用测试内独立算式」）：子流名、种类次序与候选次序都在测试里字面写出，不读实现。
        PieceType[] v1 = [PieceType.Fortress, PieceType.Line, PieceType.Synergy];
        PieceType[] v2 = [PieceType.Fortress, PieceType.Line, PieceType.Synergy, PieceType.Bannerman, PieceType.Chain, PieceType.Sentry, PieceType.Boundary];
        SupplyKind[] kinds = [SupplyKind.SpareStone, SupplyKind.DraftLot, SupplyKind.Commission];
        var seen = new HashSet<string>();
        foreach ((ContentSet set, PieceType[] candidates) in new[] { (ContentSet.V1, v1), (ContentSet.V2, v2) })
        {
            for (ulong s = 1; s <= 40; s++)
            {
                var seed = new GameSeed(s);
                RandomStream rng = seed.Stream("carry-ai");
                var expected = new List<string>();
                foreach (PlayerId ai in new[] { Four[1], Four[2], Four[3] })
                {
                    SupplyKind kind = kinds[rng.NextInt(3)];
                    PieceType? type = kind == SupplyKind.Commission ? candidates[rng.NextInt(candidates.Length)] : null;
                    expected.Add($"{ai}:{kind}>{type?.ToString() ?? "-"}");
                    seen.Add($"{kind}>{type?.ToString() ?? "-"}");
                }

                // 传入次序打乱：结果仍按编号升序抽取。
                ImmutableSortedDictionary<PlayerId, CarryIn> drawn = CarryAi.Draw(seed, [Four[3], Four[1], Four[2]], 1, set);
                Assert.Equal(string.Join(" ", expected), CarryFixtures.CarryText(drawn));
            }
        }

        // 样本口径下界：三种补给都抽到过，换型令抽到过至少 3 种类型。
        Assert.Contains("SpareStone>-", seen);
        Assert.Contains("DraftLot>-", seen);
        Assert.True(seen.Count(k => k.StartsWith("Commission>", StringComparison.Ordinal)) >= 3, string.Join(" ", seen));
    }

    [Fact]
    public void 带入数量只能为0或1()
    {
        // 每名玩家每局 MUST NOT 带入超过 1 件；AI 名单重复即响亮失败。
        Assert.Throws<ArgumentOutOfRangeException>(() => CarryAi.Draw(new GameSeed(1), Four[1..], 2, ContentSet.V2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CarryAi.Draw(new GameSeed(1), Four[1..], -1, ContentSet.V2));
        Assert.Throws<ArgumentException>(() => CarryAi.Draw(new GameSeed(1), [Four[1], Four[1]], 1, ContentSet.V2));
    }

    [Fact]
    public void 与本机玩家的选择无关()
    {
        // 规格 Scenario：同一种子下本机玩家第一次带入备用子，第二次带入换型令（指定连珠子）
        // → 两局中每名 AI 的补给与类型（含征召签抽得的类型）逐项相同，信物分布与首回合顺序也逐项相同。
        bool sawDraft = false;
        for (ulong s = 1; s <= 12; s++)
        {
            var seed = new GameSeed(s);
            MatchFlow spare = Create(seed, CarryFixtures.Spare);
            MatchFlow commission = Create(seed, CarryFixtures.Commission(PieceType.Line));

            Assert.Equal(AiCarries(spare.CarryIns), AiCarries(commission.CarryIns));
            Assert.Equal(SupplyKind.Commission, commission.CarryIns[Me].Kind);
            sawDraft |= spare.CarryIns.Any(kv => kv.Key != Me && kv.Value.Kind == SupplyKind.DraftLot);

            spare.PlantPrototype();
            commission.PlantPrototype();
            Assert.Equal(Relics(spare), Relics(commission));
            Assert.Equal(spare.ActionOrder, commission.ActionOrder);
        }

        Assert.True(sawDraft, "样本口径：12 颗种子里应有 AI 抽到征召签");
    }

    [Fact]
    public void 不扰动征募序列()
    {
        // 规格 Scenario：同一种子下 AI 带入的均为备用子，与全员不带入的对局对照
        // → 两局的信物分布逐格相同、第 1 大回合行动顺序相同，第 1 大回合第一位行动者的征募候选逐项相同。
        // 样本：种子 1–400 中第一颗让 3 名 AI 都抽到备用子的种子（每颗 1/27）；本机玩家也带备用子，保证全员只多一枚普通子。
        GameSeed? pick = Enumerable.Range(1, 400).Select(i => (GameSeed?)new GameSeed((ulong)i))
            .FirstOrDefault(s => CarryAi.ForHumanMatch(s!.Value, Four, Me, CarryFixtures.Spare, ContentSet.V2).Values.All(c => c.Kind == SupplyKind.SpareStone));
        Assert.True(pick is not null, "样本口径：种子 1–400 中应有 AI 全抽备用子的一颗");
        GameSeed seed = pick!.Value;

        MatchFlow carried = Create(seed, CarryFixtures.Spare);
        MatchFlow plain = MatchFlow.Create(Map, seed, Four, MatchOptions.Immediate);
        Assert.All(Four, p => Assert.Equal("Basic×6+0", CarryFixtures.HandText(carried, p)));

        carried.PlantPrototype();
        plain.PlantPrototype();
        Assert.Equal(Relics(plain), Relics(carried));
        Assert.Equal(plain.ActionOrder, carried.ActionOrder);

        carried.BeginTurn();
        plain.BeginTurn();
        Assert.Equal(plain.EnterRecruit().CandidateTypes, carried.EnterRecruit().CandidateTypes);
    }
}
