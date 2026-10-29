param([switch]$Verify,[switch]$Install,[switch]$AppOnly,[string]$ToolsRoot='D:\app')
& (Join-Path $PSScriptRoot 'build\build.ps1') @PSBoundParameters
