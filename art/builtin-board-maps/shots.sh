#!/bin/bash
# 用法：[ONLY="名字 …"] shots.sh <工作树根> <输出目录> [nobuild]
# builtin-board-maps 段 D（design D7）：构建 Godot 工程（Debug），拍选图界面三张，并跑选图自检、新缺省图与 v5 上的 --auto-demo / --pick-check。
# 每张截图另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。截图不用 --headless（配 --screenshot 会停住），路径一律绝对路径；
# 不截图的自检用 --headless。
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

# run <名字> <参数…>：无头自检，只看退出码、Exception 与日志读数。
run() {
  name="$1"
  shift
  if [ -n "$ONLY" ] && [[ " $ONLY " != *" $name "* ]]; then return; fi
  "$GODOT" --headless --path "$PROJ" -- "$@" > "$OUT/$name.log" 2>&1
  code=$?
  echo "$name exit=$code exceptions=$(grep -c -i 'exception' "$OUT/$name.log")"
}

# 1 缺省进入选图：预选 4 人内置棋盘图。
shot mapselect-default 60 --map-select --mute
# 2 随机棋盘图 3 人（与在界面上把人数从 4 调到 3 后的状态相同：棋盘数回到 3 人缺省 6、标识以 :p3 结尾）。
shot mapselect-random-p3 60 --map-select --map=board:12345:p3 --mute
# 3 选中 2 人内置棋盘图：只显示完整标识与尺寸说明，不显示种子与调节控件。
shot mapselect-builtin-p2 60 --map-select --map=siege-2p-board-v1 --mute

# 4 选图自检：随机棋盘图 → 换一张 → 调人数 → 调棋盘数 → 非法 / 合法种子 → 逐个内置棋盘图 → 回到进入项 → 开始，随后照常自动演示。
run selfcheck-default --map-select --auto-demo --mute
run selfcheck-board12345 --map-select --auto-demo --map=board:12345 --mute
# 5 新缺省图（不给 --map）与 v5 上的自动演示与拾取自检。
run autodemo-default --auto-demo --mute
run autodemo-v5 --auto-demo --map=siege-4p-base-v5 --mute
run pickcheck-default --auto-demo --pick-check --mute
