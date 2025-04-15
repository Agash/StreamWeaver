using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StreamWeaver.UI.ViewModels;

/// <summary>
/// Represents a single option in the poll creation dialog.
/// </summary>
public partial class PollOptionViewModel : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "Option text cannot be empty.")]
    [MaxLength(30, ErrorMessage = "Option text cannot exceed 30 characters.")]
    [NotifyDataErrorInfo]
    public partial string? Text { get; set; }

    public void Validate() => ValidateAllProperties();
}

/// <summary>
/// ViewModel for the Create Poll dialog.
/// </summary>
public partial class CreatePollDialogViewModel : ObservableValidator
{
    private const int MaxOptions = 5;
    private const int MinOptions = 2;

    [ObservableProperty]
    [Required(ErrorMessage = "Poll question cannot be empty.")]
    [MaxLength(100, ErrorMessage = "Question cannot exceed 100 characters.")]
    [NotifyDataErrorInfo]
    public partial string? Question { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<PollOptionViewModel> Options { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public CreatePollDialogViewModel()
    {
        // Start with the minimum required options
        AddOption();
        AddOption();
    }

    [RelayCommand(CanExecute = nameof(CanAddOption))]
    private void AddOption()
    {
        if (Options.Count < MaxOptions)
        {
            Options.Add(new PollOptionViewModel());
            AddOptionCommand.NotifyCanExecuteChanged();
            RemoveOptionCommand.NotifyCanExecuteChanged(); // Can now remove if > MinOptions
        }
    }

    private bool CanAddOption() => Options.Count < MaxOptions;

    [RelayCommand(CanExecute = nameof(CanRemoveOption))]
    private void RemoveOption(PollOptionViewModel? optionToRemove)
    {
        if (optionToRemove != null && Options.Count > MinOptions)
        {
            Options.Remove(optionToRemove);
            AddOptionCommand.NotifyCanExecuteChanged(); // Can now add if < MaxOptions
            RemoveOptionCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRemoveOption() => Options.Count > MinOptions;

    /// <summary>
    /// Validates the poll data and provides the validated question and options if successful.
    /// </summary>
    /// <param name="validatedQuestion">The validated poll question, if successful.</param>
    /// <param name="validatedOptions">The list of validated option strings, if successful.</param>
    /// <returns>True if validation passes, false otherwise.</returns>
    public bool TryGetValidatedData([NotNullWhen(true)] out string? validatedQuestion, [NotNullWhen(true)] out List<string>? validatedOptions)
    {
        validatedQuestion = null;
        validatedOptions = null;
        ErrorMessage = null;

        ValidateAllProperties();
        if (HasErrors)
        {
            ErrorMessage = string.Join("; ", GetErrors().Select(e => e.ErrorMessage));
            return false;
        }

        bool allOptionsValid = true;
        List<string> currentOptions = [];
        foreach (PollOptionViewModel optionVm in Options)
        {
            optionVm.Validate();

            if (optionVm.HasErrors)
            {
                allOptionsValid = false;
                break; // Stop validation on the first invalid option
            }

            if (optionVm.Text != null)
            {
                currentOptions.Add(optionVm.Text);
            }
        }

        if (!allOptionsValid)
        {
            ErrorMessage = "One or more options are invalid (empty or too long).";
            return false;
        }

        if (Options.Count is < MinOptions or > MaxOptions)
        {
            ErrorMessage = $"Poll must have between {MinOptions} and {MaxOptions} options.";
            return false;
        }

        if (currentOptions.GroupBy(opt => opt, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            ErrorMessage = "Poll options must be unique.";
            return false;
        }

        validatedQuestion = Question!;
        validatedOptions = currentOptions;
        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        ValidateProperty(e.PropertyName!);
    }
}
