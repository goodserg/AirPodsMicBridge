$ErrorActionPreference = 'Stop'
$buildRoot = 'D:\codex\tmp\AirPodsMicBridge-build'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$env:TEMP = 'D:\codex\tmp'
$env:TMP = 'D:\codex\tmp'
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
        using (Icon icon = UiFeedback.CreateTrayIcon(Color.FromArgb(38,58,79)))
        using (FileStream stream = File.Create(args[0])) icon.Save(stream);
        using (Bitmap image = UiFeedback.CreateCardPreview(true)) image.Save(args[1], ImageFormat.Png);
        using (Bitmap image = UiFeedback.CreateOsdPreview(true)) image.Save(args[2], ImageFormat.Png);
        using (Bitmap preview = new Bitmap(740, 220))
        using (Graphics graphics = Graphics.FromImage(preview))
        using (Font font = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
        using (SolidBrush text = new SolidBrush(Color.FromArgb(226,228,232))) {
            graphics.Clear(Color.FromArgb(35,29,27));
            graphics.DrawString("Уровень — заливка внутри капсулы микрофона",font,text,18,12);
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
            graphics.DrawString("OSD · 48 физических px · отступ 0",font,text,18,12);
            using(Bitmap image=UiFeedback.CreateOsdPreview(true)) graphics.DrawImageUnscaled(image,18,40);
            using(Bitmap image=UiFeedback.CreateOsdPreview(false)) graphics.DrawImageUnscaled(image,82,40);
            graphics.DrawString("Красный — пока mute; зелёный — 2,2 с",font,text,18,101);
            graphics.DrawString("Трей · прозрачный фон",font,text,343,12);
            graphics.FillRectangle(Brushes.Black,343,40,120,48);
            using(Icon icon=UiFeedback.CreateTrayIcon(Color.FromArgb(255,36,69))) DrawTray(graphics,icon,354,49,32);
            using(Icon icon=UiFeedback.CreateLevelTrayIcon(0.5f)) DrawTray(graphics,icon,395,49,32);
            graphics.DrawString("Карточка выключена по умолчанию",font,text,343,101);
            preview.Save(args[4],ImageFormat.Png);
        }
    }
}
'@
$assetFile = Join-Path $buildRoot 'IconMaker.cs'
$assetExe = Join-Path $buildRoot 'IconMaker.exe'
$assetSource | Set-Content -LiteralPath $assetFile -Encoding UTF8
& $compiler /nologo /langversion:5 /target:exe /platform:x64 /codepage:65001 "/out:$assetExe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $assetFile (Join-Path $PSScriptRoot 'src\UiFeedback.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation compilation failed' }
$assetRoot = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
$iconFile = Join-Path $assetRoot 'airpods-mic.ico'
& $assetExe $iconFile (Join-Path $assetRoot 'notification-preview.png') (Join-Path $assetRoot 'indicator-preview.png') (Join-Path $assetRoot 'tray-level-preview.png') (Join-Path $assetRoot 'ui-preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
& $compiler /nologo /langversion:5 /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$builtFile" "/win32icon:$iconFile" "/win32manifest:$(Join-Path $PSScriptRoot 'src\app.manifest')" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'src\BridgeApp.cs') (Join-Path $PSScriptRoot 'src\CoreAudioAdapter.cs') (Join-Path $PSScriptRoot 'src\UiFeedback.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath $builtFile -Destination (Join-Path $PSScriptRoot 'AirPodsMicBridge.exe') -Force
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'AirPodsMicBridge.exe') -Algorithm SHA256
