using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a HostEvent object into a descriptive string for display.
/// </summary>
public class HostEventTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is HostEvent hostEvent)
        {
            if (hostEvent.IsHosting)
            {
                return $"You are now hosting {hostEvent.HostedChannel ?? "Unknown"}";
            }
            else
            {
                if (!string.IsNullOrEmpty(hostEvent.HosterUsername))
                {
                    return $"{hostEvent.HosterUsername} is hosting you with {hostEvent.ViewerCount} viewer{(hostEvent.ViewerCount != 1 ? "s" : "")}";
                }
                else
                {
                    return "Host mode ended.";
                }
            }
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
