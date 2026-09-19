using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sentinel.UI.Services;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI
{
    /// <summary>
    /// Modeless Sentinel window. Code-behind handles chrome only: theme, compact sidebar, caption buttons, keyboard
    /// shortcuts and the in-window sheet. All behaviour lives in <see cref="MainViewModel"/> and the host.
    /// </summary>
    public partial class SentinelWindow : Window
    {
        /// <summary>Below this width the sidebar shows icons only (labels stay available as tooltips / names).</summary>
        public const double CompactWidth = 1150;

        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
            nameof(IsCompact), typeof(bool), typeof(SentinelWindow), new PropertyMetadata(false));

        private readonly ThemeManager _theme;
        private readonly ISentinelHost _host;
        private IInputElement _focusBeforeSheet;

        public SentinelWindow(ISentinelHost host, IDialogService dialogs = null)
        {
            _host = host;
            InitializeComponent();
            _theme = new ThemeManager(this);
            _theme.ApplyTheme();
            _theme.ThemeChanged += (s, e) => UpdateThemeButton();
            UpdateThemeButton();

            var prefs = _theme.Preferences;
            Width = Math.Max(MinWidth, prefs.WindowWidth);
            Height = Math.Max(MinHeight, prefs.WindowHeight);
            Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width, prefs.WindowLeft));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height, prefs.WindowTop));

            Sheets = new SheetDialogService();
            Sheets.SheetChanged += (s, e) => OnSheetChanged();
            SheetHost.DataContext = Sheets;

            ViewModel = new MainViewModel(host, dialogs ?? Sheets);
            DataContext = ViewModel;
            ViewModel.Doors.IsInspectorVisible = prefs.InspectorVisible;
            DoorsPage.WorkspaceWidth = prefs.InspectorWidth;

            SentinelPage page;
            if (Enum.TryParse(prefs.LastPage, out page) && page != SentinelPage.Doors) ViewModel.CurrentPage = page;

            host.DocumentClosing += (s, e) => Close();
            Loaded += (s, e) => ViewModel.Initialize();
            Closing += OnClosing;
            SizeChanged += (s, e) => UpdateLayoutMode();
            StateChanged += (s, e) => UpdateMaximizedState();
            PreviewKeyDown += OnPreviewKeyDown;
            ViewModel.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MainViewModel.CurrentPage)) UpdateLayoutMode(); };
        }

        public MainViewModel ViewModel { get; }

        /// <summary>The window's own dialog service (sheets). The harness uses it to capture sheets.</summary>
        public SheetDialogService Sheets { get; }

        public string WindowTitle => "Sentinel — " + _host.DocumentTitle;

        public ThemeManager Theme => _theme;

        public bool IsSheetShown => SheetHost.Visibility == System.Windows.Visibility.Visible;

        /// <summary>Opens or closes the Doors page source popover (link, scope, levels, Find doors).</summary>
        public bool IsDoorSourceOpen { get => SourceButton.IsChecked == true; set => SourceButton.IsChecked = value; }

        /// <summary>Content of the door source popover (the harness renders it; popups are separate windows).</summary>
        public FrameworkElement DoorSourcePanel => (FrameworkElement)SourcePopup.Child;

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            private set => SetValue(IsCompactProperty, value);
        }

        // ------------------------------------------------------------------ layout

        private void UpdateLayoutMode()
        {
            IsCompact = ActualWidth < CompactWidth;
            SidebarColumn.Width = new GridLength(IsCompact ? 60 : 212);
            // Doors shows its source button in this place; other pages show a short description when there is room.
            PageSubtitleText.Visibility = ActualWidth < 1250 || ViewModel.IsDoorsPage ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void UpdateMaximizedState()
        {
            // A maximized WindowChrome window extends past the work area by the resize frame; pad the content back in.
            var maximized = WindowState == WindowState.Maximized;
            var frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = maximized ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
            MaximizeIcon.Kind = maximized ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
            MaximizeButton.ToolTip = maximized ? "Restore down" : "Maximize";
            AutomationPropertiesHelper.SetName(MaximizeButton, maximized ? "Restore down" : "Maximize");
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Kind = _theme.IsDarkMode ? MaterialDesignThemes.Wpf.PackIconKind.WeatherNight : MaterialDesignThemes.Wpf.PackIconKind.WhiteBalanceSunny;
            ThemeText.Text = _theme.IsDarkMode ? "Dark appearance" : "Light appearance";
        }

        // ------------------------------------------------------------------ keyboard

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (key == Key.Escape && SourceButton.IsChecked == true)
            {
                SourceButton.IsChecked = false;
                e.Handled = true;
                return;
            }

            if (Sheets.IsOpen)
            {
                if (key == Key.Escape)
                {
                    Sheets.Complete(!Sheets.Current.HasSecondary);
                    e.Handled = true;
                }
                else if (key == Key.Enter && !(Keyboard.FocusedElement is Button))
                {
                    Sheets.Complete(true);
                    e.Handled = true;
                }
                return; // no page shortcuts behind a sheet
            }

            var doors = ViewModel.Doors;
            if (mods == ModifierKeys.Control)
            {
                switch (key)
                {
                    case Key.D1: case Key.NumPad1: ViewModel.CurrentPage = SentinelPage.Doors; e.Handled = true; return;
                    case Key.D2: case Key.NumPad2: ViewModel.CurrentPage = SentinelPage.DoorSets; e.Handled = true; return;
                    case Key.D3: case Key.NumPad3: ViewModel.CurrentPage = SentinelPage.Components; e.Handled = true; return;
                    case Key.D4: case Key.NumPad4: ViewModel.CurrentPage = SentinelPage.Rules; e.Handled = true; return;
                    case Key.D5: case Key.NumPad5: ViewModel.CurrentPage = SentinelPage.Settings; e.Handled = true; return;
                    case Key.S:
                        if (ViewModel.SaveCommand.CanExecute(null)) ViewModel.SaveCommand.Execute(null);
                        e.Handled = true;
                        return;
                    case Key.F:
                        if (ViewModel.IsDoorsPage) { DoorsPage.FocusSearch(); e.Handled = true; }
                        return;
                }
            }

            if (!ViewModel.IsDoorsPage) return;
            if (key == Key.F5 && mods == ModifierKeys.None)
            {
                if (doors.RefreshCommand.CanExecute(null)) doors.RefreshCommand.Execute(null);
                e.Handled = true;
            }
            else if (key == Key.Escape && mods == ModifierKeys.None && doors.Preview.IsActive && !(Keyboard.FocusedElement is ComboBox))
            {
                doors.Preview.ExitCommand.Execute(null);
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Alt && doors.Preview.IsActive && (key == Key.Left || key == Key.Right))
            {
                var cmd = key == Key.Left ? doors.Preview.PreviousCommand : doors.Preview.NextCommand;
                if (cmd.CanExecute(null)) cmd.Execute(null);
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------------ sheet

        private void OnSheetChanged()
        {
            var open = Sheets.IsOpen;
            if (open && SheetHost.Visibility != System.Windows.Visibility.Visible)
                _focusBeforeSheet = Keyboard.FocusedElement;

            SheetHost.Visibility = open ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (open)
            {
                // The sheet drops from the title area (where the window's commands live) and returns there.
                Motion.Play(SheetCard, -10);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Sheets.Current == null) return;
                    if (Sheets.Current.HasPath) { SheetPath.Focus(); SheetPath.SelectAll(); }
                    else SheetPrimary.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                var restore = _focusBeforeSheet as UIElement;
                _focusBeforeSheet = null;
                if (restore != null && restore.IsVisible && restore.IsEnabled) restore.Focus();
            }
        }

        private void FindDoors_Click(object sender, RoutedEventArgs e) => SourceButton.IsChecked = false;

        private void SheetPrimary_Click(object sender, RoutedEventArgs e) => Sheets.Complete(true);
        private void SheetSecondary_Click(object sender, RoutedEventArgs e) => Sheets.Complete(false);

        // Clicks on the dimmed content do nothing (the sheet needs an answer); they must not reach the page.
        private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        // ------------------------------------------------------------------ chrome

        private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _theme.ToggleTheme();
            UpdateThemeButton();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            try
            {
                if (ViewModel.Doors.Preview.IsActive) ViewModel.Doors.Preview.Exit();
                ViewModel.FlushPendingSave();
                _theme.CaptureWindowPlacement();
                _theme.Preferences.LastPage = ViewModel.CurrentPage.ToString();
                _theme.Preferences.InspectorVisible = ViewModel.Doors.IsInspectorVisible;
                _theme.Preferences.InspectorWidth = DoorsPage.WorkspaceWidth;
                _theme.Save();
            }
            catch (Exception ex)
            {
                Infrastructure.SentinelLog.Error("Closing Sentinel window", ex);
            }
        }
    }

    internal static class AutomationPropertiesHelper
    {
        public static void SetName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);
    }
}
