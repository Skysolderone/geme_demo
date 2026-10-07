#!/bin/bash
# 用法：[ONLY="名字 …"] shots.sh <工作树根> <输出目录> [nobuild]
# retire-legacy-maps 段 D 收尾（tasks 5.1）：viewport-camera / visual-style-baseline 增量里改到棋盘图的场景中，能用现有截图参数取证的几张。
# 每张截图另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。截图不用 --headless（配 --screenshot 会停住），路径一律绝对路径；
# 不截图的自检用 --headless。
if [ -z "$1" ] || [ -z "$2" ]; then echo "用法：shots.sh <工作树根> <输出目录> [nobuild]" >&2; exit 2; fi
# 两个目录一律转成绝对路径：--screenshot= 给相对路径会存盘失败而退出码仍是 0（testing.md）。
ROOT="$(cd "$1" && pwd)" || exit 2
mkdir -p "$2" || exit 2
OUT="$(cd "$2" && pwd)" || exit 2
GODOT="$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot"
PROJ="$ROOT/src/godot"

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

# run <名字> <参数…>：无头自检，只看退出码、Exception 与日志读数。
run() {
  name="$1"
  shift
  if [ -n "$ONLY" ] && [[ " $ONLY " != *" $name "* ]]; then return; fi
  "$GODOT" --headless --path "$PROJ" -- "$@" > "$OUT/$name.log" 2>&1
  code=$?
  echo "$name exit=$code exceptions=$(grep -c -i 'exception' "$OUT/$name.log")"
}

# 1 visual-style-baseline「禁手标记的远近可辨 / 近景不变」：缺省图 siege-4p-board-v1，开局缩放下对准第一个单子禁手格。
shot forbidden-near-board4 5 --auto-demo --shot-forbidden --mute
# 2 同一 Requirement「全局预览下可辨」：45 列的棋盘图（board:363041195 = 45×32）切全局预览，盘面上有单子禁手。
shot forbidden-overview-45col 5 --auto-demo --map=board:363041195 --shot-forbidden --overview --mute
# 3 visual-style-baseline「合批不改画面」：board:1 同一帧，合批与逐格（--no-batch）各一张，按文件字节比对。
shot batch-board1 5 --map=board:1 --mute
shot nobatch-board1 5 --map=board:1 --no-batch --mute
if [ -z "$ONLY" ] && [ -s "$OUT/batch-board1.png" ] && [ -s "$OUT/nobatch-board1.png" ]; then
  if cmp -s "$OUT/batch-board1.png" "$OUT/nobatch-board1.png"; then echo "batch vs nobatch: PNG 字节相同"; else echo "batch vs nobatch: PNG 字节不同（需逐像素比对）"; fi
fi

# 4 viewport-camera「两层自检」与已删除地图的命令行报错（无头）。
run pickcheck-default --auto-demo --pick-check --mute
run retired-v5 --map=siege-4p-base-v5 --mute
run retired-gen1 --map=gen:1 --mute
