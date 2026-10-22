using System.Collections.Concurrent;
using System.IO;
using System.IO.Enumeration;
using System.Text;

namespace DeadLinks.Services;

/// <summary>Finds shortcuts (.lnk, .url) and symbolic links / junctions and says whether their targets exist. Read only.</summary>
public sealed class LinkScanner
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(2.5);
    private readonly ConcurrentDictionary<string, bool> _unreachable = new(StringComparer.OrdinalIgnoreCase);

    // ── Locations ───────────────────────────────────────────────

    public static IReadOnlyList<(string Name, string Path)> DefaultLocations()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var list = new List<(string, string)>
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Public desktop", Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
            ("Start menu", Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)),
            ("Start menu (all users)", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)),
            ("Taskbar pins", System.IO.Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")),
            ("Quick Launch", System.IO.Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch")),
        };
        return list.Where(l => !string.IsNullOrEmpty(l.Item2) && Directory.Exists(l.Item2)).ToList();
    }

    // ── Scan ────────────────────────────────────────────────────

    public IReadOnlyList<LinkItem> Scan(IEnumerable<string> roots, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var items = new List<LinkItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int visited = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };
        foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => System.IO.Path.GetFullPath(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            var candidates = new FileSystemEnumerable<(string Path, bool IsDir, bool IsLink)>(root,
                (ref FileSystemEntry e) => (e.ToFullPath(), e.IsDirectory, (e.Attributes & FileAttributes.ReparsePoint) != 0), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                    if (e.IsDirectory) return false;
                    var name = e.FileName;
                    return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
                },
                ShouldRecursePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0,   // never follow links out of the tree
            };
            foreach (var (path, isDir, isLink) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (++visited % 50 == 0) progress?.Report(visited);
                if (!seen.Add(path)) continue;
                var item = Inspect(path, isDir, isLink);
                if (item != null) items.Add(item);
            }
        }
        progress?.Report(visited);
        return items.OrderBy(i => Rank(i.Status)).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Dead links first, then offline, then not checkable, then fine.</summary>
    private static int Rank(LinkStatus s) => s switch { LinkStatus.TargetMissing => 0, LinkStatus.Unavailable => 1, LinkStatus.Unchecked => 2, _ => 3 };

    /// <summary>Classifies one path; null when it is a plain file or a reparse point that is not a link (for example a cloud placeholder).</summary>
    public LinkItem? Inspect(string path, bool isDirectory, bool isReparse)
    {
        string lower = path.ToLowerInvariant();
        try
        {
            if (!isDirectory && lower.EndsWith(".lnk", StringComparison.Ordinal)) return InspectShortcut(path);
            if (!isDirectory && lower.EndsWith(".url", StringComparison.Ordinal)) return InspectInternetShortcut(path);
            if (isReparse) return InspectLink(path, isDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidDataException)
        {
            return new LinkItem(path, lower.EndsWith(".url", StringComparison.Ordinal) ? LinkKind.InternetShortcut : LinkKind.Shortcut,
                string.Empty, LinkStatus.Unchecked, "Could not be read");
        }
        return null;
    }

    private LinkItem InspectShortcut(string path)
    {
        var info = ShellLinkReader.Read(path, resolveMoved: true);
        if (info.IsAdvertised && !PathExists(info.Path))
            return new LinkItem(path, LinkKind.Shortcut, info.Path, LinkStatus.Unchecked, "Installed on demand by Windows Installer");
        var status = ClassifyTarget(info.Path, out string note);
        return new LinkItem(path, LinkKind.Shortcut, info.Path, status, note);
    }

    private LinkItem InspectInternetShortcut(string path)
    {
        string? url = ReadUrlFile(path);
        if (url == null) return new LinkItem(path, LinkKind.InternetShortcut, string.Empty, LinkStatus.Unchecked, "No address in the file");
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new LinkItem(path, LinkKind.InternetShortcut, url, LinkStatus.Unchecked, "Web address: not tested (DeadLinks never goes online)");
        string local;
        try { local = new Uri(url).LocalPath; }
        catch (UriFormatException) { return new LinkItem(path, LinkKind.InternetShortcut, url, LinkStatus.Unchecked, "Address not understood"); }
        var status = ClassifyTarget(local, out string note);
        return new LinkItem(path, LinkKind.InternetShortcut, local, status, note);
    }

    private LinkItem? InspectLink(string path, bool isDirectory)
    {
        FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        string? raw = info.LinkTarget;
        if (raw == null) return null;                                     // not a symbolic link or junction (cloud placeholder, dedup, …)
        string target = System.IO.Path.IsPathRooted(raw) ? raw : System.IO.Path.GetFullPath(raw, System.IO.Path.GetDirectoryName(path)!);
        if (target.StartsWith(@"\??\", StringComparison.Ordinal)) target = target[4..];
        var status = ClassifyTarget(target, out string note);
        return new LinkItem(path, LinkKind.SymbolicLink, target, status, note, isDirectory);
    }

    public static string? ReadUrlFile(string path)
    {
        foreach (string line in File.ReadLines(path, Encoding.Default))
        {
            var t = line.Trim();
            if (t.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return t[4..].Trim();
        }
        return null;
    }

    // ── Target classification ───────────────────────────────────

    public LinkStatus ClassifyTarget(string? raw, out string note)
    {
        note = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            note = "No file target (app, system or Store shortcut)";
            return LinkStatus.Unchecked;
        }
        string target = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("::", StringComparison.Ordinal) || !System.IO.Path.IsPathRooted(target))
        {
            note = "Not a file path";
            return LinkStatus.Unchecked;
        }
        if (target.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            note = "Microsoft Store app";
            return LinkStatus.Unchecked;
        }

        string root = System.IO.Path.GetPathRoot(target) ?? string.Empty;
        bool network = root.StartsWith(@"\\", StringComparison.Ordinal) || IsNetworkDrive(root);
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            string host = root.TrimStart('\\').Split('\\')[0];
            if (_unreachable.ContainsKey(host))
            {
                note = $"Network location {host} is not reachable";
                return LinkStatus.Unavailable;
            }
            if (!ExistsWithTimeout(root, directory: true))
            {
                _unreachable[host] = true;
                note = $"Network location {host} is not reachable";
                return LinkStatus.Unavailable;
            }
        }
        else if (!Directory.Exists(root))
        {
            note = $"Drive {root.TrimEnd('\\')} is not connected";
            return LinkStatus.Unavailable;
        }

        bool exists = network ? ExistsWithTimeout(target, directory: null) : PathExists(target);
        if (exists) return LinkStatus.Ok;

        string? parent = System.IO.Path.GetDirectoryName(target.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) && !CanList(parent))
        {
            note = "No permission to check this location";
            return LinkStatus.Unchecked;
        }
        note = "The target no longer exists";
        return LinkStatus.TargetMissing;
    }

    private static bool PathExists(string path) => path.Length > 0 && (File.Exists(path) || Directory.Exists(path));

    private static bool CanList(string dir)
    {
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
            e.MoveNext();
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool IsNetworkDrive(string root)
    {
        try { return root.Length >= 2 && new DriveInfo(root).DriveType == DriveType.Network; }
        catch (ArgumentException) { return false; }
    }

    private static bool ExistsWithTimeout(string path, bool? directory)
    {
        var task = Task.Run(() => directory == true ? Directory.Exists(path) : directory == false ? File.Exists(path) : PathExists(path));
        return task.Wait(NetworkTimeout) && task.Result;
    }
}
