namespace Seekr.App.Services;

public static class AdaptiveColumnLayout
{
    public static IReadOnlyDictionary<string,double> Calculate(IReadOnlyDictionary<string,double> preferred, double viewport, double selectionWidth, bool customName)
    {
        var budget=Math.Max(100,viewport-selectionWidth-20);
        var widths=preferred.ToDictionary(pair=>pair.Key,pair=>double.IsFinite(pair.Value)?Math.Max(0,pair.Value):140);
        if(widths.ContainsKey("Имя") && !customName) widths["Имя"]=Math.Max(140,budget-widths.Where(pair=>pair.Key!="Имя").Sum(pair=>pair.Value));
        var minimum=widths.ToDictionary(pair=>pair.Key,pair=>pair.Key switch{"Имя"=>140d,"Размер" or "Свободно"=>76,"Тип"=>96,_=>118});
        var excess=widths.Values.Sum()-budget;
        if(excess>0)
        {
            var room=widths.Sum(pair=>Math.Max(0,pair.Value-minimum[pair.Key]));
            if(room>0) foreach(var key in widths.Keys.ToArray()) widths[key]-=Math.Max(0,widths[key]-minimum[key])*Math.Min(1,excess/room);
        }
        // Narrow panes keep name/size; less essential columns return at their preferred widths when space returns.
        foreach(var key in new[]{"Тип","Дата создания","Дата изменения","Свободно"})
            if(widths.Values.Sum()>budget && widths.ContainsKey(key)) widths[key]=0;
        if(widths.Values.Sum()>budget && widths.ContainsKey("Имя")) widths["Имя"]=Math.Max(100,budget-widths.Where(pair=>pair.Key!="Имя").Sum(pair=>pair.Value));
        return widths;
    }
}
