using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sentinel.UiHarness
{
    internal static class WorkspaceProbe
    {
        public static double WorkspaceOffset(DependencyObject root)
        {
            var sv = Find(root, "Scroll") as ScrollViewer;
            return sv == null ? -1 : sv.VerticalOffset;
        }

        /// <summary>A visible combo box by its accessible name (e.g. "Door set"), to drive it like a user would.</summary>
        public static ComboBox Combo(DependencyObject root, string automationName)
        {
            if (root is ComboBox cb && AutomationProperties.GetName(cb) == automationName && cb.IsVisible) return cb;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var r = Combo(VisualTreeHelper.GetChild(root, i), automationName);
                if (r != null) return r;
            }
            return null;
        }

        private static DependencyObject Find(DependencyObject d, string name)
        {
            if (d is FrameworkElement fe && fe.Name == name) return d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var r = Find(VisualTreeHelper.GetChild(d, i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
