using System.Diagnostics;
using Siege.Core.Batch;
using Siege.Core.Board;
using Xunit.Abstractions;

namespace Siege.Core.Tests.CaptureResolution;

/// <summary>
/// 守门（tasks 2.3，不对应 Scenario）：已确定活形棋串在任何非所有者的合法批次后仍为已确定活形——
/// "仍为已确定活形"包含"原有的每一枚棋子都还在盘上"，即没有被提走（design D4）。
/// </summary>
/// <remarks>
/// <para>随机局面性质测试：每个种子在 13×13 盘面上摆 4 个活形模板（两单格眼环、带深水墙的 6 格眼、直四、贴岩石的一字两眼），
/// 余下格随机撒子 / 深水 / 林地；随后 40 个随机批次，行动方随机，落点偏向他人活形附近，七成是带随机合法改造的匠人。
/// 每个确认成功的批次后，对"批次开始前为已确定活形、所有者不是行动方"的每条棋串逐子核对。</para>
/// <para>样本口径下界（testing.md）：全部种子累计必须出现过破坏活形拒绝、活棋禁入拒绝与受保护棋串核对，
/// 否则"去掉第 6 步"这类变异不会让本测试红。</para>
/// <para>默认套件跑种子 1–20；种子 1–200 设 <c>SIEGE_SLOW=1</c> 并按 <c>--filter "Category=Slow"</c> 单独运行（裁决 R3 / R7）。</para>
/// </remarks>
public class 活形保护性质Tests(ITestOutputHelper output)
{
    private const int Size = 13;
    private const int BatchesPerSeed = 40;

    private static readonly string[][] Templates =
    [
        // 两单格眼环
        ["00000", "0.0.0", "00000"],
        // 6 格眼空间，右侧一格是未架桥深水（搭桥漏眼的靶子）
        ["00000", "0...~", "0...0", "00000"],
        // 直四
        ["000000", "0....0", "000000"],
        // 一字两眼：每个眼只有一条气边（其余三面岩石），一道栅栏就能切串或隔眼
        [".00000.", "#.###.#", "#######"],
    ];

    [Fact]
    public void 已确定活形在非所有者的合法批次后仍为已确定活形() => Run(1, 20);

    [SlowFact]
    [Trait("Category", "Slow")]
    public void 已确定活形在非所有者的合法批次后仍为已确定活形_种子1至200() => Run(1, 200);

    private void Run(int firstSeed, int lastSeed)
    {
        var watch = Stopwatch.StartNew();
        var totals = new Tally();
        var violations = new List<string>();
        for (int seed = firstSeed; seed <= lastSeed; seed++)
        {
            RunSeed(seed, totals, violations);
        }

        watch.Stop();
        output.WriteLine(
            $"种子 {firstSeed}–{lastSeed}：批次 {totals.Batches}，确认 {totals.Confirmed}，受保护棋串核对 {totals.ProtectedChecks}，" +
            $"拒绝 {string.Join("，", totals.Rejections.Select(kv => $"{kv.Key}={kv.Value}"))}；耗时 {watch.Elapsed.TotalSeconds:F1} s");

        Assert.Empty(violations);
        Assert.True(totals.ProtectedChecks > 0, "样本里没有任何受保护棋串被核对过。");
        Assert.True(totals.Rejections.GetValueOrDefault(BatchFailureKind.BreaksLife) > 0, "样本里没有出现破坏活形拒绝，第 6 步未被触发。");
        Assert.True(totals.Rejections.GetValueOrDefault(BatchFailureKind.LifeForbidden) > 0, "样本里没有出现活棋禁入拒绝。");
    }

    private static void RunSeed(int seed, Tally totals, List<string> violations)
    {
        var rng = new Random(seed);
        GameBoard board = Build(rng);
        SettlementDriver driver = BatchFixtures.Driver(board);
        for (int i = 0; i < BatchesPerSeed; i++)
        {
            var mover = new PlayerId(rng.Next(4));
            LifeShapeReport before = LifeShapeReport.Analyze(board);
            Group[] protectedGroups = [.. before.Groups.Where(g => g.Life == LifeState.Alive && g.Group.Owner != mover).Select(g => g.Group)];
            Placement[] batch = RandomBatch(rng, board, before, mover);
            totals.Batches++;

            SettlementOutcome outcome = driver.Confirm(BatchFixtures.Context(board, mover), batch);
            if (!outcome.Confirmed)
            {
                BatchFailureKind kind = outcome.Failure!.Kind;
                totals.Rejections[kind] = totals.Rejections.GetValueOrDefault(kind) + 1;
                continue;
            }

            totals.Confirmed++;
            LifeShapeReport after = LifeShapeReport.Analyze(board);
            foreach (Group group in protectedGroups)
            {
                totals.ProtectedChecks++;
                foreach (Coord stone in group.Stones)
                {
                    if (after.GroupLifeAt(stone) is not { Life: LifeState.Alive } now || now.Group.Owner != group.Owner)
                    {
                        violations.Add(
                            $"种子 {seed} 第 {i + 1} 批（{mover}：{string.Join("，", batch)}）后，{group.Owner} 的活形棋串 " +
                            $"{string.Join(",", group.Stones.Notations())} 在 {stone.ToNotation()} 失活。");
                        break;
                    }
                }
            }
        }
    }

    /// <summary>1–3 枚；八成落点取自他人活形棋串与其眼空间周围两格内（含眼空间本身，好让第 1 步有机会拒绝），七成是带随机合法改造的匠人。</summary>
    private static Placement[] RandomBatch(Random rng, GameBoard board, LifeShapeReport life, PlayerId mover)
    {
        Coord[] empty = [.. board.AllCoords().Where(c => board[c].IsPlayableEmpty).Order()];
        Coord[] targets = [.. life.Groups.Where(g => g.Life == LifeState.Alive && g.Group.Owner != mover)
            .SelectMany(g => g.Group.Stones.Concat(g.EyeSpaces.SelectMany(e => e.Cells)))];
        Coord[] near = [.. empty.Where(c => targets.Any(t => Math.Abs(t.X - c.X) + Math.Abs(t.Y - c.Y) <= 2))];

        int count = 1 + rng.Next(3);
        var placements = new List<Placement>();
        for (int k = 0; k < count && empty.Length > 0; k++)
        {
            Coord[] pool = near.Length > 0 && rng.NextDouble() < 0.8 ? near : empty;
            Coord cell = pool[rng.Next(pool.Length)];
            if (placements.Any(p => p.Coord == cell))
            {
                continue;
            }

            if (rng.NextDouble() < 0.7)
            {
                System.Collections.Immutable.ImmutableArray<TerrainEdit> options = TerrainEditRules.LegalTargets(board.Map, cell);
                TerrainEdit? edit = options.IsEmpty ? null : options[rng.Next(options.Length)];
                if (edit is { } e && placements.Any(p => p.Edit == e))
                {
                    edit = null;
                }

                placements.Add(new Placement(cell, PieceType.Artisan, edit));
            }
            else
            {
                placements.Add(new Placement(cell, PieceType.Basic));
            }
        }

        return [.. placements];
    }

    private static GameBoard Build(Random rng)
    {
        char[,] grid = new char[Size, Size];
        bool[,] reserved = new bool[Size, Size];
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                grid[r, c] = '.';
            }
        }

        foreach (string[] template in Templates.OrderBy(_ => rng.Next()).ToArray())
        {
            char owner = (char)('0' + rng.Next(4));
            int h = template.Length;
            int w = template[0].Length;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int top = rng.Next(Size - h + 1);
                int left = rng.Next(Size - w + 1);
                if (!Free(reserved, top - 1, left - 1, h + 2, w + 2))
                {
                    continue;
                }

                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        char t = template[r][c];
                        grid[top + r, left + c] = t == '0' ? owner : t;
                    }
                }

                for (int r = Math.Max(0, top - 1); r < Math.Min(Size, top + h + 1); r++)
                {
                    for (int c = Math.Max(0, left - 1); c < Math.Min(Size, left + w + 1); c++)
                    {
                        reserved[r, c] = true;
                    }
                }

                break;
            }
        }

        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                if (reserved[r, c])
                {
                    continue;
                }

                double roll = rng.NextDouble();
                grid[r, c] = roll switch
                {
                    < 0.20 => (char)('0' + rng.Next(4)),
                    < 0.24 => '~',
                    < 0.28 => 'T',
                    _ => '.',
                };
            }
        }

        string[] rows = [.. Enumerable.Range(0, Size).Select(r => new string([.. Enumerable.Range(0, Size).Select(c => grid[r, c])]))];
        return LifeShapeFixtures.Grid(rows);
    }

    private static bool Free(bool[,] reserved, int top, int left, int h, int w)
    {
        for (int r = Math.Max(0, top); r < Math.Min(Size, top + h); r++)
        {
            for (int c = Math.Max(0, left); c < Math.Min(Size, left + w); c++)
            {
                if (reserved[r, c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private sealed class Tally
    {
        internal int Batches { get; set; }

        internal int Confirmed { get; set; }

        internal int ProtectedChecks { get; set; }

        internal SortedDictionary<BatchFailureKind, int> Rejections { get; } = [];
    }
}

/// <summary>只在环境变量 <c>SIEGE_SLOW=1</c> 时运行的慢测试；默认套件里显示为 Skipped（testing.md「慢测试与计时测试」）。</summary>
public sealed class SlowFactAttribute : FactAttribute
{
    public SlowFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SIEGE_SLOW") != "1")
        {
            Skip = "慢测试：设 SIEGE_SLOW=1 后按 --filter \"Category=Slow\" 单独运行。";
        }
    }
}
