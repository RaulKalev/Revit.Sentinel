using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI.Views
{
    /// <summary>
    /// Review workspace. The scroll position is kept while the same door refreshes (edits, validation, expanding a
    /// component) and only returns to the top – with a short reveal – when a different door is shown.
    /// </summary>
    public partial class DoorWorkspace : UserControl
    {
        private DoorInspectorViewModel _inspector;
        private string _shownKey;

        public DoorWorkspace()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_inspector != null) _inspector.PropertyChanged -= OnInspectorChanged;
                _inspector = (DataContext as DoorsViewModel)?.Inspector;
                if (_inspector != null) _inspector.PropertyChanged += OnInspectorChanged;
            };
            // Short windows: keep the sticky header compact so the adjustments below stay usable.
            SizeChanged += (s, e) => IssuesScroll.MaxHeight = ActualHeight < 460 ? 52 : 104;
        }

        private void OnInspectorChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(DoorInspectorViewModel.CurrentKey)) return;
            var key = _inspector.CurrentKey;
            if (key == _shownKey) return;
            _shownKey = key;
            Scroll.ScrollToTop();
            if (key != null && IsVisible) Motion.Play(ScrollContent, 4);
        }

        private void DoorMoreButton_Click(object sender, RoutedEventArgs e)
        {
            DoorMoreMenu.PlacementTarget = DoorMoreButton;
            DoorMoreMenu.Placement = PlacementMode.Bottom;
            DoorMoreMenu.DataContext = _inspector;
            DoorMoreMenu.IsOpen = true;
        }
    }
}
