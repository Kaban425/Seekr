namespace Seekr.App.Services;

public sealed class AppSettings : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string _customWallpaper = "";
    public string CustomWallpaper { get => _customWallpaper; set => SetProperty(ref _customWallpaper, value); }
    public List<string> HiddenNavigationPaths { get; set; } = [];
    private string _design = "Default";
    public string Design { get => _design; set => SetProperty(ref _design, value); }
    private string _font = "Segoe UI";
    public string Font { get => _font; set => SetProperty(ref _font, value); }
    private string _background = "Solid";
    public string Background { get => _background; set => SetProperty(ref _background, value); }
    private string _iconStyle = "Pastel";
    public string IconStyle { get => _iconStyle; set => SetProperty(ref _iconStyle, value); }
    private string _customAccent = "";
    public string CustomAccent { get => _customAccent; set => SetProperty(ref _customAccent, value); }
    private string _theme = "System";
    public string Theme { get => _theme; set => SetProperty(ref _theme, value); }

    private string _accentColor = "System";
    public string AccentColor { get => _accentColor; set => SetProperty(ref _accentColor, value); }

    private double _uiScale = 1.0;
    public double UiScale { get => _uiScale; set => SetProperty(ref _uiScale, value); }

    private double _sidebarWidth = 260;
    public double SidebarWidth { get => _sidebarWidth; set => SetProperty(ref _sidebarWidth, value); }

    private bool _showPreviewPane;
    public bool ShowPreviewPane { get => _showPreviewPane; set => SetProperty(ref _showPreviewPane, value); }

    private bool _compactMode;
    public bool CompactMode { get => _compactMode; set => SetProperty(ref _compactMode, value); }
    private bool _alwaysShowCheckboxes;
    public bool AlwaysShowCheckboxes { get => _alwaysShowCheckboxes; set => SetProperty(ref _alwaysShowCheckboxes, value); }
    private bool _showSizeBars;
    public bool ShowSizeBars { get => _showSizeBars; set => SetProperty(ref _showSizeBars, value); }

    private bool _showHiddenFiles;
    public bool ShowHiddenFiles { get => _showHiddenFiles; set => SetProperty(ref _showHiddenFiles, value); }

    private bool _showSystemFiles;
    public bool ShowSystemFiles { get => _showSystemFiles; set => SetProperty(ref _showSystemFiles, value); }

    private bool _showFolderSizes = true;
    public bool ShowFolderSizes { get => _showFolderSizes; set => SetProperty(ref _showFolderSizes, value); }

    private bool _openWithSingleClick;
    public bool OpenWithSingleClick { get => _openWithSingleClick; set => SetProperty(ref _openWithSingleClick, value); }

    private int _searchResultLimit = 500;
    public int SearchResultLimit { get => _searchResultLimit; set => SetProperty(ref _searchResultLimit, value); }

    private bool _autoRefreshIndex = true;
    public bool AutoRefreshIndex { get => _autoRefreshIndex; set => SetProperty(ref _autoRefreshIndex, value); }

    private int _autoRefreshIntervalSeconds = 10;
    public int AutoRefreshIntervalSeconds { get => _autoRefreshIntervalSeconds; set => SetProperty(ref _autoRefreshIntervalSeconds, value); }

    public Dictionary<string, double> ColumnWidths { get; set; } = [];
    public List<string> HiddenColumns { get; set; } = ["Дата создания"];
    public List<string> ColumnOrder { get; set; } = [];

    public List<SavedSearch> SavedSearches { get; set; } = [];

    public List<string> PinnedFolders { get; set; } = [];

    public AppSettings Clone() => new()
    {
        CustomWallpaper = CustomWallpaper, HiddenNavigationPaths = [.. HiddenNavigationPaths], Design = Design, Font = Font, Background = Background, IconStyle = IconStyle, CustomAccent = CustomAccent,
        Theme = Theme,
        AccentColor = AccentColor,
        UiScale = UiScale,
        SidebarWidth = SidebarWidth,
        ShowPreviewPane = ShowPreviewPane,
        CompactMode = CompactMode,
        AlwaysShowCheckboxes = AlwaysShowCheckboxes,
        ShowSizeBars = ShowSizeBars,
        ShowHiddenFiles = ShowHiddenFiles,
        ShowSystemFiles = ShowSystemFiles,
        ShowFolderSizes = ShowFolderSizes,
        OpenWithSingleClick = OpenWithSingleClick,
        SearchResultLimit = SearchResultLimit,
        AutoRefreshIndex = AutoRefreshIndex,
        AutoRefreshIntervalSeconds = AutoRefreshIntervalSeconds,
        PinnedFolders = [.. PinnedFolders],
        SavedSearches = [.. SavedSearches],
        ColumnWidths = new(ColumnWidths),
        HiddenColumns = [.. HiddenColumns],
        ColumnOrder = [.. ColumnOrder],
    };
}
