param(
    [string]$BuildRoot = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'AirPodsMicBridge-build' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'dist\AirPodsMicBridge' }
$buildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$builtFile = Join-Path $buildRoot 'AirPodsMicBridge.exe'
$assetSource = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using MagicPodsR7Bridge;
static class IconMaker {
    static void DrawTray(Graphics graphics, Icon icon, int x, int y, int edge) {
        using (Bitmap bitmap = icon.ToBitmap()) {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bitmap, new Rectangle(x,y,edge,edge), 0,0,32,32, GraphicsUnit.Pixel);
        }
    }
    static void Main(string[] args) {
        Localization.SetLanguage("en");
        Appearance.SetTheme(AppTheme.Light);
        Appearance.SetTrayTheme(AppTheme.Dark);
        using (Icon icon = UiFeedback.CreateTrayIcon(Color.FromArgb(38,58,79)))
        using (FileStream stream = File.Create(args[0])) icon.Save(stream);
        using (Bitmap image = UiFeedback.CreateCardPreview(true)) image.Save(args[1], ImageFormat.Png);
        using (Bitmap image = UiFeedback.CreateOsdPreview(true)) image.Save(args[2], ImageFormat.Png);
        using (Bitmap preview = new Bitmap(740, 220))
        using (Graphics graphics = Graphics.FromImage(preview))
        using (Font font = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
        using (SolidBrush text = new SolidBrush(Color.FromArgb(226,228,232))) {
            graphics.Clear(Color.FromArgb(35,29,27));
            graphics.DrawString("Microphone level fills the capsule",font,text,18,12);
            int[] sizes = {16,20,32};
            for (int row=0;row<sizes.Length;row++) {
                int y=67+row*46;
                graphics.DrawString(sizes[row]+" px",font,text,18,y+3);
                for (int index=0;index<9;index++) {
                    int x=95+index*69;
                    if(row==0) graphics.DrawString((index*12.5f).ToString("0.#")+"%",font,text,x,42);
                    using (Icon icon=UiFeedback.CreateLevelTrayIcon(index/8.0f)) DrawTray(graphics,icon,x,y,sizes[row]);
                }
            }
            preview.Save(args[3], ImageFormat.Png);
        }
        using (Bitmap preview = new Bitmap(650, 178))
        using (Graphics graphics = Graphics.FromImage(preview))
        using (Font font = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
        using (SolidBrush text = new SolidBrush(Color.FromArgb(64,75,87))) {
            graphics.Clear(Color.FromArgb(235,239,243));
            graphics.DrawString("OSD · 48 physical px · no margin",font,text,18,12);
            using(Bitmap image=UiFeedback.CreateOsdPreview(true)) graphics.DrawImageUnscaled(image,18,40);
            using(Bitmap image=UiFeedback.CreateOsdPreview(false)) graphics.DrawImageUnscaled(image,82,40);
            graphics.DrawString("Red while muted; green for 2.2 seconds",font,text,18,101);
            graphics.DrawString("Tray · transparent background",font,text,343,12);
            graphics.FillRectangle(Brushes.Black,343,40,120,48);
            using(Icon icon=UiFeedback.CreateTrayIcon(Color.FromArgb(255,36,69))) DrawTray(graphics,icon,354,49,32);
            using(Icon icon=UiFeedback.CreateLevelTrayIcon(0.5f)) DrawTray(graphics,icon,395,49,32);
            graphics.DrawString("Notification card is disabled by default",font,text,343,101);
            preview.Save(args[4],ImageFormat.Png);
        }
    }
}
'@
$assetFile = Join-Path $buildRoot 'IconMaker.cs'
$assetExe = Join-Path $buildRoot 'IconMaker.exe'
$assetSource | Set-Content -LiteralPath $assetFile -Encoding UTF8
$uiSources = @('UiFeedback.cs','Appearance.cs','Localization.cs') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
& $compiler /nologo /langversion:5 /target:exe /platform:x64 /codepage:65001 "/out:$assetExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $assetFile @uiSources
if ($LASTEXITCODE -ne 0) { throw 'Icon generation compilation failed' }
$assetRoot = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
$iconFile = Join-Path $assetRoot 'airpods-mic.ico'
& $assetExe $iconFile (Join-Path $assetRoot 'notification-preview.png') (Join-Path $assetRoot 'indicator-preview.png') (Join-Path $assetRoot 'tray-level-preview.png') (Join-Path $assetRoot 'ui-preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
& $compiler /nologo /langversion:5 /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$builtFile" "/win32icon:$iconFile" "/win32manifest:$(Join-Path $PSScriptRoot 'src\app.manifest')" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'src\BridgeApp.cs') (Join-Path $PSScriptRoot 'src\CoreAudioAdapter.cs') @uiSources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath $builtFile -Destination (Join-Path $OutputDirectory 'AirPodsMicBridge.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'README.ru.md'),(Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\releases\v1.2.0.md') -Destination (Join-Path $OutputDirectory 'RELEASE_NOTES.md') -Force
$outputAssets = Join-Path $OutputDirectory 'assets'
New-Item -ItemType Directory -Path $outputAssets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $assetRoot 'theme-preview.png') -Destination $outputAssets -Force
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$sumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
$sumLines = @(Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Where-Object { $_.FullName -ne $sumPath } | Sort-Object FullName | ForEach-Object { '{0} *{1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.FullName.Substring($outputRoot.Length+1).Replace('\','/') })
[System.IO.File]::WriteAllLines($sumPath,$sumLines,[System.Text.UTF8Encoding]::new($false))
Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'AirPodsMicBridge.exe') -Algorithm SHA256
