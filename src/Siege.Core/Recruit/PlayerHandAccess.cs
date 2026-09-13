using Siege.Core.Board;

namespace Siege.Core.Recruit;

/// <summary>
/// 某玩家本人的私有访问句柄（design.md D6）：私有手牌视图、征募面板、选取与弃牌操作都绑定在 <see cref="Player"/> 上。
/// 流程层只把它交给该玩家的控制者；其他玩家与对战 AI 只拿得到 <see cref="HandLedger.PublicView"/>。
/// </summary>
public sealed class PlayerHandAccess
{
    private readonly HandLedger _ledger;

    internal PlayerHandAccess(HandLedger ledger, PlayerId player)
    {
        _ledger = ledger;
        Player = player;
    }

    /// <summary>句柄绑定的玩家。</summary>
    public PlayerId Player { get; }

    /// <summary>私有手牌视图：类型 → 两段账、槽位占用、本轮新增未提交数。</summary>
    public HandPrivateView PrivateView() => _ledger.PrivateViewOf(Player);

    /// <summary>整理手牌阶段：主动弃掉一整类棋子以腾出槽位，不返还任何资源。</summary>
    public void Discard(PieceType type) => _ledger.Discard(Player, type);

    /// <summary>按指定数量弃牌：数量必须恰为该类型的全部持有量，否则拒绝并说明弃牌必须整类进行。</summary>
    public void Discard(PieceType type, int count) => _ledger.Discard(Player, type, count);

    /// <summary>结束整理、进入征募阶段并生成面板。槽位超限未解除时拒绝（阻断式，design.md D7）。</summary>
    public RecruitPanelView EnterRecruit() => _ledger.EnterRecruit(Player);

    /// <summary>当前征募面板（含每个候选位的可选性与不可选原因）。不在征募阶段时抛出。</summary>
    public RecruitPanelView Panel() => _ledger.PanelOf(Player);

    /// <summary>选取一个候选位：受免费选取数与类型槽约束；选取数是上限，可少选或不选。</summary>
    public void Pick(int candidateIndex) => _ledger.Pick(Player, candidateIndex);
}
