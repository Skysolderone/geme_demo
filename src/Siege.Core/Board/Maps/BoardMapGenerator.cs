using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>一块棋盘的宽与高。</summary>
public readonly record struct BoardSize(int Width, int Height);

/// <summary>
/// 一次棋盘档生成的结果：地图、实际采用的尝试序号（只供诊断，不进标识），以及此前各次作废尝试的原因（按尝试次序，只供诊断）。
/// </summary>
public sealed record GeneratedBoardMap(MapData Map, int Attempt, ImmutableArray<string> Discarded);

/// <summary>
/// 棋盘档 2 / 3 / 4 人地图的确定性生成器（board-map D3–D8、board-isolated-gen D1–D3 / D5）：<c>(地图种子, 参数) → MapData</c>。
/// 同一种子与参数在任何机器上得到逐字节相同的地图；全程整数运算，随机只来自 <see cref="MapRandom.ForBoardAttempt"/>，
/// 不读时钟与环境，也拿不到对局种子。与边疆档生成器互不相干：随机序列不同构，源码不共用工作态。
/// </summary>
/// <remarks>
/// <para><b>校验闭环</b>：第 k 次尝试（k 从 0 起）先抽各棋盘的边长（按原分布整组重抽直到落进人数对应的目标带，至多 200 组，仍不进即作废），
/// 再在 60×60 的工作区里摆放（棋盘之间不开通道），摆完后按全部棋盘的实际外接范围四周各留 2 格裁出图面（列数与行数各自独立、各在 15–60；
/// 工作区放不下或裁出尺寸越界即作废）。每一步的随机源只由（地图种子, 人数, 序号）决定——同一种子不同人数的图互不相关：
/// 第 k 次尝试的抽样用第 <c>2k</c> 个随机源，摆放与布点用第 <c>2k + 1</c> 个。
/// 摆成之后依次过棋盘档静态校验（<see cref="MapValidator"/>，与地图文件同一套）与布局规则自检（<see cref="CheckLayout"/>）；
/// 任一关不过即换下一次尝试；到上限抛 <see cref="MapGenerationException"/>，MUST NOT 返回未通过校验的地图。</para>
/// <para>规格：openspec/changes/board-isolated-gen/specs/map-generation</para>
/// </remarks>
public static class BoardMapGenerator
{
    /// <summary>尝试上限的缺省值。</summary>
    public const int DefaultMaxAttempts = 64;

    /// <summary>可落子格总数（= Σ 棋盘面积）的目标带：4 人 300–800、3 人 225–600、2 人 150–400（负责人裁决 2026-10-06 第 8 条：按人数缩放）。</summary>
    public static (int Min, int Max) TargetPlayable(int players) => (75 * players, 200 * players);

    /// <summary>按完整标识（<c>board:&lt;种子&gt;[:p&lt;人数&gt;][:n&lt;N&gt;]</c>）生成。</summary>
    public static MapData Generate(string mapId)
    {
        (ulong seed, BoardMapParameters parameters) = BoardMapId.Parse(mapId);
        return Generate(seed, parameters);
    }

    /// <summary>生成地图。<see cref="MapData.Id"/> 为规范化标识。</summary>
    public static MapData Generate(ulong mapSeed, BoardMapParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts).Map;

    /// <summary>生成地图，并给出实际采用的尝试序号与作废尝试的原因。</summary>
    public static GeneratedBoardMap GenerateDetailed(ulong mapSeed, BoardMapParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts, fixedBirths: null, fixedPublics: null);

    /// <summary>
    /// 测试入口：棋盘边长不抽样、直接用给定的（出生棋盘数 = 人数 + 1、公共棋盘数与参数一致，第一块公共棋盘放在中央），
    /// 其余（摆放、布点、校验闭环）与公开入口完全相同。两个数组要么都给、要么都不给。
    /// </summary>
    internal static GeneratedBoardMap GenerateDetailed(
        ulong mapSeed, BoardMapParameters? parameters, int maxAttempts, BoardSize[]? fixedBirths, BoardSize[]? fixedPublics)
    {
        parameters ??= BoardMapParameters.Default;
        parameters.EnsureValid();
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "尝试上限至少为 1。");
        }

        if ((fixedBirths is null) != (fixedPublics is null)
            || (fixedBirths is not null && (fixedBirths.Length != parameters.BirthBoards || fixedPublics!.Length != parameters.PublicBoards)))
        {
            throw new ArgumentException($"给定边长时，出生棋盘必须恰 {parameters.BirthBoards} 块（人数 + 1）、公共棋盘数必须与参数一致。");
        }

        string id = BoardMapId.Format(mapSeed, parameters);
        ImmutableArray<string>.Builder discarded = ImmutableArray.CreateBuilder<string>();
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (TryAttempt(id, mapSeed, attempt, parameters, fixedBirths, fixedPublics, out GeneratedBoardMap? made, out string reason))
            {
                return made! with { Discarded = discarded.ToImmutable() };
            }

            discarded.Add($"第 {attempt} 次尝试{reason}");
        }

        throw new MapGenerationException(
            $"地图 {id} 在 {maxAttempts} 次尝试内没有得到通过校验的地图。最后一次的失败原因——{discarded[^1]}");
    }

    /// <summary>一次尝试：抽样 → 摆放并裁出图面 → 目标带 → 静态校验 → 布局自检。作废时 <paramref name="reason"/> 说明原因。</summary>
    private static bool TryAttempt(
        string id, ulong mapSeed, int attempt, BoardMapParameters parameters, BoardSize[]? fixedBirths, BoardSize[]? fixedPublics,
        out GeneratedBoardMap? made, out string reason)
    {
        made = null;
        int first = checked(attempt * 2);
        (int minPlayable, int maxPlayable) = TargetPlayable(parameters.Players);
        BoardSize[] births;
        BoardSize[] publics;
        if (fixedBirths is not null)
        {
            births = fixedBirths;
            publics = fixedPublics!;
        }
        else if (!BoardMapLayout.TrySampleSizes(
            MapRandom.ForBoardAttempt(mapSeed, parameters.Players, first), parameters.BirthBoards, parameters.PublicBoards, minPlayable, maxPlayable,
            out births, out publics, out _, out string unfit))
        {
            reason = $"抽样作废：{unfit}";
            return false;
        }

        var layout = new BoardMapLayout(MapRandom.ForBoardAttempt(mapSeed, parameters.Players, first + 1), births, publics);
        if (!layout.TryBuild(out string broken))
        {
            reason = $"摆放作废：{broken}";
            return false;
        }

        int playable = layout.PlayableCount();
        if (playable < minPlayable || playable > maxPlayable)
        {
            reason = $"规模作废：可落子格 {playable} 落在 {parameters.Players} 人目标带 {minPlayable}–{maxPlayable} 之外。";
            return false;
        }

        MapData map = ToMapData(id, parameters.Players, layout);
        MapValidationResult validation = MapValidator.Validate(map);
        if (!validation.IsValid)
        {
            reason = $"未通过棋盘档静态校验：{string.Join("；", validation.Failures)}";
            return false;
        }

        if (CheckLayout(map, layout) is { } rule)
        {
            reason = $"不满足布局规则：{rule}";
            return false;
        }

        made = new GeneratedBoardMap(map, attempt, []);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 布局规则自检：静态校验不查、但规格「棋盘档布局规则」要求的特征（边长分档、主战场、信物分档与位置、中央入口的高档信物）。
    /// 返回第一条不满足的说明；全部满足为 <c>null</c>。
    /// </summary>
    private static string? CheckLayout(MapData map, BoardMapLayout layout)
    {
        int boards = layout.Boards.Length;
        int largest = -1;
        int relics = 0;
        for (int i = 0; i < boards; i++)
        {
            BoardMapLayout.Rect rect = layout.Boards[i];
            bool birth = layout.IsBirth(i);
            int min = birth ? BoardMapLayout.BirthMinSide : BoardMapLayout.PublicMinSide;
            int max = birth ? BoardMapLayout.BirthMaxSide : BoardMapLayout.PublicMaxSide;
            if (rect.W < min || rect.W > max || rect.H < min || rect.H > max)
            {
                return $"第 {i + 1} 块棋盘 {rect.W}×{rect.H} 的边长不在 {min}–{max} 之内。";
            }

            if (!birth && (largest < 0 || rect.Area > layout.Boards[largest].Area))
            {
                largest = i;
            }

            int want = birth ? 1 : BoardMapLayout.PublicRelicsFor(rect.W < rect.H ? rect.W : rect.H);
            int found = 0;
            for (int y = rect.Y; y <= rect.Y1; y++)
            {
                for (int x = rect.X; x <= rect.X1; x++)
                {
                    if (!map.RelicCells.ContainsKey(new Coord(x, y)))
                    {
                        continue;
                    }

                    found++;
                    if (x == rect.X || x == rect.X1 || y == rect.Y || y == rect.Y1)
                    {
                        return $"信物格 {new Coord(x, y)} 在棋盘的最外一圈。";
                    }
                }
            }

            if (found != want)
            {
                return $"第 {i + 1} 块棋盘上有 {found} 个信物格，应为 {want} 个。";
            }

            relics += found;
        }

        BoardMapLayout.Rect main = layout.Boards[largest];
        if (main.W < BoardMapLayout.MainMinSide || main.H < BoardMapLayout.MainMinSide)
        {
            return $"面积最大的公共棋盘 {main.W}×{main.H} 的宽与高须都不小于 {BoardMapLayout.MainMinSide}。";
        }

        if (relics != map.RelicCells.Count)
        {
            return $"有 {map.RelicCells.Count - relics} 个信物格不在任何棋盘内。";
        }

        return map.RelicCells.TryGetValue(map.CentralEntrance, out RelicCellSpec spec) && spec.Budget == BudgetTier.High
            ? null
            : "中央入口上没有高档信物格。";
    }

    /// <summary>
    /// 把工作态网格灌成不可变地图数据：棋盘之外的格一律障碍（填场景）；不标咽喉（棋盘档豁免）；全图 h=0 草地；人数上限 = 参数人数（D5）。
    /// </summary>
    private static MapData ToMapData(string id, int players, BoardMapLayout layout)
    {
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var zoneBuilders = new ImmutableHashSet<Coord>.Builder[BoardMapParameters.BirthBoardsFor(players)];
        for (int i = 0; i < zoneBuilders.Length; i++)
        {
            zoneBuilders[i] = ImmutableHashSet.CreateBuilder<Coord>();
        }

        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                var c = new Coord(x, y);
                int cell = layout.Grid[x, y];
                if (cell == 0)
                {
                    obstacles.Add(c);
                }
                else if (layout.IsBirth(cell - 1))
                {
                    zoneBuilders[cell - 1].Add(c);
                }

                if (layout.Relics[x, y] is { } spec)
                {
                    relics[c] = spec;
                }
            }
        }

        var plates = ImmutableArray.CreateBuilder<BoardPlate>(layout.Boards.Length);
        for (int i = 0; i < layout.Boards.Length; i++)
        {
            BoardMapLayout.Rect rect = layout.Boards[i];
            plates.Add(new BoardPlate(
                new Coord(rect.X, rect.Y), rect.W, rect.H, layout.IsBirth(i) ? BoardPlateKind.Birth : BoardPlateKind.Public));
        }

        var zones = ImmutableArray.CreateBuilder<ImmutableHashSet<Coord>>(zoneBuilders.Length);
        for (int i = 0; i < zoneBuilders.Length; i++)
        {
            zones.Add(zoneBuilders[i].ToImmutable());
        }

        return new MapData
        {
            Id = id,
            Width = layout.Width,
            Height = layout.Height,
            MaxPlayers = players,
            Profile = MapProfile.Board,
            Obstacles = obstacles.ToImmutable(),
            BirthZones = zones.MoveToImmutable(),
            Boards = plates.MoveToImmutable(),
            RelicCells = relics.ToImmutable(),
            ChokePoints = [],
            CentralEntrance = new Coord(layout.Entrance.X, layout.Entrance.Y),
        };
    }
}
