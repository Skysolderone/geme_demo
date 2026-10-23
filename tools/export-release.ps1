param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version,

    [string]$Godot = $env:GODOT
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src/godot'
$build = Join-Path $root 'build'
$package = Join-Path $build 'windows/Siege'
$archive = Join-Path $build "Siege-windows-x64-v$Version.zip"
$log = Join-Path $build 'export-windows.log'
$errorLog = Join-Path $build 'export-windows-error.log'

if (-not $Godot) {
    $found = Get-Command godot -ErrorAction SilentlyContinue
    if ($found) { $Godot = $found.Source }
}
if (-not $Godot) {
    $installed = 'D:\software\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
    if (Test-Path -LiteralPath $installed) { $Godot = $installed }
}
if (-not $Godot -or -not (Test-Path -LiteralPath $Godot)) {
    throw '找不到 Godot 4.7.2 .NET 版。请设置 GODOT 环境变量或传入 -Godot 路径。'
}
if (-not $env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES = 'E:\dev-cache\nuget\packages'
}
New-Item -ItemType Directory -Force -Path $build | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $build 'windows') | Out-Null
foreach ($directory in @($build, (Join-Path $build 'windows'))) {
    $resolved = (Resolve-Path -LiteralPath $directory).Path
    $expected = [IO.Path]::GetFullPath($directory)
    if (-not [string]::Equals($resolved, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        -not $expected.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝使用工作树外的构建目录：$resolved"
    }
}

# 工程自带 nuget.config 含本机绝对路径；发布构建从 Godot 安装目录寻找本地包源。
if ($env:NUGET_CONFIG) {
    if (-not (Test-Path -LiteralPath $env:NUGET_CONFIG)) { throw "NUGET_CONFIG 不存在：$env:NUGET_CONFIG" }
    $env:RestoreConfigFile = (Resolve-Path -LiteralPath $env:NUGET_CONFIG).Path
} else {
    $localPackages = Join-Path (Split-Path -Parent $Godot) 'GodotSharp/Tools/nupkgs'
    $localSource = if (Test-Path -LiteralPath $localPackages) {
        '    <add key="godot-local" value="' + [Security.SecurityElement]::Escape($localPackages) + '" />'
    } else { '' }
    $nugetConfig = Join-Path $build 'nuget-release.config'
    $configText = "<configuration>`n  <packageSources>`n    <clear />`n    <add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" />`n$localSource`n  </packageSources>`n</configuration>`n"
    [IO.File]::WriteAllText($nugetConfig, $configText, [Text.UTF8Encoding]::new($false))
    $env:RestoreConfigFile = $nugetConfig
}

# Godot 导出配置是本机生成物，不入库。
$presetText = @"
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
application/file_version="${Version}.0"
application/product_version="${Version}.0"
application/product_name="Siege"
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
"@
[IO.File]::WriteAllText((Join-Path $project 'export_presets.cfg'), $presetText, [Text.UTF8Encoding]::new($false))

# 只清理当前工作树中本脚本维护的包目录和旧版压缩包。
if (Test-Path -LiteralPath $package) {
    $resolved = (Resolve-Path -LiteralPath $package).Path
    $expected = [IO.Path]::GetFullPath($package)
    if (-not [string]::Equals($resolved, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        -not $expected.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理工作树外的路径：$resolved"
    }
    Remove-Item -LiteralPath $package -Recurse -Force
}
Get-ChildItem -LiteralPath $build -File -Filter 'Siege-windows-x64-v*.zip' |
    Remove-Item -Force
New-Item -ItemType Directory -Force -Path $package | Out-Null

Write-Host '== 构建 Godot .NET 工程'
& dotnet build (Join-Path $project 'Siege.Godot.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败。' }

Write-Host '== 导入 Godot 资源'
$arguments = '--headless --editor --path "{0}" --import' -f $project
$process = Start-Process -FilePath $Godot -ArgumentList $arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $errorLog
if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $log, $errorLog -Pattern '^\s*ERROR:' -Quiet)) {
    throw "Godot 资源导入失败；见 $log 和 $errorLog"
}

Write-Host '== 导出 Windows x64'
$arguments = '--headless --path "{0}" --export-release "Windows Desktop" "{1}"' -f $project, (Join-Path $package 'Siege.exe')
$process = Start-Process -FilePath $Godot -ArgumentList $arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $errorLog
if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $log, $errorLog -Pattern '^\s*ERROR:' -Quiet)) {
    throw "Godot 导出失败；见 $log 和 $errorLog"
}
foreach ($name in @('Siege.exe', 'Siege.pck', 'data_Siege.Godot_windows_x86_64')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $name))) {
        throw "导出包缺少 $name；见 $log"
    }
}

# Zip 条目统一使用正斜杠，Windows 以外的解压工具也能正确识别 Siege/ 目录。
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $package -Recurse -File)) {
        $relative = $file.FullName.Substring($package.Length).TrimStart('\', '/')
        $entry = 'Siege/' + $relative.Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $zip.Dispose()
}
Write-Host "完成：$archive"
