using System;
using System.Collections.Generic;
using System.Globalization;

namespace MagicPodsR7Bridge
{
    public sealed class LanguageOption
    {
        public string Code { get; private set; }
        public string NativeName { get; private set; }

        internal LanguageOption(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }
    }

    public static class Localization
    {
        private static readonly CultureInfo WindowsCulture =
            CultureInfo.ReadOnly((CultureInfo)CultureInfo.CurrentUICulture.Clone());
        private static readonly LanguageOption[] Options =
        {
            new LanguageOption("ru", "Русский"),
            new LanguageOption("en", "English"),
            new LanguageOption("es", "Español"),
            new LanguageOption("pt-BR", "Português (Brasil)"),
            new LanguageOption("pt", "Português"),
            new LanguageOption("id", "Bahasa Indonesia")
        };

        private static readonly string[] Keys =
        {
            "cardTitle", "microphoneMuted", "microphoneLive", "waitingR7",
            "controlEnabled", "showCard", "exit", "tooltipMuted", "tooltipPaused",
            "tooltipWaiting", "tooltipLive", "waitingR7Suffix", "toggleFailed",
            "startupError", "language", "theme", "automatic", "light", "dark"
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Catalog =
            CreateCatalog();
        private static volatile string currentLanguage = ResolveLanguage("auto", WindowsCulture);

        public static string CurrentLanguage
        {
            get { return currentLanguage; }
        }

        // The returned array can be reordered by a caller without changing the catalog.
        public static LanguageOption[] Languages
        {
            get { return (LanguageOption[])Options.Clone(); }
        }

        public static void SetLanguage(string preference)
        {
            currentLanguage = ResolveLanguage(preference, WindowsCulture);
        }

        public static string ResolveLanguage(string preference, CultureInfo culture)
        {
            string requested = preference == null ? String.Empty : preference.Trim();
            foreach (LanguageOption option in Options)
            {
                if (String.Equals(requested, option.Code, StringComparison.OrdinalIgnoreCase))
                    return option.Code;
            }

            // Unknown preferences have the same meaning as automatic selection.
            if (culture == null) return "en";
            if (String.Equals(culture.Name, "pt-BR", StringComparison.OrdinalIgnoreCase))
                return "pt-BR";
            string language = culture.TwoLetterISOLanguageName;
            foreach (LanguageOption option in Options)
            {
                if (String.Equals(language, option.Code, StringComparison.OrdinalIgnoreCase))
                    return option.Code;
            }
            return "en";
        }

        public static string Get(string key)
        {
            return GetText(currentLanguage, key);
        }

        public static string Get(string key, params object[] arguments)
        {
            string language = currentLanguage;
            return String.Format(GetFormatCulture(language), GetText(language, key),
                arguments ?? new object[0]);
        }

        // Called by console checks; no UI, audio, or operating-system settings are touched.
        public static void ValidateCatalog()
        {
            if (Catalog.Count != Options.Length)
                throw new InvalidOperationException("Localization language count is inconsistent.");

            const string marker = "__localization_argument_0__";
            foreach (LanguageOption option in Options)
            {
                Dictionary<string, string> entries;
                if (!Catalog.TryGetValue(option.Code, out entries) || entries.Count != Keys.Length)
                    throw new InvalidOperationException("Localization keys are inconsistent: " + option.Code);
                foreach (string key in Keys)
                {
                    string value;
                    if (!entries.TryGetValue(key, out value) || String.IsNullOrWhiteSpace(value))
                        throw new InvalidOperationException("Missing localization: " + option.Code + "/" + key);
                    object[] arguments = key == "startupError" ? new object[] { marker } : new object[0];
                    string formatted;
                    try { formatted = String.Format(GetFormatCulture(option.Code), value, arguments); }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException("Invalid localization format: " +
                            option.Code + "/" + key, ex);
                    }
                    if (key == "startupError")
                    {
                        int first = formatted.IndexOf(marker, StringComparison.Ordinal);
                        if (first < 0 || formatted.IndexOf(marker, first + marker.Length,
                            StringComparison.Ordinal) >= 0)
                            throw new InvalidOperationException("Expected one startup error argument: " + option.Code);
                    }
                }
            }
        }

        private static string GetText(string language, string key)
        {
            if (key == null) throw new ArgumentNullException("key");
            string value;
            if (Catalog[language].TryGetValue(key, out value) || Catalog["en"].TryGetValue(key, out value))
                return value;
            throw new KeyNotFoundException("Unknown localization key: " + key);
        }

        private static CultureInfo GetFormatCulture(string language)
        {
            switch (language)
            {
                case "ru": return CultureInfo.GetCultureInfo("ru-RU");
                case "es": return CultureInfo.GetCultureInfo("es-ES");
                case "pt-BR": return CultureInfo.GetCultureInfo("pt-BR");
                case "pt": return CultureInfo.GetCultureInfo("pt-PT");
                case "id": return CultureInfo.GetCultureInfo("id-ID");
                default: return CultureInfo.GetCultureInfo("en-US");
            }
        }

        private static Dictionary<string, string> Entries(params string[] values)
        {
            if (values.Length != Keys.Length)
                throw new InvalidOperationException("Localization entry count is inconsistent.");
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < Keys.Length; index++) entries.Add(Keys[index], values[index]);
            return entries;
        }

        private static Dictionary<string, Dictionary<string, string>> CreateCatalog()
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                { "ru", Entries(
                    "AirPods — микрофон",
                    "Микрофон выключен",
                    "Микрофон включён",
                    "Ожидание микрофона Р7",
                    "Управление микрофоном включено",
                    "Показывать всплывающую карточку",
                    "Выход",
                    "AirPods: микрофон выключен",
                    "AirPods: управление приостановлено",
                    "AirPods: ожидание подключения",
                    "AirPods: микрофон включён",
                    " · ожидание Р7",
                    "Не удалось переключить микрофон",
                    "Не удалось запустить AirPods Mic Bridge.\n{0}",
                    "Язык",
                    "Тема",
                    "Автоматически (Windows)",
                    "Светлая",
                    "Тёмная") },
                { "en", Entries(
                    "AirPods — microphone",
                    "Microphone muted",
                    "Microphone on",
                    "Waiting for the R7 microphone",
                    "Microphone control enabled",
                    "Show pop-up card",
                    "Exit",
                    "AirPods: microphone muted",
                    "AirPods: control paused",
                    "AirPods: waiting for connection",
                    "AirPods: microphone on",
                    " · waiting for R7",
                    "Could not toggle the microphone",
                    "Could not start AirPods Mic Bridge.\n{0}",
                    "Language",
                    "Theme",
                    "Automatic (Windows)",
                    "Light",
                    "Dark") },
                { "es", Entries(
                    "AirPods — micrófono",
                    "Micrófono silenciado",
                    "Micrófono activado",
                    "Esperando el micrófono de R7",
                    "Control del micrófono activado",
                    "Mostrar tarjeta emergente",
                    "Salir",
                    "AirPods: micrófono silenciado",
                    "AirPods: control en pausa",
                    "AirPods: esperando conexión",
                    "AirPods: micrófono activado",
                    " · esperando R7",
                    "No se pudo cambiar el estado del micrófono",
                    "No se pudo iniciar AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Oscuro") },
                { "pt-BR", Entries(
                    "AirPods — microfone",
                    "Microfone desativado",
                    "Microfone ativado",
                    "Aguardando o microfone do R7",
                    "Controle do microfone ativado",
                    "Mostrar cartão pop-up",
                    "Sair",
                    "AirPods: microfone desativado",
                    "AirPods: controle pausado",
                    "AirPods: aguardando conexão",
                    "AirPods: microfone ativado",
                    " · aguardando R7",
                    "Não foi possível alternar o microfone",
                    "Não foi possível iniciar o AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Escuro") },
                { "pt", Entries(
                    "AirPods — microfone",
                    "Microfone desligado",
                    "Microfone ligado",
                    "A aguardar o microfone do R7",
                    "Controlo do microfone ativado",
                    "Mostrar cartão de notificação",
                    "Sair",
                    "AirPods: microfone desligado",
                    "AirPods: controlo em pausa",
                    "AirPods: a aguardar ligação",
                    "AirPods: microfone ligado",
                    " · a aguardar R7",
                    "Não foi possível alternar o microfone",
                    "Não foi possível iniciar o AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Escuro") },
                { "id", Entries(
                    "AirPods — mikrofon",
                    "Mikrofon dinonaktifkan",
                    "Mikrofon aktif",
                    "Menunggu mikrofon R7",
                    "Kontrol mikrofon aktif",
                    "Tampilkan kartu notifikasi",
                    "Keluar",
                    "AirPods: mikrofon dinonaktifkan",
                    "AirPods: kontrol dijeda",
                    "AirPods: menunggu koneksi",
                    "AirPods: mikrofon aktif",
                    " · menunggu R7",
                    "Tidak dapat mengubah status mikrofon",
                    "Tidak dapat memulai AirPods Mic Bridge.\n{0}",
                    "Bahasa",
                    "Tema",
                    "Otomatis (Windows)",
                    "Terang",
                    "Gelap") }
            };
        }
    }
}
