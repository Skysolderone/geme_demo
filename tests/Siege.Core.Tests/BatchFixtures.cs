using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;

namespace Siege.Core.Tests;

/// <summary>批次层测试的公共夹具：上下文工厂、记录式钩子桩、劫争盘面。</summary>
internal static class BatchFixtures
{
    internal static readonly PlayerId P2 = new(2);

    /// <summary>默认库存：每种类型 10 枚，够任何用例；需要精确库存的用例自行传入。</summary>
    internal static IReadOnlyDictionary<PieceType, int> GenerousStock() =>
        Enum.GetValues<PieceType>().ToDictionary(t => t, _ => 10);

    internal static IReadOnlyDictionary<PieceType, int> Stock(params (PieceType Type, int Count)[] items) =>
        items.ToDictionary(i => i.Type, i => i.Count);

    internal static BatchContext Context(
        GameBoard board,
        PlayerId player,
        int limit = 3,
        IReadOnlyDictionary<PieceType, int>? stock = null,
        IReadOnlySet<Coord>? range = null) =>
        new()
        {
            Player = player,
            DeployLimit = limit,
            LegalRange = range ?? BatchContext.EntireBoard(board),
            Stock = stock ?? GenerousStock(),
        };

    internal static Placement P(string notation, PieceType type = PieceType.Basic) =>
        new(Coord.Parse(notation), type);

    internal static SettlementDriver Driver(GameBoard board, RecordingHooks? hooks = null, BoardHistory? history = null) =>
        new(board, history ?? new BoardHistory(), hooks ?? new RecordingHooks());

    /// <summary>
    /// 9×9 双劫盘面。劫 A：P0 墙 B4/C3/C5，P1 墙 D3/D5/E4，劫点 C4（P1 侧）与 D4（P0 侧）。
    /// 劫 B：P0 墙 F7/G6/G8，P1 墙 H6/H8/J7，劫点 G7（P1 侧）与 H7（P0 侧）。
    /// 初始由 <paramref name="holderA"/> / <paramref name="holderB"/> 各持一枚普通子在己方劫点上。
    /// "持劫"的一方棋子只有一口气（对面的劫点），对方提掉它即"提劫"。
    /// </summary>
    internal static GameBoard KoBoard(PlayerId holderA, PlayerId holderB)
    {
        GameBoard board = TestMaps.Blank(size: 9)
            .Place("B4", TestMaps.P0).Place("C3", TestMaps.P0).Place("C5", TestMaps.P0)
            .Place("D3", TestMaps.P1).Place("D5", TestMaps.P1).Place("E4", TestMaps.P1)
            .Place("F7", TestMaps.P0).Place("G6", TestMaps.P0).Place("G8", TestMaps.P0)
            .Place("H6", TestMaps.P1).Place("H8", TestMaps.P1).Place("J7", TestMaps.P1);
        board.Place(KoPoint('A', holderA), holderA, PieceType.Basic);
        board.Place(KoPoint('B', holderB), holderB, PieceType.Basic);
        return board;
    }

    /// <summary>某玩家在某劫中"提劫"时的落点（即该玩家一侧的劫点）。</summary>
    internal static Coord KoPoint(char ko, PlayerId player)
    {
        bool isP0 = player == TestMaps.P0;
        return ko switch
        {
            'A' => TestMaps.At(isP0 ? "D4" : "C4"),
            'B' => TestMaps.At(isP0 ? "H7" : "G7"),
            _ => throw new ArgumentOutOfRangeException(nameof(ko)),
        };
    }

    /// <summary>提劫：<paramref name="player"/> 在指定劫用指定类型落一子并确认。</summary>
    internal static SettlementOutcome TakeKo(SettlementDriver driver, PlayerId player, char ko, PieceType type, int limit = 3) =>
        driver.Confirm(Context(driver.Board, player, limit), [new Placement(KoPoint(ko, player), type)]);

    /// <summary>种子驱动的 Fisher-Yates 洗牌。不用 Random.Shared——回归必须可复现。</summary>
    internal static List<T> Shuffle<T>(IEnumerable<T> items, Random rng)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}

/// <summary>记录每次回调的步骤名与回调发生时的正式盘面快照，用于验证结算顺序与原子性。</summary>
internal sealed class RecordingHooks : ISettlementHooks
{
    internal List<(string Step, string Board)> Calls { get; } = [];

    internal List<(PlayerId Player, IReadOnlyDictionary<PieceType, int> Deployed)> Deductions { get; } = [];

    internal List<PlayerId> Passes { get; } = [];

    internal List<SettlementContext> Contexts { get; } = [];

    internal string[] Steps => Calls.Select(c => c.Step).ToArray();

    public void DeductHand(PlayerId player, IReadOnlyDictionary<PieceType, int> deployed)
    {
        Deductions.Add((player, deployed));
        Calls.Add(("DeductHand", Board?.Serialize() ?? string.Empty));
    }

    public void OnRevealRelics(SettlementContext context) => Record("OnRevealRelics", context);

    public void OnRecalculatePower(SettlementContext context) => Record("OnRecalculatePower", context);

    public void OnCheckEndConditions(SettlementContext context) => Record("OnCheckEndConditions", context);

    public void OnPass(PlayerId player)
    {
        Passes.Add(player);
        Calls.Add(("OnPass", Board?.Serialize() ?? string.Empty));
    }

    /// <summary>DeductHand 与 OnPass 不带盘面，桩需要知道正式盘面才能采样。</summary>
    internal GameBoard? Board { get; set; }

    private void Record(string step, SettlementContext context)
    {
        Contexts.Add(context);
        Calls.Add((step, context.Board.Serialize()));
    }
}
