using System.Collections.Generic;
using System.IO;
using Sentinel.UI.Services;

namespace Sentinel.UiHarness
{
    /// <summary>Answers every prompt automatically and records what was asked.</summary>
    public class AutoDialogs : IDialogService
    {
        private readonly string _outDir;

        public AutoDialogs(string outDir) { _outDir = outDir; }

        public bool AnswerYes { get; set; } = true;
        public bool OptionAnswer { get; set; }
        public List<string> Transcript { get; } = new List<string>();

        public bool Confirm(string title, string message, string details = null, string yesText = "Yes", string noText = "Cancel")
        {
            Transcript.Add("CONFIRM [" + title + "] " + message + (details != null ? "\n  details:\n" + Indent(details) : "") + "\n  → " + (AnswerYes ? yesText : noText));
            return AnswerYes;
        }

        public bool ConfirmWithOption(string title, string message, string details, string optionText, ref bool option,
            string yesText = "Continue", string noText = "Cancel")
        {
            option = OptionAnswer;
            Transcript.Add("CONFIRM+OPTION [" + title + "] " + message + "\n  option: " + optionText + " = " + option +
                           (details != null ? "\n  details:\n" + Indent(details) : "") + "\n  → " + (AnswerYes ? yesText : noText));
            return AnswerYes;
        }

        public void Show(string title, string message, string details = null, bool isError = false)
        {
            Transcript.Add((isError ? "ERROR" : "INFO") + " [" + title + "] " + message + (details != null ? "\n  details:\n" + Indent(details) : ""));
        }

        public string PickSaveFile(string title, string filter, string defaultName) => Path.Combine(_outDir, defaultName);

        public string PickOpenFile(string title, string filter) => Path.Combine(_outDir, "Sentinel.sentinel-library.json");

        private static string Indent(string s) => "    " + s.TrimEnd().Replace("\n", "\n    ");
    }
}
