using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Tests;

/// <summary>信物层测试的公共夹具：手工指定内容的信物账本、把账本接到结算驱动器第 4 / 5 步的钩子。</summary>
internal static class RelicFixtures
{
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);

    internal static readonly GameSeed Seed = new(20260913);

    internal static RelicContent Command(int magnitude = 1) => new(RelicType.Command, magnitude);

    internal static RelicContent Depot(int magnitude = 1) => new(RelicType.Depot, magnitude);

    internal static RelicContent Prospecting(int magnitude = 1) => new(RelicType.Prospecting, magnitude);

    internal static RelicContent Conscription(int magnitude = 1) => new(RelicType.Conscription, magnitude);

    internal static RelicContent Vanguard(int magnitude = 1) => new(RelicType.Vanguard, magnitude);

    internal static RelicContent Emblem(PieceType piece, int count = 1) => new(RelicType.SchoolEmblem, count, piece);

    /// <summary>
    /// 一张 9×9 合成盘面 + 手工指定内容的信物账本。信物格全部标为公共区标准档（内容既已手工指定，分区只影响记录）。
    /// </summary>
    internal static (GameBoard Board, RelicLedger Ledger) Scene(params (string Cell, RelicContent Content)[] relics)
    {
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        MapData map = TestMaps.Synthetic(
            size: 9, maxPlayers: 4,
            relics: relics.Select(r => KeyValuePair.Create(TestMaps.At(r.Cell), spec)));
        GameBoard board = GameBoard.LoadUnvalidated(map);
        ImmutableArray<RelicPlacement> placements = [.. relics.Select(r => new RelicPlacement(TestMaps.At(r.Cell), r.Content, spec)).OrderBy(p => p.Coord)];
        var record = new RelicGenerationRecord(Seed, map.Id, placements, Converged: true, Rerolls: 0);
        return (board, new RelicLedger(record));
    }

    internal static IReadOnlyDictionary<PlayerId, PlayerStatus> Roster(params (PlayerId Player, PlayerStatus Status)[] entries) =>
        entries.ToDictionary(e => e.Player, e => e.Status);

    /// <summary>全员参赛的名册。</summary>
    internal static IReadOnlyDictionary<PlayerId, PlayerStatus> AllActive(params PlayerId[] players) =>
        players.ToDictionary(p => p, _ => PlayerStatus.Active);

    /// <summary>一次「结算」：先第 4 步揭示，再第 5 步重算控制。盘面已由调用方改好。</summary>
    internal static ImmutableArray<RelicRevealEvent> Settle(this RelicLedger ledger, GameBoard board, int majorRound, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster = null)
    {
        ImmutableArray<RelicRevealEvent> revealed = ledger.Reveal(board, majorRound);
        if (roster is null)
        {
            ledger.RecalculateControl(board);
        }
        else
        {
            ledger.RecalculateControl(board, roster);
        }

        return revealed;
    }

    /// <summary>四人基准图上、种子驱动的随机盘面：每格 1/3 概率落子，所有者均匀。用固定种子的 <see cref="RandomStream"/>，不用 <c>Random.Shared</c>。</summary>
    internal static GameBoard RandomBoard(GameBoard empty, RandomStream rng, PlayerId[] players)
    {
        foreach (Coord c in empty.AllCoords())
        {
            if (empty[c].Terrain == Terrain.Playable && rng.NextInt(3) == 0)
            {
                empty.Place(c, players[rng.NextInt(players.Length)], PieceType.Basic);
            }
        }

        return empty;
    }
}

/// <summary>
/// 把 <see cref="RelicLedger"/> 接到结算顺序第 4 步（揭示）与第 5 步（重算控制）。正式接线属于 add-match-flow；
/// 这里只演示「回调时盘面已提完子，账本基于提子后的覆盖关系揭示」。
/// </summary>
internal sealed class RelicHooks : ISettlementHooks
{
    private readonly RelicLedger _ledger;
    private readonly IReadOnlyDictionary<PlayerId, PlayerStatus>? _roster;

    internal RelicHooks(RelicLedger ledger, IReadOnlyDictionary<PlayerId, PlayerStatus>? roster = null)
    {
        _ledger = ledger;
        _roster = roster;
    }

    internal int MajorRound { get; set; } = 1;

    internal List<string> Steps { get; } = [];

    internal List<RelicRevealEvent> Revealed { get; } = [];

    /// <summary>第 4 步回调时的探针：让测试断言「揭示时看到的盘面已完成提子」。</summary>
    internal Action<SettlementContext>? RevealProbe { get; init; }

    public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed) => Steps.Add("DeductHand");

    public void OnRevealRelics(SettlementContext context)
    {
        Steps.Add("OnRevealRelics");
        RevealProbe?.Invoke(context);
        Revealed.AddRange(_ledger.Reveal(context.Board, MajorRound));
    }

    public void OnRecalculatePower(SettlementContext context)
    {
        Steps.Add("OnRecalculatePower");
        if (_roster is null)
        {
            _ledger.RecalculateControl(context.Board);
        }
        else
        {
            _ledger.RecalculateControl(context.Board, _roster);
        }
    }

    public void OnCheckEndConditions(SettlementContext context) => Steps.Add("OnCheckEndConditions");

    public void OnPass(PlayerId player) => Steps.Add("OnPass");
}
