using System;
using System.Collections.Generic;
using System.Globalization;

namespace AirPodsMicBridge
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
            "cardTitle", "microphoneMuted", "microphoneLive", "waitingMicrophone",
            "controlEnabled", "showCard", "exit", "tooltipMuted", "tooltipPaused",
            "tooltipWaiting", "tooltipLive", "waitingMicrophoneSuffix", "toggleFailed",
            "startupError", "language", "theme", "automatic", "light", "dark",
            "startWithWindows", "startupSettingFailed",
            "diagnosticTitle", "diagnosticMicrophone", "diagnosticRefresh", "diagnosticInstructions",
            "diagnosticStart", "diagnosticStop", "diagnosticSave", "diagnosticClose", "diagnosticReady",
            "diagnosticPress", "diagnosticFinished", "diagnosticEvents", "diagnosticNoMicrophones",
            "diagnosticError", "diagnosticSaved", "diagnosticSaveFailed", "diagnosticReportFilter",
            "diagnosticRawInputUnavailable"
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
                    "Ожидание использования микрофона",
                    "Управление микрофоном включено",
                    "Показывать всплывающую карточку",
                    "Выход",
                    "AirPods: микрофон выключен",
                    "AirPods: управление приостановлено",
                    "AirPods: ожидание подключения",
                    "AirPods: микрофон включён",
                    " · ожидание использования микрофона",
                    "Не удалось переключить микрофон",
                    "Не удалось запустить AirPods Mic Bridge.\n{0}",
                    "Язык",
                    "Тема",
                    "Автоматически (Windows)",
                    "Светлая",
                    "Тёмная",
                    "Запускать вместе с Windows",
                    "Не удалось изменить автозапуск",
                    "Диагностика гарнитуры", "Микрофон", "Обновить",
                    "Начните звонок или проверку микрофона в другом приложении. Нажимайте кнопку гарнитуры один раз по подсказке, с паузой 5 секунд. Управление микрофоном утилитой приостановлено; гарнитура может выполнять собственное действие. Звук не записывается.",
                    "Начать", "Остановить", "Сохранить отчёт…", "Закрыть", "Выберите микрофон и начните проверку.",
                    "Нажмите кнопку гарнитуры один раз", "Проверка завершена. Сохраните отчёт.", "События", "Нет подключённых микрофонов.",
                    "Ошибка диагностики", "Отчёт сохранён", "Не удалось сохранить отчёт", "Отчёт диагностики (*.jsonl)|*.jsonl",
                    "События HID недоступны. Медиаклавиши и состояние микрофона продолжают записываться.") },
                { "en", Entries(
                    "AirPods — microphone",
                    "Microphone muted",
                    "Microphone on",
                    "Waiting for microphone use",
                    "Microphone control enabled",
                    "Show pop-up card",
                    "Exit",
                    "AirPods: microphone muted",
                    "AirPods: control paused",
                    "AirPods: waiting for connection",
                    "AirPods: microphone on",
                    " · waiting for microphone use",
                    "Could not toggle the microphone",
                    "Could not start AirPods Mic Bridge.\n{0}",
                    "Language",
                    "Theme",
                    "Automatic (Windows)",
                    "Light",
                    "Dark",
                    "Start with Windows",
                    "Could not change startup settings",
                    "Headset diagnostics", "Microphone", "Refresh",
                    "Start a call or microphone test in another app. Press the headset button once at each prompt, five seconds apart. Microphone control is paused during this test; the headset may still perform its own action. Audio is not recorded.",
                    "Start", "Stop", "Save report…", "Close", "Select a microphone and start the test.",
                    "Press the headset button once", "Test finished. Save the report.", "Events", "No microphones are connected.",
                    "Diagnostics error", "Report saved", "Could not save the report", "Diagnostic report (*.jsonl)|*.jsonl",
                    "Raw HID input is unavailable. Media keys and microphone state are still logged.") },
                { "es", Entries(
                    "AirPods — micrófono",
                    "Micrófono silenciado",
                    "Micrófono activado",
                    "Esperando el uso del micrófono",
                    "Control del micrófono activado",
                    "Mostrar tarjeta emergente",
                    "Salir",
                    "AirPods: micrófono silenciado",
                    "AirPods: control en pausa",
                    "AirPods: esperando conexión",
                    "AirPods: micrófono activado",
                    " · esperando el uso del micrófono",
                    "No se pudo cambiar el estado del micrófono",
                    "No se pudo iniciar AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Oscuro",
                    "Iniciar con Windows",
                    "No se pudo cambiar el inicio automático",
                    "Diagnóstico de auriculares", "Micrófono", "Actualizar",
                    "Inicie una llamada o prueba del micrófono en otra aplicación. Pulse el botón del auricular una vez con cada indicación, cada 5 segundos. El control del micrófono está en pausa; el auricular puede realizar su propia acción. No se graba audio.",
                    "Iniciar", "Detener", "Guardar informe…", "Cerrar", "Seleccione un micrófono e inicie la prueba.",
                    "Pulse el botón del auricular una vez", "Prueba finalizada. Guarde el informe.", "Eventos", "No hay micrófonos conectados.",
                    "Error de diagnóstico", "Informe guardado", "No se pudo guardar el informe", "Informe de diagnóstico (*.jsonl)|*.jsonl",
                    "La entrada HID no está disponible. Se siguen registrando las teclas multimedia y el estado del micrófono.") },
                { "pt-BR", Entries(
                    "AirPods — microfone",
                    "Microfone desativado",
                    "Microfone ativado",
                    "Aguardando o uso do microfone",
                    "Controle do microfone ativado",
                    "Mostrar cartão pop-up",
                    "Sair",
                    "AirPods: microfone desativado",
                    "AirPods: controle pausado",
                    "AirPods: aguardando conexão",
                    "AirPods: microfone ativado",
                    " · aguardando o uso do microfone",
                    "Não foi possível alternar o microfone",
                    "Não foi possível iniciar o AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Escuro",
                    "Iniciar com o Windows",
                    "Não foi possível alterar a inicialização automática",
                    "Diagnóstico do headset", "Microfone", "Atualizar",
                    "Inicie uma chamada ou teste do microfone em outro aplicativo. Pressione o botão do headset uma vez a cada aviso, com 5 segundos de intervalo. O controle do microfone está pausado; o headset pode executar sua própria ação. O áudio não é gravado.",
                    "Iniciar", "Parar", "Salvar relatório…", "Fechar", "Selecione um microfone e inicie o teste.",
                    "Pressione o botão do headset uma vez", "Teste concluído. Salve o relatório.", "Eventos", "Nenhum microfone conectado.",
                    "Erro de diagnóstico", "Relatório salvo", "Não foi possível salvar o relatório", "Relatório de diagnóstico (*.jsonl)|*.jsonl",
                    "A entrada HID não está disponível. As teclas de mídia e o estado do microfone continuam sendo registrados.") },
                { "pt", Entries(
                    "AirPods — microfone",
                    "Microfone desligado",
                    "Microfone ligado",
                    "A aguardar a utilização do microfone",
                    "Controlo do microfone ativado",
                    "Mostrar cartão de notificação",
                    "Sair",
                    "AirPods: microfone desligado",
                    "AirPods: controlo em pausa",
                    "AirPods: a aguardar ligação",
                    "AirPods: microfone ligado",
                    " · a aguardar a utilização do microfone",
                    "Não foi possível alternar o microfone",
                    "Não foi possível iniciar o AirPods Mic Bridge.\n{0}",
                    "Idioma",
                    "Tema",
                    "Automático (Windows)",
                    "Claro",
                    "Escuro",
                    "Iniciar com o Windows",
                    "Não foi possível alterar o arranque automático",
                    "Diagnóstico dos auscultadores", "Microfone", "Atualizar",
                    "Inicie uma chamada ou teste do microfone noutra aplicação. Prima o botão dos auscultadores uma vez a cada indicação, com 5 segundos de intervalo. O controlo do microfone está em pausa; os auscultadores podem executar a sua própria ação. O áudio não é gravado.",
                    "Iniciar", "Parar", "Guardar relatório…", "Fechar", "Selecione um microfone e inicie o teste.",
                    "Prima o botão dos auscultadores uma vez", "Teste concluído. Guarde o relatório.", "Eventos", "Não há microfones ligados.",
                    "Erro de diagnóstico", "Relatório guardado", "Não foi possível guardar o relatório", "Relatório de diagnóstico (*.jsonl)|*.jsonl",
                    "A entrada HID não está disponível. As teclas multimédia e o estado do microfone continuam a ser registados.") },
                { "id", Entries(
                    "AirPods — mikrofon",
                    "Mikrofon dinonaktifkan",
                    "Mikrofon aktif",
                    "Menunggu penggunaan mikrofon",
                    "Kontrol mikrofon aktif",
                    "Tampilkan kartu notifikasi",
                    "Keluar",
                    "AirPods: mikrofon dinonaktifkan",
                    "AirPods: kontrol dijeda",
                    "AirPods: menunggu koneksi",
                    "AirPods: mikrofon aktif",
                    " · menunggu penggunaan mikrofon",
                    "Tidak dapat mengubah status mikrofon",
                    "Tidak dapat memulai AirPods Mic Bridge.\n{0}",
                    "Bahasa",
                    "Tema",
                    "Otomatis (Windows)",
                    "Terang",
                    "Gelap",
                    "Jalankan bersama Windows",
                    "Tidak dapat mengubah pengaturan mulai otomatis",
                    "Diagnostik headset", "Mikrofon", "Muat ulang",
                    "Mulai panggilan atau pengujian mikrofon di aplikasi lain. Tekan tombol headset sekali pada setiap petunjuk, dengan jeda 5 detik. Kontrol mikrofon dijeda; headset masih dapat menjalankan tindakannya sendiri. Audio tidak direkam.",
                    "Mulai", "Hentikan", "Simpan laporan…", "Tutup", "Pilih mikrofon dan mulai pengujian.",
                    "Tekan tombol headset sekali", "Pengujian selesai. Simpan laporan.", "Peristiwa", "Tidak ada mikrofon yang terhubung.",
                    "Kesalahan diagnostik", "Laporan disimpan", "Tidak dapat menyimpan laporan", "Laporan diagnostik (*.jsonl)|*.jsonl",
                    "Input HID tidak tersedia. Tombol media dan status mikrofon tetap dicatat.") }
            };
        }
    }
}
