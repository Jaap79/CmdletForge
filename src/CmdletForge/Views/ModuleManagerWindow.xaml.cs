using System.Windows;
using CmdletForge.Models;
using CmdletForge.Services;
using CmdletForge.Theming;

namespace CmdletForge.Views;

public partial class ModuleManagerWindow : Window
{
    private readonly ModuleService _moduleService;
    private readonly ThemeService _themeService;

    public ModuleManagerWindow(ModuleService moduleService, ThemeService themeService)
    {
        InitializeComponent();
        _moduleService = moduleService;
        _themeService = themeService;
        SourceInitialized += (_, _) => _themeService.Apply(_themeService.CurrentTheme, _themeService.CurrentPalette, this);
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "Geïnstalleerde en beschikbare versies ophalen...");
        try
        {
            ModuleList.ItemsSource = await _moduleService.GetModulesAsync();
            StatusText.Text = "Gereed. Selecteer één module voor installatie of update.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Moduleoverzicht kon niet worden geladen.", ex);
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Moduleoverzicht mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var name = SelectedName();
        if (name is null)
            return;
        if (MessageBox.Show(this,
                $"Module '{name}' voor de huidige Windows-gebruiker installeren vanuit PSGallery?",
                "Module installeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await RunActionAsync(name, install: true);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var name = SelectedName();
        if (name is null)
            return;
        if (MessageBox.Show(this,
                $"Module '{name}' bijwerken voor de huidige Windows-gebruiker? Afhankelijkheden kunnen eveneens wijzigen.",
                "Module bijwerken",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunActionAsync(name, install: false);
    }

    private async Task RunActionAsync(string name, bool install)
    {
        SetBusy(true, install ? $"{name} installeren..." : $"{name} bijwerken...");
        try
        {
            if (install)
                await _moduleService.InstallAsync(name);
            else
                await _moduleService.UpdateAsync(name);
            StatusText.Text = install ? $"{name} is geïnstalleerd." : $"{name} is bijgewerkt.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Moduleactie mislukt voor {name}.", ex);
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Moduleactie mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private string? SelectedName()
    {
        var name = ModuleList.SelectedItem is ModuleInfo info ? info.Name : CustomModuleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Selecteer een module of vul een exacte modulenaam in.", "Modules", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        return name;
    }

    private void UseCustom_Click(object sender, RoutedEventArgs e)
    {
        ModuleList.SelectedItem = null;
        InstallButton.Focus();
    }

    private void SetBusy(bool busy, string status)
    {
        InstallButton.IsEnabled = !busy;
        UpdateButton.IsEnabled = !busy;
        CustomModuleBox.IsEnabled = !busy;
        StatusText.Text = status;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
}
