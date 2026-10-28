using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.PieceRelics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 带入信物接线（relic-redesign 接线段 W2，R14–R17 / D5 / D6）：每名玩家的带入清单与逐件状态（未挂出 / 已挂出或已掉落的本局编号）、
/// 部署时挂出、出局 / 弃赛时掉落、终局与中途的带出结算。档案读写不在这里（入口层经 <see cref="RelicProfileStore"/>）。
/// </summary>
/// <remarks>
/// <para><b>编号</b>（design.md D1）：本局编号在信物<b>公开</b>时才分配——挂出（结算第 2 步，从 <see cref="BatchContext.NextRelicId"/> 起按批内顺序）
/// 或掉落（R17，按清单下标顺序）——与地图刷新共用游标 <see cref="NextRelicId"/>。开局不给带入件编号：否则第一批刷新件的编号就暴露了全员带入总件数（R22）。
/// 终局时仍未挂出的件在结算时刻临时编号（<see cref="CarriedTokensForSettlement"/>，不写回对局状态），只用于结算去重。</para>
/// <para><b>可见性</b>（R22）：清单与未挂出件只经 <see cref="PendingCarriedOf"/>（本人座位视图 / 本人部署上下文）给出，不进 <see cref="Publish"/>；
/// 挂出后的信物在盘面信物层里，全公开。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    /// <summary>有带入的玩家 → 逐件已分配的本局编号（<c>null</c> = 未挂出）。下标与 <see cref="CarryIns"/> 里该玩家清单的下标一致。</summary>
    private readonly SortedDictionary<PlayerId, int?[]> _carriedIds = [];

    /// <summary>
    /// 某玩家尚未挂出的带入件：清单下标 → 种类与保险（私有信息，R22：只给本人）。出局 / 弃赛后只剩保险件（非保险件已掉落）。不带入时为空。
    /// </summary>
    public ImmutableSortedDictionary<int, CarriedRelic> PendingCarriedOf(PlayerId player)
    {
        Require(player);
        if (!_carriedIds.TryGetValue(player, out int?[]? ids))
        {
            return ImmutableSortedDictionary<int, CarriedRelic>.Empty;
        }

        ImmutableArray<CarriedRelic> items = CarryIns[player].Items;
        return Enumerable.Range(0, items.Length).Where(i => ids[i] is null).ToImmutableSortedDictionary(i => i, i => items[i]);
    }

    /// <summary>
    /// 建局校验（D5，与恢复共用）：关闭时不得有带入；带入者须在名册；每份清单 ≤ 5 件、保险 ≤ 2（<see cref="RelicLoadout.ShapeRefusal"/>，不看库存——库存在入口层经档案校验）；
    /// 空清单不得列出（"不带入"即不列）。不符即抛 <see cref="ArgumentException"/>。
    /// </summary>
    private static void RequireValidCarryIns(bool carryInOut, ImmutableSortedDictionary<PlayerId, RelicLoadout> carryIns, IReadOnlyCollection<PlayerId> players)
    {
        ArgumentNullException.ThrowIfNull(carryIns);
        if (carryIns.Count == 0)
        {
            return;
        }

        if (!carryInOut)
        {
            throw new ArgumentException($"带入带出已关闭，却给出了 {carryIns.Count} 名玩家的带入；关闭时各玩家带入必须为空。", nameof(carryIns));
        }

        foreach ((PlayerId player, RelicLoadout loadout) in carryIns)
        {
            ArgumentNullException.ThrowIfNull(loadout);
            if (!players.Contains(player))
            {
                throw new ArgumentException($"带入者 {player} 不在本局名册中。", nameof(carryIns));
            }

            if (loadout.ShapeRefusal() is { } refusal)
            {
                throw new ArgumentException($"玩家 {player} 的带入清单不合规：{refusal}。", nameof(carryIns));
            }

            if (loadout.Count == 0)
            {
                throw new ArgumentException($"玩家 {player} 的带入清单为空：不带入的玩家不列出。", nameof(carryIns));
            }
        }
    }

    /// <summary>建局（构造器调用）：每名有带入的玩家逐件都未挂出。</summary>
    private void InitCarried()
    {
        foreach ((PlayerId player, RelicLoadout loadout) in Options.CarryIns)
        {
            _carriedIds.Add(player, new int?[loadout.Count]);
        }
    }

    /// <summary>
    /// 结算第 6 步开头（出局检查之前）：把本批挂出的带入件记为已挂出，编号游标推进到本批最后一件之后。
    /// 必须先于出局检查——出局者的 R17 掉落也从游标取编号。编号从正式盘面读回（第 2 步按 <see cref="BatchRehearsal.CarriedTokens"/> 写入）。
    /// </summary>
    private void RecordCarriedAttachments(SettlementContext context)
    {
        if (context.IsPass || !_carriedIds.TryGetValue(context.Player, out int?[]? ids))
        {
            return;
        }

        var attached = new List<BoardRelic>();
        foreach (Placement placement in context.Placements)
        {
            if (placement.AttachCarried is not { } index)
            {
                continue;
            }

            RelicToken token = Board.RelicAt(placement.Coord)
                ?? throw new SiegeRuleException($"带入件挂出后 {placement.Coord.ToNotation()} 上没有信物。");
            ids[index] = token.Id;
            _nextRelicId = Math.Max(_nextRelicId, token.Id + 1);
            attached.Add(new BoardRelic(placement.Coord, token));
        }

        if (attached.Count > 0)
        {
            Emit(FlowEventKind.CarriedRelicsAttached, context.Player, string.Join(" ", attached.Select(Describe)));
        }
    }

    /// <summary>
    /// R17：<paramref name="player"/> 出局 / 弃赛（状态已改）时，其未挂出的非保险带入件按清单下标顺序各取一个本局编号，经 <see cref="RelicSpawner.Drop"/>
    /// （子流 <c>relic-drop:&lt;编号&gt;</c>）立即落到地图，不占刷新上限；候选格不够时多出的件消失（已编号、不在盘面上，结算时归入消失）。保险件不掉落，局终归还原主。
    /// </summary>
    private void DropCarriedOf(PlayerId player)
    {
        if (!_carriedIds.TryGetValue(player, out int?[]? ids))
        {
            return;
        }

        ImmutableArray<CarriedRelic> items = CarryIns[player].Items;
        var tokens = new List<RelicToken>();
        for (int i = 0; i < items.Length; i++)
        {
            if (ids[i] is null && !items[i].Insured)
            {
                ids[i] = _nextRelicId;
                tokens.Add(RelicToken.Carried(_nextRelicId++, items[i].Kind, player, insured: false));
            }
        }

        if (tokens.Count == 0)
        {
            return;
        }

        ImmutableArray<BoardRelic> dropped = RelicSpawner.Drop(Board, new BirthBoardArea(Board.Map), Seed, player, tokens);
        foreach (BoardRelic relic in dropped)
        {
            Board.AttachRelic(relic.Cell, relic.Token);
        }

        int lost = tokens.Count - dropped.Length;
        Emit(FlowEventKind.CarriedRelicsDropped, player,
            string.Join(" ", dropped.Select(Describe)) + (lost > 0 ? $"；{lost} 件无处可落、消失" : string.Empty));
    }

    /// <summary>
    /// 本局全部带入件（结算用，D6）：已挂出 / 已掉落的按其本局编号；仍未挂出的从当前游标起按（玩家、清单下标）顺序临时编号——不写回对局状态，只用于结算去重。
    /// </summary>
    private ImmutableArray<RelicToken> CarriedTokensForSettlement()
    {
        ImmutableArray<RelicToken>.Builder tokens = ImmutableArray.CreateBuilder<RelicToken>();
        int provisional = _nextRelicId;
        foreach ((PlayerId player, int?[] ids) in _carriedIds)
        {
            ImmutableArray<CarriedRelic> items = CarryIns[player].Items;
            for (int i = 0; i < items.Length; i++)
            {
                tokens.Add(RelicToken.Carried(ids[i] ?? provisional++, items[i].Kind, player, items[i].Insured));
            }
        }

        return tokens.ToImmutable();
    }

    /// <summary>
    /// 终局的带出结算（D6 + R27 + R19，唯一实现 <see cref="RelicSettlement"/>）：完赛者按最终名次、弃赛者按弃赛快照的弃赛时名次与大回合（MUST NOT 读最终名次）、
    /// 出局者 0 点；信物按终局盘面的持有关系归属。只读，不改对局状态；档案写入由入口层做（同一局只结算一次由档案的在途记录去重）。
    /// 截断局（未终局）不结算：调用即抛 <see cref="SiegeRuleException"/>。
    /// </summary>
    public RelicMatchSettlement SettleCarried()
    {
        if (Phase != MatchPhase.Ended || Result is not { } result)
        {
            throw new SiegeRuleException("对局尚未终局：带出结算只在终局后进行（截断局不结算）。");
        }

        ImmutableSortedDictionary<PlayerId, RelicPlayerOutcome>.Builder outcomes = ImmutableSortedDictionary.CreateBuilder<PlayerId, RelicPlayerOutcome>();
        foreach (Standing standing in result.Standings)
        {
            outcomes.Add(standing.Player, standing.Group switch
            {
                StandingGroup.Finisher => RelicPlayerOutcome.Finished(standing.Rank),
                StandingGroup.Resigned => ResignedOutcome(standing.Player),
                StandingGroup.Eliminated => RelicPlayerOutcome.Eliminated(),
                _ => throw new SiegeRuleException($"未知名次分组 {standing.Group}。"),
            });
        }

        return RelicSettlement.Settle(Board, outcomes.ToImmutable(), CarriedTokensForSettlement());
    }

    /// <summary>
    /// 中途的带出结算（D6：出局 / 弃赛者的结算在其出局 / 弃赛时立即完成）：<paramref name="player"/> 须已出局或弃赛。非完赛者按 D6 恰好只拿回保险件，
    /// 点数按 R19（弃赛 50%、保护期内 0、出局 0），与终局时 <see cref="SettleCarried"/> 给这名玩家的结果相同（之后的对局事件不改变它）。
    /// 走同一实现 <see cref="RelicSettlement.Settle"/>：其余玩家此刻都不是完赛者，以"中途退出"占位，不影响本人的结果。
    /// </summary>
    public RelicPlayerSettlement EarlySettlementFor(PlayerId player)
    {
        PlayerRecord record = Require(player);
        RelicPlayerOutcome mine = record.Status switch
        {
            PlayerStatus.Resigned => ResignedOutcome(player),
            PlayerStatus.Eliminated => RelicPlayerOutcome.Eliminated(),
            _ => throw new SiegeRuleException($"玩家 {player} 仍在局（{record.Status}），没有中途结算。"),
        };

        ImmutableSortedDictionary<PlayerId, RelicPlayerOutcome> outcomes =
            _players.ToImmutableSortedDictionary(p => p, p => p == player ? mine : RelicPlayerOutcome.Abandoned());
        return RelicSettlement.Settle(Board, outcomes, CarriedTokensForSettlement()).Of(player);
    }

    private RelicPlayerOutcome ResignedOutcome(PlayerId player)
    {
        ResignationSnapshot snapshot = _resignations.LastOrDefault(r => r.Player == player)
            ?? throw new SiegeRuleException($"弃赛者 {player} 没有弃赛快照。");
        return RelicPlayerOutcome.Resigned(
            snapshot.RankAtResign ?? throw new SiegeRuleException($"弃赛者 {player} 的快照没有弃赛时名次，不能结算。"), snapshot.MajorRound);
    }

    private static string Describe(BoardRelic relic) =>
        $"{relic.Cell.ToNotation()}#{relic.Token.Id}:{relic.Token.Kind}{(relic.Token.Insured ? "(保险)" : string.Empty)}";

    /// <summary>存档段：每名有带入的玩家的清单与逐件编号（<c>null</c> = 未挂出）。</summary>
    private List<CarryInSaveData> ExportCarried() =>
        [.. _carriedIds.Select(kv => new CarryInSaveData
        {
            Player = kv.Key.Value,
            Items = [.. CarryIns[kv.Key].Items.Select((item, i) => new CarriedSaveData { Kind = item.Kind, Insured = item.Insured, Id = kv.Value[i] })],
        })];

    /// <summary>存档段的清单部分（对局配置）。</summary>
    private static ImmutableSortedDictionary<PlayerId, RelicLoadout> ImportLoadouts(List<CarryInSaveData>? data) =>
        (data ?? []).ToImmutableSortedDictionary(
            c => new PlayerId(c.Player),
            c => new RelicLoadout([.. (c.Items ?? []).Select(i => new CarriedRelic(i.Kind, i.Insured))]));

    /// <summary>
    /// 恢复逐件编号（信物刷新段与盘面之后）：编号须小于游标、互不重复；在盘面上的同编号信物须是同一件（种类、带入者、保险一致）。不符即抛 <see cref="FormatException"/>。
    /// </summary>
    private void RestoreCarried(List<CarryInSaveData>? data)
    {
        Dictionary<int, RelicToken> onBoard = Board.RelicsOnBoard().ToDictionary(r => r.Token.Id, r => r.Token);
        var seen = new HashSet<int>();
        foreach (CarryInSaveData saved in data ?? [])
        {
            var player = new PlayerId(saved.Player);
            int?[] ids = _carriedIds[player];
            List<CarriedSaveData> items = saved.Items ?? [];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id is not { } id)
                {
                    continue;
                }

                if (id < 0 || id >= _nextRelicId || !seen.Add(id))
                {
                    throw new FormatException($"存档的带入件编号 {id}（玩家 {player} 第 {i + 1} 件）越界或重复。");
                }

                if (onBoard.TryGetValue(id, out RelicToken token) && token != RelicToken.Carried(id, items[i].Kind, player, items[i].Insured))
                {
                    throw new FormatException($"存档的带入件编号 {id} 与盘面上的信物 {token} 不符。");
                }

                ids[i] = id;
            }
        }
    }
}

/// <summary>一名玩家的带入（存档；私有信息，不进公开视图）：玩家编号与清单。</summary>
public sealed class CarryInSaveData
{
    public int Player { get; set; }

    public List<CarriedSaveData>? Items { get; set; }
}

/// <summary>带入清单的一件：种类、保险，以及已分配的本局编号（已挂出或已掉落；未挂出为 <c>null</c>）。</summary>
public sealed class CarriedSaveData
{
    public RelicKind Kind { get; set; }

    public bool Insured { get; set; }

    public int? Id { get; set; }
}
