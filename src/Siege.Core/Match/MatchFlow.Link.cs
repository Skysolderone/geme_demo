using System.Collections.Immutable;
using Siege.Core.Board;
using Siege.Core.Board.Maps;
using Siege.Core.Relics;
using Siege.Core.Scoring;

namespace Siege.Core.Match;

/// <summary>
/// 链接模式的对局流程（board-link-mode 段 B：battlefield「阶段推进」「配对事件由对局外部提供」「链接事件」）。
/// 本文件里的一切只在 <see cref="MatchMode.Link"/> 下被调用；旧模式不进入任何一条分支、不派生任何新子流，行为与之前逐字节相同。
/// </summary>
/// <remarks>
/// <para><b>阶段</b>：插旗（<see cref="MatchPhase.FlagPlanting"/>）→ 构筑（<see cref="MatchPhase.InProgress"/>，<see cref="LinkStage"/> = Building）
/// → 待首轮配对（<see cref="MatchPhase.AwaitingPairing"/>）→ 交战（InProgress / Fighting）→ 待决赛链接（<see cref="MatchPhase.AwaitingFinalLink"/>）
/// → 交战（InProgress / Final）→ 终局（<see cref="MatchPhase.Ended"/>）。全部阶段转换都在大回合结束时（<see cref="AdvanceLinkPhase"/>）。</para>
/// <para><b>配对</b>：Core 不抽配对随机数。进入待配对时若挂接了 <see cref="PairingSource"/> 就当场向它要结果；否则停在待配对，由外部调用 <see cref="ApplyPairing(PairingDecision)"/>。
/// 两条路径共用同一份校验与执行。</para>
/// <para><b>链接</b>（段 A 接线契约）：对 <see cref="GameBoard.LinkedMap"/> 调 <see cref="LinkMapGenerator.Link(MapData, int, int)"/>，再 <see cref="GameBoard.ApplyLink"/>；
/// 同一配对事件内按规范次序逐对执行（R2）；公共棋盘的信物内容由 <c>relic-link:&lt;X&gt;:&lt;Y&gt;</c> 子流生成并追加进账本（B6）。</para>
/// <para><b>段 B 后半接线点</b>：战场结算（2.6）替换 <see cref="SettleBattlefield"/> 的判定来源（现只由测试桩调用）；终局与名次（2.7）、按座位视图（2.8）、
/// 存档与日志（2.9）读 <see cref="Pairings"/> / <see cref="Links"/> / <see cref="LockedFlagCounts"/> / <see cref="SettledBattlefields"/>。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private readonly List<PairingRecord> _pairings = [];
    private readonly List<LinkRecord> _links = [];
    private readonly SortedSet<int> _settledBattlefields = [];
    private ImmutableSortedDictionary<int, int> _lockedFlagCounts = ImmutableSortedDictionary<int, int>.Empty;
    private bool _firstPairingStepDone;
    private bool _finalLinked;
    private PairingRequest? _pendingPairing;

    /// <summary>对局模式（实现裁决 B1）。</summary>
    public MatchMode Mode => Options.Mode;

    /// <summary>
    /// 挂接的配对来源（实现裁决 B3）：进入待配对 / 待决赛链接时当场调用并校验。为 <c>null</c> 时对局停在待配对阶段，等外部调用 <see cref="ApplyPairing(PairingDecision)"/>。
    /// 不是对局配置、不入存档——配对结果本身才是记录（2.9）。
    /// </summary>
    public IPairingSource? PairingSource { get; set; }

    /// <summary>链接模式在"进行中"之内的阶段标签；旧模式为 <c>null</c>。</summary>
    public LinkStage? LinkStage => Mode != MatchMode.Link ? null
        : _finalLinked ? Match.LinkStage.Final
        : _firstPairingStepDone ? Match.LinkStage.Fighting
        : Match.LinkStage.Building;

    /// <summary>待处理的配对请求：只在 <see cref="MatchPhase.AwaitingPairing"/> / <see cref="MatchPhase.AwaitingFinalLink"/> 时非空。</summary>
    public PairingRequest? PendingPairing => _pendingPairing;

    /// <summary>已接受的配对事件，按发生顺序（内存记录；序列化属 2.9）。</summary>
    public IReadOnlyList<PairingRecord> Pairings => _pairings;

    /// <summary>已执行的链接，按执行顺序（内存记录；序列化属 2.9）。</summary>
    public IReadOnlyList<LinkRecord> Links => _links;

    /// <summary>锁定时每块出生棋盘上的旗数（出生棋盘编号 → 旗数，只含有旗的棋盘）。插旗锁定前为空。</summary>
    public ImmutableSortedDictionary<int, int> LockedFlagCounts => _lockedFlagCounts;

    /// <summary>已结算的战场标识（升序）。战场合并后新战场视为未结算（两个旧标识一并移除）。</summary>
    public ImmutableArray<int> SettledBattlefields => [.. _settledBattlefields];

    /// <summary>某战场是否已结算。</summary>
    public bool IsBattlefieldSettled(int battlefieldId) => _settledBattlefields.Contains(battlefieldId);

    /// <summary>某玩家所在战场的标识（= 含其出生棋盘的连通分量里最小的出生棋盘编号，随链接变化）。须已锁定出生区。</summary>
    public int BattlefieldOf(PlayerId player)
    {
        int zone = Require(player).BirthZone ?? throw new SiegeRuleException($"玩家 {player} 尚未锁定出生区。");
        return PublicRules.BattlefieldOf(Board, zone).Id;
    }

    /// <summary>某战场里仍在局的玩家（升序）。</summary>
    public ImmutableArray<PlayerId> ActivePlayersIn(int battlefieldId) =>
        [.. _players.Where(p => _records[p].Status == PlayerStatus.Active && BattlefieldOf(p) == battlefieldId)];

    /// <summary>
    /// 首轮配对候选（battlefield「阶段推进」）：锁定时旗数恰为 1 的出生棋盘上、仍在局的玩家所在的战场（升序）。
    /// 锁定时旗数 ≥ 2 的棋盘 MUST NOT 进入，即便它已在构筑期结算只剩胜者。
    /// </summary>
    public ImmutableArray<int> FirstRoundCandidates()
    {
        RequireLinkMode();
        return [.. _players
            .Where(p => _records[p].BirthZone is { } zone && _lockedFlagCounts.GetValueOrDefault(zone) == 1 && _records[p].Status == PlayerStatus.Active)
            .Select(BattlefieldOf)
            .Distinct()
            .Order()];
    }

    /// <summary>决赛链接的候选：全部在局玩家所在的战场（升序）。</summary>
    public ImmutableArray<int> OccupiedBattlefields() =>
        [.. _players.Where(p => _records[p].Status == PlayerStatus.Active).Select(BattlefieldOf).Distinct().Order()];

    /// <summary>
    /// 提交一次配对事件（battlefield「配对事件由对局外部提供」）。只在待配对 / 待决赛链接阶段可用。
    /// 校验：每个候选恰出现一次（在某一对里或作为等待者），非候选不得出现，一对的两方不同；候选为奇数时恰有一名等待者、为偶数时没有；
    /// 决赛只接受唯一合法结果。校验失败抛 <see cref="SiegeRuleException"/>，对局保持原阶段、什么都不变。
    /// 通过后按规范次序（含最小出生棋盘编号的一对先，R2）逐对链接，进入交战并继续行动顺序。
    /// </summary>
    public void ApplyPairing(PairingDecision decision)
    {
        ResolvePairing(decision);
        EnsureCurrentActive();
    }

    /// <summary>同 <see cref="ApplyPairing(PairingDecision)"/>，输入为配对列表与等待者。</summary>
    public void ApplyPairing(IEnumerable<(int X, int Y)> pairs, int? waiter = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ApplyPairing(new PairingDecision([.. pairs], waiter));
    }

    // ---------- 内部 ----------

    /// <summary>插旗锁定时记下每块出生棋盘上的旗数（首轮候选的依据）。两种模式都记，旧模式不读。</summary>
    private void RecordLockedFlagCounts(ImmutableSortedDictionary<PlayerId, int> zones) =>
        _lockedFlagCounts = zones.Values.GroupBy(z => z).ToImmutableSortedDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// 大回合结束时的阶段转换（只在链接模式下由 <see cref="EndMajorRound"/> 调用，此时 <see cref="MajorRound"/> 已推进到下一轮）：
    /// 第 3 大回合结束执行首轮配对步骤（候选 ≥ 2 进入待配对，≤ 1 直接进入交战）；此后每个大回合结束检查决赛触发
    /// （全部多人战场已结算且在局玩家 ≥ 2）。
    /// </summary>
    private void AdvanceLinkPhase(int completed)
    {
        if (Phase != MatchPhase.InProgress)
        {
            return;
        }

        if (!_firstPairingStepDone)
        {
            if (completed < BuildProtectionRounds)
            {
                return;
            }

            _firstPairingStepDone = true;
            ImmutableArray<int> candidates = FirstRoundCandidates();
            if (candidates.Length >= 2)
            {
                EnterAwaiting(new PairingRequest(PairingKind.FirstRound, candidates, MajorRound));
                return;
            }
        }

        if (!_finalLinked && FinalReady())
        {
            EnterAwaiting(new PairingRequest(PairingKind.Final, OccupiedBattlefields(), MajorRound));
        }
    }

    /// <summary>
    /// 决赛触发：在局玩家 ≥ 2，且每个含在局玩家的战场都只剩 1 名在局玩家（多人战场结算后只剩胜者；减员结算同理；单人等待者的战场本来就不需要结算）。
    /// 无人的出生棋盘不计入（battlefield「阶段推进」最后一条）。
    /// </summary>
    private bool FinalReady()
    {
        if (ActiveCount < 2)
        {
            return false;
        }

        return OccupiedBattlefields().All(id => ActivePlayersIn(id).Length == 1);
    }

    private void EnterAwaiting(PairingRequest request)
    {
        Phase = request.Kind == PairingKind.FirstRound ? MatchPhase.AwaitingPairing : MatchPhase.AwaitingFinalLink;
        _pendingPairing = request;
        if (PairingSource is { } source)
        {
            // 在大回合结束的推进循环之内：结果不合法即抛出，对局停在待配对阶段，外部可再调 ApplyPairing。
            ResolvePairing(source.Decide(request));
        }
    }

    /// <summary>校验并执行一次配对事件；不推进行动指针（大回合结束的推进循环之内调用时由循环继续推进）。</summary>
    private void ResolvePairing(PairingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        RequireLinkMode();
        PairingRequest request = _pendingPairing
            ?? throw new SiegeRuleException($"对局阶段为 {Phase}，当前没有待处理的配对事件。");
        ImmutableArray<(int X, int Y)> pairs = Validate(request, decision);

        int sequence = _pairings.Count + 1;
        _pairings.Add(new PairingRecord(sequence, request.Kind, MajorRound, request.Candidates, pairs, decision.Waiter));
        _pendingPairing = null;
        Phase = MatchPhase.InProgress;
        Emit(FlowEventKind.PairingApplied, null,
            $"{(request.Kind == PairingKind.FirstRound ? "首轮配对" : "决赛链接")} #{sequence}：候选 {string.Join(" ", request.Candidates.Select(BirthZoneLabel.Of))}；{decision}");

        foreach ((int x, int y) in pairs)
        {
            ExecuteLink(x, y, sequence);
        }

        if (request.Kind == PairingKind.Final)
        {
            _finalLinked = true;
        }
    }

    /// <summary>「配对事件由对局外部提供」的全部校验；返回规范次序的配对。</summary>
    private static ImmutableArray<(int X, int Y)> Validate(PairingRequest request, PairingDecision decision)
    {
        ImmutableArray<(int X, int Y)> pairs = decision.Canonical;
        if (request.Kind == PairingKind.Final)
        {
            PairingDecision only = ScriptedPairingSource.Default(request);
            if (decision.Waiter is not null || !pairs.SequenceEqual(only.Canonical))
            {
                throw new SiegeRuleException($"决赛链接只有一种合法结果：{only}；收到 {decision}。");
            }

            return pairs;
        }

        var seen = new HashSet<int>();
        foreach ((int x, int y) in pairs)
        {
            if (x == y)
            {
                throw new SiegeRuleException($"配对的两方必须是不同的战场：({BirthZoneLabel.Of(x)},{BirthZoneLabel.Of(y)})。");
            }

            foreach (int id in new[] { x, y })
            {
                RequireCandidate(request, id);
                if (!seen.Add(id))
                {
                    throw new SiegeRuleException($"战场 {BirthZoneLabel.Of(id)} 在配对事件中出现了不止一次。");
                }
            }
        }

        bool odd = request.Candidates.Length % 2 == 1;
        if (odd && decision.Waiter is null)
        {
            throw new SiegeRuleException($"候选为奇数（{request.Candidates.Length} 个），配对事件须恰指定一名等待者。");
        }

        if (!odd && decision.Waiter is not null)
        {
            throw new SiegeRuleException($"候选为偶数（{request.Candidates.Length} 个），配对事件不得指定等待者。");
        }

        if (decision.Waiter is { } waiter)
        {
            RequireCandidate(request, waiter);
            if (!seen.Add(waiter))
            {
                throw new SiegeRuleException($"等待者 {BirthZoneLabel.Of(waiter)} 同时出现在配对里。");
            }
        }

        if (seen.Count != request.Candidates.Length)
        {
            ImmutableArray<int> missing = [.. request.Candidates.Where(c => !seen.Contains(c))];
            throw new SiegeRuleException($"配对事件漏了候选 {string.Join(" ", missing.Select(BirthZoneLabel.Of))}：每个候选须恰在一对里或作为等待者。");
        }

        return pairs;
    }

    private static void RequireCandidate(PairingRequest request, int id)
    {
        if (!request.Candidates.Contains(id))
        {
            throw new SiegeRuleException($"战场 {BirthZoneLabel.Of(id)} 不是本次配对的候选（候选 {string.Join(" ", request.Candidates.Select(BirthZoneLabel.Of))}）。");
        }
    }

    /// <summary>
    /// 执行一次链接（段 A 接线契约 1）：对开局 + 链接的地图（不含改造）生成增长，换到盘面上（已有改造重新落在新图上），
    /// 新增信物格的内容由 <c>relic-link:&lt;X&gt;:&lt;Y&gt;</c> 子流生成并追加进账本（B6），两个旧战场的结算标记一并清除，派生量重算。
    /// <c>internal</c> 供测试按非规范次序直接调用（"信物子流独立"）；对局流程只经 <see cref="ResolvePairing"/> 调用。
    /// </summary>
    internal void ExecuteLink(int x, int y, int pairingSequence)
    {
        RequireLinkMode();
        MapData before = Board.LinkedMap;
        LinkResult result = LinkMapGenerator.Link(before, x, y);
        Board.ApplyLink(result.Map);
        ImmutableArray<RelicPlacement> relics = RelicGenerator.GenerateLinked(
            result.Map.RelicCells.Where(kv => !before.RelicCells.ContainsKey(kv.Key)), Seed, x, y, ContentSet);
        Relics.Append(relics);
        _settledBattlefields.Remove(x);
        _settledBattlefields.Remove(y);
        int lo = Math.Min(x, y);
        int hi = Math.Max(x, y);
        _links.Add(new LinkRecord(_links.Count + 1, pairingSequence, lo, hi, result.Map.MapVersion, result.Degraded, result.Summary, [.. relics.Select(r => r.Coord)]));
        Emit(FlowEventKind.BattlefieldsLinked, null,
            $"链接 #{_links.Count}：战场 {BirthZoneLabel.Of(lo)} + {BirthZoneLabel.Of(hi)}{(result.Degraded ? "（退化直通道）" : string.Empty)}；{result.Summary}");
        RecalculateDerived();
    }

    /// <summary>
    /// 战场结算的执行（battlefield「战场结算」）：标记已结算，胜者留下，其余在局玩家出局（同一次结算共享一个出局序号）。
    /// <b>2.6 之前的桩</b>：判定条件（整轮 Pass / 填满 / 减员）、按势力与判定链排出胜者、"战场落败"出局原因与势力记录都属 2.6 / 2.7，
    /// 现只由测试接缝 <see cref="DebugSettleBattlefield"/> 以给定胜者调用。
    /// </summary>
    private void SettleBattlefield(int battlefieldId, PlayerId winner)
    {
        RequireLinkMode();
        if (_settledBattlefields.Contains(battlefieldId))
        {
            throw new SiegeRuleException($"战场 {BirthZoneLabel.Of(battlefieldId)} 已结算，不得再次结算。");
        }

        ImmutableArray<PlayerId> members = ActivePlayersIn(battlefieldId);
        if (!members.Contains(winner))
        {
            throw new SiegeRuleException($"胜者 {winner} 不是战场 {BirthZoneLabel.Of(battlefieldId)} 的在局玩家。");
        }

        _settledBattlefields.Add(battlefieldId);
        PlayerId[] losers = [.. members.Where(p => p != winner)];
        if (losers.Length > 0)
        {
            int order = ++_eliminationSequence;
            foreach (PlayerId loser in losers)
            {
                PlayerRecord record = _records[loser];
                record.Status = PlayerStatus.Eliminated;
                record.EliminationOrder = order;
                record.EliminatedInMajorRound = MajorRound;
                Emit(FlowEventKind.PlayerEliminated, loser, $"第 {order} 个出局（战场 {BirthZoneLabel.Of(battlefieldId)} 落败）");
            }
        }

        RecalculateDerived();
    }

    private void RequireLinkMode()
    {
        if (Mode != MatchMode.Link)
        {
            throw new SiegeRuleException($"对局模式为 {Mode}：该操作只属于链接模式。");
        }
    }

    /// <summary>测试桩（2.6 前）：在小回合边界以给定胜者结算一个战场；随后与弃赛一样立即做终局检查。</summary>
    internal void DebugSettleBattlefield(int battlefieldId, PlayerId winner)
    {
        RequireStage(TurnStage.Idle);
        SettleBattlefield(battlefieldId, winner);
        CheckEndConditions();
        if (_pendingEnd is { } reason)
        {
            Finish(reason);
        }
        else
        {
            EnsureCurrentActive();
        }
    }
}
