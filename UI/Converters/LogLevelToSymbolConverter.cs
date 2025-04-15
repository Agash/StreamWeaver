using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

public partial class LogLevelToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogLevel level)
        {
            // Using Segoe Fluent Icons hex codes
            return level switch
            {
                LogLevel.Trace => "\uEBE8", // Bug (Treat Trace as Debug)
                LogLevel.Debug => "\uEBE8", // Bug
                LogLevel.Information => "\uE946", // Info
                LogLevel.Warning => "\uE7BA", // Warning
                LogLevel.Error => "\uEA39", // ErrorBadge
                LogLevel.Critical => "\uE814", // ReportHacked / Skull
                LogLevel.None => "\uE783", // BlockContact
                _ => "\uE9CE", // Help / Question mark
            };
        }

        return "\uE9CE"; // Default Help/Question
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
