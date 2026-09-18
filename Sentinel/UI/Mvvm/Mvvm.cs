using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Sentinel.UI.Mvvm
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected void OnPropertiesChanged(params string[] names)
        {
            foreach (var n in names) OnPropertyChanged(n);
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        /// <summary>Raise for all properties (empty name = WPF refreshes every binding).</summary>
        public void RefreshAll() => OnPropertyChanged(string.Empty);
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        {
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter)
        {
            if (CanExecute(parameter)) _execute(parameter);
        }

        public static void Requery() => CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Value + display text pair for enum/choice combo boxes.</summary>
    public class Option<T>
    {
        public T Value { get; set; }
        public string Text { get; set; }

        public Option(T value, string text)
        {
            Value = value;
            Text = text;
        }

        public override string ToString() => Text;
    }

    /// <summary>Invariant-culture-tolerant number parsing for text inputs (accepts "," as decimal separator).</summary>
    public static class NumberText
    {
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Format(double? value) => value.HasValue ? Format(value.Value) : "";
    }

    /// <summary>
    /// Brings an ObservableCollection to a desired content/order with Insert/Move/Remove only. Unlike Clear()+Add,
    /// items that stay keep their identity, so a bound Selector does not push SelectedItem = null into the view model.
    /// </summary>
    public static class CollectionSync
    {
        public static void Sync<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IList<T> desired)
        {
            var keep = new HashSet<T>(desired);
            for (int i = target.Count - 1; i >= 0; i--)
                if (!keep.Contains(target[i])) target.RemoveAt(i);

            for (int i = 0; i < desired.Count; i++)
            {
                var item = desired[i];
                var current = target.IndexOf(item);
                if (current < 0) target.Insert(i, item);
                else if (current != i) target.Move(current, i);
            }
        }
    }

    public static class Choices
    {
        public static List<Option<T>> Of<T>(params Tuple<T, string>[] items) =>
            items.Select(i => new Option<T>(i.Item1, i.Item2)).ToList();
    }
}
