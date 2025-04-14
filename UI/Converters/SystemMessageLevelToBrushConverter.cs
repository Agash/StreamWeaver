using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

public class SystemMessageLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = new(Colors.CornflowerBlue);
    private static readonly SolidColorBrush WarningBrush = new(Colors.Orange);
    private static readonly SolidColorBrush ErrorBrush = new(Colors.Red);
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is SystemMessageLevel level
            ? level switch
            {
                SystemMessageLevel.Info => InfoBrush,
                SystemMessageLevel.Warning => WarningBrush,
                SystemMessageLevel.Error => ErrorBrush,
                _ => DefaultBrush,
            }
            : (object)DefaultBrush;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
