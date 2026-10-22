# Builds WindowWranglerHook.dll from WindowWranglerHook.c.
#
#   pwsh native/build.ps1
#
# Uses zig as the C compiler: one download, no installer, nothing system-wide. If it isn't in
# .tools/zig yet, this fetches it from ziglang.org and checks the published checksum first.
# The built DLL is committed, so you only need this when the C file changes.
$ErrorActionPreference = 'Stop'
$version = '0.13.0'
$root = Split-Path $PSScriptRoot -Parent
$zig = Join-Path $root ".tools\zig\zig.exe"

if (-not (Test-Path $zig)) {
    $entry = (Invoke-RestMethod "https://ziglang.org/download/index.json").$version.'x86_64-windows'
    $download = Join-Path ([IO.Path]::GetTempPath()) "zig-$version-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $download | Out-Null
    $zip = Join-Path $download "zig.zip"
    Write-Host "Downloading zig $version..."
    Invoke-WebRequest $entry.tarball -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() -ne $entry.shasum) { throw "zig download failed its checksum; not using it." }
    Expand-Archive $zip -DestinationPath $download
    New-Item -ItemType Directory -Force (Join-Path $root ".tools") | Out-Null
    Move-Item (Join-Path $download "zig-windows-x86_64-$version") (Join-Path $root ".tools\zig")
    Remove-Item $download -Recurse -Force
}

# 64-bit only: Window Wrangler is 64-bit, and a hook DLL can only go into apps of its own bitness.
& $zig cc -shared -O2 -target x86_64-windows-gnu -Wall -Werror `
    -o (Join-Path $PSScriptRoot "WindowWranglerHook.dll") (Join-Path $PSScriptRoot "WindowWranglerHook.c") -luser32 -ldwmapi
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
# zig leaves an import library and debug info next to it; the app doesn't need them.
Remove-Item (Join-Path $PSScriptRoot "WindowWranglerHook.lib"), (Join-Path $PSScriptRoot "WindowWranglerHook.pdb") -ErrorAction SilentlyContinue
Write-Host "Built native/WindowWranglerHook.dll"
