using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AirPodsMicBridge
{
    // Use the explicit Unicode shell interface rather than WScript.Shell's
    // TargetPath setter, which rejects an existing Cyrillic path on the CI image.
    internal static class ShellShortcut
    {
        internal static void Write(string shortcutPath, string target, string arguments, string workingDirectory)
        {
            if (String.IsNullOrWhiteSpace(shortcutPath)) throw new ArgumentException("A shortcut path is required.", "shortcutPath");
            if (String.IsNullOrWhiteSpace(target)) throw new ArgumentException("A target path is required.", "target");
            string path = Path.GetFullPath(shortcutPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            object instance = null;
            try
            {
                instance = new ShellLinkComObject();
                var link = (IShellLinkW)instance;
                Check(link.SetPath(target));
                Check(link.SetArguments(arguments ?? String.Empty));
                Check(link.SetWorkingDirectory(workingDirectory ?? String.Empty));
                Check(link.SetIconLocation(target, 0));
                Check(link.SetDescription("AirPods Mic Bridge"));
                ((IPersistFile)instance).Save(path, true);
            }
            finally
            {
                // Both interface casts share this one owned RCW.
                if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
            }
        }

        private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        private sealed class ShellLinkComObject { }

        // Every method is declared in the native IShellLinkW vtable order, even
        // when unused. Setters take UTF-16 strings; no ANSI/code-page conversion.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);
            [PreserveSig] int GetIDList(out IntPtr itemList);
            [PreserveSig] int SetIDList(IntPtr itemList);
            [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);
            [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
            [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
            [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            [PreserveSig] int GetHotkey(out ushort hotkey);
            [PreserveSig] int SetHotkey(ushort hotkey);
            [PreserveSig] int GetShowCmd(out int command);
            [PreserveSig] int SetShowCmd(int command);
            [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);
            [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
            [PreserveSig] int Resolve(IntPtr window, uint flags);
            [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}