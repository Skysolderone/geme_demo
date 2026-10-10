# 段 C2 对照：去掉日志里的耗时字段（键名以 Ms 结尾：ElapsedMs / TotalMs / MajorRoundMs 等）后逐行重写，输出规范化文本的 SHA-256。
# 用法：python normalize.py <match.jsonl> [<输出 .txt.gz>]
import gzip, hashlib, json, sys

def strip(o):
    if isinstance(o, dict):
        return {k: strip(v) for k, v in o.items() if not k.endswith("Ms")}
    if isinstance(o, list):
        return [strip(v) for v in o]
    return o

lines = []
with open(sys.argv[1], encoding="utf-8") as f:
    for line in f:
        line = line.rstrip("\n")
        if line:
            lines.append(json.dumps(strip(json.loads(line)), ensure_ascii=False, separators=(",", ":")))
text = ("\n".join(lines) + "\n").encode("utf-8")
print(hashlib.sha256(text).hexdigest(), len(lines), len(text))
if len(sys.argv) > 2:
    with gzip.GzipFile(sys.argv[2], "wb", mtime=0) as g:
        g.write(text)
