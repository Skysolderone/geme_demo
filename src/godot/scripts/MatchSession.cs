using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Carry;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Presentation.Preview;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 对局驱动（tactical-ui 裁决 11）：<b>单线程</b>，人类回合直接调 <see cref="MatchFlow"/> 的阶段接口，
/// AI 回合走 <see cref="MatchRunner.RunTurn"/>。本类只接线，不含任何规则计算——界面要显示的一切都从 <see cref="World"/> 取。
/// </summary>
public sealed class MatchSession
{
    private readonly Dictionary<int, PlayerId> _zoneOwners = [];
    private MatchRunner? _runner;

    private MatchSession(MatchFlow match, PlayerId me, ulong seed, AiDifficulty difficulty, int? cellLimit)
    {
        Match = match;
        Me = me;
        Seed = seed;
        Difficulty = difficulty;
        // AI 候选格上限：未显式给出时按地图的可落子格数取（阈值逻辑在 Core，与批量 / 终端版共用）。
        Search = AiSearchConfig.ForMap(difficulty, match.Map.PlayableCount, cellLimit).Validated();
        World = BuildWorld();
    }

    /// <summary>规则内核。</summary>
    public MatchFlow Match { get; }

    /// <summary>本机玩家。</summary>
    public PlayerId Me { get; }

    /// <summary>本局种子；打印出来即可复现同一局。</summary>
    public ulong Seed { get; }

    /// <summary>AI 难度。</summary>
    public AiDifficulty Difficulty { get; }

    /// <summary>AI 的剪枝参数（含候选格上限 K）。</summary>
    public AiSearchConfig Search { get; }

    /// <summary>当前观察者世界。每次状态变化后整体重建，两份快照必然同一时刻。</summary>
    public ViewerWorld World { get; private set; }

    /// <summary>最近一次被拒绝的操作说明；无则为 <c>null</c>。</summary>
    public FailurePresentation? LastFailure { get; private set; }

    /// <summary>一行状态提示。</summary>
    public string Notice { get; private set; } = string.Empty;

    /// <summary>当前征募面板；不在征募阶段为 <c>null</c>。</summary>
    public RecruitPanelView? RecruitPanel { get; private set; }

    /// <summary>手牌栏里选中的棋子类型（决定点格子时放什么）。</summary>
    public PieceType? SelectedType { get; set; }

    /// <summary>出生区 → 玩家（插旗锁定后才有）。</summary>
    public IReadOnlyDictionary<int, PlayerId> ZoneOwners => _zoneOwners;

    /// <summary>是否还在等玩家插旗。</summary>
    public bool AwaitingZone => Match.Phase == MatchPhase.FlagPlanting;

    /// <summary>轮到本机玩家行动。</summary>
    public bool IsMyTurn => Match.Phase == MatchPhase.InProgress && Match.CurrentPlayer == Me;

    /// <summary>对局已结束。</summary>
    public bool IsOver => Match.Phase == MatchPhase.Ended;

    /// <summary>本回合暂放的落点。</summary>
    public ImmutableArray<Placement> Staged => Match.CurrentBatch?.Placements ?? [];

    /// <summary>
    /// 开一局：地图由入口经 <see cref="MapCatalog"/> 解析后传入（缺省四方标准地图），本机玩家坐第 <paramref name="seat"/> 位（1 起）。
    /// <paramref name="carryInOut"/> 为真即开启带入带出（carry-in-out D10）：本机玩家带 <paramref name="carry"/>（可为空），
    /// 各 AI 按"与本机玩家同等数量"从 <c>carry-ai</c> 子流抽取（<see cref="CarryAi.ForHumanMatch"/>，与终端版同一实现）。关闭时对局选项与引入之前相同。
    /// </summary>
    public static MatchSession Create(MapData map, ulong seed, int playerCount, int seat, AiDifficulty difficulty, int? cellLimit = null, bool carryInOut = false, CarryIn? carry = null)
    {
        PlayerId[] players = [.. Enumerable.Range(0, playerCount).Select(i => new PlayerId(i))];
        PlayerId me = players[seat - 1];
        MatchOptions options = MatchOptions.Immediate;
        if (carryInOut)
        {
            options = options with { CarryInOut = true, CarryIns = CarryAi.ForHumanMatch(new GameSeed(seed), players, me, carry, options.ContentSet) };
        }

        MatchFlow match = MatchFlow.Create(map, new GameSeed(seed), players, options);
        return new MatchSession(match, me, seed, difficulty, cellLimit);
    }

    // ---------- 带入带出（carry-in-out）：只接线，结算一律由 Core 的纯函数给出、档案一律经 CarryProfileStore ----------

    private string _carryMatchId = string.Empty;

    /// <summary>本局的档案存取；带入带出关闭（含 <c>--no-carry</c> 与全部自动化模式）时为 <c>null</c>。</summary>
    public CarryProfileStore? CarryStore { get; private set; }

    /// <summary>本机玩家本局的带出结算；尚未结算为 <c>null</c>。弃赛 / 出局即时结算，其余在终局结算，每局只结算一次。</summary>
    public CarryOutResult? CarrySettled { get; private set; }

    /// <summary>本机玩家的带入（征召签已在建局时解析出类型）；无带入为 <c>null</c>。</summary>
    public CarryIn? MyCarry => Match.CarryIns.GetValueOrDefault(Me);

    /// <summary>开局登记：扣除库存、写在途记录（<see cref="CarryProfileStore.Begin"/>）。<paramref name="matchId"/> 只用于"同一局只结算一次"。</summary>
    public void BeginCarry(CarryProfileStore store, string matchId)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.Begin(matchId, MyCarry);
        CarryStore = store;
        _carryMatchId = matchId;
    }

    /// <summary>本机玩家此刻能否弃赛：对局进行中、本人仍在参赛，且处于小回合边界或本人的小回合内（与 <see cref="MatchFlow.Resign"/> 的前提一致）。</summary>
    public bool CanResign => Match.Phase == MatchPhase.InProgress
        && Match.StateOf(Me).Status == Siege.Core.Scoring.PlayerStatus.Active
        && (Match.Stage == TurnStage.Idle || Match.CurrentPlayer == Me);

    /// <summary>
    /// 本机玩家弃赛（二次确认在界面上做）：规则层记弃赛快照与弃赛时势力名次；带入带出开启时立即结算并写档（design.md D5），此后的对局事件不再改变它。
    /// 弃赛后 AI 继续把对局下完。
    /// </summary>
    public void Resign()
    {
        if (!CanResign)
        {
            return;
        }

        Match.Resign(Me);
        ResignationSnapshot snapshot = Match.Resignations.Last(r => r.Player == Me);
        RecruitPanel = null;
        SelectedType = null;
        LastFailure = null;
        Notice = $"{Siege.Presentation.Text.Labels.Player(Me)} 弃赛（弃赛时势力 {snapshot.Power}，弃赛名次第 {snapshot.RankAtResign}）。此后由 AI 继续把对局下完。";
        if (CarryStore is not null && CarrySettled is null)
        {
            CarrySettled = CarryOutSettlement.Resigned(Match.Players.Length, snapshot.RankAtResign!.Value, snapshot.MajorRound, MyCarry);
            CarryStore.Settle(_carryMatchId, CarrySettled);
        }

        Rebuild();
    }

    /// <summary>
    /// 每步之后调用：本机玩家出局即结算（补给丢失、带出 0），对局终局即按最终名次结算完赛者；已结算或带入带出关闭时什么也不做。
    /// 返回本次新产生的结算（界面据此弹出结算面板），否则 <c>null</c>。
    /// </summary>
    public CarryOutResult? SettleCarryIfDue()
    {
        if (CarryStore is null || CarrySettled is not null)
        {
            return null;
        }

        if (Match.StateOf(Me).Status == Siege.Core.Scoring.PlayerStatus.Eliminated)
        {
            CarrySettled = CarryOutSettlement.Eliminated(MyCarry);
        }
        else if (IsOver)
        {
            CarrySettled = CarryOutSettlement.Settle(Match.Result, Match.Resignations, Match.CarryIns, Match.Players)[Me];
        }
        else
        {
            return null;
        }

        CarryStore.Settle(_carryMatchId, CarrySettled);
        return CarrySettled;
    }

    /// <summary>某格属于哪个出生区；不是出生区格为 <c>null</c>。</summary>
    public int? BirthZoneAt(Coord coord) => Match.Map.BirthZoneOf(coord);

    /// <summary>
    /// 插旗：本机玩家选定出生区，其余玩家的区由 Core 的唯一实现给出（<see cref="MatchFlow.PlantPrototype"/>：
    /// 标准图按编号顺排，平台多于人数的图由种子选区），随后锁定并开始第 1 大回合。
    /// </summary>
    public void ChooseZone(int zone)
    {
        if (!AwaitingZone)
        {
            return;
        }

        ImmutableArray<(PlayerId Player, int Zone)> choices = Match.PlantPrototype((Me, zone));
        foreach ((PlayerId player, int chosen) in choices)
        {
            _zoneOwners.TryAdd(chosen, player);
        }

        _runner = new MatchRunner(Match);
        foreach (PlayerId player in Match.Players.Where(p => p != Me))
        {
            _runner.SetController(player, HeuristicAi.Create(Match, player, Difficulty, config: Search));
        }

        Notice = $"出生区锁定。第 1 大回合顺序：{string.Join(" > ", Match.ActionOrder.Select(Siege.Presentation.Text.Labels.Player))}";
        Rebuild();
    }

    /// <summary>
    /// 推进本机玩家回合里不需要玩家决策的部分：开局 → 整理手牌（无超限时直接过）→ 停在征募面板。
    /// 每帧调用一次，幂等。
    /// </summary>
    public void AdvanceHumanStages()
    {
        if (!IsMyTurn)
        {
            return;
        }

        if (Match.Stage == TurnStage.Idle)
        {
            Match.BeginTurn();
            LastFailure = null;
            SelectedType = null;
            Rebuild();
        }

        if (IsMyTurn && Match.Stage == TurnStage.OrganizeHand && Match.CurrentHand().PrivateView().Overflow == 0)
        {
            RecruitPanel = Match.EnterRecruit();
            Rebuild();
        }
    }

    /// <summary>整理手牌阶段：整类弃牌（手牌类型超出槽位时的唯一出路）。</summary>
    public void Discard(PieceType type)
    {
        if (!IsMyTurn || Match.Stage != TurnStage.OrganizeHand)
        {
            return;
        }

        Guard(() => Match.CurrentHand().Discard(type));
    }

    /// <summary>征募阶段：选取一个候选。</summary>
    public void Pick(int candidateIndex)
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Recruit)
        {
            return;
        }

        Guard(() =>
        {
            Match.CurrentHand().Pick(candidateIndex);
            RecruitPanel = Match.CurrentHand().Panel();
        });
    }

    /// <summary>征募阶段 → 部署阶段。</summary>
    public void EnterDeploy()
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Recruit)
        {
            return;
        }

        Guard(() =>
        {
            Match.EnterDeploy();
            RecruitPanel = null;
            SelectedType ??= World.OwnHand.Types.Cast<PieceType?>().FirstOrDefault();
        });
    }

    /// <summary>部署阶段：该格已有本人暂放则撤回，否则用当前选中的类型暂放一枚。</summary>
    public void ToggleStage(Coord coord)
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy || Match.CurrentBatch is not { } batch)
        {
            return;
        }

        if (batch.Unstage(coord))
        {
            LastFailure = null;
            Notice = $"撤回 {coord.ToNotation()}";
            Rebuild();
            return;
        }

        if (SelectedType is not { } type)
        {
            Notice = "先在左下角手牌栏选一种棋子。";
            return;
        }

        BatchFailure? failure = batch.Stage(coord, type);
        LastFailure = failure is null ? null : FailurePresentation.From(failure);
        Notice = failure is null ? $"暂放 {coord.ToNotation()}" : LastFailure!.Detail;
        Rebuild();
    }

    /// <summary>
    /// 部署阶段：把某枚暂放匠人的改造目标向后轮换一位（"不改造" → 目标 1 → … → 目标 N → "不改造"）。
    /// </summary>
    /// <remarks>
    /// 候选目标<b>整份</b>取自预演呈现的 <see cref="ArtisanEditView.Targets"/>（← Core 富预演 ← 改造合法性唯一实现），
    /// 本方法只做"取下一个"的下标运算，MUST NOT 自己判断某个目标合不合法。
    /// 换目标的手段是撤回后原位重暂放：<c>StagedBatch.Replace</c> 只换类型、不带改造目标。
    /// </remarks>
    public bool CycleEdit(Coord? preferred)
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy || Match.CurrentBatch is not { } batch
            || World.Preview() is not { } preview || preview.ArtisanEdits.IsEmpty)
        {
            return false;
        }

        ArtisanEditView artisan = preview.ArtisanEdits.FirstOrDefault(a => preferred is { } p && a.ArtisanCell == p)
            ?? preview.ArtisanEdits[^1];
        if (artisan.Targets.IsEmpty)
        {
            Notice = $"{artisan.ArtisanCell.ToNotation()} 的匠人此刻没有可改造的目标。";
            return false;
        }

        // −1 = 当前"不改造"（轮换的第 0 位），下一位就是 Targets[0]。
        int current = -1;
        for (int i = 0; i < artisan.Targets.Length; i++)
        {
            if (artisan.Targets[i].IsChosen)
            {
                current = i;
                break;
            }
        }

        // 末位之后回到"不改造"（裁决 T-6）。
        int next = current + 1;
        TerrainEdit? edit = next < artisan.Targets.Length ? artisan.Targets[next].Edit : null;

        if (!batch.Unstage(artisan.ArtisanCell))
        {
            return false;
        }

        BatchFailure? failure = batch.Stage(artisan.ArtisanCell, PieceType.Artisan, edit);
        LastFailure = failure is null ? null : FailurePresentation.From(failure);
        Notice = failure is null
            ? $"{artisan.ArtisanCell.ToNotation()} 的匠人：{(edit is { } e ? Siege.Presentation.Text.Labels.TerrainEdit(e) : ArtisanEditView.NoEditText)}"
            : LastFailure!.Detail;
        Rebuild();
        return failure is null;
    }

    /// <summary>部署阶段：撤回某格（右键）。</summary>
    public void Unstage(Coord coord)
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy || Match.CurrentBatch is not { } batch)
        {
            return;
        }

        if (batch.Unstage(coord))
        {
            LastFailure = null;
            Notice = $"撤回 {coord.ToNotation()}";
            Rebuild();
        }
    }

    /// <summary>部署阶段：清空暂放。</summary>
    public void ClearBatch()
    {
        if (IsMyTurn && Match.Stage == TurnStage.Deploy && Match.CurrentBatch is { } batch)
        {
            batch.Clear();
            LastFailure = null;
            Rebuild();
        }
    }

    /// <summary>第 5 阶段：确认本批次。被拒时停留在部署阶段，暂放保留，失败说明进 <see cref="LastFailure"/>。</summary>
    public void Confirm()
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy)
        {
            return;
        }

        SettlementOutcome outcome = Match.Confirm();
        if (!outcome.Confirmed)
        {
            LastFailure = FailurePresentation.From(outcome.Failure!);
            Notice = $"{LastFailure.Title}：{LastFailure.Detail}";
            Rebuild();
            return;
        }

        LastFailure = null;
        RecruitPanel = null;
        Notice = "已确认落子。";
        Rebuild();
    }

    /// <summary>第 5 阶段：Pass（0 落子）。</summary>
    public void Pass()
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy)
        {
            return;
        }

        Match.Pass();
        LastFailure = null;
        RecruitPanel = null;
        Notice = "本回合 Pass。";
        Rebuild();
    }

    /// <summary>跑完一个 AI 小回合，返回落子与被提子的演出数据。</summary>
    public TurnFlash RunAiTurn()
    {
        if (Match.Phase != MatchPhase.InProgress || Match.CurrentPlayer is not { } actor || actor == Me || _runner is null)
        {
            return TurnFlash.None;
        }

        MatchPublicView before = Match.Publish();
        _runner.RunTurn();
        MatchPublicView after = Match.Publish();

        ImmutableArray<Coord>.Builder placed = ImmutableArray.CreateBuilder<Coord>();
        ImmutableArray<Coord>.Builder captured = ImmutableArray.CreateBuilder<Coord>();
        foreach (Coord coord in after.Board.AllCoords())
        {
            Occupant? was = before.Board[coord].Occupant;
            Occupant? now = after.Board[coord].Occupant;
            if (now is { } n && was is null && n.Owner == actor)
            {
                placed.Add(coord);
            }
            else if (was is not null && now is null)
            {
                captured.Add(coord);
            }
        }

        string who = Siege.Presentation.Text.Labels.Player(actor);
        Notice = placed.Count == 0
            ? $"{who} Pass。"
            : $"{who} 落子 {placed.Count} 枚" + (captured.Count == 0 ? "。" : $"，提走 {captured.Count} 子。");
        Rebuild();

        // 改造的落成反馈不在这里算：AI 与人类两条路径都由 GameRoot 按默认棋盘视图的 Edits 增量统一给出（TurnFlash.Edits）。
        return new TurnFlash(placed.ToImmutable(), captured.ToImmutable(), []);
    }

    /// <summary>无人值守演示：把整理手牌阶段推过去（超出槽位时整类弃牌），停在征募面板。</summary>
    public void AutoAdvanceToRecruit()
    {
        AdvanceHumanStages();
        if (IsMyTurn && Match.Stage == TurnStage.OrganizeHand)
        {
            // 手牌类型超出槽位：弃掉第一种，解除强制弃牌门。
            PieceType? first = Match.CurrentHand().PrivateView().Types.Cast<PieceType?>().FirstOrDefault();
            if (first is { } type)
            {
                Discard(type);
            }

            AdvanceHumanStages();
        }
    }

    /// <summary>
    /// 无人值守演示：在本回合合法落子范围里找第一个能暂放的格子。
    /// 合法性由规则内核（<see cref="StagedBatch.Stage"/>）回答，界面不自行判断。
    /// </summary>
    public bool StageFirstLegal()
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy || Match.CurrentBatch is not { } batch || SelectedType is not { } type)
        {
            return false;
        }

        foreach (Coord coord in batch.Context.LegalRange.Order())
        {
            if (batch.Stage(coord, type) is null)
            {
                Rebuild();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 无人值守演示：把一枚匠人摆到"此刻能烧林"的落点上；没有这样的落点就退回第一个合法落点。
    /// </summary>
    /// <remarks>
    /// 全图只有 4 格林地，AI 的候选排行榜又被 16 条栅栏候选挤占（裁决 T-12），烧林在自检里几乎永远跑不到。
    /// 这里用"暂放 → 看预演给的目标 → 不合意就撤回"的笨办法找落点：判断全程只读
    /// <see cref="ArtisanEditView.BurnCells"/>（← 改造合法性唯一实现），界面一次都没有自己算过合法性。
    /// </remarks>
    public bool StageArtisanPreferringBurn()
    {
        if (!IsMyTurn || Match.Stage != TurnStage.Deploy || Match.CurrentBatch is not { } batch)
        {
            return false;
        }

        Coord? fallback = null;
        foreach (Coord coord in batch.Context.LegalRange.Order())
        {
            if (batch.Stage(coord, PieceType.Artisan) is not null)
            {
                continue;
            }

            Rebuild();
            PreviewPresentation? shown = World.Preview();

            // CanConfirm 也要看：LegalRange 只过了预演第 1–2 步，落到一个自己会被闷死的格上整批都会被拒（自杀手）。
            if (shown is { CanConfirm: true })
            {
                if (shown.ArtisanEdits.LastOrDefault() is { BurnCells.IsEmpty: false })
                {
                    return true;
                }

                fallback ??= coord;
            }

            batch.Unstage(coord);
        }

        if (fallback is not { } cell || batch.Stage(cell, PieceType.Artisan) is not null)
        {
            Rebuild();
            return false;
        }

        Rebuild();
        return true;
    }

    /// <summary>重建观察者世界：公开快照、补充载荷、私有手牌、预演<b>一次性</b>取齐，杜绝跨帧混用。</summary>
    public void Rebuild() => World = BuildWorld();

    private ViewerWorld BuildWorld()
    {
        MatchPublicView view = Match.Publish();
        PublicSupplement supplement = Match.PublishSupplement();
        HandPrivateView hand = Match.Hands.AccessFor(Me).PrivateView();
        BatchPreview? preview = Match.Stage == TurnStage.Deploy && Match.CurrentPlayer == Me ? Match.PreviewCurrentBatch() : null;
        return ViewerWorld.Build(Me, view, supplement, hand, preview);
    }

    private void Guard(Action action)
    {
        try
        {
            action();
            LastFailure = null;
        }
        catch (SiegeRuleException ex)
        {
            Notice = ex.Message;
        }

        Rebuild();
    }
}
