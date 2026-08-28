using System.Windows;
using CmdletForge.Models;
using CmdletForge.Services;
using CmdletForge.Theming;

namespace CmdletForge.Views;

public partial class UpdateWindow : Window
{
    private readonly string _powerShellExecutable;
    private readonly ThemeService _themeService;
    private readonly AppUpdateService _appUpdateService = new();
    private UpdateInfo? _updateInfo;
    private StagedUpdate? _stagedUpdate;

    public UpdateWindow(string powerShellExecutable, ThemeService themeService)
    {
        InitializeComponent();
        _powerShellExecutable = powerShellExecutable;
        _themeService = themeService;
        SourceInitialized += (_, _) => _themeService.Apply(_themeService.CurrentTheme, _themeService.CurrentPalette, this);
        Loaded += UpdateWindow_Loaded;
        Closed += (_, _) => _appUpdateService.Dispose();
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_appUpdateService.IsConfigured)
        {
            AppStatus.Text = $"Versie {AppInfo.Version.ToString(3)} · updatebron niet geconfigureerd in deze lokale build.";
            CheckAppButton.IsEnabled = false;
        }
        try
        {
            var version = await SystemUpdateService.GetPowerShellVersionAsync(_powerShellExecutable);
            PowerShellUpdateStatus.Text = $"Actieve versie: {version}";
        }
        catch (Exception ex)
        {
            PowerShellUpdateStatus.Text = ex.Message;
        }
    }

    private async void CheckApp_Click(object sender, RoutedEventArgs e)
    {
        CheckAppButton.IsEnabled = false;
        AppStatus.Text = "GitHub-release controleren...";
        try
        {
            _updateInfo = await _appUpdateService.CheckAsync();
            if (_updateInfo.IsUpdateAvailable)
            {
                AppStatus.Text = $"Update beschikbaar: {_updateInfo.LatestVersion} (huidig {AppInfo.Version.ToString(3)}).";
                DownloadAppButton.IsEnabled = true;
            }
            else
            {
                AppStatus.Text = $"Je gebruikt de nieuwste versie ({AppInfo.Version.ToString(3)}).";
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("App-updatecontrole is mislukt.", ex);
            AppStatus.Text = ex.Message;
        }
        finally
        {
            CheckAppButton.IsEnabled = true;
        }
    }

    private async void DownloadApp_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInfo is null)
            return;
        DownloadAppButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value => DownloadProgress.Value = value * 100);
        try
        {
            _stagedUpdate = await _appUpdateService.DownloadAndVerifyAsync(_updateInfo, progress);
            AppStatus.Text = $"Download geverifieerd: SHA-256 {_stagedUpdate.Sha256[..12]}…";
            ApplyAppButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("App-update kon niet worden gedownload of geverifieerd.", ex);
            AppStatus.Text = ex.Message;
            DownloadAppButton.IsEnabled = true;
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyApp_Click(object sender, RoutedEventArgs e)
    {
        if (_stagedUpdate is null || Owner is not MainWindow mainWindow)
            return;
        mainWindow.ApplyUpdateAndExit(_stagedUpdate);
    }

    private void UpdatePowerShell_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "PowerShell bijwerken via winget? Winget opent een apart proces en kan om UAC-toestemming vragen.",
                "PowerShell bijwerken",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            SystemUpdateService.StartPowerShellUpdate();
            PowerShellUpdateStatus.Text = "Winget-update gestart. Herstart de terminal na voltooiing.";
        }
        catch (Exception ex)
        {
            AppLog.Error("PowerShell-update kon niet worden gestart.", ex);
            MessageBox.Show(this, ex.Message, "Update mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
