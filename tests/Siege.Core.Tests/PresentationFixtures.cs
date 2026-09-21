using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Siege.Core.Ai;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Determinism;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using Siege.Presentation.Visibility;
using PreviewResult = Siege.Core.Preview.BatchPreview;

namespace Siege.Core.Tests;

/// <summary>tactical-ui 测试的公共夹具：观察者世界组装、值投影、状态指纹、类型闭包与 IL 调用扫描。</summary>
internal static class PresentationFixtures
{
    internal static readonly PlayerId P0 = MatchFixtures.P0;
    internal static readonly PlayerId P1 = MatchFixtures.P1;
    internal static readonly PlayerId P2 = MatchFixtures.P2;
    internal static readonly PlayerId P3 = MatchFixtures.P3;

    internal static Assembly PresentationAssembly => typeof(PublicWorld).Assembly;

    // ---------- 世界组装（阶段 B 的调用序列即此） ----------

    /// <summary>观察者 <paramref name="viewer"/> 此刻能看到的世界：公开快照 + 补充载荷 + 本人手牌 + 本人部署时的富预演。</summary>
    internal static ViewerWorld World(this MatchFlow match, PlayerId viewer)
    {
        PreviewResult? preview = match.Phase == MatchPhase.InProgress && match.CurrentPlayer == viewer && match.Stage == TurnStage.Deploy
            ? match.PreviewCurrentBatch()
            : null;
        return ViewerWorld.Build(viewer, match.Publish(), match.PublishSupplement(), match.Hands.AccessFor(viewer).PrivateView(), preview);
    }

    internal static PublicWorld PublicWorldOf(this MatchFlow match) => PublicWorld.From(match.Publish(), match.PublishSupplement());

    /// <summary>不含任何信物的账本（静态预演用例的盘面没有信物格）。</summary>
    internal static RelicLedger EmptyRelics(GameBoard board) =>
        new(new RelicGenerationRecord(MatchFixtures.Seed, board.Map.Id, [], Converged: true, Rerolls: 0));

    /// <summary>空手牌的私有视图（静态预演用例不经手牌账本）。</summary>
    internal static HandPrivateView EmptyHand(PlayerId player) =>
        new(player, ImmutableSortedDictionary<PieceType, HandEntry>.Empty, TurnPhase.Idle, null);

    internal static IReadOnlyDictionary<PlayerId, PlayerStatus> Roster(params PlayerId[] players) =>
        players.ToDictionary(p => p, _ => PlayerStatus.Active);

    /// <summary>在任意盘面上直接调富预演（不经 MatchFlow），用于摆设计文档算例。</summary>
    internal static PreviewResult RichPreview(
        GameBoard board, PlayerId player, IReadOnlyDictionary<PlayerId, PlayerStatus> roster, int limit, IReadOnlyDictionary<PieceType, int>? stock,
        BoardHistory? history, params (string Cell, PieceType Type)[] placements) =>
        BatchPreviewBuilder.Build(
            board,
            BatchFixtures.Context(board, player, limit, stock),
            [.. placements.Select(p => new Placement(Coord.Parse(p.Cell), p.Type))],
            history ?? new BoardHistory(),
            roster,
            EmptyRelics(board),
            majorRound: 5);

    /// <summary>
    /// 9×9 流程夹具地图，但每个信物格自带分区与档位（默认夹具把全部信物标为公共区标准档，测不了"默认棋盘不区分分区"）。
    /// 进入第 1 大回合后摆到第 5 大回合（全图可落子）。
    /// </summary>
    internal static MatchFlow StartedWithZonedRelics(params (string Cell, RelicContent Content, RelicZone Zone, BudgetTier Tier)[] relics)
    {
        MapData map = MatchFixtures.Map() with
        {
            RelicCells = relics.ToImmutableDictionary(r => Coord.Parse(r.Cell), r => new RelicCellSpec(r.Zone, r.Tier)),
        };
        ImmutableArray<RelicPlacement> placements =
            [.. relics.Select(r => new RelicPlacement(Coord.Parse(r.Cell), r.Content, new RelicCellSpec(r.Zone, r.Tier))).OrderBy(p => p.Coord)];
        MatchFlow match = MatchFlow.CreateUnvalidated(map, MatchFixtures.Seed, MatchFixtures.All,
            new RelicGenerationRecord(MatchFixtures.Seed, map.Id, placements, Converged: true, Rerolls: 0), MatchOptions.Immediate);
        foreach (PlayerId p in MatchFixtures.All)
        {
            match.Debug.SeedHand(p, (PieceType.Basic, 50));
        }

        match.PlantSequentially(MatchFixtures.All.Select((p, i) => (p, i)));
        return match.AtRound(5);
    }

    /// <summary>直接在权威盘面摆指定类型的棋子（绕过规则，只为构造局面），随后揭示并重算，让公开状态与盘面一致。</summary>
    internal static MatchFlow Pieces(this MatchFlow match, PlayerId owner, PieceType type, params string[] cells)
    {
        foreach (string cell in cells)
        {
            match.Board.Place(Coord.Parse(cell), owner, type);
        }

        match.Relics.Reveal(match.Board, match.MajorRound);
        match.Debug.Recalculate();
        return match;
    }

    // ---------- 投影 ----------

    /// <summary>
    /// 把一棵表现模型 / 预演载荷的对象树投影成确定性文本（含集合字段的 record 不能直接 Assert.Equal，testing.md）。
    /// 公开实例属性逐个展开；坐标用围棋记法；盘面用序列化。
    /// </summary>
    internal static string Dump(object? value)
    {
        var sb = new StringBuilder();
        Append(sb, value, depth: 0);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, object? value, int depth)
    {
        if (depth > 16)
        {
            throw new InvalidOperationException("投影深度超过 16：对象树可能有环。");
        }

        switch (value)
        {
            case null:
                sb.Append('∅');
                return;
            case string s:
                sb.Append('"').Append(s).Append('"');
                return;
            case Coord c:
                sb.Append(c.ToNotation());
                return;
            case GameBoard board:
                sb.Append(board.Serialize());
                return;
            case CoverageMap:
                sb.Append("<coverage>");
                return;
            case Multiplier m:
                sb.Append('×').Append(m.ToString());
                return;
            case Enum or PlayerId or bool or int or long or byte:
                sb.Append(value);
                return;
        }

        Type type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && (bool)type.GetProperty("IsDefault")!.GetValue(value)!)
        {
            sb.Append("<default>");
            return;
        }

        if (value is IEnumerable sequence)
        {
            sb.Append('[');
            bool first = true;
            foreach (object? item in sequence)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                Append(sb, item, depth + 1);
                first = false;
            }

            sb.Append(']');
            return;
        }

        sb.Append(type.Name).Append('{');
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (prop.Name == "EqualityContract")
            {
                continue;
            }

            sb.Append(prop.Name).Append('=');
            Append(sb, prop.GetValue(value), depth + 1);
            sb.Append(';');
        }

        sb.Append('}');
    }

    /// <summary>
    /// 对局权威状态的指纹：部署阶段不能 <see cref="MatchFlow.Serialize"/>（只允许小回合边界），用逐项投影代替。
    /// 覆盖盘面、历史、信物揭示与控制、势力榜版本与峰值、流程状态、事件、全部玩家两段账、征募子流位置、效果快照与暂放批次。
    /// </summary>
    internal static string Fingerprint(MatchFlow m)
    {
        var lines = new List<string>
        {
            m.Board.Serialize(),
            m.History.Serialize(),
            string.Join(";", m.Relics.RevealEvents.Select(e => e.ToString())),
            m.Relics.DeployLimitPeak?.ToString() ?? "-",
            string.Join(";", m.Relics.PublicStates().Select(r => r.ToString())),
            $"v{m.Scoreboard.Version} peak={m.Scoreboard.Peak?.MultiplierCount}/{m.Scoreboard.Peak?.MajorRound}",
            m.Scoreboard.Latest is null ? "-" : AiFixtures.PowerText(m),
            AiFixtures.FlowText(m),
            $"events={m.Events.Count} last={m.Events.LastOrDefault()}",
            string.Join(";", m.Players.Select(p => $"{m.Hands.Debug.PrivateViewOf(p)}/{m.Hands.PhaseOf(p)}/{m.Hands.Debug.PrivateViewOf(p).TypeSlots}")),
            $"recruit={m.Hands.RecruitStreamConsumed} records={m.Hands.Records.Count}",
            m.CurrentSnapshot is { } s ? $"{s.Player}/{s.RevealCount}/{s.FreePickCount}/{s.TypeSlots}/{s.DeployLimit}/{s.HeldTypeCount}" : "-",
            m.CurrentBatch is { } b ? string.Join(",", b.Placements) : "-",
            $"reports={m.InitiativeReports.Count} resign={m.Resignations.Count}",
        };
        return string.Join("\n", lines);
    }

    // ---------- 信息边界：类型闭包 ----------

    /// <summary>面向"他人"的模型闭包中 MUST NOT 出现的类型：私有视图、征募面板、暂放批次、预演、可变账本与对局本体、种子与生成记录。</summary>
    internal static readonly Type[] PrivateTypes =
    [
        typeof(HandPrivateView),
        typeof(HandEntry),
        typeof(RecruitPanelView),
        typeof(RecruitCandidateView),
        typeof(PlayerHandAccess),
        typeof(StagedBatch),
        typeof(Placement),
        typeof(BatchContext),
        typeof(PreviewResult),
        typeof(ResignationSnapshot),
        typeof(HandLedger),
        typeof(RelicLedger),
        typeof(MatchFlow),
        typeof(MatchRunner),
        typeof(MatchDebugView),
        typeof(RelicGenerationRecord),
        typeof(RelicPlacement),
        typeof(GameSeed),
    ];

    /// <summary>
    /// 从 <paramref name="root"/> 出发经类型化 API 可达的 Siege.Core + Siege.Presentation 类型闭包。算法同 <see cref="AiFixtures.ReachableTypes"/>：
    /// 根类型取全部实例字段（含私有）与构造参数；其余类型取公开实例字段 / 属性 / 方法的返回与参数类型；泛型参数、数组元素、可空一律展开。
    /// </summary>
    internal static ImmutableHashSet<Type> ReachableTypes(Type root)
    {
        Assembly[] scope = [typeof(GameBoard).Assembly, PresentationAssembly];
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();

        void Visit(Type t)
        {
            if (t.IsByRef || t.IsArray || t.IsPointer)
            {
                Visit(t.GetElementType()!);
                return;
            }

            if (t.IsGenericType)
            {
                foreach (Type arg in t.GetGenericArguments())
                {
                    Visit(arg);
                }
            }

            if (t.IsGenericParameter || !scope.Contains(t.Assembly))
            {
                return;
            }

            if (seen.Add(t))
            {
                queue.Enqueue(t);
            }
        }

        Visit(root);
        bool isRoot = true;
        while (queue.Count > 0)
        {
            Type t = queue.Dequeue();
            BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.Public | (isRoot ? BindingFlags.NonPublic : BindingFlags.Default);
            foreach (FieldInfo f in t.GetFields(fieldFlags))
            {
                Visit(f.FieldType);
            }

            if (isRoot)
            {
                foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (ParameterInfo p in c.GetParameters())
                    {
                        Visit(p.ParameterType);
                    }
                }
            }

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                Visit(p.PropertyType);
            }

            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.DeclaringType == typeof(object))
                {
                    continue;
                }

                Visit(m.ReturnType);
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Visit(p.ParameterType);
                }
            }

            isRoot = false;
        }

        return seen.ToImmutableHashSet();
    }

    /// <summary>闭包中出现的私有类型名（空即通过）。</summary>
    internal static string[] PrivateLeaks(Type root) =>
        [.. ReachableTypes(root).Intersect(PrivateTypes).Select(t => t.Name).Order()];

    // ---------- 依赖守门：IL 调用扫描 ----------

    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => (ushort)o.Value);

    /// <summary>
    /// 程序集内每个方法体（含构造函数、属性访问器、lambda / 迭代器等编译器生成的嵌套类型）引用到的方法与字段，逐条 IL 指令解析。
    /// </summary>
    internal static IEnumerable<(MethodBase Caller, MemberInfo Target, OpCode OpCode)> IlReferences(Assembly assembly)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (Type type in assembly.GetTypes())
        {
            IEnumerable<MethodBase> methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (MethodBase method in methods)
            {
                byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null)
                {
                    continue;
                }

                Type[]? typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
                Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
                int i = 0;
                while (i < il.Length)
                {
                    ushort code = il[i];
                    i++;
                    if (code == 0xFE)
                    {
                        code = (ushort)(0xFE00 | il[i]);
                        i++;
                    }

                    OpCode op = OpCodesByValue[code];
                    int operandSize = op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                        _ => 4,
                    };

                    MemberInfo? target = op.OperandType switch
                    {
                        OperandType.InlineMethod => method.Module.ResolveMethod(BitConverter.ToInt32(il, i), typeArgs, methodArgs),
                        OperandType.InlineField => method.Module.ResolveField(BitConverter.ToInt32(il, i), typeArgs, methodArgs),
                        OperandType.InlineTok => method.Module.ResolveMember(BitConverter.ToInt32(il, i), typeArgs, methodArgs),
                        _ => null,
                    };

                    if (target is not null)
                    {
                        yield return (method, target, op);
                    }
                    else if (op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8 || op == OpCodes.Conv_R4 || op == OpCodes.Conv_R8 || op == OpCodes.Conv_R_Un)
                    {
                        yield return (method, method, op);
                    }

                    i += operandSize;
                }
            }
        }
    }

    // ---------- 杂项 ----------

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "siege.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到 siege.sln 所在目录。");
    }
}
