param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$Version = '1.3.0',
    [string]$AppSourceDirectory = '',
    [string]$BuildRoot = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AppSourceDirectory)) { $AppSourceDirectory = Join-Path $PSScriptRoot 'dist\AirPodsMicBridge' }
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'AirPodsMicBridge-installer-checks' }
$BuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
$fixtureRoot = Join-Path $BuildRoot 'fixture'
$outputRoot = Join-Path $BuildRoot 'output'
$logsRoot = Join-Path $BuildRoot 'logs'
$dataRoot = Join-Path $fixtureRoot 'data'
New-Item -ItemType Directory -Path $logsRoot, $dataRoot -Force | Out-Null
$marker = Join-Path $dataRoot 'preserved-user-data.txt'
$markerText = 'Installer checks must preserve this user-data fixture.'
[System.IO.File]::WriteAllText($marker, $markerText)
$shortcut = Join-Path $dataRoot 'startup\AirPods Mic Bridge.lnk'
$programs = Join-Path $fixtureRoot 'startmenu\AirPods Mic Bridge'
$assertions = 0

function Assert-Installer([bool]$Condition, [string]$Message) {
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

function Invoke-InstallerProcess([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    Assert-Installer ($process.ExitCode -eq 0) "Installer process failed with exit $($process.ExitCode): $File"
}

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    $env:TEMP = $BuildRoot
    $env:TMP = $BuildRoot
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -CompilerPath $CompilerPath -Version $Version -AppSourceDirectory $AppSourceDirectory -OutputRoot $outputRoot -BuildRoot (Join-Path $BuildRoot 'compile') -SmokeRoot $fixtureRoot
    $installer = Join-Path $outputRoot "AirPodsMicBridge-$Version-Setup-Smoke.exe"
    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($language in @('ru', 'en', 'es', 'ptBR', 'pt', 'id')) {
            foreach ($startup in @('default', 'no')) {
                $installFolder = Join-Path $fixtureRoot "chosen-$language-$startup"
                $installedExe = Join-Path $installFolder 'AirPodsMicBridge.exe'
                $uninstaller = Join-Path $installFolder 'unins000.exe'
                $log = Join-Path $logsRoot "install-$language-$startup.log"
                $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', "/DIR=`"$installFolder`"", "/LANG=$language", "/LOG=`"$log`"")
                if ($startup -eq 'no') { $arguments += '/STARTUP=no' }
                Invoke-InstallerProcess $installer $arguments
                Assert-Installer (Test-Path -LiteralPath $installedExe -PathType Leaf) "Missing application for $language/$startup"
                Assert-Installer ((Get-Item -LiteralPath $installedExe).VersionInfo.FileVersion -eq ($Version + '.0')) 'Installed version does not match'
                $links = @(Get-ChildItem -LiteralPath $programs -Filter '*.lnk' -File)
                Assert-Installer ($links.Count -eq 2) "Expected application and uninstall Start Menu shortcuts for $language"
                Assert-Installer ((Test-Path -LiteralPath $shortcut -PathType Leaf) -eq ($startup -eq 'default')) "Unexpected startup selection for $language/$startup"
                if ($startup -eq 'default') {
                    $link = $shell.CreateShortcut($shortcut)
                    try {
                        Assert-Installer ([String]::Equals($link.TargetPath, $installedExe, [StringComparison]::OrdinalIgnoreCase)) 'Startup target must be the installed application'
                        Assert-Installer ([String]::Equals($link.WorkingDirectory, (Split-Path -Parent $installedExe), [StringComparison]::OrdinalIgnoreCase)) 'Startup working directory must be the installed application directory'
                        Assert-Installer ($link.Arguments -eq '--autostart') 'Startup shortcut must use the guarded autostart entry point'
                    }
                    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
                    # Upgrade the existing installation and explicitly turn startup off.
                    $upgradeLog = Join-Path $logsRoot "upgrade-$language-no.log"
                    Invoke-InstallerProcess $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', "/DIR=`"$installFolder`"", "/LANG=$language", '/STARTUP=no', "/LOG=`"$upgradeLog`"")
                    Assert-Installer (-not (Test-Path -LiteralPath $shortcut)) "Upgrade must honor /STARTUP=no for $language"
                }
                $uninstallLog = Join-Path $logsRoot "uninstall-$language-$startup.log"
                Invoke-InstallerProcess $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$uninstallLog`"")
                Assert-Installer (-not (Test-Path -LiteralPath $installedExe)) "Uninstall must remove the application for $language/$startup"
                Assert-Installer (-not (Test-Path -LiteralPath $shortcut)) "Uninstall must remove the startup shortcut for $language/$startup"
                Assert-Installer (@(Get-ChildItem -LiteralPath $programs -Filter '*.lnk' -File -ErrorAction SilentlyContinue).Count -eq 0) 'Uninstall must remove Start Menu shortcuts'
                Assert-Installer ([System.IO.File]::ReadAllText($marker) -eq $markerText) 'Uninstall must preserve user data'
                Write-Host "Installer checks passed for $language/$startup."
            }
        }
    }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    Write-Host "Installer checks passed: $assertions assertions across six languages."
}
finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
