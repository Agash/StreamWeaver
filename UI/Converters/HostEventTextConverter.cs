using Microsoft.UI.Xaml.Data;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a HostEvent object into a descriptive string for display.
/// </summary>
public class HostEventTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is HostEvent hostEvent
            ? hostEvent.IsHosting
                ? $"You are now hosting {hostEvent.HostedChannel ?? "Unknown"}"
                : !string.IsNullOrEmpty(hostEvent.HosterUsername)
                    ? $"{hostEvent.HosterUsername} is hosting you with {hostEvent.ViewerCount} viewer{(hostEvent.ViewerCount != 1 ? "s" : "")}"
                    : (object)"Host mode ended."
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
