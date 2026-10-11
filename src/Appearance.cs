using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AirPodsMicBridge
{
    public enum AppTheme { Light, Dark }

    public static class Appearance
    {
        private static AppTheme currentTheme = ResolveTheme("auto");
        private static AppTheme currentTrayTheme = ResolveTrayTheme();
        private static readonly ToolStripRenderer renderer = new ThemeMenuRenderer();

        public static AppTheme CurrentTheme { get { return currentTheme; } }
        public static AppTheme CurrentTrayTheme { get { return currentTrayTheme; } }
        public static ToolStripRenderer MenuRenderer { get { return renderer; } }
        public static Color TrayOutlineColor { get { return currentTrayTheme == AppTheme.Dark ? Color.FromArgb(194, 148, 143) : Color.FromArgb(112, 79, 75); } }
        public static Color TrayLevelColor { get { return currentTrayTheme == AppTheme.Dark ? Color.FromArgb(22, 163, 106) : Color.FromArgb(22, 128, 74); } }

        private static bool IsDark { get { return currentTheme == AppTheme.Dark; } }
        internal static Color BackgroundColor { get { return IsDark ? Color.FromArgb(31, 35, 42) : Color.FromArgb(252, 253, 255); } }
        internal static Color ForegroundColor { get { return IsDark ? Color.FromArgb(238, 242, 248) : Color.FromArgb(31, 41, 55); } }
        internal static Color SecondaryTextColor { get { return IsDark ? Color.FromArgb(171, 184, 202) : Color.FromArgb(99, 113, 130); } }
        internal static Color BorderColor { get { return IsDark ? Color.FromArgb(61, 70, 83) : Color.FromArgb(218, 225, 232); } }
        private static Color SelectionColor { get { return IsDark ? Color.FromArgb(51, 63, 81) : Color.FromArgb(231, 238, 249); } }
        private static Color DisabledColor { get { return IsDark ? Color.FromArgb(119, 132, 151) : Color.FromArgb(144, 155, 171); } }

        public static AppTheme ResolveTheme(string preference)
        {
            string value = (preference ?? "auto").Trim().ToLowerInvariant();
            if (value == "light") return AppTheme.Light;
            if (value == "dark") return AppTheme.Dark;
            return ReadWindowsTheme("AppsUseLightTheme", AppTheme.Light);
        }

        public static AppTheme ResolveTrayTheme()
        {
            return ReadWindowsTheme("SystemUsesLightTheme", AppTheme.Dark);
        }

        private static AppTheme ReadWindowsTheme(string name, AppTheme fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object setting = key == null ? null : key.GetValue(name);
                    if (setting != null) return Convert.ToInt32(setting) == 0 ? AppTheme.Dark : AppTheme.Light;
                }
            }
            catch { }
            return fallback;
        }

        public static void SetTheme(AppTheme theme)
        {
            currentTheme = theme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        }

        public static void SetTrayTheme(AppTheme theme)
        {
            currentTrayTheme = theme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        }

        internal static Color CardAccent(bool? muted)
        {
            if (!muted.HasValue) return IsDark ? Color.FromArgb(171, 184, 202) : Color.FromArgb(100, 116, 139);
            if (muted.Value) return IsDark ? Color.FromArgb(255, 111, 139) : Color.FromArgb(255, 36, 69);
            return IsDark ? Color.FromArgb(66, 217, 155) : Color.FromArgb(22, 128, 74);
        }

        internal static Color CardTint(bool? muted)
        {
            if (!muted.HasValue) return IsDark ? Color.FromArgb(43, 51, 64) : Color.FromArgb(238, 242, 246);
            if (muted.Value) return IsDark ? Color.FromArgb(66, 35, 46) : Color.FromArgb(255, 235, 239);
            return IsDark ? Color.FromArgb(24, 55, 43) : Color.FromArgb(231, 246, 238);
        }

        public static void ApplyMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            ApplyStrip(menu);
        }

        private static void ApplyStrip(ToolStrip strip)
        {
            strip.Renderer = renderer;
            strip.BackColor = BackgroundColor;
            strip.ForeColor = ForegroundColor;
            foreach (ToolStripItem item in strip.Items)
            {
                item.BackColor = BackgroundColor;
                item.ForeColor = item.Enabled ? ForegroundColor : DisabledColor;
                ToolStripDropDownItem parent = item as ToolStripDropDownItem;
                if (parent != null && parent.HasDropDownItems) ApplyStrip(parent.DropDown);
            }
            strip.Invalidate();
        }

        private sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
        {
            public ThemeMenuRenderer() { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs args)
            {
                using (SolidBrush brush = new SolidBrush(BackgroundColor)) args.Graphics.FillRectangle(brush, args.AffectedBounds);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs args)
            {
                using (SolidBrush brush = new SolidBrush(BackgroundColor)) args.Graphics.FillRectangle(brush, args.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs args)
            {
                using (Pen pen = new Pen(BorderColor))
                    args.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, args.ToolStrip.Width - 1), Math.Max(0, args.ToolStrip.Height - 1));
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
            {
                Color color = args.Item.Enabled && (args.Item.Selected || args.Item.Pressed) ? SelectionColor : BackgroundColor;
                using (SolidBrush brush = new SolidBrush(color))
                    args.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, args.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
            {
                TextRenderer.DrawText(args.Graphics, args.Text, args.TextFont, args.TextRectangle,
                    args.Item.Enabled ? ForegroundColor : DisabledColor, args.TextFormat);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs args)
            {
                args.ArrowColor = args.Item.Enabled ? ForegroundColor : DisabledColor;
                base.OnRenderArrow(args);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs args)
            {
                using (Pen pen = new Pen(BorderColor))
                {
                    if (args.Vertical)
                        args.Graphics.DrawLine(pen, args.Item.Width / 2, 3, args.Item.Width / 2, Math.Max(3, args.Item.Height - 4));
                    else
                        args.Graphics.DrawLine(pen, 28, args.Item.Height / 2, Math.Max(28, args.Item.Width - 5), args.Item.Height / 2);
                }
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs args)
            {
                Rectangle rectangle = args.ImageRectangle;
                if (rectangle.Width < 1 || rectangle.Height < 1) return;
                Color color = args.Item.Enabled ? ForegroundColor : DisabledColor;
                using (SolidBrush background = new SolidBrush(args.Item.Enabled ? SelectionColor : BackgroundColor))
                    args.Graphics.FillRectangle(background, rectangle);
                ToolStripMenuItem item = args.Item as ToolStripMenuItem;
                float x = rectangle.X, y = rectangle.Y, width = rectangle.Width, height = rectangle.Height;
                if (item != null && item.CheckState == CheckState.Indeterminate)
                {
                    using (SolidBrush brush = new SolidBrush(color))
                        args.Graphics.FillRectangle(brush, x + width * 0.25f, y + height * 0.44f, width * 0.5f, Math.Max(2, height * 0.12f));
                    return;
                }
                SmoothingMode previous = args.Graphics.SmoothingMode;
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(color, Math.Max(1.5f, width * 0.11f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    args.Graphics.DrawLines(pen, new PointF[] {
                        new PointF(x + width * 0.2f, y + height * 0.5f),
                        new PointF(x + width * 0.43f, y + height * 0.73f),
                        new PointF(x + width * 0.8f, y + height * 0.27f)
                    });
                }
                args.Graphics.SmoothingMode = previous;
            }
        }
    }
}
