using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Relics;

namespace Siege.Core.Tests;

/// <summary>流程层测试的公共夹具：带 4 个角落出生区的 9×9 合成地图、手工信物、跑一个小回合的快捷方法。</summary>
internal static class MatchFixtures
{
    internal static readonly PlayerId P0 = TestMaps.P0;
    internal static readonly PlayerId P1 = TestMaps.P1;
    internal static readonly PlayerId P2 = new(2);
    internal static readonly PlayerId P3 = new(3);
    internal static readonly PlayerId[] All = [P0, P1, P2, P3];

    internal static readonly GameSeed Seed = new(0x5EED_0912_2026UL);

    /// <summary>
    /// 9×9 合成地图：四个 3×3 角落出生区（0 左下、1 右下、2 左上、3 右上），信物格由用例指定。
    /// 不满足人数预算与距离校验，只能经 <see cref="MatchFlow.CreateUnvalidated"/> 使用。
    /// </summary>
    internal static MapData Map(params string[] relicCells)
    {
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        return new MapData
        {
            Id = "test-match-9x9",
            Width = 9,
            Height = 9,
            MaxPlayers = 4,
            Obstacles = [],
            BirthZones = [Corner(0, 0), Corner(6, 0), Corner(0, 6), Corner(6, 6)],
            RelicCells = relicCells.ToImmutableDictionary(Coord.Parse, _ => spec),
            ChokePoints = [Coord.Parse("E5")],
            CentralEntrance = Coord.Parse("E5"),
        };
    }

    private static ImmutableHashSet<Coord> Corner(int x0, int y0)
    {
        ImmutableHashSet<Coord>.Builder b = ImmutableHashSet.CreateBuilder<Coord>();
        for (int x = x0; x < x0 + 3; x++)
        {
            for (int y = y0; y < y0 + 3; y++)
            {
                b.Add(new Coord(x, y));
            }
        }

        return b.ToImmutable();
    }

    internal static RelicGenerationRecord Relics(MapData map, params (string Cell, RelicContent Content)[] relics)
    {
        var spec = new RelicCellSpec(RelicZone.Contested, BudgetTier.Standard);
        ImmutableArray<RelicPlacement> placements = [.. relics.Select(r => new RelicPlacement(Coord.Parse(r.Cell), r.Content, spec)).OrderBy(p => p.Coord)];
        return new RelicGenerationRecord(Seed, map.Id, placements, Converged: true, Rerolls: 0);
    }

    /// <summary>创建一局但停在插旗阶段。</summary>
    internal static MatchFlow Create(GameSeed? seed = null, MatchOptions? options = null, params (string Cell, RelicContent Content)[] relics)
    {
        MapData map = Map([.. relics.Select(r => r.Cell)]);
        return MatchFlow.CreateUnvalidated(map, seed ?? Seed, All, Relics(map, relics), options);
    }

    /// <summary>创建一局并依次插旗（默认各占各的出生区），给每人 50 枚普通子，进入第 1 大回合。</summary>
    internal static MatchFlow Started(GameSeed? seed = null, int[]? zones = null, params (string Cell, RelicContent Content)[] relics)
    {
        MatchFlow match = Create(seed, MatchOptions.Immediate, relics);
        zones ??= [0, 1, 2, 3];
        foreach (PlayerId p in All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(All.Select((p, i) => (p, zones[i])));
        return match;
    }

    /// <summary>
    /// 把对局摆到第 <paramref name="majorRound"/> 大回合开始、指定顺序。第 1–4 大回合开始时全员仍在保护中（第 4 大回合的解除发生在各自小回合完成后）；
    /// 第 5 大回合起默认全员已解除，<paramref name="protectedPlayers"/> 可指定保留。
    /// </summary>
    internal static MatchFlow AtRound(this MatchFlow match, int majorRound, PlayerId[]? order = null, params PlayerId[] protectedPlayers)
    {
        match.Debug.SetMajorRound(majorRound);
        match.Debug.SetOrder(order ?? All);
        foreach (PlayerId p in All)
        {
            match.Debug.SetProtection(p, majorRound <= MatchFlow.BuildProtectionRounds + 1 || protectedPlayers.Contains(p));
        }

        return match;
    }

    /// <summary>直接在权威盘面摆子（绕过规则，只为构造局面），然后重算派生量。</summary>
    internal static MatchFlow Stones(this MatchFlow match, PlayerId owner, params string[] cells)
    {
        foreach (string cell in cells)
        {
            match.Board.Place(Coord.Parse(cell), owner, PieceType.Basic);
        }

        match.Debug.Recalculate();
        return match;
    }

    /// <summary>跑完当前玩家的小回合：开始 → 征募（不选） → 部署指定落点 → 确认。落点为空即 Pass。</summary>
    internal static SettlementOutcome PlayTurn(this MatchFlow match, params string[] cells)
    {
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();
        foreach (string cell in cells)
        {
            BatchFailure? failure = batch.Stage(Coord.Parse(cell), PieceType.Basic);
            if (failure is not null)
            {
                throw new InvalidOperationException($"夹具暂放失败：{failure.Message}");
            }
        }

        SettlementOutcome outcome = match.Confirm();
        if (!outcome.Confirmed)
        {
            throw new InvalidOperationException($"夹具确认失败：{outcome.Failure!.Message}");
        }

        return outcome;
    }

    /// <summary>当前玩家 Pass。</summary>
    internal static SettlementOutcome PassTurn(this MatchFlow match) => match.PlayTurn();

    /// <summary>某玩家在盘面上的棋子数。</summary>
    internal static int StoneCount(this MatchFlow match, PlayerId player) => match.Board.GroupsOf(player).Sum(g => g.Size);

    /// <summary>某种类事件的数量。</summary>
    internal static int CountEvents(this MatchFlow match, FlowEventKind kind) => match.Events.Count(e => e.Kind == kind);
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
