param([switch]$Verify,[switch]$Install,[switch]$AppOnly,[string]$ToolsRoot='D:\app')
$ErrorActionPreference = 'Stop'
if ($AppOnly -and $Install) { throw '-AppOnly and -Install cannot be combined.' }
$root = Split-Path $PSScriptRoot -Parent
$version = '0.2.1'
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
 $publish = Join-Path $root "artifacts\publish-$version"
 dotnet publish src\AudioStretch.App\AudioStretch.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o $publish
 if ($LASTEXITCODE) { throw 'App publish failed.' }
 dotnet publish src\AudioStretch.UpdateHost\AudioStretch.UpdateHost.csproj -c Release -o artifacts\update-host
 if ($LASTEXITCODE) { throw 'Update host publish failed.' }
 Copy-Item -LiteralPath artifacts\update-host\AudioStretch.UpdateHost.exe -Destination $publish -Force
 Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $publish -Force
 Copy-Item -LiteralPath artifacts\cargo\x86_64-pc-windows-msvc\release\audio-stretch-cli.exe -Destination $publish -Force
 foreach ($name in @('AudioStretch.exe','AudioStretch.pri','MainWindow.xbf','SettingsWindow.xbf','audio_stretch_core.dll','coreclr.dll','Microsoft.UI.Xaml.dll','AudioStretch.UpdateHost.exe','tool-catalog.json')) {
  if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing published file: $name" }
 }
 # Public installers distribute the app/runtimes. Audio tool archives come directly from upstream at first setup.
 foreach($name in @('ffmpeg.exe','ffprobe.exe','rubberband-r3.exe','sndfile.dll')) {
  if(Get-ChildItem -LiteralPath $publish -Recurse -File -Filter $name){throw "Public publish contains an unintended audio tool: $name"}
 }
 if ($Verify) {
  $verification = Join-Path $root 'artifacts\verification'
  $tools = Join-Path $verification 'tools'
  New-Item -ItemType Directory -Force $tools,work\downloads | Out-Null
  foreach ($pair in @(@('ffmpeg','ffmpeg.exe'),@('ffmpeg','ffprobe.exe'),@('rubberband','rubberband-r3.exe'),@('rubberband','sndfile.dll'))) {
   Copy-Item -LiteralPath (Join-Path (Join-Path $ToolsRoot $pair[0]) $pair[1]) -Destination $tools -Force
  }
  Copy-Item -LiteralPath (Join-Path $publish 'audio-stretch-cli.exe') -Destination $verification -Force
  dotnet run --project tests\AudioStretch.Smoke\AudioStretch.Smoke.csproj -c Release -- $verification
  if ($LASTEXITCODE) { throw 'Managed/native smoke failed.' }
  $catalog = Get-Content updates\tool-catalog.json -Raw | ConvertFrom-Json
  foreach($package in $catalog.packages){
   $archive = Join-Path $root "work\downloads\$($package.id).zip"
   if(-not(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $package.sha256){Invoke-WebRequest -Uri $package.url -OutFile $archive}
   if((Get-FileHash -LiteralPath $archive).Hash -ne $package.sha256){throw "Test archive failed checksum: $($package.id)"}
  }
  dotnet run --project tests\AudioStretch.Updates.Tests\AudioStretch.Updates.Tests.csproj -c Release -- $root
  if ($LASTEXITCODE) { throw 'Update tests failed.' }
  $managedTools = Get-Content artifacts\managed-test-tools.txt -Raw
  python tests\verify_audio.py $verification $managedTools
  if ($LASTEXITCODE) { throw 'Audio verification failed.' }
 }
 if (-not $AppOnly) {
  $iscc = @((Get-Command ISCC -ErrorAction SilentlyContinue).Source,"$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe","${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
  if (-not $iscc) { throw 'Inno Setup 6 is required.' }
  & $iscc /Qp "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$(Join-Path $root 'artifacts\installer')" (Join-Path $PSScriptRoot 'AudioStretch.iss')
  if ($LASTEXITCODE) { throw 'Installer build failed.' }
  $installer = Join-Path $root "artifacts\installer\AudioStretch-$version-Setup.exe"
  "$( (Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant())  $(Split-Path $installer -Leaf)" | Set-Content artifacts\SHA256SUMS.txt -Encoding utf8NoBOM
  if ($Install) {
   $process = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$(Join-Path $root 'artifacts\install.log')" -WindowStyle Hidden -Wait -PassThru
   if ($process.ExitCode) { throw "Installer failed: $($process.ExitCode)" }
  }
 }
} finally { Pop-Location }
