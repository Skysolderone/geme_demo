using System.Collections.Immutable;
using System.Numerics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Core.Scoring;

namespace Siege.Core.Simul;

/// <summary>新玩法（同时决策，simul-a）的顶层阶段：插旗 → 进行中 → 已结束。</summary>
public enum SimulPhase
{
    FlagPlanting,
    InProgress,
    Ended,
}

/// <summary>进行中的回合内阶段：全员准备（整理手牌、征召、暂放、提交）→ 统一揭晓与结算（<see cref="SimulFlow.ResolveRound"/> 内部瞬时）。</summary>
public enum SimulStage
{
    Prep,
    Resolving,
}

/// <summary>新玩法的终局原因（v0.1 §10，按判定先后排列）。</summary>
public enum SimulEndReason
{
    /// <summary>参赛中（未认输）的玩家不超过 1 人。</summary>
    LastPlayerStanding,

    /// <summary>占满：结算后不存在非活形锁定的空可部署格。</summary>
    BoardFull,

    /// <summary>本回合无人提交任何单位。</summary>
    NoDeployRound,

    /// <summary>达到大回合上限（兜底）。</summary>
    RoundCap,
}

/// <summary>新玩法的对局配置。开局固定。</summary>
public sealed record SimulOptions
{
    public static readonly SimulOptions Default = new();

    /// <summary>大回合上限（0 = 不启用；未给即小图缺省 <see cref="MatchOptions.DefaultRoundCap"/>）。</summary>
    public int? RoundCap { get; init; }

    /// <summary>匠人征召权重（沿用现有缺省）。</summary>
    public int ArtisanWeight { get; init; } = RecruitWeights.DefaultArtisanWeight;

    /// <summary>对局内容集（沿用现有缺省）。</summary>
    public ContentSet ContentSet { get; init; } = ContentSets.Default;

    /// <summary>计分规则版本（沿用现有缺省）。</summary>
    public ScoringVersion ScoringVersion { get; init; } = ScoringVersions.Default;
}

/// <summary>部署校验的不通过原因类别（J2-3）。</summary>
public enum SimulRejectKind
{
    /// <summary>不在本人的合法部署范围内（前 3 回合只能部署在出生区，或不是可部署格）。</summary>
    OutOfRange,

    /// <summary>回合开始时该格已有单位。</summary>
    Occupied,

    /// <summary>该格是活形内部格（已锁定），双方都不能部署（G2）。</summary>
    Locked,

    /// <summary>同一格暂放了两次。</summary>
    DuplicateCell,

    /// <summary>超过本回合部署上限。</summary>
    OverLimit,

    /// <summary>某类型超过手牌库存。</summary>
    OverStock,

    /// <summary>八邻禁落：同一区内本人两个落点相邻（含斜邻）。</summary>
    Crowded,

    /// <summary>非匠人带了地形改造。</summary>
    EditNotArtisan,

    /// <summary>地形改造不合法（按回合开始时的地形与改造范围判定）。</summary>
    EditIllegal,

    /// <summary>本人批次里两份改造指向同一目标。</summary>
    EditDuplicate,

    /// <summary>挂带入信物：段 A 的新玩法关闭信物（K1）。</summary>
    RelicNotAllowed,
}

/// <summary>一条校验不通过的原因：类别、涉及的格（批次级原因为 <c>null</c>）与面向玩家的文字。</summary>
public sealed record SimulBatchIssue(SimulRejectKind Kind, Coord? Cell, string Message)
{
    public override string ToString() => Message;
}

/// <summary>一次部署校验的结果。<see cref="IsValid"/> 为真才能提交。</summary>
public sealed record SimulBatchCheck(ImmutableArray<SimulBatchIssue> Issues)
{
    public bool IsValid => Issues.IsEmpty;

    /// <summary>第一条原因的文字；通过时为 <c>null</c>。</summary>
    public string? FirstReason => Issues.IsEmpty ? null : Issues[0].Message;

    public bool Has(SimulRejectKind kind) => Issues.Any(i => i.Kind == kind);
}

/// <summary>揭晓时公开的一名玩家的批次（0 枚即本回合不部署）。</summary>
public sealed record SimulRevealedBatch(PlayerId Player, ImmutableArray<Placement> Placements);

/// <summary>冲突中一名投入者的战力构成：基础战力 + 回合开始时与该格四邻相接（气边）的本人单位数。</summary>
public sealed record SimulConflictEntrant(PlayerId Player, PieceType Type, int BasePower, int Support)
{
    public int Power => BasePower + Support;
}

/// <summary>一次掷骰：玩家与点数（1–6）。</summary>
public readonly record struct SimulDieRoll(PlayerId Player, int Value)
{
    public override string ToString() => $"{Player}:{Value}";
}

/// <summary>一格冲突的完整记录：投入者（按玩家编号升序）、各轮掷骰（没有并列时为空）与胜者。</summary>
public sealed record SimulConflict(Coord Cell, ImmutableArray<SimulConflictEntrant> Entrants, ImmutableArray<ImmutableArray<SimulDieRoll>> Rolls, PlayerId Winner)
{
    public SimulConflictEntrant EntrantOf(PlayerId player) =>
        Entrants.FirstOrDefault(e => e.Player == player) ?? throw new KeyNotFoundException($"冲突格 {Cell.ToNotation()} 没有投入者 {player}。");

    /// <summary>面向玩家的一句话："P0 3 : P1 2，P0 胜"；掷骰时附"掷骰 3 : 3 → 5 : 2"。</summary>
    public string Describe()
    {
        string powers = string.Join(" : ", Entrants.Select(e => $"{e.Player} {e.Power}"));
        string dice = Rolls.IsEmpty ? string.Empty : "，掷骰 " + string.Join(" → ", Rolls.Select(r => string.Join(" : ", r.Select(d => d.Value))));
        return $"{Cell.ToNotation()}：{powers}{dice}，{Winner} 胜";
    }
}

/// <summary>落到正式盘面上的一枚单位。</summary>
public readonly record struct SimulLanded(PlayerId Player, Placement Placement);

/// <summary>作废的地形改造及原因。</summary>
public enum SimulEditVoidReason
{
    /// <summary>所属部署在冲突中落败（K2）。</summary>
    ConflictLost,

    /// <summary>与结算顺位更靠前者的改造指向同一目标（K2）。</summary>
    DuplicateTarget,
}

/// <summary>一份地形改造的去向：生效或作废。</summary>
public sealed record SimulEditOutcome(PlayerId Player, Coord ArtisanCell, TerrainEdit Edit, SimulEditVoidReason? VoidReason)
{
    public bool Applied => VoidReason is null;
}

/// <summary>
/// 围杀的一步（Q03 / J2-7）：<see cref="By"/> 是执行这一步的顺位玩家（提走全部非本人的无气整体）；
/// 为 <c>null</c> 即顺位全部走完后的孤立自绝（移除仍然无气的整体，不论属于谁）。
/// </summary>
public sealed record SimulCaptureStep(PlayerId? By, ImmutableArray<CapturedStone> Removed)
{
    public bool IsIsolation => By is null;
}

/// <summary>新玩法一名玩家的势力：现有势力算法的结果，加上"活形内部空格算所有者领地"的修正（Q06 / J2-8）。</summary>
/// <param name="Player">玩家。</param>
/// <param name="Status">参赛中 / 已认输。</param>
/// <param name="Territory">领地格（未经荒漠过滤）：现有独占空格，去掉别人的活形内部格，再并上本人的活形内部格；坐标序。</param>
/// <param name="ScoredTerritory">计分领地格（荒漠不计，同 <see cref="PowerCalculator.ScoresTerritory"/>）。</param>
/// <param name="LockedCells">本人活形内部格（已锁定），坐标序。</param>
/// <param name="GroupScore">全部整体的军势之和（现有算法原样）。</param>
/// <param name="Base">现有势力算法对这名玩家的原始明细。</param>
public sealed record SimulPlayerPower(
    PlayerId Player,
    PlayerStatus Status,
    ImmutableArray<Coord> Territory,
    ImmutableArray<Coord> ScoredTerritory,
    ImmutableArray<Coord> LockedCells,
    BigInteger GroupScore,
    PlayerPower Base)
{
    /// <summary>总势力 = 计分领地格数 + 军势之和。</summary>
    public BigInteger Total => ScoredTerritory.Length + GroupScore;
}

/// <summary>新玩法的势力快照（全量重算的结果）。</summary>
public sealed record SimulPowerSnapshot(PowerSnapshot Base, ImmutableArray<SimulPlayerPower> Players)
{
    public SimulPlayerPower Of(PlayerId player) =>
        Players.FirstOrDefault(p => p.Player == player) ?? throw new KeyNotFoundException($"势力快照中没有玩家 {player}。");

    /// <summary>竞争名次（并列共享，1、1、3），只在参赛中玩家之间排；已认输为 <c>null</c>。</summary>
    public int? RankOf(PlayerId player)
    {
        SimulPlayerPower me = Of(player);
        if (me.Status != PlayerStatus.Active)
        {
            return null;
        }

        return 1 + Players.Count(p => p.Status == PlayerStatus.Active && p.Total > me.Total);
    }
}

/// <summary>新玩法的终局结果：原因、所在回合、名次（<see cref="FinalStandings"/> 的竞争名次）、按种子打破并列后的严格名次与胜者。</summary>
public sealed record SimulResult(
    SimulEndReason Reason,
    int Round,
    ImmutableArray<Standing> Standings,
    ImmutableArray<PlayerId> Placing,
    ImmutableArray<PlayerId> Winners)
{
    public Standing Of(PlayerId player) =>
        Standings.FirstOrDefault(s => s.Player == player) ?? throw new KeyNotFoundException($"终局名次中没有玩家 {player}。");
}

/// <summary>一个回合的结算报告（J3）：顺位、揭晓的批次、冲突、落下、改造去向、围杀分步、本回合势力与终局原因。</summary>
public sealed record SimulRoundReport(
    int Round,
    ImmutableArray<PlayerId> Order,
    ImmutableArray<SimulRevealedBatch> Batches,
    ImmutableArray<SimulConflict> Conflicts,
    ImmutableArray<SimulLanded> Landed,
    ImmutableArray<SimulEditOutcome> Edits,
    ImmutableArray<SimulCaptureStep> Captures,
    SimulPowerSnapshot Power,
    SimulEndReason? EndReason)
{
    /// <summary>本回合提交的单位总数（含冲突落败的）。</summary>
    public int SubmittedCount => Batches.Sum(b => b.Placements.Length);

    /// <summary>本回合掷骰的总次数。</summary>
    public int DiceRolled => Conflicts.Sum(c => c.Rolls.Sum(r => r.Length));

    /// <summary>本回合被移除的单位（按步骤先后）。</summary>
    public IEnumerable<CapturedStone> Removed => Captures.SelectMany(s => s.Removed);
}

/// <summary>公开视图里的一名玩家：参赛状态、是否已提交（不含暂放内容）、手牌类型集合。</summary>
public sealed record SimulSeatView(PlayerId Player, PlayerStatus Status, bool Submitted, ImmutableSortedSet<PieceType> HandTypes);

/// <summary>
/// 新玩法的公开视图（J3）：盘面副本、活形分析、势力、顺位、每人提交状态与手牌类型集合、上一回合报告。
/// 结构上没有任何人的暂放内容与手牌数量。
/// </summary>
public sealed record SimulPublicView(
    int Round,
    SimulPhase Phase,
    ImmutableArray<PlayerId> Order,
    GameBoard Board,
    LifeShapeReport LifeShape,
    SimulPowerSnapshot? Power,
    ImmutableArray<SimulSeatView> Seats,
    SimulRoundReport? LastReport,
    SimulResult? Result);
