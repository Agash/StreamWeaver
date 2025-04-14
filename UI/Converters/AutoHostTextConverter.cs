using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

// Returns " (AutoHost)" if the input boolean value is true, otherwise empty string.
public partial class AutoHostTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool isAutoHost && isAutoHost ? " (AutoHost)" : (object)string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
