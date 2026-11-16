using System.Collections.ObjectModel;

namespace Seekr.App.Services;

public sealed record ClipboardEntry(string[] Paths, bool Move, DateTime Created)
{
    public string Label => $"{(Move ? "Переместить" : "Копировать")} · {Paths.Length} · {Created:HH:mm} · {System.IO.Path.GetFileName(Paths[0])}";
}
public static class ClipboardHistory
{
    public static ObservableCollection<ClipboardEntry> Entries { get; } = [];
    public static void Add(string[] paths, bool move)
    {
        if (paths.Length == 0) return;
        var old = Entries.FirstOrDefault(x => x.Move == move && x.Paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase));
        if (old is not null) Entries.Remove(old);
        Entries.Insert(0, new(paths, move, DateTime.Now));
        while (Entries.Count > 12) Entries.RemoveAt(Entries.Count - 1);
    }
}
