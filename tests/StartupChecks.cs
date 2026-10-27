using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AirPodsMicBridge;

// Real .lnk round trips in the caller's mandatory isolated fixture directory. No programs are launched.
internal static class StartupChecks
{
    private static int checks;

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string configured = Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA");
            Assert(!String.IsNullOrWhiteSpace(configured), "AIRPODS_MIC_BRIDGE_DATA must name an isolated fixture directory.");
            string isolated = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fixtureStartup = Path.Combine(isolated, "startup");
            string actualStartup = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Startup,
                Environment.SpecialFolderOption.DoNotVerify)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert(!String.Equals(fixtureStartup, actualStartup, StringComparison.OrdinalIgnoreCase) &&
                !fixtureStartup.StartsWith(actualStartup + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Startup checks must not use the real user startup directory or its descendants.");
            Assert(String.Equals(PreferenceStore.DataDirectory, isolated, StringComparison.OrdinalIgnoreCase), "Data-directory override was not applied.");
            Assert(String.Equals(Path.GetFullPath(StartupRegistration.StartupDirectory), fixtureStartup, StringComparison.OrdinalIgnoreCase), "Startup registration is not using the isolated startup directory.");
            Assert(String.Equals(Path.GetDirectoryName(StartupRegistration.ShortcutPath), StartupRegistration.StartupDirectory, StringComparison.OrdinalIgnoreCase), "Shortcut escaped the isolated startup directory.");
            string actualCommon = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup,
                Environment.SpecialFolderOption.DoNotVerify)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert(String.Equals(StartupRegistration.CommonStartupDirectory, Path.Combine(isolated, "common-startup"), StringComparison.OrdinalIgnoreCase) &&
                !StartupRegistration.CommonStartupDirectory.StartsWith(actualCommon + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Common startup checks must stay inside the isolated fixture.");
            CheckRegistration(isolated);
            CheckCommonRegistration(isolated);
            Console.WriteLine("Startup checks passed: " + checks);
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

    private static void CheckRegistration(string isolated)
    {
        string fixtureRoot = Path.Combine(isolated, "startup-checks-" + Guid.NewGuid().ToString("N"));
        string firstDirectory = Path.Combine(fixtureRoot, "Приложение с пробелами");
        string secondDirectory = Path.Combine(fixtureRoot, "Новое расположение программы");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        string firstExecutable = Path.Combine(firstDirectory, "Проверка микрофона.exe");
        string secondExecutable = Path.Combine(secondDirectory, "Обновлённая программа.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, firstExecutable);
        File.Copy(Assembly.GetExecutingAssembly().Location, secondExecutable);
        string missingExecutable = Path.Combine(fixtureRoot, "missing executable.exe");
        string shortcutPath = StartupRegistration.ShortcutPath;
        byte[] original = File.Exists(shortcutPath) ? File.ReadAllBytes(shortcutPath) : null;
        string otherShortcut = Path.Combine(StartupRegistration.StartupDirectory, "StartupChecks unrelated " + Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            StartupRegistration.SetEnabled(false, null);
            Assert(!StartupRegistration.IsEnabled, "Disabled registration still reports enabled.");
            bool missingRejected = false;
            try { StartupRegistration.SetEnabled(true, missingExecutable); }
            catch (FileNotFoundException) { missingRejected = true; }
            Assert(missingRejected && !StartupRegistration.IsEnabled, "A missing executable must not create startup registration.");

            Directory.CreateDirectory(StartupRegistration.StartupDirectory);
            WriteShortcut(otherShortcut, firstExecutable, "--unrelated", firstDirectory);
            byte[] unrelated = File.ReadAllBytes(otherShortcut);
            StartupRegistration.SetEnabled(true, firstExecutable);
            Assert(StartupRegistration.IsEnabled, "Creating a startup shortcut did not enable registration.");
            CheckShortcut(shortcutPath, firstExecutable, firstDirectory, "Initial shortcut");
            Assert(SameBytes(unrelated, File.ReadAllBytes(otherShortcut)), "Enabling registration changed an unrelated shortcut.");

            // Exercise replacement of an existing shortcut, including stale arguments/working directory.
            WriteShortcut(shortcutPath, firstExecutable, "--stale-fixture-argument", firstDirectory);
            StartupRegistration.SetEnabled(true, secondExecutable);
            Assert(StartupRegistration.IsEnabled, "Updating the executable disabled startup registration.");
            CheckShortcut(shortcutPath, secondExecutable, secondDirectory, "Updated shortcut");
            Assert(SameBytes(unrelated, File.ReadAllBytes(otherShortcut)), "Updating registration changed an unrelated shortcut.");
            StartupRegistration.SetEnabled(true, secondExecutable);
            CheckShortcut(shortcutPath, secondExecutable, secondDirectory, "Repeated enable");

            byte[] validShortcut = File.ReadAllBytes(shortcutPath);
            missingRejected = false;
            try { StartupRegistration.SetEnabled(true, missingExecutable); }
            catch (FileNotFoundException) { missingRejected = true; }
            Assert(missingRejected && StartupRegistration.IsEnabled, "Rejecting an absent update target must preserve existing registration.");
            Assert(SameBytes(validShortcut, File.ReadAllBytes(shortcutPath)), "An absent update target rewrote the existing shortcut.");
            CheckShortcut(shortcutPath, secondExecutable, secondDirectory, "After failed update");

            StartupRegistration.SetEnabled(false, missingExecutable);
            Assert(!StartupRegistration.IsEnabled && !File.Exists(shortcutPath), "Disabling registration must remove only the application shortcut.");
            Assert(File.Exists(otherShortcut) && SameBytes(unrelated, File.ReadAllBytes(otherShortcut)), "Disabling registration removed or altered an unrelated shortcut.");
            StartupRegistration.SetEnabled(false, null);
            Assert(!StartupRegistration.IsEnabled, "Repeated disable must succeed without a valid target path.");
        }
        finally
        {
            if (original != null) File.WriteAllBytes(shortcutPath, original);
            else if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
            if (File.Exists(otherShortcut)) File.Delete(otherShortcut);
            if (File.Exists(firstExecutable)) File.Delete(firstExecutable);
            if (File.Exists(secondExecutable)) File.Delete(secondExecutable);
            // Leave empty fixture directories under the caller's disposable test root.
        }
    }

    private static void CheckShortcut(string path, string executable, string workingDirectory, string label)
    {
        // This is an independent native reader, not the product writer. WScript's
        // legacy reader did not preserve Cyrillic on the English Windows CI image.
        object instance = null;
        string stage = "Load";
        try
        {
            instance = new ShortcutReadComObject();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)instance).Load(path, 0);
            var link = (IShortcutReadW)instance;
            var target = new System.Text.StringBuilder(32768);
            var arguments = new System.Text.StringBuilder(32768);
            var directory = new System.Text.StringBuilder(32768);
            stage = "GetPath";
            CheckResult(link.GetPath(target, target.Capacity, IntPtr.Zero, 4)); // SLGP_RAWPATH
            stage = "GetArguments";
            CheckResult(link.GetArguments(arguments, arguments.Capacity));
            stage = "GetWorkingDirectory";
            CheckResult(link.GetWorkingDirectory(directory, directory.Capacity));
            Assert(String.Equals(target.ToString(), executable, StringComparison.Ordinal), label +
                ": Unicode target mismatch; expected=" + executable + "; actual=" + target);
            Assert(arguments.ToString() == "--autostart", label + ": startup must respect the current user's opt-out; actual=" + arguments);
            Assert(String.Equals(directory.ToString(), workingDirectory, StringComparison.Ordinal), label +
                ": Unicode working directory mismatch; expected=" + workingDirectory + "; actual=" + directory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(label + ": native shortcut reader failed at " + stage +
                "; shortcut=" + path + "; shortcutLength=" + path.Length +
                "; targetLength=" + executable.Length + "; workingDirectoryLength=" + workingDirectory.Length, ex);
        }
        finally { Release(instance); }
        // Also inspect the serialized link independently of COM getters.
        byte[] bytes = File.ReadAllBytes(path);
        Assert(ContainsBytes(bytes, System.Text.Encoding.Unicode.GetBytes(workingDirectory)),
            label + ": serialized link lacks the exact UTF-16 working directory.");
        Assert(ContainsBytes(bytes, System.Text.Encoding.Unicode.GetBytes(Path.GetFileName(executable))),
            label + ": serialized link lacks the exact UTF-16 target file name.");
    }

    private static void CheckCommonRegistration(string isolated)
    {
        string executable = Assembly.GetExecutingAssembly().Location;
        string directory = Path.GetDirectoryName(executable);
        string userPath = StartupRegistration.ShortcutPath;
        string commonPath = StartupRegistration.CommonShortcutPath;
        string disabledPath = StartupRegistration.DisabledPath;
        byte[] userBefore = File.Exists(userPath) ? File.ReadAllBytes(userPath) : null;
        byte[] commonBefore = File.Exists(commonPath) ? File.ReadAllBytes(commonPath) : null;
        byte[] disabledBefore = File.Exists(disabledPath) ? File.ReadAllBytes(disabledPath) : null;
        string other = Path.Combine(StartupRegistration.CommonStartupDirectory, "Unrelated " + Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            StartupRegistration.SetEnabledForAllUsers(false, null);
            StartupRegistration.SetEnabled(true, executable);
            Assert(File.Exists(userPath), "Per-user enable must create its shortcut when no common registration exists.");
            StartupRegistration.SetEnabledForAllUsers(true, executable);
            Assert(!File.Exists(userPath) && File.Exists(commonPath) && StartupRegistration.IsEnabled, "All-user enable must migrate the installing user's shortcut.");
            CheckShortcut(commonPath, executable, directory, "Common shortcut");
            WriteShortcut(other, executable, "--unrelated", directory);
            byte[] unrelated = File.ReadAllBytes(other);
            byte[] registered = File.ReadAllBytes(commonPath);
            StartupRegistration.SetEnabled(false, null);
            Assert(File.Exists(commonPath) && SameBytes(registered, File.ReadAllBytes(commonPath)), "User opt-out must preserve the common shortcut.");
            Assert(File.Exists(disabledPath) && !StartupRegistration.IsEnabled && !StartupRegistration.AutoStartAllowed, "User opt-out must suppress automatic launch.");
            StartupRegistration.SetEnabled(false, null);
            Assert(!StartupRegistration.IsEnabled, "Repeated common-registration opt-out must remain disabled.");
            bool rejected = false;
            try { StartupRegistration.SetEnabled(true, Path.Combine(isolated, "missing.exe")); }
            catch (FileNotFoundException) { rejected = true; }
            Assert(rejected && !StartupRegistration.IsEnabled && File.Exists(disabledPath), "Invalid enable must preserve the user's opt-out.");
            StartupRegistration.SetEnabled(true, executable);
            Assert(StartupRegistration.IsEnabled && StartupRegistration.AutoStartAllowed && !File.Exists(disabledPath), "User enable must clear the opt-out.");
            Assert(!File.Exists(userPath) && SameBytes(registered, File.ReadAllBytes(commonPath)), "User enable must reuse common registration without a second shortcut.");
            StartupRegistration.SetEnabledForAllUsers(false, null);
            Assert(!File.Exists(commonPath) && !StartupRegistration.IsEnabled, "All-user disable must remove the application's common shortcut.");
            Assert(File.Exists(other) && SameBytes(unrelated, File.ReadAllBytes(other)), "All-user disable must preserve unrelated startup entries.");
            StartupRegistration.SetEnabled(true, executable);
            Assert(File.Exists(userPath) && StartupRegistration.IsEnabled, "User can enable startup after common registration is removed.");
        }
        finally
        {
            Restore(userPath, userBefore);
            Restore(commonPath, commonBefore);
            Restore(disabledPath, disabledBefore);
            if (File.Exists(other)) File.Delete(other);
        }
    }

    private static void Restore(string path, byte[] bytes)
    {
        if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
        else { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
    }

    private static void WriteShortcut(string path, string executable, string arguments, string workingDirectory)
    {
        object shell = null, shortcut = null;
        try
        {
            ShellShortcut.Write(path, executable, arguments, workingDirectory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Fixture shortcut creation failed: shortcut=" + path +
                "; shortcutLength=" + path.Length + "; target=" + executable + "; targetLength=" + executable.Length +
                "; targetExists=" + File.Exists(executable) + "; workingDirectory=" + workingDirectory +
                "; workingDirectoryLength=" + workingDirectory.Length + "; directoryExists=" + Directory.Exists(workingDirectory) +
                "; culture=" + System.Globalization.CultureInfo.CurrentCulture.Name +
                "; uiCulture=" + System.Globalization.CultureInfo.CurrentUICulture.Name +
                "; process64=" + Environment.Is64BitProcess + "; OS=" + Environment.OSVersion.VersionString, ex);
        }
        finally { Release(shortcut); Release(shell); }
    }

    private static void CheckResult(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    private static bool ContainsBytes(byte[] bytes, byte[] pattern)
    {
        for (int offset = 0; offset <= bytes.Length - pattern.Length; offset++)
        {
            int index = 0;
            while (index < pattern.Length && bytes[offset + index] == pattern[index]) index++;
            if (index == pattern.Length) return true;
        }
        return false;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private sealed class ShortcutReadComObject { }

    // Full native vtable, declared independently of src/ShellShortcut.cs.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShortcutReadW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int capacity, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemList);
        [PreserveSig] int SetIDList(IntPtr itemList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder description, int capacity);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int capacity);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int capacity);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out ushort hotkey);
        [PreserveSig] int SetHotkey(ushort hotkey);
        [PreserveSig] int GetShowCmd(out int command);
        [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int capacity, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }
}
