using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Sentinel.Infrastructure;
using Sentinel.Revit;
using Sentinel.UI;

namespace Sentinel
{
    /// <summary>Opens the modeless Sentinel window (single instance, bound to the active document).</summary>
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        private static SentinelWindow _window;
        private static RevitSentinelHost _host;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var uidoc = uiApp.ActiveUIDocument;
                if (uidoc == null)
                {
                    message = "Open a project first.";
                    return Result.Cancelled;
                }
                var doc = uidoc.Document;
                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Sentinel", "Sentinel works in project documents, not in the family editor.");
                    return Result.Cancelled;
                }

                if (_window != null)
                {
                    if (_host != null && _host.Document.Equals(doc))
                    {
                        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                        _window.Activate();
                        return Result.Succeeded;
                    }
                    TaskDialog.Show("Sentinel", "Sentinel is open for \"" + _host?.DocumentTitle + "\". Close that window to work on \"" + doc.Title + "\".");
                    _window.Activate();
                    return Result.Succeeded;
                }

                // ExternalEvent creation requires this API context.
                _host = new RevitSentinelHost(uiApp, doc);
                _window = new SentinelWindow(_host);
                _host.AttachDispatcher(_window.Dispatcher);
                new WindowInteropHelper(_window) { Owner = uiApp.MainWindowHandle };
                _window.Closed += (s, e) =>
                {
                    try { _host?.Shutdown(); } catch (Exception ex) { SentinelLog.Error("Host shutdown failed", ex); }
                    _host = null;
                    _window = null;
                };
                _window.Show(); // modeless
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                SentinelLog.Error("Opening Sentinel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        internal static void CloseWindow()
        {
            try { _window?.Close(); } catch { }
        }
    }
}
