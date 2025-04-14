using Microsoft.UI.Xaml.Data;

namespace StreamWeaver.UI.Converters;

// Returns " (X-Month Milestone)" if months > 0, otherwise empty string.
public partial class MembershipMilestoneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int months && months > 0 ? $" ({months}-Month Milestone)" : (object)string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
