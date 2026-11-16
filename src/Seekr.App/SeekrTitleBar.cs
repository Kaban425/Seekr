using System.Windows;
using Wpf.Ui.Controls;

namespace Seekr.App;

/// <summary>Resolve the current owner on every caption action, including after template reloads.</summary>
public sealed class SeekrTitleBar : TitleBar
{
    public SeekrTitleBar()
    {
        SetResourceReference(StyleProperty, typeof(TitleBar));
        SetValue(TemplateButtonCommandProperty,
            new Wpf.Ui.Input.RelayCommand<TitleBarButtonType>(ExecuteCaption));
    }

    private void ExecuteCaption(TitleBarButtonType action)
    {
        if (Window.GetWindow(this) is not { } window) return;
        switch (action)
        {
            case TitleBarButtonType.Minimize:
                if (window.ResizeMode != ResizeMode.NoResize) SystemCommands.MinimizeWindow(window);
                break;
            case TitleBarButtonType.Maximize:
            case TitleBarButtonType.Restore:
                if (!CanMaximize || window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize) return;
                if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
                else SystemCommands.MaximizeWindow(window);
                break;
            case TitleBarButtonType.Close:
                window.Close();
                break;
        }
    }
}
