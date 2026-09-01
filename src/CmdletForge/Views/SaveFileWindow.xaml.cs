using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CmdletForge.Services;
using CmdletForge.Theming;

namespace CmdletForge.Views;

public partial class SaveFileWindow : Window
{
    private readonly ObservableCollection<FileEntry> _entries = [];
    private readonly ThemeService _themeService;
    private string _currentDirectory;
    private string? _pendingPath;

    public SaveFileWindow(ThemeService themeService, string initialFileName, string? documentPath)
    {
        InitializeComponent();
        _themeService = themeService;
        _currentDirectory = InitialDirectory(documentPath);
        EntriesList.ItemsSource = _entries;
        FileTypeCombo.ItemsSource = FileTypeOption.All;
        FileTypeCombo.SelectedIndex = FileTypeOption.IndexForFileName(initialFileName);
        FileNameBox.Text = initialFileName;

        SourceInitialized += (_, _) => _themeService.Apply(_themeService.CurrentTheme, _themeService.CurrentPalette, this);
        Loaded += (_, _) =>
        {
            RefreshDirectory();
            FileNameBox.Focus();
            FileNameBox.SelectAll();
        };
    }

    public string? SelectedPath { get; private set; }

    private static string InitialDirectory(string? documentPath)
    {
        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            var documentDirectory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
            if (!string.IsNullOrWhiteSpace(documentDirectory) && Directory.Exists(documentDirectory))
                return documentDirectory;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Environment.CurrentDirectory;
    }

    private FileTypeOption SelectedFileType => FileTypeCombo.SelectedItem as FileTypeOption ?? FileTypeOption.All[0];

    private void RefreshDirectory()
    {
        try
        {
            _currentDirectory = Path.GetFullPath(_currentDirectory);
            DirectoryBox.Text = _currentDirectory;
            _entries.Clear();

            foreach (var directory in Directory.EnumerateDirectories(_currentDirectory)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    _entries.Add(FileEntry.Directory(info));
                }
                catch (UnauthorizedAccessException) { }
            }

            var extension = SelectedFileType.Extension;
            foreach (var file in Directory.EnumerateFiles(_currentDirectory)
                         .Where(path => extension is null || string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    _entries.Add(FileEntry.File(new FileInfo(file)));
                }
                catch (UnauthorizedAccessException) { }
            }

            EmptyPanel.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus(ex.Message);
        }
    }

    private void NavigateTo(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            if (!Directory.Exists(fullPath))
            {
                ShowStatus("Deze map bestaat niet of is niet bereikbaar.");
                return;
            }

            _currentDirectory = fullPath;
            RefreshDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus(ex.Message);
        }
    }

    private void TrySave()
    {
        ClearOverwriteState();
        var resolution = SavePathService.Resolve(_currentDirectory, FileNameBox.Text, SelectedFileType.Extension);
        if (!resolution.IsValid || resolution.Path is null)
        {
            ShowStatus(resolution.Error ?? "Het opslagpad is ongeldig.");
            FileNameBox.Focus();
            return;
        }

        var fullPath = resolution.Path;
        var fileName = Path.GetFileName(fullPath);
        FileNameBox.Text = fileName;
        if (Directory.Exists(fullPath))
        {
            NavigateTo(fullPath);
            return;
        }

        if (File.Exists(fullPath))
        {
            _pendingPath = fullPath;
            OverwriteText.Text = $"{fileName} bestaat al.";
            SaveButtons.Visibility = Visibility.Collapsed;
            NewFolderPanel.Visibility = Visibility.Collapsed;
            OverwriteButtons.Visibility = Visibility.Visible;
            return;
        }

        CompleteSave(fullPath);
    }

    private void CompleteSave(string path)
    {
        SelectedPath = Path.GetFullPath(path);
        DialogResult = true;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void ClearOverwriteState()
    {
        _pendingPath = null;
        OverwriteButtons.Visibility = Visibility.Collapsed;
        if (NewFolderPanel.Visibility != Visibility.Visible)
            SaveButtons.Visibility = Visibility.Visible;
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDirectory);
        if (parent is not null)
            NavigateTo(parent.FullName);
    }

    private void Go_Click(object sender, RoutedEventArgs e) => NavigateTo(DirectoryBox.Text);
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDirectory();
    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is FileEntry { IsDirectory: false } entry)
            FileNameBox.Text = entry.Name;
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(EntriesList, source) is not ListViewItem { DataContext: FileEntry entry })
            return;

        if (entry.IsDirectory)
            NavigateTo(entry.Path);
        else
            TrySave();
    }

    private void FileTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshDirectory();
    }

    private void DirectoryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        NavigateTo(DirectoryBox.Text);
        e.Handled = true;
    }

    private void FileNameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        TrySave();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && OverwriteButtons.Visibility == Visibility.Visible)
        {
            CancelOverwrite_Click(sender, e);
            e.Handled = true;
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        ClearOverwriteState();
        SaveButtons.Visibility = Visibility.Collapsed;
        NewFolderPanel.Visibility = Visibility.Visible;
        NewFolderBox.Text = string.Empty;
        NewFolderBox.Focus();
    }

    private void CancelNewFolder_Click(object sender, RoutedEventArgs e)
    {
        NewFolderPanel.Visibility = Visibility.Collapsed;
        SaveButtons.Visibility = Visibility.Visible;
        ClearStatus();
    }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = NewFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowStatus("Vul een geldige mapnaam in.");
            return;
        }

        try
        {
            var path = Path.Combine(_currentDirectory, name);
            if (Directory.Exists(path))
            {
                ShowStatus("Deze map bestaat al.");
                return;
            }

            Directory.CreateDirectory(path);
            NewFolderPanel.Visibility = Visibility.Collapsed;
            SaveButtons.Visibility = Visibility.Visible;
            NavigateTo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus(ex.Message);
        }
    }

    private void NewFolderBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CreateFolder_Click(sender, e);
        e.Handled = true;
    }

    private void CancelOverwrite_Click(object sender, RoutedEventArgs e)
    {
        ClearOverwriteState();
        FileNameBox.Focus();
        FileNameBox.SelectAll();
    }

    private void ConfirmOverwrite_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingPath is { } path)
            CompleteSave(path);
    }

    private sealed record FileTypeOption(string Label, string? Extension)
    {
        public static IReadOnlyList<FileTypeOption> All { get; } =
        [
            new("PowerShell-script (*.ps1)", ".ps1"),
            new("PowerShell-module (*.psm1)", ".psm1"),
            new("PowerShell-data (*.psd1)", ".psd1"),
            new("Alle bestanden (*.*)", null)
        ];

        public static int IndexForFileName(string name)
        {
            var extension = Path.GetExtension(name);
            for (var index = 0; index < All.Count; index++)
            {
                if (string.Equals(All[index].Extension, extension, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return 0;
        }

        public override string ToString() => Label;
    }

    private sealed record FileEntry(string Name, string Path, string Type, string Modified, string Size, bool IsDirectory)
    {
        public static FileEntry Directory(DirectoryInfo info) => new(
            info.Name, info.FullName, "Map", info.LastWriteTime.ToString("g", CultureInfo.CurrentCulture), string.Empty, true);

        public static FileEntry File(FileInfo info) => new(
            info.Name, info.FullName, string.IsNullOrWhiteSpace(info.Extension) ? "Bestand" : $"{info.Extension.TrimStart('.').ToUpperInvariant()}-bestand",
            info.LastWriteTime.ToString("g", CultureInfo.CurrentCulture), FormatSize(info.Length), false);

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024d:0.#} KB";
            return $"{bytes / (1024d * 1024d):0.#} MB";
        }
    }
}
