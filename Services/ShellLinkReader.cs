using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeadLinks.Services;

/// <summary>Reads and (for tests) writes Windows shortcuts (.lnk) with the shell link interfaces.</summary>
public static class ShellLinkReader
{
    public sealed record Info(string Path, bool IsAdvertised);

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    private const uint SLR_NO_UI = 0x1, SLR_NOUPDATE = 0x8, SLR_NOSEARCH = 0x10;

    /// <summary>Target path of a shortcut. With <paramref name="resolveMoved"/> the shell also tries to find a target that was moved (link tracking, no search, no dialog).</summary>
    public static Info Read(string lnkPath, bool resolveMoved = false)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            string path = GetPath(link);
            if (resolveMoved && !Exists(path))
            {
                try
                {
                    link.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_NOUPDATE | SLR_NOSEARCH);
                    string moved = GetPath(link);
                    if (moved.Length > 0) path = moved;
                }
                catch (COMException) { /* cannot be resolved: keep the stored path */ }
            }
            bool advertised;
            try { advertised = IsAdvertisedShortcut(File.ReadAllBytes(lnkPath)); }
            catch (IOException) { advertised = false; }
            return new Info(path, advertised);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string GetPath(IShellLinkW link)
    {
        var sb = new StringBuilder(1024);
        link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
        return sb.ToString();
    }

    private static bool Exists(string path) => path.Length > 0 && (File.Exists(path) || Directory.Exists(path));

    /// <summary>Creates a shortcut (used by the tests and the fixtures of the user guide).</summary>
    public static void Create(string lnkPath, string target)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// True for a Windows Installer "advertised" shortcut (Darwin data block in the extra data): its target is
    /// installed on demand, so a missing path does not mean the shortcut is dead. Layout: [MS-SHLLINK].
    /// </summary>
    public static bool IsAdvertisedShortcut(byte[] d)
    {
        if (d.Length < 0x4C || BitConverter.ToUInt32(d, 0) != 0x4C) return false;
        uint flags = BitConverter.ToUInt32(d, 0x14);
        long pos = 0x4C;
        bool unicode = (flags & 0x80) != 0;
        if ((flags & 0x1) != 0)                                   // HasLinkTargetIDList
        {
            if (pos + 2 > d.Length) return false;
            pos += 2 + BitConverter.ToUInt16(d, (int)pos);
        }
        if ((flags & 0x2) != 0)                                   // HasLinkInfo
        {
            if (pos + 4 > d.Length) return false;
            pos += BitConverter.ToUInt32(d, (int)pos);
        }
        foreach (uint bit in new uint[] { 0x4, 0x8, 0x10, 0x20, 0x40 })   // name, relative path, working dir, arguments, icon
        {
            if ((flags & bit) == 0) continue;
            if (pos + 2 > d.Length) return false;
            pos += 2 + BitConverter.ToUInt16(d, (int)pos) * (unicode ? 2 : 1);
        }
        while (pos + 8 <= d.Length)
        {
            uint size = BitConverter.ToUInt32(d, (int)pos);
            if (size < 4) break;
            if (size >= 8 && BitConverter.ToUInt32(d, (int)pos + 4) == 0xA0000006) return true;
            pos += size;
        }
        return false;
    }
}
