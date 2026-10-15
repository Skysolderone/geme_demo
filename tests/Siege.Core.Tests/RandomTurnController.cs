using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Recruit;

namespace Siege.Core.Tests;

/// <summary>
/// 占位随机策略（种子驱动）：整理时随机弃到合法，征募随机选，部署随机挑合法空格；用 <see cref="RandomStream"/>，不用 <c>Random.Shared</c>。
/// AI 决策逻辑属于 add-heuristic-ai；这里只为端到端跑通流程。
/// retire-legacy-rules 2.2：原与 9×9 流程夹具同文件，夹具删除时原样迁出为独立文件（流程回归类 3 个文件在用）。
/// </summary>
internal sealed class RandomTurnController(RandomStream rng) : ITurnController
{
    public void OrganizeHand(PlayerHandAccess hand, int overflow)
    {
        for (int i = 0; i < overflow; i++)
        {
            PieceType[] types = [.. hand.PrivateView().Types];
            hand.Discard(types[rng.NextInt(types.Length)]);
        }
    }

    public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
    {
        int picks = rng.NextInt(panel.PicksRemaining + 1);
        for (int i = 0; i < picks; i++)
        {
            int[] selectable = [.. hand.Panel().Candidates.Where(c => c.IsSelectable).Select(c => c.Index)];
            if (selectable.Length == 0)
            {
                return;
            }

            hand.Pick(selectable[rng.NextInt(selectable.Length)]);
        }
    }

    public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
    {
        Coord[] cells = [.. batch.Context.LegalRange.Where(c => batch.Board[c].IsPlayableEmpty).Order()];
        PieceType[] types = [.. batch.Context.Stock.Where(kv => kv.Value > 0).Select(kv => kv.Key)];
        if (cells.Length == 0 || types.Length == 0)
        {
            return;
        }

        int want = rng.NextInt(batch.Context.DeployLimit + 1);
        int attempts = 0;
        while (batch.Count < want && attempts++ < 12)
        {
            Coord cell = cells[rng.NextInt(cells.Length)];
            PieceType type = types[rng.NextInt(types.Length)];
            if (batch.Stage(cell, type) is not null)
            {
                continue;
            }

            if (!rehearse().IsLegal)
            {
                batch.Unstage(cell);
            }
        }
    }

    public bool OnRejected(StagedBatch batch, BatchFailure failure)
    {
        if (batch.Count == 0)
        {
            return false;
        }

        batch.Unstage(batch.Placements[^1].Coord);
        return true;
    }
}
