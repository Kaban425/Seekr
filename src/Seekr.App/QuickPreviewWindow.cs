using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Seekr.App.Services;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Seekr.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The window Closed event cancels and disposes the preview lifetime.")]
public sealed class QuickPreviewWindow : FluentWindow
{
    private bool _closed;
    private readonly CancellationTokenSource _lifetime = new();
    public QuickPreviewWindow(string path)
    {
        Title = "Быстрый просмотр · " + Path.GetFileName(path); Width = 900; Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var close = new System.Windows.Controls.Button { Content = "Закрыть · Пробел / Esc", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(close, Dock.Top); close.Click += (_, _) => Close(); panel.Children.Add(close);
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        panel.Children.Add(host);
        Content = panel; WindowPresentation.ApplyDialog(this, panel);
        PreviewKeyDown += (_, e) => { if (e.Key is Key.Space or Key.Escape) { Close(); e.Handled = true; } };
        WebView2? browser = null;
        var token = _lifetime.Token;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); browser?.Dispose(); };
        Loaded += async (_, _) =>
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("Файл больше не существует.");
                var extension = info.Extension.ToLowerInvariant();
                if (extension == ".pdf")
                {
                    browser = new WebView2(); host.Content = browser;
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "PreviewBrowser"));
                    if (_closed) return;
                    await browser.EnsureCoreWebView2Async(environment);
                    if (_closed) return;
                    var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
                    var core = browser.CoreWebView2;
                    core.Settings.IsScriptEnabled = false;
                    core.Settings.AreDevToolsEnabled = false;
                    core.Settings.AreDefaultContextMenusEnabled = false;
                    core.Settings.IsWebMessageEnabled = false;
                    core.NewWindowRequested += (_, args) => args.Handled = true;
                    core.DownloadStarting += (_, args) => args.Cancel = true;
                    core.NavigationStarting += (_, args) => { if (args.Uri.Split('#')[0] != uri) args.Cancel = true; };
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                    core.WebResourceRequested += (_, args) =>
                    {
                        if (args.Request.Uri.Split('#')[0] != uri)
                            args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                    };
                    core.Navigate(uri);
                }
                else if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")
                {
                    if (info.Length > 50L * 1024 * 1024) throw new IOException("Для предпросмотра изображений установлен лимит 50 МБ.");
                    var bitmap = await Task.Run(() => BoundedImageLoader.Load(path, 1800, 50L * 1024 * 1024, token), token);
                    if (!_closed) host.Content = new Image { Source = bitmap, Stretch = System.Windows.Media.Stretch.Uniform };
                }
                else if (ContentReaders.Supports(path))
                {
                    var text = await Task.Run(() => ContentReaders.Read(path, token), token);
                    if (!_closed) host.Content = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(12) };
                }
                else host.Content = new System.Windows.Controls.TextBlock { Text = $"{path}\n\nРазмер: {ViewModels.ResultItem.FormatBytes(info.Length)}\n\nПредпросмотр этого формата пока не поддерживается.", TextWrapping = TextWrapping.Wrap };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
                or FormatException or System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException or System.Text.DecoderFallbackException)
            {
                if (!_closed) host.Content = new System.Windows.Controls.TextBlock { Text = "Предпросмотр недоступен: " + ex.Message + "\nДля PDF требуется Microsoft Edge WebView2 Runtime.", TextWrapping = TextWrapping.Wrap };
            }
        };
    }
}

