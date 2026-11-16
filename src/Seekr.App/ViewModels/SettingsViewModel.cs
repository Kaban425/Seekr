using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;

namespace Seekr.App.ViewModels;

public sealed class AccentColorOption(string name, string hex)
{
    public string Name { get; } = name;

    public string Hex { get; } = hex;

    public override string ToString() => Name;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    public IReadOnlyList<string> Designs { get; } = ["Default", "Polar", "Velvet", "Rose", "Arcade", "Sakura", "Terminal", "Studio", "Lagoon", "Blueprint", "Paper", "Graphite"];
    public IReadOnlyList<string> Fonts { get; } = ["Segoe UI", "Calibri", "Arial", "Verdana", "Consolas", "Georgia", "Pixel"];
    public IReadOnlyList<string> Backgrounds { get; } = ["Solid", "Aurora", "Ocean", "Lavender", "Sand", "PhotoCoupe", "PhotoLake", "PhotoRally", "PhotoGarden", "PhotoSnow", "PhotoPolar", "PhotoVelvet", "PhotoRose", "PhotoArcade", "PhotoSakura", "PhotoBlueprint", "PhotoPaper", "Custom"];
    public IReadOnlyList<string> IconStyles { get; } = ["Pastel", "Outline", "Mono", "Pixel", "Anime", "Rune", "Tactical", "Neon", "Blueprint", "Paper", "Minimal", "Polar", "Velvet", "Rose", "Glass"];

    [ObservableProperty]
    private AppSettings _settings;

    [ObservableProperty]
    private IReadOnlyList<string> _themes = ["System", "Light", "Dark"];

    [ObservableProperty]
    private IReadOnlyList<AccentColorOption> _accentColors = [
        new("Системный", "System"),
        new("Красный", "#E81123"),
        new("Оранжевый", "#FF8C00"),
        new("Жёлтый", "#FFD400"),
        new("Зелёный", "#107C10"),
        new("Бирюзовый", "#00B7C3"),
        new("Синий", "#0078D4"),
        new("Индиго", "#5C2E91"),
        new("Фиолетовый", "#8764B8"),
        new("Розовый", "#E3008C"),
    ];

    [ObservableProperty]
    private IReadOnlyList<int> _searchLimits = [100, 250, 500, 1000, 2000];

    [ObservableProperty]
    private IReadOnlyList<int> _refreshIntervals = [5, 10, 30, 60, 300];

    [ObservableProperty]
    private AccentColorOption? _selectedAccentColor;

    [ObservableProperty]
    private string _version = BuildInfo.DisplayVersion;

    private readonly AppSettings _original;
    private readonly string? _settingsPath;
    private readonly System.Windows.Threading.DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool _publishing;
    [ObservableProperty] private string _saveStatus = "Изменения применяются автоматически";

    private void PreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleSave();
    private void ScheduleSave()
    {
        if (_publishing) return;
        _liveTimer.Stop();
        _liveTimer.Start();
    }
    partial void OnSettingsChanging(AppSettings value) => Settings.PropertyChanged -= PreferenceChanged;
    partial void OnSettingsChanged(AppSettings value)
    {
        value.PropertyChanged += PreferenceChanged;
        ScheduleSave();
    }
    partial void OnSelectedAccentColorChanged(AccentColorOption? value)
    {
        if (value is null || _publishing) return;
        Settings.AccentColor = value.Hex;
        Settings.CustomAccent = "";
        ScheduleSave();
    }
    public void FinishEditing()
    {
        if (_liveTimer.IsEnabled) Publish();
        _liveTimer.Stop();
        Settings.PropertyChanged -= PreferenceChanged;
    }
    private void Publish()
    {
        _liveTimer.Stop();
        if (!string.IsNullOrWhiteSpace(Settings.CustomAccent)
            && !System.Text.RegularExpressions.Regex.IsMatch(Settings.CustomAccent, "^#[0-9A-Fa-f]{6}$"))
        { SaveStatus = "Введите цвет #RRGGBB или очистите поле"; return; }
        _publishing = true;
        try
        {
            Settings.AccentColor = string.IsNullOrWhiteSpace(Settings.CustomAccent)
                ? SelectedAccentColor?.Hex ?? "System" : Settings.CustomAccent;
            var latest = SettingsService.Load(_settingsPath);
            Settings.SavedSearches = latest.SavedSearches;
            Settings.PinnedFolders = latest.PinnedFolders;
            Settings.HiddenNavigationPaths = latest.HiddenNavigationPaths;
            Settings.ColumnWidths = latest.ColumnWidths;
            Settings.ColumnOrder = latest.ColumnOrder;
            Settings.HiddenColumns = latest.HiddenColumns;
            Settings.SidebarWidth = latest.SidebarWidth;
            if (!SettingsService.Save(Settings, _settingsPath, notify: false))
            { SaveStatus = "Не удалось сохранить изменения"; return; }
            if (_settingsPath is null) { ApplySettings(); SettingsService.NotifyChanged(); }
            SaveStatus = "Все изменения сохранены";
        }
        finally { _publishing = false; }
    }

    public SettingsViewModel(AppSettings settings, string? settingsPath = null)
    {
        _settingsPath = settingsPath;
        _original = settings.Clone();
        _settings = settings.Clone();
        _settings.PropertyChanged += PreferenceChanged;
        _liveTimer.Tick += (_, _) => Publish();
        _selectedAccentColor = _accentColors.FirstOrDefault(c => c.Hex == settings.AccentColor)
            ?? _accentColors[0];
    }

    [RelayCommand]
    private void ApplyProfile(string? profile)
    {
        var settings = Settings.Clone();
        settings.CompactMode = profile == "Compact";
        settings.UiScale = profile == "Compact" ? 0.9 : 1.0;
        settings.ShowPreviewPane = profile == "Comfortable";
        settings.ShowFolderSizes = true;
        if (profile == "Cleanup") settings.SearchResultLimit = 1000;
        Settings = settings;
    }

    [RelayCommand]
    private void ChooseWallpaper()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp", Title = "Выбрать свои обои" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _ = WallpaperCatalog.Custom(dialog.FileName, true);
            Settings.CustomWallpaper = dialog.FileName;
            Settings.Background = "Custom";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
        { SaveStatus = "Не удалось открыть обои: " + ex.Message; }
    }
    [RelayCommand] private void RestoreNavigation()
    {
        Settings.HiddenNavigationPaths.Clear();
        SettingsService.Update(s => { s.HiddenNavigationPaths.Clear(); return null; }, _settingsPath);
        SaveStatus = "Стандартные папки возвращены";
    }

    [RelayCommand]
    private void ApplyDesign(string? name)
    {
        if (name is not ("Polar" or "Velvet" or "Rose" or "Arcade" or "Sakura" or "Terminal" or "Studio" or "Lagoon" or "Blueprint" or "Paper" or "Graphite")) return;
        var settings = Settings.Clone(); settings.Design = name; settings.IconStyle = name switch { "Graphite"=>"Glass", "Blueprint" => "Blueprint", "Paper" => "Paper", "Arcade" => "Pixel", "Sakura" => "Anime", "Terminal" => "Blueprint", "Studio" => "Minimal", "Lagoon" => "Pastel", _ => name };
        settings.Theme = name == "Paper" ? "Light" : "Dark"; settings.Background = name switch { "Graphite"=>"Solid", "Terminal" => "Solid", "Studio" => "Solid", "Lagoon" => "PhotoLake", _ => "Photo" + name };
        settings.Font = name == "Arcade" ? "Pixel" : name is "Velvet" or "Terminal" or "Blueprint" ? "Consolas" : name == "Paper" ? "Georgia" : "Segoe UI";
        if (name == "Arcade") settings.UiScale = 1;
        settings.CustomAccent = name switch { "Graphite"=>"#A1D9F2", "Blueprint" => "#91CDE9", "Paper" => "#8B633D", "Terminal" => "#91CDA5", "Studio" => "#E9A36B", "Lagoon" => "#8FC9C6", "Polar" => "#88C0D0", "Velvet" => "#BD93F9", "Arcade" => "#E3BA66", "Sakura" => "#BC9EB8", _ => "#F5C2E7" };
        Settings = settings;
    }

    [RelayCommand]
    private void ApplyArtProfile(string? profile)
    {
        var settings = Settings.Clone();
        settings.Design = "Default"; settings.IconStyle = profile ?? "Pastel";
        settings.Font = profile switch { "Pixel" => "Consolas", "Anime" => "Calibri", "Rune" => "Verdana", _ => "Segoe UI" };
        settings.Theme = profile == "Anime" ? "Light" : "Dark";
        settings.Background = profile switch { "Anime" => "PhotoGarden", "Rune" => "PhotoSnow", "Tactical" => "PhotoRally", _ => "Solid" };
        settings.CustomAccent = profile switch { "Pixel" => "#D9B844", "Anime" => "#B84486", "Rune" => "#B88C46", _ => "#6F9778" };
        Settings = settings;
    }

    [RelayCommand]
    private void Save(Window? window)
    {
        Publish();
        window?.Close();
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Command must be instance-bound.")]
    private void Cancel(Window? window)
    {
        window?.Close();
    }

    [RelayCommand]
    private void Revert()
    {
        Settings = _original.Clone();
        _publishing = true;
        try { SelectedAccentColor = AccentColors.FirstOrDefault(c => c.Hex == Settings.AccentColor) ?? AccentColors[0]; }
        finally { _publishing = false; }
        ScheduleSave();
    }

    [RelayCommand]
    private void Reset()
    {
        Settings = new AppSettings();
        SelectedAccentColor = AccentColors[0];
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Command is instance-bound by MVVM bindings.")]
    [RelayCommand] private void ReportIssue() => new FeedbackWindow { Owner = Application.Current.MainWindow }.ShowDialog();
    [RelayCommand] private void OpenDiagnostics()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DiagnosticService.DirectoryPath);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiagnosticService.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { SaveStatus = "Папка диагностики недоступна."; }
    }

    private static void ApplySettings()
    {
        ThemeService.ApplyCurrentTheme();
        AccentColorService.ApplyCurrentAccentColor();
        ScaleService.ApplyCurrentScale();
        AppearanceService.Apply(SettingsService.Load());
    }
}




