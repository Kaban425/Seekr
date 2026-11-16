using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Seekr.Setup.Tests;

public sealed class SetupVisualTests
{
    [Fact]
    public void InstallerArrangesWithoutStartingInstallation()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Seekr.Setup.App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
                var window = new Seekr.Setup.MainWindow();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(820,590)); content.Arrange(new Rect(0,0,820,590)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(820,590,96,96,PixelFormats.Pbgra32);
                var backdrop = new DrawingVisual(); using(var drawing=backdrop.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(21,32,46)),null,new Rect(0,0,820,590));
                bitmap.Render(backdrop); bitmap.Render(content);
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while(root is not null && !File.Exists(Path.Combine(root.FullName,"Seekr.slnx"))) root=root.Parent;
                Assert.NotNull(root);
                var output=Directory.CreateDirectory(Path.Combine(root.FullName,"artifacts","setup-visual")).FullName;
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var file=File.Create(Path.Combine(output,"installer.png"))) png.Save(file);
                Assert.InRange(content.ActualWidth,819,821);
                Assert.NotNull(window.FindName("PrimaryButton"));
                window.Close(); app.Shutdown();
            }
            catch(Exception ex){error=ex;}
            finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if(error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
