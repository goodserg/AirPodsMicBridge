using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using AirPodsMicBridge;
using Appearance = AirPodsMicBridge.Appearance;

// Rendering checks only. The hidden form never starts a diagnostic session,
// enumerates microphones, registers RawInput, or changes an audio endpoint.
internal static class DiagnosticAppearanceChecks
{
    private static int checks;

    [STAThread]
    public static int Main()
    {
        string previousLanguage = Localization.CurrentLanguage;
        AppTheme previousTheme = Appearance.CurrentTheme;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                Appearance.SetTheme(theme);
                foreach (LanguageOption language in Localization.Languages)
                {
                    Localization.SetLanguage(language.Code);
                    CheckForm(theme + "/" + language.Code);
                }
            }
            CheckThemeChanges();
            Console.WriteLine("Diagnostic appearance checks passed: " + checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
            Appearance.SetTheme(previousTheme);
        }
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Field<T>(DiagnosticForm form, string name) where T : class
    {
        FieldInfo field = typeof(DiagnosticForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(field != null, "Missing diagnostic field: " + name);
        T value = field.GetValue(form) as T;
        Assert(value != null, "Unexpected diagnostic control type: " + name);
        return value;
    }

    private static void CreateHiddenHandles(Control control)
    {
        // Creating HWNDs activates WinForms DPI scaling; Show/Shown is never called.
        IntPtr handle = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
        control.PerformLayout();
    }

    private static Bitmap Render(Control control)
    {
        Assert(control.Width > 4 && control.Height > 4, "Control has no renderable surface: " + control.GetType().Name);
        var result = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(result, new Rectangle(Point.Empty, result.Size));
        return result;
    }

    private static bool SameColor(Color actual, Color expected)
    {
        return actual.ToArgb() == expected.ToArgb();
    }

    private static int CountColor(Bitmap image, Rectangle bounds, Color expected)
    {
        bounds.Intersect(new Rectangle(Point.Empty, image.Size));
        int count = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (SameColor(image.GetPixel(x, y), expected)) count++;
        return count;
    }

    private static void CheckForm(string context)
    {
        int activeNotifications = 0;
        using (var form = new DiagnosticForm(delegate { activeNotifications++; }))
        {
            Assert(!form.Visible && !form.IsHandleCreated && form.ActiveSession == null, context + ": construction must stay passive.");
            CreateHiddenHandles(form);
            form.PerformLayout();
            Assert(!form.Visible && form.ActiveSession == null && activeNotifications == 0, context + ": rendering must not start the test or show the form.");
            Assert(SameColor(form.BackColor, Appearance.BackgroundColor), context + ": form background is outside the theme palette.");

            ComboBox microphones = Field<ComboBox>(form, "microphones");
            Control progress = Field<Control>(form, "progress");
            Button refresh = Field<Button>(form, "refreshButton");
            Button start = Field<Button>(form, "startButton");
            Button stop = Field<Button>(form, "stopButton");
            Button save = Field<Button>(form, "saveButton");
            Button close = Field<Button>(form, "closeButton");

            CheckCombo(microphones, context);
            CheckProgress(progress, context);
            CheckButton(refresh, "diagnosticRefresh", true, context);
            CheckButton(start, "diagnosticStart", true, context);
            CheckButton(stop, "diagnosticStop", false, context);
            CheckButton(save, "diagnosticSave", false, context);
            CheckButton(close, "diagnosticClose", true, context);
            CheckLayout(form, context);
            Assert(!form.Visible && form.ActiveSession == null && activeNotifications == 0, context + ": leaf rendering must remain passive.");
        }
        Assert(activeNotifications == 0, context + ": disposing an idle form must not signal a running test.");
    }

    private static void CheckButton(Button button, string key, bool enabled, string context)
    {
        Assert(button.Text == Localization.Get(key), context + ": button localization is stale: " + key);
        button.Enabled = enabled;
        using (Bitmap image = Render(button))
        {
            Assert(SameColor(image.GetPixel(3, 3), Appearance.BackgroundColor), context + ": button background is native or unthemed: " + key);
            Rectangle textBounds = new Rectangle(4, 4, image.Width - 8, image.Height - 8);
            Color textColor = enabled ? Appearance.ForegroundColor : Appearance.SecondaryTextColor;
            Assert(CountColor(image, textBounds, textColor) > 0, context + ": button text did not render with the correct enabled/disabled palette: " + key);
            if (!enabled)
            {
                Assert(CountColor(image, textBounds, Appearance.ForegroundColor) == 0, context + ": disabled button retained normal text color: " + key);
                Assert(Math.Abs(textColor.GetBrightness() - Appearance.BackgroundColor.GetBrightness()) > 0.2f,
                    context + ": disabled button text lacks contrast: " + key);
            }
        }
        Size text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Assert(button.ClientSize.Width >= text.Width + button.Padding.Horizontal + 4,
            context + ": translated button text is clipped horizontally: " + key);
        Assert(button.ClientSize.Height >= text.Height + button.Padding.Vertical,
            context + ": translated button text is clipped vertically: " + key);
    }

    private static void CheckCombo(ComboBox combo, string context)
    {
        Assert(combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.DrawMode == DrawMode.OwnerDrawFixed,
            context + ": microphone selector needs owner drawing without editable text.");
        Assert(combo.Items.Count == 0, context + ": hidden rendering must not enumerate microphones.");
        using (Bitmap image = Render(combo)) CheckComboSurface(image, context + "/empty");
        combo.Items.Add("AirPods Pro");
        combo.SelectedIndex = 0;
        using (Bitmap image = Render(combo))
        {
            CheckComboSurface(image, context + "/selected");
            Assert(CountColor(image, new Rectangle(3, 2, Math.Max(1, image.Width / 2), image.Height - 4), Appearance.ForegroundColor) > 0,
                context + ": selected microphone text is missing or uses the native palette.");
        }
        combo.Enabled = false;
        using (Bitmap image = Render(combo))
        {
            CheckComboSurface(image, context + "/disabled");
            Assert(CountColor(image, new Rectangle(3, 2, Math.Max(1, image.Width / 2), image.Height - 4), Appearance.SecondaryTextColor) > 0,
                context + ": disabled microphone text is missing or uses the native palette.");
        }
        combo.Enabled = true;

        // Exercise the dropdown's owner drawing without opening a popup window.
        MethodInfo draw = combo.GetType().GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(draw != null, context + ": dropdown owner drawing is missing.");
        foreach (DrawItemState state in new[] { DrawItemState.None, DrawItemState.Selected, DrawItemState.Disabled })
        {
            combo.Enabled = (state & DrawItemState.Disabled) == 0;
            using (var image = new Bitmap(combo.Width, combo.ItemHeight))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Magenta);
                var bounds = new Rectangle(Point.Empty, image.Size);
                var args = new DrawItemEventArgs(graphics, combo.Font, bounds, 0, state);
                draw.Invoke(combo, new object[] { args });
                Color background = (state & DrawItemState.Selected) != 0 ? Appearance.CardTint(null) : Appearance.BackgroundColor;
                Assert(SameColor(image.GetPixel(image.Width - 4, image.Height / 2), background),
                    context + ": dropdown item background is unthemed: " + state);
                Color text = (state & DrawItemState.Disabled) != 0 ? Appearance.SecondaryTextColor : Appearance.ForegroundColor;
                Assert(CountColor(image, bounds, text) > 0, context + ": dropdown item text is unthemed: " + state);
            }
        }
        combo.Enabled = true;
    }

    private static void CheckComboSurface(Bitmap image, string context)
    {
        // The right half excludes short selection text, and spans the arrow surface.
        Assert(SameColor(image.GetPixel(image.Width / 2, image.Height / 2), Appearance.BackgroundColor),
            context + ": native combo selection background escaped the theme.");
        Assert(SameColor(image.GetPixel(image.Width - 4, 3), Appearance.CardTint(null)),
            context + ": native dropdown arrow background escaped the theme.");
        if (Appearance.CurrentTheme == AppTheme.Dark)
        {
            int white = CountColor(image, new Rectangle(Point.Empty, image.Size), Color.White);
            Assert(white == 0, context + ": dark combo retained a native white surface.");
        }
    }

    private static void SetProgress(Control progress, string property, int value)
    {
        PropertyInfo member = progress.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
        Assert(member != null && member.CanWrite, "Missing writable progress property: " + property);
        member.SetValue(progress, value, null);
    }

    private static int GetProgress(Control progress, string property)
    {
        return (int)progress.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(progress, null);
    }

    private static void CheckProgress(Control progress, string context)
    {
        Assert(progress.AccessibleRole == AccessibleRole.ProgressBar, context + ": progress lost its accessible role.");
        SetProgress(progress, "Minimum", 0); SetProgress(progress, "Maximum", 30);
        Color fill = Color.FromArgb(139, 109, 239);
        int previousFill = -1;
        foreach (int value in new[] { 0, 15, 30 })
        {
            SetProgress(progress, "Value", value);
            Assert(progress.Text == value + " / 30", context + ": progress value text is stale for accessibility.");
            if (value == 15)
            {
                Assert(progress.AccessibilityObject.Value == progress.Text,
                    context + ": progress must expose its current value through MSAA.");
                Assert((progress.AccessibilityObject.State & AccessibleStates.ReadOnly) != 0,
                    context + ": accessible progress must be read only.");
                progress.AccessibilityObject.Value = "1";
                Assert(GetProgress(progress, "Value") == 15 && progress.AccessibilityObject.Value == progress.Text,
                    context + ": an accessibility client must not change progress.");
            }
            using (Bitmap image = Render(progress))
            {
                Rectangle inside = new Rectangle(2, 2, image.Width - 4, image.Height - 4);
                int colored = CountColor(image, new Rectangle(Point.Empty, image.Size), fill);
                Assert(colored == CountColor(image, inside, fill), context + ": purple progress fill escaped its inset track.");
                Assert(value == 0 ? colored == 0 : colored > previousFill, context + ": progress fill does not follow its value.");
                Color left = image.GetPixel(image.Width / 4, image.Height / 2);
                Color right = image.GetPixel(image.Width * 3 / 4, image.Height / 2);
                Assert(SameColor(left, value == 0 ? Appearance.CardTint(null) : fill), context + ": progress fill/track uses the native palette.");
                Assert(SameColor(right, value == 30 ? fill : Appearance.CardTint(null)), context + ": progress tail uses the wrong theme or value.");
                Assert(SameColor(image.GetPixel(0, image.Height / 2), Appearance.BorderColor), context + ": progress border is unthemed.");
                previousFill = colored;
            }
        }
        SetProgress(progress, "Value", -1);
        Assert(GetProgress(progress, "Value") == 0, context + ": progress must clamp values below its minimum.");
        SetProgress(progress, "Value", 31);
        Assert(GetProgress(progress, "Value") == 30, context + ": progress must clamp values above its maximum.");
        SetProgress(progress, "Minimum", 10); SetProgress(progress, "Maximum", 20); SetProgress(progress, "Value", 15);
        using (Bitmap image = Render(progress))
        {
            Assert(SameColor(image.GetPixel(image.Width / 4, image.Height / 2), fill) &&
                SameColor(image.GetPixel(image.Width * 3 / 4, image.Height / 2), Appearance.CardTint(null)),
                context + ": a nonzero minimum distorted progress rendering.");
        }
    }

    private static void CheckLayout(Control parent, string context)
    {
        foreach (Control child in parent.Controls)
        {
            Assert(child.Left >= 0 && child.Top >= 0 && child.Right <= parent.ClientSize.Width && child.Bottom <= parent.ClientSize.Height,
                context + ": control extends outside its parent after DPI layout: " + child.GetType().Name);
            Label label = child as Label;
            if (label != null && !String.IsNullOrEmpty(label.Text))
            {
                Size available = new Size(Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal), Int32.MaxValue);
                Size measured = TextRenderer.MeasureText(label.Text, label.Font, available, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
                Assert(measured.Height <= label.ClientSize.Height - label.Padding.Vertical,
                    context + ": translated label is clipped vertically: " + label.Text);
            }
            CheckLayout(child, context);
        }
    }

    private static void CheckThemeChanges()
    {
        Localization.SetLanguage("en"); Appearance.SetTheme(AppTheme.Light);
        using (var form = new DiagnosticForm(null))
        {
            CreateHiddenHandles(form);
            ComboBox combo = Field<ComboBox>(form, "microphones");
            Button disabled = Field<Button>(form, "saveButton");
            Control progress = Field<Control>(form, "progress");
            combo.Items.Add("AirPods Pro"); combo.SelectedIndex = 0;
            SetProgress(progress, "Value", 0);
            foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Dark })
            {
                Appearance.SetTheme(theme); form.UpdateAppearance();
                using (Bitmap image = Render(combo)) CheckComboSurface(image, "theme switch/" + theme);
                CheckButton(disabled, "diagnosticSave", false, "theme switch/" + theme);
                using (Bitmap image = Render(progress))
                    Assert(SameColor(image.GetPixel(image.Width / 2, image.Height / 2), Appearance.CardTint(null)),
                        "Theme switch left the old progress track palette.");
                Assert(!form.Visible && form.ActiveSession == null, "Changing appearance must not start or show diagnostic UI.");
            }
        }
    }
}
