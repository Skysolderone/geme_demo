# retire-legacy-rules 第 4 组：改前 / 改后对照。用法：python compare.py <scratch 下改前日志目录> <改后日志目录>
# ① maps-before/ 与 maps-after/ 逐文件逐字节；② 日志首部改前 / 改后 / g3 改后三方逐字节；③ 完整日志去掉计时字段（ElapsedMs / TotalMs 数值与 MajorRoundMs 数组）后逐字节。
import os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
b, a = os.path.join(HERE, 'maps-before'), os.path.join(HERE, 'maps-after')
fb, fa = sorted(os.listdir(b)), sorted(os.listdir(a))
print('地图导出：改前', len(fb), '份，改后', len(fa), '份，文件名集合相同' if fb == fa else '，文件名集合不同！')
diff = [f for f in fb if f in fa and open(os.path.join(b, f), 'rb').read() != open(os.path.join(a, f), 'rb').read()]
print('逐字节不同：', diff if diff else '无（%d 份全部相同，含 digests.txt）' % len(fb))
rd = lambda p: open(p, 'rb').read()
hb, ha, g3 = rd(os.path.join(HERE, 'log-header-before.jsonl')), rd(os.path.join(HERE, 'log-header-after.jsonl')), rd(os.path.join(HERE, '..', 'g3', 'log-header-after.jsonl'))
print('link:1 种子 1 日志首部：改前 == 改后', hb == ha, '；改前 == g3/log-header-after.jsonl', hb == g3)
dig = re.search(rb'"MapDigest":"([0-9A-F]+)"', ha).group(1).decode()
first = open(os.path.join(a, 'digests.txt'), encoding='utf-8').read().splitlines()[1]
print('首部地图摘要', dig, '== digests.txt link:1 v0' , first.split()[2] == dig)
if len(sys.argv) > 2:
    strip = lambda t: re.sub(rb'"[A-Za-z]*Ms":(\d+|\[[\d,]*\]),?', b'', t)
    lb = rd(os.path.join(sys.argv[1], 'match-0000000000000001.jsonl'))
    la = rd(os.path.join(sys.argv[2], 'match-0000000000000001.jsonl'))
    print('完整日志（去计时字段）：改前', lb.count(b'\n'), '行，改后', la.count(b'\n'), '行，逐字节相同', strip(lb) == strip(la), '；未去计时字段', lb == la)
