using System.Collections.Immutable;

namespace Siege.Core.Relics;

/// <summary>一条已揭示记录：信物格（围棋记法）与揭示发生的大回合。内容不存——由种子重算。</summary>
public sealed record RevealedRelic(string Coord, int MajorRound);

/// <summary><see cref="RelicLedger.ExportState"/> 的输出 / 存档恢复（<c>MatchFlow</c> 重放链接之后写回账本）的输入。</summary>
public sealed record RelicLedgerState(ImmutableArray<RevealedRelic> Revealed, DeployLimitPeak? DeployLimitPeak);
