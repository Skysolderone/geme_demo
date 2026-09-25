using System.Collections.Immutable;

namespace Siege.Core.Board;

/// <summary>
/// 对局内容集（more-pieces-relics D8，match-setup「对局内容集」）：决定本局有哪些棋子类型与信物类型。开局固定、始终公开，入存档、日志首部与批次配置。
/// </summary>
/// <remarks>
/// <para><see cref="V1"/> = 原六种棋子与原六类信物，棋池、信物生成表、徽记绑定集合与引入新内容之前完全相同；<see cref="V2"/> = 十种棋子与十类信物。</para>
/// <para>新局缺省 <see cref="V2"/>（<see cref="ContentSets.Default"/>）；恢复不含该字段的旧存档、回放不含该字段的旧日志一律按 <see cref="V1"/>
/// （<see cref="ContentSets.Legacy"/>），同一种子才能重建出保存时的信物分布与征募序列。计分、控制、改造规则两者共用。</para>
/// <para>显式编号 1 / 2：0 不是合法内容集，未初始化的值会被 <see cref="ContentSets.PieceTypesOf"/> 响亮拒绝。</para>
/// </remarks>
public enum ContentSet
{
    /// <summary>原六种棋子、原六类信物（引入旗手子等四种棋子之前的内容）。</summary>
    V1 = 1,

    /// <summary>十种棋子、十类信物。</summary>
    V2 = 2,
}

/// <summary>内容集的唯一类型清单：征募棋池、日志的棋子计数、徽记绑定集合都从这里取"本内容集有哪些棋子类型"，不各自列举。</summary>
public static class ContentSets
{
    /// <summary>新开对局的缺省内容集。</summary>
    public const ContentSet Default = ContentSet.V2;

    /// <summary>旧存档 / 旧日志缺内容集字段时的回填值：那些对局只可能是原六 + 六。</summary>
    public const ContentSet Legacy = ContentSet.V1;

    private static readonly ImmutableArray<PieceType> V1Pieces =
    [
        PieceType.Basic,
        PieceType.Fortress,
        PieceType.Line,
        PieceType.Multiplier,
        PieceType.Synergy,
        PieceType.Artisan,
    ];

    private static readonly ImmutableArray<PieceType> V2Pieces =
    [
        .. V1Pieces,
        PieceType.Bannerman,
        PieceType.Chain,
        PieceType.Sentry,
        PieceType.Boundary,
    ];

    /// <summary>
    /// 该内容集的棋子类型，按固定类型次序（= 枚举次序，D9）。v1 的清单是 v2 清单的前缀——征募表的下标因此在两个内容集之间通用。
    /// 显式列出，不用 <c>Enum.GetValues</c>：枚举再追加值也不会悄悄改变 v1 的内容。
    /// </summary>
    public static ImmutableArray<PieceType> PieceTypesOf(ContentSet set) => set switch
    {
        ContentSet.V1 => V1Pieces,
        ContentSet.V2 => V2Pieces,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "未知对局内容集。"),
    };

    /// <summary>内容集须为已定义的值（v1 / v2）。</summary>
    public static ContentSet RequireValid(ContentSet set)
    {
        _ = PieceTypesOf(set);
        return set;
    }
}
