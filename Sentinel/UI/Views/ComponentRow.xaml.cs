using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Sentinel.UI.ViewModels;

namespace Sentinel.UI.Views
{
    /// <summary>A component of the reviewed door: summary, badges, state, and the inline per-door editor.</summary>
    public partial class ComponentRow : UserControl
    {
        /// <summary>"Override" badge: overridden template components (added ones show "Added" instead).</summary>
        public static readonly IMultiValueConverter OverrideBadge = new OverrideBadgeConverter();

        private InspectorComponentViewModel _vm;

        public ComponentRow()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm = DataContext as InspectorComponentViewModel;
                if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
            };
            Unloaded += (s, e) => { if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged; };
            Loaded += (s, e) => { if (_vm != null) { _vm.PropertyChanged -= OnVmPropertyChanged; _vm.PropertyChanged += OnVmPropertyChanged; } };
        }

        // Opening "Adjust" scrolls the editor into view. Only a user-initiated open does this (rows built while an
        // edit is resumed after a refresh keep the current scroll position).
        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(InspectorComponentViewModel.IsEditing) || _vm == null || !_vm.IsEditing) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_vm != null && _vm.IsEditing && EditorPanel.IsVisible) EditorPanel.BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        private sealed class OverrideBadgeConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var overridden = values.Length > 0 && values[0] is bool && (bool)values[0];
                var added = values.Length > 1 && values[1] is bool && (bool)values[1];
                return overridden && !added ? Visibility.Visible : Visibility.Collapsed;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
                throw new NotSupportedException();
        }
    }
}
