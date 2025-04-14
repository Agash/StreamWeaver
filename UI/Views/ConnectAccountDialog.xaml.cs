using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Views;

public sealed partial class ConnectAccountDialog : ContentDialog
{
    internal ConnectAccountViewModel ViewModel =>
        DataContext as ConnectAccountViewModel ?? throw new InvalidOperationException("DataContext must be ConnectAccountViewModel");

    public ConnectAccountDialog() => this.InitializeComponent();

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            bool success = await ViewModel.HandleConnectAsync();
            if (!success)
            {
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => ViewModel.HandleCancel();
}
