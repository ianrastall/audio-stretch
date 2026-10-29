param([string]$Version='0.3.0')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$ui=Join-Path (Split-Path $root -Parent) 'jupiter-ui'
Push-Location $root
try {
 $scratch=Join-Path $root 'work\source-package'
 New-Item -ItemType Directory -Force $scratch | Out-Null
 $archive=Join-Path $scratch 'repository.zip'
 git archive --format=zip --output=$archive HEAD
 if($LASTEXITCODE){throw 'Source archive failed.'}
 $destination=Join-Path $root "artifacts\AudioStretch-$Version-source.zip"
 if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination}
 Add-Type -AssemblyName System.IO.Compression
 $source=[IO.Compression.ZipFile]::OpenRead($archive)
 $output=[IO.Compression.ZipFile]::Open($destination,[IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach($entry in $source.Entries){
   if($entry.Name -eq ''){continue}
   $created=$output.CreateEntry("audio-stretch/$($entry.FullName)",[IO.Compression.CompressionLevel]::Optimal)
   $inputStream=$entry.Open();$outputStream=$created.Open()
   try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
  }
  $uiFiles=@(git -C $ui ls-files)
  if($LASTEXITCODE){throw 'Cannot enumerate the shared UI sources.'}
  $manifest=@()
  foreach($relative in $uiFiles){
   $path=Join-Path $ui $relative
   if(-not(Test-Path -LiteralPath $path -PathType Leaf)){continue}
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($output,$path,"jupiter-ui/$relative",[IO.Compression.CompressionLevel]::Optimal) | Out-Null
   $manifest += "$((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant())  jupiter-ui/$relative"
  }
  $entry=$output.CreateEntry('SOURCE-SNAPSHOT.txt')
  $writer=[IO.StreamWriter]::new($entry.Open())
  try{$writer.WriteLine("AudioStretch commit: $(git rev-parse HEAD)`nShared Jupiter.UI is the exact tracked-file working snapshot used by the build. The shared checkout was not modified or committed by packaging.`n`n"+($manifest -join "`n"))}finally{$writer.Dispose()}
 }finally{$source.Dispose();$output.Dispose()}
 $installer=Join-Path $root "artifacts\installer\AudioStretch-$Version-Setup.exe"
 @($installer,$destination) | ForEach-Object { "$((Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant())  $(Split-Path $_ -Leaf)" } | Set-Content artifacts\SHA256SUMS.txt -Encoding utf8NoBOM
 Get-Item -LiteralPath $destination
}finally{Pop-Location}
