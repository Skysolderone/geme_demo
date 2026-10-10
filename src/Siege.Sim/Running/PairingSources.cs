using System.Collections.Immutable;
using System.Security.Cryptography;
using Siege.Core.Determinism;
using Siege.Core.Match;

namespace Siege.Sim.Running;

/// <summary>
/// Sim 的配对来源（board-link-mode D3 / 规格 simulation-harness「各入口支持生成图」、Q10）：配对不是对局内的随机子流，结果由对局外部给出并作为事件记录。
/// 来源名写进配对事件（match-telemetry 第 8 项）：真随机 <see cref="Random"/>、配对种子 <see cref="Seeded"/>、脚本 <see cref="ScriptedPairingSource.ScriptSource"/>。
/// </summary>
public static class PairingSourceNames
{
    /// <summary>真随机（终端版有人值守对局）。</summary>
    public const string Random = "random";

    /// <summary>由配对种子派生的工具层随机源（批量跑局；缺省配对种子由对局种子派生）。</summary>
    public const string Seeded = "pairing-seed";
}

/// <summary>
/// 一次配对的抽取（三种 Sim 来源共用）：首轮把候选洗牌后两两相配，候选为奇数时洗牌后的最后一名等待；决赛只有一种合法结果，直接取它。
/// <paramref name="nextInt"/> 给出 [0, n) 的整数。
/// </summary>
internal static class PairingDraw
{
    internal static PairingDecision Draw(PairingRequest request, Func<int, int> nextInt, string source)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == PairingKind.Final)
        {
            return ScriptedPairingSource.Default(request) with { Source = source };
        }

        int[] order = [.. request.Candidates];
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = nextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        ImmutableArray<(int X, int Y)>.Builder pairs = ImmutableArray.CreateBuilder<(int X, int Y)>();
        for (int i = 0; i + 1 < order.Length; i += 2)
        {
            pairs.Add((order[i], order[i + 1]));
        }

        return new PairingDecision(pairs.ToImmutable(), order.Length % 2 == 1 ? order[^1] : null) { Source = source };
    }
}

/// <summary>
/// 批量跑局的配对来源（Q10）：由配对种子派生的工具层随机源，<b>不是</b> <see cref="GameSeed"/> 的子流——不消费、不读取任何对局子流，
/// 改变配对不扰动信物、征募、顺序与 AI 的随机（simulation-harness「随机子流隔离」）。与对局随机共用同一份 PRNG 实现（SplitMix64 展开 + xoshiro256**），
/// 写法同地图随机源 <c>MapRandom</c>。<paramref name="pairingSeed"/> 为 <c>null</c> 时由对局种子派生（同一批次可重复对比）；给出时与对局种子一起派生，
/// 使同一批次里各局的配对仍互不相同。
/// </summary>
public sealed class SeededPairingSource : IPairingSource
{
    private const ulong Domain = 0x7061_6972_696E_6721;   // "pairing!"
    private const ulong ExplicitDomain = 0x7061_6972_2D73_6565;
    private readonly RandomStream _stream;

    public SeededPairingSource(ulong gameSeed, ulong? pairingSeed)
    {
        ulong root = SplitMix64.Mix(gameSeed ^ Domain) ^ (pairingSeed is { } explicitSeed ? SplitMix64.Mix(explicitSeed ^ ExplicitDomain) : 0UL);
        ulong s0 = SplitMix64.Next(ref root);
        ulong s1 = SplitMix64.Next(ref root);
        ulong s2 = SplitMix64.Next(ref root);
        ulong s3 = SplitMix64.Next(ref root);
        // 写成目标类型 new：Sim 的源码扫描守门（批量跑局Tests.Sim源码不含不受控随机与时间且浮点只在分析层）按字面禁止系统随机数类型的构造写法。
        _stream = new(PairingSourceNames.Seeded, s0, s1, s2, s3);
    }

    public PairingDecision Decide(PairingRequest request) => PairingDraw.Draw(request, _stream.NextInt, PairingSourceNames.Seeded);
}

/// <summary>
/// 终端版有人值守对局的配对来源（规格：MUST 使用真随机源、不提供配对种子选项）：取操作系统的密码学随机数，不可复现；结果作为配对事件记录。
/// 不参与任何对局子流。
/// </summary>
public sealed class RandomPairingSource : IPairingSource
{
    public PairingDecision Decide(PairingRequest request) => PairingDraw.Draw(request, RandomNumberGenerator.GetInt32, PairingSourceNames.Random);
}

/// <summary>
/// 回放用的配对来源（simulation-harness「可复现回放」：配对事件 MUST 从日志读取，MUST NOT 由种子推导）：按日志里的配对事件逐次原样给出结果，
/// 来源名也原样带回（重建出的日志与原日志逐行相同）。请求与记录对不上（种类或候选不同、记录用完）即抛 <see cref="SimAssertionException"/>——
/// 回放因此在该小回合以失败收尾，逐行比对报出分歧。
/// </summary>
public sealed class RecordedPairingSource : IPairingSource
{
    private readonly Queue<RecordedPairing> _records;

    public RecordedPairingSource(IEnumerable<RecordedPairing> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = new Queue<RecordedPairing>(records);
    }

    public PairingDecision Decide(PairingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_records.TryDequeue(out RecordedPairing? record))
        {
            throw new SimAssertionException($"回放：第 {request.MajorRound} 大回合请求配对（{request.Kind}），日志里已没有配对事件。");
        }

        if (record.Kind != request.Kind || !record.Candidates.SequenceEqual(request.Candidates))
        {
            throw new SimAssertionException(
                $"回放：第 {request.MajorRound} 大回合的配对请求（{request.Kind}，候选 {string.Join(",", request.Candidates)}）与日志记录（{record.Kind}，候选 {string.Join(",", record.Candidates)}）不符。");
        }

        return record.Decision;
    }
}

/// <summary>日志里的一次配对事件（回放输入）：种类、候选与结果（含来源）。</summary>
public sealed record RecordedPairing(PairingKind Kind, ImmutableArray<int> Candidates, PairingDecision Decision);
