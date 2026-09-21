using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Match;

/// <summary>
/// 弃赛时刻的完整快照（裁决记录 5）：盘面序列化、手牌两段账、效果快照、控制信物列表与势力值。
/// 粒度宁可过细——将来带入带出系统裁剪容易，补记困难。
/// </summary>
public sealed record ResignationSnapshot(
    PlayerId Player,
    int MajorRound,
    string Board,
    HandPrivateView Hand,
    EffectSnapshot Effects,
    ImmutableArray<Coord> ControlledRelics,
    BigInteger Power);
