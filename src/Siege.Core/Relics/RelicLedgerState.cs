using System.Collections.Immutable;

namespace Siege.Core.Relics;

/// <summary>一条已揭示记录：信物格（围棋记法）与揭示发生的大回合。内容不存——由种子重算。</summary>
public sealed record RevealedRelic(string Coord, int MajorRound);

/// <summary><see cref="RelicLedger.ExportState"/> 的输出 / <see cref="RelicLedger.Restore"/> 的输入。</summary>
public sealed record RelicLedgerState(ImmutableArray<RevealedRelic> Revealed, DeployLimitPeak? DeployLimitPeak);
