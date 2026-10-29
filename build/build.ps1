param([switch]$Verify,[switch]$Install,[switch]$AppOnly,[string]$ToolsRoot='D:\app')
$ErrorActionPreference = 'Stop'
if ($AppOnly -and $Install) { throw '-AppOnly and -Install cannot be combined.' }
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 if ($Verify) {
  cargo fmt --all -- --check
  if ($LASTEXITCODE) { throw 'Rust formatting failed.' }
  cargo clippy --locked --all-targets --target-dir artifacts\cargo -- -D warnings
  if ($LASTEXITCODE) { throw 'Rust lint failed.' }
  cargo test --locked --target-dir artifacts\cargo
  if ($LASTEXITCODE) { throw 'Rust tests failed.' }
 }
 dotnet restore src\AudioStretch.App\AudioStretch.App.csproj --locked-mode
 if ($LASTEXITCODE) { throw 'Locked NuGet restore failed.' }
 dotnet publish src\AudioStretch.App\AudioStretch.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts\publish
 if ($LASTEXITCODE) { throw 'App publish failed.' }
 $publish = Join-Path $root 'artifacts\publish'
 $tools = Join-Path $publish 'tools'
 New-Item -ItemType Directory -Force $tools | Out-Null
 foreach ($pair in @(@('ffmpeg','ffmpeg.exe'),@('ffmpeg','ffprobe.exe'),@('rubberband','rubberband-r3.exe'),@('rubberband','sndfile.dll'))) {
  $source = Join-Path (Join-Path $ToolsRoot $pair[0]) $pair[1]
  if (-not (Test-Path -LiteralPath $source)) { throw "Missing audio tool: $source" }
  Copy-Item -LiteralPath $source -Destination (Join-Path $tools $pair[1]) -Force
 }
 Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $publish -Force
 Copy-Item -LiteralPath artifacts\cargo\x86_64-pc-windows-msvc\release\audio-stretch-cli.exe -Destination $publish -Force
 foreach ($name in @('AudioStretch.exe','AudioStretch.pri','MainWindow.xbf','SettingsWindow.xbf','audio_stretch_core.dll','coreclr.dll','Microsoft.UI.Xaml.dll')) {
  if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing published file: $name" }
 }
 Get-ChildItem -LiteralPath $tools -File | ForEach-Object { [PSCustomObject]@{File=$_.Name;Size=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} } | ConvertTo-Json | Set-Content artifacts\tool-manifest.json
 if ($Verify) {
  dotnet run --project tests\AudioStretch.Smoke\AudioStretch.Smoke.csproj -c Release -- $publish
  if ($LASTEXITCODE) { throw 'Managed/native smoke failed.' }
  python tests\verify_audio.py $publish
  if ($LASTEXITCODE) { throw 'Audio verification failed.' }
 }
 if (-not $AppOnly) {
  $iscc = @((Get-Command ISCC -ErrorAction SilentlyContinue).Source,"$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe","${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
  if (-not $iscc) { throw 'Inno Setup 6 is required.' }
  & $iscc /Qp "/DAppVersion=0.2.0" "/DSourceDir=$publish" "/DOutputDir=$(Join-Path $root 'artifacts\installer')" (Join-Path $PSScriptRoot 'AudioStretch.iss')
  if ($LASTEXITCODE) { throw 'Installer build failed.' }
  if ($Install) {
   $process = Start-Process -FilePath (Join-Path $root 'artifacts\installer\AudioStretch-0.2.0-Setup.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$(Join-Path $root 'artifacts\install.log')" -WindowStyle Hidden -Wait -PassThru
   if ($process.ExitCode) { throw "Installer failed: $($process.ExitCode)" }
  }
 }
} finally { Pop-Location }
