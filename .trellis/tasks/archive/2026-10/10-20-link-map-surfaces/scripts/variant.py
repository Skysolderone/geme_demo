import sys, subprocess, os, time, shutil
p = r'E:/wws/geme_demo/src/Siege.Core/Board/Maps/LinkMapGenerator.Surfaces.cs'
orig = open(p, 'rb').read()
bak = p + f'.bak{int(time.time())}'
shutil.copy(p, bak)
anchor = b"&& !core.Any(k => Adjacency.AreAdjacent(k, c));"
assert orig.count(anchor) == 1
variants = {
    'noS1': b"&& (c.X >= 0 || !core.Any(k => Adjacency.AreAdjacent(k, c)));",

}
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en')
results = {}
try:
    for name, rep in variants.items():
        open(p, 'wb').write(orig.replace(anchor, rep))
        r = subprocess.run(['dotnet', 'test', r'E:/wws/geme_demo/tests/Siege.Core.Tests', '-c', 'Release', '--filter', 'FullyQualifiedName~TmpSurfaceDistribution', '--logger', 'console;verbosity=detailed'], capture_output=True, env=env)
        out = r.stdout.decode('utf-8', 'replace')
        results[name] = [l.strip() for l in out.splitlines() if any(k in l for k in ('种子 1–2000', '每张图', '尺寸', '主题', '种类', 'Passed!', 'Failed!', 'error'))]
finally:
    open(p, 'wb').write(orig)
    os.utime(p, None)
assert open(p, 'rb').read() == orig
os.remove(bak)
with open(sys.argv[1], 'w', encoding='utf-8') as f:
    for name, lines in results.items():
        f.write(f'## 变体 {name}\n' + '\n'.join(lines) + '\n\n')
print('restored OK')
