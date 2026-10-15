using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 一个战场进入决赛倒计时的记录（final-countdown，match-telemetry「决赛倒计时的记录」；存档往返）：序号（从 1 起）、战场标识、倒计时起点 R、期限大回合、
/// 得到期限的那个大回合（= 第一次看到它是未结算的多人战场的大回合结束；R 当时即 R）。期限 = max(R + K, 得到期限的大回合 + 1)。
/// </summary>
public sealed record FinalCountdownRecord(int Sequence, int Battlefield, int Start, int Deadline, int AssignedInMajorRound);

/// <summary>
/// 决赛倒计时（final-countdown，battlefield「决赛倒计时」；裁决 W1–W3、design D1–D3）。本文件里的一切只在链接模式、K &gt; 0 时生效。
/// </summary>
/// <remarks>
/// <para><b>等待决赛的玩家</b>（<see cref="IsWaitingForFinal"/>）：在局、尚未进入决赛、所在战场只剩他一名在局玩家，且所在战场已结算（他是胜者；含构筑期结算与减员结算）
/// 或首轮配对步骤已完成（第 3 大回合结束时首轮配对的等待者、3+1 的独占者）。</para>
/// <para><b>起点 R</b>：第一次出现等待决赛的玩家的那个大回合（结束时起算），且不早于第 3 大回合（裁决 W7：构筑期内结算的战场，其胜者的等待按第 3 大回合结束起算）。<b>期限</b>：此后每个大回合结束（以及外部提交首轮配对之后）对每个未结算、在局 ≥ 2 的战场，
/// 若还没有期限则赋 max(R + K, 当前完成的大回合 + 1)——R 那一回合看到的恰为 R + K，之后才出现的（如首轮配对后新拼接的战场）至少给满 1 个大回合。
/// 链接合并两个战场时清掉两者的期限（合并后的新战场视为新出现）；决赛链接之后不再赋期限，决赛战场不倒计时。</para>
/// <para><b>到期</b>：<see cref="JudgeAtRoundEnd"/> 的第三个触发条件（c），排在整轮 Pass 与填满之后，比较链与执行完全复用，原因记 <see cref="SettlementReason.Expired"/>；
/// 先手预测（裁决 S10）复用同一判定，因此"假设此刻结束"在期限大回合同样剔除将到期落败者。</para>
/// <para><b>K = 0</b>：R 永不设定、不赋任何期限、不发任何事件，判定路径与引入之前逐步相同（design D3）。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private readonly SortedDictionary<int, int> _deadlines = [];
    private readonly List<FinalCountdownRecord> _countdowns = [];
    private int? _countdownStart;

    /// <summary>决赛倒计时大回合数 K（对局配置，始终公开）。</summary>
    public int FinalCountdownRounds => Options.FinalCountdownRounds;

    /// <summary>恢复自不含决赛倒计时字段的旧链接模式存档时为 <c>true</c>：按 K = 0（不启用）回填，保持旧存档的行为。</summary>
    public bool FinalCountdownBackfilled { get; private set; }

    /// <summary>倒计时起点 R：第一次出现等待决赛的玩家的那个大回合；尚未出现（或 K = 0）为 <c>null</c>。</summary>
    public int? FinalCountdownStart => _countdownStart;

    /// <summary>当前处于倒计时中的战场 → 期限大回合（升序；已结算的战场仍保留其期限，链接合并时两个旧标识一并移除）。</summary>
    public ImmutableSortedDictionary<int, int> BattlefieldDeadlines => _deadlines.ToImmutableSortedDictionary();

    /// <summary>全部倒计时记录，按赋期限的先后（含之后被链接合并掉的战场）。</summary>
    public IReadOnlyList<FinalCountdownRecord> FinalCountdowns => _countdowns;

    /// <summary>某战场的期限大回合；不在倒计时中为 <c>null</c>。</summary>
    public int? DeadlineOf(int battlefieldId) => _deadlines.TryGetValue(battlefieldId, out int deadline) ? deadline : null;

    /// <summary>
    /// 某玩家此刻是否是"等待决赛的玩家"（battlefield「决赛倒计时」）：在局、尚未进入决赛、所在战场只剩他一人，且所在战场已结算或首轮配对步骤已完成
    /// （待首轮配对时单人战场的玩家还要被配对，不算）。与 K 无关（K = 0 时也照实回答）。
    /// </summary>
    public bool IsWaitingForFinal(PlayerId player)
    {
        PlayerRecord record = Require(player);
        if (_finalLinked || record.Status != PlayerStatus.Active || record.BirthZone is null || Phase == MatchPhase.FlagPlanting)
        {
            return false;
        }

        int battlefield = BattlefieldOf(player);
        if (ActivePlayersIn(battlefield).Length != 1)
        {
            return false;
        }

        return _settledBattlefields.Contains(battlefield)
            || (_firstPairingStepDone && _pendingPairing?.Kind != PairingKind.FirstRound);
    }

    /// <summary>
    /// 等待的起算大回合（裁决 W7）：第 3 大回合（构筑期末）结束。决赛倒计时的起点 R 不早于它（构筑期内结算的战场，其胜者的等待按第 3 大回合结束起算），
    /// 遥测的等待大回合数（<c>PlayerRecord.WaitingRounds</c>）也只计它之后的大回合——两处共用这一个判定，不各算一份。
    /// </summary>
    private const int WaitingStartRound = BuildProtectionRounds;

    /// <summary>刚结束的大回合 <paramref name="completed"/> 是否已到等待的起算点（≥ <see cref="WaitingStartRound"/>）。</summary>
    private static bool WaitingStarted(int completed) => completed >= WaitingStartRound;

    /// <summary>建局与恢复共用的校验：K 须为非负整数。</summary>
    private static void RequireValidFinalCountdown(int rounds)
    {
        if (rounds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rounds), rounds, "决赛倒计时大回合数须为非负整数（0 = 不启用）。");
        }
    }

    /// <summary>
    /// 推进倒计时状态（幂等）：<paramref name="completed"/> 是刚结束的那个大回合。尚无起点且此刻有等待决赛的玩家 → 起点 R = <paramref name="completed"/>；
    /// 有起点后，给每个还没有期限的未结算多人战场赋期限 max(R + K, <paramref name="completed"/> + 1)。
    /// 只在进行中阶段做（待配对 / 待决赛链接时等配对执行完再做）；K = 0、决赛链接之后什么都不做。
    /// </summary>
    private void UpdateFinalCountdown(int completed)
    {
        if (FinalCountdownRounds == 0 || _finalLinked || Phase != MatchPhase.InProgress)
        {
            return;
        }

        // 裁决 W7：R 不早于第 3 大回合。构筑期内什么都不做——到第 3 大回合结束时等待者仍在，R 即取 3，首轮战场至少可打满 K 个大回合。
        if (!WaitingStarted(completed))
        {
            return;
        }

        if (_countdownStart is null)
        {
            if (!_players.Any(IsWaitingForFinal))
            {
                return;
            }

            _countdownStart = completed;
        }

        int start = _countdownStart.Value;
        foreach (int id in OccupiedBattlefields())
        {
            if (_settledBattlefields.Contains(id) || _deadlines.ContainsKey(id) || ActivePlayersIn(id).Length < 2)
            {
                continue;
            }

            int deadline = Math.Max(start + FinalCountdownRounds, completed + 1);
            _deadlines[id] = deadline;
            var record = new FinalCountdownRecord(_countdowns.Count + 1, id, start, deadline, completed);
            _countdowns.Add(record);
            Emit(FlowEventKind.FinalCountdown, null,
                $"决赛倒计时 #{record.Sequence}：起点第 {start} 大回合（K = {FinalCountdownRounds}），战场 {BirthZoneLabel.Of(id)} 最迟第 {deadline} 大回合结束时结算");
        }
    }

    /// <summary>判定（c）：该战场在倒计时中且本大回合已到期限。</summary>
    private bool CountdownExpired(int battlefieldId) =>
        !_finalLinked && _deadlines.TryGetValue(battlefieldId, out int deadline) && MajorRound >= deadline;
}
