using System.Diagnostics;
using System.IO;
using DeadLinks.Services;

namespace DeadLinks.Tests;

public class LinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deadlinks-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly LinkScanner _scanner = new();

    public LinkTests()
    {
        Directory.CreateDirectory(_dir);
        _data = Path.Combine(_dir, "_data");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    private string P(string name) => Path.Combine(_dir, "tree", name);

    private string MakeFile(string name, string content = "x")
    {
        string p = P(name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private string MakeLnk(string name, string target)
    {
        string p = P(name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        ShellLinkReader.Create(p, target);
        return p;
    }

    private static bool Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    private static string FreeDriveLetter()
    {
        for (char c = 'Z'; c >= 'D'; c--) if (!Directory.Exists(c + @":\")) return c + @":\";
        throw new InvalidOperationException("no free drive letter");
    }

    private LinkItem ScanOne(string path)
    {
        var all = _scanner.Scan([Path.GetDirectoryName(path)!]);
        return all.Single(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    // ── Shortcuts (.lnk) ─────────────────────────────────────────

    [Fact]
    public void ShortcutToExistingFileIsOk()
    {
        string target = MakeFile("real.txt");
        var item = ScanOne(MakeLnk("good.lnk", target));
        Assert.Equal(LinkStatus.Ok, item.Status);
        Assert.Equal(LinkKind.Shortcut, item.Kind);
        Assert.Equal(target, item.Target, ignoreCase: true);
    }

    [Fact]
    public void ShortcutToExistingFolderIsOk()
    {
        string folder = Path.Combine(P("sub"), "inner");
        Directory.CreateDirectory(folder);
        Assert.Equal(LinkStatus.Ok, ScanOne(MakeLnk("folder.lnk", folder)).Status);
    }

    [Fact]
    public void ShortcutToMissingFileIsDead()
    {
        string target = Path.Combine(Path.GetDirectoryName(MakeFile("anchor.txt"))!, "gone.exe");
        var item = ScanOne(MakeLnk("dead.lnk", target));
        Assert.Equal(LinkStatus.TargetMissing, item.Status);
        Assert.Contains("no longer exists", item.Note);
    }

    [Fact]
    public void ShortcutToDeletedFileBecomesDead()
    {
        string target = MakeFile("temp.txt");
        string lnk = MakeLnk("temp.lnk", target);
        Assert.Equal(LinkStatus.Ok, ScanOne(lnk).Status);
        File.Delete(target);
        Assert.Equal(LinkStatus.TargetMissing, ScanOne(lnk).Status);
    }

    [Fact]
    public void ShortcutOnDisconnectedDriveIsUnavailableNotDead()
    {
        var item = ScanOne(MakeLnk("usb.lnk", FreeDriveLetter() + @"Tools\app.exe"));
        Assert.Equal(LinkStatus.Unavailable, item.Status);
        Assert.Contains("not connected", item.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData(@"C:\Program Files\WindowsApps\Some.App_1.0_x64__abc\app.exe")]
    [InlineData("relative\\path.exe")]
    public void NonFileTargetsAreNeverFlagged(string? raw)
    {
        Assert.Equal(LinkStatus.Unchecked, _scanner.ClassifyTarget(raw, out _));
    }

    [Fact]
    public void EnvironmentVariablesInTargetsAreExpanded()
    {
        Assert.Equal(LinkStatus.Ok, _scanner.ClassifyTarget(@"%WINDIR%\notepad.exe", out _));
        Assert.Equal(LinkStatus.TargetMissing, _scanner.ClassifyTarget(@"%WINDIR%\definitely-not-here-12345.exe", out _));
    }

    [Fact]
    public void UnreachableNetworkShareIsUnavailableAndFast()
    {
        var sw = Stopwatch.StartNew();
        var status = _scanner.ClassifyTarget(@"\\deadlinks-no-such-host-7f3a.invalid\share\file.txt", out string note);
        sw.Stop();
        Assert.Equal(LinkStatus.Unavailable, status);
        Assert.Contains("not reachable", note);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());
    }

    // ── Internet shortcuts (.url) ────────────────────────────────

    [Fact]
    public void UrlFilesToLocalFilesAreCheckedAndWebAddressesAreNot()
    {
        string real = MakeFile("doc.txt");
        File.WriteAllText(P("good.url"), "[InternetShortcut]\r\nURL=" + new Uri(real).AbsoluteUri + "\r\n");
        File.WriteAllText(P("gone.url"), "[InternetShortcut]\r\nURL=" + new Uri(Path.Combine(_dir, "tree", "missing file.txt")).AbsoluteUri + "\r\n");
        File.WriteAllText(P("web.url"), "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        File.WriteAllText(P("empty.url"), "[InternetShortcut]\r\n");

        var all = _scanner.Scan([Path.Combine(_dir, "tree")]).ToDictionary(i => Path.GetFileName(i.Path));
        Assert.Equal(LinkStatus.Ok, all["good.url"].Status);
        Assert.Equal(LinkStatus.TargetMissing, all["gone.url"].Status);
        Assert.Equal(LinkStatus.Unchecked, all["web.url"].Status);
        Assert.Equal(LinkStatus.Unchecked, all["empty.url"].Status);
        Assert.All(all.Values, i => Assert.Equal(LinkKind.InternetShortcut, i.Kind));
    }

    // ── Links and junctions ──────────────────────────────────────

    [Fact]
    public void JunctionIsOkWhileTargetExistsAndDeadAfterwards()
    {
        string target = Path.Combine(_dir, "jtarget");
        Directory.CreateDirectory(target);
        string link = P("junc");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.True(Junction(link, target));

        var item = ScanOne(link);
        Assert.Equal(LinkKind.SymbolicLink, item.Kind);
        Assert.True(item.IsDirectory);
        Assert.Equal(LinkStatus.Ok, item.Status);

        Directory.Delete(target);
        Assert.Equal(LinkStatus.TargetMissing, ScanOne(link).Status);
    }

    [Fact]
    public void FileSymlinkIsClassifiedWhenTheSystemAllowsCreatingOne()
    {
        string target = MakeFile("t.txt");
        string link = P("sym.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // needs Developer Mode or admin: skipped
        Assert.Equal(LinkStatus.Ok, ScanOne(link).Status);
        File.Delete(target);
        var dead = ScanOne(link);
        Assert.Equal(LinkStatus.TargetMissing, dead.Status);
        Assert.False(dead.IsDirectory);
    }

    [Fact]
    public void ScanDoesNotFollowJunctionsAndIgnoresPlainFiles()
    {
        string outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(outside);
        MakeLnk_At(Path.Combine(outside, "hidden.lnk"), Path.Combine(outside, "nothing.exe"));
        string link = P("jump");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.True(Junction(link, outside));
        MakeFile("plain.txt");
        MakeFile("doc.docx");

        var all = _scanner.Scan([Path.Combine(_dir, "tree")]);
        Assert.Single(all);                                          // only the junction itself
        Assert.DoesNotContain(all, i => i.Path.EndsWith("hidden.lnk", StringComparison.OrdinalIgnoreCase));
    }

    private static void MakeLnk_At(string path, string target) => ShellLinkReader.Create(path, target);

    // ── Scan behaviour ───────────────────────────────────────────

    [Fact]
    public void ScanFindsNestedShortcutsAndSortsDeadFirst()
    {
        string real = MakeFile("a/real.txt");
        MakeLnk("a/b/c/ok.lnk", real);
        MakeLnk("z/dead1.lnk", Path.Combine(_dir, "tree", "z", "nothing1.exe"));
        MakeLnk("dead2.lnk", Path.Combine(_dir, "tree", "nothing2.exe"));
        var all = _scanner.Scan([Path.Combine(_dir, "tree")]);
        Assert.Equal(3, all.Count);
        Assert.Equal([LinkStatus.TargetMissing, LinkStatus.TargetMissing, LinkStatus.Ok], all.Select(i => i.Status).ToArray());
    }

    [Fact]
    public void MissingRootsAndEmptyFoldersGiveNoResults()
    {
        Directory.CreateDirectory(P("empty"));
        Assert.Empty(_scanner.Scan([P("empty"), Path.Combine(_dir, "no-such-folder"), ""]));
    }

    [Fact]
    public void CancellationStopsTheScan()
    {
        MakeLnk("x.lnk", Path.Combine(_dir, "tree", "n.exe"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _scanner.Scan([Path.Combine(_dir, "tree")], cts.Token));
    }

    [Fact]
    public void DamagedShortcutFilesAreReportedAsNotCheckable()
    {
        string p = P("broken.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "this is not a shortcut");
        var item = ScanOne(p);
        Assert.Equal(LinkStatus.Unchecked, item.Status);
    }

    [Fact]
    public void AdvertisedShortcutDetection()
    {
        // header (0x4C bytes, flags = 0), one Darwin data block (size 0x314, signature 0xA0000006), terminal block
        var d = new byte[0x4C + 0x314 + 4];
        BitConverter.GetBytes(0x4Cu).CopyTo(d, 0);
        BitConverter.GetBytes(0x314u).CopyTo(d, 0x4C);
        BitConverter.GetBytes(0xA0000006u).CopyTo(d, 0x4C + 4);
        Assert.True(ShellLinkReader.IsAdvertisedShortcut(d));

        var plain = new byte[0x4C + 4];
        BitConverter.GetBytes(0x4Cu).CopyTo(plain, 0);
        Assert.False(ShellLinkReader.IsAdvertisedShortcut(plain));
        Assert.False(ShellLinkReader.IsAdvertisedShortcut([1, 2, 3]));
        Assert.False(ShellLinkReader.IsAdvertisedShortcut(File.ReadAllBytes(MakeLnk("real.lnk", MakeFile("r.txt")))));
    }

    // ── Removal, log and undo ────────────────────────────────────

    private LinkRemover Remover(Func<string, bool>? del = null) => new(_data, del ?? (p => { File.Delete(p); return true; }));

    [Fact]
    public void RemoveBacksUpLogsAndOnlyTouchesDeadItems()
    {
        string real = MakeFile("keep.txt");
        string good = MakeLnk("good.lnk", real);
        string dead = MakeLnk("dead.lnk", Path.Combine(_dir, "tree", "nothing.exe"));
        var items = _scanner.Scan([Path.Combine(_dir, "tree")]);

        var (batch, outcomes) = Remover().Remove(items);     // the Ok item is passed in as well and must be ignored
        Assert.NotNull(batch);
        Assert.Single(batch!.Items);
        Assert.False(File.Exists(dead));
        Assert.True(File.Exists(good));
        Assert.True(File.Exists(real));
        Assert.Equal([true], outcomes.Select(o => o.Removed));
        Assert.True(File.Exists(Path.Combine(_data, "removal-log.csv")));
        Assert.Contains("dead.lnk", File.ReadAllText(Path.Combine(_data, "removal-log.csv")));
        Assert.Single(Remover().History());
    }

    [Fact]
    public void RestoreBringsBackTheSameShortcutAndNeverOverwrites()
    {
        string dead = MakeLnk("dead.lnk", Path.Combine(_dir, "tree", "nothing.exe"));
        byte[] original = File.ReadAllBytes(dead);
        var remover = Remover();
        var (batch, _) = remover.Remove(_scanner.Scan([Path.Combine(_dir, "tree")]));
        Assert.False(File.Exists(dead));

        var restored = remover.Restore(batch!);
        Assert.True(restored.Single().Removed);
        Assert.Equal(original, File.ReadAllBytes(dead));
        Assert.True(remover.History().Single().Restored);

        File.WriteAllText(dead, "changed since");
        var again = remover.Restore(batch!);
        Assert.False(again.Single().Removed);
        Assert.Equal("changed since", File.ReadAllText(dead));
    }

    [Fact]
    public void DeadJunctionIsRemovedAndRestoredWithoutTouchingOtherFolders()
    {
        string target = Path.Combine(_dir, "jt");
        Directory.CreateDirectory(target);
        string link = P("junc");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.True(Junction(link, target));
        string bystander = MakeFile("bystander.txt");
        Directory.Delete(target);

        var remover = Remover();
        var (batch, outcomes) = remover.Remove(_scanner.Scan([Path.Combine(_dir, "tree")]).Where(i => i.Kind == LinkKind.SymbolicLink));
        Assert.True(outcomes.Single().Removed);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(bystander));

        Directory.CreateDirectory(target);
        Assert.True(remover.Restore(batch!).Single().Removed);
        Assert.True(Directory.Exists(link));
        Assert.True(new DirectoryInfo(link).LinkTarget != null);
    }

    [Fact]
    public void FailedDeleteIsReportedAndNotRecordedAsRemoved()
    {
        string dead = MakeLnk("locked.lnk", Path.Combine(_dir, "tree", "nothing.exe"));
        var (batch, outcomes) = Remover(_ => throw new UnauthorizedAccessException()).Remove(_scanner.Scan([Path.Combine(_dir, "tree")]));
        Assert.Null(batch);
        Assert.False(outcomes.Single().Removed);
        Assert.Contains("administrator", outcomes.Single().Message);
        Assert.True(File.Exists(dead));
    }

    [Fact]
    public void ShortcutsGoToTheRecycleBinByDefault()
    {
        string dead = MakeLnk("recycle-me.lnk", Path.Combine(_dir, "tree", "nothing.exe"));
        var remover = new LinkRemover(_data);                         // real Recycle Bin
        var (batch, outcomes) = remover.Remove(_scanner.Scan([Path.Combine(_dir, "tree")]));
        Assert.NotNull(batch);
        Assert.True(outcomes.Single().Removed, outcomes.Single().Message);
        Assert.Equal("Moved to the Recycle Bin", outcomes.Single().Message);
        Assert.False(File.Exists(dead));
    }

    [Fact]
    public void NothingToRemoveDoesNothing()
    {
        var (batch, outcomes) = Remover().Remove([]);
        Assert.Null(batch);
        Assert.Empty(outcomes);
        Assert.False(Directory.Exists(Path.Combine(_data, "history")));
    }
}
