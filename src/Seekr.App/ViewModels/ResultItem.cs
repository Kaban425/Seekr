using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.IO;
using Seekr.App.Utils;
using Seekr.Core.Index;

namespace Seekr.App.ViewModels;

/// <summary>One row in the results list, formatted for the Explorer-style grid.</summary>
public sealed partial class ResultItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Name { get; }
    public string Folder { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string Extension { get; }
    public string ModifiedString { get; }
    public string CreatedString { get; }
    public DateTime ModifiedUtc { get; }
    public DateTime CreatedUtc { get; }
    public string TypeString { get; }
    public bool IsDrive { get; }
    public long? FreeBytes { get; }
    public string FreeSpaceString => FreeBytes is { } bytes ? FormatBytes(bytes) : string.Empty;
    public long? UsedBytes { get; }
    public string MapSize => IsDrive && UsedBytes is { } used
        ? $"Занято {FormatBytes(used)} из {FormatBytes(EffectiveSize)} · Доступно {FreeSpaceString}"
        : SizeString;
    private readonly bool _showFolderSizes;
    public string SizeString => IsDirectory && !IsDrive && !_showFolderSizes ? string.Empty : EffectiveSize < 0 ? IsDrive ? "—" : "Считается…" : (IsSizePartial ? "≥ " : string.Empty) + FormatBytes(EffectiveSize);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeString), nameof(MapSize))]
    private long _effectiveSize;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeString), nameof(MapSize))]
    private bool _isSizePartial;
    [ObservableProperty] private string _sizeStatus = "Размер из индекса";
    [ObservableProperty] private double _mapPercent;

    public bool HasMeasuredSize { get; private set; }

    public void ApplyMeasurement(Services.FolderMeasurement measurement)
    {
        HasMeasuredSize = true;
        IsSizePartial = measurement.Skipped > 0;
        EffectiveSize = measurement.Bytes;
        SizeStatus = measurement.Description;
    }

    public ResultItem(SearchResult result, bool showFolderSizes = true, Services.DriveSpace? driveSpace = null)
    {
        Name = result.Entry.Name;
        Folder = Path.GetDirectoryName(result.FullPath) ?? string.Empty;
        FullPath = result.FullPath;
        IsDirectory = result.Entry.IsDirectory;
        IsDrive = IsDirectory && string.Equals(Path.GetPathRoot(FullPath)?.TrimEnd('\\'), FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        FreeBytes = driveSpace?.Free;
        Extension = result.Entry.Extension;
        ModifiedString = FormatDate(result.Entry.ModifiedUtc);
        CreatedString = FormatDate(result.Entry.CreatedUtc);
        ModifiedUtc = result.Entry.ModifiedUtc;
        CreatedUtc = result.Entry.CreatedUtc;
        TypeString = IsDrive ? "Диск" + (driveSpace is null ? "" : " · " + driveSpace.Format) : FilePresentation.GetTypeName(IsDirectory, Extension);
        EffectiveSize = IsDrive ? driveSpace?.Total ?? -1 : result.EffectiveSize;
        if (driveSpace is { Total: > 0 } && IsDrive)
            UsedBytes = driveSpace.Total - Math.Clamp(driveSpace.TotalFree, 0, driveSpace.Total);
        _showFolderSizes = showFolderSizes;
        IsSizePartial = IsDirectory && !IsDrive && result.EffectiveSize >= 0;
        HasMeasuredSize = driveSpace is not null;
        SizeStatus = IsDrive ? driveSpace is null ? "Сведения о ёмкости диска недоступны" : "Полная ёмкость диска; доступное место — в столбце «Свободно»" : IsDirectory ? result.EffectiveSize >= 0 ? "Оценка по индексу; уточняем размер" : "Ожидает подсчёта" : "Размер файла на момент поиска";
    }

    public static ResultItem From(SearchResult result) => new(result);

    private static string FormatDate(DateTime utc) =>
        utc == DateTime.MinValue ? string.Empty : utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string FormatBytes(long bytes)
    {
        const long Kb = 1024;
        const long Mb = Kb * 1024;
        const long Gb = Mb * 1024;
        const long Tb = Gb * 1024;

        return bytes switch
        {
            < 0 => string.Empty,
            < Kb => $"{bytes} B",
            < Mb => $"{bytes / (double)Kb:0.0} KB",
            < Gb => $"{bytes / (double)Mb:0.0} MB",
            < Tb => $"{bytes / (double)Gb:0.00} GB",
            _ => $"{bytes / (double)Tb:0.00} TB"
        };
    }
}

