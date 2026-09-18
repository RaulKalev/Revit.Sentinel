using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Sentinel.UI
{
    public partial class TitleBar : UserControl
    {
        private Point _mouseDownPoint;
        private bool _isDraggingFromMaximized = false;

        public static readonly DependencyProperty TitleProperty =
    DependencyProperty.Register(
        "Title",
        typeof(string),
        typeof(TitleBar),
        new PropertyMetadata("Sentinel")); // Default value

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public TitleBar()
        {
            InitializeComponent();
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window == null || e.ChangedButton != MouseButton.Left)
                return;

            if (e.ClickCount == 2)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            _mouseDownPoint = e.GetPosition(this);
            _isDraggingFromMaximized = window.WindowState == WindowState.Maximized;

            // Capture mouse so we can handle MouseMove later
            Mouse.Capture(this);
        }
        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured)
                return;

            var window = Window.GetWindow(this);
            if (window == null) return;

            Point currentPoint = e.GetPosition(this);
            Vector delta = currentPoint - _mouseDownPoint;

            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                ReleaseMouseCapture();

                if (_isDraggingFromMaximized)
                {
                    var percentHorizontal = _mouseDownPoint.X / ActualWidth;
                    var screenMousePosition = System.Windows.Forms.Control.MousePosition;

                    double restoredWidth = window.RestoreBounds.Width;
                    window.WindowState = WindowState.Normal;

                    window.Left = screenMousePosition.X - (restoredWidth * percentHorizontal);
                    window.Top = screenMousePosition.Y - 10;
                }

                try
                {
                    window.DragMove();
                }
                catch { /* ignore exceptions during drag */ }
            }
        }
        private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsMouseCaptured)
            {
                Mouse.Capture(null); // Release mouse capture
            }
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
        private void ToggleFeatureButton_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void ToggleFeatureButton_Unchecked(object sender, RoutedEventArgs e)
        {

        }

    }
}
