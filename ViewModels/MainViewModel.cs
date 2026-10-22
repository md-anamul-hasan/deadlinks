using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DeadLinks.Services;
using Microsoft.Win32;

namespace DeadLinks.ViewModels;

public sealed class LocationOption : ViewModelBase
{
    private bool _isChecked;
    public required string Name { get; init; }
    public required string Path { get; init; }
    /// <summary>Path as shown in the list: the user profile folder is written as %USERPROFILE%.</summary>
    public string DisplayPath => PathDisplay.Shorten(Path);
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}

public sealed class LinkRow : ViewModelBase
{
    private bool _isSelected;
    public LinkRow(LinkItem item) => Item = item;
    public LinkItem Item { get; }
    public bool CanSelect => Item.Status is LinkStatus.TargetMissing or LinkStatus.Unavailable;
    public bool IsSelected { get => _isSelected; set { if (CanSelect) SetProperty(ref _isSelected, value); } }
    public string Name => System.IO.Path.GetFileName(Item.Path);
    public string Folder => System.IO.Path.GetDirectoryName(Item.Path) ?? string.Empty;
    public string TargetText => Item.Target.Length == 0 ? "–" : Item.Target;
}

public sealed class MainViewModel : ShellViewModel
{
    private readonly LinkScanner _scanner = new();
    private readonly LinkRemover _remover;
    private readonly List<LinkItem> _items = new();
    private bool _isBusy;
    private bool _hasScanned;
    private bool _showAll;
    private int _visited;
    private string _statusText = "Ready";
    private string _summary = string.Empty;
    private CancellationTokenSource? _cts;

    public MainViewModel() : this(null) { }

    public MainViewModel(LinkRemover? remover)
    {
        _remover = remover ?? new LinkRemover();
        foreach (var (name, path) in LinkScanner.DefaultLocations())
            Locations.Add(new LocationOption { Name = name, Path = path, IsChecked = !name.Equals("Quick Launch", StringComparison.Ordinal) });

        AddFolderCommand = new RelayCommand(AddFolder);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsBusy);
        SelectDeadCommand = new RelayCommand(() => SetSelection(r => r.Item.Status == LinkStatus.TargetMissing));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
        RemoveCommand = new RelayCommand(async () => await RemoveAsync(), () => !IsBusy);
        RestoreCommand = new RelayCommand(p => Restore(p as RemovalBatch));
        OpenDataFolderCommand = new RelayCommand(() => { Directory.CreateDirectory(_remover.DataFolder); OpenPath(_remover.DataFolder); });
        RefreshHistory();
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasScanned { get => _hasScanned; private set { if (SetProperty(ref _hasScanned, value)) OnPropertyChanged(nameof(ShowEmptyText)); } }
    public string EmptyText => "Nothing to clean up here: every shortcut in the chosen locations points to something that exists (or cannot be checked).";
    public bool ShowEmptyText => HasScanned && Rows.Count == 0;

    public bool ShowAll
    {
        get => _showAll;
        set { if (SetProperty(ref _showAll, value)) Rebuild(); }
    }

    public ObservableCollection<LocationOption> Locations { get; } = new();
    public ObservableCollection<LinkRow> Rows { get; } = new();
    public ObservableCollection<RemovalBatch> History { get; } = new();
    public bool HasHistory => History.Count > 0;

    public ICommand AddFolderCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand SelectDeadCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    /// <summary>"DeadLinks.exe &lt;folder&gt;": scan just that folder.</summary>
    public void ScanFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var l in Locations) l.IsChecked = false;
        var existing = Locations.FirstOrDefault(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) existing.IsChecked = true;
        else Locations.Add(new LocationOption { Name = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, Path = path, IsChecked = true });
        if (ScanCommand.CanExecute(null)) ScanCommand.Execute(null);
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder to scan for dead shortcuts and links" };
        if (dlg.ShowDialog() != true) return;
        if (Locations.Any(l => l.Path.Equals(dlg.FolderName, StringComparison.OrdinalIgnoreCase))) return;
        Locations.Add(new LocationOption { Name = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : dlg.FolderName, Path = dlg.FolderName, IsChecked = true });
    }

    private async Task ScanAsync()
    {
        var roots = Locations.Where(l => l.IsChecked).Select(l => l.Path).ToList();
        if (roots.Count == 0) { ShowBanner("Tick at least one location to scan.", "Warning"); return; }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        StatusText = "Scanning…";
        var progress = new Progress<int>(n => { _visited = n; StatusText = $"Scanning… {n:N0} items looked at"; });
        try
        {
            var found = await Task.Run(() => _scanner.Scan(roots, ct, progress), ct);
            _items.Clear();
            _items.AddRange(found);
            HasScanned = true;
            Rebuild();
            int dead = _items.Count(i => i.Status == LinkStatus.TargetMissing);
            StatusText = dead == 0 ? "No dead shortcuts" : $"{dead:N0} dead shortcut(s)";
            ShowBanner(dead == 0 ? "No dead shortcuts found." : $"Found {dead:N0} dead shortcut(s) or link(s). Review the list, then remove.", dead == 0 ? "Success" : "Warning");
        }
        catch (OperationCanceledException) { StatusText = "Cancelled"; }
        finally { IsBusy = false; }
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var i in _items.Where(i => ShowAll || i.Status is LinkStatus.TargetMissing or LinkStatus.Unavailable))
        {
            var row = new LinkRow(i);
            if (i.Status == LinkStatus.TargetMissing) row.IsSelected = true;     // dead links start ticked; offline drives never do
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LinkRow.IsSelected)) OnPropertyChanged(nameof(SelectedCount)); };
            Rows.Add(row);
        }
        int ok = _items.Count(i => i.Status == LinkStatus.Ok), dead = _items.Count(i => i.Status == LinkStatus.TargetMissing),
            off = _items.Count(i => i.Status == LinkStatus.Unavailable), unk = _items.Count(i => i.Status == LinkStatus.Unchecked);
        Summary = $"{_items.Count:N0} shortcuts and links checked: {dead:N0} dead, {off:N0} on offline drives, {ok:N0} fine, {unk:N0} not checkable.";
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ShowEmptyText));
        OnPropertyChanged(nameof(HasResults));
    }

    public bool HasResults => HasScanned && _items.Count > 0;
    public int SelectedCount => Rows.Count(r => r.IsSelected);

    private void SetSelection(Func<LinkRow, bool> pick)
    {
        foreach (var r in Rows) r.IsSelected = pick(r);
    }

    private async Task RemoveAsync()
    {
        var chosen = Rows.Where(r => r.IsSelected).Select(r => r.Item).ToList();
        if (chosen.Count == 0) { ShowBanner("Tick the shortcuts you want to remove.", "Warning"); return; }
        int offline = chosen.Count(c => c.Status == LinkStatus.Unavailable);
        string extra = offline > 0 ? $"\n\n{offline} of them point to a drive that is offline right now and may work again later." : string.Empty;
        var answer = MessageBox.Show(
            $"Remove {chosen.Count:N0} shortcut(s) and link(s)?\n\nShortcuts go to the Recycle Bin; a copy is kept so you can restore them from the History page.{extra}",
            "DeadLinks", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        IsBusy = true;
        try
        {
            var (batch, outcomes) = await Task.Run(() => _remover.Remove(chosen));
            var removed = outcomes.Where(o => o.Removed).Select(o => o.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _items.RemoveAll(i => removed.Contains(i.Path));
            Rebuild();
            RefreshHistory();
            int failed = outcomes.Count(o => !o.Removed);
            ShowBanner($"Removed {removed.Count:N0}" + (failed > 0 ? $"; {failed:N0} could not be removed ({outcomes.First(o => !o.Removed).Message})." : "."),
                       failed > 0 ? "Warning" : "Success");
            StatusText = $"Removed {removed.Count:N0}";
        }
        finally { IsBusy = false; }
    }

    private void Restore(RemovalBatch? batch)
    {
        if (batch == null) return;
        var outcomes = _remover.Restore(batch);
        int ok = outcomes.Count(o => o.Removed);
        int failed = outcomes.Count(o => !o.Removed && !o.Message.StartsWith("Already", StringComparison.Ordinal));
        RefreshHistory();
        ShowBanner($"Restored {ok:N0} item(s)" + (failed > 0 ? $"; {failed:N0} could not be restored ({outcomes.First(o => !o.Removed).Message})." : "."), failed > 0 ? "Warning" : "Success");
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var b in _remover.History()) History.Add(b);
        OnPropertyChanged(nameof(HasHistory));
    }
}
