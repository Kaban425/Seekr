using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App;

public sealed record CommandChoice(string Label, Action Execute);
/// <summary>Keyboard-first palette shared by command search and clipboard history.</summary>
public sealed class ChoiceWindow : FluentWindow
{
    public ChoiceWindow(string title, IReadOnlyList<CommandChoice> choices)
    {
        Title = title; Width = 640; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(20) };
        var input = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 12), FontSize = 16, Padding = new Thickness(10) };
        DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input);
        var list = new ListBox { DisplayMemberPath = nameof(CommandChoice.Label), ItemsSource = choices };
        panel.Children.Add(list);
        var view = CollectionViewSource.GetDefaultView(list.ItemsSource);
        input.TextChanged += (_, _) => { view.Filter = item => ((CommandChoice)item).Label.Contains(input.Text, StringComparison.OrdinalIgnoreCase); if (list.Items.Count > 0) list.SelectedIndex = 0; };
        void Accept() { if (list.SelectedItem is CommandChoice choice) { Close(); choice.Execute(); } }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            if (e.Key == Key.Down && input.IsKeyboardFocusWithin) { list.Focus(); list.SelectedIndex = 0; e.Handled = true; }
        };
        list.MouseDoubleClick += (_, _) => Accept();
        Content = panel; WindowPresentation.ApplyDialog(this, panel);
        Loaded += (_, _) => { input.Focus(); if (list.Items.Count > 0) list.SelectedIndex = 0; };
    }
}
