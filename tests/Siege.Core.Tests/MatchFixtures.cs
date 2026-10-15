using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests;

/// <summary>旧模式（Legacy）9×9 流程夹具的残留建局助手（retire-legacy-rules：第 2 组随 (c) / (j) 方法一起删除）。</summary>
internal static class MatchFixtures
{
    // retire-legacy-rules 1.2：常量、9×9 地图数据与手工信物记录已迁到 TestMaps，AtRound / Stones / PlayTurn 等扩展迁到 LinkModeFixtures；
    // 本类只剩旧模式建局助手，供清单第 3 节 (c) / (j) 方法在第 2 组删除之前使用。
    internal static readonly PlayerId P0 = TestMaps.P0;
    internal static readonly PlayerId P1 = TestMaps.P1;
    internal static readonly PlayerId P2 = TestMaps.P2;
    internal static readonly PlayerId P3 = TestMaps.P3;
    internal static readonly PlayerId[] All = TestMaps.All;

    internal static readonly GameSeed Seed = TestMaps.Seed;

    /// <summary>2 人局（catch-up-recruit「2 人局」算例）：只插旗、各 50 枚普通子，摆到第 <paramref name="majorRound"/> 大回合、指定顺序。</summary>
    internal static MatchFlow TwoPlayer(MatchOptions? options = null, int majorRound = 5, PlayerId[]? order = null)
    {
        MapData map = Map();
        MatchFlow match = MatchFlow.CreateUnvalidated(map, Seed, [P0, P1], Relics(map), options ?? MatchOptions.Immediate);
        match.Debug.SeedHand(P0, (PieceType.Basic, 50));
        match.Debug.SeedHand(P1, (PieceType.Basic, 50));
        match.PlantSequentially([(P0, 0), (P1, 1)]);
        match.Debug.SetMajorRound(majorRound);
        match.Debug.SetOrder(order ?? [P0, P1]);
        return match;
    }

    /// <summary>9×9 合成地图（见 <see cref="TestMaps.NineByNine(string[])"/>）。</summary>
    internal static MapData Map(params string[] relicCells) => TestMaps.NineByNine(relicCells);

    /// <summary>同上，另可指定地形。</summary>
    internal static MapData Map(TerrainData? terrain, params string[] relicCells) => TestMaps.NineByNine(terrain, relicCells);

    internal static RelicGenerationRecord Relics(MapData map, params (string Cell, RelicContent Content)[] relics) => TestMaps.Relics(map, relics);

    /// <summary>创建一局但停在插旗阶段。</summary>
    internal static MatchFlow Create(GameSeed? seed = null, MatchOptions? options = null, params (string Cell, RelicContent Content)[] relics) =>
        Create(null, seed, options, relics);

    /// <summary>同上，另可指定地形。</summary>
    internal static MatchFlow Create(TerrainData? terrain, GameSeed? seed = null, MatchOptions? options = null, params (string Cell, RelicContent Content)[] relics)
    {
        MapData map = Map(terrain, [.. relics.Select(r => r.Cell)]);
        return MatchFlow.CreateUnvalidated(map, seed ?? Seed, All, Relics(map, relics), options);
    }

    /// <summary>创建一局并依次插旗（默认各占各的出生区），给每人 50 枚普通子，进入第 1 大回合。<paramref name="options"/> 缺省为 <see cref="MatchOptions.Immediate"/>。</summary>
    internal static MatchFlow Started(GameSeed? seed = null, int[]? zones = null, MatchOptions? options = null, params (string Cell, RelicContent Content)[] relics) =>
        Started(null, seed, zones, options, relics);

    /// <summary>同上，另可指定地形。</summary>
    internal static MatchFlow Started(TerrainData? terrain, GameSeed? seed = null, int[]? zones = null, MatchOptions? options = null, params (string Cell, RelicContent Content)[] relics)
    {
        MatchFlow match = Create(terrain, seed, options ?? MatchOptions.Immediate, relics);
        zones ??= [0, 1, 2, 3];
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, zones[i])));
        return match;
    }
}

/// <summary>
/// 占位随机策略（种子驱动）：整理时随机弃到合法，征募随机选，部署随机挑合法空格；用 <see cref="RandomStream"/>，不用 <c>Random.Shared</c>。
/// AI 决策逻辑属于 add-heuristic-ai；这里只为端到端跑通流程。
/// </summary>
internal sealed class RandomTurnController(RandomStream rng) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        for (int i = 0; i < overflow; i++)
        {
            PieceType[] types = [.. hand.PrivateView().Types];
            hand.Discard(types[rng.NextInt(types.Length)]);
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        int picks = rng.NextInt(panel.PicksRemaining + 1);
        for (int i = 0; i < picks; i++)
        {
            int[] selectable = [.. hand.Panel().Candidates.Where(c => c.IsSelectable).Select(c => c.Index)];
            if (selectable.Length == 0)
            {
                return;
            }

            hand.Pick(selectable[rng.NextInt(selectable.Length)]);
        }
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        Coord[] cells = [.. batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        PieceType[] types = [.. batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key)];
        if (cells.Length == 0 || types.Length == 0)
        {
            return;
        }

        int want = rng.NextInt(batch.Context.DeployLimit + 1);
        int attempts = 0;
        while (batch.Count < want && attempts++ < 12)
        {
            Coord cell = cells[rng.NextInt(cells.Length)];
            PieceType type = types[rng.NextInt(types.Length)];
            if (batch.Stage(cell, type) is not null)
            {
                continue;
            }

            if (!rehearse().IsLegal)
            {
                batch.Unstage(cell);
            }
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        if (batch.Count == 0)
        {
            return false;
        }

        batch.Unstage(batch.Placements[^1].Coord);
        return true;
    }
}
