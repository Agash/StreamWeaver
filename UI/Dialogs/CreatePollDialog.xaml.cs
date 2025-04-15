using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Dialogs;

public sealed partial class CreatePollDialog : ContentDialog
{
    internal CreatePollDialogViewModel ViewModel =>
        DataContext as CreatePollDialogViewModel ?? throw new InvalidOperationException("DataContext must be CreatePollDialogViewModel");

    public CreatePollDialog() => this.InitializeComponent();// Consider setting DataContext here if not done externally before showing// DataContext = new CreatePollDialogViewModel(...); // If ViewModel needs args or DI

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            // ViewModel handles validation internally when TryGetValidatedData is called
            if (!ViewModel.TryGetValidatedData(out _, out _))
            {
                args.Cancel = true; // Keep dialog open if validation fails
            }
            // If validation passes, args.Cancel remains false, and the dialog will close.
            // The MainChatViewModel will retrieve the validated data after the dialog closes.
        }
        finally
        {
            deferral.Complete();
        }
    }
}
