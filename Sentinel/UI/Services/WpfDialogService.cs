using System.Windows;
using Microsoft.Win32;

namespace Sentinel.UI.Services
{
    /// <summary>Dialogs owned by the Sentinel window (Revit itself stays responsive to the modeless window).</summary>
    public class WpfDialogService : IDialogService
    {
        private readonly Window _owner;
        private readonly ThemeManager _theme;

        public WpfDialogService(Window owner, ThemeManager theme)
        {
            _owner = owner;
            _theme = theme;
        }

        public bool Confirm(string title, string message, string details = null, string yesText = "Yes", string noText = "Cancel")
        {
            var dlg = new SentinelDialog(_theme, title, message, details, yesText, noText, null, false, false) { Owner = _owner };
            return dlg.ShowDialog() == true;
        }

        public bool ConfirmWithOption(string title, string message, string details, string optionText, ref bool option,
            string yesText = "Continue", string noText = "Cancel")
        {
            var dlg = new SentinelDialog(_theme, title, message, details, yesText, noText, optionText, option, false) { Owner = _owner };
            var ok = dlg.ShowDialog() == true;
            option = dlg.OptionChecked;
            return ok;
        }

        public void Show(string title, string message, string details = null, bool isError = false)
        {
            var dlg = new SentinelDialog(_theme, title, message, details, "OK", null, null, false, isError) { Owner = _owner };
            dlg.ShowDialog();
        }

        public string PickSaveFile(string title, string filter, string defaultName)
        {
            var d = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, AddExtension = true };
            return d.ShowDialog(_owner) == true ? d.FileName : null;
        }

        public string PickOpenFile(string title, string filter)
        {
            var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return d.ShowDialog(_owner) == true ? d.FileName : null;
        }
    }
}
