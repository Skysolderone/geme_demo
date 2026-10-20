import sys, subprocess, os, time, shutil, re
sys.stdout.reconfigure(encoding='utf-8')
R = r'E:/wws/geme_demo/'
S = R + 'src/Siege.Core/Board/Maps/LinkMapGenerator.Surfaces.cs'
G = R + 'src/Siege.Core/Board/Maps/LinkMapGenerator.cs'
V = R + 'src/Siege.Core/Board/MapValidator.cs'
P = R + 'src/Siege.Sim/Program.cs'
muts = [
 ('M-LS1 河流配对改沼泽', S, "BoardTheme.River => Surface.Shallows,", "BoardTheme.River => Surface.Marsh,"),
 ('M-LS2 大棋盘也去掉 S1', S, "&& !core.Any(k => Adjacency.AreAdjacent(k, c));", "&& (c.X >= 0 || !core.Any(k => Adjacency.AreAdjacent(k, c)));"),
 ('M-LS3 不排除信物格', S, "&& !relics.ContainsKey(c)\n", "&& (c.X >= 0 || !relics.ContainsKey(c))\n"),
 ('M-LS4 先退基础候选', S, "List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c) && Fits(board, surface, c))];\n                if (pool.Count == 0)\n                {\n                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // S2", "List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c))];\n                if (pool.Count == 0)\n                {\n                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // S2"),
 ('M-LS5 小中间带目标 3', S, "int target = middle.Count <= SmallMiddleBand ? 2 :", "int target = middle.Count <= SmallMiddleBand ? 3 :"),
 ('M-LS6 记数写目标', S, "SpecialSurfaces = placed.Count };", "SpecialSurfaces = target };"),
 ('M-LS8 浅滩条件改林地', S, "Surface.Shallows => board.Surfaces.Any(kv => kv.Value == Surface.DeepWater &&", "Surface.Shallows => board.Surfaces.Any(kv => kv.Value == Surface.Forest &&"),
 ('M-LF1 去掉 F1（小棋盘也查 S1）', S, "Coord[] core = middle.Count <= SmallMiddleBand ? [] : [.. plate.CoreCells()];", "Coord[] core = middle.Count < 0 ? [] : [.. plate.CoreCells()];"),
 ('M-LF2 混合不补位', S, "pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // F2", "pool = [.. basis.Where(c => !placed.ContainsKey(c) && c.X < 0)];   // F2"),
 ('M-LF2b 混合直接用基础候选', S, "List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c) && Fits(board, surface, c))];\n                if (pool.Count == 0)\n                {\n                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // F2", "List<Coord> pool = [.. basis.Where(c => !placed.ContainsKey(c))];\n                if (pool.Count == 0)\n                {\n                    pool = [.. basis.Where(c => !placed.ContainsKey(c))];   // F2"),
 ('M-LF3 混合同种可重复', S, "                if (pool.Count > 0)\n                {\n                    placed[pool[rng.NextInt(pool.Count)]] = surface;\n                }", "                if (pool.Count > 0)\n                {\n                    placed[pool[rng.NextInt(pool.Count)]] = surface;\n                    pool.RemoveAll(placed.ContainsKey);\n                    if (pool.Count > 0 && placed.Count < target) { placed[pool[0]] = surface; }\n                }"),
 ('M-LP1 map 行全图计数', P, "int special = plate.Cells().Count(c =>", "int special = map.AllCoords().Count(c =>"),
]
only = sys.argv[1:] 
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en')
flt = 'FullyQualifiedName~LinkMapGeneration|FullyQualifiedName~地图静态校验规则Tests|FullyQualifiedName~各入口支持生成图_链接模式Tests|FullyQualifiedName~新地表判断唯一Tests'
for name, path, a, b in muts:
    if only and not any(name.startswith(o) for o in only): continue
    orig = open(path, 'rb').read()
    eol = b'\r\n' if orig.count(b'\r\n') else b'\n'
    A = a.replace('\r\n', '\n').replace('\n', eol.decode()).encode('utf-8')
    B = b.replace('\r\n', '\n').replace('\n', eol.decode()).encode('utf-8')
    bak = path + f'.bak{int(time.time()*1000)}'
    shutil.copy(path, bak)
    res = ''
    try:
        assert orig.count(A) == 1, f'{name}: anchor count {orig.count(A)}'
        open(path, 'wb').write(orig.replace(A, B))
        r = subprocess.run(['dotnet', 'test', R + 'tests/Siege.Core.Tests', '-c', 'Release', '--filter', flt], capture_output=True, env=env)
        out = r.stdout.decode('utf-8', 'replace') + r.stderr.decode('utf-8', 'replace')
        failed = sorted(set(re.findall(r'^\s*Failed (\S+)', out, re.M)))
        stat = re.findall(r'(Passed!|Failed!)\s*-\s*Failed:\s*(\d+), Passed:\s*(\d+)', out)
        if not stat:
            res = 'NO STAT (build fail?) ' + out[-600:]
        else:
            res = f'{stat[-1][0]} Failed {stat[-1][1]} Passed {stat[-1][2]} :: ' + '; '.join(f.split('.')[-1] for f in failed)
    finally:
        open(path, 'wb').write(orig)
        os.utime(path, None)
        assert open(path, 'rb').read() == orig
        os.remove(bak)
    print(f'{name}: {res}', flush=True)
print('ALL RESTORED')
