param([string]$BuildRoot = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'AirPodsMicBridge-checks' }
$BuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$localizationExe = Join-Path $BuildRoot 'LocalizationChecks.exe'
& $compiler /nologo /langversion:5 /target:exe /platform:x64 /codepage:65001 "/out:$localizationExe" (Join-Path $PSScriptRoot 'src\Localization.cs') (Join-Path $PSScriptRoot 'tests\LocalizationChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Localization check compilation failed' }
& $localizationExe
if ($LASTEXITCODE -ne 0) { throw 'Localization checks failed' }

$presentationExe = Join-Path $BuildRoot 'PresentationChecks.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName }
& $compiler /nologo /langversion:5 /target:exe /main:PresentationChecks /platform:x64 /codepage:65001 "/out:$presentationExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @sources (Join-Path $PSScriptRoot 'tests\PresentationChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Presentation check compilation failed' }
$previousData = [Environment]::GetEnvironmentVariable('AIRPODS_MIC_BRIDGE_DATA')
try {
    $env:AIRPODS_MIC_BRIDGE_DATA = Join-Path $BuildRoot 'data'
    New-Item -ItemType Directory -Path $env:AIRPODS_MIC_BRIDGE_DATA -Force | Out-Null
    & $presentationExe $BuildRoot
    if ($LASTEXITCODE -ne 0) { throw 'Presentation checks failed' }
} finally {
    [Environment]::SetEnvironmentVariable('AIRPODS_MIC_BRIDGE_DATA',$previousData)
}
