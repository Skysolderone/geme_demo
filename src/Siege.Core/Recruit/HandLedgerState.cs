using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>某类型在小回合边界的持有数量（此时两段账的新增必为 0，只剩基数）。</summary>
public sealed record HandStockEntry(PieceType Type, int Count);

/// <summary>一名玩家在小回合边界的手牌状态。玩家用整数编号表示，便于直接写入存档。</summary>
public sealed record PlayerHandState(int Player, ImmutableArray<HandStockEntry> Hand, bool Resigned);

/// <summary>
/// <see cref="HandLedger.Export"/> 的输出 / <see cref="HandLedger.Restore"/> 的输入：全部玩家的手牌、弃赛标记、
/// <c>recruit</c> 子流消费次数与小回合序号。只含小回合边界上存在的状态，不含进行中的面板。
/// </summary>
public sealed record HandLedgerState(ImmutableArray<PlayerHandState> Players, long RecruitConsumed, int Sequence);
