#!/bin/bash
# 用法：[ONLY="名字 …"] shots.sh <工作树根> <输出目录> [nobuild]
# builtin-board-maps 段 B（design D2 / D7）：内置棋盘图候选种子的全局预览，每种人数 3 个（缺省棋盘数：4 人 7、3 人 6、2 人 5）。
# 选种口径：种子 1–200 里先按硬条件筛（主战场宽高都 ≥ 13、公共棋盘不全同形、出生棋盘面积 30–42），再按"可落子格离该档中位数的距离"取前 3。
# 每张另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。不用 --headless（配 --screenshot 会停住），路径一律绝对路径。
ROOT="$1"
OUT="$2"
GODOT="$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot"
PROJ="$ROOT/src/godot"
mkdir -p "$OUT"

if [ "$3" != "nobuild" ]; then
  echo "== build"
  dotnet build "$PROJ/Siege.Godot.csproj" -v q -p:RestoreConfigFile="$HOME/Applications/godot-4.7.2-mono/nuget.config" 2>&1 | tail -4
  echo "build exit=${PIPESTATUS[0]}"
fi

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
  if [ -n "$ONLY" ] && [[ " $ONLY " != *" $name "* ]]; then return; fi
  rm -f "$OUT/$name.png"
  "$GODOT" --path "$PROJ" -- "$@" "--screenshot=$OUT/$name.png:$frame" > "$OUT/$name.log" 2>&1
  code=$?
  if [ -s "$OUT/$name.png" ]; then file=ok; else file=MISSING; fi
  echo "$name exit=$code file=$file exceptions=$(grep -c -i 'exception' "$OUT/$name.log")"
}

# 4 人（n7）
shot 4p-173 7 --auto-demo --map=board:173 --rounds=12 --overview --mute
shot 4p-5 7 --auto-demo --map=board:5 --rounds=12 --overview --mute
shot 4p-78 7 --auto-demo --map=board:78 --rounds=12 --overview --mute
# 3 人（n6）
shot 3p-55 7 --auto-demo --map=board:55:p3 --rounds=12 --overview --mute
shot 3p-159 7 --auto-demo --map=board:159:p3 --rounds=12 --overview --mute
shot 3p-181 7 --auto-demo --map=board:181:p3 --rounds=12 --overview --mute
# 2 人（n5）
shot 2p-16 7 --auto-demo --map=board:16:p2 --rounds=12 --overview --mute
shot 2p-23 7 --auto-demo --map=board:23:p2 --rounds=12 --overview --mute
shot 2p-155 7 --auto-demo --map=board:155:p2 --rounds=12 --overview --mute
