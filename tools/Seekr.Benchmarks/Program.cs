using System.Diagnostics;
using System.Text.Json;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer;
using Seekr.Indexer.Service;

// Standalone, deterministic workload. Never scans user directories or talks to the service.
var responseOnly = args.Length == 2 && args[1] == "--response-only";
var output = args.Length is 1 or 2 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Specify output JSON path.");
var metrics = new List<object>();
string[] queryTexts = ["document", "ext:pdf", "no-such-file", "document-00000", "size:>100MB"];
var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
foreach (var count in new[] { 100_000, 500_000 })
{
    var entries = new FileEntry[count + 1];
    entries[0] = new(0, -1, @"C:\benchmark", true, 0, time, time);
    for (var i = 1; i < entries.Length; i++)
        entries[i] = new(i, 0, $"document-{i:D7}.{(i % 3 == 0 ? "pdf" : "txt")}", false, i * 1024L, time, time);
    if (responseOnly)
    {
        if (count != 500_000) continue;
        Measure("response.buffered.500000", () =>
        {
            using var response = new MemoryStream();
            IndexWire.Write(response, entries);
            Stream.Null.Write(response.GetBuffer().AsSpan(0, (int)response.Length));
        });
        Measure("response.streamed.500000", () => IndexWire.WriteAsync(Stream.Null, entries, CancellationToken.None).GetAwaiter().GetResult());
        continue;
    }
    Measure($"index.build.{count}", () => GC.KeepAlive(new NameIndex(entries)));
    var index = new NameIndex(entries);
    var queries = queryTexts.Select(SearchQuery.Parse).ToArray();
    foreach (var query in queries)
        Measure($"search.{count}.{Array.IndexOf(queries, query)}", () => GC.KeepAlive(index.Search(query, 500)));
    Measure($"wire.write.{count}", () => IndexWire.Write(Stream.Null, entries));
    Measure($"permissions.{count}", () => GC.KeepAlive(AccessFilter.Filter(entries, _ => true, 2)));
}

if (!responseOnly)
{
var root = Path.Combine(Path.GetTempPath(), "Seekr-benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    for (var i = 0; i < 50; i++)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, i.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
        for (var j = 0; j < 100; j++) File.WriteAllText(Path.Combine(folder, $"file-{j}.txt"), "benchmark");
    }
    Measure("scan.5000files.50folders", () => GC.KeepAlive(DirectoryScanner.Scan(root, CancellationToken.None)));
}
finally
{
    // root is the exact fixture created above, never a derived user path.
    Directory.Delete(root, recursive: true);
}
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    capturedUtc = DateTime.UtcNow, runtime = Environment.Version.ToString(), os = Environment.OSVersion.VersionString,
    processorCount = Environment.ProcessorCount, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    configuration = "Release; 1 warm-up + 5 measured iterations; warm OS cache; isolated benchmark process",
    queryOrder = queryTexts, metrics,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

void Measure(string name, Action action)
{
    action();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var timings = new double[5];
    var allocated = GC.GetTotalAllocatedBytes(precise: true);
    for (var i = 0; i < timings.Length; i++)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    var allocatedPerIteration = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / timings.Length;
    Array.Sort(timings);
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    metrics.Add(new { name, p50Ms = timings[2], p95Ms = timings[^1], allocatedBytesPerIteration = allocatedPerIteration,
        managedHeapBytes = GC.GetTotalMemory(false), workingSetBytes = process.WorkingSet64,
        privateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64 });
    Console.WriteLine($"{name}: p50={timings[2]:F2}ms p95={timings[^1]:F2}ms allocated={allocatedPerIteration:N0}B");
}
