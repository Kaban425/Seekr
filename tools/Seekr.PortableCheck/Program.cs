using Seekr.Core.Operations;
using Seekr.Core.Search;

var folder = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "seekr-portable-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
try
{
    var a = Path.Combine(folder, "a.txt"); var b = Path.Combine(folder, "b.txt"); var c = Path.Combine(folder, "c.txt");
    await File.WriteAllTextAsync(a, "same"); await File.WriteAllTextAsync(b, "same"); await File.WriteAllTextAsync(c, "else");
    var duplicates = FolderAnalysis.Scan(folder, true);
    Check(duplicates.Items.Count == 2 && duplicates.Items.All(x => x.Path != c), "Duplicates compare content, including equal-size different files");
    Check(duplicates.Items.Select(x => x.Group).Distinct().Count() == 1, "Duplicate copies share a group");
    FolderAnalysis.ValidateDuplicateSelection(duplicates.Items, new HashSet<string> { a });
    Check(true, "Duplicate deletion revalidates the selected copy against a retained copy");
    await File.WriteAllTextAsync(a, "edit");
    try { FolderAnalysis.ValidateDuplicateSelection(duplicates.Items, new HashSet<string> { a }); throw new InvalidOperationException("Changed duplicate accepted"); }
    catch (IOException) { Check(true, "Changed duplicates cannot be deleted from a stale scan"); }
    await File.WriteAllTextAsync(a, "same");
    var nested = Path.Combine(folder, "empty"); Directory.CreateDirectory(nested);
    var old = Path.Combine(folder, "old.msi"); await File.WriteAllTextAsync(old, "installer"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-100));
    var recent = Path.Combine(folder, "new.msi"); await File.WriteAllTextAsync(recent, "installer");
    var clean = FolderAnalysis.Scan(folder, false);
    Check(clean.Items.Any(x => x.Path == nested) && clean.Items.Any(x => x.Path == old) && clean.Items.All(x => x.Path != recent), "Cleanup includes empty folders and old installers, leaves recent ones");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { FolderAnalysis.Scan(folder, true, token: cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "Scan can be cancelled"); }
    var destination = Path.Combine(folder, "destination"); Directory.CreateDirectory(destination);
    var transfer = await FileTransfer.ExecuteAsync([a], destination, true, _ => Task.FromResult(ConflictChoice.Skip));
    Check(transfer.Failed.Count == 0 && transfer.UndoMoves.Count == 1, "Move records its exact destination for undo");
    transfer.UndoMoves[0].Restore();
    Check(File.Exists(a) && !File.Exists(Path.Combine(destination, "a.txt")), "Undo returns the moved file");
    var renamed = Path.Combine(folder, "renamed.txt"); FileTransfer.Rename(a, "renamed.txt"); var undo = MoveUndo.Capture(a, renamed);
    await File.WriteAllTextAsync(renamed, "changed");
    try { undo.Restore(); throw new InvalidOperationException("Changed file was restored"); }
    catch (IOException) { Check(File.Exists(renamed), "Undo refuses changed contents"); }
    undo = MoveUndo.Capture(a, renamed); await File.WriteAllTextAsync(a, "occupied");
    try { undo.Restore(); throw new InvalidOperationException("Existing file overwritten"); }
    catch (IOException) { Check(await File.ReadAllTextAsync(a) == "occupied", "Undo never overwrites an occupied original name"); }
    var tree = Path.Combine(folder, "tree"); Directory.CreateDirectory(tree); await File.WriteAllTextAsync(Path.Combine(tree, "nested.txt"), "nested");
    transfer = await FileTransfer.ExecuteAsync([tree], destination, true, _ => Task.FromResult(ConflictChoice.Skip));
    transfer.UndoMoves.Single().Restore();
    Check(File.Exists(Path.Combine(tree, "nested.txt")), "Directory move and undo preserve nested files");
    var copy = await FileTransfer.ExecuteAsync([b], destination, false, _ => Task.FromResult(ConflictChoice.Skip));
    Check(copy.UndoMoves.Count == 0, "Copy is not mistaken for a reversible move");
    var conflict = await FileTransfer.ExecuteAsync([b], destination, true, _ => Task.FromResult(ConflictChoice.Replace));
    Check(conflict.UndoMoves.Count == 0 && conflict.UndoUnavailable is not null, "Replacement is explicitly excluded from automatic undo");
    var suggestions = SearchSuggestions.For("report size:");
    Check(suggestions.Count > 0 && suggestions.All(x => SearchQuery.Parse(x.Token).Error is null), "Suggestions use valid search syntax");
    Check(SearchSuggestions.Insert("report ext:p", "ext:pdf") == "report ext:pdf ", "Completion preserves the existing query");
    var outside = Path.Combine(folder, "outside"); Directory.CreateDirectory(outside); await File.WriteAllTextAsync(Path.Combine(outside, "copy.txt"), "same");
    var scope = Path.Combine(folder, "scope"); Directory.CreateDirectory(scope);
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(scope, "link"), outside);
        var linked = FolderAnalysis.Scan(scope, true);
        Check(linked.Items.Count == 0 && linked.Skipped == 1, "Scanner does not follow symbolic links");
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException && (ex.HResult & 0xffff) == 1314) { Console.WriteLine("SKIP symbolic link creation requires Windows Developer Mode or privileges."); }
    Console.WriteLine($"{checks} portable checks passed.");
}
finally { Directory.Delete(folder, recursive: true); }
