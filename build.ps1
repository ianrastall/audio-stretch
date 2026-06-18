#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$publishDir = Join-Path $PSScriptRoot 'publish'

Write-Host "Publishing AudioStretch (single-file, self-contained)..." -ForegroundColor Cyan
dotnet publish "$PSScriptRoot\AudioStretch.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded `
    -p:DebugSymbols=false `
    -o "$publishDir" --nologo

if ($LASTEXITCODE -eq 0) {
    Copy-Item -LiteralPath "$publishDir\AudioStretch.exe" -Destination "$PSScriptRoot\AudioStretch.exe" -Force
    Remove-Item -LiteralPath "$PSScriptRoot\AudioStretch.pdb" -Force -ErrorAction SilentlyContinue
    Write-Host "Done: $PSScriptRoot\AudioStretch.exe" -ForegroundColor Green
} else {
    Write-Host "Publish failed." -ForegroundColor Red
}
