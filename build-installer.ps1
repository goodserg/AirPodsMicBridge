param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$Version = '1.3.0',
    [string]$AppSourceDirectory = '',
    [string]$OutputRoot = '',
    [string]$BuildRoot = '',
    [string]$SmokeRoot = ''
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have the form major.minor.patch' }
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
if ([string]::IsNullOrWhiteSpace($AppSourceDirectory)) { $AppSourceDirectory = Join-Path $PSScriptRoot 'dist\AirPodsMicBridge' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot 'dist' }
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'AirPodsMicBridge-installer' }
$AppSourceDirectory = (Resolve-Path -LiteralPath $AppSourceDirectory).Path
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$callerTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$BuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
$exe = Join-Path $AppSourceDirectory 'AirPodsMicBridge.exe'
if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne ($Version + '.0')) { throw 'Built application version does not match the installer version' }
$compilerRoot = Split-Path -Parent $CompilerPath
foreach ($languageFile in @('Default.isl', 'Languages\Russian.isl', 'Languages\Spanish.isl', 'Languages\BrazilianPortuguese.isl', 'Languages\Portuguese.isl')) {
    if (-not (Test-Path -LiteralPath (Join-Path $compilerRoot $languageFile) -PathType Leaf)) { throw "Missing Inno Setup language file: $languageFile" }
}
New-Item -ItemType Directory -Path $BuildRoot, $OutputRoot -Force | Out-Null
$defines = @("/DAppVersion=$Version", "/DAppSourceDirectory=$AppSourceDirectory", "/DOutputRoot=$OutputRoot")
$suffix = ''
if (-not [string]::IsNullOrWhiteSpace($SmokeRoot)) {
    $SmokeRoot = [System.IO.Path]::GetFullPath($SmokeRoot)
    $safeFixture = $false
    foreach ($temporaryRoot in @($BuildRoot, $callerTemp)) {
        $prefix = $temporaryRoot.TrimEnd('\') + '\'
        if ($SmokeRoot.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { $safeFixture = $true }
    }
    if (-not $safeFixture) { throw 'SmokeRoot must be below BuildRoot or the caller temporary directory' }
    $defines += "/DSmokeRoot=$SmokeRoot"
    $suffix = '-Smoke'
}
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    $env:TEMP = $BuildRoot
    $env:TMP = $BuildRoot
    $compilerOutput = & $CompilerPath @defines (Join-Path $PSScriptRoot 'installer\AirPodsMicBridge.iss') 2>&1
    $compilerExitCode = $LASTEXITCODE
    $compilerOutput | Where-Object { $_ -match '^(Compiler engine version:|Warning:|Error|Successful compile)' } | ForEach-Object { Write-Host $_ }
    if ($compilerExitCode -ne 0) { throw "Installer compilation failed with exit code $compilerExitCode" }
    if (@($compilerOutput | Where-Object { $_ -match '^Warning:' }).Count -gt 0) { throw 'Installer compilation produced warnings; check language compatibility' }
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
$installer = Join-Path $OutputRoot "AirPodsMicBridge-$Version-Setup$suffix.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Compiler did not produce the expected installer' }
Get-FileHash -LiteralPath $installer -Algorithm SHA256
