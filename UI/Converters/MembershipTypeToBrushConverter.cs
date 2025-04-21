using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.Core.Models.Events;
using Windows.UI; // Required for Color struct

namespace StreamWeaver.UI.Converters;

/// <summary>
/// Converts MembershipEventType to a background SolidColorBrush for visual distinction.
/// Provides subtle background tints.
/// </summary>
public partial class MembershipTypeToBrushConverter : IValueConverter
{
    // Define base color and subtle variations
    private static readonly Color s_baseMemberColor = Color.FromArgb(255, 15, 157, 88); // Google Green
    private static readonly SolidColorBrush s_newBrush = new(Color.FromArgb(0x1A, s_baseMemberColor.R, s_baseMemberColor.G, s_baseMemberColor.B)); // ~10% Opacity
    private static readonly SolidColorBrush s_milestoneBrush = new(Color.FromArgb(0x26, s_baseMemberColor.R, s_baseMemberColor.G, s_baseMemberColor.B)); // ~15% Opacity (Slightly darker/more prominent)
    private static readonly SolidColorBrush s_giftPurchaseBrush = new(Color.FromArgb(0x1A, 66, 133, 244)); // ~10% Google Blue
    private static readonly SolidColorBrush s_giftRedemptionBrush = new(Color.FromArgb(0x1A, 219, 68, 55)); // ~10% Google Red
    private static readonly SolidColorBrush s_defaultBrush = new(Colors.Transparent); // Default transparent

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not MembershipEventType type)
        {
            return s_defaultBrush;
        }

        return type switch
        {
            MembershipEventType.New => s_newBrush,
            MembershipEventType.Milestone => s_milestoneBrush,
            MembershipEventType.GiftPurchase => s_giftPurchaseBrush,
            MembershipEventType.GiftRedemption => s_giftRedemptionBrush,
            _ => s_defaultBrush, // Default to transparent for Unknown
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotImplementedException();
}
