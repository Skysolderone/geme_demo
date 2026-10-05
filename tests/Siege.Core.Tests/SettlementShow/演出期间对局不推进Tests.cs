using System.Collections.Immutable;
using Siege.Core.Batch;
using Siege.Core.Match;
using Siege.Presentation.Show;
using static Siege.Core.Tests.SettlementShow.ShowFixtures;

namespace Siege.Core.Tests.SettlementShow;

/// <summary>
/// 规格：settlement-show —— Requirement: 演出期间对局不推进（turn-settlement-show 4.2）。
/// 「AI 连续小回合」「演出期间可平移镜头」两条是主循环行为（tasks 3.2 / 3.7 人工检查）；这里守「演出不影响对局结果」。
/// </summary>
public class 演出期间对局不推进Tests
{
    /// <summary>同一组操作：第 5 大回合起四名玩家各走一手，其中 P0 的第二手提掉 P1 的 B2。</summary>
    private static readonly string[][] Script =
    [
        ["F6", "E6"],   // P0
        ["B8"],         // P1
        ["H2"],         // P2
        [],             // P3 Pass
        ["C2", "B1"],   // P0：B2 被围（A2、B3 已有子）→ 提子
        [],             // P1 Pass
    ];

    private static MatchFlow Start() => MatchFixtures.Started().AtRound(5, MatchFixtures.All)
        .Stones(MatchFixtures.P1, "B2")
        .Stones(MatchFixtures.P0, "A2", "B3");

    [Fact]
    public void 演出不影响对局结果()
    {
        // 同一种子、同一组操作，分别以"开启演出（正常时长、逐帧推进到播完）"与"演出时长为 0"运行：每一步的公开快照逐项相同，
        // 且生成节拍 / 推进时间线前后对局指纹不变——演出只读快照，不改变任何对局状态。
        MatchFlow shown = Start();
        MatchFlow zero = Start();
        var beatKinds = new List<string>();
        foreach (string[] cells in Script)
        {
            string a = Step(shown, cells, ShowDuration.Normal, beatKinds);
            string b = Step(zero, cells, ShowDuration.Zero, null);
            Assert.Equal(a, b);
            Assert.Equal(PresentationFixtures.Fingerprint(shown), PresentationFixtures.Fingerprint(zero));
        }

        // 脚本确实覆盖了各种节拍：至少一步是"落子 → 提子 → 军势揭示 → 势力"（B2 被提；tiered-number-show 起落子的结算带军势揭示节拍）。
        // 按节拍类型断言而不是按个数：加入军势揭示后"落子 + 揭示 + 势力"也是 3 个，按个数数不出有没有提子。
        Assert.Contains("PlacementBeat,CaptureBeat,PowerRevealBeat,PowerBeat", beatKinds);
    }

    private static string Step(MatchFlow match, string[] cells, ShowDuration duration, List<string>? beatKinds)
    {
        MatchPublicView before = match.Publish();
        SettlementOutcome outcome = match.PlayTurn(cells);
        MatchPublicView after = match.Publish();
        string fingerprint = PresentationFixtures.Fingerprint(match);

        ImmutableArray<SettlementBeat> beats = SettlementBeats.Generate(before, after, outcome.CaptureRecord);
        beatKinds?.Add(string.Join(",", beats.Select(b => b.GetType().Name)));
        var timeline = new ShowTimeline(beats, duration);
        int frames = 0;
        while (!timeline.IsFinished)
        {
            timeline.Advance(16, fast: frames % 3 == 0);
            _ = timeline.Mask();
            Assert.True(++frames < 1000, "时间线没有播完");
        }

        Assert.Equal(fingerprint, PresentationFixtures.Fingerprint(match));
        return Snapshot(after);
    }

    /// <summary>公开快照的逐项投影：阶段、行动玩家、大回合、盘面、势力与名次、Pass 连击、结果。</summary>
    private static string Snapshot(MatchPublicView view) =>
        string.Join("|",
            view.Phase, view.Stage, view.CurrentPlayer?.ToString() ?? "-", view.MajorRound, view.BoardSerialized, view.PassStreak,
            view.Power is null ? "-" : string.Join(";", SettlementBeats.Readings(view.Power).Select(r => $"{r.Player}:{r.Total}:{r.Rank?.ToString() ?? "-"}")),
            view.Result?.Reason.ToString() ?? "-");
}
