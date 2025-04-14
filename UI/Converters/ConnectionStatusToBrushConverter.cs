using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.Core.Models.Settings;

namespace StreamWeaver.UI.Converters;

public partial class ConnectionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is ConnectionStatus status
            ? status switch
            {
                ConnectionStatus.Connected => new SolidColorBrush(Colors.LimeGreen),
                ConnectionStatus.Connecting => new SolidColorBrush(Colors.Orange),
                ConnectionStatus.Error => new SolidColorBrush(Colors.Red),
                ConnectionStatus.Disconnected => new SolidColorBrush(Colors.Gray),
                _ => new SolidColorBrush(Colors.Gray),
            }
            : (object)new SolidColorBrush(Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
