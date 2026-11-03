using System.Reflection;
using System.Text.RegularExpressions;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Presentation.Hand;
using Siege.Presentation.Hud;
using Siege.Presentation.Text;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.HandInfoPanel;

/// <summary>
/// hand-effects（recruit-panel-redesign 裁决 8 的后续）：手牌处也能看到每类棋子的效果一句，口径与征募面板候选行第二行同一份
/// <see cref="Labels.PieceEffect"/>。文字在 Presentation 的行视图里产生一次（<see cref="OwnHandRowView.EffectText"/> /
/// <see cref="OpponentHandRowView.EffectText"/>），左下手牌面板经 <see cref="HudPanelRows.HandRow"/> 透传，Godot 只摆放。
/// </summary>
public class 手牌效果说明Tests
{
    [Fact]
    public void 自己区域每行带效果一句且与征募面板同口径()
    {
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));

        OwnHandAreaView own = match.World(P0).HandPanel().Own;

        Assert.Equal([(PieceType.Basic, 6), (PieceType.Fortress, 1)], own.Rows.Select(r => (r.Type, r.Count)));
        Assert.All(own.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.EffectText)));
        Assert.Equal(own.Rows.Select(r => Labels.PieceEffect(r.Type)), own.Rows.Select(r => r.EffectText));

        // 与征募面板候选行第二行逐字相同：同一类棋子在两处读到的是同一句。
        HandPrivateView hand = match.Hands.AccessFor(P0).PrivateView();
        var candidate = new RecruitCandidateView(Index: 0, PieceType.Fortress, IsPicked: false, IsSelectable: true, Reason: null);
        Assert.Equal(HudPanelRows.RecruitRow(candidate, hand).DetailText, own.Rows.Single(r => r.Type == PieceType.Fortress).EffectText);
    }

    [Fact]
    public void 敌方区域按类型带效果且仍然只有类型()
    {
        MatchFlow match = AiFixtures.Round5();
        match.Debug.SeedHand(P0, (PieceType.Basic, 6), (PieceType.Fortress, 1));

        OpponentHandAreaView seen = match.World(P1).HandPanel().Opponents.Single(o => o.Player == P0);

        Assert.Equal(seen.Types, seen.Rows.Select(r => r.Type));
        Assert.Equal(seen.TypeNames, seen.Rows.Select(r => r.Name));
        Assert.Equal(seen.Types.Select(Labels.PieceEffect), seen.Rows.Select(r => r.EffectText));

        // 敌方行只有类型、名称与效果三项，没有数量；闭包里依旧没有任何私有类型。
        Assert.Equal(["EffectText", "Name", "Type"], typeof(OpponentHandRowView).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name).Order());
        Assert.All(typeof(OpponentHandRowView).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            p => Assert.True(p.PropertyType == typeof(PieceType) || p.PropertyType == typeof(string), p.Name));
        Assert.Empty(PrivateLeaks(typeof(OpponentHandAreaView)));
    }

    [Fact]
    public void 手牌面板行透传效果_弃牌阶段也带()
    {
        var row = new OwnHandRowView(PieceType.Line, "连珠子", 2, 0, "连珠子 ×2", Labels.PieceEffect(PieceType.Line));

        HandRowView normal = HudPanelRows.HandRow(row, discard: false);
        HandRowView discard = HudPanelRows.HandRow(row, discard: true);

        Assert.Equal(Labels.PieceEffect(PieceType.Line), normal.EffectText);
        Assert.Equal(Labels.PieceEffect(PieceType.Line), discard.EffectText);
        Assert.Equal("弃掉整类：连珠子", discard.NameText);
    }

    /// <summary>
    /// 源码扫描守门（Godot 工程不在解决方案里，对 IL 守门隐身）：左下手牌面板与 H 键面板两处都摆放了视图给的效果文字，
    /// 并且 Godot 侧不自己取文案（不调 <c>Labels.PieceEffect</c>），文案只在 Presentation 产生一次。
    /// </summary>
    [Fact]
    public void 图形版两处面板都摆放效果文字且不自取文案()
    {
        string hud = GodotScriptCode("Hud.cs");
        Assert.True(hud.Length >= 30_000, $"Hud.cs 只有 {hud.Length} 字符");
        Assert.DoesNotContain("PieceEffect(", hud, StringComparison.Ordinal);

        string hand = MethodBody(hud, "private void RefreshHand(MatchSession session, ViewerWorld world)");
        Assert.True(hand.Length >= 800, $"RefreshHand 只有 {hand.Length} 字符");
        Assert.Matches(@"Ui\.Text\(view\.EffectText, Ui\.MutedText, UiTheme\.Caption\)", hand);
        Assert.Matches(@"HudPanelRows\.HandRow\(", hand);

        string info = MethodBody(hud, "private void BuildHandInfo(HandInfoPanelView panel)");
        Assert.True(info.Length >= 600, $"BuildHandInfo 只有 {info.Length} 字符");
        // 我方逐行、敌方逐类各一处把 EffectText 交给摆放辅助方法。
        Assert.Equal(2, Regex.Matches(info, @"row\.EffectText").Count);
        Assert.Matches(@"foreach \(OpponentHandRowView row in opponent\.Rows\)", info);
        Assert.Matches(@"Ui\.Text\(effectText, Ui\.MutedText, UiTheme\.Caption, wrap: true\)", MethodBody(hud, "private static HBoxContainer HandInfoRow(string leadText, string effectText, Color ink)"));
    }
}
