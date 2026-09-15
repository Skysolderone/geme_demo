using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Relics;
using Siege.Presentation.Preview;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>规格：batch-preview —— Requirement: 未知信物在预演中只显示"将揭示"</summary>
public class 未知信物在预演中只显示将揭示Tests
{
    [Fact]
    public void 将揭示提示()
    {
        // 设计文档 §14.1 / D3：暂放使未知信物格 E5 首次进入覆盖范围 → 只标"将揭示"，不显示类型与强度。
        // 同种子两局只改 E5 的真实内容（军令 +1 / 兵站 +2）：富预演载荷与全部呈现数据逐字段一致，内容无从经由界面推断。
        // 变异验证 M-R1：BatchPreviewBuilder 的将揭示改为 `copy.Reveal(...).Where(e => e.Content.Type == RelicType.Command)` → 本测试红 1（兵站局将揭示为空，两局投影不同）。
        // 变异验证 M-R2：将揭示改为空集合 → 本测试红 1。
        (string Preview, string Shown) command = Stage(RelicFixtures.Command(), out MatchFlow first);
        (string Preview, string Shown) depot = Stage(RelicFixtures.Depot(2), out MatchFlow second);

        Assert.Equal(command.Preview, depot.Preview);
        Assert.Equal(command.Shown, depot.Shown);

        PreviewPresentation shown = first.World(P0).Preview()!;
        RevealHintView hint = Assert.Single(shown.RevealHints);
        Assert.Equal(("E5", "将揭示"), (hint.Coord.ToNotation(), hint.Text));
        Assert.Equal(["E5"], shown.Highlights.Where(h => h.Kind == HighlightKind.WillReveal).Select(h => h.Coord).Notations());
        Assert.DoesNotContain("军令", command.Shown);
        Assert.DoesNotContain("Command", command.Shown);
        Assert.DoesNotContain("兵站", depot.Shown);

        // 预演不揭示：正式账本仍未揭示；确认后揭示的恰好是预演标出的格
        RelicPublicState before = first.Publish().Relics.Single();
        Assert.False(before.IsRevealed);
        Assert.Null(before.Content);
        Assert.True(first.Confirm().Confirmed);
        Assert.Equal(["E5"], first.Relics.RevealEvents.Select(e => e.Coord).Notations());
        Assert.True(second.Confirm().Confirmed);
    }

    private static (string Preview, string Shown) Stage(RelicContent content, out MatchFlow match)
    {
        match = AiFixtures.Round5(relics: ("E5", content));
        StagedBatch batch = match.OpenDeploy();
        Assert.Null(batch.Stage(TestMaps.At("E4"), PieceType.Basic));
        Core.Preview.BatchPreview preview = match.PreviewCurrentBatch();
        Assert.Equal(["E5"], preview.WillReveal.Notations());
        return (Dump(preview), Dump(match.World(P0).Preview()));
    }
}
