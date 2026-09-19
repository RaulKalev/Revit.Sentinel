using System.Windows;

namespace Sentinel.UI
{
    /// <summary>
    /// <c>ui:Emphasis.IsPrimary="{Binding …}"</c> switches a button between Button.Primary and Button.Secondary, so the
    /// action bar can highlight the next step for the current selection without duplicating buttons.
    /// </summary>
    public static class Emphasis
    {
        public static readonly DependencyProperty IsPrimaryProperty = DependencyProperty.RegisterAttached(
            "IsPrimary", typeof(bool), typeof(Emphasis), new PropertyMetadata(false, OnChanged));

        public static bool GetIsPrimary(DependencyObject d) => (bool)d.GetValue(IsPrimaryProperty);
        public static void SetIsPrimary(DependencyObject d, bool value) => d.SetValue(IsPrimaryProperty, value);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var el = d as FrameworkElement;
            if (el == null) return;
            var style = el.TryFindResource((bool)e.NewValue ? "Button.Primary" : "Button.Secondary") as Style;
            if (style != null) el.Style = style;
            else el.Loaded += Apply; // resources not reachable before the element is in the tree
        }

        private static void Apply(object sender, RoutedEventArgs e)
        {
            var el = (FrameworkElement)sender;
            el.Loaded -= Apply;
            var style = el.TryFindResource(GetIsPrimary(el) ? "Button.Primary" : "Button.Secondary") as Style;
            if (style != null) el.Style = style;
        }
    }
}
