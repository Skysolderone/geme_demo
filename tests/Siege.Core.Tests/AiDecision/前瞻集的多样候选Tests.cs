using System.Collections.Immutable;
using Siege.Core.Ai;
using Siege.Core.Board;
using Siege.Core.Match;
using static Siege.Core.Tests.LookaheadFixtures;

namespace Siege.Core.Tests.AiDecision;

/// <summary>
/// 规格：ai-decision（expert-strength）—— Requirement: 前瞻集的多样候选。
/// 前瞻集的构造（去重、补足、次序、上限、关闭）用合成候选与"按排除格给出结果"的替身重跑逐条取样；排除重跑本身（同一停手阈值）用真实局面。
/// </summary>
public class 前瞻集的多样候选Tests
{
    /// <summary>按排除格查表的替身重跑：记下每次调用的排除格；表里没有的格返回空批次。</summary>
    private sealed class FakeRerun(Dictionary<string, CandidateBatch> results)
    {
        public List<string> Calls { get; } = [];

        public CandidateBatch Run(Coord excluded)
        {
            string cell = excluded.ToNotation();
            Calls.Add(cell);
            return results.TryGetValue(cell, out CandidateBatch? result) ? result : SyntheticCandidates.Pass(0);
        }
    }

    private static string Keys(ImmutableArray<ExpertLookahead.LookaheadMember> set) =>
        string.Join(" | ", set.Select(m => $"{m.Candidate.Key}@{m.Source}"));

    [Fact]
    public void 格集合相同只留排序靠前者()
    {
        // A 与 B 的落点格都是 {C1, C2, C3}：A 全是普通子、B 在 C2 放堡垒子，A 的自身总分高于 B → 前瞻集含 A、不含 B（只比格，不比类型）。
        // 变异 E-D1（去重键连类型一起比）→ 见段 A 实施记录。
        CandidateBatch a = SyntheticCandidates.Typed("C1:Basic,C2:Basic,C3:Basic", 10);
        CandidateBatch b = SyntheticCandidates.Typed("C1:Basic,C2:Fortress,C3:Basic", 9);
        CandidateBatch c = SyntheticCandidates.Typed("E5:Basic", 5);
        var rerun = new FakeRerun([]);

        var set = ExpertLookahead.LookaheadSet(SyntheticCandidates.List(b, c, a), 4, 8, rerun.Run);

        Assert.Equal("C1:Basic,C2:Basic,C3:Basic@Original | E5:Basic@Original", Keys(set));
    }

    [Fact]
    public void 排除重跑补足落点不同的候选()
    {
        // W = 4、S = 8，去重后前瞻集只有 A（落点次序 C1、C2、C3）。排除 C1、C2 重跑的结果格集合各不相同且与 A 不同；排除 C3 的结果与排除 C2 的结果格集合相同 →
        // 前瞻集依次为 A、排除 C1 的结果、排除 C2 的结果，排除 C3 的结果被丢弃；再以下一个锚（排除 C1 的结果，落点次序 C2、C3、D1）继续：
        // 排除 C2 / C3 与已有成员重复被丢弃，排除 D1 得到新的格集合 → 追加，达到 W 即停。补充成员的来源记为多样补充，且都不含被排除的那一格。
        CandidateBatch a = SyntheticCandidates.Typed("C1:Basic,C2:Basic,C3:Basic", 30);
        CandidateBatch x1 = SyntheticCandidates.Typed("C2:Basic,C3:Basic,D1:Basic", 20);
        CandidateBatch x2 = SyntheticCandidates.Typed("C1:Basic,D2:Basic,D3:Basic", 25);
        CandidateBatch x3 = SyntheticCandidates.Typed("D3:Fortress,C1:Basic,D2:Basic", 26);
        CandidateBatch x4 = SyntheticCandidates.Typed("C1:Basic,C2:Basic,E5:Basic", 1);
        var rerun = new FakeRerun(new() { ["C1"] = x1, ["C2"] = x2, ["C3"] = x3, ["D1"] = x4 });

        var set = ExpertLookahead.LookaheadSet(SyntheticCandidates.List(a), 4, 8, rerun.Run);

        Assert.Equal(["C1", "C2", "C3", "C2", "C3", "D1"], rerun.Calls);
        Assert.Equal($"{a.Key}@Original | {x1.Key}@Supplement | {x2.Key}@Supplement | {x4.Key}@Supplement", Keys(set));
        Assert.All(set.Skip(1).Zip(new[] { "C1", "C2", "D1" }), p => Assert.DoesNotContain(TestMaps.At(p.Second), CoordsOf(p.First.Candidate.Key)));
    }

    [Fact]
    public void 补充候选同样受停手阈值()
    {
        // 真实局面（9×9、第 5 大回合、专家 P0 的部署上限 1）：左下 B1 / A2 / B2 / C2 / D2 已有眼 A1，D1 围出第二眼（自身 1020）；停手阈值 700，其余落点都越不过。
        // 候选生成只得到 {D1}；多样补充排除 D1 重跑时剩余每个单点的边际提升都不大于阈值 → 空批次，不追加，前瞻集不出现空批次 → 仍是"不前瞻"的单个候选。
        // 变异 E-D3（排除重跑绕过停手阈值：阈值传 0）→ 见段 A 实施记录。
        MatchFlow match = AiFixtures.Round5().Stones(P0, "B1", "A2", "B2", "C2", "D2");
        match.SetDeployLimit(1);
        HeuristicTurnController expert = Decide(match, AiDifficulty.Expert, StrengthConfig(passThreshold: 700, permille: 0));
        LookaheadRecord record = Record(expert);

        Assert.Equal(["D1:Basic"], expert.LastCandidates.Where(c => !c.IsPass).Select(c => c.Key));
        Assert.True(record.SupplementRehearsals > 0, record.ToText());   // 确实做过排除重跑
        Assert.Equal(LookaheadStatus.NotApplied, record.Status);
        LookaheadEntry only = Assert.Single(record.Entries);
        Assert.Equal("D1:Basic", only.CandidateKey);
        Assert.Equal(CandidateSource.Original, only.Source);
        Assert.Equal("D1:Basic", expert.LastChoice!.Key);
    }

    [Fact]
    public void 前瞻集第一个仍是高难的选择()
    {
        // 多样补充成员的自身总分（50）高于全部原排序成员（10）→ 它排在原排序成员之后；前瞻集的第一个与高难的选择（CandidateSelection.Best）相同。
        // 真实局面上的同一性质由 专家前瞻的确定性与耗时Tests.不消费新随机 逐局面核对。
        // 变异 E-D2（补充成员按自身总分插到前面）→ 见段 A 实施记录。
        CandidateBatch a = SyntheticCandidates.Typed("C1:Basic", 10);
        CandidateBatch high = SyntheticCandidates.Typed("D1:Fortress", 50);
        var candidates = SyntheticCandidates.List(a);
        var rerun = new FakeRerun(new() { ["C1"] = high });

        var set = ExpertLookahead.LookaheadSet(candidates, 4, 8, rerun.Run);

        Assert.Equal("C1:Basic@Original | D1:Fortress@Supplement", Keys(set));
        Assert.Equal(CandidateSelection.Best(candidates).Key, set[0].Candidate.Key);
    }

    [Fact]
    public void 已满W个不补足()
    {
        // 去重后已有 W 个落点格集合各不相同的原排序成员 → 不做任何排除重跑，前瞻集即这 W 个成员（第 5 个与已有成员不重复也不进）。
        // 变异 E-D4（已满 W 仍做排除重跑）→ 见段 A 实施记录。
        var candidates = SyntheticCandidates.List(
            SyntheticCandidates.Typed("C1:Basic", 50),
            SyntheticCandidates.Typed("C1:Fortress", 49),
            SyntheticCandidates.Typed("C2:Basic", 40),
            SyntheticCandidates.Typed("C3:Basic", 30),
            SyntheticCandidates.Typed("C4:Basic", 20),
            SyntheticCandidates.Typed("C5:Basic", 10));
        var rerun = new FakeRerun(new() { ["C1"] = SyntheticCandidates.Typed("J9:Basic", 1) });

        var set = ExpertLookahead.LookaheadSet(candidates, 4, 8, rerun.Run);

        Assert.Empty(rerun.Calls);
        Assert.Equal("C1:Basic@Original | C2:Basic@Original | C3:Basic@Original | C4:Basic@Original", Keys(set));
    }

    [Fact]
    public void 达到补充上限即停止()
    {
        // S = 2：第一次排除重跑补出 1 个新成员，第二次与已有成员重复 → 两次之后前瞻集仍只有 2 个成员，不再做排除重跑，按这 2 个成员前瞻。
        CandidateBatch a = SyntheticCandidates.Typed("C1:Basic,C2:Basic", 30);
        var rerun = new FakeRerun(new()
        {
            ["C1"] = SyntheticCandidates.Typed("C2:Basic,D5:Basic", 20),
            ["C2"] = SyntheticCandidates.Typed("C1:Fortress,C2:Basic", 25),
            ["D5"] = SyntheticCandidates.Typed("J9:Basic", 1),
        });

        var set = ExpertLookahead.LookaheadSet(SyntheticCandidates.List(a), 4, 2, rerun.Run);

        Assert.Equal(["C1", "C2"], rerun.Calls);
        Assert.Equal("C1:Basic,C2:Basic@Original | C2:Basic,D5:Basic@Supplement", Keys(set));
    }

    [Fact]
    public void 多样候选关闭()
    {
        // S = 0：前瞻集与一层前瞻第 2 步的原规则逐项相同——不按格集合去重（格集合相同的 A、B 都在），不做排除重跑。
        // 变异 E-G2（S = 0 时仍按格集合去重）→ 本测试与 G1（一层配置与改动前的专家逐步相同）都应红，见段 A 实施记录。
        var candidates = SyntheticCandidates.List(
            SyntheticCandidates.Typed("C1:Basic,C2:Basic", 30),
            SyntheticCandidates.Typed("C1:Basic,C2:Fortress", 20),
            SyntheticCandidates.Pass(1_000));
        var rerun = new FakeRerun(new() { ["C1"] = SyntheticCandidates.Typed("J9:Basic", 1) });

        var set = ExpertLookahead.LookaheadSet(candidates, 4, 0, rerun.Run);

        Assert.Empty(rerun.Calls);
        Assert.Equal("C1:Basic,C2:Basic@Original | C1:Basic,C2:Fortress@Original", Keys(set));
        Assert.Equal(ExpertLookahead.LookaheadSet(candidates, 4).Select(c => c.Key), set.Select(m => m.Candidate.Key));
    }
}
