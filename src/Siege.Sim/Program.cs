using Siege.Core.Board;
using Siege.Core.Board.Maps;

MapData map = FourPlayerBaseMap.Create();
MapValidationResult result = MapValidator.Validate(map);

Console.WriteLine($"地图 {map.Id}  {map.Width}×{map.Height}");
Console.WriteLine($"可落子格 {map.PlayableCount}   障碍 {map.Obstacles.Count} ({map.Obstacles.Count * 100.0 / (map.Width * map.Height):F1}%)");
Console.WriteLine($"出生区 {map.BirthZones.Length} 个，各 {string.Join("/", map.BirthZones.Select(z => z.Count))} 格");
Console.WriteLine($"信物格 {map.RelicCells.Count}（出生区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.BirthZone)}，公共区 {map.RelicCells.Count(r => r.Value.Zone == RelicZone.Contested)}）");
Console.WriteLine($"咽喉 {string.Join(" ", map.ChokePoints.Order())}   中央入口 {map.CentralEntrance}");
Console.WriteLine();
Console.WriteLine(result);
Console.WriteLine();

for (int y = map.Height - 1; y >= 0; y--)
{
    Console.Write($"{y + 1,3} ");
    for (int x = 0; x < map.Width; x++)
    {
        Coord c = new(x, y);
        char ch = map.TerrainAt(c) == Terrain.Obstacle ? '#'
            : map.RelicCells.TryGetValue(c, out RelicCellSpec spec)
                ? spec.Budget switch { BudgetTier.Birth => 'r', BudgetTier.High => 'R', _ => 'o' }
            : c == map.CentralEntrance ? '@'
            : map.ChokePoints.Contains(c) ? '^'
            : map.BirthZoneOf(c) is { } z ? (char)('1' + z)
            : '.';
        Console.Write($"{ch} ");
    }

    Console.WriteLine();
}

Console.Write("    ");
for (int x = 0; x < map.Width; x++)
{
    Console.Write($"{Coord.ColumnLetters[x]} ");
}

Console.WriteLine();
Console.WriteLine();
Console.WriteLine("# 障碍  1-4 出生区  r 出生区信物  o 公共信物  R 公共高档信物  ^ 咽喉  @ 中央入口");

// 导出地图文件，供设计师脱离代码维护
Directory.CreateDirectory("maps");
string path = Path.Combine("maps", $"{map.Id}.json");
File.WriteAllText(path, MapFile.ToJson(map));
Console.WriteLine();
Console.WriteLine($"已导出 {path}");
