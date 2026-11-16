namespace Seekr.Core.Search;

public sealed record SearchSuggestion(string Token, string Description);
public static class SearchSuggestions
{
    public static IReadOnlyList<SearchSuggestion> For(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || char.IsWhiteSpace(query[^1])) return [];
        var token = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        SearchSuggestion[] options = [
            new("kind:file", "Только файлы"), new("kind:folder", "Только папки"),
            new("ext:pdf", "Документы PDF"), new("ext:docx", "Документы Word"), new("ext:jpg", "Изображения JPG"),
            new("ext:zip", "ZIP-архивы"), new("size:>10mb", "Размер больше 10 МБ"), new("size:>100mb", "Размер больше 100 МБ"),
            new("size:>1gb", "Размер больше 1 ГБ"), new("dm:today", "Изменены сегодня"), new("dm:thisweek", "Изменены на этой неделе")];
        return token.Length == 0 ? [] : options.Where(x => x.Token.StartsWith(token, StringComparison.OrdinalIgnoreCase)).Take(6).ToArray();
    }
    public static string Insert(string query, string token)
    {
        var last = query.LastIndexOf(' ');
        return (last >= 0 ? query[..(last + 1)] : "") + token + " ";
    }
}
