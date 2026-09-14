using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 对局顶层状态的持久化与恢复（implement 1.1 / 8.2）。
/// </summary>
/// <remarks>
/// <para><b>存档边界 = 小回合边界</b>：插旗阶段、两个小回合之间（<see cref="TurnStage.Idle"/>）或已结束时可存档；小回合进行中不可——
/// 进行中的征募面板与暂放批次属于该玩家的私有瞬态，手牌层不导出它们。</para>
/// <para>随机子流只记录消费次数，恢复时按种子重新派生再推进；信物内容由 <c>relic-gen</c> 子流重算，不存内容。
/// 事件日志与征募记录是遥测，不随存档往返；势力榜恢复后按盘面重算。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>序列化为 JSON。只允许在小回合边界调用。</summary>
    public string Serialize()
    {
        if (Stage != TurnStage.Idle)
        {
            throw new SiegeRuleException($"小回合进行中（阶段 {Stage}），对局只能在小回合边界存档。");
        }

        var data = new MatchSaveData
        {
            MapId = Map.Id,
            Seed = Seed.ToString(),
            FlagTimeLimitTicks = Options.FlagTimeLimit.Ticks,
            MaxMajorRounds = MaxMajorRounds,
            Phase = Phase,
            MajorRound = MajorRound,
            Order = [.. _order.Select(p => p.Value)],
            OrderIndex = _orderIndex,
            PassStreak = _passStreak,
            EliminationSequence = _eliminationSequence,
            SetupConsumed = _setup.Consumed,
            FlagsLocked = Flags.IsLocked,
            Players = [.. _players.Select(p => new PlayerSaveData
            {
                Player = p.Value,
                Status = _records[p].Status,
                Protection = _records[p].Protection,
                BirthZone = _records[p].BirthZone,
                Flag = Flags.FlagOf(p),
                LastRoundPosition = _records[p].LastRoundPosition,
                SeedRank = _records[p].SeedRank,
                EliminationOrder = _records[p].EliminationOrder,
                EliminatedInMajorRound = _records[p].EliminatedInMajorRound,
                ResignedInMajorRound = _records[p].ResignedInMajorRound,
                PowerAtResign = _records[p].PowerAtResign,
            })],
            Board = Board.Serialize(),
            History = History.Serialize(),
            Relics = Relics.ExportState(),
            Hands = Hands.Export(),
            Initiative = [.. _initiative],
            Resignations = [.. _resignations.Select(r => new ResignationSaveData
            {
                Player = r.Player.Value,
                MajorRound = r.MajorRound,
                Board = r.Board,
                Hand = [.. r.Hand.Entries.Select(kv => new HandSegmentSaveData { Type = kv.Key, Carried = kv.Value.Carried, Gained = kv.Value.Gained })],
                HandPhase = r.Hand.Phase,
                TypeSlots = r.Hand.TypeSlots,
                RevealCount = r.Effects.RevealCount,
                FreePickCount = r.Effects.FreePickCount,
                EffectTypeSlots = r.Effects.TypeSlots,
                DeployLimit = r.Effects.DeployLimit,
                HeldTypeCount = r.Effects.HeldTypeCount,
                Emblems = [.. r.Effects.EmblemCounts.Select(kv => new HandStockEntry(kv.Key, kv.Value))],
                ControlledRelics = [.. r.ControlledRelics.Select(c => c.ToNotation())],
                Power = r.Power,
            })],
            Result = Result is null ? null : new ResultSaveData
            {
                Reason = Result.Reason,
                MajorRound = Result.MajorRound,
                Standings = [.. Result.Standings.Select(s => new StandingSaveData
                {
                    Rank = s.Rank,
                    Player = s.Player.Value,
                    Group = s.Group,
                    Status = s.Input.Status,
                    Power = s.Input.Power,
                    ControlledRelics = s.Input.ControlledRelics,
                    ExclusiveCells = s.Input.ExclusiveCells,
                    Stones = s.Input.Stones,
                    EliminationOrder = s.Input.EliminationOrder,
                })],
            },
        };

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>从 <see cref="Serialize"/> 的输出恢复。地图由调用方提供，其 <see cref="MapData.Id"/> MUST 与存档一致。</summary>
    public static MatchFlow Restore(MapData map, string json)
    {
        ArgumentNullException.ThrowIfNull(map);
        MatchSaveData data = Parse(json);
        RequireMap(map, data);
        GameSeed seed = GameSeed.Parse(data.Seed!);
        return RestoreCore(map, GameBoard.Restore(map, data.Board!), seed, RelicGenerator.Generate(map, seed), data);
    }

    /// <summary>测试专用：在未校验的合成地图与手工信物分布上恢复。</summary>
    internal static MatchFlow RestoreUnvalidated(MapData map, RelicGenerationRecord relics, string json)
    {
        MatchSaveData data = Parse(json);
        RequireMap(map, data);
        return RestoreCore(map, GameBoard.RestoreUnvalidated(map, data.Board!), GameSeed.Parse(data.Seed!), relics, data);
    }

    private static MatchSaveData Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<MatchSaveData>(json, JsonOptions) ?? throw new FormatException("对局存档为空或不是合法 JSON。");
    }

    private static void RequireMap(MapData map, MatchSaveData data)
    {
        if (!string.Equals(map.Id, data.MapId, StringComparison.Ordinal))
        {
            throw new FormatException($"存档记录的地图为 {data.MapId}，提供的地图为 {map.Id}。");
        }
    }

    private static MatchFlow RestoreCore(MapData map, GameBoard board, GameSeed seed, RelicGenerationRecord relicRecord, MatchSaveData data)
    {
        ImmutableArray<PlayerId> players = [.. data.Players.Select(p => new PlayerId(p.Player)).Order()];
        // round-cap：旧存档没有大回合上限字段 → 按标准局初值回填（不是 0，否则旧局会变成不限轮），并在 MaxMajorRoundsBackfilled 上留痕。
        bool backfilled = data.MaxMajorRounds is null;
        var options = new MatchOptions
        {
            FlagTimeLimit = TimeSpan.FromTicks(data.FlagTimeLimitTicks),
            MaxMajorRounds = data.MaxMajorRounds ?? MatchOptions.DefaultMaxMajorRounds,
        };
        RequireValidMaxMajorRounds(options.MaxMajorRounds, nameof(data));
        var match = new MatchFlow(
            map, board, seed, players,
            RelicLedger.Restore(relicRecord, data.Relics ?? throw new FormatException("存档缺少信物账本。")),
            HandLedger.Restore(seed, data.Hands ?? throw new FormatException("存档缺少手牌账本。")),
            BoardHistory.Deserialize(data.History ?? string.Empty),
            options);
        match.MaxMajorRoundsBackfilled = backfilled;

        foreach (PlayerSaveData saved in data.Players)
        {
            PlayerRecord record = match.Require(new PlayerId(saved.Player));
            record.Status = saved.Status;
            record.Protection = saved.Protection;
            record.BirthZone = saved.BirthZone;
            record.LastRoundPosition = saved.LastRoundPosition;
            record.SeedRank = saved.SeedRank;
            record.EliminationOrder = saved.EliminationOrder;
            record.EliminatedInMajorRound = saved.EliminatedInMajorRound;
            record.ResignedInMajorRound = saved.ResignedInMajorRound;
            record.PowerAtResign = saved.PowerAtResign;
        }

        if (data.FlagsLocked)
        {
            match.Flags.RestoreLocked(data.Players.Where(p => p.Flag is not null).Select(p => KeyValuePair.Create(new PlayerId(p.Player), p.Flag!.Value)));
        }
        else
        {
            foreach (PlayerSaveData saved in data.Players.Where(p => p.Flag is not null))
            {
                match.Flags.Plant(new PlayerId(saved.Player), saved.Flag!.Value);
            }
        }

        match.Phase = data.Phase;
        match.MajorRound = data.MajorRound;
        match._order = [.. data.Order.Select(v => new PlayerId(v))];
        match._orderIndex = data.OrderIndex;
        match._passStreak = data.PassStreak;
        match._eliminationSequence = data.EliminationSequence;
        match._setup.Advance(data.SetupConsumed);
        match._initiative.AddRange(data.Initiative);
        foreach (ResignationSaveData r in data.Resignations)
        {
            var player = new PlayerId(r.Player);
            var entries = r.Hand.ToImmutableSortedDictionary(h => h.Type, h => new HandEntry(h.Carried, h.Gained));
            var effects = new EffectSnapshot(player, r.MajorRound, r.RevealCount, r.FreePickCount, r.EffectTypeSlots, r.DeployLimit,
                r.Emblems.ToImmutableSortedDictionary(e => e.Type, e => e.Count), r.HeldTypeCount);
            match._resignations.Add(new ResignationSnapshot(player, r.MajorRound, r.Board!, new HandPrivateView(player, entries, r.HandPhase, r.TypeSlots),
                effects, [.. r.ControlledRelics.Select(Coord.Parse)], r.Power));
        }

        if (data.Result is { } result)
        {
            match.Result = new MatchResult(result.Reason, result.MajorRound, [.. result.Standings.Select(s =>
                new Standing(s.Rank, new PlayerId(s.Player), s.Group,
                    new StandingInput(new PlayerId(s.Player), s.Status, s.Power, s.ControlledRelics, s.ExclusiveCells, s.Stones, s.EliminationOrder)))]);
        }

        if (data.Phase != MatchPhase.FlagPlanting)
        {
            match.RecalculateDerived();
        }

        return match;
    }
}

/// <summary>对局存档的 JSON 结构。字段只用基本类型与已有的持久化记录；玩家编号用整数，坐标用围棋记法。</summary>
public sealed class MatchSaveData
{
    public int Version { get; set; } = 1;

    public string? MapId { get; set; }

    public string? Seed { get; set; }

    public long FlagTimeLimitTicks { get; set; }

    /// <summary>大回合上限（round-cap）。旧存档无此字段（<c>null</c>）→ 恢复时回填 <see cref="MatchOptions.DefaultMaxMajorRounds"/>。</summary>
    public int? MaxMajorRounds { get; set; }

    public MatchPhase Phase { get; set; }

    public int MajorRound { get; set; }

    public List<int> Order { get; set; } = [];

    public int OrderIndex { get; set; }

    public int PassStreak { get; set; }

    public int EliminationSequence { get; set; }

    public long SetupConsumed { get; set; }

    public bool FlagsLocked { get; set; }

    public List<PlayerSaveData> Players { get; set; } = [];

    public string? Board { get; set; }

    public string? History { get; set; }

    public RelicLedgerState? Relics { get; set; }

    public HandLedgerState? Hands { get; set; }

    public List<InitiativeReport> Initiative { get; set; } = [];

    public List<ResignationSaveData> Resignations { get; set; } = [];

    public ResultSaveData? Result { get; set; }
}

public sealed class PlayerSaveData
{
    public int Player { get; set; }

    public PlayerStatus Status { get; set; }

    public bool Protection { get; set; }

    public int? BirthZone { get; set; }

    public int? Flag { get; set; }

    public int? LastRoundPosition { get; set; }

    public int SeedRank { get; set; }

    public int? EliminationOrder { get; set; }

    public int? EliminatedInMajorRound { get; set; }

    public int? ResignedInMajorRound { get; set; }

    public long? PowerAtResign { get; set; }
}

public sealed class HandSegmentSaveData
{
    public PieceType Type { get; set; }

    public int Carried { get; set; }

    public int Gained { get; set; }
}

public sealed class ResignationSaveData
{
    public int Player { get; set; }

    public int MajorRound { get; set; }

    public string? Board { get; set; }

    public List<HandSegmentSaveData> Hand { get; set; } = [];

    public TurnPhase HandPhase { get; set; }

    public int? TypeSlots { get; set; }

    public int RevealCount { get; set; }

    public int FreePickCount { get; set; }

    public int EffectTypeSlots { get; set; }

    public int DeployLimit { get; set; }

    public int HeldTypeCount { get; set; }

    public List<HandStockEntry> Emblems { get; set; } = [];

    public List<string> ControlledRelics { get; set; } = [];

    public long Power { get; set; }
}

public sealed class StandingSaveData
{
    public int Rank { get; set; }

    public int Player { get; set; }

    public StandingGroup Group { get; set; }

    public PlayerStatus Status { get; set; }

    public long Power { get; set; }

    public int ControlledRelics { get; set; }

    public int ExclusiveCells { get; set; }

    public int Stones { get; set; }

    public int? EliminationOrder { get; set; }
}

public sealed class ResultSaveData
{
    public EndReason Reason { get; set; }

    public int MajorRound { get; set; }

    public List<StandingSaveData> Standings { get; set; } = [];
}
