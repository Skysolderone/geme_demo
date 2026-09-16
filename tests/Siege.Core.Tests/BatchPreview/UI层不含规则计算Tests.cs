using System.Reflection;
using System.Reflection.Emit;
using Siege.Core.Batch;
using Siege.Core.Board;
using Siege.Core.Match;
using Siege.Core.Preview;
using Siege.Core.Recruit;
using Siege.Core.Relics;
using Siege.Core.Scoring;
using static Siege.Core.Tests.PresentationFixtures;

namespace Siege.Core.Tests.BatchPreview;

/// <summary>强制回归（implement 2.2 / 6.3，tactical-ui D2）：表现层只消费预演结果与快照，不调用任何规则计算入口。</summary>
public class UI层不含规则计算Tests
{
    /// <summary>整个类型都是计算入口或可变对局对象：表现层不得调用其任何成员。</summary>
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(PowerCalculator), typeof(global::Siege.Core.Scoring.PieceEffects), typeof(CaptureResolver), typeof(BatchRehearsal), typeof(BatchPreviewBuilder),
        typeof(global::Siege.Core.Scoring.CatchUpCompensation),   // catch-up-recruit：补偿判定唯一实现在规则层，表现层只读来源拆分
        typeof(LibertySnapshot), typeof(global::Siege.Core.Match.InitiativeOrder), typeof(FinalStandings), typeof(Adjacency), typeof(MapValidator),
        typeof(RelicLedger), typeof(RelicGenerator), typeof(HandLedger), typeof(PlayerHandAccess), typeof(PowerScoreboard),
        typeof(MatchFlow), typeof(MatchRunner), typeof(SettlementDriver), typeof(StagedBatch), typeof(BoardHistory), typeof(FlagPlanting),
    ];

    /// <summary>只读类型上的计算 / 写入方法：盘面的棋串、气、邻接、写盘；覆盖表的重算；倍率的乘法取整。</summary>
    private static readonly (Type Type, string Name)[] ForbiddenMembers =
    [
        (typeof(GameBoard), nameof(GameBoard.GroupAt)),
        (typeof(GameBoard), nameof(GameBoard.LibertiesOf)),
        (typeof(GameBoard), nameof(GameBoard.IsCaptured)),
        (typeof(GameBoard), nameof(GameBoard.GroupsOf)),
        (typeof(GameBoard), nameof(GameBoard.AllGroups)),
        (typeof(GameBoard), nameof(GameBoard.Neighbors)),
        (typeof(GameBoard), nameof(GameBoard.LibertyNeighbors)),
        (typeof(GameBoard), nameof(GameBoard.CoverageTargets)),
        (typeof(GameBoard), nameof(GameBoard.HasPlayableEmptyCell)),
        (typeof(GameBoard), nameof(GameBoard.Place)),
        (typeof(GameBoard), nameof(GameBoard.Clear)),
        (typeof(GameBoard), nameof(GameBoard.RemoveStones)),
        (typeof(GameBoard), nameof(GameBoard.Clone)),
        (typeof(CoverageMap), nameof(CoverageMap.Compute)),
        (typeof(Multiplier), nameof(Multiplier.Apply)),
    ];

    [Fact]
    public void 表现层不调用规则计算入口()
    {
        // 逐条解析 Siege.Presentation 全部方法体（含 lambda / 迭代器生成的嵌套类型）的 IL，收集 call / callvirt / newobj / ldftn 引用的方法。
        // 变异验证 M-D1：TacticalLayers.Power 里加一行 `_ = PowerCalculator.Compute(world.View.Board);` → 本测试红 1（报出 TacticalLayers.Power → PowerCalculator.Compute）。
        // 变异验证 M-D2：TacticalLayers.Liberties 的 lambda 里改用 `world.View.Board.LibertiesOf(world.View.Board.GroupAt(g.Stones[0])!)` → 本测试红 1（lambda 在编译器生成的 <>c__DisplayClass 里，证明嵌套类型被扫到）。
        string[] violations =
        [
            .. IlReferences(PresentationAssembly)
                .Where(r => r.Target is MethodBase m && IsForbidden(m))
                .Select(r => $"{r.Caller.DeclaringType!.FullName}.{r.Caller.Name} → {r.Target.DeclaringType!.Name}.{r.Target.Name}")
                .Distinct()
                .Order(),
        ];

        Assert.Empty(violations);

        // 反面：扫描器确实能看到表现层对 Core 只读成员的调用（否则"空"可能只是没扫到）
        string[] seen = [.. IlReferences(PresentationAssembly).Where(r => r.Target is MethodBase).Select(r => $"{r.Target.DeclaringType!.Name}.{r.Target.Name}").Distinct()];
        Assert.Contains("CoverageMap.OwnershipOf", seen);
        Assert.Contains("GroupPower.get_Multiplier", seen);
        Assert.Contains("Coord.ToNotation", seen);
    }

    [Fact]
    public void 表现层不持有可变对局对象()
    {
        // Presentation 是视图模型：任何字段、属性、方法参数与返回值都不得是对局本体、账本、暂放批次或私有句柄——对局操作意图由 Godot 层直接调 MatchFlow。
        // 变异验证 M-D3：给 ViewerWorld 加 `public StagedBatch? Batch { get; init; }` → 本测试红 1。
        Type[] mutable = [typeof(MatchFlow), typeof(MatchRunner), typeof(HandLedger), typeof(RelicLedger), typeof(StagedBatch), typeof(PlayerHandAccess), typeof(SettlementDriver), typeof(BoardHistory), typeof(PowerScoreboard)];
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<string>();
        foreach (Type type in PresentationAssembly.GetTypes())
        {
            IEnumerable<(string Where, Type Type)> signatures =
                type.GetFields(all).Select(f => (f.Name, f.FieldType))
                .Concat(type.GetProperties(all).Select(p => (p.Name, p.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => (m.Name, p.ParameterType)).Append((m.Name, m.ReturnType))))
                .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => (".ctor", p.ParameterType))));
            found.AddRange(signatures.Where(s => mutable.Contains(s.Type)).Select(s => $"{type.Name}.{s.Where}: {s.Type.Name}"));
        }

        Assert.Empty(found);
    }

    [Fact]
    public void 表现层不引用Godot()
    {
        // 裁决 8：Siege.Presentation 零 Godot 依赖。与"内核不引用Godot"同法，看落进程序集的实际引用。
        // 变异验证 M-D4：把第二条断言的前缀 "Godot" 临时改成 "Siege"（模拟被禁前缀命中一条真实引用）→ 本测试红 1，证明断言读的是真实引用表而不是空表。
        AssemblyName[] referenced = PresentationAssembly.GetReferencedAssemblies();

        Assert.Contains(referenced, r => r.Name == "Siege.Core");
        Assert.DoesNotContain(referenced, r => r.Name is not null && r.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, r => r.Name == "Siege.Sim");
    }

    [Fact]
    public void 内核与表现层不出现浮点()
    {
        // determinism.md：计分与倍率禁止浮点；tactical-ui 把同一约束扩到表现层（倍率显示用 Multiplier.ToString）。
        // 检查字段 / 属性 / 参数 / 返回 / 局部变量类型，以及 IL 中的 ldc.r4 / ldc.r8 / conv.r*。
        // 变异验证 M-D5：GroupPowerView 加 `public double Ratio => (double)Power / 2;` → 本测试红 1。
        Type[] floats = [typeof(double), typeof(float), typeof(decimal)];
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<string>();
        foreach (Assembly assembly in new[] { typeof(GameBoard).Assembly, PresentationAssembly })
        {
            foreach (Type type in assembly.GetTypes())
            {
                found.AddRange(type.GetFields(all).Where(f => IsFloat(f.FieldType, floats)).Select(f => $"{type.Name}.{f.Name}"));
                found.AddRange(type.GetProperties(all).Where(p => IsFloat(p.PropertyType, floats)).Select(p => $"{type.Name}.{p.Name}"));
                foreach (MethodBase method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                {
                    if (method is MethodInfo mi && IsFloat(mi.ReturnType, floats))
                    {
                        found.Add($"{type.Name}.{method.Name} 返回");
                    }

                    found.AddRange(method.GetParameters().Where(p => IsFloat(p.ParameterType, floats)).Select(p => $"{type.Name}.{method.Name}({p.Name})"));
                    found.AddRange((method.GetMethodBody()?.LocalVariables ?? []).Where(l => IsFloat(l.LocalType, floats)).Select(l => $"{type.Name}.{method.Name} 局部#{l.LocalIndex}"));
                }
            }

            found.AddRange(IlReferences(assembly)
                .Where(r => r.OpCode == OpCodes.Ldc_R4 || r.OpCode == OpCodes.Ldc_R8 || r.OpCode == OpCodes.Conv_R4 || r.OpCode == OpCodes.Conv_R8 || r.OpCode == OpCodes.Conv_R_Un)
                .Select(r => $"{r.Caller.DeclaringType!.Name}.{r.Caller.Name} {r.OpCode.Name}"));
        }

        // 唯一豁免：MatchOptions 的静态初始化 `TimeSpan.FromSeconds(15)`（插旗时限，非计分路径，match-flow 既有代码，本 change 只做纯增量不改它）。
        Assert.Equal(["MatchOptions..cctor ldc.r8"], found);
    }

    private static bool IsFloat(Type type, Type[] floats) =>
        floats.Contains(Nullable.GetUnderlyingType(type) ?? type) || (type.HasElementType && IsFloat(type.GetElementType()!, floats));

    private static bool IsForbidden(MethodBase method)
    {
        Type? declaring = method.DeclaringType;
        if (declaring is null)
        {
            return false;
        }

        if (declaring.Namespace == typeof(Ai.HeuristicAi).Namespace)
        {
            return true;
        }

        return ForbiddenTypes.Contains(declaring) || ForbiddenMembers.Contains((declaring, method.Name));
    }
}
