using System;
using System.Collections.Generic;
using System.IO;
using Sentinel.UI.Services;

namespace Sentinel.UiHarness
{
    /// <summary>Answers every sheet immediately (synchronously) and records it for the transcript.</summary>
    public class AutoDialogs : IDialogService
    {
        private readonly string _outDir;

        public AutoDialogs(string outDir) { _outDir = outDir; }

        public bool AnswerYes { get; set; } = true;
        public bool OptionAnswer { get; set; }
        public List<string> Transcript { get; } = new List<string>();

        public void Confirm(string title, string message, string details, string yesText, string noText, Action<bool> result)
        {
            Transcript.Add("CONFIRM [" + title + "] " + message + (details != null ? "\n  details:\n" + Indent(details) : "") + "\n  → " + (AnswerYes ? yesText : noText ?? "Cancel"));
            result?.Invoke(AnswerYes);
        }

        public void ConfirmWithOption(string title, string message, string details, string optionText, bool option,
            string yesText, string noText, Action<bool, bool> result)
        {
            Transcript.Add("CONFIRM+OPTION [" + title + "] " + message + "\n  option: " + optionText + " = " + OptionAnswer +
                           (details != null ? "\n  details:\n" + Indent(details) : "") + "\n  → " + (AnswerYes ? yesText : noText ?? "Cancel"));
            result?.Invoke(AnswerYes, OptionAnswer);
        }

        public void Show(string title, string message, string details = null, bool isError = false)
        {
            Transcript.Add((isError ? "ERROR" : "INFO") + " [" + title + "] " + message + (details != null ? "\n  details:\n" + Indent(details) : ""));
        }

        public void PromptPath(string title, string message, string defaultPath, string actionText, Action<string> result)
        {
            var path = Path.Combine(_outDir, Path.GetFileName(defaultPath ?? "Sentinel.sentinel-library.json"));
            Transcript.Add("PATH [" + title + "] " + message + "\n  → " + actionText + " " + path);
            result?.Invoke(AnswerYes ? path : null);
        }

        private static string Indent(string s) => "    " + s.TrimEnd().Replace("\n", "\n    ");
    }
}
