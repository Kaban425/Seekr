namespace Seekr.App.ViewModels;

/// <summary>One clickable segment of the address-bar breadcrumb.</summary>
public sealed class BreadcrumbItem(string label, int index)
{
    public string Label { get; } = label;

    public int Index { get; } = index;
}
