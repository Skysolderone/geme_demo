using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Presentation.MapSelect;
using Siege.Presentation.Text;

namespace Siege.Core.Tests.Simul;

/// <summary>openspec/specs/simultaneous-round —— Requirement: 新局面板玩法（G5 / K4；A3 裁决 D1）。</summary>
/// <remarks>变异记录（A3-S1 实跑）：M23 切回经典不恢复尺寸 → 红 1（切回经典恢复原尺寸）。</remarks>
public sealed class 新局面板玩法Tests
{
    [Fact]
    public void 选新玩法锁定小图()
    {
        var model = new MapSelectModel(12345UL);
        Assert.Equal(GameMode.Classic, model.Mode);
        Assert.Equal(MapTier.Large, model.Tier);
        Assert.False(model.TierLocked);

        Assert.True(model.SelectMode(GameMode.Simul));
        Assert.Equal(GameMode.Simul, model.Mode);
        Assert.Equal(MapTier.Small, model.Tier);
        Assert.True(model.TierLocked);
        Assert.Equal(LinkMapId.Format(12345UL, model.Players, MapTier.Small), model.CurrentId);
        Assert.False(model.SelectMode(GameMode.Simul));   // 已是新玩法：标识不变
        Assert.Equal([GameMode.Classic, GameMode.Simul], MapSelectModel.ModeOptions);
    }

    [Fact]
    public void 新玩法下尺寸不可切()
    {
        var model = new MapSelectModel(7UL);
        model.SelectMode(GameMode.Simul);
        string id = model.CurrentId;

        Assert.False(model.SelectTier(MapTier.Large));
        Assert.Equal(MapTier.Small, model.Tier);
        Assert.Equal(id, model.CurrentId);
        Assert.Equal(Labels.SimulTierLocked, model.Notice);
        Assert.False(model.SelectTier(MapTier.Small));

        // 人数与种子照常可改，尺寸仍是小图。
        Assert.True(model.SelectPlayers(2));
        Assert.True(model.Reroll(99UL));
        Assert.Equal(LinkMapId.Format(99UL, 2, MapTier.Small), model.CurrentId);
    }

    [Fact]
    public void 切回经典恢复原尺寸()
    {
        var large = new MapSelectModel(1UL);
        large.SelectMode(GameMode.Simul);
        Assert.True(large.SelectMode(GameMode.Classic));
        Assert.Equal(MapTier.Large, large.Tier);
        Assert.False(large.TierLocked);

        var small = new MapSelectModel(1UL);
        small.SelectTier(MapTier.Small);
        Assert.False(small.SelectMode(GameMode.Simul));   // 本来就是小图：标识不变
        Assert.False(small.SelectMode(GameMode.Classic));
        Assert.Equal(MapTier.Small, small.Tier);
        Assert.True(small.SelectTier(MapTier.Large));   // 经典下尺寸照常可切
    }

    [Fact]
    public void 回退与确认带上玩法()
    {
        var model = new MapSelectModel(5UL);
        model.Accept();
        model.SelectMode(GameMode.Simul);
        model.RollBack("这张图没有生成出来");
        Assert.Equal(GameMode.Classic, model.Mode);
        Assert.Equal(MapTier.Large, model.Tier);

        model.SelectMode(GameMode.Simul);
        model.Accept();
        model.SelectMode(GameMode.Classic);
        Assert.Equal(LinkMapId.Format(5UL, model.Players, MapTier.Small), model.Confirm());
        Assert.Equal(GameMode.Simul, model.Mode);
        Assert.Throws<InvalidOperationException>(() => model.SelectMode(GameMode.Classic));
    }

    [Fact]
    public void 新玩法下按大图标识预选被拒()
    {
        var model = new MapSelectModel(5UL);
        model.SelectMode(GameMode.Simul);
        Assert.False(model.TrySelectId("link:5:4:l"));
        Assert.Equal(MapTier.Small, model.Tier);
        Assert.True(model.TrySelectId("link:6:3"));
        Assert.Equal("link:6:3", model.CurrentId);
    }

    [Fact]
    public void 新玩法文案不写棋盘棋子()
    {
        string[] texts =
        [
            Labels.GameModeName(GameMode.Classic), Labels.GameModeName(GameMode.Simul), Labels.SimulIntro, Labels.SimulTierLocked,
            Labels.SimulRoundTitle(3), Labels.SimulSubmitted(true), Labels.SimulSubmitted(false), Labels.SimulAiThinking,
            .. Enum.GetValues<Core.Simul.SimulEndReason>().Select(Labels.SimulEndReason),
            // 图形版新玩法界面文案（A3-S2）：常量全部 + 拼句函数各取一例。
            .. typeof(SimulTexts).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!),
            SimulTexts.HandRow("普通", 3), SimulTexts.SeatLine(new Core.Board.PlayerId(0), Core.Scoring.PlayerStatus.Active, true),
            SimulTexts.SeatLine(new Core.Board.PlayerId(1), Core.Scoring.PlayerStatus.Resigned, false), SimulTexts.RoundResolved(2, 1, 5, 0),
            SimulTexts.PlacingRow(1, new Core.Board.PlayerId(0), true), SimulTexts.ResultReason(Core.Simul.SimulEndReason.BoardFull, 9),
        ];
        Assert.True(texts.Length >= 30, "样本口径：新玩法文案条数过少");
        Assert.All(texts, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.DoesNotContain("棋盘", t, StringComparison.Ordinal);
            Assert.DoesNotContain("棋子", t, StringComparison.Ordinal);
        });
        Assert.Contains("3", Labels.SimulIntro, StringComparison.Ordinal);   // 部署上限取规则常量
    }
}
