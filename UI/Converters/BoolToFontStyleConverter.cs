using Microsoft.UI.Xaml.Data;
using Windows.UI.Text;

namespace StreamWeaver.UI.Converters;

// Converts True to Italic, False to Normal
public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is bool b ? b ? FontStyle.Italic : FontStyle.Normal : (object)FontStyle.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
