using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Recruit;

namespace Siege.Core.Simul;

/// <summary>
/// 一名参赛玩家本回合的私有准备句柄（J3 / Q09）：整理手牌、征召、暂放（加、撤、换位、换类型、挂改造）、预检、提交、撤回提交。
/// 流程层只把它交给该玩家的控制者。提交前可以随意改；提交后锁定（手牌与暂放都不能再动），撤回后恢复可编辑。
/// 合法性只在 Core（<see cref="SimulRules.Validate"/>）判定。
/// </summary>
public sealed class SimulPrep
{
    private readonly SimulFlow _flow;

    internal SimulPrep(SimulFlow flow, PlayerId player)
    {
        _flow = flow;
        Player = player;
    }

    public PlayerId Player { get; }

    /// <summary>本回合是否已提交。</summary>
    public bool IsSubmitted => _flow.SeatOf(Player).Submitted;

    /// <summary>当前暂放（按暂放先后）。</summary>
    public ImmutableArray<Placement> Placements => [.. _flow.SeatOf(Player).Staged];

    /// <summary>私有手牌视图。</summary>
    public HandPrivateView Hand() => _flow.Hands.PrivateViewOf(Player);

    /// <summary>手牌所处阶段：整理（可整类弃牌）或征召（面板已打开）。</summary>
    public TurnPhase HandPhase => _flow.Hands.PhaseOf(Player);

    // ---------- 手牌与征召 ----------

    /// <summary>整理手牌：整类弃牌（只在打开征召面板之前）。</summary>
    public void Discard(PieceType type)
    {
        RequireEditable();
        _flow.Hands.Discard(Player, type);
    }

    /// <summary>打开征召面板：候选是回合开始时按结算顺位预抽好的那一组（K5），与打开先后无关。</summary>
    public RecruitPanelView EnterRecruit()
    {
        RequireEditable();
        return _flow.Hands.EnterRecruitPredrawn(Player);
    }

    /// <summary>当前征召面板；未打开时抛出。</summary>
    public RecruitPanelView Panel() => _flow.Hands.PanelOf(Player);

    /// <summary>选取一个候选位。</summary>
    public void Pick(int candidateIndex)
    {
        RequireEditable();
        _flow.Hands.Pick(Player, candidateIndex);
    }

    // ---------- 暂放 ----------

    /// <summary>加一枚暂放（同一格不能暂放两次）。</summary>
    public void Stage(Placement placement)
    {
        RequireEditable();
        List<Placement> staged = _flow.SeatOf(Player).Staged;
        if (staged.Any(p => p.Coord == placement.Coord))
        {
            throw new SiegeRuleException($"{placement.Coord.ToNotation()} 已有暂放。");
        }

        staged.Add(placement);
    }

    /// <summary>加一枚暂放（类型 + 落点）。</summary>
    public void Stage(Coord cell, PieceType type) => Stage(new Placement(cell, type));

    /// <summary>撤回该格的暂放。</summary>
    public void Unstage(Coord cell)
    {
        RequireEditable();
        _flow.SeatOf(Player).Staged.RemoveAt(IndexOf(cell));
    }

    /// <summary>换位：把 <paramref name="from"/> 的暂放挪到 <paramref name="to"/>（类型与改造照搬）。</summary>
    public void Move(Coord from, Coord to)
    {
        RequireEditable();
        List<Placement> staged = _flow.SeatOf(Player).Staged;
        int i = IndexOf(from);
        if (from != to && staged.Any(p => p.Coord == to))
        {
            throw new SiegeRuleException($"{to.ToNotation()} 已有暂放。");
        }

        staged[i] = staged[i] with { Coord = to };
    }

    /// <summary>换类型。</summary>
    public void ChangeType(Coord cell, PieceType type)
    {
        RequireEditable();
        List<Placement> staged = _flow.SeatOf(Player).Staged;
        int i = IndexOf(cell);
        staged[i] = staged[i] with { Type = type };
    }

    /// <summary>给该格的暂放挂上（或以 <c>null</c> 摘掉）地形改造。</summary>
    public void SetEdit(Coord cell, TerrainEdit? edit)
    {
        RequireEditable();
        List<Placement> staged = _flow.SeatOf(Player).Staged;
        int i = IndexOf(cell);
        staged[i] = staged[i] with { Edit = edit };
    }

    /// <summary>清空暂放。</summary>
    public void Clear()
    {
        RequireEditable();
        _flow.SeatOf(Player).Staged.Clear();
    }

    // ---------- 预检、提交、撤回 ----------

    /// <summary>预检：按回合开始盘面与本人此刻的暂放、手牌校验，返回全部不通过原因。零副作用。</summary>
    public SimulBatchCheck Check()
    {
        _flow.RequirePrep(Player);
        return _flow.Check(Player);
    }

    /// <summary>提交：校验通过才锁定，返回校验结果；不通过时保持可编辑、不消耗手牌。0 枚即本回合不部署（不撤销本回合征召）。</summary>
    public SimulBatchCheck Submit()
    {
        RequireEditable();
        SimulBatchCheck check = _flow.Check(Player);
        if (check.IsValid)
        {
            _flow.SeatOf(Player).Submitted = true;
        }

        return check;
    }

    /// <summary>撤回提交：回到可编辑状态（揭晓之前）。</summary>
    public void Withdraw()
    {
        _flow.RequirePrep(Player);
        if (!IsSubmitted)
        {
            throw new SiegeRuleException($"玩家 {Player} 尚未提交，无从撤回。");
        }

        _flow.SeatOf(Player).Submitted = false;
    }

    private void RequireEditable()
    {
        _flow.RequirePrep(Player);
        if (IsSubmitted)
        {
            throw new SiegeRuleException($"玩家 {Player} 已提交，本回合的手牌与暂放已锁定；先撤回提交再修改。");
        }
    }

    private int IndexOf(Coord cell)
    {
        int i = _flow.SeatOf(Player).Staged.FindIndex(p => p.Coord == cell);
        return i >= 0 ? i : throw new SiegeRuleException($"{cell.ToNotation()} 没有暂放。");
    }
}
