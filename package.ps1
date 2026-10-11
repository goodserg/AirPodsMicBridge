param([string]$Version = '1.2.0', [string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot 'dist' }
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$folder = Join-Path $OutputRoot 'AirPodsMicBridge'
$exe = Join-Path $folder 'AirPodsMicBridge.exe'
if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne ($Version+'.0')) { throw 'Built version does not match the package version' }
$manifestPath = Join-Path $folder 'SHA256SUMS.txt'
$sumLines = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object { $_.FullName -ne $manifestPath } | Sort-Object FullName | ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.FullName.Substring($folder.Length+1).Replace('\','/') })
[System.IO.File]::WriteAllLines($manifestPath,$sumLines,[System.Text.UTF8Encoding]::new($false))
$zip = Join-Path $OutputRoot ("AirPodsMicBridge-$Version.zip")
Compress-Archive -LiteralPath $folder -DestinationPath $zip -CompressionLevel Optimal -Force
$releaseFiles = @($exe,$zip,(Join-Path $folder 'README.md'),(Join-Path $folder 'README.ru.md'),(Join-Path $folder 'CHANGELOG.md'),(Join-Path $folder 'RELEASE_NOTES.md'),$manifestPath)
$releaseSums = @(Get-Item -LiteralPath $releaseFiles | ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.Name })
[System.IO.File]::WriteAllLines((Join-Path $OutputRoot ("AirPodsMicBridge-$Version-SHA256SUMS.txt")),$releaseSums,[System.Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $zip | Select-Object FullName,Length
