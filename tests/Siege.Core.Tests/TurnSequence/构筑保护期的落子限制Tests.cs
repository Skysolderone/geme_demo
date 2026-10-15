using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;

namespace Siege.Core.Tests.TurnSequence;

/// <summary>规格：turn-sequence —— Requirement: 构筑保护期的落子限制（旧模式 3×3 出生区限制）。</summary>
/// <remarks>retire-legacy-rules：「保护期内规则照常」已迁到 <see cref="战场内合法落子范围Tests"/>（改名「构筑期内规则照常」）；余下一条为清单 (j)，第 2 组随整文件删除。</remarks>
public class 构筑保护期的落子限制Tests
{
    [Fact]
    public void 保护期内越区落子被拒()
    {
        // 设计文档 §4.2：第 2 大回合在自己出生区之外暂放 → 拒绝并说明违反当前合法落子范围。
        // 变异验证 M-T8：LegalRangeFor 恒返回全图 → 红 2（本测试 + 范围随大回合切换）。
        MatchFlow match = MatchFixtures.Started().AtRound(2, [MatchFixtures.P0, MatchFixtures.P1, MatchFixtures.P2, MatchFixtures.P3]);
        match.BeginTurn();
        match.EnterRecruit();
        StagedBatch batch = match.EnterDeploy();

        BatchFailure? failure = batch.Stage(TestMaps.At("E5"), PieceType.Basic);
        Assert.NotNull(failure);
        Assert.Equal(BatchFailureKind.OutOfLegalRange, failure.Kind);
        Assert.Contains("合法落子范围", failure.Message);
        Assert.Null(batch.Stage(TestMaps.At("B2"), PieceType.Basic));
    }
}
