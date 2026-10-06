#!/bin/bash
# 用法：[ONLY="名字 …"] shots.sh <工作树根> <输出目录> [nobuild]
# board-isolated-gen 段 C（design D7）：构建 Godot 工程（Debug）并拍互不连通棋盘图的全局预览与对局中途近景；
# 每张另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。不用 --headless（配 --screenshot 会停住），路径一律绝对路径。
# 种子 6：三种人数下主战场都明显（4 人 15×14 对 9×11、3 人 15×12 对 8×10、2 人 14×15 对 9×7），对照 `Siege.Sim map --map <标识>` 的文本图。
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

# 1–3 开局全局预览：4 / 3 / 2 人，缺省棋盘数（出生 = 人数 + 1，公共 2 块）。
shot p4-overview 7 --auto-demo --map=board:6 --rounds=12 --overview --mute
shot p3-overview 7 --auto-demo --map=board:6:p3 --rounds=12 --overview --mute
shot p2-overview 7 --auto-demo --map=board:6:p2 --rounds=12 --overview --mute
# 4 4 人最多棋盘数（10 块：出生 5、公共 5）的全局预览。
shot p4-n10-overview 7 --auto-demo --map=board:6:n10 --rounds=12 --overview --mute
# 5 4 人对局中途（第 12 大回合前后）的全局预览：看哪些棋盘有人落子。
shot p4-mid-overview 133 --auto-demo --map=board:6 --rounds=12 --overview --mute
# 6 同一帧对准主战场（棋盘清单第 5 块 = 第一块公共棋盘 15×14）的近景。
shot p4-mid-main 133 --auto-demo --map=board:6 --rounds=12 --shot-board=5 --mute
