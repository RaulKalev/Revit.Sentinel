using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using Sentinel.Infrastructure;

namespace Sentinel
{
    [AppLoader]
    public class App : IExternalApplication
    {
        public const string TabName = "RK Tools";
        public const string PanelName = "Sentinel";

        private RibbonPanel _ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); } catch { /* tab already exists (other RK Tools plugins) */ }
            _ribbonPanel = application.CreateOrSelectPanel(TabName, PanelName);

            _ribbonPanel.CreatePushButton<Command>("Sentinel")
                .SetLargeImage("Resources/Sentinel32.png")
                .SetImage("Resources/Sentinel16.png")
                .SetToolTip("Sentinel – access control door sets: discover linked doors, assign door set types, preview and place security devices.")
                .SetContextualHelp("https://github.com/RaulKalev/Revit.Sentinel");

            SentinelLog.Info("Sentinel " + typeof(App).Assembly.GetName().Version + " loaded.");
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            Command.CloseWindow();
            _ribbonPanel?.Remove();
            return Result.Succeeded;
        }
    }
}
