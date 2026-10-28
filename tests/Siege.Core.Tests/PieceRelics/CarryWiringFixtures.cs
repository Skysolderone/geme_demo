using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.PieceRelics;

namespace Siege.Core.Tests.PieceRelics;

/// <summary>带入接线（relic-redesign 接线段 W2）测试的共用夹具：清单、建局选项、手牌文本。</summary>
internal static class CarryWiringFixtures
{
    /// <summary>一份带入清单：逐件种类与保险。</summary>
    internal static RelicLoadout Loadout(params (RelicKind Kind, bool Insured)[] items) =>
        new([.. items.Select(i => new CarriedRelic(i.Kind, i.Insured))]);

    /// <summary>开启带入带出、指定各玩家带入清单的立即插旗选项。</summary>
    internal static MatchOptions On(params (int Player, RelicLoadout Loadout)[] carries) => MatchOptions.Immediate with
    {
        CarryInOut = true,
        CarryIns = carries.ToImmutableSortedDictionary(c => new PlayerId(c.Player), c => c.Loadout),
    };

    /// <summary>手牌的确定性文本：按类型次序 "类型×回合前基数+本轮新增"，如 "Basic×5+0"。</summary>
    internal static string HandText(MatchFlow match, PlayerId player) =>
        string.Join(" ", match.Hands.Debug.PrivateViewOf(player).Entries.Select(kv => $"{kv.Key}×{kv.Value.Carried}+{kv.Value.Gained}"));
}

/// <summary>真实用户目录的只读快照：用来断言某段代码没有碰缺省档案路径所在目录（D8：自动化 / 关闭带入时不读写档案）。</summary>
internal static class RealProfileDir
{
    /// <summary>缺省档案所在目录（<c>%APPDATA%\Siege</c>）的状态：不存在记为 "absent"，存在则列出文件名、长度与修改时间。</summary>
    internal static string State()
    {
        string dir = Path.GetDirectoryName(RelicProfileStore.DefaultPath())!;
        return System.IO.Directory.Exists(dir)
            ? string.Join("|", System.IO.Directory.GetFiles(dir).Order(StringComparer.Ordinal)
                .Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"))
            : "absent";
    }
}
