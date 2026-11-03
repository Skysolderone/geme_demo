using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Recruit;
using Siege.Core.Scoring;
using Siege.Presentation.Hud;
using Siege.Presentation.Text;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.Recruitment;

/// <summary>
/// 征募面板重设计（recruit-panel-redesign）：每个候选行带一句棋子效果、手牌已有数 / 新类型标记，已选与不可选分开表达；
/// 头部把「还可免费选取 N 枚」单独成行。文字全在 Presentation（<see cref="HudPanelRows"/> / <see cref="Labels"/>），Godot 只摆放。
/// </summary>
public class 征募面板行文案Tests
{
    private static HandPrivateView Hand(params (PieceType Type, int Carried, int Gained)[] entries) =>
        new(P0, entries.ToImmutableSortedDictionary(e => e.Type, e => new HandEntry(e.Carried, e.Gained)), TurnPhase.Recruit, TypeSlots: 5);

    [Fact]
    public void 十种棋子各有一句效果且取自计分常量()
    {
        string[] texts = Enum.GetValues<PieceType>().Select(Labels.PieceEffect).ToArray();
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(texts.Length, texts.Distinct().Count());

        // 基础军势写死在文案里（表现层不得调用规则入口 BasePower），这里替它守住与 Core 一致。
        Assert.Contains($"军势 {Core.Scoring.PieceEffects.BasePower(PieceType.Fortress)}，", Labels.PieceEffect(PieceType.Fortress), StringComparison.Ordinal);
        Assert.Contains($"军势 {Core.Scoring.PieceEffects.BasePower(PieceType.Basic)}，", Labels.PieceEffect(PieceType.Basic), StringComparison.Ordinal);
        Assert.Contains($"军势 {Core.Scoring.PieceEffects.BasePower(PieceType.Artisan)}；", Labels.PieceEffect(PieceType.Artisan), StringComparison.Ordinal);
        Assert.Contains($"+{Core.Scoring.PieceEffects.SynergyPerType}", Labels.PieceEffect(PieceType.Synergy), StringComparison.Ordinal);
        Assert.Contains($"+{Core.Scoring.PieceEffects.BannerPerRelic}", Labels.PieceEffect(PieceType.Bannerman), StringComparison.Ordinal);
        Assert.Contains($"+{Core.Scoring.PieceEffects.SentryPerForeignStone}", Labels.PieceEffect(PieceType.Sentry), StringComparison.Ordinal);
        Assert.Contains($"+{Core.Scoring.PieceEffects.BoundaryPerExclusiveCell}", Labels.PieceEffect(PieceType.Boundary), StringComparison.Ordinal);
        Assert.Contains("1.5", Labels.PieceEffect(PieceType.Multiplier), StringComparison.Ordinal);
        Assert.Contains("L×(L−1)", Labels.PieceEffect(PieceType.Line), StringComparison.Ordinal);
        Assert.Contains("搭桥", Labels.PieceEffect(PieceType.Artisan), StringComparison.Ordinal);
        Assert.Contains("棋子数 − 1", Labels.PieceEffect(PieceType.Chain), StringComparison.Ordinal);
    }

    [Fact]
    public void 可选行给出名字效果与手牌已有数()
    {
        var candidate = new RecruitCandidateView(Index: 0, PieceType.Line, IsPicked: false, IsSelectable: true, Reason: null);

        RecruitRowView row = HudPanelRows.RecruitRow(candidate, Hand((PieceType.Line, 1, 1)));

        Assert.Equal(0, row.Index);
        Assert.Equal(PieceType.Line, row.Type);
        Assert.Equal("[1] 连珠子", row.NameText);
        Assert.Equal(Labels.PieceEffect(PieceType.Line), row.DetailText);
        Assert.Equal("已有 ×2", row.HandText);
        Assert.Equal(RecruitRowState.Pickable, row.State);
    }

    [Fact]
    public void 手牌里没有的类型标为新类型并说明占槽()
    {
        var candidate = new RecruitCandidateView(Index: 2, PieceType.Artisan, IsPicked: false, IsSelectable: true, Reason: null);

        RecruitRowView row = HudPanelRows.RecruitRow(candidate, Hand((PieceType.Basic, 3, 0)));

        Assert.Equal("[3] 匠人", row.NameText);
        Assert.Equal("新类型 · 占 1 槽", row.HandText);
    }

    [Fact]
    public void 已选行是确认态而不是禁用态()
    {
        var candidate = new RecruitCandidateView(Index: 4, PieceType.Basic, IsPicked: true, IsSelectable: false, Reason: null);

        RecruitRowView row = HudPanelRows.RecruitRow(candidate, Hand((PieceType.Basic, 2, 1)));

        Assert.Equal(RecruitRowState.Picked, row.State);
        Assert.Equal("✓ 已选", row.StateText);
        Assert.Equal(Labels.PieceEffect(PieceType.Basic), row.DetailText);
        Assert.Equal("已有 ×3", row.HandText);
    }

    [Fact]
    public void 不可选行原样给出引擎的原因()
    {
        const string reason = "无可用类型槽：5 个类型槽已被占满，且手牌中没有旗手子；弃掉一整类即可选取。";
        var candidate = new RecruitCandidateView(Index: 1, PieceType.Bannerman, IsPicked: false, IsSelectable: false, Reason: reason);

        RecruitRowView row = HudPanelRows.RecruitRow(candidate, Hand((PieceType.Basic, 1, 0)));

        Assert.Equal(RecruitRowState.Blocked, row.State);
        Assert.Equal("不可选", row.StateText);
        Assert.Equal(reason, row.DetailText);
        Assert.Equal("新类型 · 占 1 槽", row.HandText);
    }

    [Fact]
    public void 头部把剩余免费选取数单独成行()
    {
        var panel = new RecruitPanelView(
            P0,
            [new RecruitCandidateView(0, PieceType.Basic, false, true, null), new RecruitCandidateView(1, PieceType.Line, true, false, null)],
            FreePickCount: 3,
            PicksMade: 1,
            TypeSlots: 5,
            OccupiedSlots: 4);

        RecruitHeaderView header = HudPanelRows.RecruitHeader(panel);

        Assert.Equal("征募", header.TitleText);
        Assert.Equal("还可免费选取 2 枚", header.PicksText);
        Assert.Equal("展示 2 枚候选 · 类型槽 4 / 5 · 选了就不能退回", header.MetaText);
        Assert.False(header.PicksExhausted);
    }

    [Fact]
    public void 免费选取用完时头部标出()
    {
        var panel = new RecruitPanelView(P0, [new RecruitCandidateView(0, PieceType.Basic, true, false, null)], FreePickCount: 1, PicksMade: 1, TypeSlots: 5, OccupiedSlots: 1);

        RecruitHeaderView header = HudPanelRows.RecruitHeader(panel);

        Assert.Equal("免费选取已用完", header.PicksText);
        Assert.True(header.PicksExhausted);
    }
}
