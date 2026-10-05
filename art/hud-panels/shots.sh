#!/bin/bash
# 用法：shots.sh <工作树根> <输出目录>
# hud-panels 段 B（design D7）：构建 Godot 工程（Debug）并拍信息面板的对照图；
# 每张另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。不用 --headless（配 --screenshot 会停住），路径一律绝对路径。
ROOT="$1"
OUT="$2"
GODOT="$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot"
PROJ="$ROOT/src/godot"
mkdir -p "$OUT"

echo "== build"
dotnet build "$PROJ/Siege.Godot.csproj" -v q -p:RestoreConfigFile="$HOME/Applications/godot-4.7.2-mono/nuget.config" 2>&1 | tail -4
echo "build exit=${PIPESTATUS[0]}"

if [ ! -d "$PROJ/.godot/imported" ]; then
  echo "== import"
  "$GODOT" --headless --path "$PROJ" --import > "$OUT/import.log" 2>&1
  echo "import exit=$?"
fi

# shot <名字> <帧> <参数…>
shot() {
  name="$1"
  frame="$2"
  shift 2
  rm -f "$OUT/$name.png"
  "$GODOT" --path "$PROJ" -- "$@" "--screenshot=$OUT/$name.png:$frame" > "$OUT/$name.log" 2>&1
  code=$?
  if [ -s "$OUT/$name.png" ]; then file=ok; else file=MISSING; fi
  echo "$name exit=$code file=$file exceptions=$(grep -c -i 'exception' "$OUT/$name.log")"
}

# 插旗提示：v5 不带推屏提示一行，边疆图带。
shot flag-v5 150 --map=siege-4p-base-v5 --seed=1 --mute
shot flag-frontier 150 --map=siege-frontier-v2 --seed=1 --mute
# 对局中途：有排名数据、轮到某人、手里有几类棋（自动演示跑到靠后的帧）。
shot mid-v5 45 --auto-demo --map=siege-4p-base-v5 --mute
shot mid-frontier 45 --auto-demo --map=siege-frontier-v2 --mute
# 结算演出中的势力栏（段首放大、明细为本段增量）。
shot show-power 40 --auto-demo --map=siege-4p-base-v5 --shot-show=power --mute
