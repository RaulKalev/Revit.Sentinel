using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace Sentinel.UI
{
    /// <summary>Themed message / confirmation dialog (modal only to the Sentinel window, not to Revit).</summary>
    public partial class SentinelDialog : Window
    {
        public SentinelDialog(ThemeManager theme, string title, string message, string details, string yesText, string noText,
            string optionText, bool optionValue, bool isError)
        {
            InitializeComponent();
            theme?.ApplyTheme(Resources);

            TitleText.Text = title;
            MessageText.Text = message;
            if (!string.IsNullOrWhiteSpace(details))
            {
                DetailsBox.Text = details.TrimEnd();
                DetailsBox.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(optionText))
            {
                OptionBox.Content = optionText;
                OptionBox.IsChecked = optionValue;
                OptionBox.Visibility = Visibility.Visible;
            }
            YesButton.Content = yesText ?? "OK";
            if (noText == null) NoButton.Visibility = Visibility.Collapsed;
            else NoButton.Content = noText;
            if (isError)
            {
                HeaderIcon.Kind = PackIconKind.AlertCircleOutline;
                HeaderIcon.SetResourceReference(ForegroundProperty, "ErrorBrush");
            }
        }

        public bool OptionChecked => OptionBox.IsChecked == true;

        private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }
}
