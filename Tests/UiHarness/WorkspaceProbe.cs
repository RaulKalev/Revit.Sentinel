using System.Windows;
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
