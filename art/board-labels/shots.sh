#!/bin/bash
# 用法：[ONLY="名字 …"] shots.sh <工作树根> <输出目录> [probe <起始帧> <结束帧> <种子> <大回合数> | nobuild]
# ONLY 给出时只拍列出的那几张；<工作树根> 也可以是导出的旧快照目录（拍改前图用）。
# board-labels 段 B（design D7）：构建 Godot 工程（Debug）并拍盘面标注的对照图；
# 每张另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。不用 --headless（配 --screenshot 会停住），路径一律绝对路径。
# probe 模式：逐帧试 --shot-show=reveal，只打印每帧的揭示条目落点（[show-tier] 一行），用来找贴左右边的揭示。
ROOT="$1"
OUT="$2"
GODOT="$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot"
PROJ="$ROOT/src/godot"
mkdir -p "$OUT"

if [ "$3" != "probe" ] && [ "$3" != "nobuild" ]; then
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

if [ "$3" = "probe" ]; then
  for ((f = $4; f <= $5; f++)); do
    shot "probe-$f" "$f" --auto-demo --map=siege-4p-base-v5 --rounds="$7" --seed="$6" --shot-show=reveal --mute
    grep -o '揭示条目 [0-9]* 条：.*；亮环' "$OUT/probe-$f.log" | sed -E 's/「[^」]*」[^；]*//g' | head -1
  done
  exit 0
fi

# 1 远边揭示（formation-tiers 的问题样例：第 9 大回合，金方 C11「= 56」与 L12「= 12」压住远边列字母）。
shot far-reveal 35 --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=reveal --mute
# 2 同一揭示拉近三档（近景；注视远边 G12）。v5 一屏放得下，--overview 在它上面不生效（BoardCamera.ToggleOverview 返回 false），全局预览改用边疆图（第 5 张）。
shot far-reveal-near 35 --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=reveal --shot-cell=G12 --shot-zoom=3 --mute
# 3 左右边揭示（probe 找到的帧：第 6 大回合蓝方 J5 / B11 / N11，N11 的结果升到右边行数字 13 旁）。
shot side-reveal 26 --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=reveal --mute
# 3b 同一局面同一帧改取势力节拍（D8-2）：右侧条目已显示超过淡出时长，右边 11–13 行已淡出；side-reveal 那张拍到的是 N11 条目刚出现、淡出前一刻。
shot side-reveal-show 26 --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=power --mute
# 4 对局中途的常驻标注（无演出；含金方小标注与最右一列 N10 的标注）。
shot mid-labels 33 --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --mute
# 5 全局预览下的揭示（边疆图第 2 大回合：紫方 B27 / C28 贴着远边与左边，相机距离 38.4、常驻标注不画）。
shot overview-reveal 13 --auto-demo --map=siege-frontier-v2 --rounds=14 --seed=6 --shot-show=reveal --overview --mute
