using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Match;

/// <summary>配对事件的种类（battlefield「配对事件由对局外部提供」）。序列化名称稳定。</summary>
public enum PairingKind
{
    /// <summary>首轮配对：第 3 大回合结束时，锁定时旗数恰为 1 的出生棋盘上仍在局者的战场两两配对。</summary>
    FirstRound,

    /// <summary>决赛链接：全部多人战场已结算且在局玩家 ≥ 2 时，把全部在局玩家所在的战场链接为一个（合法结果只有一种）。</summary>
    Final,
}

/// <summary>
/// 交给配对来源的请求：种类与候选战场标识（升序）。候选为奇数时配对结果须恰含一名等待者。
/// </summary>
public sealed record PairingRequest(PairingKind Kind, ImmutableArray<int> Candidates, int MajorRound);

/// <summary>
/// 一次配对事件的结果（对局外部给出）：若干对战场标识与至多一名等待者。对的列出顺序与对内两个标识的先后都无关紧要——
/// 执行时一律按规范次序（每对内小者在前；"含最小出生棋盘编号的一对先"，裁决 R2）。
/// </summary>
public sealed record PairingDecision(ImmutableArray<(int X, int Y)> Pairs, int? Waiter)
{
    /// <summary>
    /// 结果的来源（match-telemetry 第 8 项：真随机 / 配对种子 / 脚本），只作记录，不参与校验。外部未注明时为 <see cref="ExternalSource"/>。
    /// </summary>
    public string Source { get; init; } = ExternalSource;

    /// <summary>未注明来源的外部提交。</summary>
    public const string ExternalSource = "external";

    /// <summary>规范次序的配对：对内小者在前，各对按小者升序。</summary>
    public ImmutableArray<(int X, int Y)> Canonical =>
        [.. Pairs.Select(p => (Math.Min(p.X, p.Y), Math.Max(p.X, p.Y))).OrderBy(p => p.Item1).ThenBy(p => p.Item2)];

    public override string ToString() =>
        $"{string.Join(" ", Canonical.Select(p => $"({BirthZoneLabel.Of(p.X)},{BirthZoneLabel.Of(p.Y)})"))}" +
        (Waiter is { } w ? $" 等待 {BirthZoneLabel.Of(w)}" : string.Empty);
}

/// <summary>
/// 配对来源（board-link-mode D3 / 实现裁决 B3）：Core 自身 MUST NOT 为配对或等待者抽随机数，配对结果来自对局外部。
/// <see cref="MatchFlow"/> 在大回合结束进入待配对 / 待决赛链接时调用挂接的来源（<see cref="MatchFlow.PairingSource"/>），
/// 再按「配对事件由对局外部提供」的全部规则校验结果；不合法即抛错并停在待配对状态。真随机实现在 Sim（终端）与 Presentation（图形版），不在 Core；抽取写法共用 <see cref="PairingDraw"/>。
/// </summary>
public interface IPairingSource
{
    /// <summary>给出一次配对事件的结果。</summary>
    PairingDecision Decide(PairingRequest request);
}

/// <summary>
/// 配对结果的来源名（match-telemetry 第 8 项，写进配对事件）：真随机 <see cref="Random"/>（终端 / 图形版有人值守）、配对种子 <see cref="Seeded"/>（批量跑局）、
/// 脚本 <see cref="ScriptedPairingSource.ScriptSource"/>（测试与无人值守自检）。段 D 从 Sim 移入 Core，供 Presentation 的真随机来源共用（不得引用 Sim）。
/// </summary>
public static class PairingSourceNames
{
    /// <summary>真随机（有人值守对局）。</summary>
    public const string Random = "random";

    /// <summary>由配对种子派生的工具层随机源（批量跑局；缺省配对种子由对局种子派生）。</summary>
    public const string Seeded = "pairing-seed";
}

/// <summary>
/// 一次配对的抽取（各随机来源共用的<b>唯一</b>写法；纯函数，随机数由调用方的 <c>nextInt</c> 给出）：首轮把候选洗牌后两两相配，候选为奇数时洗牌后的最后一名等待；
/// 决赛只有一种合法结果，直接取它。Core 自身不产生随机数（board-link-mode D3）——真随机 / 配对种子的来源分别在 Presentation（图形版）与 Sim（终端、批量）。
/// </summary>
public static class PairingDraw
{
    /// <summary><paramref name="nextInt"/> 给出 <c>[0, n)</c> 的整数；<paramref name="source"/> 写进结果的 <see cref="PairingDecision.Source"/>。</summary>
    public static PairingDecision Draw(PairingRequest request, Func<int, int> nextInt, string source)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nextInt);
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
/// 脚本固定配对（实现裁决 B3）：供测试与无人值守自检。按给定脚本逐次给出结果；脚本用完（或未给脚本）时走确定性缺省——
/// 候选按编号升序两两相配，奇数时编号最大者等待。决赛的缺省即唯一合法结果。不抽任何随机数。
/// </summary>
public sealed class ScriptedPairingSource : IPairingSource
{
    private readonly Queue<PairingDecision> _script;

    public ScriptedPairingSource(params PairingDecision[] script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _script = new Queue<PairingDecision>(script);
    }

    /// <summary>本来源给出的结果在记录里的来源名。</summary>
    public const string ScriptSource = "script";

    /// <summary>已收到的请求，按顺序（测试读取）。</summary>
    public List<PairingRequest> Requests { get; } = [];

    public PairingDecision Decide(PairingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        return (_script.Count > 0 ? _script.Dequeue() : Default(request)) with { Source = ScriptSource };
    }

    /// <summary>确定性缺省：按编号升序两两相配，奇数时最大者等待。</summary>
    public static PairingDecision Default(PairingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int[] sorted = [.. request.Candidates.Order()];
        if (request.Kind == PairingKind.Final)
        {
            return new PairingDecision([.. sorted.Skip(1).Select(c => (sorted[0], c))], null);
        }

        ImmutableArray<(int X, int Y)>.Builder pairs = ImmutableArray.CreateBuilder<(int X, int Y)>();
        for (int i = 0; i + 1 < sorted.Length; i += 2)
        {
            pairs.Add((sorted[i], sorted[i + 1]));
        }

        return new PairingDecision(pairs.ToImmutable(), sorted.Length % 2 == 1 ? sorted[^1] : null);
    }
}

/// <summary>
/// 一次被接受的配对事件（match-telemetry 第 8 项；随存档往返）。<paramref name="Sequence"/> 从 1 起；<paramref name="MajorRound"/> 为事件发生时（已推进到的）大回合；
/// <paramref name="Pairs"/> 为规范次序；<paramref name="Source"/> 为结果来源（脚本 / 外部 / 段 C 的真随机与配对种子）。
/// </summary>
public sealed record PairingRecord(
    int Sequence, PairingKind Kind, int MajorRound, ImmutableArray<int> Candidates, ImmutableArray<(int X, int Y)> Pairs, int? Waiter, string Source = PairingDecision.ExternalSource);

/// <summary>
/// 一次执行完的链接（match-telemetry 第 9 项；存档只存 (X, Y, 所属配对) 并在恢复时重放核对）：链接序号（从 1 起）、所属配对事件序号、两个战场（X &lt; Y）、
/// 链接后的地图版本号、是否退化为直通道、生成器给出的几何摘要、新增的信物格（坐标字典序）。
/// </summary>
public sealed record LinkRecord(
    int Sequence, int PairingSequence, int X, int Y, int MapVersion, bool Degraded, string Summary, ImmutableArray<Coord> NewRelicCells)
{
    /// <summary>新增的公共棋盘（外接矩形即其原点与边长）；退化链接为 <c>null</c>。</summary>
    public BoardPlate? PublicBoard { get; init; }

    /// <summary>新增通道的格数。</summary>
    public int CorridorCells { get; init; }

    /// <summary>新增的通道（外接矩形；长度 = 较长边），按生成器给出的次序。退化链接为一条直通道。只作记录（遥测：通道长度，负责人裁决 R3 复核项），不入存档。</summary>
    public ImmutableArray<Board.Maps.LinkCorridor> Corridors { get; init; } = [];

    /// <summary>链接后（开局 + 链接、不含改造）地图的内容摘要（<see cref="MapFile.Digest"/>）。</summary>
    public string MapDigest { get; init; } = string.Empty;

    /// <summary>新增信物格及其内容（坐标字典序）。</summary>
    public ImmutableArray<Relics.RelicPlacement> NewRelics { get; init; } = [];
}
