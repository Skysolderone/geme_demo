using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Presentation.Hand;

/// <summary>
/// 本机玩家的一次征募阶段（recruit-panel-collapse D1）：大回合序号 + 本大回合内的小回合序号（本机玩家在行动顺序中的位置，1 起）。
/// 每名玩家每个大回合只行动一次，二者合起来即可区分"同一次征募里的刷新"与"新的一次征募"。
/// </summary>
public readonly record struct RecruitPhaseKey(int MajorRound, int MinorRound)
{
    /// <summary>当前是否处在 <paramref name="me"/> 的征募阶段；是则给出该阶段的标识，否则为 <c>null</c>。只读公开快照。</summary>
    public static RecruitPhaseKey? Of(MatchPublicView view, PlayerId me)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Phase != MatchPhase.InProgress || view.Stage != TurnStage.Recruit || view.CurrentPlayer != me)
        {
            return null;
        }

        return new RecruitPhaseKey(view.MajorRound, view.ActionOrder.IndexOf(me) + 1);
    }
}

/// <summary>征募面板的呈现：面板内容（与账本里的面板同一份）、是否收起、收起时提示条的文案。</summary>
public sealed record RecruitCenterView(RecruitPanelView Panel, bool IsCollapsed, string BarText);

/// <summary>
/// 征募面板的收起 / 展开（hand-info-panel「征募面板可收起」，recruit-panel-collapse D1–D4）。纯表现层状态：
/// 不引用任何对局对象、不进对局状态 / 存档 / 日志；图形版只读它决定画面板还是顶部提示条。
/// </summary>
/// <remarks>
/// 完整语义都在这里（testing.md「共用同一套状态机最容易在最外层被复写」）：V 键、面板上的"收起"按钮、点提示条都调 <see cref="Toggle"/>，
/// 引擎侧不自己拼"在不在征募阶段 / 手牌面板开没开"的判断。
/// </remarks>
public sealed class RecruitPanelCollapse
{
    private RecruitPhaseKey? _phase;

    /// <summary>当前是否收起。</summary>
    public bool IsCollapsed { get; private set; }

    /// <summary>
    /// 喂入当前的征募阶段（不在本机征募阶段传 <c>null</c>）。阶段变了即重置为展开——每个新的征募阶段面板都以展开出现，
    /// 收起状态不跨小回合保留；同一阶段内反复刷新（选取、推屏、缩放、开信息层）不改变收起状态。
    /// </summary>
    public void Sync(RecruitPhaseKey? phase)
    {
        if (phase != _phase)
        {
            _phase = phase;
            IsCollapsed = false;
        }
    }

    /// <summary>
    /// 收起 / 展开（V 键、"收起"按钮、点提示条）。只在本机征募阶段、手牌信息面板关着时生效——手牌面板优先级不变（D4），
    /// 它开着时征募面板本就不显示，此时翻转一个看不见的状态只会让玩家关掉手牌面板后困惑。返回是否切换了。
    /// </summary>
    public bool Toggle(bool handPanelOpen)
    {
        if (_phase is null || handPanelOpen)
        {
            return false;
        }

        IsCollapsed = !IsCollapsed;
        return true;
    }

    /// <summary>征募面板的呈现。面板内容原样透传：收起 / 展开 MUST NOT 改变展示的候选、已选取与剩余选取数。</summary>
    public RecruitCenterView Present(RecruitPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return new RecruitCenterView(panel, IsCollapsed, BarText(panel));
    }

    /// <summary>收起时顶部提示条的文案（D3）。</summary>
    public static string BarText(RecruitPanelView panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return $"征募（已收起）· 按 V 展开 · 展示 {panel.ShowCount} · 还可免费选取 {panel.PicksRemaining}";
    }
}
