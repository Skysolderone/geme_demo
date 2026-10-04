#!/bin/bash
# 导出可下载的游戏包到 build/（macOS 通用包 + Windows x64 包），供上传到 GitHub Releases。
# 用法：tools/export-release.sh <版本号，如 0.1.0>
# 前提：在 macOS 上运行（要用系统 codesign 给应用包签名）；装了 Godot 4.7.2 .NET 与同版本导出模板（GODOT 环境变量可指向可执行文件）；.NET 8 SDK。
# 导出预设 export_presets.cfg 按仓库约定不入库（.gitignore），由本脚本每次生成。
set -euo pipefail

VERSION="${1:?用法：tools/export-release.sh <版本号，如 0.1.0>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT="${GODOT:-$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot}"
OUT="$ROOT/build"
PROJECT="$ROOT/src/godot"

[ -x "$GODOT" ] || { echo "找不到 Godot：$GODOT（用 GODOT=<路径> 指定）" >&2; exit 1; }

# 仓库里的 src/godot/nuget.config 写死了 Windows 机的本地源，别的机器上还原会失败（NU1301）。
# MSBuild 把环境变量当属性读：给了 NUGET_CONFIG（或本机缺省位置有配置）就让导出时的 dotnet publish 改用它。
NUGET_CONFIG="${NUGET_CONFIG:-$HOME/Applications/godot-4.7.2-mono/nuget.config}"
if [ -f "$NUGET_CONFIG" ]; then
  export RestoreConfigFile="$NUGET_CONFIG"
fi

# Godot 导出失败时仍以 0 退出：把输出存下来，出现 ERROR 就让脚本失败。
export_preset() {
  local preset="$1" target="$2" log="$OUT/export-$3.log"
  "$GODOT" --headless --path "$PROJECT" --export-release "$preset" "$target" > "$log" 2>&1 || { tail -20 "$log" >&2; exit 1; }
  if grep -q "^ERROR:" "$log"; then
    grep -A3 "^ERROR:" "$log" | head -20 >&2
    echo "导出 $preset 失败，完整日志：$log" >&2
    exit 1
  fi
}

cat > "$PROJECT/export_presets.cfg" <<PRESETS
[preset.0]

name="macOS"
platform="macOS"
runnable=true
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path=""
script_export_mode=2

[preset.0.options]

export/distribution_type=1
binary_format/architecture="universal"
application/bundle_identifier="io.github.skysolderone.siege"
application/short_version="$VERSION"
application/version="$VERSION"
application/min_macos_version_x86_64="10.15"
application/min_macos_version_arm64="11.00"
codesign/codesign=0
notarization/notarization=0
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false

[preset.1]

name="Windows Desktop"
platform="Windows Desktop"
runnable=true
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path=""
script_export_mode=2

[preset.1.options]

binary_format/embed_pck=false
binary_format/architecture="x86_64"
debug/export_console_wrapper=0
codesign/enable=false
application/modify_resources=false
application/file_version="$VERSION.0"
application/product_version="$VERSION.0"
application/product_name="Siege"
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
PRESETS

rm -rf "$OUT/macos" "$OUT/windows" "$OUT/Siege-macos.zip" "$OUT/Siege-windows-x64.zip"
mkdir -p "$OUT/macos" "$OUT/windows/Siege"

echo "== 导出 macOS"
export_preset "macOS" "$OUT/macos/export.zip" macos
# 应用包名取自工程名（很长）：解开后改名为 Siege.app。
# 签名：Godot 内置的临时签名（codesign/codesign=1）产出的包在 Apple 芯片上一启动就被系统杀掉（2026-10-04 实测，退出码 137、无任何输出），
# 所以预设里关掉签名，这里用系统 codesign 做临时签名（没有开发者证书；.NET 运行时需要 JIT 等几项权限）。
(
  cd "$OUT/macos"
  ditto -x -k export.zip . && rm export.zip && mv ./*.app Siege.app
  cat > entitlements.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.allow-dyld-environment-variables</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
PLIST
  codesign --force --deep --sign - --entitlements entitlements.plist Siege.app
  codesign --verify --deep --strict Siege.app
  # ditto 打包保留签名与可执行位。
  ditto -c -k --keepParent Siege.app "$OUT/Siege-macos.zip"
)

echo "== 导出 Windows x64"
export_preset "Windows Desktop" "$OUT/windows/Siege/Siege.exe" windows
(cd "$OUT/windows" && zip -q -r "$OUT/Siege-windows-x64.zip" Siege)

echo "== 完成"
ls -la "$OUT"/Siege-*.zip
