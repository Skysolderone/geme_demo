using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Core.Simul;
using Siege.Presentation.Show;
using Siege.Presentation.Text;
using Siege.Presentation.Visibility;

namespace Siege.Godot;

/// <summary>
/// 新玩法（simul-a，同时决策）的对局驱动：<b>单线程</b>，与 <see cref="MatchSession"/> 并列、互不分支。持有 <see cref="SimulFlow"/>、本机玩家与种子；
/// 本机的整理手牌、征召、暂放、提交、撤回、认输一律经 <see cref="SimulPrep"/> 句柄，AI 每次一名（<see cref="SimulGreedyAi"/>），
/// 揭晓前后各取一次本机投影生成演出节拍（<see cref="SimulBeats"/>）。本类只接线，不含任何规则计算——界面要显示的一切都从 <see cref="World"/> / <see cref="Payload"/> 取。
/// </summary>
public sealed class SimulSession
{
    private readonly SimulFlow _flow;

    private SimulSession(SimulFlow flow, PlayerId me, ulong seed)
    {
        _flow = flow;
        Me = me;
        Seed = seed;
        Payload = flow.PublishFor(me);
        World = Build(Payload);
    }

    /// <summary>
    /// 开局（A3 裁决 D6）：本机是玩家 1（P0），在 3D 场景里点中的出生座位 <paramref name="zone"/> 即本机的选择，其余玩家经 flag-pick 盲选（座位互斥），锁定后进入第 1 回合。
    /// 人数 = 地图人数；只开小图（规则层建局时校验）。
    /// </summary>
    public static SimulSession Start(MapData map, ulong seed, int zone)
    {
        ArgumentNullException.ThrowIfNull(map);
        PlayerId[] players = [.. Enumerable.Range(0, map.MaxPlayers).Select(i => new PlayerId(i))];
        PlayerId me = players[0];
        SimulFlow flow = SimulFlow.Create(map, new GameSeed(seed), players);
        flow.PlantPrototype((me, zone));
        return new SimulSession(flow, me, seed);
    }

    /// <summary>本机玩家。</summary>
    public PlayerId Me { get; }

    /// <summary>对局种子。</summary>
    public ulong Seed { get; }

    /// <summary>本机观察者世界（渲染、信息层、预演都从这里取）。只在状态变化后重建。</summary>
    public ViewerWorld World { get; private set; }

    /// <summary>与 <see cref="World"/> 同一时刻的本机投影（含本人校验结果与座位归属）。</summary>
    public SimulViewerPayload Payload { get; private set; }

    /// <summary>当前地图（地形可被改造）。</summary>
    public MapData Map => _flow.Map;

    /// <summary>出生座位 → 玩家。</summary>
    public IReadOnlyDictionary<int, PlayerId> ZoneOwners => Payload.ZoneOwners;

    /// <summary>当前回合号。</summary>
    public int Round => _flow.Round;

    /// <summary>已结算回合的报告。</summary>
    public IReadOnlyList<SimulRoundReport> Reports => _flow.Reports;

    /// <summary>终局结果；未终局为 <c>null</c>。</summary>
    public SimulResult? Result => _flow.Result;

    public bool IsOver => _flow.Phase == SimulPhase.Ended;

    /// <summary>本机仍是参赛中。</summary>
    public bool IsActive => _flow.StatusOf(Me) == PlayerStatus.Active;

    /// <summary>本机本回合已无事可做：已提交、已认输或已终局。</summary>
    public bool MyPrepDone => IsOver || !IsActive || _flow.IsSubmitted(Me);

    /// <summary>本机还能改手牌与暂放（准备中、未提交）。</summary>
    public bool CanEdit => !MyPrepDone;

    /// <summary>本机是否已提交（可撤回）。</summary>
    public bool CanWithdraw => !IsOver && IsActive && _flow.IsSubmitted(Me);

    /// <summary>点格暂放时用的类型；为 <c>null</c> 时取手牌里的第一类。</summary>
    public PieceType? SelectedType { get; set; }

    /// <summary>状态提示（被拒原因、AI 提交、揭晓摘要）。</summary>
    public string Notice { get; set; } = string.Empty;

    /// <summary>征召面板是否展开（界面状态）。</summary>
    public bool RecruitOpen { get; private set; }

    /// <summary>本机的征召面板；没打开过（或不在准备中）为 <c>null</c>。</summary>
    public RecruitPanelView? RecruitPanel => CanEdit && Prep.HandPhase == TurnPhase.Recruit ? Prep.Panel() : null;

    /// <summary>本机此刻的校验结果（准备中才有）。</summary>
    public SimulBatchCheck? Check => Payload.OwnCheck;

    /// <summary>本机此刻的暂放。</summary>
    public ImmutableArray<Placement> Staged => CanEdit ? Prep.Placements : [];

    /// <summary>每名玩家本回合的公开状态：参赛状态与是否已提交（对手只公开这两项，Q09）。按玩家编号。</summary>
    public ImmutableArray<(PlayerId Player, PlayerStatus Status, bool Submitted)> Seats =>
        [.. _flow.Players.Select(p => (p, _flow.StatusOf(p), _flow.IsSubmitted(p)))];

    /// <summary>本机此刻的可部署格（单枚口径，S3 的高亮层读它）；不在准备中为空。</summary>
    public ImmutableArray<Coord> DeployableCells => CanEdit ? Prep.DeployableCells() : [];

    private SimulPrep Prep => _flow.PrepFor(Me);

    // ---------- 本机操作（全部经 SimulPrep 句柄；被拒原因进 Notice） ----------

    /// <summary>点格：该格已有本机暂放即撤回，否则按所选类型暂放。</summary>
    public void ToggleStage(Coord coord) => Guard(() =>
    {
        if (!CanEdit)
        {
            return;
        }

        SimulPrep prep = Prep;
        if (prep.Placements.Any(p => p.Coord == coord))
        {
            prep.Unstage(coord);
            return;
        }

        PieceType? type = SelectedType is { } chosen && prep.Hand().CountOf(chosen) > 0 ? chosen : prep.Hand().Types.Cast<PieceType?>().FirstOrDefault();
        if (type is { } t)
        {
            prep.Stage(coord, t);
        }
    });

    /// <summary>撤回该格的暂放（没有暂放即无动作）。</summary>
    public void Unstage(Coord coord) => Guard(() =>
    {
        if (CanEdit && Prep.Placements.Any(p => p.Coord == coord))
        {
            Prep.Unstage(coord);
        }
    });

    /// <summary>
    /// 换改造目标：光标停在某枚已暂放的匠人上就换那一枚，否则换最后暂放的那一枚；在本人预演给出的合法目标里依次轮换，转完一圈回到不带改造。
    /// </summary>
    public void CycleEdit(Coord? preferred) => Guard(() =>
    {
        if (!CanEdit || Payload.OwnPreview is not { } preview)
        {
            return;
        }

        Placement? artisan = Prep.Placements.Where(p => p.Type == PieceType.Artisan).Cast<Placement?>()
            .FirstOrDefault(p => preferred is { } at && p!.Value.Coord == at)
            ?? Prep.Placements.Where(p => p.Type == PieceType.Artisan).Cast<Placement?>().LastOrDefault();
        if (artisan is not { } a || preview.EditOptions.FirstOrDefault(o => o.ArtisanCell == a.Coord) is not { } options || options.Legal.IsEmpty)
        {
            return;
        }

        int index = a.Edit is { } current ? options.Legal.IndexOf(current) : -1;
        Prep.SetEdit(a.Coord, index + 1 < options.Legal.Length ? options.Legal[index + 1] : null);
    });

    /// <summary>清空暂放。</summary>
    public void Clear() => Guard(() =>
    {
        if (CanEdit)
        {
            Prep.Clear();
        }
    });

    /// <summary>打开征召面板（候选是回合开始时预抽好的那一组）。</summary>
    public void OpenRecruit() => Guard(() =>
    {
        if (!CanEdit)
        {
            return;
        }

        if (Prep.HandPhase != TurnPhase.Recruit)
        {
            Prep.EnterRecruit();
        }

        RecruitOpen = true;
    });

    /// <summary>选取一个征召候选。</summary>
    public void Pick(int index) => Guard(() =>
    {
        if (CanEdit)
        {
            Prep.Pick(index);
        }
    });

    /// <summary>收起征召面板。</summary>
    public void CloseRecruit()
    {
        RecruitOpen = false;
        Rebuild();
    }

    /// <summary>提交当前暂放：校验不通过时保持可编辑，第一条原因进 <see cref="Notice"/>。</summary>
    public void Submit() => Guard(() =>
    {
        if (!CanEdit)
        {
            return;
        }

        SimulBatchCheck check = Prep.Submit();
        RecruitOpen = false;
        Notice = check.IsValid ? SimulTexts.WaitingOthers : check.FirstReason ?? string.Empty;
    });

    /// <summary>本回合不部署：清空暂放并提交 0 个单位（不撤销本回合征召）。</summary>
    public void SubmitEmpty() => Guard(() =>
    {
        if (!CanEdit)
        {
            return;
        }

        Prep.Clear();
        Prep.Submit();
        RecruitOpen = false;
        Notice = SimulTexts.WaitingOthers;
    });

    /// <summary>撤回提交（揭晓之前）。</summary>
    public void Withdraw() => Guard(() =>
    {
        if (CanWithdraw)
        {
            Prep.Withdraw();
            Notice = string.Empty;
        }
    });

    /// <summary>认输（Q11）：本回合的暂放作废、不扣牌，单位留在场上。</summary>
    public void Resign() => Guard(() =>
    {
        if (!IsOver && IsActive)
        {
            _flow.Resign(Me);
            Notice = SimulTexts.Resigned;
        }
    });

    // ---------- 推进（GameRoot 每帧至多调一次） ----------

    /// <summary>无人值守：本机也由贪心 AI 代打一回合（只读公开视图与本人句柄）。</summary>
    public void AutoPlayMe()
    {
        if (CanEdit)
        {
            SimulGreedyAi.Play(_flow.PublicView(), Prep);
            Rebuild();
        }
    }

    /// <summary>
    /// 截图专用（<c>--simul-hold</c>）：本机由贪心 AI 暂放一批再撤回提交，停在"有暂放、可编辑"的准备状态，供定帧截图。
    /// 暂放内容与 <see cref="AutoPlayMe"/> 相同，只是不提交。
    /// </summary>
    public void AutoStageMe()
    {
        if (CanEdit)
        {
            SimulGreedyAi.Play(_flow.PublicView(), Prep);
            Prep.Withdraw();
            Notice = string.Empty;
            Rebuild();
        }
    }

    /// <summary>下一名还没提交的 AI（按结算顺位）；都提交了为 <c>null</c>。</summary>
    public PlayerId? NextAi() =>
        IsOver ? null : _flow.Order.Where(p => p != Me && !_flow.IsSubmitted(p)).Cast<PlayerId?>().FirstOrDefault();

    /// <summary>让一名 AI 做完本回合的征召、暂放并提交。</summary>
    public void PlayAi(PlayerId ai)
    {
        SimulGreedyAi.Play(_flow.PublicView(), _flow.PrepFor(ai));
        Notice = $"{Labels.Player(ai)} {Labels.SimulSubmitted(true)}";
        Rebuild();
    }

    /// <summary>全员都已提交、可以揭晓。</summary>
    public bool ReadyToResolve => !IsOver && _flow.AllSubmitted;

    /// <summary>揭晓并结算本回合：结算前后各取一次本机投影，生成演出节拍（冲突在前）。</summary>
    public (SimulRoundReport Report, ImmutableArray<SettlementBeat> Beats) Resolve()
    {
        Siege.Core.Match.MatchPublicView before = _flow.PublishFor(Me).View;
        SimulRoundReport report = _flow.ResolveRound();
        Siege.Core.Match.MatchPublicView after = _flow.PublishFor(Me).View;
        RecruitOpen = false;
        Notice = SimulTexts.RoundResolved(report.Round, report.Conflicts.Length, report.Landed.Length, report.Captures.Sum(s => s.Removed.Length));
        Rebuild();
        return (report, SimulBeats.Generate(before, after, report));
    }

    /// <summary>重建本机观察者世界（投影与世界一次取齐）。</summary>
    public void Rebuild()
    {
        Payload = _flow.PublishFor(Me);
        World = Build(Payload);
    }

    private static ViewerWorld Build(SimulViewerPayload p) => ViewerWorld.Build(p.Viewer, p.View, p.Supplement, p.OwnHand, p.OwnPreview);

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (SiegeRuleException ex)
        {
            Notice = ex.Message;
        }

        Rebuild();
    }
}
