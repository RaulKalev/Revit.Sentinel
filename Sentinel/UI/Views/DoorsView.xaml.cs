using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI.Views
{
    public partial class DoorsView : UserControl
    {
        private DoorsViewModel _vm;
        private bool _restoring;
        private double _workspaceWidth = 380;

        public DoorsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null)
                {
                    _vm.RestoreSelectionRequested -= OnRestoreSelection;
                    _vm.PropertyChanged -= OnVmPropertyChanged;
                }
                _vm = DataContext as DoorsViewModel;
                if (_vm != null)
                {
                    _vm.RestoreSelectionRequested += OnRestoreSelection;
                    _vm.PropertyChanged += OnVmPropertyChanged;
                    UpdateWorkspaceVisibility();
                }
            };
            SizeChanged += (s, e) => UpdateDensity();
        }

        /// <summary>Width of the review panel (persisted per user by the window).</summary>
        public double WorkspaceWidth
        {
            get => WorkspaceColumn.ActualWidth > 0 ? WorkspaceColumn.ActualWidth : _workspaceWidth;
            set
            {
                _workspaceWidth = Math.Max(WorkspaceColumn.MinWidth, Math.Min(WorkspaceColumn.MaxWidth, value));
                UpdateWorkspaceVisibility();
            }
        }

        public void FocusSearch()
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(DoorsViewModel.IsInspectorVisible))
                UpdateWorkspaceVisibility();
        }

        // ------------------------------------------------------------------ layout

        private void UpdateWorkspaceVisibility()
        {
            var visible = _vm == null || _vm.IsInspectorVisible;
            Workspace.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            WorkspaceSplitter.Visibility = Workspace.Visibility;
            SplitterColumn.Width = new GridLength(visible ? 12 : 0);
            WorkspaceColumn.MinWidth = visible ? 300 : 0;
            WorkspaceColumn.Width = new GridLength(visible ? EffectiveWorkspaceWidth() : 0);
        }

        /// <summary>The chosen panel width, reduced on small windows so the list keeps most of the space.</summary>
        private double EffectiveWorkspaceWidth()
        {
            var w = ActualWidth;
            return w > 0 && w < 1100 ? Math.Min(_workspaceWidth, Math.Max(300, w * 0.38)) : _workspaceWidth;
        }

        /// <summary>Keeps identity, status, access and reason readable as the window gets smaller.</summary>
        private void UpdateDensity()
        {
            var w = ActualWidth;
            SearchHost.Width = w < 1060 ? 170 : 220;
            AssignCombo.Width = w < 1000 ? 150 : 210;
            // At small sizes the list keeps at least ~60 % of the width; the panel returns to its chosen width later.
            if (_vm == null || _vm.IsInspectorVisible)
                WorkspaceColumn.Width = new GridLength(EffectiveWorkspaceWidth());
        }

        private void DoorGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var w = DoorGrid.ActualWidth;
            // Lower-priority columns go first; status collapses to its icon (the word stays in the tooltip and panel).
            ReviewColumn.Visibility = w < 900 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            PartsColumn.Visibility = w < 780 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            LevelColumn.Visibility = w < 680 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            var status = w < 560 ? 44 : 160;
            StatusColumn.MinWidth = status;
            StatusColumn.Width = new DataGridLength(status);
            StatusColumn.Header = status < 60 ? null : "Status";
        }

        private void WorkspaceSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (WorkspaceColumn.ActualWidth > 0) _workspaceWidth = WorkspaceColumn.ActualWidth;
        }

        // ------------------------------------------------------------------ selection

        // The DataGrid's multi-selection cannot be bound; it is pushed to the view model here.
        private void DoorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoring) return;
            _vm?.SetSelection(DoorGrid.SelectedItems.Cast<DoorRowViewModel>());
        }

        private void DoorGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only rows zoom (not headers or the scrollbar).
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is DataGridRow)) dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            var row = (dep as DataGridRow)?.Item as DoorRowViewModel;
            if (row != null) _vm?.Zoom(row);
        }

        private void OnRestoreSelection(object sender, List<string> keys)
        {
            var set = new HashSet<string>(keys ?? new List<string>());
            _restoring = true;
            try
            {
                DoorGrid.SelectedItems.Clear();
                foreach (var item in DoorGrid.Items.OfType<DoorRowViewModel>().Where(r => r.Key != null && set.Contains(r.Key)))
                    DoorGrid.SelectedItems.Add(item);
            }
            finally
            {
                _restoring = false;
            }
            _vm?.SetSelection(DoorGrid.SelectedItems.Cast<DoorRowViewModel>());
            if (DoorGrid.SelectedItem != null) DoorGrid.ScrollIntoView(DoorGrid.SelectedItem);
        }

        // ------------------------------------------------------------------ toolbar

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void PlaceOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            PlaceOptionsMenu.PlacementTarget = PlaceOptionsButton;
            PlaceOptionsMenu.Placement = PlacementMode.Top;
            PlaceOptionsMenu.DataContext = DataContext;
            PlaceOptionsMenu.IsOpen = true;
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            MoreMenu.PlacementTarget = MoreButton;
            MoreMenu.Placement = PlacementMode.Top;
            MoreMenu.DataContext = DataContext;
            MoreMenu.IsOpen = true;
        }
    }
}
