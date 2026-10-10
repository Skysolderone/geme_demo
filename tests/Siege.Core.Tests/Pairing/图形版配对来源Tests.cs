using System.Collections.Immutable;
using Siege.Core.Match;
using Siege.Presentation.Pairing;

namespace Siege.Core.Tests.Pairing;

/// <summary>
/// board-link-mode 实现裁决 D3（tasks 4.1）：图形版有人值守对局的配对来源在 <c>Siege.Presentation</c>（零 Godot 依赖、不引用 Sim），真随机、不可复现、
/// 结果作为配对事件记录并注明来源 <see cref="PairingSourceNames.Random"/>；无人值守路径用 Core 的 <see cref="ScriptedPairingSource"/>。
/// 规格：simulation-harness「各入口支持生成图」（有人值守 MUST 真随机；无人值守 SHALL 固定脚本）；battlefield「配对事件由对局外部提供」（结果须合法）。
/// </summary>
/// <remarks>变异验证（段 D-a 实跑）：M-D8 <c>PairingDraw.Draw</c> 首轮不洗牌、也不配对（返回空配对）→ 「首轮配对合法_每个候选恰出现一次」红 1。</remarks>
public class 图形版配对来源Tests
{
    private static readonly PairingRequest Even = new(PairingKind.FirstRound, [0, 1, 3, 4], 4);
    private static readonly PairingRequest Odd = new(PairingKind.FirstRound, [1, 2, 4], 4);
    private static readonly PairingRequest Final = new(PairingKind.Final, [0, 3], 9);

    [Fact]
    public void 首轮配对合法_每个候选恰出现一次()
    {
        var source = new RandomPairingSource();
        for (int i = 0; i < 50; i++)
        {
            PairingDecision even = source.Decide(Even);
            Assert.Equal(PairingSourceNames.Random, even.Source);
            Assert.Null(even.Waiter);
            Assert.Equal(2, even.Pairs.Length);
            Assert.Equal([0, 1, 3, 4], even.Pairs.SelectMany(p => new[] { p.X, p.Y }).Order());
            Assert.All(even.Pairs, p => Assert.NotEqual(p.X, p.Y));

            PairingDecision odd = source.Decide(Odd);
            Assert.Single(odd.Pairs);
            Assert.NotNull(odd.Waiter);
            Assert.Equal([1, 2, 4], odd.Pairs.SelectMany(p => new[] { p.X, p.Y }).Append(odd.Waiter!.Value).Order());
        }
    }

    [Fact]
    public void 真随机_多次抽取结果不全相同()
    {
        // 4 个候选有 3 种配对；真随机 60 次全同的概率约 (1/3)^59，不会发生。
        var source = new RandomPairingSource();
        ImmutableArray<string> draws = [.. Enumerable.Range(0, 60).Select(_ => source.Decide(Even).ToString())];
        Assert.True(draws.Distinct().Count() >= 2, $"60 次抽取全部相同：{draws[0]}");
    }

    [Fact]
    public void 决赛只有一种合法结果_与脚本缺省相同()
    {
        PairingDecision final = new RandomPairingSource().Decide(Final);
        Assert.Equal(ScriptedPairingSource.Default(Final).Canonical, final.Canonical);
        Assert.Null(final.Waiter);
        Assert.Equal(PairingSourceNames.Random, final.Source);
    }

    [Fact]
    public void 无人值守用固定脚本_来源名为script()
    {
        // 图形版 --auto-demo / --pick-check / --screenshot= 挂 Core 的 ScriptedPairingSource：不抽随机数、缺省按编号升序两两相配。
        var scripted = new ScriptedPairingSource();
        PairingDecision decision = scripted.Decide(Even);
        Assert.Equal(ScriptedPairingSource.ScriptSource, decision.Source);
        Assert.Equal([(0, 1), (3, 4)], decision.Canonical);
        PairingDecision again = scripted.Decide(Even);
        Assert.Equal(decision.Canonical, again.Canonical);   // 不抽随机数：每次相同
        Assert.Equal((decision.Waiter, decision.Source), (again.Waiter, again.Source));
    }
}
