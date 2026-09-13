using Siege.Core.Board;

namespace Siege.Core.Relics;

/// <summary>信物控制的四态（设计文档 §7.3 + 裁决记录 5）。</summary>
public enum RelicControlKind
{
    /// <summary>无人控制：该格为空且无任何覆盖。</summary>
    Uncontrolled,

    /// <summary>争议：该格为空且被多名玩家覆盖，不向任何玩家提供效果。</summary>
    Contested,

    /// <summary>由参赛中的玩家控制（直接占据或唯一覆盖），效果进入其下一次快照。</summary>
    Controlled,

    /// <summary>封锁：由已弃赛 / 已出局玩家的遗留棋子控制。没人在用它，但别人也拿不到。</summary>
    Blocked,
}

/// <summary>信物控制状态：四态 + 持有者。<see cref="Holder"/> 只在 <see cref="RelicControlKind.Controlled"/> 与 <see cref="RelicControlKind.Blocked"/> 时非空。</summary>
public readonly record struct RelicControl(RelicControlKind Kind, PlayerId? Holder)
{
    public static readonly RelicControl Uncontrolled = new(RelicControlKind.Uncontrolled, null);

    public static readonly RelicControl Contested = new(RelicControlKind.Contested, null);

    /// <summary>该信物的效果是否应进入 <paramref name="player"/> 的快照：只有「参赛中且控制」才算。</summary>
    public bool GrantsEffectTo(PlayerId player) => Kind == RelicControlKind.Controlled && Holder == player;

    public override string ToString() => Holder is { } h ? $"{Kind}({h})" : Kind.ToString();
}

/// <summary>
/// 信物的公开状态（供 add-tactical-ui / add-heuristic-ai）。未揭示时 <see cref="Content"/> 为 <c>null</c>——
/// 公开视图里根本不存在未揭示信物的内容字段，不是靠约定不去读。
/// </summary>
public sealed record RelicPublicState(Coord Coord, RelicCellSpec Spec, bool IsRevealed, RelicContent? Content, int? RevealedInMajorRound, RelicControl Control);

/// <summary>揭示事件（设计文档 §17 要求记录揭示时间）。</summary>
public sealed record RelicRevealEvent(Coord Coord, RelicContent Content, int MajorRound)
{
    public override string ToString() => $"R{MajorRound} {Coord.ToNotation()} {Content}";
}

/// <summary>遥测：整局出现过的最高部署上限及其首次出现的大回合（设计文档 §16 的 5–8 目标区间回归）。</summary>
public sealed record DeployLimitPeak(int DeployLimit, int MajorRound, PlayerId Player);
