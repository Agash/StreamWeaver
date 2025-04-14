using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

// Returns " \"Message\"" if message exists, otherwise empty string.
public partial class SubEventMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string message && !string.IsNullOrEmpty(message))
        {
            bool addQuotes = (parameter as string) != "NoQuotes";
            return addQuotes ? $" \"{message}\"" : $" {message}";
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
