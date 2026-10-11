using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicPodsR7Bridge
{
    // Construct and call on the application's WinForms UI thread.
    public sealed class UiFeedback : IDisposable
    {
        private readonly OsdWindow osd;
        private readonly CardWindow card;
        private bool disposed;
        private bool showNotificationCard;
        private bool? observedMuted;
        private static readonly Color LiveColor = Color.FromArgb(22, 128, 74);
        private static readonly Bitmap MutedOsdGlyph = DecodeGlyph("iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAB8ElEQVR4nNWXvW4TQRCAP49SIh1KGwpDm4I8gCtq8wauoM0jOA2i5g2cAh4gT+BQ5AESCfqjoUXmCQatmBWj5ezkbuNz5pNWd567Pe/s/OzMRFWJjBAcIThCcITgCMERgiMERwiOEBwhOEJw5BG+cQG0QCqqNsAlcMJITCqLuVvgrEP+A5gBP3nCFli4xScrvADe2uKnwGfGQFWHjrX+5bKQz/QfTcX3ecioscAru14X8m/u/hkBs9BvRkQIjhzwv1OWKjmNokDKWjd2zZwD3wvZvRxxGPLh88HJ8n1Kwc2DY6kihbWWKhcdzzInO+Yv9X9WY6bRWj4WKfgOeN/3I30UOLWapzUTbyO5gGdtc+aFPPn6G/f7zOqofvQwVzpVMzMzd+K2cJULk7cdc8p31FzJu1N5su8cQ8uHT6o6VdWNW+zalMksLT66yoq84GWHbLVPBRbFjs5cMHuWpmC7Y1e9RfKY9w3iIdknW2HjMtDc7XhTKNbek42oGUP6gRSkX12wpvL5CvgFHAOvXXDuvy/QYZo3Fge7WO9z57XChfyYmtt4tzofY+Fa4ULburMv5jIvGZGjyvmNNS3PnSw39HvvhxO1FlgB77Y8mzACQnAmjxQDB0MIjhAcIThCcITgCMERgiMERwiOEBw59AJq+QP1tP/uRiMHrwAAAABJRU5ErkJggg==");
        private static readonly Bitmap LiveOsdGlyph = DecodeGlyph("iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABS0lEQVR4nO3XsU3EMBQG4N8WJVIQLU1ES8ECqagzwo3ACMcKbMCtwASBEa5ggNBci2CCHxlepHcmSuXAs3ifFCVyFN+zY997AZxzzjnnXFEk70iO/PZOckfyAjUguee80fwgSG5UwNsUMMlevY0BlpEcJNBd1t6pgTUlfzOW7AzApZyfsvYXdX1qeQCzQggfWElE5SIqF1G5iMpFVC7iPw+A5JVk2XEpQZFsZxJeeqbHX0pBqwybsu2DXO913SPF3VQPHT0DQ+XDfZppqT6nYIesuNtm9VJjrYDr5JiKN2bBt+reUb1k5S2k2d9IW69mvMkGVqy0DiU6kU36DGDarK8AHgG8ATgHcA3gRt3rQggHWCKznPbBkqGGj5pWlo1eVrfmA1/Y3CNWdFK6Q/lrTB8tZ6rta/bNrfs5Kpn9gBXENTp1zjnnnHP4FZ8Ypd3stgL3AQAAAABJRU5ErkJggg==");

        public UiFeedback()
        {
            osd = new OsdWindow();
            card = new CardWindow();
        }

        public bool ShowNotificationCard
        {
            get { return showNotificationCard; }
            set
            {
                ThrowIfDisposed();
                showNotificationCard = value;
                if (!value) card.HideFeedback();
            }
        }

        public void ShowState(bool muted)
        {
            ThrowIfDisposed();
            observedMuted = muted;
            osd.ShowState(muted);
            if (showNotificationCard)
                card.ShowContent(MicrophoneMessage(muted), muted);
        }

        public void UpdateAppearance()
        {
            ThrowIfDisposed();
            card.UpdateAppearance();
        }

        private static string MicrophoneMessage(bool muted)
        {
            return Localization.Get(muted ? "microphoneMuted" : "microphoneLive");
        }

        // Observer updates do not show a card or restart the green hide timer.
        public void UpdateIndicator(bool? muted)
        {
            ThrowIfDisposed();
            if (!muted.HasValue)
            {
                HideIndicator();
                return;
            }
            bool wasMuted = observedMuted.HasValue && observedMuted.Value;
            if (muted.Value && !wasMuted) osd.ShowState(true);
            else if (!muted.Value && wasMuted) osd.ShowState(false);
            observedMuted = muted;
        }

        public void HideIndicator()
        {
            ThrowIfDisposed();
            observedMuted = null;
            osd.HideFeedback();
        }

        // General information uses the optional notification card only.
        public void ShowMessage(string message)
        {
            ThrowIfDisposed();
            if (showNotificationCard) card.ShowContent(message ?? String.Empty, null);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            osd.Dispose();
            card.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("UiFeedback");
        }

        // The transparent outline follows the native microphone tray style.
        // The returned icon is owned by the caller. Red shades include the mute X.
        public static Icon CreateTrayIcon(Color color)
        {
            bool muted = color.R > color.G + 50 && color.R > color.B + 20;
            using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    ConfigureGraphics(graphics);
                    graphics.Clear(Color.Transparent);
                    DrawTrayMicrophone(graphics, color, muted);
                }
                return CloneIcon(bitmap);
            }
        }

        // Cache a small set of these icons in the caller; each result is caller-owned.
        public static Icon CreateLevelTrayIcon(float level)
        {
            if (Single.IsNaN(level)) level = 0;
            level = Math.Max(0, Math.Min(1, level));
            using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (GraphicsPath capsule = RoundedRectangle(new RectangleF(11.5f, 3.5f, 9, 16), 4.5f))
                using (SolidBrush fill = new SolidBrush(Appearance.TrayLevelColor))
                {
                    ConfigureGraphics(graphics);
                    graphics.Clear(Color.Transparent);
                    GraphicsState previous = graphics.Save();
                    graphics.SetClip(capsule, CombineMode.Intersect);
                    graphics.FillRectangle(fill, 11.5f, 3.5f + 16 * (1 - level), 9, 16 * level);
                    graphics.Restore(previous);
                    DrawTrayMicrophone(graphics, Appearance.TrayOutlineColor, false);
                }
                return CloneIcon(bitmap);
            }
        }

        private static Icon CloneIcon(Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone();
            }
            finally { DestroyIcon(handle); }
        }

        // Clone the tiny embedded masks so their source streams can be released.
        // Both immutable images are shared for the lifetime of the process.
        private static Bitmap DecodeGlyph(string data)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(Convert.FromBase64String(data)))
            using (Bitmap image = new Bitmap(stream)) return new Bitmap(image);
        }

        // Shared drawing paths allow review without showing any windows.
        public static Bitmap CreateOsdPreview(bool muted)
        {
            Bitmap bitmap = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) DrawOsd(graphics, bitmap.Size, muted);
            return bitmap;
        }

        public static Bitmap CreateCardPreview(bool muted)
        {
            return CreateCardPreview(MicrophoneMessage(muted), muted);
        }

        public static Bitmap CreateCardPreview(string message, bool? muted)
        {
            Bitmap bitmap = new Bitmap(340, 110, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) DrawCard(graphics, bitmap.Size, message ?? String.Empty, muted);
            return bitmap;
        }

        private static void ConfigureGraphics(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        private static void DrawOsd(Graphics graphics, Size size, bool muted)
        {
            graphics.Clear(muted ? Color.FromArgb(255, 41, 71) : LiveColor);
            graphics.DrawImageUnscaled(muted ? MutedOsdGlyph : LiveOsdGlyph, 0, 0);
        }

        private static void DrawTrayMicrophone(Graphics graphics, Color color, bool muted)
        {
            using (Pen pen = new Pen(color, 1.9f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                using (GraphicsPath capsule = RoundedRectangle(new RectangleF(11.5f, 3.5f, 9, 16), 4.5f))
                    graphics.DrawPath(pen, capsule);
                graphics.DrawLine(pen, 7.5f, 15, 7.5f, 17.5f);
                graphics.DrawArc(pen, 7.5f, 9, 17, 17, 180, -180);
                graphics.DrawLine(pen, 24.5f, 17.5f, 24.5f, 15);
                graphics.DrawLine(pen, 16, 26, 16, 29.5f);
                graphics.DrawLine(pen, 11.5f, 29.5f, 20.5f, 29.5f);
                if (muted)
                {
                    graphics.DrawLine(pen, 24.5f, 4.5f, 28.5f, 8.5f);
                    graphics.DrawLine(pen, 28.5f, 4.5f, 24.5f, 8.5f);
                }
            }
        }

        private static void DrawMicrophone(Graphics graphics, RectangleF bounds, Color color, bool muted)
        {
            GraphicsState previous = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 48.0f, bounds.Height / 48.0f);
            using (Pen pen = new Pen(color, 2.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                using (GraphicsPath capsule = RoundedRectangle(new RectangleF(19, 8, 10, 22), 5))
                    graphics.DrawPath(pen, capsule);
                graphics.DrawLine(pen, 14, 22, 14, 25);
                graphics.DrawArc(pen, 14, 15, 20, 20, 180, -180);
                graphics.DrawLine(pen, 34, 25, 34, 22);
                graphics.DrawLine(pen, 24, 35, 24, 40);
                graphics.DrawLine(pen, 18, 40, 30, 40);
                if (muted)
                {
                    graphics.DrawLine(pen, 34, 10, 40, 16);
                    graphics.DrawLine(pen, 40, 10, 34, 16);
                }
            }
            graphics.Restore(previous);
        }

        private static void DrawCard(Graphics graphics, Size size, string message, bool? muted)
        {
            ConfigureGraphics(graphics);
            graphics.Clear(Color.Transparent);
            float sx = size.Width / 340.0f;
            float sy = size.Height / 110.0f;
            GraphicsState previous = graphics.Save();
            graphics.ScaleTransform(sx, sy);
            using (GraphicsPath outline = RoundedRectangle(new RectangleF(0.5f, 0.5f, 339, 109), 12))
            using (SolidBrush background = new SolidBrush(Appearance.BackgroundColor))
            using (Pen border = new Pen(Appearance.BorderColor, 1))
            {
                graphics.FillPath(background, outline);
                graphics.DrawPath(border, outline);
            }
            Color accent = Appearance.CardAccent(muted);
            Color tint = Appearance.CardTint(muted);
            using (GraphicsPath tile = RoundedRectangle(new RectangleF(18, 33, 44, 44), 11))
            using (SolidBrush brush = new SolidBrush(tint)) graphics.FillPath(brush, tile);
            DrawMicrophone(graphics, new RectangleF(22, 37, 36, 36), accent, muted.HasValue && muted.Value);
            using (Font headerFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font bodyFont = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush headerBrush = new SolidBrush(Appearance.SecondaryTextColor))
            using (SolidBrush bodyBrush = new SolidBrush(Appearance.ForegroundColor))
            using (StringFormat format = new StringFormat())
            {
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.LineLimit;
                graphics.DrawString(Localization.Get("cardTitle"), headerFont, headerBrush, new RectangleF(78, 19, 244, 20), format);
                graphics.DrawString(message, bodyFont, bodyBrush, new RectangleF(77, 42, 244, 54), format);
            }
            graphics.Restore(previous);
        }

        private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static float DesktopScale()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1.0f, graphics.DpiX / 96.0f);
        }

        private static int Scaled(float value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private abstract class FeedbackWindow : Form
        {
            private readonly Timer hideTimer;
            private const int WsExNoActivate = 0x08000000;
            private const int WsExToolWindow = 0x00000080;
            private const int WsExTransparent = 0x00000020;
            private const int WsExLayered = 0x00080000;

            protected FeedbackWindow()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(252, 253, 255);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                hideTimer = new Timer();
                hideTimer.Interval = 2200;
                hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTransparent | WsExLayered;
                    return value;
                }
            }

            protected override void OnHandleCreated(EventArgs args)
            {
                base.OnHandleCreated(args);
                SetLayeredWindowAttributes(Handle, 0, 255, 0x00000002);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
                base.WndProc(ref message);
            }

            public void HideFeedback()
            {
                hideTimer.Stop();
                Hide();
            }

            protected void ShowAt(Rectangle bounds, bool autoHide)
            {
                hideTimer.Stop();
                Bounds = bounds;
                Invalidate();
                if (!Visible) Show();
                SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);
                if (autoHide) hideTimer.Start();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) hideTimer.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class OsdWindow : FeedbackWindow
        {
            private bool muted;
            public void ShowState(bool value)
            {
                muted = value;
                Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                // OEM OSD uses physical pixels, independently of desktop DPI.
                ShowAt(new Rectangle(screen.Bounds.Left, screen.Bounds.Top, 48, 48), !muted);
            }
            protected override void OnPaint(PaintEventArgs args)
            {
                DrawOsd(args.Graphics, ClientSize, muted);
            }
        }

        private sealed class CardWindow : FeedbackWindow
        {
            private string message = String.Empty;
            private bool? muted;
            public CardWindow()
            {
                BackColor = Appearance.BackgroundColor;
            }
            public void UpdateAppearance()
            {
                if (muted.HasValue) message = MicrophoneMessage(muted.Value);
                BackColor = Appearance.BackgroundColor;
                Invalidate();
            }
            public void ShowContent(string value, bool? state)
            {
                message = value;
                muted = state;
                Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                float scale = DesktopScale();
                int width = Scaled(340, scale), height = Scaled(110, scale), margin = Scaled(16, scale);
                Rectangle area = screen.WorkingArea;
                ShowAt(new Rectangle(area.Right - width - margin, area.Bottom - height - margin, width, height), true);
            }
            protected override void OnSizeChanged(EventArgs args)
            {
                base.OnSizeChanged(args);
                if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
                using (GraphicsPath path = RoundedRectangle(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), ClientSize.Width * 12.0f / 340.0f))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }
            protected override void OnPaint(PaintEventArgs args)
            {
                DrawCard(args.Graphics, ClientSize, message, muted);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
