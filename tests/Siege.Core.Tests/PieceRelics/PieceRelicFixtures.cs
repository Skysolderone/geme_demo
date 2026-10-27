using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>
/// 挂在棋子上的信物（relic-redesign 加法段）的测试夹具。只用规则层小盘面（<see cref="TestMaps"/>），不建局；
/// 档案测试一律用临时目录，MUST NOT 读写真实用户目录。
/// </summary>
internal static class PieceRelicFixtures
{
    /// <summary>在 <paramref name="notation"/> 放一件信物（格上有子即挂在该子上）。</summary>
    internal static GameBoard Attach(this GameBoard board, string notation, RelicToken token)
    {
        board.AttachRelic(TestMaps.At(notation), token);
        return board;
    }

    /// <summary>地图刷出的一件信物。</summary>
    internal static RelicToken Map(int id, RelicKind kind) => RelicToken.Spawned(id, kind);

    /// <summary><paramref name="origin"/> 带入的一件信物。</summary>
    internal static RelicToken Carried(int id, RelicKind kind, PlayerId origin, bool insured = false) => RelicToken.Carried(id, kind, origin, insured);

    /// <summary>无名册、计分规则 v1（不计阵型）下 <paramref name="player"/> 唯一一条棋串的明细。</summary>
    internal static GroupPower OnlyGroup(GameBoard board, PlayerId player) =>
        Assert.Single(PowerCalculator.Compute(board).Of(player).Groups);

    /// <summary>计分规则 v2（计阵型）下 <paramref name="player"/> 唯一一条棋串的明细；名册取盘面上出现的全部玩家、一律参赛中。</summary>
    internal static GroupPower OnlyGroupV2(GameBoard board, PlayerId player)
    {
        var roster = board.AllGroups().Select(g => g.Owner).Distinct().ToDictionary(p => p, _ => PlayerStatus.Active);
        PowerSnapshot snapshot = PowerCalculator.Compute(board, roster, ImmutableDictionary<Coord, RelicType>.Empty, ScoringVersion.V2);
        return Assert.Single(snapshot.Of(player).Groups);
    }

    /// <summary>两格的切比雪夫距离（测试内独立算式，不调用被测代码）。</summary>
    internal static int Chebyshev(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>
/// 档案 v2 测试的临时目录：每个实例一个独立子目录，释放时整体删除。
/// </summary>
internal sealed class TempRelicProfile : IDisposable
{
    internal TempRelicProfile()
    {
        Directory = Path.Combine(Path.GetTempPath(), "siege-relic-profile-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        FilePath = Path.Combine(Directory, "profile.json");
    }

    internal string Directory { get; }

    internal string FilePath { get; }

    /// <summary>固定时钟：2026-10-27 09:30:00（只用于损坏备份的文件名）。</summary>
    internal static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 10, 27, 9, 30, 0, TimeSpan.FromHours(8));

    internal RelicProfileStore Store() => new(FilePath, Clock);

    /// <summary>直接写档案文件（测试构造初始状态，不经被测代码）。</summary>
    internal void Write(string text) => File.WriteAllText(FilePath, text);

    internal string Read() => File.ReadAllText(FilePath);

    /// <summary>目录下的全部文件名（排序）。</summary>
    internal string[] Files() => [.. System.IO.Directory.GetFiles(Directory).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];

    /// <summary>按 design.md D7 的格式手写一份 v2 档案 JSON（测试内独立写法，不调用被测的写出）。</summary>
    internal static string JsonV2(int points, int drum = 0, int command = 0, int watchtower = 0, int chameleon = 0, int banner = 0, string inFlight = "null", int version = 2) =>
        $"{{\"version\":{version},\"points\":{points},\"inventory\":{{\"WarDrum\":{drum},\"Command\":{command},\"Watchtower\":{watchtower},\"Chameleon\":{chameleon},\"Banner\":{banner}}},\"inFlight\":{inFlight}}}";

    /// <summary>按旧补给档案（v1）格式手写一份 JSON。</summary>
    internal static string JsonV1(int points, int spare, int draft, int commission, string inFlight = "null") =>
        $"{{\"version\":1,\"points\":{points},\"inventory\":{{\"SpareStone\":{spare},\"DraftLot\":{draft},\"Commission\":{commission}}},\"inFlight\":{inFlight}}}";

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
