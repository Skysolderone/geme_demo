using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Simul;

namespace Siege.Core.Tests.Simul;

/// <summary>
/// 新玩法（simul-a）的测试夹具：手写链接档图，n 块 8×8 出生座位（出生岛）一字排开，相邻两块之间夹一块 3×8 的公共平台、首尾相接，
/// 整张图是一个战场。出生岛 0 占 C3–K10（x 2–9、y 2–9），公共平台 0 占 L3–N10（x 10–12），出生岛 1 从 O3 起（x 13–20）。
/// 出生岛 0 的核心区是 E5–G7。图外一律障碍，与盘边同为墙。
/// </summary>
internal static class SimulFixtures
{
    internal static readonly PlayerId P0 = new(0);
    internal static readonly PlayerId P1 = new(1);
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);
    internal static readonly PlayerId[] Four = [P0, P1, P2, P3];

    internal static readonly GameSeed Seed = new(0x51A1_0001);

    internal static PlayerId[] PlayersOf(int n) => [.. Four.Take(n)];

    /// <summary>夹具图：<paramref name="players"/> 块出生岛 + <paramref name="players"/> − 1 块公共平台，首尾相接。</summary>
    internal static MapData Map(int players, TerrainData? terrain = null)
    {
        var plates = new List<BoardPlate>();
        for (int i = 0; i < players; i++)
        {
            plates.Add(new BoardPlate(new Coord(2 + (11 * i), 2), 8, 8, BoardPlateKind.Birth, BoardTheme.River));
        }

        for (int i = 0; i < players - 1; i++)
        {
            plates.Add(new BoardPlate(new Coord(10 + (11 * i), 2), 3, 8, BoardPlateKind.Public));
        }

        int width = plates.Max(b => b.Origin.X + b.Width) + 2;
        int height = plates.Max(b => b.Origin.Y + b.Height) + 2;
        ImmutableHashSet<Coord> playable = [.. plates.SelectMany(b => b.Cells())];
        return new MapData
        {
            Id = $"link:90011:{players}",
            Width = width,
            Height = height,
            MaxPlayers = players,
            Boards = [.. plates],
            Obstacles = [.. TestMaps.Rect(0, 0, width, height).Where(c => !playable.Contains(c))],
            TerrainData = terrain ?? TerrainData.Flat,
            BirthZones = [.. plates.Where(b => b.Kind == BoardPlateKind.Birth).Select(b => (ImmutableHashSet<Coord>)[.. b.CoreCells()])],
            ChokePoints = [],
            CentralEntrance = plates[0].CoreOrigin,
        };
    }

    /// <summary>只有一块林地 <paramref name="forest"/> 的地形。</summary>
    internal static TerrainData Forest(params string[] forest) => new(
        ImmutableDictionary<Coord, int>.Empty,
        forest.ToImmutableDictionary(Coord.Parse, _ => Surface.Forest),
        [],
        []);

    /// <summary>
    /// 建局、按 <paramref name="setup"/> 摆子（插旗阶段，绕过规则），全员锁在出生岛 <paramref name="zone"/>（缺省 0，同岛共享），开始第 1 回合；
    /// 再按需改回合号与顺位。每人手牌 20 个普通单位，另加 <paramref name="extra"/>。
    /// </summary>
    internal static SimulFlow Started(
        int players,
        Action<SimulFlow>? setup = null,
        int round = 1,
        PlayerId[]? order = null,
        TerrainData? terrain = null,
        GameSeed? seed = null,
        SimulOptions? options = null,
        int zone = 0,
        params (PieceType Type, int Count)[] extra)
    {
        PlayerId[] all = PlayersOf(players);
        SimulFlow flow = SimulFlow.CreateUnvalidated(Map(players, terrain), seed ?? Seed, all, options);
        foreach (PlayerId p in all)
        {
            flow.Debug.SeedHand(p, [(PieceType.Basic, 20), .. extra]);
        }

        setup?.Invoke(flow);
        flow.Debug.LockAs(all.Select(p => (p, zone)));
        if (round != 1 || order is not null)
        {
            flow.Debug.SetRound(round, order);
        }

        return flow;
    }

    /// <summary>暂放并提交，断言提交成功。</summary>
    internal static void Submit(this SimulFlow flow, PlayerId player, params Placement[] placements)
    {
        SimulPrep prep = flow.PrepFor(player);
        foreach (Placement p in placements)
        {
            prep.Stage(p);
        }

        SimulBatchCheck check = prep.Submit();
        Assert.True(check.IsValid, $"{player} 的批次应当合法：{check.FirstReason}");
    }

    /// <summary>暂放普通单位并提交。</summary>
    internal static void Submit(this SimulFlow flow, PlayerId player, params string[] cells) =>
        flow.Submit(player, [.. cells.Select(c => new Placement(Coord.Parse(c), PieceType.Basic))]);

    /// <summary>其余参赛玩家都提交 0 枚。</summary>
    internal static void SubmitRestEmpty(this SimulFlow flow)
    {
        foreach (PlayerId p in flow.ActivePlayers.Where(p => !flow.IsSubmitted(p)))
        {
            Assert.True(flow.PrepFor(p).Submit().IsValid);
        }
    }

    /// <summary>暂放（不提交）后预检。</summary>
    internal static SimulBatchCheck CheckOf(this SimulFlow flow, PlayerId player, params string[] cells)
    {
        SimulPrep prep = flow.PrepFor(player);
        prep.Clear();
        foreach (string c in cells)
        {
            prep.Stage(Coord.Parse(c), PieceType.Basic);
        }

        return prep.Check();
    }

    internal static Placement Unit(string cell, PieceType type = PieceType.Basic, TerrainEdit? edit = null) => new(Coord.Parse(cell), type, edit);

    internal static PlayerId? OwnerAt(this SimulFlow flow, string cell) => flow.Board[Coord.Parse(cell)].Occupant?.Owner;

    internal static int HandCount(this SimulFlow flow, PlayerId player) => flow.Hands.PrivateViewOf(player).TotalCount;
}
