using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace DeadLinks.Services;

public sealed record BackedUpItem(string Path, LinkKind Kind, string Target, bool IsDirectory, string? BackupFile);

public sealed record RemovalBatch(string Id, DateTime Time, List<BackedUpItem> Items)
{
    public bool Restored { get; set; }
    [JsonIgnore]
    public string Title => $"{Time:yyyy-MM-dd HH:mm}  ·  {Items.Count} item(s)" + (Restored ? "  ·  restored" : string.Empty);
}

public sealed record RemovalOutcome(string Path, bool Removed, string Message);

/// <summary>
/// Removes dead shortcuts and links. Before anything is removed a backup copy of each shortcut file and a manifest
/// are written; shortcuts then go to the Recycle Bin, links are deleted (a dead link has nothing to recycle).
/// Every removal is appended to removal-log.csv, and a batch can be restored from its manifest.
/// </summary>
public sealed class LinkRemover
{
    private readonly string _root;
    private readonly Func<string, bool> _deleteFile;

    public LinkRemover(string? dataFolder = null, Func<string, bool>? deleteFile = null)
    {
        _root = dataFolder ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeadLinks");
        _deleteFile = deleteFile ?? RecycleFile;
    }

    public string DataFolder => _root;
    private string HistoryFolder => System.IO.Path.Combine(_root, "history");
    public string LogPath => System.IO.Path.Combine(_root, "removal-log.csv");

    private static bool RecycleFile(string path)
    {
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        return true;
    }

    public (RemovalBatch? Batch, List<RemovalOutcome> Outcomes) Remove(IEnumerable<LinkItem> items)
    {
        var outcomes = new List<RemovalOutcome>();
        var todo = items.Where(i => i.Status == LinkStatus.TargetMissing || i.Status == LinkStatus.Unavailable).ToList();
        if (todo.Count == 0) return (null, outcomes);

        string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        string dir = System.IO.Path.Combine(HistoryFolder, id);
        Directory.CreateDirectory(System.IO.Path.Combine(dir, "files"));
        var batch = new RemovalBatch(id, DateTime.Now, new List<BackedUpItem>());

        // 1. back up first: nothing is removed unless its backup and the manifest are on disk
        int n = 0;
        var prepared = new List<(LinkItem Item, BackedUpItem Backup)>();
        foreach (var item in todo)
        {
            string? backupFile = null;
            try
            {
                if (item.Kind != LinkKind.SymbolicLink)
                {
                    backupFile = System.IO.Path.Combine("files", $"{++n}{System.IO.Path.GetExtension(item.Path)}");
                    File.Copy(item.Path, System.IO.Path.Combine(dir, backupFile), overwrite: true);
                }
                var b = new BackedUpItem(item.Path, item.Kind, item.Target, item.IsDirectory, backupFile);
                batch.Items.Add(b);
                prepared.Add((item, b));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new RemovalOutcome(item.Path, false, "Not removed: backup failed (" + ex.Message + ")"));
            }
        }
        SaveManifest(dir, batch);

        // 2. remove
        var removed = new List<BackedUpItem>();
        foreach (var (item, backup) in prepared)
        {
            try
            {
                if (item.Kind == LinkKind.SymbolicLink)
                {
                    if (item.IsDirectory) Directory.Delete(item.Path, recursive: false); else File.Delete(item.Path);
                }
                else
                {
                    _deleteFile(item.Path);
                }
                removed.Add(backup);
                outcomes.Add(new RemovalOutcome(item.Path, true, item.Kind == LinkKind.SymbolicLink ? "Link deleted" : "Moved to the Recycle Bin"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                string why = ex is UnauthorizedAccessException ? "needs administrator rights (not removed)" : ex.Message;
                outcomes.Add(new RemovalOutcome(item.Path, false, "Not removed: " + why));
            }
        }
        batch.Items.Clear();
        batch.Items.AddRange(removed);          // the manifest lists only what was really removed
        SaveManifest(dir, batch);
        AppendLog(batch, outcomes);
        if (removed.Count == 0) { try { Directory.Delete(dir, true); } catch (IOException) { } return (null, outcomes); }
        return (batch, outcomes);
    }

    // ── History and undo ────────────────────────────────────────

    public IReadOnlyList<RemovalBatch> History()
    {
        var list = new List<RemovalBatch>();
        if (!Directory.Exists(HistoryFolder)) return list;
        foreach (var d in Directory.GetDirectories(HistoryFolder).OrderByDescending(x => x, StringComparer.Ordinal))
        {
            try
            {
                var b = JsonSerializer.Deserialize<RemovalBatch>(File.ReadAllText(System.IO.Path.Combine(d, "manifest.json")));
                if (b != null) list.Add(b);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { /* skip a damaged entry */ }
        }
        return list;
    }

    /// <summary>Puts the removed shortcuts and links back. Existing files are never overwritten.</summary>
    public List<RemovalOutcome> Restore(RemovalBatch batch)
    {
        var outcomes = new List<RemovalOutcome>();
        string dir = System.IO.Path.Combine(HistoryFolder, batch.Id);
        foreach (var item in batch.Items)
        {
            try
            {
                if (File.Exists(item.Path) || Directory.Exists(item.Path)) { outcomes.Add(new RemovalOutcome(item.Path, false, "Already exists, left as it is")); continue; }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.Path)!);
                if (item.BackupFile != null)
                {
                    File.Copy(System.IO.Path.Combine(dir, item.BackupFile), item.Path);
                }
                else if (item.IsDirectory)
                {
                    try { Directory.CreateSymbolicLink(item.Path, item.Target); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MakeJunction(item.Path, item.Target); }
                }
                else
                {
                    File.CreateSymbolicLink(item.Path, item.Target);
                }
                outcomes.Add(new RemovalOutcome(item.Path, true, "Restored"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new RemovalOutcome(item.Path, false, "Could not restore: " + ex.Message));
            }
        }
        batch.Restored = outcomes.All(o => o.Removed || o.Message.StartsWith("Already", StringComparison.Ordinal));
        SaveManifest(dir, batch);
        return outcomes;
    }

    private static void MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi) ?? throw new IOException("Could not start cmd.exe");
        p.WaitForExit();
        if (p.ExitCode != 0) throw new IOException("A symbolic link or junction could not be created here.");
    }

    private static void SaveManifest(string dir, RemovalBatch batch) =>
        File.WriteAllText(System.IO.Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true }));

    private void AppendLog(RemovalBatch batch, List<RemovalOutcome> outcomes)
    {
        try
        {
            Directory.CreateDirectory(_root);
            bool header = !File.Exists(LogPath);
            var sb = new StringBuilder();
            if (header) sb.Append("Time,Path,Result\r\n");
            foreach (var o in outcomes) sb.Append(batch.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append(',').Append(Csv(o.Path)).Append(',').Append(Csv(o.Message)).Append("\r\n");
            File.AppendAllText(LogPath, sb.ToString(), new UTF8Encoding(true));
        }
        catch (IOException) { /* the log is a convenience; the manifest is what undo uses */ }
    }

    private static string Csv(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
