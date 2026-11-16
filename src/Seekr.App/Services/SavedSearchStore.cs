using Seekr.Core.Search;

namespace Seekr.App.Services;

public sealed class SavedSearchStore(string? path = null)
{
    public IReadOnlyList<SavedSearch> List() => SettingsService.Load(path).SavedSearches;
    public string? Add(SavedSearch search) => Change(null, search);
    public string? Update(SavedSearch previous, SavedSearch search) => Change(previous, search);
    public string? Remove(SavedSearch search) => SettingsService.Update(settings =>
        settings.SavedSearches.Remove(search) ? null : "Сохранённый поиск уже изменён или удалён.", path);
    private string? Change(SavedSearch? previous, SavedSearch search)
    {
        search = search with { Name = search.Name.Trim() };
        if (search.Name.Length is < 1 or > 80) return "Название должно содержать от 1 до 80 символов.";
        var parsed = SearchQuery.Parse(search.Query);
        if (parsed.Error is not null || parsed.IsEmpty || (search.SearchContents && parsed.Terms.Count == 0)) return "Укажите корректный поисковый запрос.";
        return SettingsService.Update(settings =>
        {
            var index = previous is null ? -1 : settings.SavedSearches.IndexOf(previous);
            if (previous is not null && index < 0) return "Сохранённый поиск уже изменён. Откройте его заново.";
            if (settings.SavedSearches.Where((_, i) => i != index).Any(x => x.Name.Equals(search.Name, StringComparison.OrdinalIgnoreCase))) return "Поиск с таким названием уже существует.";
            if (index < 0 && settings.SavedSearches.Count >= 100) return "Достигнут лимит: 100 поисков.";
            if (index < 0) settings.SavedSearches.Add(search); else settings.SavedSearches[index] = search;
            return null;
        }, path);
    }
}
