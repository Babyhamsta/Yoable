using System.Threading.Tasks;

namespace Yoable.Services;

/// <summary>
/// Cross-platform dialog service for showing messages and getting user input
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an information message
    /// </summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Shows an error message
    /// </summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a warning message
    /// </summary>
    Task ShowWarningAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// Shows a dialog with Yes/No/Cancel buttons
    /// </summary>
    Task<DialogResult> ShowYesNoCancelAsync(string title, string message);
}

public enum DialogResult
{
    Yes,
    No,
    Cancel
}
