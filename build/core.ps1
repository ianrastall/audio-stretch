$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 cargo build --release --locked --target-dir artifacts\cargo
 if ($LASTEXITCODE) { throw 'Rust core build failed.' }
 New-Item -ItemType Directory -Force artifacts\core | Out-Null
 Copy-Item -LiteralPath artifacts\cargo\x86_64-pc-windows-msvc\release\audio_stretch_core.dll -Destination artifacts\core\audio_stretch_core.dll -Force
} finally { Pop-Location }
