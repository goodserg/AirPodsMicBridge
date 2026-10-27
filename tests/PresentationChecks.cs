using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using AirPodsMicBridge;
using Appearance = AirPodsMicBridge.Appearance;

internal static class PresentationChecks
{
    private static int checks;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string output = args.Length == 0 ? AppDomain.CurrentDomain.BaseDirectory : Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            CheckPreferences();
            CheckPresentation();
            CheckFeedbackDoesNotShow();
            WritePreview(Path.Combine(output, "theme-preview.png"));
            Console.WriteLine("Presentation checks passed: " + checks + "; preview=" + Path.Combine(output, "theme-preview.png"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckPreferences()
    {
        string isolated = Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA");
        Assert(!String.IsNullOrWhiteSpace(isolated), "Set AIRPODS_MIC_BRIDGE_DATA to an isolated fixture directory before running checks.");
        Assert(String.Equals(Path.GetFullPath(isolated), PreferenceStore.DataDirectory, StringComparison.OrdinalIgnoreCase), "Preference data override was not applied.");
        string path = PreferenceStore.SettingsPath;
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        Directory.CreateDirectory(PreferenceStore.DataDirectory);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            BridgePreferences defaults = PreferenceStore.Load();
            Assert(!defaults.ShowNotificationCard && defaults.Language == "auto" && defaults.Theme == "auto" && defaults.SoundNotifications,
                "Fresh preferences must enable sound, disable cards and use automatic language/theme.");
            File.WriteAllText(path, "{\"ShowNotificationCard\":true}");
            BridgePreferences old = PreferenceStore.Load();
            Assert(old.ShowNotificationCard && old.Language == "auto" && old.Theme == "auto" && old.SoundNotifications,
                "Old card-only preferences must retain the option and acquire automatic defaults with sound enabled.");
            File.WriteAllText(path, "{\"ShowNotificationCard\":false,\"Language\":\"ru\",\"Theme\":\"dark\"}");
            BridgePreferences migrated = PreferenceStore.Load();
            Assert(migrated.SoundNotifications && !migrated.ShowNotificationCard && migrated.Language == "ru" && migrated.Theme == "dark",
                "Upgrading pre-sound preferences must enable sound and retain every existing option.");
            File.WriteAllText(path, "{\"SoundNotifications\":false}");
            Assert(!PreferenceStore.Load().SoundNotifications, "Explicitly disabled sound must survive loading.");
            string[] themes = { "auto", "light", "dark" };
            foreach (LanguageOption language in Localization.Languages)
            {
                foreach (string theme in themes)
                {
                    var expected = new BridgePreferences { Language = language.Code, Theme = theme, ShowNotificationCard = theme == "dark", SoundNotifications = theme != "light" };
                    PreferenceStore.Save(expected);
                    BridgePreferences actual = PreferenceStore.Load();
                    Assert(actual.Language == expected.Language && actual.Theme == expected.Theme && actual.ShowNotificationCard == expected.ShowNotificationCard && actual.SoundNotifications == expected.SoundNotifications,
                        "Preference round trip failed: " + language.Code + "/" + theme);
                }
            }
            File.WriteAllText(path, "{not valid JSON");
            BridgePreferences damaged = PreferenceStore.Load();
            Assert(!damaged.ShowNotificationCard && damaged.Language == "auto" && damaged.Theme == "auto" && damaged.SoundNotifications,
                "Damaged preferences must fall back to defaults, including sound enabled.");
            File.WriteAllText(path, "null");
            Assert(PreferenceStore.Load().Theme == "auto", "JSON null must fall back to defaults.");
        }
        finally
        {
            if (original != null) File.WriteAllBytes(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CheckPresentation()
    {
        Localization.SetLanguage("en");
        Appearance.SetTheme(AppTheme.Light);
        Color lightBackground;
        using (Bitmap light = UiFeedback.CreateCardPreview(false)) lightBackground = light.GetPixel(330, 55);
        Appearance.SetTheme(AppTheme.Dark);
        using (Bitmap dark = UiFeedback.CreateCardPreview(false))
            Assert(dark.GetPixel(330, 55).GetBrightness() < lightBackground.GetBrightness(), "Dark card must have a darker background than the light card.");
        using (Bitmap baselineMuted = UiFeedback.CreateOsdPreview(true))
        using (Bitmap baselineLive = UiFeedback.CreateOsdPreview(false))
        {
            Assert(baselineMuted.Size == new Size(48, 48) && baselineLive.Size == new Size(48, 48), "OSD previews must remain 48 x 48.");
            foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                Appearance.SetTheme(theme);
                Appearance.SetTrayTheme(theme);
                foreach (string language in new[] { "en", "ru" })
                {
                    Localization.SetLanguage(language);
                    using (Bitmap muted = UiFeedback.CreateOsdPreview(true))
                    using (Bitmap live = UiFeedback.CreateOsdPreview(false))
                        Assert(SamePixels(muted, baselineMuted) && SamePixels(live, baselineLive), "OSD glyphs must not change with theme or language.");
                    using (ContextMenuStrip menu = MakeMenu())
                    {
                        ToolStripMenuItem parent = (ToolStripMenuItem)menu.Items[0];
                        ToolStripMenuItem selected = (ToolStripMenuItem)parent.DropDownItems[0];
                        ToolStripMenuItem unselected = (ToolStripMenuItem)parent.DropDownItems[1];
                        Appearance.ApplyMenu(menu);
                        Assert(menu.BackColor == Appearance.BackgroundColor && menu.ForeColor == Appearance.ForegroundColor, "Menu palette was not applied.");
                        Assert(parent.DropDown.BackColor == menu.BackColor && parent.DropDown.Renderer == Appearance.MenuRenderer, "Submenu palette/renderer was not applied recursively.");
                        Assert(selected.Checked && !unselected.Checked, "Applying appearance must preserve menu selection.");
                        Assert(parent.Text == (language == "ru" ? "Тема" : "Theme"), "Menu title did not use the chosen language.");
                        Assert(selected.ForeColor == menu.ForeColor && unselected.ForeColor != menu.ForeColor, "Disabled menu text needs a distinct foreground.");
                    }
                }
                CheckTrayFill();
            }
        }
        Appearance.SetTheme(AppTheme.Light);
        Localization.SetLanguage("en");
        using (Bitmap english = UiFeedback.CreateCardPreview(true))
        {
            Localization.SetLanguage("ru");
            using (Bitmap russian = UiFeedback.CreateCardPreview(true))
                Assert(!SamePixels(english, russian), "Card text did not change between English and Russian.");
        }
    }

    private static ContextMenuStrip MakeMenu()
    {
        var menu = new ContextMenuStrip();
        var theme = new ToolStripMenuItem(Localization.Get("theme"));
        theme.DropDownItems.Add(new ToolStripMenuItem(Localization.Get("light")) { Checked = true });
        theme.DropDownItems.Add(new ToolStripMenuItem(Localization.Get("dark")) { Enabled = false });
        menu.Items.Add(theme);
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("showCard")) { Checked = true });
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("soundNotifications")) { Checked = true });
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("startWithWindows")) { Checked = true });
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("diagnosticTitle")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("language")));
        menu.Items.Add(new ToolStripMenuItem(Localization.Get("exit")));
        return menu;
    }

    private static void CheckTrayFill()
    {
        using (Icon outline = UiFeedback.CreateTrayIcon(Appearance.TrayOutlineColor))
        using (Icon emptyIcon = UiFeedback.CreateLevelTrayIcon(0))
        using (Icon halfIcon = UiFeedback.CreateLevelTrayIcon(0.5f))
        using (Icon fullIcon = UiFeedback.CreateLevelTrayIcon(1))
        using (Bitmap contour = outline.ToBitmap())
        using (Bitmap empty = emptyIcon.ToBitmap())
        using (Bitmap half = halfIcon.ToBitmap())
        using (Bitmap full = fullIcon.ToBitmap())
        {
            Assert(contour.GetPixel(0, 0).A == 0 && full.GetPixel(0, 0).A == 0 && full.GetPixel(31, 31).A == 0, "Tray icons must retain transparent backgrounds.");
            int halfDifference = 0, fullDifference = 0;
            // Capsule bounds include a small antialiasing margin, excluding stand and surround.
            Rectangle capsule = new Rectangle(10, 2, 12, 20);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    bool halfChanged = empty.GetPixel(x, y).ToArgb() != half.GetPixel(x, y).ToArgb();
                    bool fullChanged = empty.GetPixel(x, y).ToArgb() != full.GetPixel(x, y).ToArgb();
                    if ((halfChanged || fullChanged) && !capsule.Contains(x, y))
                        throw new InvalidOperationException("Level fill escaped the microphone capsule at " + x + "," + y);
                    if (halfChanged) halfDifference++;
                    if (fullChanged) fullDifference++;
                }
            Assert(halfDifference > 0 && fullDifference > halfDifference, "Half and full level fills must occupy different capsule areas.");
            Assert(full.GetPixel(16, 8).A > empty.GetPixel(16, 8).A, "Full level must fill the transparent interior of the microphone.");
        }
    }

    private static void CheckFeedbackDoesNotShow()
    {
        using (var feedback = new UiFeedback())
        {
            Form osd = (Form)typeof(UiFeedback).GetField("osd", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feedback);
            Form card = (Form)typeof(UiFeedback).GetField("card", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feedback);
            Assert(!feedback.ShowNotificationCard, "Cards must default to disabled.");
            Assert(!osd.IsHandleCreated && !card.IsHandleCreated, "Feedback construction must not create windows.");
            feedback.UpdateIndicator(false);
            feedback.UpdateIndicator(null);
            feedback.UpdateAppearance();
            feedback.ShowMessage("Cards are disabled");
            feedback.ShowNotificationCard = false;
            Assert(!osd.IsHandleCreated && !card.IsHandleCreated && !osd.Visible && !card.Visible, "False/null observer updates or disabled cards must not create or show HWNDs.");
        }
    }

    private static bool SamePixels(Bitmap left, Bitmap right)
    {
        if (left.Size != right.Size) return false;
        for (int y = 0; y < left.Height; y++)
            for (int x = 0; x < left.Width; x++)
                if (left.GetPixel(x, y).ToArgb() != right.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    private static void WritePreview(string path)
    {
        using (Bitmap image = new Bitmap(1140, 640, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(image))
        using (Font title = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.FromArgb(225, 230, 236));
            for (int row = 0; row < 2; row++)
            {
                int y = 18 + row * 312;
                AppTheme theme = row == 0 ? AppTheme.Light : AppTheme.Dark;
                Appearance.SetTheme(theme);
                Appearance.SetTrayTheme(theme);
                Localization.SetLanguage(row == 0 ? "en" : "ru");
                using (SolidBrush panel = new SolidBrush(Appearance.BackgroundColor)) graphics.FillRectangle(panel, 18, y, 1104, 292);
                using (SolidBrush text = new SolidBrush(Appearance.ForegroundColor))
                    graphics.DrawString(row == 0 ? "Light / English" : "Dark / Русский", title, text, 36, y + 12);
                using (Bitmap muted = UiFeedback.CreateCardPreview(true)) graphics.DrawImageUnscaled(muted, 36, y + 48);
                using (Bitmap live = UiFeedback.CreateCardPreview(false)) graphics.DrawImageUnscaled(live, 396, y + 48);
                using (Icon outline = UiFeedback.CreateTrayIcon(Appearance.TrayOutlineColor)) graphics.DrawIcon(outline, new Rectangle(42, y + 195, 32, 32));
                for (int level = 0; level < 3; level++)
                    using (Icon icon = UiFeedback.CreateLevelTrayIcon(level / 2.0f)) graphics.DrawIcon(icon, new Rectangle(106 + level * 64, y + 195, 32, 32));
                using (Icon muted = UiFeedback.CreateTrayIcon(Color.FromArgb(255, 36, 69))) graphics.DrawIcon(muted, new Rectangle(298, y + 195, 32, 32));
                using (Bitmap muted = UiFeedback.CreateOsdPreview(true)) graphics.DrawImageUnscaled(muted, 400, y + 187);
                using (Bitmap live = UiFeedback.CreateOsdPreview(false)) graphics.DrawImageUnscaled(live, 480, y + 187);
                using (ContextMenuStrip menu = MakeMenu())
                {
                    Appearance.ApplyMenu(menu);
                    menu.PerformLayout();
                    menu.Size = menu.GetPreferredSize(Size.Empty);
                    using (Bitmap rendered = new Bitmap(menu.Width, menu.Height))
                    {
                        menu.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                        graphics.DrawImageUnscaled(rendered, 778, y + 48);
                    }
                }
            }
            image.Save(path, ImageFormat.Png);
        }
    }
}
