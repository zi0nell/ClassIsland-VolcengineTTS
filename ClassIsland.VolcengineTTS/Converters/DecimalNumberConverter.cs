using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassIsland.VolcengineTTS.Converters;

/// <summary>
/// 在 Avalonia NumericUpDown 的 decimal? 与模型中的 int / double 属性之间进行安全双向转换。
/// </summary>
public sealed class DecimalNumberConverter : IValueConverter
{
    public static readonly DecimalNumberConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => null,
            decimal d => d,
            int i => (decimal)i,
            long l => (decimal)l,
            float f => (decimal)f,
            double dbl => (decimal)dbl,
            _ => System.Convert.ToDecimal(value, culture)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        var dec = System.Convert.ToDecimal(value, culture);
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(int))
        {
            return (int)Math.Round(dec);
        }

        if (underlying == typeof(double))
        {
            return (double)dec;
        }

        if (underlying == typeof(float))
        {
            return (float)dec;
        }

        return dec;
    }
}
