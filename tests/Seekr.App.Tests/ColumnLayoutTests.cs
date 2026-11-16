using Seekr.App.Services;

namespace Seekr.App.Tests;

public sealed class ColumnLayoutTests
{
    [Theory]
    [InlineData(320)][InlineData(560)][InlineData(840)][InlineData(1100)]
    public void ColumnsFitAvailableViewportWithoutMutatingPreferredWidths(double viewport)
    {
        var desired = new Dictionary<string,double>{{"Имя",560},{"Размер",112},{"Дата изменения",154},{"Тип",144}};
        var layout = AdaptiveColumnLayout.Calculate(desired,viewport,44,true);
        Assert.True(layout.Values.Sum()+44 <= viewport+0.1);
        Assert.Equal(560,desired["Имя"]);
        Assert.True(layout["Имя"]>=100);
        var restored=AdaptiveColumnLayout.Calculate(desired,1400,44,true);
        Assert.Equal(560,restored["Имя"]);
    }
}
