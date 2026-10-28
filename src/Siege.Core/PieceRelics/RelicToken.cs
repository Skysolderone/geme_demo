using Siege.Core.Board;

namespace Siege.Core.PieceRelics;

/// <summary>
/// 一件信物（design.md D1）：本局编号、种类、带入者与保险标记。信物放在盘面信物层上（<see cref="GameBoard.RelicAt"/>）——
/// 格上有子即挂在该子上、空格即地图信物；<b>不另存持有者</b>，持有者永远等于该格棋子的主人。
/// </summary>
/// <remarks>
/// <para><see cref="Id"/>：本局内唯一，按出现顺序递增（刷新与带入挂出都分配），用于结算归属与遥测。由调用方分配，盘面只校验唯一。</para>
/// <para><see cref="Origin"/>：带入者；地图刷出的为 <c>null</c>。<see cref="Insured"/> 只对带入件有意义——地图信物勾保险即抛出。</para>
/// </remarks>
public readonly record struct RelicToken
{
    /// <summary>构造一件信物。编号为负、或地图信物（无带入者）标了保险时抛 <see cref="ArgumentException"/>。</summary>
    public RelicToken(int id, RelicKind kind, PlayerId? origin, bool insured)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "信物编号不得为负。");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知信物种类。");
        }

        if (insured && origin is null)
        {
            throw new ArgumentException("地图刷出的信物没有带入者，不能是保险件。", nameof(insured));
        }

        Id = id;
        Kind = kind;
        Origin = origin;
        Insured = insured;
    }

    /// <summary>地图刷出的信物（无带入者、非保险）。</summary>
    public static RelicToken Spawned(int id, RelicKind kind) => new(id, kind, origin: null, insured: false);

    /// <summary>玩家 <paramref name="origin"/> 带入的信物。</summary>
    public static RelicToken Carried(int id, RelicKind kind, PlayerId origin, bool insured) => new(id, kind, origin, insured);

    /// <summary>本局内唯一的编号。</summary>
    public int Id { get; }

    /// <summary>种类。</summary>
    public RelicKind Kind { get; }

    /// <summary>带入者；地图刷出的为 <c>null</c>。</summary>
    public PlayerId? Origin { get; }

    /// <summary>是否保险件（只有带入件可能为 <c>true</c>）。</summary>
    public bool Insured { get; }

    /// <summary>是否带入件。</summary>
    public bool IsCarried => Origin is not null;

    public override string ToString() =>
        $"#{Id} {Kind.DisplayName()}{(Origin is { } o ? $"（{o} 带入{(Insured ? "，保险" : string.Empty)}）" : "（地图）")}";
}

/// <summary>盘面上的一件信物：所在格与信物本身。格上的棋子（若有）即持有者。</summary>
public readonly record struct BoardRelic(Coord Cell, RelicToken Token);
