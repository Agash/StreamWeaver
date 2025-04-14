using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.UI.Converters;

public class ConnectionStatusToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Connected => "\uE73E",
                ConnectionStatus.Connecting => "\uF16A",
                ConnectionStatus.Error => "\uEA39",
                ConnectionStatus.Disconnected => "\uE8C9",
                _ => "\uE9CE",
            };
        }

        return "\uE9CE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
