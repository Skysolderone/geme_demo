#!/bin/bash
# 用法：hud-shots.sh <工作树根> <输出目录>
# 构建 Godot 工程并拍四张 HUD 截图；每张另查文件存在与日志里有没有 Exception（退出码 0 不代表成功）。
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

shot() {
  name="$1"
  shift
  rm -f "$OUT/$name.png"
  "$GODOT" --path "$PROJ" -- "$@" "--screenshot=$OUT/$name.png:150" > "$OUT/$name.log" 2>&1
  code=$?
  if [ -s "$OUT/$name.png" ]; then file=ok; else file=MISSING; fi
  echo "$name exit=$code file=$file exceptions=$(grep -c -i 'exception' "$OUT/$name.log")"
}

shot game --map=siege-4p-base-v5 --seed=1 --mute
shot mapselect --map-select --mute
shot carry-supply --carry-preview=supply --mute
shot carry-settlement --carry-preview=settlement --mute
