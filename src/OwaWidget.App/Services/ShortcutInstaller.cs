using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace OwaWidget.App.Services;

public static class ShortcutInstaller
{
    public const string AppUserModelId = "OwaWidget.CorporateMail";

    private const string ShortcutName = "Owl.lnk";

    private const string LegacyShortcutName = "OWA Widget.lnk";

    public static void SetProcessIdentity()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (Exception exception)
        {
            Log.Error("set AUMID failed", exception);
        }
    }

    public static void EnsureShortcut()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            var programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs");

            var path = Path.Combine(programs, ShortcutName);
            RemoveLegacyShortcuts(programs);

            var activator = FindActivatorClsid(executable);

            if (File.Exists(path) && ResolveTarget(path) == executable)
            {
                Log.Info("shortcut already present");
                return;
            }

            Create(path, executable, activator);
            Log.Info($"shortcut created: {path}, activator={activator}");
        }
        catch (Exception exception)
        {
            Log.Error("shortcut creation failed", exception);
        }
    }

    private static void RemoveLegacyShortcuts(string programs)
    {
        foreach (var folder in new[] { programs, Path.Combine(programs, "Startup") })
        {
            var legacy = Path.Combine(folder, LegacyShortcutName);

            if (!File.Exists(legacy))
            {
                continue;
            }

            try
            {
                File.Delete(legacy);
                Log.Info($"legacy shortcut removed: {legacy}");
            }
            catch (Exception exception)
            {
                Log.Error("legacy shortcut removal failed", exception);
            }
        }
    }

    public static void SetStartupShortcut(bool enabled)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup", ShortcutName);

            if (!enabled)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Info("startup shortcut removed");
                }

                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            if (File.Exists(path) && ResolveTarget(path) == executable)
            {
                return;
            }

            Create(path, executable, FindActivatorClsid(executable));
            Log.Info($"startup shortcut created: {path}");
        }
        catch (Exception exception)
        {
            Log.Error("startup shortcut failed", exception);
        }
    }

    private static Guid FindActivatorClsid(string executable)
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID");
        if (classes is null)
        {
            return Guid.Empty;
        }

        foreach (var name in classes.GetSubKeyNames())
        {
            try
            {
                using var server = classes.OpenSubKey($@"{name}\LocalServer32");
                if (server?.GetValue(null) is not string command)
                {
                    continue;
                }

                if (command.Contains(executable, StringComparison.OrdinalIgnoreCase) &&
                    command.Contains("ToastActivated", StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(name, out var guid))
                {
                    return guid;
                }
            }
            catch (Exception)
            {
            }
        }

        return Guid.Empty;
    }

    private static string? ResolveTarget(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);

            var builder = new char[260];
            link.GetPath(builder, builder.Length, IntPtr.Zero, 0);

            return new string(builder).TrimEnd('\0');
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Create(string shortcutPath, string executable, Guid activator)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(executable);
        link.SetArguments(string.Empty);
        link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
        link.SetDescription("Корпоративная почта в трее");

        var store = (IPropertyStore)link;

        WriteString(store, PropertyKeys.AppUserModelId, AppUserModelId);

        if (activator != Guid.Empty)
        {
            WriteClsid(store, PropertyKeys.ToastActivatorClsid, activator);
        }

        store.Commit();

        ((IPersistFile)link).Save(shortcutPath, true);
    }

    private static void WriteString(IPropertyStore store, PropertyKey key, string value)
    {
        var variant = new PropVariant
        {
            ValueType = 31,
            Pointer = Marshal.StringToCoTaskMemUni(value)
        };

        try
        {
            store.SetValue(ref key, ref variant);
        }
        finally
        {
            Marshal.FreeCoTaskMem(variant.Pointer);
        }
    }

    private static void WriteClsid(IPropertyStore store, PropertyKey key, Guid value)
    {
        var buffer = Marshal.AllocCoTaskMem(16);

        try
        {
            Marshal.StructureToPtr(value, buffer, false);

            var variant = new PropVariant
            {
                ValueType = 72,
                Pointer = buffer
            };

            store.SetValue(ref key, ref variant);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static class PropertyKeys
    {
        private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        public static PropertyKey AppUserModelId => new(AppUserModel, 5);

        public static PropertyKey ToastActivatorClsid => new(AppUserModel, 26);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort ValueType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Padding;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] char[] file, int maxPath, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] char[] name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] char[] directory, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] char[] arguments, int maxArguments);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotKey(out short hotKey);

        void SetHotKey(short hotKey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] char[] iconPath, int iconPathLength, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);
}