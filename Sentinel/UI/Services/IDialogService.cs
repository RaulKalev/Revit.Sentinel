using System;

namespace Sentinel.UI.Services
{
    /// <summary>
    /// User prompts. All prompts are non-blocking (answer delivered through a callback) because plugin windows must
    /// stay modeless and never run a nested modal loop on Revit's UI thread (AGENTS.md: Show(), never ShowDialog()).
    /// The window implementation shows in-window sheets; the harness answers automatically.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Two-choice question. <paramref name="details"/> is shown in a scrollable box when given.</summary>
        void Confirm(string title, string message, string details, string yesText, string noText, Action<bool> result);

        /// <summary>Confirmation with one extra checkbox option. Callback: (confirmed, option).</summary>
        void ConfirmWithOption(string title, string message, string details, string optionText, bool option,
            string yesText, string noText, Action<bool, bool> result);

        /// <summary>Information or error with a single acknowledge button.</summary>
        void Show(string title, string message, string details = null, bool isError = false);

        /// <summary>Asks for a file path (prefilled). Callback receives the path, or null when cancelled.</summary>
        void PromptPath(string title, string message, string defaultPath, string actionText, Action<string> result);
    }
}
