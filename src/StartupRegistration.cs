using System;
using System.IO;

namespace AirPodsMicBridge
{
    internal static class StartupRegistration
    {
        internal static readonly string StartupDirectory = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA")) ?
            Environment.GetFolderPath(Environment.SpecialFolder.Startup) : Path.Combine(PreferenceStore.DataDirectory, "startup");
        internal static readonly string ShortcutPath = Path.Combine(StartupDirectory, "AirPods Mic Bridge.lnk");
        internal static readonly string CommonStartupDirectory = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA")) ?
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) : Path.Combine(PreferenceStore.DataDirectory, "common-startup");
        internal static readonly string CommonShortcutPath = Path.Combine(CommonStartupDirectory, "AirPods Mic Bridge.lnk");
        internal static readonly string DisabledPath = Path.Combine(PreferenceStore.DataDirectory, "startup-disabled");
        internal static bool AutoStartAllowed { get { return !File.Exists(DisabledPath); } }
        internal static bool IsEnabled { get { return AutoStartAllowed && (File.Exists(ShortcutPath) || File.Exists(CommonShortcutPath)); } }

        internal static void SetEnabled(bool enabled, string executablePath)
        {
            if (!enabled)
            {
                if (File.Exists(CommonShortcutPath))
                {
                    Directory.CreateDirectory(PreferenceStore.DataDirectory);
                    File.WriteAllText(DisabledPath, String.Empty);
                }
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                return;
            }
            // Validate before changing the user's opt-out or existing registration.
            string target = ValidateTarget(executablePath);
            if (!File.Exists(CommonShortcutPath)) WriteShortcut(ShortcutPath, target);
            if (File.Exists(DisabledPath)) File.Delete(DisabledPath);
        }

        internal static void SetEnabledForAllUsers(bool enabled, string executablePath)
        {
            if (!enabled)
            {
                if (File.Exists(CommonShortcutPath)) File.Delete(CommonShortcutPath);
                SetEnabled(false, null);
                return;
            }
            string target = ValidateTarget(executablePath);
            WriteShortcut(CommonShortcutPath, target);
            // Migrate the installing user's existing shortcut without touching other users.
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            if (File.Exists(DisabledPath)) File.Delete(DisabledPath);
        }

        private static string ValidateTarget(string executablePath)
        {
            string target = Path.GetFullPath(executablePath);
            if (!File.Exists(target)) throw new FileNotFoundException("The startup executable does not exist.", target);
            return target;
        }

        private static void WriteShortcut(string shortcutPath, string target)
        {
            ShellShortcut.Write(shortcutPath, target, "--autostart", Path.GetDirectoryName(target));
        }

    }
}
