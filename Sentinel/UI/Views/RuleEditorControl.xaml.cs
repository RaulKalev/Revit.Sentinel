using System.Windows;
using System.Windows.Controls;

namespace Sentinel.UI.Views
{
    /// <summary>Editor for a placement rule (DataContext: PlacementRuleEditor).</summary>
    public partial class RuleEditorControl : UserControl
    {
        public static readonly DependencyProperty ShowDuplicateOptionProperty = DependencyProperty.Register(
            nameof(ShowDuplicateOption), typeof(bool), typeof(RuleEditorControl), new PropertyMetadata(true));

        public bool ShowDuplicateOption
        {
            get => (bool)GetValue(ShowDuplicateOptionProperty);
            set => SetValue(ShowDuplicateOptionProperty, value);
        }

        public RuleEditorControl()
        {
            InitializeComponent();
        }
    }
}
