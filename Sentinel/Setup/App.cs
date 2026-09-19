using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ricaun.Revit.UI;
using Sentinel.Infrastructure;

namespace Sentinel
{
    [AppLoader]
    public class App : IExternalApplication
    {
        public const string TabName = "RK Tools";
        public const string PanelName = "Sentinel";

        private const string DarkIcon = "pack://application:,,,/Sentinel;component/Assets/Dark%20-%20Sentinel.tiff";
        private const string LightIcon = "pack://application:,,,/Sentinel;component/Assets/Light%20-%20Sentinel.tiff";

        private RibbonPanel _ribbonPanel;
        private PushButton _button;

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); } catch { /* tab already exists (other RK Tools plugins) */ }
            _ribbonPanel = application.CreateOrSelectPanel(TabName, PanelName);

            _button = _ribbonPanel.CreatePushButton<Command>("Sentinel");
            _button
                .SetToolTip("Sentinel – access control door sets: discover linked doors, assign door set types, preview and place security devices.")
                .SetContextualHelp("https://github.com/RaulKalev/Revit.Sentinel");
            ApplyThemeIcon();

            // Follow Revit's light/dark UI theme while Revit runs, not only at startup.
            application.ThemeChanged += OnThemeChanged;

            SentinelLog.Info("Sentinel " + typeof(App).Assembly.GetName().Version + " loaded.");

            // Inert unless SENTINEL_SELFTEST_OUTPUT is set (Tools/Run-RevitSelfTest.ps1).
            Diagnostics.SelfTest.TryAttach(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ThemeChanged -= OnThemeChanged;
            Command.CloseWindow();
            _ribbonPanel?.Remove();
            return Result.Succeeded;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e) => ApplyThemeIcon();

        /// <summary>Light icon on Revit's dark theme, dark icon on the light theme.</summary>
        private void ApplyThemeIcon()
        {
            try
            {
                var icon = UIThemeManager.CurrentTheme == UITheme.Dark ? DarkIcon : LightIcon;
                _button?.SetLargeImage(icon).SetImage(icon);
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Setting the ribbon icon failed", ex);
            }
        }
    }
}
