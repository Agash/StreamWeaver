using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.Core.Models.Events;
using Windows.UI;

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts a DonationEvent object into a SolidColorBrush, typically for background or border.
/// Differentiates between SuperChats, Bits, and other donations.
/// TODO: Implement SuperChat tier coloring based on amount.
/// </summary>
public class DonationToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush s_defaultBrush = new(Colors.DodgerBlue);
    private static readonly SolidColorBrush s_bitsBrush = new(Colors.MediumPurple);
    private static readonly SolidColorBrush s_superChatBaseBrush = new(Colors.Gold);

    public bool IsBackgroundConverter { get; set; } = false;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DonationEvent donation)
        {
            return GetFallbackBrush();
        }

        SolidColorBrush baseBrush = donation.Type switch
        {
            DonationType.SuperChat => GetSuperChatBrush(donation.Amount),
            DonationType.Bits => s_bitsBrush,
            DonationType.Streamlabs or DonationType.Other => s_defaultBrush,
            _ => s_defaultBrush,
        };

        if (IsBackgroundConverter && baseBrush is SolidColorBrush solidBrush)
        {
            Color color = solidBrush.Color;

            return new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
        }

        return baseBrush;
    }

    private static SolidColorBrush GetSuperChatBrush(decimal amount)
    {
        if (amount >= 500)
            return new SolidColorBrush(Colors.Red);
        if (amount >= 100)
            return new SolidColorBrush(Colors.Magenta);
        if (amount >= 50)
            return new SolidColorBrush(Colors.Orange);
        return amount >= 20
            ? new SolidColorBrush(Colors.Yellow)
            : amount >= 5
            ? new SolidColorBrush(Colors.Green)
            : amount >= 2 ? new SolidColorBrush(Colors.Cyan) : new SolidColorBrush(Colors.Blue);
    }

    private SolidColorBrush GetFallbackBrush()
    {
        if (IsBackgroundConverter)
        {
            Color color = s_defaultBrush.Color;
            return new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
        }

        return s_defaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
