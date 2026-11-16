using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Seekr.App.Icons;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static void VerifyDetailsAndAppearance(App app,WorkspaceView view,WorkspaceViewModel vm)
    {
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var grid=(GridView)view.FindName("ResultsGrid"); var list=(ListView)view.FindName("ResultsList");
        var name=grid.Columns.Single(column=>Equals(column.Header,"Имя"));
        name.Width=560;
        vm.CurrentPath=@"C:\Проекты"; vm.Query=""; vm.IsSearching=false;
        vm.Results=[VisualItem(0,@"C:\Проекты\Документы",true,320000000),VisualItem(1,@"C:\Проекты\Длинное название файла для проверки сведений.pdf",false,12000000)];
        vm.Results[1].IsSelected=true;
        var canvas=new Border { Child=view };
        canvas.SetResourceReference(Border.BackgroundProperty,"Seekr.Canvas");
        canvas.SetResourceReference(FrameworkElement.LayoutTransformProperty,"Seekr.ContentScale");
        var host=new Border { Child=canvas,Background=new LinearGradientBrush(Color.FromRgb(105,118,128),Color.FromRgb(29,52,77),30) };
        try
        {
            foreach(var scale in new[]{1d,1.5})
            foreach(var width in new[]{840d,1100,1440})
            {
                ScaleService.ApplyScale(scale); vm.ShowPreviewPane=true;
                host.Measure(new Size(width,800)); host.Arrange(new Rect(0,0,width,800)); host.UpdateLayout();
                var columns=grid.Columns.Sum(column=>column.Width);
                Assert.True(columns <= list.ActualWidth+1,$"Columns {columns}, list {list.ActualWidth}, width {width}, scale {scale}");
                var panel=(Border)view.FindName("PreviewPanel");
                Assert.Equal(Visibility.Visible,panel.Visibility);
                var edge=panel.TranslatePoint(new Point(panel.ActualWidth,0),host).X;
                Assert.True(edge<=width+1,$"Details outside viewport at {width}/{scale}");
                if(scale==1 && width==1440) SaveVisual(host,"details-adaptive-wide");
            }
            ScaleService.ApplyScale(1); vm.ShowPreviewPane=false;
            host.Measure(new Size(1440,800)); host.Arrange(new Rect(0,0,1440,800)); host.UpdateLayout();
            Assert.Equal(560,name.Width);
            foreach (var theme in new[]{"Dark","Light"})
            {
                ThemeService.ApplyTheme(theme);
                AppearanceService.Apply(new AppSettings { Design="Graphite", IconStyle="Glass" });
                foreach(var surface in new[]{"Canvas","WorkspaceBackground","MenuSurface","Surface","Sidebar","Hover","Pressed","Selection"})
                    Assert.Equal(255,((SolidColorBrush)app.FindResource("Seekr."+surface)).Color.A);
            }
            foreach(var kind in new[]{IconKind.Folder,IconKind.Drive,IconKind.Home,IconKind.Search,IconKind.Settings,IconKind.Copy})
                Assert.True(IconArtwork.Image(kind).IsFrozen);
            SaveIconAtlas(app,"glass");
        }
        finally
        {
            canvas.Child=null; vm.ShowPreviewPane=false;
            ScaleService.ApplyScale(1); ThemeService.ApplyTheme("Dark"); AppearanceService.Apply(new AppSettings());
        }
    }
}
