# ai-terrain-review 1.4 变异脚本：每条"备份 → 改 → 构建 + 跑相关测试 → 还原（finally）→ touch"，按 testing.md 二进制读写、按文件行尾归一锚点、命中次数恰为 1。
import os, re, subprocess, sys, time

sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'E:\wws\geme_demo'
STAMP = time.strftime('%Y%m%d%H%M%S')
FILTER_NEW = 'FullyQualifiedName~AI改造与主题诊断Tests'
FILTER_OLD = 'FullyQualifiedName~战场落败复盘与等待者评估Tests'

MUTATIONS = [
    ('MA-1', r'src\Siege.Core\Ai\HeuristicTurnController.cs',
     '        var enumerated = new HashSet<Coord>(cells);\n',
     '        _ = Perturb(ranked);\n        var enumerated = new HashSet<Coord>(cells);\n', FILTER_NEW),
    ('MA-2', r'src\Siege.Core\Ai\HeuristicTurnController.cs',
     '                if (enumerated.Contains(cell))\n',
     '                if (enumerated.Count >= 0 || enumerated.Contains(cell))\n', FILTER_NEW),
    ('MA-3', r'src\Siege.Sim\Program.cs',
     '            ReviewArtisan = cli.Flag("review-artisan"),\n',
     '            ReviewArtisan = cli.Flag("review-artisan") || cli.Has("dir"),\n', FILTER_NEW),
    ('MA-4', r'src\Siege.Sim\Running\ArtisanReplay.cs',
     '            if (context.DeployLimit <= 0 || context.Stock.GetValueOrDefault(TerrainEditRules.EditorType) <= 0\n',
     '            if (context.DeployLimit <= 0 || context.Stock.GetValueOrDefault(TerrainEditRules.EditorType) < 0\n', FILTER_NEW),
    ('MA-5', r'src\Siege.Sim\Analysis\ThemeAnalyzer.cs',
     '            .Where(e => e.Seq < final && e.Settlement is { } s && s.Entries.Any(x => x.Player == player))\n',
     '            .Where(e => e.Settlement is { } s && s.Entries.Any(x => x.Player == player))\n', FILTER_NEW),
    ('MA-6', r'src\Siege.Sim\Running\LogReplay.cs',
     '        a.Turn == b.Turn && a.MajorRound == b.MajorRound && a.Player == b.Player && a.Passed == b.Passed\n',
     '        a.Turn > 0 || a.Turn == b.Turn && a.MajorRound == b.MajorRound && a.Player == b.Player && a.Passed == b.Passed\n',
     FILTER_NEW + '|' + FILTER_OLD),
]

only = set(sys.argv[1:])


def eol_of(data):
    crlf = data.count(b'\r\n')
    lf = data.count(b'\n') - crlf
    if crlf and lf:
        raise SystemExit('文件内部混用行尾')
    return b'\r\n' if crlf else b'\n'


def run(cmd):
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en')
    p = subprocess.run(cmd, cwd=ROOT, env=env, capture_output=True)
    return p.returncode, p.stdout.decode('utf-8', 'replace') + p.stderr.decode('utf-8', 'replace')


results = []
for name, rel, anchor, repl, flt in MUTATIONS:
    if only and name not in only:
        continue
    path = os.path.join(ROOT, rel)
    original = open(path, 'rb').read()
    backup = path + f'.bak-{name}-{STAMP}'
    open(backup, 'wb').write(original)
    eol = eol_of(original)
    a = anchor.replace('\n', eol.decode()).encode('utf-8')
    r = repl.replace('\n', eol.decode()).encode('utf-8')
    assert original.count(a) == 1, f'{name}: 锚点命中 {original.count(a)} 次'
    out = ''
    code = None
    try:
        open(path, 'wb').write(original.replace(a, r))
        bcode, bout = run(['dotnet', 'build', 'tests/Siege.Core.Tests', '-c', 'Release'])
        if bcode != 0:
            out = '构建失败：' + '\n'.join(l for l in bout.splitlines() if ' error ' in l)[:2000]
            code = 'BUILD'
        else:
            code, out = run(['dotnet', 'test', 'tests/Siege.Core.Tests', '-c', 'Release', '--no-build', '--filter', flt])
    finally:
        open(path, 'wb').write(original)
        os.utime(path, None)
        assert open(path, 'rb').read() == original, f'{name}: 还原后不一致'
        os.remove(backup)
    failed = sorted(set(m.group(1) for m in re.finditer(r'^\s*Failed (\S+)', out, re.M)))
    summary = [l.strip() for l in out.splitlines() if 'Passed!' in l or 'Failed!' in l]
    results.append((name, code, summary, failed))
    print(name, '退出码', code, summary, failed, flush=True)
    if code == 'BUILD':
        print(out)

print('---')
for name, code, summary, failed in results:
    print(name, '红' if code not in (0, 'BUILD') else ('构建失败' if code == 'BUILD' else '绿（守门缺失！）'), failed)
