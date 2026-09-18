using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Sentinel.UI.Services;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI
{
    /// <summary>
    /// Modeless Sentinel window. Code-behind only handles window chrome (theme, resize, placement); all behaviour
    /// lives in <see cref="MainViewModel"/> and the host.
    /// </summary>
    public partial class SentinelWindow : Window
    {
        private readonly ThemeManager _theme;
        private readonly WindowResizer _resizer;
        private readonly ISentinelHost _host;

        public SentinelWindow(ISentinelHost host, IDialogService dialogs = null)
        {
            _host = host;
            InitializeComponent();
            _theme = new ThemeManager(this);
            _theme.ApplyTheme();
            ThemeToggleButton.IsChecked = _theme.IsDarkMode;

            var prefs = _theme.Preferences;
            Width = Math.Max(MinWidth, prefs.WindowWidth);
            Height = Math.Max(MinHeight, prefs.WindowHeight);
            Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width, prefs.WindowLeft));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height, prefs.WindowTop));

            _resizer = new WindowResizer(this) { IgnoreElement = TitleBarControl };

            ViewModel = new MainViewModel(host, dialogs ?? new WpfDialogService(this, _theme));
            DataContext = ViewModel;

            SentinelPage page;
            if (Enum.TryParse(prefs.LastPage, out page) && page != SentinelPage.Doors) ViewModel.CurrentPage = page;

            host.DocumentClosing += (s, e) => Close();
            Loaded += (s, e) => ViewModel.Initialize();
            Closing += OnClosing;
        }

        public MainViewModel ViewModel { get; }

        public string WindowTitle => "Sentinel — " + _host.DocumentTitle;

        public ThemeManager Theme => _theme;

        private void OnClosing(object sender, CancelEventArgs e)
        {
            try
            {
                if (ViewModel.Doors.Preview.IsActive) ViewModel.Doors.Preview.Exit();
                ViewModel.FlushPendingSave();
                _theme.CaptureWindowPlacement();
                _theme.Preferences.LastPage = ViewModel.CurrentPage.ToString();
                _theme.Save();
            }
            catch (Exception ex)
            {
                Infrastructure.SentinelLog.Error("Closing Sentinel window", ex);
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _theme.ToggleTheme();
            ThemeToggleButton.IsChecked = _theme.IsDarkMode;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e) => _resizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _resizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _resizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _resizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _resizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _resizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _resizer.StartResizing(e, ResizeDirection.BottomRight);
    }
}
