import os, re, sys

# 归一：行尾、目录前缀、节点 unique_id、外部资源 id（N_xxxxx）、引擎随机子资源 id（Type_xxxxx）；其余一字不改。
def norm(data, prefix):
    t = data.replace(b'\r\n', b'\n').decode('utf-8')
    t = t.replace(prefix, 'DIR')
    t = re.sub(r'unique_id=\d+', 'unique_id=N', t)
    ext = {}
    def ext_id(m):
        return ext.setdefault(m.group(1), 'E%d' % len(ext))
    t = re.sub(r'(?<=id=")(\d+_[a-z0-9]{5})(?=")', ext_id, t)
    t = re.sub(r'(?<=ExtResource\(")(\d+_[a-z0-9]{5})(?="\))', ext_id, t)
    sub = {}
    def sub_id(m):
        return sub.setdefault(m.group(0), m.group(1) + '_S%d' % len(sub))
    t = re.sub(r'\b([A-Z][A-Za-z0-9]+)_[a-z0-9]{5}\b', sub_id, t)
    return t

def listing(d):
    out = set()
    for root, _, fs in os.walk(d):
        for f in fs:
            out.add(os.path.relpath(os.path.join(root, f), d).replace(os.sep, '/'))
    return out

a, pa, b, pb = sys.argv[1:5]
only = sys.argv[5] if len(sys.argv) > 5 else None
files, filesb = listing(a), listing(b)
if only:
    files = {f for f in files if re.search(only, f)}
    filesb = {f for f in filesb if re.search(only, f)}
raw = same = diff = 0
diffs = []
for f in sorted(files & filesb):
    da = open(os.path.join(a, f), 'rb').read()
    db = open(os.path.join(b, f), 'rb').read()
    if da == db:
        raw += 1
        same += 1
    elif norm(da, pa) == norm(db, pb):
        same += 1
    else:
        diff += 1
        diffs.append(f)
missing = sorted(files ^ filesb)
print(f'files={len(files & filesb)} raw_same={raw} norm_same={same} diff={diff} missing={len(missing)}')
for f in diffs:
    print('DIFF', f)
for f in missing:
    print('ONLY-ONE-SIDE', f)
