using System.Collections.Immutable;
using Siege.Core.Board;

namespace Siege.Core.Batch;

/// <summary>
/// 批次层对上游的全部输入，由调用方在每个小回合提供。本层<b>不生产</b>其中任何一项：
/// 部署上限来自效果快照（军令信物），合法落子范围与改造范围来自流程层（open-map：构筑期为锁定的出生棋盘，第 4 大回合起为全图），
/// 手牌库存来自征募层。见 openspec/changes/add-batch-deployment/design.md「接口契约」。
/// </summary>
public sealed record BatchContext
{
    private readonly int _deployLimit;

    /// <summary>当前行动玩家。</summary>
    public required PlayerId Player { get; init; }

    /// <summary>本小回合的部署上限。基础 3，可由军令信物提高；本层 MUST NOT 施加全局硬上限。</summary>
    public required int DeployLimit
    {
        get => _deployLimit;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(DeployLimit), value, "部署上限不得为负。");
            }

            _deployLimit = value;
        }
    }

    /// <summary>当前允许落子的格集合（<c>PublicRules.LegalRange</c>）。构筑期为玩家锁定的出生棋盘的可落子格，第 4 大回合起为全图，都已扣除本人禁入格。</summary>
    public required IReadOnlySet<Coord> LegalRange { get; init; }

    /// <summary>
    /// 改造范围（open-map D4，<c>PublicRules.EditScope</c>）：允许作为改造目标的格集合——构筑期为自己的出生棋盘，第 4 大回合起为全图；不扣禁入格。
    /// <c>null</c> = 不限（不经对局流程构造的上下文：单元测试、旧调用点与引入之前相同）。
    /// </summary>
    public IReadOnlySet<Coord>? EditScope { get; init; }

    /// <summary>手牌库存：按棋子类型的数量映射。缺失的类型视为 0。</summary>
    public required IReadOnlyDictionary<PieceType, int> Stock { get; init; }

    /// <summary>
    /// 本人尚未挂出的带入件（relic-redesign R14）：带入清单下标 → 种类与保险。只在本人的部署阶段构造（私有，R22）；
    /// <c>null</c> 或空 = 没有可挂的带入件（不经对局流程构造的上下文与引入之前相同）。
    /// </summary>
    public IReadOnlyDictionary<int, PieceRelics.CarriedRelic>? Carried { get; init; }

    /// <summary>本批挂出的带入件从这个本局编号起按批内顺序分配（对局的编号游标，刷新与挂出共用，design.md D1）。</summary>
    public int NextRelicId { get; init; }

    /// <summary>某类型的库存数量，缺失视为 0。</summary>
    public int StockOf(PieceType type) => Stock.TryGetValue(type, out int count) ? count : 0;

    /// <summary>"全图"合法落子范围：棋盘上的全部格子。地形是否可落子由预演第 1 步单独判定。</summary>
    public static IReadOnlySet<Coord> EntireBoard(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.AllCoords().ToImmutableHashSet();
    }
}
