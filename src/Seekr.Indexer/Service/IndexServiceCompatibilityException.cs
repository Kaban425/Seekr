namespace Seekr.Indexer.Service;

public sealed class IndexServiceCompatibilityException : IOException
{
    public IndexServiceCompatibilityException() : base("Служба SeekrIndex использует несовместимый формат. Обновите службу в окне «Поиск по дискам». Просмотр папок доступен.") { }
    public IndexServiceCompatibilityException(Exception inner) : base("Служба SeekrIndex вернула несовместимые данные. Обновите службу в окне «Поиск по дискам».", inner) { }
}
