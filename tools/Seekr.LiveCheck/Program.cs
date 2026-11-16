using System.Diagnostics;
using System.Security.Principal;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

// Opt-in integration check: creates only its own temporary fixture. No service installation,
// journal creation or journal deletion. --native requires elevation; default uses the service.
var native = args.Contains("--native", StringComparer.Ordinal);
var outputIndex = Array.IndexOf(args, "--output");
using var log = outputIndex >= 0 ? new StreamWriter(args[outputIndex + 1]) { AutoFlush = true } : null;
var key = "seekr-live-check-" + Guid.NewGuid().ToString("N");
var root = Path.Combine(Path.GetTempPath(), key);
var firstFolder = Path.Combine(root, "before");
var secondFolder = Path.Combine(root, "after");
var firstFile = Path.Combine(firstFolder, key + ".txt");
var secondFile = Path.Combine(secondFolder, key + ".txt");
var letter = char.ToUpperInvariant(Path.GetPathRoot(root)![0]);
var timer = Stopwatch.StartNew();

try
{
    using var identity = WindowsIdentity.GetCurrent();
    Write($"mode={(native ? "native" : "service")} elevated={new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)}");
    Directory.CreateDirectory(firstFolder);
    var baseline = Probe(default);
    Require(baseline.Kind == JournalChangeKind.Baseline, "journal baseline available");

    File.WriteAllText(firstFile, "Seekr integration fixture");
    var created = Probe(baseline.Checkpoint);
    Require(created.Kind == JournalChangeKind.Changed, "creation detected");
    Require(Find().Contains(firstFile, StringComparer.OrdinalIgnoreCase), "created file searchable");

    var beforeRename = Probe(default);
    Directory.Move(firstFolder, secondFolder);
    Require(Probe(beforeRename.Checkpoint).Kind == JournalChangeKind.Changed, "directory rename detected");
    var renamed = Find();
    Require(renamed.Contains(secondFile, StringComparer.OrdinalIgnoreCase)
        && !renamed.Contains(firstFile, StringComparer.OrdinalIgnoreCase), "child path follows renamed directory");

    var beforeDelete = Probe(default);
    File.Delete(secondFile);
    Require(Probe(beforeDelete.Checkpoint).Kind == JournalChangeKind.Changed, "deletion detected");
    Require(!Find().Any(path => path.EndsWith(key + ".txt", StringComparison.OrdinalIgnoreCase)), "deleted file removed from search");
    Write($"PASS in {timer.Elapsed.TotalSeconds:F1}s");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
    or System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
{
    Write($"FAIL: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
finally
{
    // Known fixture paths only; no recursive deletion and no traversal of user directories.
    if (File.Exists(firstFile)) File.Delete(firstFile);
    if (File.Exists(secondFile)) File.Delete(secondFile);
    if (Directory.Exists(firstFolder)) Directory.Delete(firstFolder);
    if (Directory.Exists(secondFolder)) Directory.Delete(secondFolder);
    if (Directory.Exists(root)) Directory.Delete(root);
}

JournalProbe Probe(JournalCheckpoint checkpoint) => native
    ? MftVolumeReader.ProbeJournal(letter, checkpoint, CancellationToken.None)
    : IndexPipe.TryProbe(letter, checkpoint, CancellationToken.None)
        ?? throw new InvalidOperationException("SeekrIndex is not running. Update it before this check.");

string[] Find()
{
    var entries = native ? MftVolumeReader.ReadVolume(letter, CancellationToken.None)
        : IndexPipe.TryLoad(letter, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60))
            ?? throw new InvalidOperationException("SeekrIndex unavailable.");
    return new NameIndex(entries).Search(SearchQuery.Parse(key), 50).Select(result => result.FullPath).ToArray();
}

void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Write("PASS: " + label);
}

void Write(string text)
{
    Console.WriteLine(text);
    log?.WriteLine(text);
}
