using System.ComponentModel;
using Seekr.Core.Index;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

namespace Seekr.Indexer;

public enum IndexSource { Service, AllDrives, UserProfile, AccessibleDrives }
public sealed record VolumeIndexStatus(string Path, string Mode, int Entries, string? Error, DateTime UpdatedAt, bool RequiresServiceUpdate = false);
public sealed record LoadedIndex(NameIndex Index, IndexSource Source, TimeSpan Elapsed,
    IReadOnlyList<VolumeIndexStatus>? Volumes = null);

public static class IndexLoader
{
    private static readonly TimeSpan ServiceConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ServiceResponseTimeout = TimeSpan.FromSeconds(60);

    public static LoadedIndex Load(CancellationToken cancellationToken, IProgress<VolumeIndexStatus>? progress = null)
    {
        var started = DateTime.UtcNow;
        var volumes = new List<IReadOnlyList<FileEntry>>();
        var status = new List<VolumeIndexStatus>();
        void Report(VolumeIndexStatus entry) { status.Add(entry); progress?.Report(entry); }
        var allService = true;
        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = drive.Name;
            progress?.Report(new(path, "Чтение…", 0, null, DateTime.Now));
            string? failure = null;
            var requiresServiceUpdate = false;
            try
            {
                if (!drive.IsReady) { Report(new(path, "Не готов", 0, "Носитель не подключён или заблокирован", DateTime.Now)); continue; }
                IReadOnlyList<FileEntry>? entries = null;
                var mode = "Обход доступных папок";
                if (drive.DriveType == DriveType.Fixed && drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        entries = IndexPipe.TryLoad(path[0], ServiceConnectTimeout, ServiceResponseTimeout, cancellationToken);
                        if (entries is not null) mode = "Служба SeekrIndex";
                        else failure = "Служба недоступна";
                    }
                    catch (IndexServiceCompatibilityException ex)
                    {
                        requiresServiceUpdate = true; failure = ex.Message;
                        progress?.Report(new(path, "Требуется обновление службы", 0, failure, DateTime.Now, true));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or AggregateException)
                    { failure = "Служба: " + ex.GetBaseException().Message; }
                    if (entries is null)
                    {
                        try { entries = MftVolumeReader.ReadVolume(path[0], cancellationToken); mode = "Прямое чтение NTFS"; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                        { failure += "; NTFS: " + ex.Message; }
                    }
                }
                if (entries is null)
                {
                    allService = false;
                    entries = DirectoryScanner.Scan(path, cancellationToken);
                }
                else allService &= mode == "Служба SeekrIndex";
                volumes.Add(entries);
                Report(new(path, mode, entries.Count, failure, DateTime.Now, requiresServiceUpdate));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            { allService = false; Report(new(path, "Недоступен", 0, ex.Message, DateTime.Now, requiresServiceUpdate)); }
        }
        return new(new NameIndex(EntryMerger.Concat(volumes), cancellationToken), allService ? IndexSource.Service : IndexSource.AccessibleDrives,
            DateTime.UtcNow - started, status);
    }

    public static IEnumerable<char> FixedNtfsDrives() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
        .Select(d => d.Name[0]);
}
