using Seekr.App.Services;

namespace Seekr.App.Tests;

public sealed class GlassSettingsTests
{
    [Fact]
    public void LegacyGlassSettingsMigrateWithoutLosingPersonalization()
    {
        var path=Path.Combine(Path.GetTempPath(),"Seekr-glass-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            File.WriteAllText(path,"""{"design":"DarkGlass","iconStyle":"Glass","glass":true,"glassOpacity":0.22,"customWallpaper":"C:\\Pictures\\own.jpg","uiScale":1.25}""");
            var restored=SettingsService.Load(path);
            Assert.Equal("Graphite",restored.Design); Assert.Equal("Glass",restored.IconStyle);
            Assert.Equal(@"C:\Pictures\own.jpg",restored.CustomWallpaper); Assert.Equal(1.25,restored.UiScale);
            Assert.True(SettingsService.Save(restored,path));
            using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(json.RootElement.TryGetProperty("glass",out _));
            Assert.False(json.RootElement.TryGetProperty("glassOpacity",out _));
        }
        finally { File.Delete(path); }
    }
}

public sealed partial class WorkspaceTests
{
    [Fact]
    public void LargeFilesCommandUsesFilesAndDescendingSize()
    {
        RunSta(()=>
        {
            using var vm=new Seekr.App.ViewModels.WorkspaceViewModel(persistViewSettings:false);
            vm.FindLargeFilesCommand.Execute(null);
            Assert.Equal("kind:file size:>100MB",vm.Query);
            Assert.Equal("Размер",vm.SortColumn); Assert.True(vm.IsSortDescending);
            Assert.Contains(vm.ActiveFilters,filter=>filter.Label=="Объекты: файлы");
        });
    }
}
