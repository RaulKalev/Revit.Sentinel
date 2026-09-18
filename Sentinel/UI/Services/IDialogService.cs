namespace Sentinel.UI.Services
{
    /// <summary>User prompts, abstracted so view models stay testable (the harness answers automatically).</summary>
    public interface IDialogService
    {
        /// <summary>Yes/No question. <paramref name="details"/> is shown in a scrollable box when given.</summary>
        bool Confirm(string title, string message, string details = null, string yesText = "Yes", string noText = "Cancel");

        /// <summary>Confirmation with one extra checkbox option.</summary>
        bool ConfirmWithOption(string title, string message, string details, string optionText, ref bool option,
            string yesText = "Continue", string noText = "Cancel");

        void Show(string title, string message, string details = null, bool isError = false);

        /// <summary>Returns a path or null.</summary>
        string PickSaveFile(string title, string filter, string defaultName);

        string PickOpenFile(string title, string filter);
    }
}
