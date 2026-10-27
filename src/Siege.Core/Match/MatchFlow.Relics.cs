using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.PieceRelics;

namespace Siege.Core.Match;

/// <summary>
/// 挂在棋子上的信物的地图刷新接线（relic-redesign 接线段 W1-2，design.md D2）：大回合<b>开始</b>时先落位到期的一批、再预告下一批。
/// 刷新本身是 <see cref="RelicSpawner"/> 的纯函数；本文件只管时机、编号与记录（对局状态、存档、公开视图 R22）。
/// </summary>
/// <remarks>
/// <para>时机：大回合开始只有一处——<see cref="EndMajorRound"/> 推进大回合序号之后（第 1 大回合由插旗锁定开始，不是预告 / 落位轮）。
/// 对局在大回合结束时已结束（最后战场结算 / 只剩一名）即不再开始新大回合，也就不刷新。</para>
/// <para>预告必须持久化：它是按预告那一刻的盘面抽的，落位时不再抽，存档里没有那一刻的盘面可以复算。</para>
/// <para>n 取开局参赛人数（<see cref="Players"/>，不随出局变化）；出生区域 = 出生棋盘（<see cref="BirthBoardArea"/>）。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private readonly List<RelicBatchRecord> _relicBatches = [];
    private int _nextRelicId;

    /// <summary>全部刷新批次（预告与落位记录，按预告先后）。全公开（R22）。</summary>
    public IReadOnlyList<RelicBatchRecord> RelicBatches => _relicBatches;

    /// <summary>当前尚未落位的预告；没有时为 <c>null</c>。全公开（R11 / R22）。</summary>
    public RelicForecast? PendingRelicForecast =>
        _relicBatches.Count > 0 && !_relicBatches[^1].HasLanded ? _relicBatches[^1].Forecast : null;

    /// <summary>下一件信物的本局编号（刷新与带入挂出都从这里分配，D1）。</summary>
    public int NextRelicId => _nextRelicId;

    /// <summary>
    /// 大回合开始（<see cref="MajorRound"/> 已推进）：① 有到期（落位大回合 = 本大回合）的预告即落位——按预告次序跳过已被占或已有信物的格，
    /// 其余依次分配编号写入盘面；② 本大回合是预告轮即预告下一批（子流 <c>relic-spawn:&lt;落位大回合&gt;</c>，场上未拾取件数取此刻盘面）。
    /// 地图信物落在空格上不改变任何势力（只有挂在棋子上的才计分），因此不需要重算势力榜。
    /// </summary>
    private void StartMajorRoundRelics()
    {
        if (_relicBatches.Count > 0 && _relicBatches[^1] is { HasLanded: false } pending && pending.Forecast.LandingRound == MajorRound)
        {
            ImmutableArray<BoardRelic> landed = RelicSpawner.Land(Board, pending.Forecast, _nextRelicId);
            foreach (BoardRelic relic in landed)
            {
                Board.AttachRelic(relic.Cell, relic.Token);
            }

            _nextRelicId += landed.Length;
            _relicBatches[^1] = pending with { Landed = landed };
            Emit(FlowEventKind.RelicsLanded, null, _relicBatches[^1].ToString());
        }

        if (RelicSpawner.Forecast(Board, new BirthBoardArea(Board.Map), Seed, MajorRound, _players.Length, RelicSpawner.UnclaimedMapRelics(Board)) is { } forecast)
        {
            _relicBatches.Add(new RelicBatchRecord(forecast, Landed: null));
            Emit(FlowEventKind.RelicsForecast, null, _relicBatches[^1].ToString());
        }
    }

    /// <summary>存档段：编号游标与全部批次。</summary>
    private RelicSpawnSaveData ExportRelics() => new()
    {
        NextRelicId = _nextRelicId,
        Batches = [.. _relicBatches.Select(b => new RelicBatchSaveData
        {
            LandingRound = b.Forecast.LandingRound,
            Slots = [.. b.Forecast.Slots.Select(s => new RelicSlotSaveData { Cell = s.Cell.ToNotation(), Kind = s.Kind })],
            Landed = b.Landed is { } landed
                ? [.. landed.Select(r => new RelicLandedSaveData { Cell = r.Cell.ToNotation(), Id = r.Token.Id, Kind = r.Token.Kind })]
                : null,
        })],
    };

    /// <summary>
    /// 恢复存档段。信物层本身随盘面序列化（<see cref="GameBoard.Serialize"/> 的 <c>#</c> 段）写回，这里只恢复记录与游标。
    /// 只有最后一批可以尚未落位；编号游标不得小于任何已落位信物的编号 + 1。不符即抛 <see cref="FormatException"/>。
    /// </summary>
    private void RestoreRelics(RelicSpawnSaveData? data)
    {
        if (data is null)
        {
            throw new FormatException("存档缺少信物刷新段（RelicSpawn）。");
        }

        for (int i = 0; i < data.Batches.Count; i++)
        {
            RelicBatchSaveData saved = data.Batches[i];
            if (saved.Landed is null && i != data.Batches.Count - 1)
            {
                throw new FormatException($"存档的信物刷新段第 {i + 1} 批未落位，但它不是最后一批。");
            }

            var forecast = new RelicForecast(saved.LandingRound, [.. saved.Slots.Select(s => new RelicSpawnSlot(ParseCell(s.Cell), s.Kind))]);
            ImmutableArray<BoardRelic>? landed = saved.Landed is null ? null
                : [.. saved.Landed.Select(r => new BoardRelic(ParseCell(r.Cell), RelicToken.Spawned(r.Id, r.Kind)))];
            if (landed is { } l && l.Any(r => r.Token.Id >= data.NextRelicId))
            {
                throw new FormatException($"存档的信物编号游标 {data.NextRelicId} 不大于已落位信物的编号。");
            }

            _relicBatches.Add(new RelicBatchRecord(forecast, landed));
        }

        _nextRelicId = data.NextRelicId;

        static Coord ParseCell(string? notation)
        {
            try
            {
                return Coord.Parse(notation ?? throw new FormatException("存档的信物刷新段缺少格坐标。"));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"存档的信物刷新段坐标错误：\"{notation}\"。", ex);
            }
        }
    }
}

/// <summary>对局存档的信物刷新段（relic-redesign W1）：编号游标与全部批次。</summary>
public sealed class RelicSpawnSaveData
{
    public int NextRelicId { get; set; }

    public List<RelicBatchSaveData> Batches { get; set; } = [];
}

/// <summary>一批刷新：落位大回合、预告的每件（格与种类）、落位结果（尚未落位为 <c>null</c>）。</summary>
public sealed class RelicBatchSaveData
{
    public int LandingRound { get; set; }

    public List<RelicSlotSaveData> Slots { get; set; } = [];

    public List<RelicLandedSaveData>? Landed { get; set; }
}

/// <summary>预告的一件：格（围棋记法）与种类（按名字写出）。</summary>
public sealed class RelicSlotSaveData
{
    public string? Cell { get; set; }

    public RelicKind Kind { get; set; }
}

/// <summary>落位的一件：格、编号与种类（地图刷出：无带入者、非保险）。</summary>
public sealed class RelicLandedSaveData
{
    public string? Cell { get; set; }

    public int Id { get; set; }

    public RelicKind Kind { get; set; }
}
