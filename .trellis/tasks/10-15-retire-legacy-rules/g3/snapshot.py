# retire-legacy-rules 第 3 组：存档快照生成（改前 / 改后共用）。用法：python snapshot.py <输出目录名，如 saves-before>
# 把 SnapshotProbe.cs.txt 拷进 tests 编译跑一次，跑完无论成败都删掉拷贝（临时测试不留在仓库里）。
import os, shutil, subprocess, sys

ROOT = r'E:\wws\geme_demo'
HERE = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(HERE, sys.argv[1])
probe = os.path.join(ROOT, 'tests', 'Siege.Core.Tests', 'G3SnapshotProbe.cs')
shutil.copyfile(os.path.join(HERE, 'SnapshotProbe.cs.txt'), probe)
try:
    if os.path.isdir(out):
        shutil.rmtree(out)
    env = dict(os.environ, G3_SNAPSHOT_OUT=out, DOTNET_CLI_UI_LANGUAGE='en')
    p = subprocess.run(['dotnet', 'test', 'tests/Siege.Core.Tests', '--filter', 'FullyQualifiedName~G3SnapshotProbe'],
                       cwd=ROOT, env=env, capture_output=True)
    text = (p.stdout + p.stderr).decode('utf-8', 'replace')
    print('\n'.join(l for l in text.splitlines() if 'Passed' in l or 'Failed' in l or 'error' in l)[-3000:])
    print('退出码', p.returncode)
finally:
    os.remove(probe)
for name in sorted(os.listdir(out)) if os.path.isdir(out) else []:
    print(name, os.path.getsize(os.path.join(out, name)))
