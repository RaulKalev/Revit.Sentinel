using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI.Views
{
    public partial class DoorsView : UserControl
    {
        private DoorsViewModel _vm;
        private bool _restoring;

        public DoorsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null) _vm.RestoreSelectionRequested -= OnRestoreSelection;
                _vm = DataContext as DoorsViewModel;
                if (_vm != null) _vm.RestoreSelectionRequested += OnRestoreSelection;
            };
        }

        // The DataGrid's multi-selection cannot be bound; it is pushed to the view model here.
        private void DoorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoring) return;
            _vm?.SetSelection(DoorGrid.SelectedItems.Cast<DoorRowViewModel>());
        }

        private void DoorGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = DoorGrid.SelectedItem as DoorRowViewModel;
            if (row != null) _vm?.Zoom(row);
        }

        private void OnRestoreSelection(object sender, List<string> keys)
        {
            if (keys == null || keys.Count == 0) return;
            var set = new HashSet<string>(keys);
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
    }
}
