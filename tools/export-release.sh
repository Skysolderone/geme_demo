#!/usr/bin/env bash
# 导出当前开放地图版的 Windows x64 包；需要 Godot 4.7.2 .NET 和 .NET 8 SDK。
# 用法：tools/export-release.sh 0.5.0 [Godot 可执行文件]
set -euo pipefail

VERSION="${1:?用法：tools/export-release.sh <版本号，如 0.5.0> [Godot 路径]}"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "版本号应为 x.y.z" >&2; exit 2; }
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/godot"
BUILD="$ROOT/build"
PACKAGE="$BUILD/windows/Siege"
ARCHIVE="$BUILD/Siege-windows-x64-v$VERSION.zip"
GODOT="${2:-${GODOT:-godot}}"

command -v "$GODOT" >/dev/null 2>&1 || { echo "找不到 Godot：$GODOT" >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo "找不到 .NET 8 SDK" >&2; exit 1; }
command -v zip >/dev/null 2>&1 || { echo "找不到 zip 命令" >&2; exit 1; }
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) export NUGET_PACKAGES="${NUGET_PACKAGES:-E:/dev-cache/nuget/packages}" ;;
esac
mkdir -p "$BUILD"
mkdir -p "$BUILD/windows"
ROOT_REAL="$(cd -P "$ROOT" && pwd -P)"
BUILD_REAL="$(cd -P "$BUILD" && pwd -P)"
WINDOWS_REAL="$(cd -P "$BUILD/windows" && pwd -P)"
[[ "$BUILD_REAL" == "$ROOT_REAL/build" && "$WINDOWS_REAL" == "$ROOT_REAL/build/windows" ]] || {
  echo "拒绝使用工作树外的构建目录" >&2; exit 1;
}

# Godot 导出配置属于本机生成物，不入库。NUGET_CONFIG 可覆盖工程内的机器专用包源。
if [[ -n "${NUGET_CONFIG:-}" ]]; then
  [[ -f "$NUGET_CONFIG" ]] || { echo "NUGET_CONFIG 不存在：$NUGET_CONFIG" >&2; exit 1; }
  export RestoreConfigFile="$NUGET_CONFIG"
else
  LOCAL_PACKAGES="$(dirname "$(command -v "$GODOT")")/GodotSharp/Tools/nupkgs"
  LOCAL_SOURCE=''
  if [[ -d "$LOCAL_PACKAGES" ]]; then
    LOCAL_SOURCE="    <add key=\"godot-local\" value=\"$LOCAL_PACKAGES\" />"
  fi
  cat > "$BUILD/nuget-release.config" <<CONFIG
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
$LOCAL_SOURCE
  </packageSources>
</configuration>
CONFIG
  export RestoreConfigFile="$BUILD/nuget-release.config"
fi
cat > "$PROJECT/export_presets.cfg" <<PRESETS
[preset.0]

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

[preset.0.options]

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

# 只清理当前工作树中本脚本维护的产物。
[[ "$PACKAGE" == "$ROOT"/build/windows/Siege ]] || { echo "拒绝清理工作树外的目录" >&2; exit 1; }
if [[ -e "$PACKAGE" || -L "$PACKAGE" ]]; then
  [[ ! -L "$PACKAGE" && "$(cd -P "$PACKAGE" && pwd -P)" == "$ROOT_REAL/build/windows/Siege" ]] || {
    echo "拒绝清理工作树外的包目录" >&2; exit 1;
  }
fi
rm -rf -- "$PACKAGE"
find "$BUILD" -maxdepth 1 -type f -name 'Siege-windows-x64-v*.zip' -delete
mkdir -p "$PACKAGE"

dotnet build "$PROJECT/Siege.Godot.csproj" -c Release
"$GODOT" --headless --editor --path "$PROJECT" --import > "$BUILD/import-godot.log" 2>&1 || { tail -30 "$BUILD/import-godot.log" >&2; exit 1; }
"$GODOT" --headless --path "$PROJECT" --export-release 'Windows Desktop' "$PACKAGE/Siege.exe" > "$BUILD/export-windows.log" 2>&1 || { tail -30 "$BUILD/export-windows.log" >&2; exit 1; }
if grep -q '^ERROR:' "$BUILD/export-windows.log"; then
  grep -A3 '^ERROR:' "$BUILD/export-windows.log" | head -30 >&2
  exit 1
fi
for name in Siege.exe Siege.pck data_Siege.Godot_windows_x86_64; do
  [[ -e "$PACKAGE/$name" ]] || { echo "导出包缺少 $name；见 $BUILD/export-windows.log" >&2; exit 1; }
done
(cd "$BUILD/windows" && zip -q -r "$ARCHIVE" Siege)
echo "完成：$ARCHIVE"
