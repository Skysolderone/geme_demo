using System.Collections.Immutable;
using Siege.Core.Determinism;

namespace Siege.Core.Board.Maps;

/// <summary>一块棋盘的宽与高。</summary>
public readonly record struct BoardSize(int Width, int Height);

/// <summary>
/// 一次棋盘档生成的结果：地图、实际采用的尝试序号（只供诊断，不进标识）、各条通道两端的棋盘下标（棋盘清单的下标），
/// 以及此前各次作废尝试的原因（按尝试次序，只供诊断）。
/// </summary>
public sealed record GeneratedBoardMap(MapData Map, int Attempt, ImmutableArray<(int From, int To)> Links, ImmutableArray<string> Discarded);

/// <summary>
/// 棋盘档 4 人地图的确定性生成器（board-map D3–D8）：<c>(地图种子, 参数) → MapData</c>。
/// 同一种子与参数在任何机器上得到逐字节相同的地图；全程整数运算，随机只来自 <see cref="MapRandom.ForBoardAttempt"/>，
/// 不读时钟与环境，也拿不到对局种子。与边疆档生成器互不相干：随机序列不同构，源码不共用工作态。
/// </summary>
/// <remarks>
/// <para><b>校验闭环</b>：第 k 次尝试（k 从 0 起）先抽各棋盘的边长（朝 300–800 的目标带收敛，落不进即作废），
/// 再在 50×50 的工作区里摆放，摆完后按全部棋盘与通道的实际外接范围四周各留 2 格裁出图面（列数与行数各自独立、各在 20–50；
/// 工作区放不下或裁出尺寸越界即作废）。每一步的随机源只由（地图种子, 序号）决定：
/// 第 k 次尝试的抽样用第 <c>2k</c> 个随机源，摆放与布点用第 <c>2k + 1</c> 个。
/// 摆成之后依次过棋盘档静态校验（<see cref="MapValidator"/>，与地图文件同一套）与布局规则自检（<see cref="CheckLayout"/>）；
/// 任一关不过即换下一次尝试；到上限抛 <see cref="MapGenerationException"/>，MUST NOT 返回未通过校验的地图。</para>
/// <para>规格：openspec/changes/board-map/specs/map-generation</para>
/// </remarks>
public static class BoardMapGenerator
{
    /// <summary>尝试上限的缺省值。</summary>
    public const int DefaultMaxAttempts = 64;

    /// <summary>可落子格总数（棋盘 + 通道）的目标带下限。</summary>
    public const int TargetMinPlayable = 300;

    /// <summary>可落子格总数（棋盘 + 通道）的目标带上限。</summary>
    public const int TargetMaxPlayable = 800;

    /// <summary>按完整标识（<c>board:&lt;种子&gt;[:n&lt;N&gt;]</c>）生成。</summary>
    public static MapData Generate(string mapId)
    {
        (ulong seed, BoardMapParameters parameters) = BoardMapId.Parse(mapId);
        return Generate(seed, parameters);
    }

    /// <summary>生成地图。<see cref="MapData.Id"/> 为规范化标识。</summary>
    public static MapData Generate(ulong mapSeed, BoardMapParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts).Map;

    /// <summary>生成地图，并给出实际采用的尝试序号、通道清单与作废尝试的原因。</summary>
    public static GeneratedBoardMap GenerateDetailed(ulong mapSeed, BoardMapParameters? parameters = null, int maxAttempts = DefaultMaxAttempts) =>
        GenerateDetailed(mapSeed, parameters, maxAttempts, fixedBirths: null, fixedPublics: null);

    /// <summary>
    /// 测试入口：棋盘边长不抽样、直接用给定的（出生棋盘 5 块、公共棋盘数与参数一致，第一块公共棋盘放在中央），
    /// 其余（摆放、通道、布点、校验闭环）与公开入口完全相同。两个数组要么都给、要么都不给。
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
            || (fixedBirths is not null && (fixedBirths.Length != BoardMapParameters.BirthBoards || fixedPublics!.Length != parameters.PublicBoards)))
        {
            throw new ArgumentException("给定边长时，出生棋盘必须恰 5 块、公共棋盘数必须与参数一致。");
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
        BoardSize[] births;
        BoardSize[] publics;
        if (fixedBirths is not null)
        {
            births = fixedBirths;
            publics = fixedPublics!;
        }
        else if (!BoardMapLayout.TrySampleSizes(
            MapRandom.ForBoardAttempt(mapSeed, first), parameters.PublicBoards, TargetMinPlayable, TargetMaxPlayable,
            out births, out publics, out string unfit))
        {
            reason = $"抽样作废：{unfit}";
            return false;
        }

        var layout = new BoardMapLayout(MapRandom.ForBoardAttempt(mapSeed, first + 1), births, publics);
        if (!layout.TryBuild(out string broken))
        {
            reason = $"摆放作废：{broken}";
            return false;
        }

        int playable = layout.PlayableCount();
        if (playable < TargetMinPlayable || playable > TargetMaxPlayable)
        {
            reason = $"规模作废：可落子格 {playable} 落在目标带 {TargetMinPlayable}–{TargetMaxPlayable} 之外。";
            return false;
        }

        MapData map = ToMapData(id, layout);
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

        made = new GeneratedBoardMap(map, attempt, [.. layout.Links.Select(link => (link.From, link.To))], []);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 布局规则自检：静态校验不查、但规格「棋盘档布局规则」要求的特征。返回第一条不满足的说明；全部满足为 <c>null</c>。
    /// </summary>
    private static string? CheckLayout(MapData map, BoardMapLayout layout)
    {
        int boards = layout.Boards.Length;
        var links = new int[boards];
        var reachesPublic = new bool[boards];
        var adjacent = new bool[boards, boards];
        foreach (BoardMapLayout.Link link in layout.Links)
        {
            links[link.From]++;
            links[link.To]++;
            adjacent[link.From, link.To] = true;
            adjacent[link.To, link.From] = true;
            reachesPublic[link.From] |= !layout.IsBirth(link.To);
            reachesPublic[link.To] |= !layout.IsBirth(link.From);
        }

        bool anchored = false;
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

            anchored |= !birth && rect.W == BoardMapLayout.AnchorSide && rect.H == BoardMapLayout.AnchorSide;

            int fewest = birth ? BoardMapLayout.BirthMinLinks : 1;
            if (links[i] < fewest || links[i] > layout.MaxLinks(i))
            {
                return $"第 {i + 1} 块{(birth ? "出生" : "公共")}棋盘有 {links[i]} 条通道，不在 {fewest}–{layout.MaxLinks(i)} 之内。";
            }

            if (birth)
            {
                int neighbors = 0;
                for (int j = 0; j < boards; j++)
                {
                    neighbors += adjacent[i, j] ? 1 : 0;
                }

                if (neighbors < 2)
                {
                    return $"第 {i + 1} 块出生棋盘的通道全部通向同一块棋盘，应通向至少两块不同的棋盘。";
                }

                if (!reachesPublic[i])
                {
                    return $"第 {i + 1} 块出生棋盘没有直通公共棋盘的通道。";
                }
            }

            int want = birth ? 1 : (rect.W < rect.H ? rect.W : rect.H) <= BoardMapLayout.SmallPublicSide ? 2 : 3;
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

        if (!anchored)
        {
            return $"没有一块公共棋盘的宽与高都为 {BoardMapLayout.AnchorSide}。";
        }

        if (relics != map.RelicCells.Count)
        {
            return $"有 {map.RelicCells.Count - relics} 个信物格不在任何棋盘内。";
        }

        return map.RelicCells.TryGetValue(map.CentralEntrance, out RelicCellSpec spec) && spec.Budget == BudgetTier.High
            ? null
            : "中央入口上没有高档信物格。";
    }

    /// <summary>把工作态网格灌成不可变地图数据：棋盘与通道之外的格一律障碍（填场景），通道格全部标为咽喉；全图 h=0 草地。</summary>
    private static MapData ToMapData(string id, BoardMapLayout layout)
    {
        var obstacles = ImmutableHashSet.CreateBuilder<Coord>();
        var chokes = ImmutableHashSet.CreateBuilder<Coord>();
        var relics = ImmutableDictionary.CreateBuilder<Coord, RelicCellSpec>();
        var zones = new ImmutableHashSet<Coord>.Builder[BoardMapParameters.BirthBoards];
        for (int i = 0; i < zones.Length; i++)
        {
            zones[i] = ImmutableHashSet.CreateBuilder<Coord>();
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
                else if (cell < 0)
                {
                    chokes.Add(c);
                }
                else if (layout.IsBirth(cell - 1))
                {
                    zones[cell - 1].Add(c);
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

        return new MapData
        {
            Id = id,
            Width = layout.Width,
            Height = layout.Height,
            MaxPlayers = 4,
            Profile = MapProfile.Board,
            Obstacles = obstacles.ToImmutable(),
            BirthZones = [.. zones.Select(z => z.ToImmutable())],
            Boards = plates.MoveToImmutable(),
            RelicCells = relics.ToImmutable(),
            ChokePoints = chokes.ToImmutable(),
            CentralEntrance = new Coord(layout.Entrance.X, layout.Entrance.Y),
        };
    }
}
