using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Recruit;
using Siege.Sim.Config;
using Siege.Sim.Logging;
using Siege.Sim.Running;

namespace Siege.Core.Tests.SimulationHarness;

/// <summary>
/// 段 C（board-link-mode 3.0–3.3）Sim 测试的共用夹具：<c>link:1</c> 的跑局配置、走 <c>Program.Execute</c> 的命令行调用、
/// "四人四区"的对局种子（首轮恰两对、两次链接）与只会 Pass 的控制者。
/// </summary>
internal static class LinkSimFixtures
{
    internal const string MapId = "link:1";

    /// <summary><c>link:1</c> 上 4 人 Easy 的跑局配置（完整事件流、不抽样）。</summary>
    internal static RunConfig Config(int turnLimit, ulong seedStart = 1, int count = 1, AiDifficulty difficulty = AiDifficulty.Easy) =>
        SimFixtures.Config(count: count, seedStart: seedStart, turnLimit: turnLimit, difficulty: difficulty) with { MapId = MapId };

    /// <summary>
    /// 第一个"四名 AI 盲选到四块不同出生棋盘"的对局种子（1 起找）：第 3 大回合结束时首轮恰两对、两次链接（第 12 个小回合内）。
    /// 只建局不跑，找一次。
    /// </summary>
    internal static readonly Lazy<ulong> FourZoneSeed = new(() =>
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            MatchFlow match = MatchSession.Create(Config(turnLimit: 1), seed).Match;
            if (match.PlayerStates.Select(s => s.BirthZone).Distinct().Count() == 4)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("种子 1–499 里没有四人四区的开局。");
    });

    /// <summary>四人四区种子上跑到第 3 大回合结束后一个小回合（两次链接已发生）的日志。</summary>
    internal static readonly Lazy<MatchLog> TwoLinkLog = new(() => MatchSession.Create(Config(turnLimit: 13), FourZoneSeed.Value).Run());

    /// <summary>经 <c>Program.Execute</c> 跑一次命令行，捕获标准输出与标准错误。</summary>
    internal static (int Code, string Out, string Error) RunMain(Func<ulong>? mapSeedSource, TextReader? input, params string[] args)
    {
        var output = new StringWriter();
        var err = new StringWriter();
        TextWriter savedOut = Console.Out;
        TextWriter savedErr = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(err);
            int code = Siege.Sim.Program.Execute(
                args, mapSeedSource ?? (() => throw new InvalidOperationException("本测试不应随机取地图种子。")), input, input is null ? null : output);
            return (code, output.ToString(), err.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    /// <summary>日志里某类事件。</summary>
    internal static List<LogEvent> EventsOf(this MatchLog log, string type) => [.. log.Events.Where(e => e.Type == type)];

    /// <summary>
    /// 小样本日志（段 C 后半 3.5 的分析测试用，不跑批量）：在 <c>link:1</c> 上按 <paramref name="zones"/> 开局、挂脚本固定配对，经 <see cref="MatchSession"/> 跑到终局——
    /// 前 <paramref name="stoneRounds"/> 个大回合每人落 1 子，此后一律 Pass（每个多人战场在确定的大回合结算、落败者势力 &gt; 0、Pass 时手里仍有子）。
    /// </summary>
    internal static (MatchFlow Match, MatchLog Log) PlayScripted(int[] zones, int stoneRounds = 1, IPairingSource? source = null)
    {
        MatchFlow match = LinkModeFixtures.Started(zones, source ?? new ScriptedPairingSource());
        // 首部配置写明本局的内容集（跑局层建局由 ResolvedFor 落成；ForMatch 不落成，缺项会被分析端按 v1 读，与对局实际的 v2 不符）。
        MatchSession session = MatchSession.ForMatch(match, Config(turnLimit: 200) with { ContentSet = match.ContentSet });
        foreach (PlayerId p in match.Players)
        {
            session.SetController(p, new StoneThenPass(() => match.MajorRound <= stoneRounds));
        }

        MatchLog log = session.Run();
        Assert.False(log.IsFailed, log.Failure?.Message);
        Assert.NotNull(match.Result);
        return (match, log);
    }

    /// <summary>条件成立时落 1 枚普通子（合法范围内按坐标序第一个能暂放的空格），否则 Pass。</summary>
    internal sealed class StoneThenPass(Func<bool> placeNow) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            if (!placeNow())
            {
                return;
            }

            foreach (Coord cell in batch.Context.LegalRange.Order())
            {
                if (batch.Stage(cell, PieceType.Basic) is null)
                {
                    return;
                }
            }
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }

    /// <summary>
    /// 脚本化的一个座位（link-map-terrain 裁决 T1 = A：样本下界改为不依赖 AI 走法）：在 <paramref name="byRound"/> 列出的大回合按脚本暂放（坐标、类型、可选改造），
    /// 其余大回合交给 <paramref name="fallback"/>（缺省为 Pass）。脚本暂放失败（被拒）时整批 Pass，测试会因样本缺失而响亮失败。
    /// </summary>
    internal sealed class ScriptedSeat(
        MatchFlow match,
        IReadOnlyDictionary<int, (Coord Cell, PieceType Type, TerrainEdit? Edit)[]> byRound,
        ITurnController? fallback = null) : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow) => fallback?.OrganizeHand(hand, overflow);

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
            if (!byRound.ContainsKey(match.MajorRound))
            {
                fallback?.Recruit(hand, panel);
            }
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
            if (byRound.TryGetValue(match.MajorRound, out (Coord Cell, PieceType Type, TerrainEdit? Edit)[]? script))
            {
                foreach ((Coord cell, PieceType type, TerrainEdit? edit) in script)
                {
                    if (batch.Stage(cell, type, edit) is { } failure)
                    {
                        throw new InvalidOperationException($"脚本暂放 {cell.ToNotation()}:{type} 被拒：{failure.Message}");
                    }
                }

                return;
            }

            fallback?.Deploy(batch, rehearse);
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => fallback?.OnRejected(batch, failure) ?? false;
    }

    /// <summary>只会 Pass 的控制者：部署阶段什么都不暂放，确认即 Pass。</summary>
    internal sealed class PassController : ITurnController
    {
        public void OrganizeHand(PlayerHandAccess hand, int overflow)
        {
        }

        public void Recruit(PlayerHandAccess hand, RecruitPanelView panel)
        {
        }

        public void Deploy(StagedBatch batch, Func<RehearsalResult> rehearse)
        {
        }

        public bool OnRejected(StagedBatch batch, BatchFailure failure) => false;
    }
}
