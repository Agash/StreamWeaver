using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace StreamWeaver.UI.Converters;

public partial class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush s_traceDebugBrush = new(Colors.Gray);
    private static readonly SolidColorBrush s_infoBrush = new(Colors.CornflowerBlue);
    private static readonly SolidColorBrush s_warningBrush = new(Colors.Orange);
    private static readonly SolidColorBrush s_errorBrush = new(Colors.Red);
    private static readonly SolidColorBrush s_criticalBrush = new(Colors.DarkRed);
    private static readonly SolidColorBrush s_noneBrush = new(Colors.DimGray);
    private static readonly SolidColorBrush s_defaultBrush = new(Colors.Black);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => s_traceDebugBrush,
                LogLevel.Debug => s_traceDebugBrush,
                LogLevel.Information => s_infoBrush,
                LogLevel.Warning => s_warningBrush,
                LogLevel.Error => s_errorBrush,
                LogLevel.Critical => s_criticalBrush,
                LogLevel.None => s_noneBrush,
                _ => s_defaultBrush,
            };
        }

        return s_defaultBrush; // Fallback
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
