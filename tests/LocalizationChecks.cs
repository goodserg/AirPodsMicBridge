using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AirPodsMicBridge;

internal static class LocalizationChecks
{
    private static int assertions;
    private static readonly string[] Keys =
    {
        "cardTitle", "microphoneMuted", "microphoneLive", "waitingMicrophone",
        "controlEnabled", "showCard", "exit", "tooltipMuted", "tooltipPaused",
        "tooltipWaiting", "tooltipLive", "waitingMicrophoneSuffix", "toggleFailed",
        "startupError", "language", "theme", "automatic", "light", "dark",
        "startWithWindows", "startupSettingFailed",
        "diagnosticTitle", "diagnosticMicrophone", "diagnosticRefresh", "diagnosticInstructions",
        "diagnosticStart", "diagnosticStop", "diagnosticSave", "diagnosticClose", "diagnosticReady",
        "diagnosticPress", "diagnosticFinished", "diagnosticEvents", "diagnosticNoMicrophones",
        "diagnosticError", "diagnosticSaved", "diagnosticSaveFailed", "diagnosticReportFilter",
        "diagnosticRawInputUnavailable", "soundNotifications"
    };

    private static int Main()
    {
        CultureInfo originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Localization.ValidateCatalog();
            CheckResolution();
            CheckCatalog();
            CheckSelection(originalUiCulture);
            Equal(originalCulture.Name, Thread.CurrentThread.CurrentCulture.Name,
                "Formatting must not change the thread culture.");
            Console.WriteLine("Localization checks passed: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Localization.SetLanguage("auto");
        }
    }

    private static void CheckResolution()
    {
        string[,] cases =
        {
            { "ru-RU", "ru" }, { "en-GB", "en" }, { "es-MX", "es" },
            { "pt-BR", "pt-BR" }, { "pt-PT", "pt" }, { "id-ID", "id" },
            { "fr-FR", "en" }, { "pt-AO", "pt" }
        };
        for (int index = 0; index < cases.GetLength(0); index++)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cases[index, 0]);
            foreach (string preference in new string[] { "auto", "AUTO", null, "", "unknown", "  " })
                Equal(cases[index, 1], Localization.ResolveLanguage(preference, culture),
                    "Automatic resolution for " + cases[index, 0] + "/" + preference);
        }
        Equal("ru", Localization.ResolveLanguage("ru", CultureInfo.GetCultureInfo("en-GB")), "Manual override");
        Equal("en", Localization.ResolveLanguage(" En ", CultureInfo.GetCultureInfo("ru-RU")), "Canonical language code");
        Equal("pt", Localization.ResolveLanguage("pt", CultureInfo.GetCultureInfo("pt-BR")), "Manual Portuguese override");
        Equal("pt-BR", Localization.ResolveLanguage("PT-br", CultureInfo.GetCultureInfo("pt-PT")), "Manual Brazilian override");
        Equal("en", Localization.ResolveLanguage("auto", CultureInfo.InvariantCulture), "Invariant fallback");
        Equal("en", Localization.ResolveLanguage("auto", null), "Null culture fallback");
    }

    private static void CheckCatalog()
    {
        string[,] languages =
        {
            { "ru", "Русский" }, { "en", "English" }, { "es", "Español" },
            { "pt-BR", "Português (Brasil)" }, { "pt", "Português" }, { "id", "Bahasa Indonesia" }
        };
        LanguageOption[] options = Localization.Languages;
        Equal(languages.GetLength(0), options.Length, "Language count");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index++)
        {
            Equal(languages[index, 0], options[index].Code, "Language code");
            Equal(languages[index, 1], options[index].NativeName, "Native language name");
            Check(unique.Add(options[index].Code), "Language codes must be unique.");
            Check(options[index].Code != "auto", "Automatic is a separate menu option.");
            Localization.SetLanguage(options[index].Code);
            Equal(options[index].Code, Localization.CurrentLanguage, "Selected language");
            foreach (string key in Keys)
            {
                string text = Localization.Get(key);
                Check(!String.IsNullOrWhiteSpace(text), "Empty text: " + options[index].Code + "/" + key);
                Check(text != key, "Unresolved key: " + options[index].Code + "/" + key);
                Check(text.IndexOf("R7", StringComparison.OrdinalIgnoreCase) < 0 &&
                    text.IndexOf("Р7", StringComparison.OrdinalIgnoreCase) < 0,
                    "Application-specific text: " + options[index].Code + "/" + key);
                if (key == "startupError")
                {
                    string formatted = Localization.Get(key, "TEST_ERROR");
                    Check(formatted.EndsWith("\nTEST_ERROR", StringComparison.Ordinal), "Startup argument/newline");
                    Check(formatted.IndexOf("{0}", StringComparison.Ordinal) < 0, "Unformatted startup argument");
                }
                else Equal(text, Localization.Get(key, new object[0]), "Literal format: " + key);
            }
            CultureInfo selected = CultureInfo.GetCultureInfo(FormatCultureName(options[index].Code));
            Equal(String.Format(selected, Localization.Get("startupError"), 1234.5m),
                Localization.Get("startupError", 1234.5m), "Selected formatting culture");
        }
        options[0] = null;
        Check(Localization.Languages[0] != null, "Language array must be protected from caller changes.");
        Localization.ValidateCatalog();
    }

    private static void CheckSelection(CultureInfo originalUiCulture)
    {
        string automatic = Localization.ResolveLanguage("auto", originalUiCulture);
        Localization.SetLanguage("ru");
        Equal("ru", Localization.CurrentLanguage, "Explicit selection");
        Equal(originalUiCulture.Name, Thread.CurrentThread.CurrentUICulture.Name, "Selection leaves UI culture unchanged");
        Localization.SetLanguage("invalid");
        Equal(automatic, Localization.CurrentLanguage, "Invalid selection means automatic");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        Localization.SetLanguage("auto");
        Equal(automatic, Localization.CurrentLanguage, "Automatic uses captured OS UI culture");
        Equal("fr-FR", Thread.CurrentThread.CurrentUICulture.Name, "Selection does not rewrite thread UI culture");
        Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        Localization.SetLanguage(null);
        Equal(automatic, Localization.CurrentLanguage, "Null selection means automatic");
    }

    private static string FormatCultureName(string language)
    {
        switch (language)
        {
            case "ru": return "ru-RU";
            case "es": return "es-ES";
            case "pt-BR": return "pt-BR";
            case "pt": return "pt-PT";
            case "id": return "id-ID";
            default: return "en-US";
        }
    }

    private static void Equal(object expected, object actual, string message)
    {
        Check(Object.Equals(expected, actual), message + ": expected " + expected + ", got " + actual);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
