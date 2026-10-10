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
/// <para><b>战场结算</b>：大回合结束时逐战场检查整轮 Pass / 填满（<see cref="SettleAtRoundEnd"/>），出局 / 弃赛之后即时检查减员（<see cref="SettleByAttrition"/>）；
/// 全局终局"最后战场结算"在大回合结束判定，"只剩一名参赛玩家"即时判定且优先。</para>
/// </remarks>
public sealed partial class MatchFlow
{
    private readonly List<PairingRecord> _pairings = [];
    private readonly List<LinkRecord> _links = [];
    private readonly SortedSet<int> _settledBattlefields = [];
    private readonly SortedSet<PlayerId> _passedThisRound = [];
    private readonly List<BattlefieldSettlementRecord> _settlements = [];
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

    /// <summary>已接受的配对事件，按发生顺序（随存档往返，见 MatchFlow.Persistence.Link）。</summary>
    public IReadOnlyList<PairingRecord> Pairings => _pairings;

    /// <summary>已执行的链接，按执行顺序（存档记录其序号、战场对、版本号与摘要，恢复时重放核对）。</summary>
    public IReadOnlyList<LinkRecord> Links => _links;

    /// <summary>锁定时每块出生棋盘上的旗数（出生棋盘编号 → 旗数，只含有旗的棋盘）。插旗锁定前为空。</summary>
    public ImmutableSortedDictionary<int, int> LockedFlagCounts => _lockedFlagCounts;

    /// <summary>已结算的战场标识（升序）。战场合并后新战场视为未结算（两个旧标识一并移除）。</summary>
    public ImmutableArray<int> SettledBattlefields => [.. _settledBattlefields];

    /// <summary>全部战场结算记录，按发生顺序（含减员结算）。合并后的新战场即便再结算也是新的一条。</summary>
    public IReadOnlyList<BattlefieldSettlementRecord> Settlements => _settlements;

    /// <summary>本大回合内已 Pass 的在局玩家（整轮 Pass 结算的逐战场依据；大回合结束时清空）。不复用全局的连续 Pass 计数。</summary>
    public ImmutableArray<PlayerId> PassedThisRound => [.. _passedThisRound];

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
        _pairings.Add(new PairingRecord(sequence, request.Kind, MajorRound, request.Candidates, pairs, decision.Waiter, decision.Source));
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
    /// <c>internal</c> 供测试按非规范次序直接调用（"信物子流独立"）；对局流程只经 <see cref="ResolvePairing"/> 调用，存档恢复经 <paramref name="replay"/> 重放
    /// （重放不发事件、不重算派生量——恢复末尾统一重算一次）。
    /// </summary>
    internal LinkRecord ExecuteLink(int x, int y, int pairingSequence, bool replay = false)
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
        var record = new LinkRecord(_links.Count + 1, pairingSequence, lo, hi, result.Map.MapVersion, result.Degraded, result.Summary, [.. relics.Select(r => r.Coord)])
        {
            PublicBoard = result.PublicBoard,
            CorridorCells = result.Corridors.Sum(c => c.Cells().Count()),
            Corridors = result.Corridors,
            MapDigest = MapFile.Digest(result.Map),
            NewRelics = relics,
        };
        _links.Add(record);
        if (!replay)
        {
            Emit(FlowEventKind.BattlefieldsLinked, null,
                $"链接 #{_links.Count}：战场 {BirthZoneLabel.Of(lo)} + {BirthZoneLabel.Of(hi)}{(result.Degraded ? "（退化直通道）" : string.Empty)}；{result.Summary}");
            RecalculateDerived();
        }

        return record;
    }

    // ---------- 战场结算（2.6） ----------

    /// <summary>
    /// 大回合结束时的战场结算（battlefield「战场结算」，构筑期同样生效）：先给每名在局的单人战场玩家记一个等待大回合（构筑期之后），
    /// 再对每个未结算、在局玩家 ≥ 2 的战场按标识升序检查（a）本大回合内全员 Pass、（b）无可落子空格，满足即结算。
    /// 只有 1 名在局玩家的战场不检查：既不因 Pass、也不因填满结算（裁决 S9）。
    /// 同一大回合结束时结算的全部落败者共享一个出局序号，名次按结算时的比较链跨战场统一排序（裁决 S7，见 <see cref="FinalStandings.ComputeLink"/>）。最后清空本大回合的 Pass 记录。
    /// </summary>
    private void SettleAtRoundEnd(int completed)
    {
        if (completed > BuildProtectionRounds)
        {
            foreach (PlayerId player in ActivePlayers)
            {
                if (ActivePlayersIn(BattlefieldOf(player)).Length == 1)
                {
                    _records[player].WaitingRounds++;
                }
            }
        }

        // 结算用的势力按此刻盘面现算（与终局名次同一口径）。
        MarkEstablishedPower(Scoreboard.Recalculate(Board, Roster, Math.Max(MajorRound, 1), Relics.TrueContents()));
        int? order = null;
        foreach (int id in OccupiedBattlefields())
        {
            ImmutableArray<PlayerId> members = ActivePlayersIn(id);
            if (_settledBattlefields.Contains(id) || members.Length < 2)
            {
                continue;
            }

            SettlementReason? reason = members.All(_passedThisRound.Contains) ? SettlementReason.AllPassed
                : !HasPlayableEmptyCell(id) ? SettlementReason.BoardFull
                : null;
            if (reason is { } r)
            {
                order ??= ++_eliminationSequence;
                SettleBattlefield(id, r, order, winner: null);
            }
        }

        _passedThisRound.Clear();
    }

    /// <summary>战场内是否还有可落子空格（逐格读 <c>IsPlayableEmpty</c>，与全盘的 <see cref="GameBoard.HasPlayableEmptyCell"/> 同一判据）。</summary>
    private bool HasPlayableEmptyCell(int battlefieldId) =>
        (Board.Battlefields.ById(battlefieldId) ?? throw new SiegeRuleException($"没有战场 {BirthZoneLabel.Of(battlefieldId)}。")).Cells.Any(c => Board[c].IsPlayableEmpty);

    /// <summary>每个含在局玩家的战场 → 在局人数。出局 / 弃赛之前取一次，供减员结算比较。</summary>
    private Dictionary<int, int> OccupancyCounts() =>
        OccupiedBattlefields().ToDictionary(id => id, id => ActivePlayersIn(id).Length);

    /// <summary>
    /// 减员结算（battlefield「战场结算」）：出局 / 弃赛之前在局 ≥ 2 的战场，此刻只剩 1 名在局玩家且未结算 → 即时标记已结算、该玩家为胜者。
    /// 由它引起的阶段转换仍等到大回合结束（<see cref="AdvanceLinkPhase"/>）。
    /// </summary>
    private void SettleByAttrition(Dictionary<int, int> before)
    {
        foreach ((int id, int count) in before.OrderBy(kv => kv.Key))
        {
            if (count < 2 || _settledBattlefields.Contains(id))
            {
                continue;
            }

            ImmutableArray<PlayerId> members = ActivePlayersIn(id);
            if (members.Length == 1)
            {
                SettleBattlefield(id, SettlementReason.Attrition, order: null, winner: members[0]);
            }
        }
    }

    /// <summary>全局终局条件 2：只剩一个含在局玩家的战场，且它已结算（无人的出生棋盘不计）。</summary>
    private bool LastBattlefieldSettled() =>
        OccupiedBattlefields() is [int only] && _settledBattlefields.Contains(only);

    /// <summary>
    /// 结算一个战场（battlefield「战场结算」）：按 势力 → 控制信物数 → 独占空格数 → 盘面棋子数 排序（链全同时按种子兜底顺序 <c>SeedRank</c> 靠前者为胜者、不因并列而不结算，裁决 S6），
    /// 第一名为胜者；其余在局玩家以"战场落败"出局，记录结算时的比较量（出局时势力可以大于 0）。<paramref name="winner"/> 非空时直接用它（减员结算与测试接缝）。
    /// <paramref name="order"/> 为同批结算共享的出局序号；为 <c>null</c> 且有落败者时新取一个。
    /// </summary>
    private BattlefieldSettlementRecord SettleBattlefield(int battlefieldId, SettlementReason reason, int? order, PlayerId? winner)
    {
        RequireLinkMode();
        if (_settledBattlefields.Contains(battlefieldId))
        {
            throw new SiegeRuleException($"战场 {BirthZoneLabel.Of(battlefieldId)} 已结算，不得再次结算。");
        }

        ImmutableArray<PlayerId> members = ActivePlayersIn(battlefieldId);
        if (winner is { } forced && !members.Contains(forced))
        {
            throw new SiegeRuleException($"胜者 {forced} 不是战场 {BirthZoneLabel.Of(battlefieldId)} 的在局玩家。");
        }

        PowerSnapshot power = Scoreboard.Latest ?? throw new SiegeRuleException("尚无势力快照，不能结算战场。");
        ImmutableArray<RelicPublicState> relics = Relics.PublicStates();
        List<SettlementEntry> chain = [.. members
            .Select(p => new SettlementEntry(p, 0, power.Of(p).Total, relics.Count(s => s.Control.GrantsEffectTo(p)),
                power.Of(p).ExclusiveCells.Length, Board.GroupsOf(p).Sum(g => g.Size)))
            .OrderBy(e => e, SettlementChain.Instance)
            .ThenBy(e => _records[e.Player].SeedRank)];
        SettlementEntry top = winner is { } w ? chain.Single(e => e.Player == w) : chain[0];
        chain.Remove(top);

        ImmutableArray<SettlementEntry>.Builder entries = ImmutableArray.CreateBuilder<SettlementEntry>(members.Length);
        entries.Add(top with { Rank = 1 });
        for (int i = 0; i < chain.Count; i++)
        {
            int rank = i > 0 && SettlementChain.Instance.Compare(chain[i - 1], chain[i]) == 0 ? entries[^1].Rank : i + 2;
            entries.Add(chain[i] with { Rank = rank });
        }

        int? eliminationOrder = chain.Count == 0 ? null : order ?? ++_eliminationSequence;
        _settledBattlefields.Add(battlefieldId);
        var record = new BattlefieldSettlementRecord(_settlements.Count + 1, MajorRound, battlefieldId, reason, top.Player, entries.ToImmutable(), eliminationOrder);
        _settlements.Add(record);
        Emit(FlowEventKind.BattlefieldSettled, top.Player,
            $"战场 {BirthZoneLabel.Of(battlefieldId)} 结算（{reason}）；胜者 {top.Player}；{string.Join(" ", record.Entries.Select(e => $"{e.Rank}.{e.Player}={e.Power}"))}");
        foreach (SettlementEntry loser in record.Entries.Skip(1))
        {
            PlayerRecord r = _records[loser.Player];
            r.Status = PlayerStatus.Eliminated;
            r.EliminationOrder = eliminationOrder;
            r.EliminatedInMajorRound = MajorRound;
            r.EliminationReason = EliminationReason.BattlefieldDefeat;
            r.Defeat = loser;
            Emit(FlowEventKind.PlayerEliminated, loser.Player, $"第 {eliminationOrder} 个出局（战场 {BirthZoneLabel.Of(battlefieldId)} 落败，势力 {loser.Power}）");
        }

        return record;
    }

    internal int DebugSeedRankOf(PlayerId player) => Require(player).SeedRank;

    private void RequireLinkMode()
    {
        if (Mode != MatchMode.Link)
        {
            throw new SiegeRuleException($"对局模式为 {Mode}：该操作只属于链接模式。");
        }
    }

    /// <summary>
    /// 测试接缝：在小回合边界以给定胜者结算一个战场（原因记为整轮 Pass），随后立即做终局检查（最后战场结算 → 只剩一名）。
    /// 正式流程的结算只在大回合结束（<see cref="SettleAtRoundEnd"/>）与出局 / 弃赛之后（减员）发生。
    /// </summary>
    internal void DebugSettleBattlefield(int battlefieldId, PlayerId winner)
    {
        RequireStage(TurnStage.Idle);
        MarkEstablishedPower(Scoreboard.Recalculate(Board, Roster, Math.Max(MajorRound, 1), Relics.TrueContents()));
        SettleBattlefield(battlefieldId, SettlementReason.AllPassed, order: null, winner);
        RecalculateDerived();
        if (LastBattlefieldSettled())
        {
            Finish(EndReason.LastBattlefieldSettled);
            return;
        }

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

    /// <summary>战场结算与名次的比较链：势力 → 控制信物数 → 独占空格数 → 盘面棋子数，全部"高者在前"；四级全同返回 0。</summary>
    private sealed class SettlementChain : IComparer<SettlementEntry>
    {
        internal static readonly SettlementChain Instance = new();

        public int Compare(SettlementEntry? a, SettlementEntry? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            int c = b.Power.CompareTo(a.Power);
            if (c == 0)
            {
                c = b.ControlledRelics.CompareTo(a.ControlledRelics);
            }

            if (c == 0)
            {
                c = b.ExclusiveCells.CompareTo(a.ExclusiveCells);
            }

            return c != 0 ? c : b.Stones.CompareTo(a.Stones);
        }
    }
}
